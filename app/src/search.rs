//! Background search: filters and sorts the index without blocking the window.

use crate::index::{Entry, Index};
use crate::query::{compile, Options};
use rayon::prelude::*;
use std::cmp::Ordering;
use std::collections::HashMap;
use std::sync::mpsc::{Receiver, Sender};
use std::sync::{Arc, RwLock};
use std::time::Instant;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum SortKey {
    Name,
    Path,
    Size,
    Extension,
    Modified,
    Created,
    Attributes,
}

#[derive(Clone, PartialEq, Debug)]
pub struct Request {
    pub text: String,
    pub opts: Options,
    pub sort: SortKey,
    pub ascending: bool,
    pub generation: u64,
    pub epoch: u64,
    pub serial: u64,
    /// Only entries under this drive or folder (lower-case, e.g. "c:").
    pub root: Option<String>,
}

pub struct Reply {
    pub serial: u64,
    pub epoch: u64,
    pub ids: Vec<u32>,
    pub error: Option<String>,
    pub millis: u128,
}

/// Items per drive letter (a..z) and in total, refreshed whenever the index changes.
pub static DRIVE_COUNTS: std::sync::Mutex<[u64; 26]> = std::sync::Mutex::new([0; 26]);
pub static INDEXED: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

fn under_root(root: Option<&str>, idx: &Index, e: &Entry) -> bool {
    let Some(r) = root else { return true };
    if e.dir == crate::index::NONE {
        return idx.fold(e) == r;
    }
    let d = idx.dir_fold(e);
    d == r || (d.starts_with(r) && d.as_bytes().get(r.len()) == Some(&b'\\'))
}

/// Serial of the newest search the window asked for, and of the one being run.
pub static LATEST: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
static RUNNING: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

/// True when a newer search was requested, so slow work (file contents) can stop early.
pub fn stale() -> bool {
    use std::sync::atomic::Ordering::Relaxed;
    LATEST.load(Relaxed) != RUNNING.load(Relaxed)
}

pub fn spawn(index: Arc<RwLock<Index>>, rx: Receiver<Request>, tx: Sender<Reply>, ctx: eframe::egui::Context) {
    std::thread::spawn(move || {
        let mut name_order: Vec<u32> = Vec::new();
        let mut order_key = (u64::MAX, u64::MAX);
        while let Ok(mut req) = rx.recv() {
            while let Ok(newer) = rx.try_recv() {
                req = newer;
            }
            RUNNING.store(req.serial, std::sync::atomic::Ordering::Relaxed);
            let start = Instant::now();
            let idx = index.read().unwrap();
            let key = (idx.epoch, idx.generation);
            if key != order_key {
                name_order = (0..idx.entries.len() as u32).collect();
                let entries = &idx.entries;
                name_order.par_sort_unstable_by(|a, b| by_name(&idx, &entries[*a as usize], &entries[*b as usize]));
                order_key = key;
                let mut counts = [0u64; 26];
                let mut total = 0u64;
                for e in entries.iter().filter(|e| !e.deleted) {
                    total += 1;
                    let f = if e.dir == crate::index::NONE { idx.fold(e) } else { idx.dir_fold(e) };
                    let b = f.as_bytes();
                    if b.len() >= 2 && b[1] == b':' && b[0].is_ascii_lowercase() {
                        counts[(b[0] - b'a') as usize] += 1;
                    }
                }
                *DRIVE_COUNTS.lock().unwrap() = counts;
                INDEXED.store(total, std::sync::atomic::Ordering::Relaxed);
            }
            let reply = match compile(&req.text, &req.opts) {
                Err(e) => Reply { serial: req.serial, epoch: idx.epoch, ids: Vec::new(), error: Some(e), millis: 0 },
                Ok(q) => {
                    let root = req.root.as_deref();
                    let entries = &idx.entries;
                    let mut ids: Vec<u32> = name_order
                        .par_iter()
                        .copied()
                        .filter(|&i| {
                            let e = &entries[i as usize];
                            !e.deleted && under_root(root, &idx, e) && (q.is_empty || q.matches(&idx, e))
                        })
                        .collect();
                    if q.dupe {
                        let mut counts: HashMap<&str, u32> = HashMap::new();
                        for &i in &ids {
                            *counts.entry(idx.fold(&entries[i as usize])).or_default() += 1;
                        }
                        ids.retain(|&i| counts[idx.fold(&entries[i as usize])] > 1);
                    }
                    sort(&idx, &mut ids, req.sort, req.ascending);
                    if let Some(n) = q.count {
                        ids.truncate(n);
                    }
                    Reply { serial: req.serial, epoch: idx.epoch, ids, error: None, millis: start.elapsed().as_millis() }
                }
            };
            drop(idx);
            if tx.send(reply).is_err() {
                break;
            }
            ctx.request_repaint();
        }
    });
}

/// Run one query the way the window does (sorted by name), for timing.
pub fn time_query(index: &Arc<RwLock<Index>>, text: &str) -> (usize, u128) {
    let idx = index.read().unwrap();
    let start = Instant::now();
    let mut order: Vec<u32> = (0..idx.entries.len() as u32).collect();
    order.par_sort_unstable_by(|a, b| by_name(&idx, &idx.entries[*a as usize], &idx.entries[*b as usize]));
    let sort_ms = start.elapsed().as_millis();
    let start = Instant::now();
    let Ok(q) = compile(text, &Options::default()) else { return (0, 0) };
    let n = order.par_iter().filter(|&&i| q.matches(&idx, &idx.entries[i as usize])).count();
    let _ = sort_ms;
    (n, start.elapsed().as_millis())
}

fn by_name(idx: &Index, a: &Entry, b: &Entry) -> Ordering {
    idx.fold(a).cmp(idx.fold(b)).then_with(|| idx.name(a).cmp(idx.name(b)))
}

fn sort(idx: &Index, ids: &mut [u32], key: SortKey, ascending: bool) {
    let e = |i: &u32| &idx.entries[*i as usize];
    match key {
        SortKey::Name => {}
        SortKey::Path => ids.par_sort_by(|a, b| {
            let (x, y) = (e(a), e(b));
            idx.dir_fold(x).cmp(idx.dir_fold(y)).then_with(|| by_name(idx, x, y))
        }),
        SortKey::Size => ids.par_sort_by(|a, b| {
            let size = |x: &Entry| if x.size == crate::index::NO_SIZE { 0 } else { x.size.saturating_add(1) };
            size(e(a)).cmp(&size(e(b)))
        }),
        SortKey::Extension => ids.par_sort_by(|a, b| idx.extension(e(a)).to_lowercase().cmp(&idx.extension(e(b)).to_lowercase())),
        SortKey::Modified => ids.par_sort_by_key(|a| e(a).mtime),
        SortKey::Created => ids.par_sort_by_key(|a| e(a).ctime),
        SortKey::Attributes => ids.par_sort_by_key(|a| e(a).attrs),
    }
    if !ascending {
        ids.reverse();
    }
}
