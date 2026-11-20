//! What the preview pane shows for one selected file or folder.

use crate::dates::format_filetime;
use crate::filters::{kind_of, Kind};
use crate::index::Index;
use crate::platform::{attrib_text, human_size};
use std::io::Read;

pub enum Body {
    Image(String),
    Text(String),
    Note(String),
}

pub struct Preview {
    pub path: String,
    pub name: String,
    pub kind_label: String,
    pub rows: Vec<(&'static str, String)>,
    pub body: Body,
}

const IMAGE_EXTS: [&str; 6] = ["png", "jpg", "jpeg", "gif", "bmp", "webp"];
const MAX_IMAGE: u64 = 30 * 1024 * 1024;

pub fn build(idx: &Index, id: u32) -> Option<Preview> {
    let e = idx.entries.get(id as usize)?;
    let path = idx.full_path(e);
    let ext = idx.extension(e).to_lowercase();
    let is_dir = e.is_dir();
    let kind_label = if is_dir {
        "Folder".to_string()
    } else if ext.is_empty() {
        "File".to_string()
    } else {
        format!("{} file", ext.to_uppercase())
    };
    let mut rows = Vec::new();
    rows.push(("Type", if is_dir { "Folder".to_string() } else if ext.is_empty() { "File".to_string() } else { format!("{} file", ext.to_uppercase()) }));
    if is_dir {
        rows.push(("Contains", format!("{} items", e.children)));
        if e.size != crate::index::NO_SIZE {
            rows.push(("Total size", human_size(e.size)));
        }
    } else {
        rows.push(("Size", format!("{}  ({} bytes)", human_size(e.size), crate::platform::thousands(e.size))));
    }
    rows.push(("Modified", format_filetime(e.mtime)));
    rows.push(("Created", format_filetime(e.ctime)));
    rows.push(("Location", idx.dir_path(e).to_string()));
    let attrs = attrib_text(e.attrs);
    if !attrs.is_empty() {
        rows.push(("Attributes", attrs));
    }
    let body = if is_dir {
        Body::Note("Folder".into())
    } else if IMAGE_EXTS.contains(&ext.as_str()) {
        if e.size <= MAX_IMAGE {
            Body::Image(format!("file://{path}"))
        } else {
            Body::Note("Picture is too large to preview".into())
        }
    } else if matches!(kind_of(&ext), Kind::Audio | Kind::Video | Kind::Archive | Kind::Exe) {
        Body::Note("No preview for this kind of file".into())
    } else {
        match sniff_text(&path) {
            Some(t) => Body::Text(t),
            None => Body::Note("No preview for this kind of file".into()),
        }
    };
    Some(Preview { name: idx.name(e).to_string(), path, kind_label, rows, body })
}

/// The start of a text file, or None when it looks binary.
fn sniff_text(path: &str) -> Option<String> {
    let mut f = std::fs::File::open(path).ok()?;
    let mut buf = vec![0u8; 6000];
    let n = f.read(&mut buf).ok()?;
    buf.truncate(n);
    if buf.contains(&0) {
        return None;
    }
    let s = String::from_utf8_lossy(&buf).into_owned();
    let total = s.chars().count();
    let bad = s.chars().filter(|c| *c == '\u{FFFD}').count();
    if total > 0 && bad * 20 > total {
        return None;
    }
    let mut out: String = s.lines().take(60).collect::<Vec<_>>().join("\n");
    if out.chars().count() > 2500 {
        out = out.chars().take(2500).collect();
    }
    Some(out)
}
