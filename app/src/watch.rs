//! Keeps the index current: watches the drives and applies changes in batches.

use crate::index::{fold, Entry, Index, ATTR_DIRECTORY, NO_SIZE};
use crate::scan::{meta_times, read_one, Counter, Local, Progress};
use notify::{RecursiveMode, Watcher};
use std::collections::{HashMap, HashSet};
use std::path::PathBuf;
use std::sync::atomic::AtomicU32;
use std::sync::mpsc::{channel, RecvTimeoutError};
use std::sync::{Arc, RwLock};
use std::time::Duration;

/// Start watching `roots`. Runs for the life of the program.
pub fn start(index: Arc<RwLock<Index>>, roots: Vec<String>, ctx: eframe::egui::Context) {
    std::thread::spawn(move || {
        let (tx, rx) = channel::<PathBuf>();
        let mut watcher = match notify::recommended_watcher(move |res: notify::Result<notify::Event>| {
            if let Ok(ev) = res {
                for p in ev.paths {
                    let _ = tx.send(p);
                }
            }
        }) {
            Ok(w) => w,
            Err(_) => return,
        };
        for r in &roots {
            let _ = watcher.watch(&PathBuf::from(format!("{r}\\")), RecursiveMode::Recursive);
        }
        let cache_dir = crate::index::cache_path().parent().map(|p| fold(&p.to_string_lossy())).unwrap_or_default();
        let mut lookup: Option<HashMap<Box<str>, u32>> = None;
        let mut lookup_epoch = u64::MAX;
        loop {
            let first = match rx.recv() {
                Ok(p) => p,
                Err(_) => return,
            };
            let mut batch: HashSet<String> = HashSet::new();
            batch.insert(first.to_string_lossy().into_owned());
            // Gather changes for a moment so bursts are applied together.
            loop {
                match rx.recv_timeout(Duration::from_millis(400)) {
                    Ok(p) => {
                        batch.insert(p.to_string_lossy().into_owned());
                        if batch.len() > 50_000 {
                            break;
                        }
                    }
                    Err(RecvTimeoutError::Timeout) => break,
                    Err(RecvTimeoutError::Disconnected) => return,
                }
            }
            batch.retain(|p| !fold(p).starts_with(&cache_dir) || cache_dir.is_empty());
            if batch.is_empty() {
                continue;
            }
            let mut idx = index.write().unwrap();
            if !idx.ready {
                continue;
            }
            if lookup_epoch != idx.epoch {
                lookup = None;
                lookup_epoch = idx.epoch;
            }
            let lk = lookup.get_or_insert_with(|| build_lookup(&idx));
            apply(&mut idx, lk, batch);
            idx.generation += 1;
            drop(idx);
            ctx.request_repaint();
        }
    });
}

fn build_lookup(idx: &Index) -> HashMap<Box<str>, u32> {
    idx.dirs
        .iter()
        .enumerate()
        .filter(|(_, d)| !d.fold.is_empty())
        .map(|(i, d)| (d.fold.clone(), i as u32))
        .collect()
}

fn split(path: &str) -> Option<(&str, &str)> {
    let p = path.trim_end_matches('\\');
    let i = p.rfind('\\')?;
    Some((&p[..i], &p[i + 1..]))
}

