//! The in-memory file index: every file and folder on the indexed drives.

use std::io::{BufReader, BufWriter, Read, Write};
use std::path::PathBuf;

pub const NONE: u32 = u32::MAX;
pub const NO_SIZE: u64 = u64::MAX;
pub const ATTR_DIRECTORY: u32 = 0x10;
pub const ATTR_REPARSE: u32 = 0x400;

/// One file or folder. Its name lives in the index's shared name block.
#[derive(Clone)]
pub struct Entry {
    pub name_off: u32,
    pub name_len: u16,
    /// Lower-case name with accents removed (often the same bytes as the name).
    pub fold_len: u16,
    pub fold_off: u32,
    /// Folder (path id) this entry lives in, or NONE for drive roots.
    pub dir: u32,
    /// Path id of this entry when it is a folder.
    pub self_dir: u32,
    pub size: u64,
    pub mtime: u64,
    pub ctime: u64,
    pub attrs: u32,
    pub children: u32,
    pub deleted: bool,
}

impl Entry {
    /// A new entry whose name (and folded name) are appended to `names`.
    #[allow(clippy::too_many_arguments)]
    pub fn new(names: &mut String, name: &str, dir: u32, self_dir: u32, size: u64, mtime: u64, ctime: u64, attrs: u32) -> Entry {
        let name_off = names.len() as u32;
        let name = &name[..name.len().min(u16::MAX as usize)];
        names.push_str(name);
        let folded = fold(name);
        let (fold_off, fold_len) = if folded == name {
            (name_off, name.len())
        } else {
            let off = names.len() as u32;
            names.push_str(&folded);
            (off, folded.len().min(u16::MAX as usize))
        };
        Entry {
            name_off,
            name_len: name.len() as u16,
            fold_off,
            fold_len: fold_len as u16,
            dir,
            self_dir,
            size,
            mtime,
            ctime,
            attrs,
            children: 0,
            deleted: false,
        }
    }
    pub fn is_dir(&self) -> bool {
        self.self_dir != NONE || self.attrs & ATTR_DIRECTORY != 0
    }
    /// Shift name offsets after moving a local name block into the index's.
    pub fn rebase(&mut self, by: u32) {
        self.name_off += by;
        self.fold_off += by;
    }
}

pub fn extension_of(name: &str) -> &str {
    match name.rfind('.') {
        Some(p) if p > 0 => &name[p + 1..],
        _ => "",
    }
}

#[derive(Clone, Default)]
pub struct Dir {
    pub path: Box<str>,
    pub fold: Box<str>,
    pub entry: u32,
}

#[derive(Default)]
pub struct Index {
    pub entries: Vec<Entry>,
    /// Every entry's name (and folded name) back to back.
    pub names: String,
    pub dirs: Vec<Dir>,
    /// Bumped whenever entries change.
    pub generation: u64,
    /// Bumped whenever the whole index is replaced (ids become invalid).
    pub epoch: u64,
    pub ready: bool,
}

