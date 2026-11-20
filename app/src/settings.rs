//! Settings kept between runs (theme, columns, indexing rules ...), one `key=value` per line.

use crate::query::Options;
use crate::scan::{self, Rules};
use crate::table::Columns;
use std::path::PathBuf;

#[derive(Clone)]
pub struct Settings {
    pub dark: bool,
    pub preview: bool,
    pub status_bar: bool,
    pub compact: bool,
    pub columns: Columns,
    pub opts: Options,
    /// Folder names (or full paths), one per line, left out of the index.
    pub exclude: String,
    pub hide_hidden: bool,
    pub hide_system: bool,
    pub drives_off: Vec<String>,
    pub include_extra: bool,
    /// Closing the window hides it; Ctrl+Alt+Space brings it back.
    pub background: bool,
    pub accent: String,
    /// Leave out junk: Temp, caches, Recycle Bin, ~$ lock files, VCS and node_modules folders.
    pub junk: bool,
    pub hotkey: bool,
    pub content_search: bool,
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            dark: false,
            preview: false,
            status_bar: true,
            compact: false,
            columns: Columns::default(),
            opts: Options::default(),
            exclude: String::new(),
            hide_hidden: false,
            hide_system: false,
            drives_off: Vec::new(),
            include_extra: false,
            background: false,
            accent: "blue".to_string(),
            junk: true,
            hotkey: true,
            content_search: false,
        }
    }
}

fn file() -> PathBuf {
    crate::index::cache_path().with_file_name("settings.txt")
}

impl Settings {
    pub fn load() -> Settings {
        let mut s = Settings::default();
        let Ok(text) = std::fs::read_to_string(file()) else { return s };
        for line in text.lines() {
            let Some((k, v)) = line.split_once('=') else { continue };
            let b = v == "1";
            match k {
                "dark" => s.dark = b,
                "preview" => s.preview = b,
                "status_bar" => s.status_bar = b,
                "compact" => s.compact = b,
                "col_path" => s.columns.path = b,
                "col_size" => s.columns.size = b,
                "col_extension" => s.columns.extension = b,
                "col_modified" => s.columns.modified = b,
                "col_created" => s.columns.created = b,
                "col_attributes" => s.columns.attributes = b,
                "case" => s.opts.case = b,
                "whole_word" => s.opts.whole_word = b,
                "path" => s.opts.path = b,
                "diacritics" => s.opts.diacritics = b,
                "regex" => s.opts.regex = b,
                "exclude" => s.exclude = v.replace(';', "\n"),
                "hide_hidden" => s.hide_hidden = b,
                "hide_system" => s.hide_system = b,
                "include_extra" => s.include_extra = b,
                "background" => s.background = b,
                "accent" => s.accent = v.to_string(),
                "junk" => s.junk = b,
                "hotkey" => s.hotkey = b,
                "content_search" => s.content_search = b,
                "drives_off" => s.drives_off = v.split(';').filter(|d| !d.is_empty()).map(String::from).collect(),
                _ => {}
            }
        }
        s
    }

    pub fn save(&self) {
        let f = |b: bool| if b { "1" } else { "0" };
        let c = &self.columns;
        let o = &self.opts;
        let lines = [
            ("dark", f(self.dark).to_string()),
            ("preview", f(self.preview).to_string()),
            ("status_bar", f(self.status_bar).to_string()),
            ("compact", f(self.compact).to_string()),
            ("col_path", f(c.path).to_string()),
            ("col_size", f(c.size).to_string()),
            ("col_extension", f(c.extension).to_string()),
            ("col_modified", f(c.modified).to_string()),
            ("col_created", f(c.created).to_string()),
            ("col_attributes", f(c.attributes).to_string()),
            ("case", f(o.case).to_string()),
            ("whole_word", f(o.whole_word).to_string()),
            ("path", f(o.path).to_string()),
            ("diacritics", f(o.diacritics).to_string()),
            ("regex", f(o.regex).to_string()),
            ("exclude", self.exclude.lines().map(str::trim).filter(|l| !l.is_empty()).collect::<Vec<_>>().join(";")),
            ("hide_hidden", f(self.hide_hidden).to_string()),
            ("hide_system", f(self.hide_system).to_string()),
            ("drives_off", self.drives_off.join(";")),
            ("include_extra", f(self.include_extra).to_string()),
            ("background", f(self.background).to_string()),
            ("accent", self.accent.clone()),
            ("junk", f(self.junk).to_string()),
            ("hotkey", f(self.hotkey).to_string()),
            ("content_search", f(self.content_search).to_string()),
        ];
        let text: String = lines.iter().map(|(k, v)| format!("{k}={v}\n")).collect();
        if let Some(p) = file().parent() {
            let _ = std::fs::create_dir_all(p);
        }
        let _ = std::fs::write(file(), text);
    }

    /// Hand the indexing rules to the scanner.
    pub fn apply_rules(&self) {
        let exclude = self
            .exclude
            .lines()
            .map(|l| l.trim().trim_end_matches('\\').to_lowercase())
            .filter(|l| !l.is_empty())
            .collect();
        scan::set_rules(Rules {
            exclude,
            hide_hidden: self.hide_hidden,
            hide_system: self.hide_system,
            drives_off: self.drives_off.clone(),
            include_extra: self.include_extra,
            junk: self.junk,
        });
    }
}