fn apply(idx: &mut Index, lookup: &mut HashMap<Box<str>, u32>, batch: HashSet<String>) {
    // Parents first, so new folders exist before their contents.
    let mut paths: Vec<String> = batch.into_iter().collect();
    paths.sort_by_key(|p| p.len());
    let mut targets: HashMap<(u32, String), String> = HashMap::new();
    for p in &paths {
        let Some((parent, name)) = split(p) else { continue };
        if let Some(&pid) = lookup.get(fold(parent).as_str()) {
            targets.insert((pid, fold(name)), p.clone());
        }
    }
    if targets.is_empty() {
        return;
    }
    let parents: HashSet<u32> = targets.keys().map(|k| k.0).collect();
    let mut found: HashMap<(u32, String), usize> = HashMap::new();
    for (i, e) in idx.entries.iter().enumerate() {
        if !e.deleted && parents.contains(&e.dir) {
            let key = (e.dir, idx.fold(e).to_string());
            if targets.contains_key(&key) {
                found.insert(key, i);
            }
        }
    }
    let mut keys: Vec<(u32, String)> = targets.keys().cloned().collect();
    keys.sort_by_key(|k| targets[k].len());
    for key in keys {
        let path = &targets[&key];
        let existing = found.get(&key).copied();
        match std::fs::symlink_metadata(path) {
            Ok(md) => {
                let (attrs, mtime, ctime) = meta_times(&md);
                let is_dir = md.is_dir();
                if let Some(i) = existing {
                    let e = &mut idx.entries[i];
                    e.size = if is_dir { NO_SIZE } else { md.len() };
                    e.mtime = mtime;
                    e.ctime = ctime;
                    e.attrs = attrs | if is_dir { ATTR_DIRECTORY } else { 0 };
                    if let Some((_, name)) = split(path) {
                        if idx.name(&idx.entries[i]) != name {
                            idx.rename(i, name);
                        }
                    }
                } else {
                    add_new(idx, lookup, key.0, path, is_dir, md.len(), attrs, mtime, ctime);
                }
            }
            Err(_) => {
                if let Some(i) = existing {
                    remove(idx, lookup, i);
                }
            }
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn add_new(idx: &mut Index, lookup: &mut HashMap<Box<str>, u32>, pid: u32, path: &str, is_dir: bool, size: u64, attrs: u32, mtime: u64, ctime: u64) {
    let Some((_, name)) = split(path) else { return };
    let mut self_dir = crate::index::NONE;
    if is_dir {
        self_dir = idx.dirs.len() as u32;
        idx.set_dir(self_dir, path.to_string());
        lookup.insert(fold(path).into_boxed_str(), self_dir);
    }
    let size = if is_dir { NO_SIZE } else { size };
    let attrs = attrs | if is_dir { ATTR_DIRECTORY } else { 0 };
    let e = Entry::new(&mut idx.names, name, pid, self_dir, size, mtime, ctime, attrs);
    idx.entries.push(e);
    bump_children(idx, pid, 1);
    if is_dir {
        let entry = idx.entries.len() as u32 - 1;
        idx.dirs[self_dir as usize].entry = entry;
        // A folder moved in from elsewhere arrives with its contents.
        let counter = Counter(AtomicU32::new(idx.dirs.len() as u32));
        let progress = Progress::default();
        let mut stack = vec![(path.to_string(), self_dir)];
        let mut local = Local::default();
        while let Some((p, id)) = stack.pop() {
            stack.extend(read_one(&p, id, &mut local, &counter, &progress));
            local.dirs.push((id, p));
            if local.entries.len() > 200_000 {
                break;
            }
        }
        local.dirs.retain(|(id, _)| *id != self_dir);
        for (id, p) in &local.dirs {
            lookup.insert(fold(p).into_boxed_str(), *id);
        }
        let start = idx.entries.len();
        local.move_into(idx);
        for i in start..idx.entries.len() {
            let sd = idx.entries[i].self_dir;
            if sd != crate::index::NONE && (sd as usize) < idx.dirs.len() {
                idx.dirs[sd as usize].entry = i as u32;
            }
        }
        for i in start..idx.entries.len() {
            let d = idx.entries[i].dir;
            bump_children(idx, d, 1);
        }
    }
}

fn remove(idx: &mut Index, lookup: &mut HashMap<Box<str>, u32>, i: usize) {
    idx.entries[i].deleted = true;
    let pid = idx.entries[i].dir;
    bump_children(idx, pid, -1);
    let sd = idx.entries[i].self_dir;
    if sd == crate::index::NONE {
        return;
    }
    // Everything below a removed folder goes too.
    let prefix = format!("{}\\", idx.dirs[sd as usize].fold);
    let mut dead: HashSet<u32> = HashSet::new();
    dead.insert(sd);
    for (d, info) in idx.dirs.iter().enumerate() {
        if info.fold.starts_with(&prefix) {
            dead.insert(d as u32);
        }
    }
    for d in &dead {
        let key = idx.dirs[*d as usize].fold.clone();
        lookup.remove(&key);
    }
    for e in idx.entries.iter_mut() {
        if dead.contains(&e.dir) {
            e.deleted = true;
        }
    }
}

fn bump_children(idx: &mut Index, dir: u32, delta: i32) {
    if dir == crate::index::NONE {
        return;
    }
    let ent = idx.dirs.get(dir as usize).map(|d| d.entry).unwrap_or(crate::index::NONE);
    if let Some(e) = idx.entries.get_mut(ent as usize) {
        e.children = (e.children as i64 + delta as i64).max(0) as u32;
    }
}