impl Index {
    pub fn name(&self, e: &Entry) -> &str {
        let s = e.name_off as usize;
        &self.names[s..s + e.name_len as usize]
    }
    pub fn fold(&self, e: &Entry) -> &str {
        let s = e.fold_off as usize;
        &self.names[s..s + e.fold_len as usize]
    }
    pub fn extension(&self, e: &Entry) -> &str {
        if e.is_dir() { "" } else { extension_of(self.name(e)) }
    }
    /// Give an entry a new name (the old bytes stay unused in the block).
    pub fn rename(&mut self, i: usize, name: &str) {
        let mut tmp = Entry::new(&mut self.names, name, 0, 0, 0, 0, 0, 0);
        let e = &mut self.entries[i];
        std::mem::swap(&mut e.name_off, &mut tmp.name_off);
        std::mem::swap(&mut e.name_len, &mut tmp.name_len);
        std::mem::swap(&mut e.fold_off, &mut tmp.fold_off);
        std::mem::swap(&mut e.fold_len, &mut tmp.fold_len);
    }
    pub fn dir_path(&self, e: &Entry) -> &str {
        if e.dir == NONE {
            return "";
        }
        self.dirs.get(e.dir as usize).map(|d| &*d.path).unwrap_or("")
    }
    pub fn dir_fold(&self, e: &Entry) -> &str {
        if e.dir == NONE {
            return "";
        }
        self.dirs.get(e.dir as usize).map(|d| &*d.fold).unwrap_or("")
    }
    pub fn full_path(&self, e: &Entry) -> String {
        join(self.dir_path(e), self.name(e))
    }
    pub fn set_dir(&mut self, id: u32, path: String) {
        let i = id as usize;
        if self.dirs.len() <= i {
            self.dirs.resize(i + 1, Dir { entry: NONE, ..Default::default() });
        }
        let fold = fold(&path).into_boxed_str();
        self.dirs[i].path = path.into_boxed_str();
        self.dirs[i].fold = fold;
    }
    /// Link folder records to their entries and count children.
    pub fn finalize(&mut self) {
        for d in self.dirs.iter_mut() {
            d.entry = NONE;
        }
        for (i, e) in self.entries.iter().enumerate() {
            if e.self_dir != NONE && (e.self_dir as usize) < self.dirs.len() {
                self.dirs[e.self_dir as usize].entry = i as u32;
            }
        }
        let mut counts = vec![0u32; self.dirs.len()];
        for e in self.entries.iter().filter(|e| !e.deleted) {
            if e.dir != NONE && (e.dir as usize) < counts.len() {
                counts[e.dir as usize] += 1;
            }
        }
        for e in self.entries.iter_mut() {
            if e.self_dir != NONE {
                e.children = counts.get(e.self_dir as usize).copied().unwrap_or(0);
            }
        }
        // Folder totals: files add to their folder, folders to their parent (parents have lower ids).
        let mut sizes = vec![0u64; self.dirs.len()];
        for e in self.entries.iter().filter(|e| !e.deleted && !e.is_dir() && e.size != NO_SIZE) {
            if e.dir != NONE && (e.dir as usize) < sizes.len() {
                sizes[e.dir as usize] += e.size;
            }
        }
        for d in (0..self.dirs.len()).rev() {
            let en = self.dirs[d].entry;
            if en == NONE {
                continue;
            }
            let p = self.entries[en as usize].dir;
            if p != NONE && (p as usize) < d {
                sizes[p as usize] += sizes[d];
            }
        }
        for e in self.entries.iter_mut() {
            if e.self_dir != NONE && (e.self_dir as usize) < sizes.len() {
                e.size = sizes[e.self_dir as usize];
            }
        }
        self.generation += 1;
    }
}

pub fn join(dir: &str, name: &str) -> String {
    if dir.is_empty() {
        name.to_string()
    } else if dir.ends_with('\\') {
        format!("{dir}{name}")
    } else {
        format!("{dir}\\{name}")
    }
}

/// Lower-case and strip common accents ("Café" -> "cafe").
pub fn fold(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    for c in s.chars() {
        if c.is_ascii() {
            out.push(c.to_ascii_lowercase());
        } else {
            for l in c.to_lowercase() {
                out.push(strip_accent(l));
            }
        }
    }
    out
}

/// Strip accents but keep case.
pub fn strip_accents(s: &str) -> String {
    s.chars()
        .map(|c| {
            if c.is_ascii() {
                c
            } else if c.is_uppercase() {
                let l: String = c.to_lowercase().collect();
                let b = l.chars().next().map(strip_accent).unwrap_or(c);
                b.to_uppercase().next().unwrap_or(c)
            } else {
                strip_accent(c)
            }
        })
        .collect()
}

pub fn strip_accent(c: char) -> char {
    match c {
        'à' | 'á' | 'â' | 'ã' | 'ä' | 'å' | 'ā' | 'ă' | 'ą' => 'a',
        'ç' | 'ć' | 'ĉ' | 'ċ' | 'č' => 'c',
        'ď' | 'đ' => 'd',
        'è' | 'é' | 'ê' | 'ë' | 'ē' | 'ĕ' | 'ė' | 'ę' | 'ě' => 'e',
        'ĝ' | 'ğ' | 'ġ' | 'ģ' => 'g',
        'ĥ' | 'ħ' => 'h',
        'ì' | 'í' | 'î' | 'ï' | 'ĩ' | 'ī' | 'ĭ' | 'į' | 'ı' => 'i',
        'ĵ' => 'j',
        'ķ' => 'k',
        'ĺ' | 'ļ' | 'ľ' | 'ŀ' | 'ł' => 'l',
        'ñ' | 'ń' | 'ņ' | 'ň' => 'n',
        'ò' | 'ó' | 'ô' | 'õ' | 'ö' | 'ø' | 'ō' | 'ŏ' | 'ő' => 'o',
        'ŕ' | 'ŗ' | 'ř' => 'r',
        'ś' | 'ŝ' | 'ş' | 'š' => 's',
        'ţ' | 'ť' | 'ŧ' => 't',
        'ù' | 'ú' | 'û' | 'ü' | 'ũ' | 'ū' | 'ŭ' | 'ů' | 'ű' | 'ų' => 'u',
        'ŵ' => 'w',
        'ý' | 'ÿ' | 'ŷ' => 'y',
        'ź' | 'ż' | 'ž' => 'z',
        other => other,
    }
}

