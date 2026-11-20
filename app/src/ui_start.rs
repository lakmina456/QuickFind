//! Command-line options, as in the original (-search, -filter, -sort, -case, -matchpath ...).

use super::App;
use crate::filters::FILTERS;
use crate::search::SortKey;

/// Command-line options, as in the original (-search, -filter, -case, -matchpath ...).
#[derive(Default)]
pub struct StartOptions {
    pub search: Option<String>,
    pub filter: Option<String>,
    pub bookmark: Option<String>,
    pub case: Option<bool>,
    pub diacritics: Option<bool>,
    pub path: Option<bool>,
    pub whole_word: Option<bool>,
    pub regex: Option<bool>,
    pub sort: Option<SortKey>,
    pub ascending: Option<bool>,
    pub dark: Option<bool>,
    pub inspector: bool,
    pub palette: bool,
    pub select_first: bool,
    pub accent: Option<String>,
    pub location: Option<String>,
}

impl StartOptions {
    pub fn from_args(args: &[String]) -> Self {
        let mut o = StartOptions::default();
        let mut i = 1;
        while i < args.len() {
            let a = args[i].to_ascii_lowercase();
            let next = args.get(i + 1).cloned();
            let mut took = false;
            match a.as_str() {
                "-s" | "-search" => { o.search = next; took = true; }
                "-filter" => { o.filter = next; took = true; }
                "-bookmark" => { o.bookmark = next; took = true; }
                "-location" => { o.location = next; took = true; }
                "-dark" => o.dark = Some(true),
                "-light" => o.dark = Some(false),
                "-inspector" => o.inspector = true,
                "-palette" => o.palette = true,
                "-selectfirst" => o.select_first = true,
                "-accent" => { o.accent = next; took = true; }
                "-sort" => {
                    o.sort = next.as_deref().map(|s| s.to_ascii_lowercase()).and_then(|s| match s.replace(['_', ' '], "").as_str() {
                        "name" => Some(SortKey::Name),
                        "path" => Some(SortKey::Path),
                        "size" => Some(SortKey::Size),
                        "extension" | "ext" | "type" => Some(SortKey::Extension),
                        "datemodified" | "dm" => Some(SortKey::Modified),
                        "datecreated" | "dc" => Some(SortKey::Created),
                        "attributes" => Some(SortKey::Attributes),
                        _ => None,
                    });
                    took = true;
                }
                "-sort-ascending" => o.ascending = Some(true),
                "-sort-descending" => o.ascending = Some(false),
                "-case" => o.case = Some(true),
                "-nocase" => o.case = Some(false),
                "-diacritics" => o.diacritics = Some(true),
                "-nodiacritics" => o.diacritics = Some(false),
                "-matchpath" => o.path = Some(true),
                "-nomatchpath" => o.path = Some(false),
                "-ww" | "-wholeword" => o.whole_word = Some(true),
                "-noww" | "-nowholeword" => o.whole_word = Some(false),
                "-regex" => o.regex = Some(true),
                "-noregex" => o.regex = Some(false),
                _ => {}
            }
            i += if took { 2 } else { 1 };
        }
        o
    }

    pub(super) fn apply(self, app: &mut App) {
        let o = &mut app.opts;
        o.case = self.case.unwrap_or(o.case);
        o.diacritics = self.diacritics.unwrap_or(o.diacritics);
        o.path = self.path.unwrap_or(o.path);
        o.whole_word = self.whole_word.unwrap_or(o.whole_word);
        o.regex = self.regex.unwrap_or(o.regex);
        if let Some(name) = self.bookmark {
            if let Some(b) = app.bookmarks.iter().find(|b| b.name.eq_ignore_ascii_case(&name)).cloned() {
                app.query = b.query;
                app.filter = b.filter;
            }
        }
        if let Some(f) = self.filter {
            if let Some(i) = FILTERS.iter().position(|(n, _)| n.eq_ignore_ascii_case(&f)) {
                app.filter = i;
            }
        }
        if let Some(d) = self.dark {
            app.settings.dark = d;
            app.theme_dirty = true;
        }
        if let Some(a) = self.accent {
            app.settings.accent = a;
            app.theme_dirty = true;
        }
        app.location = self.location.map(|l| l.to_ascii_lowercase().trim_end_matches('\\').to_string());
        if self.inspector {
            app.settings.preview = true;
        }
        app.show_palette = self.palette;
        app.auto_select = self.select_first;
        if let Some(s) = self.search {
            app.query = s;
        }
        if let Some(k) = self.sort {
            app.sort = k;
            app.ascending = matches!(k, SortKey::Name | SortKey::Path | SortKey::Extension);
        }
        if let Some(a) = self.ascending {
            app.ascending = a;
        }
    }
}
