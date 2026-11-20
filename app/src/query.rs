//! Search language: space = AND, | = OR, ! = NOT, < > grouping, "phrases",
//! * and ? wildcards, modifiers (case:, path:, ww:, regex:, file:, folder: ...)
//! and functions (ext:, size:, dm:, dc:, infolder:, len:, count:, dupe:, empty:).

use crate::dates;
use crate::index::{fold, strip_accents, Entry, Index, ATTR_DIRECTORY};
use regex::Regex;

#[derive(Clone, Copy, Default, PartialEq, Eq, Debug)]
pub struct Options {
    pub case: bool,
    pub whole_word: bool,
    pub path: bool,
    pub diacritics: bool,
    pub regex: bool,
}

#[derive(Clone, Copy, Default)]
struct Mods {
    case: Option<bool>,
    whole_word: Option<bool>,
    path: Option<bool>,
    diacritics: Option<bool>,
    regex: Option<bool>,
    wildcards: Option<bool>,
    wfn: bool,
    start: bool,
    end: bool,
    files_only: bool,
    folders_only: bool,
}

impl Mods {
    fn merge(&self, inner: &Mods) -> Mods {
        Mods {
            case: inner.case.or(self.case),
            whole_word: inner.whole_word.or(self.whole_word),
            path: inner.path.or(self.path),
            diacritics: inner.diacritics.or(self.diacritics),
            regex: inner.regex.or(self.regex),
            wildcards: inner.wildcards.or(self.wildcards),
            wfn: self.wfn || inner.wfn,
            start: self.start || inner.start,
            end: self.end || inner.end,
            files_only: self.files_only || inner.files_only,
            folders_only: self.folders_only || inner.folders_only,
        }
    }
    fn is_empty_type(&self) -> bool {
        !self.files_only && !self.folders_only
    }
}

enum TextMode {
    Contains(String),
    Equals(String),
    Starts(String),
    Ends(String),
    Word(String),
    Re(Regex),
    Glob(Vec<u8>),
}

struct TextTerm {
    mode: TextMode,
    case: bool,
    diacritics: bool,
    path: bool,
}

enum Term {
    Text(TextTerm),
    Ext(Vec<String>),
    /// Text inside the file (lower-case); only files up to 2 MB are read.
    Content(String),
    Size(u64, u64),
    SizeUnknown,
    Date { created: bool, lo: i64, hi: i64 },
    InFolder(String),
    Len(u64, u64),
    Empty,
    Attrib(u32),
    True,
}

enum Node {
    And(Vec<Node>),
    Or(Vec<Node>),
    Not(Box<Node>),
    Leaf { term: Term, files_only: bool, folders_only: bool },
}

pub struct Query {
    root: Node,
    pub count: Option<usize>,
    pub dupe: bool,
    pub is_empty: bool,
}

// ------------------------------------------------------------------ tokens

#[derive(Debug, Clone, PartialEq)]
enum Tok {
    Open,
    Close,
    Or,
    Not,
    Word(String),
}

const MODIFIERS: &[&str] = &[
    "nocase:", "case:", "nopath:", "path:", "nowholeword:", "wholeword:", "noww:", "ww:", "noregex:", "regex:",
    "nodiacritics:", "diacritics:", "nowildcards:", "wildcards:", "wholefilename:", "wfn:", "startwith:", "endwith:",
    "files:", "file:", "folders:", "folder:", "ascii:", "utf8:", "nofile:", "nofolder:",
];

fn is_modifier_only(w: &str) -> bool {
    let mut rest = w;
    if rest.is_empty() {
        return false;
    }
    while !rest.is_empty() {
        let lower = rest.to_ascii_lowercase();
        match MODIFIERS.iter().find(|m| lower.starts_with(**m)) {
            Some(m) => rest = &rest[m.len()..],
            None => return false,
        }
    }
    true
}

fn tokenize(s: &str) -> Vec<Tok> {
    let mut out = Vec::new();
    let mut word = String::new();
    let mut quoted = false;
    let push = |w: &mut String, out: &mut Vec<Tok>| {
        if !w.is_empty() {
            out.push(Tok::Word(std::mem::take(w)));
        }
    };
    for c in s.chars() {
        if quoted {
            if c == '"' {
                quoted = false;
            } else {
                word.push(c);
            }
            continue;
        }
        match c {
            '"' => quoted = true,
            c if c.is_whitespace() => push(&mut word, &mut out),
            '|' => {
                push(&mut word, &mut out);
                out.push(Tok::Or);
            }
            '!' if word.is_empty() => out.push(Tok::Not),
            '<' | '>' => {
                let last = word.chars().last();
                let comparison = matches!(last, Some(':') | Some('<') | Some('>') | Some('='));
                if comparison && !(c == '<' && is_modifier_only(&word)) {
                    word.push(c);
                } else {
                    push(&mut word, &mut out);
                    out.push(if c == '<' { Tok::Open } else { Tok::Close });
                }
            }
            c => word.push(c),
        }
    }
    push(&mut word, &mut out);
    out
}

