//! Walks the drives with several threads and fills an index as it goes.

use crate::index::{Entry, Index, ATTR_DIRECTORY, ATTR_REPARSE, NONE, NO_SIZE};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, RwLock};
use std::time::Duration;

#[derive(Default)]
pub struct Progress {
    pub running: AtomicBool,
    pub found: AtomicU64,
    pub current: Mutex<String>,
}

struct Shared {
    queue: Mutex<Vec<(String, u32)>>,
    pending: AtomicUsize,
    next_dir: AtomicU32,
}

const FLUSH_AT: usize = 20_000;

/// What the scanner leaves out (set from the Options window).
#[derive(Default, Clone)]
pub struct Rules {
    /// Lower-case folder/file names, or full paths when they contain a backslash.
    pub exclude: Vec<String>,
    pub hide_hidden: bool,
    pub hide_system: bool,
    pub drives_off: Vec<String>,
    /// Also index USB (removable) and network drives.
    pub include_extra: bool,
    pub junk: bool,
}

static RULES: RwLock<Rules> = RwLock::new(Rules { exclude: Vec::new(), hide_hidden: false, hide_system: false, drives_off: Vec::new(), include_extra: false, junk: false });

pub fn set_rules(r: Rules) {
    *RULES.write().unwrap() = r;
}

/// The drives to index: fixed drives minus the ones switched off.
pub fn active_roots() -> Vec<String> {
    let rules = RULES.read().unwrap();
    fixed_drives().into_iter().filter(|d| !rules.drives_off.iter().any(|x| x.eq_ignore_ascii_case(d))).collect()
}

fn excluded(list: &[String], dir: &str, name: &str) -> bool {
    let lname = name.to_lowercase();
    list.iter().any(|x| {
        if x.contains('\\') {
            crate::index::join(dir, name).to_lowercase() == *x
        } else {
            *x == lname
        }
    })
}

/// Fixed drives such as "C:".
pub fn fixed_drives() -> Vec<String> {
    let extra = RULES.read().unwrap().include_extra;
    #[cfg(windows)]
    {
        use windows_sys::Win32::Storage::FileSystem::{GetDriveTypeW, GetLogicalDrives};
        let mut out = Vec::new();
        let mask = unsafe { GetLogicalDrives() };
        for i in 0..26u32 {
            if mask & (1 << i) == 0 {
                continue;
            }
            let letter = (b'A' + i as u8) as char;
            let root: Vec<u16> = format!("{letter}:\\").encode_utf16().chain([0]).collect();
            // DRIVE_REMOVABLE = 2, DRIVE_FIXED = 3, DRIVE_REMOTE = 4
            let kind = unsafe { GetDriveTypeW(root.as_ptr()) };
            if kind == 3 || (extra && (kind == 2 || kind == 4)) {
                out.push(format!("{letter}:"));
            }
        }
        out
    }
    #[cfg(not(windows))]
    {
        vec!["/".to_string()]
    }
}

/// Scan `roots` into `target` (which should start empty). Entries appear in the
/// index while the scan runs, so searching works before it finishes.
pub fn scan_into(target: &Arc<RwLock<Index>>, roots: &[String], progress: &Progress) {
    progress.running.store(true, Ordering::SeqCst);
    progress.found.store(0, Ordering::SeqCst);
    let shared = Shared { queue: Mutex::new(Vec::new()), pending: AtomicUsize::new(0), next_dir: AtomicU32::new(0) };
    {
        let mut idx = target.write().unwrap();
        let mut q = shared.queue.lock().unwrap();
        for r in roots {
            let id = shared.next_dir.fetch_add(1, Ordering::SeqCst);
            let e = Entry::new(&mut idx.names, r, NONE, id, NO_SIZE, 0, 0, ATTR_DIRECTORY);
            idx.entries.push(e);
            q.push((r.clone(), id));
            shared.pending.fetch_add(1, Ordering::SeqCst);
        }
        idx.generation += 1;
    }
    let threads = std::thread::available_parallelism().map(|n| n.get()).unwrap_or(4).clamp(2, 8);
    std::thread::scope(|s| {
        for _ in 0..threads {
            s.spawn(|| worker(target, &shared, progress));
        }
    });
    {
        let mut idx = target.write().unwrap();
        idx.entries.shrink_to_fit();
        idx.names.shrink_to_fit();
        idx.dirs.shrink_to_fit();
        idx.finalize();
        idx.ready = true;
    }
    progress.running.store(false, Ordering::SeqCst);
}