// ---------------------------------------------------------------- cache file

const MAGIC: &[u8; 8] = b"QFIX0001";

pub fn cache_path() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(std::env::temp_dir);
    base.join("Quickfind").join("index.bin")
}

pub fn save(index: &Index) -> std::io::Result<()> {
    let path = cache_path();
    if let Some(p) = path.parent() {
        std::fs::create_dir_all(p)?;
    }
    let tmp = path.with_extension("tmp");
    {
        let mut w = BufWriter::with_capacity(1 << 20, std::fs::File::create(&tmp)?);
        w.write_all(MAGIC)?;
        w.write_all(&(index.dirs.len() as u32).to_le_bytes())?;
        for d in &index.dirs {
            write_str(&mut w, &d.path)?;
        }
        // Written in name order, so the next start's name sort finds it already sorted.
        let mut live: Vec<&Entry> = index.entries.iter().filter(|e| !e.deleted).collect();
        {
            use rayon::prelude::*;
            live.par_sort_unstable_by(|a, b| index.fold(a).cmp(index.fold(b)).then_with(|| index.name(a).cmp(index.name(b))));
        }
        w.write_all(&(live.len() as u32).to_le_bytes())?;
        for e in live {
            w.write_all(&e.dir.to_le_bytes())?;
            w.write_all(&e.self_dir.to_le_bytes())?;
            w.write_all(&e.size.to_le_bytes())?;
            w.write_all(&e.mtime.to_le_bytes())?;
            w.write_all(&e.ctime.to_le_bytes())?;
            w.write_all(&e.attrs.to_le_bytes())?;
            w.write_all(&[0u8])?;
            write_str(&mut w, index.name(e))?;
        }
        w.flush()?;
    }
    std::fs::rename(tmp, path)
}

fn write_str(w: &mut impl Write, s: &str) -> std::io::Result<()> {
    let b = s.as_bytes();
    let n = b.len().min(u16::MAX as usize);
    w.write_all(&(n as u16).to_le_bytes())?;
    w.write_all(&b[..n])
}

pub fn load() -> Option<Index> {
    let f = std::fs::File::open(cache_path()).ok()?;
    let mut r = BufReader::with_capacity(1 << 20, f);
    let mut magic = [0u8; 8];
    r.read_exact(&mut magic).ok()?;
    if &magic != MAGIC {
        return None;
    }
    let mut index = Index::default();
    let nd = read_u32(&mut r)?;
    let mut paths = Vec::with_capacity(nd as usize);
    for _ in 0..nd {
        paths.push(read_str(&mut r)?);
    }
    use rayon::prelude::*;
    index.dirs = paths
        .into_par_iter()
        .map(|p| Dir { fold: fold(&p).into_boxed_str(), path: p.into_boxed_str(), entry: NONE })
        .collect();
    let ne = read_u32(&mut r)?;
    index.entries.reserve_exact(ne as usize);
    index.names.reserve(ne as usize * 24);
    for _ in 0..ne {
        let dir = read_u32(&mut r)?;
        let self_dir = read_u32(&mut r)?;
        let size = read_u64(&mut r)?;
        let mtime = read_u64(&mut r)?;
        let ctime = read_u64(&mut r)?;
        let attrs = read_u32(&mut r)?;
        let mut del = [0u8; 1];
        r.read_exact(&mut del).ok()?;
        let name = read_str(&mut r)?;
        let mut e = Entry::new(&mut index.names, &name, dir, self_dir, size, mtime, ctime, attrs);
        e.deleted = del[0] != 0;
        index.entries.push(e);
    }
    index.names.shrink_to_fit();
    index.finalize();
    index.ready = true;
    Some(index)
}

fn read_u32(r: &mut impl Read) -> Option<u32> {
    let mut b = [0u8; 4];
    r.read_exact(&mut b).ok()?;
    Some(u32::from_le_bytes(b))
}
fn read_u64(r: &mut impl Read) -> Option<u64> {
    let mut b = [0u8; 8];
    r.read_exact(&mut b).ok()?;
    Some(u64::from_le_bytes(b))
}
fn read_str(r: &mut impl Read) -> Option<String> {
    let mut b = [0u8; 2];
    r.read_exact(&mut b).ok()?;
    let n = u16::from_le_bytes(b) as usize;
    let mut v = vec![0u8; n];
    r.read_exact(&mut v).ok()?;
    String::from_utf8(v).ok()
}