// ------------------------------------------------------------------ parser

struct Parser<'a> {
    toks: Vec<Tok>,
    pos: usize,
    opts: &'a Options,
    count: Option<usize>,
    dupe: bool,
    error: Option<String>,
}

pub fn compile(text: &str, opts: &Options) -> Result<Query, String> {
    let toks = tokenize(text);
    let is_empty = toks.is_empty();
    let mut p = Parser { toks, pos: 0, opts, count: None, dupe: false, error: None };
    let root = p.parse_or(&Mods::default());
    if let Some(e) = p.error {
        return Err(e);
    }
    Ok(Query { root, count: p.count, dupe: p.dupe, is_empty })
}

impl<'a> Parser<'a> {
    fn peek(&self) -> Option<&Tok> {
        self.toks.get(self.pos)
    }

    fn parse_or(&mut self, mods: &Mods) -> Node {
        let mut alts = vec![self.parse_and(mods)];
        while self.peek() == Some(&Tok::Or) {
            self.pos += 1;
            alts.push(self.parse_and(mods));
        }
        if alts.len() == 1 { alts.pop().unwrap() } else { Node::Or(alts) }
    }

    fn parse_and(&mut self, mods: &Mods) -> Node {
        let mut items = Vec::new();
        let mut cur = *mods;
        loop {
            match self.peek() {
                None | Some(Tok::Or) | Some(Tok::Close) => break,
                Some(Tok::Word(w)) if is_modifier_only(w) => {
                    let w = w.clone();
                    self.pos += 1;
                    let (m, _) = strip_modifiers(&w);
                    let next_is_term = !matches!(self.peek(), None | Some(Tok::Or) | Some(Tok::Close));
                    if next_is_term {
                        // Applies to the next item (and, for a bare switch, the rest of this group).
                        cur = cur.merge(&m);
                    } else if !m.is_empty_type() {
                        items.push(leaf(Term::True, &cur.merge(&m)));
                    }
                }
                _ => {
                    if let Some(n) = self.parse_unary(&cur) {
                        items.push(n);
                    }
                }
            }
        }
        // Reading file contents is slow: do it last, on what the other terms left.
        items.sort_by_key(|n| matches!(n, Node::Leaf { term: Term::Content(_), .. }));
        if items.len() == 1 { items.pop().unwrap() } else { Node::And(items) }
    }

    fn parse_unary(&mut self, mods: &Mods) -> Option<Node> {
        match self.peek()?.clone() {
            Tok::Not => {
                self.pos += 1;
                let inner = self.parse_unary(mods)?;
                Some(Node::Not(Box::new(inner)))
            }
            Tok::Open => {
                self.pos += 1;
                let n = self.parse_or(mods);
                if self.peek() == Some(&Tok::Close) {
                    self.pos += 1;
                }
                Some(n)
            }
            Tok::Close | Tok::Or => {
                self.pos += 1;
                None
            }
            Tok::Word(w) => {
                self.pos += 1;
                self.word(&w, mods)
            }
        }
    }