#[derive(Default)]
pub(crate) struct Local {
    pub entries: Vec<Entry>,
    /// Names of `entries`, moved into the index's name block on flush.
    pub names: String,
    pub dirs: Vec<(u32, String)>,
}

impl Local {
    /// Move everything into `idx` (fixing name offsets on the way).
    pub fn move_into(&mut self, idx: &mut Index) {
        for (id, path) in self.dirs.drain(..) {
            idx.set_dir(id, path);
        }
        let base = idx.names.len() as u32;
        idx.names.push_str(&self.names);
        self.names.clear();
        for e in self.entries.iter_mut() {
            e.rebase(base);
        }
        idx.entries.append(&mut self.entries);
    }
}

pub(crate) struct Counter(pub AtomicU32);
impl DirIds for Counter {
    fn new_dir_id(&self) -> u32 {
        self.0.fetch_add(1, Ordering::SeqCst)
    }
}

fn flush(target: &Arc<RwLock<Index>>, local: &mut Local) {
    if local.entries.is_empty() && local.dirs.is_empty() {
        return;
    }
    let mut idx = target.write().unwrap();
    local.move_into(&mut idx);
    idx.generation += 1;
}

fn worker(target: &Arc<RwLock<Index>>, shared: &Shared, progress: &Progress) {
    let mut local = Local::default();
    loop {
        let item = shared.queue.lock().unwrap().pop();
        let Some((path, id)) = item else {
            if shared.pending.load(Ordering::SeqCst) == 0 {
                break;
            }
            // Others are still reading folders; flush so results show up.
            flush(target, &mut local);
            std::thread::sleep(Duration::from_millis(2));
            continue;
        };
        local.dirs.push((id, path.clone()));
        let subdirs = read_one(&path, id, &mut local, shared, progress);
        if !subdirs.is_empty() {
            shared.pending.fetch_add(subdirs.len(), Ordering::SeqCst);
            shared.queue.lock().unwrap().extend(subdirs);
        }
        shared.pending.fetch_sub(1, Ordering::SeqCst);
        if local.entries.len() >= FLUSH_AT {
            if let Ok(mut c) = progress.current.try_lock() {
                *c = path;
            }
            flush(target, &mut local);
        }
    }
    flush(target, &mut local);
}

