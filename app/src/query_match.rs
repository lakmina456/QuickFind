//! Matching one entry against a compiled search.

use super::*;


impl Query {
    pub fn matches(&self, idx: &Index, e: &Entry) -> bool {
        eval(&self.root, idx, e)
    }
}

fn eval(n: &Node, idx: &Index, e: &Entry) -> bool {
    match n {
        Node::And(v) => v.iter().all(|c| eval(c, idx, e)),
        Node::Or(v) => v.iter().any(|c| eval(c, idx, e)),
        Node::Not(c) => !eval(c, idx, e),
        Node::Leaf { term, files_only, folders_only } => {
            let dir = e.is_dir();
            if (*files_only && dir) || (*folders_only && !dir) {
                return false;
            }
            term_match(term, idx, e)
        }
    }
}

fn term_match(t: &Term, idx: &Index, e: &Entry) -> bool {
    match t {
        Term::True => true,
        Term::Text(tt) => text_match(tt, idx, e),
        Term::Content(needle) => content_match(idx, e, needle),
        Term::Ext(list) => {
            let ext = idx.extension(e);
            !ext.is_empty() && list.iter().any(|x| x.eq_ignore_ascii_case(ext))
        }
        Term::Size(lo, hi) => !e.is_dir() && e.size >= *lo && e.size <= *hi,
        Term::SizeUnknown => e.is_dir(),
        Term::Date { created, lo, hi } => {
            let ft = if *created { e.ctime } else { e.mtime };
            if ft == 0 {
                return false;
            }
            let t = dates::filetime_to_unix(ft);
            t >= *lo && t < *hi
        }
        Term::InFolder(p) => idx.dir_fold(e) == p,
        Term::Len(lo, hi) => {
            let n = idx.name(e).chars().count() as u64;
            n >= *lo && n <= *hi
        }
        Term::Empty => e.is_dir() && e.children == 0 && e.attrs & ATTR_DIRECTORY != 0,
        Term::Attrib(a) => *a != 0 && e.attrs & a == *a,
    }
}

fn text_match(t: &TextTerm, idx: &Index, e: &Entry) -> bool {
    let default_norm = !t.case && !t.diacritics;
    let owned;
    let hay: &str = if t.path {
        owned = if default_norm {
            crate::index::join(idx.dir_fold(e), idx.fold(e))
        } else {
            normalize(&idx.full_path(e), t.case, t.diacritics)
        };
        &owned
    } else if default_norm {
        idx.fold(e)
    } else {
        owned = normalize(idx.name(e), t.case, t.diacritics);
        &owned
    };
    match &t.mode {
        TextMode::Contains(n) => hay.contains(n.as_str()),
        TextMode::Equals(n) => hay == n,
        TextMode::Starts(n) => hay.starts_with(n.as_str()),
        TextMode::Ends(n) => hay.ends_with(n.as_str()),
        TextMode::Word(n) => whole_word(hay, n),
        TextMode::Re(r) => r.is_match(hay),
        TextMode::Glob(p) => glob(p, hay.as_bytes()),
    }
}

/// `*` = any run of characters, `?` = exactly one character (UTF-8 aware).
fn glob(p: &[u8], s: &[u8]) -> bool {
    let char_len = |b: u8| match b {
        b if b < 0x80 => 1,
        b if b >= 0xF0 => 4,
        b if b >= 0xE0 => 3,
        _ => 2,
    };
    let (mut pi, mut si) = (0usize, 0usize);
    let mut star: Option<(usize, usize)> = None;
    while si < s.len() {
        if pi < p.len() && p[pi] == b'?' {
            pi += 1;
            si += char_len(s[si]);
        } else if pi < p.len() && p[pi] == b'*' {
            star = Some((pi, si));
            pi += 1;
        } else if pi < p.len() && p[pi] == s[si] {
            pi += 1;
            si += 1;
        } else if let Some((sp, ss)) = star {
            let next = ss + char_len(s[ss]);
            star = Some((sp, next));
            pi = sp + 1;
            si = next;
        } else {
            return false;
        }
    }
    while pi < p.len() && p[pi] == b'*' {
        pi += 1;
    }
    pi == p.len() && si <= s.len()
}

fn whole_word(hay: &str, needle: &str) -> bool {
    if needle.is_empty() {
        return true;
    }
    let is_word = |c: char| c.is_alphanumeric() || c == '_';
    let mut start = 0;
    while let Some(p) = hay[start..].find(needle) {
        let s = start + p;
        let e = s + needle.len();
        let before = hay[..s].chars().next_back().map_or(true, |c| !is_word(c));
        let after = hay[e..].chars().next().map_or(true, |c| !is_word(c));
        if before && after {
            return true;
        }
        start = s + hay[s..].chars().next().map_or(1, |c| c.len_utf8());
    }
    false
}


/// Does the file's text contain `needle` (lower-case)? Reads at most 2 MB; stops when a newer search began.
fn content_match(idx: &Index, e: &Entry, needle: &str) -> bool {
    use std::io::Read;
    if e.is_dir() || e.size == 0 || e.size > 2 * 1024 * 1024 || crate::search::stale() {
        return false;
    }
    let Ok(mut f) = std::fs::File::open(idx.full_path(e)) else { return false };
    let mut buf = Vec::with_capacity(e.size as usize);
    if f.read_to_end(&mut buf).is_err() || buf.contains(&0) {
        return false;
    }
    String::from_utf8_lossy(&buf).to_lowercase().contains(needle)
}