    fn word(&mut self, w: &str, outer: &Mods) -> Option<Node> {
        let (m, rest) = strip_modifiers(w);
        let mods = outer.merge(&m);
        let lower = rest.to_ascii_lowercase();
        let func = |name: &str| -> Option<&str> {
            if lower.starts_with(name) { Some(&rest[name.len()..]) } else { None }
        };
        for (macro_name, exts) in crate::filters::MACROS {
            if let Some(arg) = func(macro_name) {
                let mut n = vec![leaf(Term::Ext(split_exts(exts)), &mods)];
                if !arg.is_empty() {
                    n.push(self.text_leaf(arg, &mods)?);
                }
                return Some(if n.len() == 1 { n.pop().unwrap() } else { Node::And(n) });
            }
        }
        if let Some(arg) = func("content:") {
            return Some(leaf(Term::Content(arg.to_lowercase()), &mods));
        }
        if let Some(arg) = func("ext:") {
            return Some(leaf(Term::Ext(split_exts(arg)), &mods));
        }
        if let Some(arg) = func("size:") {
            return Some(leaf(parse_size(arg).unwrap_or(Term::True), &mods));
        }
        for (name, created) in [("dm:", false), ("datemodified:", false), ("dc:", true), ("datecreated:", true)] {
            if let Some(arg) = func(name) {
                return Some(match dates::parse_range(arg) {
                    Some((lo, hi)) => leaf(Term::Date { created, lo, hi }, &mods),
                    None => leaf(Term::True, &mods),
                });
            }
        }
        for name in ["infolder:", "parent:"] {
            if let Some(arg) = func(name) {
                let p = fold(arg.trim_end_matches('\\'));
                return Some(leaf(Term::InFolder(p), &mods));
            }
        }
        if let Some(arg) = func("len:") {
            let num = |v: &str| v.trim().parse::<u64>().ok().map(|n| (n, n));
            let (lo, hi) = parse_compare(arg, num).unwrap_or((0, u64::MAX));
            return Some(leaf(Term::Len(lo, hi), &mods));
        }
        if let Some(arg) = func("count:") {
            self.count = arg.trim().parse().ok();
            return Some(leaf(Term::True, &mods));
        }
        if func("dupe:").is_some() {
            self.dupe = true;
            return Some(leaf(Term::True, &mods));
        }
        if func("empty:").is_some() {
            return Some(leaf(Term::Empty, &mods));
        }
        for name in ["attrib:", "attributes:"] {
            if let Some(arg) = func(name) {
                return Some(leaf(Term::Attrib(parse_attrib(arg)), &mods));
            }
        }
        self.text_leaf(rest, &mods)
    }

    fn text_leaf(&mut self, text: &str, mods: &Mods) -> Option<Node> {
        if text.is_empty() {
            return Some(leaf(Term::True, mods));
        }
        let o = self.opts;
        let case = mods.case.unwrap_or(o.case);
        let diacritics = mods.diacritics.unwrap_or(o.diacritics);
        let regex = mods.regex.unwrap_or(o.regex);
        let whole_word = mods.whole_word.unwrap_or(o.whole_word);
        let wildcards = mods.wildcards.unwrap_or(true);
        let path = mods.path.unwrap_or(o.path) || text.contains('\\') || text.contains('/');
        let text_norm = text.replace('/', "\\");
        let norm = |s: &str| normalize(s, case, diacritics);
        let mode = if regex {
            let pat = if case { text.to_string() } else { format!("(?i){text}") };
            match Regex::new(&pat) {
                Ok(r) => TextMode::Re(r),
                Err(_) => {
                    self.error = Some(format!("Invalid regex: {text}"));
                    return None;
                }
            }
        } else if wildcards && (text.contains('*') || text.contains('?')) {
            // A wildcard search matches the whole name (or whole path).
            TextMode::Glob(norm(&text_norm).into_bytes())
        } else if mods.wfn {
            TextMode::Equals(norm(&text_norm))
        } else if mods.start {
            TextMode::Starts(norm(&text_norm))
        } else if mods.end {
            TextMode::Ends(norm(&text_norm))
        } else if whole_word {
            TextMode::Word(norm(&text_norm))
        } else {
            TextMode::Contains(norm(&text_norm))
        };
        Some(leaf(Term::Text(TextTerm { mode, case, diacritics, path }), mods))
    }
}

fn leaf(term: Term, m: &Mods) -> Node {
    Node::Leaf { term, files_only: m.files_only, folders_only: m.folders_only }
}

fn strip_modifiers(w: &str) -> (Mods, &str) {
    let mut m = Mods::default();
    let mut rest = w;
    loop {
        let lower = rest.to_ascii_lowercase();
        let Some(found) = MODIFIERS.iter().find(|p| lower.starts_with(**p)) else { break };
        match *found {
            "case:" => m.case = Some(true),
            "nocase:" => m.case = Some(false),
            "path:" => m.path = Some(true),
            "nopath:" => m.path = Some(false),
            "wholeword:" | "ww:" => m.whole_word = Some(true),
            "nowholeword:" | "noww:" => m.whole_word = Some(false),
            "regex:" => m.regex = Some(true),
            "noregex:" => m.regex = Some(false),
            "diacritics:" => m.diacritics = Some(true),
            "nodiacritics:" => m.diacritics = Some(false),
            "wildcards:" => m.wildcards = Some(true),
            "nowildcards:" => m.wildcards = Some(false),
            "wholefilename:" | "wfn:" => m.wfn = true,
            "startwith:" => m.start = true,
            "endwith:" => m.end = true,
            "file:" | "files:" | "nofolder:" => m.files_only = true,
            "folder:" | "folders:" | "nofile:" => m.folders_only = true,
            _ => {}
        }
        rest = &rest[found.len()..];
        if m.regex == Some(true) {
            break; // the rest is the pattern itself
        }
    }
    (m, rest)
}