/// Read one folder; returns the subfolders to visit next.
pub(crate) fn read_one(path: &str, id: u32, local: &mut Local, shared: &impl DirIds, progress: &Progress) -> Vec<(String, u32)> {
    let mut subdirs = Vec::new();
    let os_path: PathBuf = if path.ends_with(':') { PathBuf::from(format!("{path}\\")) } else { PathBuf::from(path) };
    let Ok(rd) = std::fs::read_dir(&os_path) else { return subdirs };
    let rules = RULES.read().unwrap();
    for de in rd.flatten() {
        let name = de.file_name().to_string_lossy().into_owned();
        let Ok(md) = de.metadata() else { continue };
        let (attrs, mtime, ctime) = meta_times(&md);
        if (rules.hide_hidden && attrs & 0x2 != 0) || (rules.hide_system && attrs & 0x4 != 0) {
            continue;
        }
        if !rules.exclude.is_empty() && excluded(&rules.exclude, path, &name) {
            continue;
        }
        if rules.junk && is_junk(path, &name, md.is_dir()) {
            continue;
        }
        let reparse = attrs & ATTR_REPARSE != 0 || md.file_type().is_symlink();
        let is_dir = md.is_dir() || attrs & ATTR_DIRECTORY != 0;
        let mut self_dir = NONE;
        if is_dir {
            self_dir = shared.new_dir_id();
            let child = crate::index::join(path, &name);
            if reparse {
                // Listed, but not followed (avoids loops through junctions).
                local.dirs.push((self_dir, child));
            } else {
                subdirs.push((child, self_dir));
            }
        }
        progress.found.fetch_add(1, Ordering::Relaxed);
        let size = if is_dir { NO_SIZE } else { md.len() };
        let attrs = attrs | if is_dir { ATTR_DIRECTORY } else { 0 };
        let e = Entry::new(&mut local.names, &name, id, self_dir, size, mtime, ctime, attrs);
        local.entries.push(e);
    }
    subdirs
}
pub(crate) trait DirIds {
    fn new_dir_id(&self) -> u32;
}
impl DirIds for Shared {
    fn new_dir_id(&self) -> u32 {
        self.next_dir.fetch_add(1, Ordering::SeqCst)
    }
}

#[cfg(windows)]
pub fn meta_times(md: &std::fs::Metadata) -> (u32, u64, u64) {
    use std::os::windows::fs::MetadataExt;
    (md.file_attributes(), md.last_write_time(), md.creation_time())
}

#[cfg(not(windows))]
pub fn meta_times(md: &std::fs::Metadata) -> (u32, u64, u64) {
    let to_ft = |t: std::io::Result<std::time::SystemTime>| {
        t.ok()
            .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
            .map(|d| (d.as_secs() + 11_644_473_600) * 10_000_000 + d.subsec_nanos() as u64 / 100)
            .unwrap_or(0)
    };
    (0, to_ft(md.modified()), to_ft(md.created()))
}

const JUNK_DIRS: [&str; 12] = [
    "$recycle.bin", "system volume information", "recovery", "node_modules", ".git", ".svn", ".hg", ".cache",
    "__pycache__", ".nuget", "inetcache", "temporary internet files",
];
const JUNK_FILES: [&str; 3] = ["thumbs.db", "desktop.ini", ".ds_store"];
const JUNK_PATHS: [&str; 20] = [
    r"\windows\winsxs\", r"\windows\installer\", r"\windows\servicing\", r"\windows\softwaredistribution\",
    r"\windows\assembly\", r"\appdata\local\temp\", r"\appdata\local\microsoft\windows\inetcache\",
    r"\appdata\local\microsoft\windows\webcache\", r"\appdata\local\microsoft\windows\notifications\",
    r"\appdata\local\packages\", r"\appdata\local\pip\cache\", r"\appdata\local\npm-cache\",
    r"\appdata\local\yarn\", r"\appdata\local\pnpm-store\", r"\appdata\local\docker\",
    r"\appdata\local\crashdumps\", r"\appdata\roaming\code\cache\", r"\appdata\roaming\code\cacheddata\",
    r"\appdata\roaming\cursor\cache\", r"\appdata\roaming\cursor\cacheddata\",
];

/// Junk filter (same lists as OmniFind): lock files, caches, VCS folders, Recycle Bin ...
fn is_junk(dir: &str, name: &str, is_dir: bool) -> bool {
    if name.starts_with("~$") || name.starts_with("~~") {
        return true;
    }
    let lname = name.to_ascii_lowercase();
    if is_dir {
        if JUNK_DIRS.contains(&lname.as_str()) {
            return true;
        }
        let full = format!("{}\\{}\\", dir.to_ascii_lowercase(), lname);
        return JUNK_PATHS.iter().any(|p| full.contains(p));
    }
    JUNK_FILES.contains(&lname.as_str())
}