fn split_exts(s: &str) -> Vec<String> {
    s.split(';').map(|e| e.trim().trim_start_matches('.').to_lowercase()).filter(|e| !e.is_empty()).collect()
}

/// "<", ">", "<=", ">=", "=", "a..b" or "a-b" over numbers converted by `unit`.
fn parse_compare(arg: &str, value: impl Fn(&str) -> Option<(u64, u64)>) -> Option<(u64, u64)> {
    let a = arg.trim();
    for (op, f) in [
        ("<=", 0u8), (">=", 1), ("<", 2), (">", 3), ("=", 4),
    ] {
        if let Some(v) = a.strip_prefix(op) {
            let (lo, hi) = value(v)?;
            return Some(match f {
                0 => (0, hi),
                1 => (lo, u64::MAX),
                2 => (0, lo.saturating_sub(1)),
                3 => (hi.saturating_add(1), u64::MAX),
                _ => (lo, hi),
            });
        }
    }
    if let Some((l, r)) = a.split_once("..").or_else(|| a.split_once('-')) {
        let (lo, _) = value(l)?;
        let (_, hi) = value(r)?;
        return Some((lo, hi));
    }
    value(a)
}


/// size: keywords and values (see MECHANICS.md, sub_140055200).
fn parse_size(arg: &str) -> Option<Term> {
    let a = arg.trim().to_ascii_lowercase();
    if a == "unknown" {
        return Some(Term::SizeUnknown);
    }
    let r = parse_compare(&a, size_value)?;
    Some(Term::Size(r.0, r.1))
}

const KB: u64 = 1024;
pub const SIZE_BANDS: [(&str, u64, u64); 7] = [
    ("empty", 0, 0),
    ("tiny", 1, 10 * KB),
    ("small", 10 * KB + 1, 100 * KB),
    ("medium", 100 * KB + 1, KB * KB),
    ("large", KB * KB + 1, 16 * KB * KB),
    ("huge", 16 * KB * KB + 1, 128 * KB * KB),
    ("gigantic", 128 * KB * KB + 1, u64::MAX - 1),
];

fn size_value(v: &str) -> Option<(u64, u64)> {
    let v = v.trim();
    if let Some(b) = SIZE_BANDS.iter().find(|b| b.0 == v) {
        return Some((b.1, b.2));
    }
    let split = v.find(|c: char| !(c.is_ascii_digit() || c == '.')).unwrap_or(v.len());
    let (num, unit) = v.split_at(split);
    let num: f64 = num.parse().ok()?;
    let mult: u64 = match unit.trim() {
        "" | "b" | "bytes" => return Some((num as u64, num as u64)),
        "k" | "kb" => KB,
        "m" | "mb" => KB * KB,
        "g" | "gb" => KB * KB * KB,
        "t" | "tb" => KB.pow(4),
        "p" | "pb" => KB.pow(5),
        "e" | "eb" => KB.pow(6),
        _ => return None,
    };
    // A value with a unit matches the 1 KB band that ends at the value rounded up.
    let hi = ((num * mult as f64) as u64).saturating_add(0x3ff) & !0x3ff;
    Some((hi.saturating_sub(0x3ff), hi))
}

fn parse_attrib(s: &str) -> u32 {
    let mut a = 0;
    for c in s.to_ascii_uppercase().chars() {
        a |= match c {
            'R' => 0x1, 'H' => 0x2, 'S' => 0x4, 'D' => 0x10, 'A' => 0x20, 'N' => 0x80, 'T' => 0x100,
            'P' => 0x200, 'L' => 0x400, 'C' => 0x800, 'O' => 0x1000, 'I' => 0x2000, 'E' => 0x4000, _ => 0,
        };
    }
    a
}

pub fn normalize(s: &str, case: bool, diacritics: bool) -> String {
    match (case, diacritics) {
        (false, false) => fold(s),
        (true, false) => strip_accents(s),
        (false, true) => s.to_lowercase(),
        (true, true) => s.to_string(),
    }
}

#[path = "query_match.rs"]
mod matching;

#[cfg(test)]
#[path = "query_tests.rs"]
mod tests;
