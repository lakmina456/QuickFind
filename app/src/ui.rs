//! The main window: state, search, selection, keyboard and the frame layout.

use crate::bookmarks::{self, Bookmark};
use crate::filters::FILTERS;
use crate::index::Index;
use crate::platform::{open, open_path};
use crate::preview::{self, Body, Preview};
use crate::query::Options;
use crate::scan::Progress;
use crate::search::{self, Reply, Request, SortKey};
use crate::settings::Settings;
use crate::startup::{setup_style, start_indexing};
use crate::table::{Columns, RowAction};
use crate::theme;
use eframe::egui::{self, Key};
use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::atomic::Ordering;
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::{Arc, RwLock};
use std::time::{Duration, Instant};

pub const APP_NAME: &str = "QuickFind";

#[path = "ui_start.rs"]
mod start;
pub use start::StartOptions;
#[path = "ui_menus.rs"]
mod menus;
#[path = "ui_bar.rs"]
mod bar;
#[path = "ui_panels.rs"]
mod panels;
#[path = "ui_side.rs"]
mod side;
#[path = "ui_inspector.rs"]
mod inspector;
#[path = "ui_layout.rs"]
mod layout_impl;

pub struct App {
    index: Arc<RwLock<Index>>,
    progress: Arc<Progress>,
    req_tx: Sender<Request>,
    rep_rx: Receiver<Reply>,
    query: String,
    opts: Options,
    filter: usize,
    sort: SortKey,
    ascending: bool,
    ids: Vec<u32>,
    ids_epoch: u64,
    error: Option<String>,
    millis: u128,
    serial: u64,
    shown_serial: u64,
    last_req: Option<Request>,
    last_sent: Instant,
    selected: HashSet<u32>,
    anchor: Option<usize>,
    cursor: Option<usize>,
    results_focus: bool,
    scroll_to: Option<usize>,
    columns: Columns,
    status_bar: bool,
    show_help: bool,
    show_about: bool,
    show_options: bool,
    opt_tab: u8,
    focus_search: bool,
    title: String,
    screenshot: Option<PathBuf>,
    frame: u32,
    shot_sent: bool,
    bookmarks: Vec<Bookmark>,
    message: Option<(String, Instant)>,
    settings: Settings,
    preview: Option<Preview>,
    preview_for: Option<u32>,
    rename: Option<(u32, String)>,
    confirm_delete: Option<Vec<String>>,
    icons: std::cell::RefCell<crate::icons::IconCache>,
    size_f: usize,
    date_f: usize,
    history: Vec<String>,
    task_tx: Sender<String>,
    task_rx: Receiver<String>,
    tabs: Vec<Tab>,
    tab: usize,
    batch: Option<Batch>,
    quitting: bool,
    location: Option<String>,
    show_palette: bool,
    palette_query: String,
    palette_idx: usize,
    drive_labels: Vec<(String, String)>,
    start_with_windows: bool,
    copied_path: bool,
    prev_total: u64,
    theme_dirty: bool,
    auto_select: bool,
}

/// One search tab: everything that makes up a search.
#[derive(Clone)]
pub struct Tab {
    query: String,
    filter: usize,
    size_f: usize,
    date_f: usize,
    sort: SortKey,
    ascending: bool,
}

impl Default for Tab {
    fn default() -> Self {
        Tab { query: String::new(), filter: 0, size_f: 0, date_f: 0, sort: SortKey::Name, ascending: true }
    }
}

/// The batch-rename window's state.
pub struct Batch {
    paths: Vec<String>,
    find: String,
    with: String,
    number: bool,
}

impl App {
    pub fn new(cc: &eframe::CreationContext<'_>, screenshot: Option<PathBuf>, start: StartOptions) -> Self {
        let mut app = Self::create(cc, screenshot);
        app.refresh_drives();
        start.apply(&mut app);
        app
    }

    fn create(cc: &eframe::CreationContext<'_>, screenshot: Option<PathBuf>) -> Self {
        let settings = Settings::load();
        settings.apply_rules();
        setup_style(&cc.egui_ctx, settings.dark, &settings.accent);
        egui_extras::install_image_loaders(&cc.egui_ctx);
        let index = Arc::new(RwLock::new(Index::default()));
        let progress = Arc::new(Progress::default());
        let (req_tx, req_rx) = channel();
        let (rep_tx, rep_rx) = channel();
        let (task_tx, task_rx) = channel();
        search::spawn(index.clone(), req_rx, rep_tx, cc.egui_ctx.clone());
        start_indexing(index.clone(), progress.clone(), cc.egui_ctx.clone(), screenshot.is_some());
        if screenshot.is_none() {
            let c = cc.egui_ctx.clone();
            crate::tray::start(move |ev| {
                match ev {
                    crate::tray::Event::Show => {
                        c.send_viewport_cmd(egui::ViewportCommand::Visible(true));
                        c.send_viewport_cmd(egui::ViewportCommand::Minimized(false));
                        c.send_viewport_cmd(egui::ViewportCommand::Focus);
                    }
                    crate::tray::Event::Exit => c.send_viewport_cmd(egui::ViewportCommand::Close),
                }
                c.request_repaint();
            });
        }
        App {
            index,
            progress,
            req_tx,
            rep_rx,
            query: String::new(),
            opts: settings.opts,
            filter: 0,
            sort: SortKey::Name,
            ascending: true,
            ids: Vec::new(),
            ids_epoch: 0,
            error: None,
            millis: 0,
            serial: 0,
            shown_serial: 0,
            last_req: None,
            last_sent: Instant::now() - Duration::from_secs(10),
            selected: HashSet::new(),
            anchor: None,
            cursor: None,
            results_focus: false,
            scroll_to: None,
            columns: settings.columns,
            status_bar: settings.status_bar,
            show_help: false,
            show_about: false,
            show_options: false,
            opt_tab: 0,
            focus_search: true,
            title: String::new(),
            screenshot,
            frame: 0,
            shot_sent: false,
            bookmarks: bookmarks::load(),
            message: None,
            settings,
            preview: None,
            preview_for: None,
            rename: None,
            confirm_delete: None,
            icons: Default::default(),
            size_f: 0,
            date_f: 0,
            history: crate::history::load(),
            task_tx,
            task_rx,
            tabs: vec![Tab::default()],
            tab: 0,
            batch: None,
            quitting: false,
            location: None,
            show_palette: false,
            palette_query: String::new(),
            palette_idx: 0,
            drive_labels: Vec::new(),
            start_with_windows: crate::sysinfo::startup_enabled(),
            copied_path: false,
            prev_total: 0,
            theme_dirty: false,
            auto_select: false,
        }
    }

    /// Write the current choices to disk.
    fn save_settings(&mut self) {
        self.settings.columns = self.columns;
        self.settings.status_bar = self.status_bar;
        self.settings.opts = self.opts;
        self.settings.save();
    }

    /// The search sent to the engine: type filter + size chip + date chip + what was typed.
    fn search_text(&self) -> String {
        let mut parts: Vec<&str> = Vec::new();
        let f = FILTERS[self.filter].1;
        if !f.is_empty() {
            parts.push(f);
        }
        if self.size_f > 0 {
            parts.push(bar::SIZES[self.size_f].1);
        }
        if self.date_f > 0 {
            parts.push(bar::DATES[self.date_f].1);
        }
        let q = self.query.trim();
        if parts.is_empty() && !(self.settings.content_search && !q.is_empty()) {
            return self.query.clone();
        }
        let typed = if q.is_empty() {
            String::new()
        } else if self.settings.content_search {
            format!("content:\"{}\"", q.replace('"', ""))
        } else {
            format!("<{}>", self.query)
        };
        let mut out = parts.join(" ");
        if !typed.is_empty() {
            out.push(' ');
            out.push_str(&typed);
        }
        out
    }

    fn cur_tab(&self) -> Tab {
        Tab { query: self.query.clone(), filter: self.filter, size_f: self.size_f, date_f: self.date_f, sort: self.sort, ascending: self.ascending }
    }

    fn load_tab(&mut self, t: Tab) {
        self.query = t.query;
        self.filter = t.filter;
        self.size_f = t.size_f;
        self.date_f = t.date_f;
        self.sort = t.sort;
        self.ascending = t.ascending;
        self.focus_search = true;
    }

    fn switch_tab(&mut self, i: usize) {
        if i == self.tab || i >= self.tabs.len() {
            return;
        }
        self.tabs[self.tab] = self.cur_tab();
        self.tab = i;
        let t = self.tabs[i].clone();
        self.load_tab(t);
    }

    fn new_tab(&mut self) {
        self.tabs[self.tab] = self.cur_tab();
        self.tabs.push(Tab::default());
        self.tab = self.tabs.len() - 1;
        self.load_tab(Tab::default());
    }

    fn close_tab(&mut self, i: usize) {
        if self.tabs.len() < 2 || i >= self.tabs.len() {
            return;
        }
        self.tabs[self.tab] = self.cur_tab();
        self.tabs.remove(i);
        self.tab = if self.tab > i || self.tab >= self.tabs.len() { self.tab.saturating_sub(1).min(self.tabs.len() - 1) } else { self.tab };
        let t = self.tabs[self.tab].clone();
        self.load_tab(t);
    }

    fn zip_selection(&mut self, id: u32) {
        let paths = if self.selected.contains(&id) { self.selected_paths() } else { vec![self.entry_text(id, 0)] };
        let Some(dest) = rfd::FileDialog::new().set_file_name("archive.zip").add_filter("Zip", &["zip"]).save_file() else { return };
        let tx = self.task_tx.clone();
        self.message = Some(("Zipping...".into(), Instant::now()));
        std::thread::spawn(move || {
            let _ = tx.send(crate::actions::zip(&paths, &dest));
        });
    }

    fn remember_query(&mut self) {
        let q = self.query.clone();
        crate::history::push(&mut self.history, &q);
    }

    /// Ready-made searches: biggest files/folders, duplicates, empty folders, changed today.
    fn quick_view(&mut self, which: u8) {
        self.filter = 0;
        self.size_f = 0;
        self.date_f = 0;
        self.query.clear();
        self.ascending = false;
        self.sort = SortKey::Size;
        match which {
            0 => self.query = "file:".into(),
            1 => self.query = "folder:".into(),
            2 => {
                self.query = "dupe: file:".into();
                self.sort = SortKey::Name;
                self.ascending = true;
            }
            3 => {
                self.query = "empty:".into();
                self.sort = SortKey::Name;
                self.ascending = true;
            }
            _ => {
                self.date_f = 1;
                self.sort = SortKey::Modified;
            }
        }
        self.focus_search = true;
    }

    /// Copy or move the selection (or the clicked row) into a folder chosen by the user.
    fn transfer_to(&mut self, id: u32, mv: bool) {
        let paths = if self.selected.contains(&id) { self.selected_paths() } else { vec![self.entry_text(id, 0)] };
        let Some(dest) = rfd::FileDialog::new().set_title(if mv { "Move to folder" } else { "Copy to folder" }).pick_folder() else { return };
        let tx = self.task_tx.clone();
        self.message = Some((if mv { "Moving..." } else { "Copying..." }.into(), Instant::now()));
        std::thread::spawn(move || {
            let _ = tx.send(crate::actions::transfer(&paths, &dest, mv));
        });
    }

    /// A folder dropped on the window searches inside it; a file searches for its name.
    fn handle_drops(&mut self, ctx: &egui::Context) {
        let dropped = ctx.input(|i| i.raw.dropped_files.clone());
        for f in dropped {
            let Some(p) = f.path else { continue };
            self.query = if p.is_dir() {
                format!("\"{}\\\"", p.display().to_string().trim_end_matches('\\'))
            } else {
                p.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
            };
            self.remember_query();
            self.focus_search = true;
        }
    }

    /// Plain words of the search, shown highlighted in the names.
    fn highlight_terms(&self) -> Vec<String> {
        if self.opts.regex {
            return Vec::new();
        }
        let mut out = Vec::new();
        for tok in self.query.split_whitespace() {
            if tok.starts_with('!') || tok.contains(':') || tok.contains('\\') {
                continue;
            }
            for part in tok.split(['|', '*', '?', '<', '>', '"']) {
                if !part.is_empty() {
                    out.push(part.to_ascii_lowercase());
                }
            }
        }
        out
    }

    fn pump_search(&mut self) {
        let (generation, epoch, ready) = {
            let idx = self.index.read().unwrap();
            (idx.generation, idx.epoch, idx.ready)
        };
        let req = Request {
            text: self.search_text(),
            opts: self.opts,
            sort: self.sort,
            ascending: self.ascending,
            generation,
            epoch,
            serial: 0,
            root: self.location.clone(),
        };
        let changed_query = match &self.last_req {
            None => true,
            Some(l) => l.text != req.text || l.opts != req.opts || l.sort != req.sort || l.ascending != req.ascending || l.epoch != req.epoch || l.root != req.root,
        };
        let changed_gen = self.last_req.as_ref().is_some_and(|l| l.generation != req.generation);
        let wait = if ready { Duration::from_millis(300) } else { Duration::from_millis(1000) };
        if changed_query || (changed_gen && self.last_sent.elapsed() >= wait) {
            if changed_query {
                self.selected.clear();
                self.anchor = None;
                self.cursor = None;
                self.scroll_to = Some(0);
            }
            self.serial += 1;
            let mut r = req.clone();
            r.serial = self.serial;
            crate::search::LATEST.store(r.serial, Ordering::Relaxed);
            let _ = self.req_tx.send(r);
            self.last_req = Some(req);
            self.last_sent = Instant::now();
        }
        while let Ok(rep) = self.rep_rx.try_recv() {
            if rep.serial >= self.shown_serial {
                self.shown_serial = rep.serial;
                self.ids = rep.ids;
                self.ids_epoch = rep.epoch;
                self.error = rep.error;
                self.millis = rep.millis;
            }
        }
    }

    /// Keep the preview pane in step with the selection.
    fn refresh_preview(&mut self, ctx: &egui::Context) {
        let want = if self.settings.preview && self.selected.len() == 1 { self.selected.iter().next().copied() } else { None };
        if want == self.preview_for {
            return;
        }
        if let Some(Preview { body: Body::Image(uri), .. }) = &self.preview {
            ctx.forget_image(uri);
        }
        self.copied_path = false;
        self.preview = want.and_then(|id| preview::build(&self.index.read().unwrap(), id));
        self.preview_for = want;
    }

    fn entry_text(&self, id: u32, what: u8) -> String {
        let idx = self.index.read().unwrap();
        match idx.entries.get(id as usize) {
            Some(e) => match what {
                0 => idx.full_path(e),
                1 => idx.name(e).to_string(),
                _ => idx.dir_path(e).to_string(),
            },
            None => String::new(),
        }
    }

    fn selected_paths(&self) -> Vec<String> {
        let idx = self.index.read().unwrap();
        self.ids
            .iter()
            .filter(|i| self.selected.contains(i))
            .filter_map(|i| idx.entries.get(*i as usize).map(|e| idx.full_path(e)))
            .collect()
    }

    fn first_selected(&self) -> Option<u32> {
        self.ids.iter().copied().find(|i| self.selected.contains(i))
    }

    fn start_rename(&mut self, id: u32) {
        self.rename = Some((id, self.entry_text(id, 1)));
    }

    fn start_delete(&mut self, id: u32) {
        let paths = if self.selected.contains(&id) { self.selected_paths() } else { vec![self.entry_text(id, 0)] };
        self.confirm_delete = Some(paths);
    }

    fn apply(&mut self, ctx: &egui::Context, a: RowAction) {
        match a {
            RowAction::Click { row, id, ctrl, shift } => {
                self.results_focus = true;
                if shift {
                    let from = self.anchor.unwrap_or(row);
                    let (lo, hi) = (from.min(row), from.max(row));
                    if !ctrl {
                        self.selected.clear();
                    }
                    self.selected.extend(self.ids[lo..=hi].iter().copied());
                } else if ctrl {
                    if !self.selected.remove(&id) {
                        self.selected.insert(id);
                    }
                    self.anchor = Some(row);
                } else {
                    self.selected.clear();
                    self.selected.insert(id);
                    self.anchor = Some(row);
                }
                self.cursor = Some(row);
            }
            RowAction::Open(id) => open(&self.entry_text(id, 0)),
            RowAction::OpenWith(id) => crate::actions::open_with(&self.entry_text(id, 0)),
            RowAction::OpenPath(id) => open_path(&self.entry_text(id, 0)),
            RowAction::CopyFull(id) => {
                let mut paths = self.selected_paths();
                if paths.is_empty() {
                    paths.push(self.entry_text(id, 0));
                }
                ctx.copy_text(paths.join("\r\n"));
            }
            RowAction::CopyName(id) => ctx.copy_text(self.entry_text(id, 1)),
            RowAction::CopyPath(id) => ctx.copy_text(self.entry_text(id, 2)),
            RowAction::CopyTo(id) => self.transfer_to(id, false),
            RowAction::MoveTo(id) => self.transfer_to(id, true),
            RowAction::CopyFiles(id) => {
                let paths = if self.selected.contains(&id) { self.selected_paths() } else { vec![self.entry_text(id, 0)] };
                let msg = match crate::actions::copy_files_to_clipboard(&paths) {
                    Ok(()) => format!("Copied {} file(s) - paste them in Explorer, e-mail or chat", paths.len()),
                    Err(e) => e,
                };
                self.message = Some((msg, Instant::now()));
            }
            RowAction::Zip(id) => self.zip_selection(id),
            RowAction::BatchRename(id) => {
                let paths = if self.selected.contains(&id) { self.selected_paths() } else { vec![self.entry_text(id, 0)] };
                self.batch = Some(Batch { paths, find: String::new(), with: String::new(), number: true });
            }
            RowAction::Rename(id) => self.start_rename(id),
            RowAction::Delete(id) => self.start_delete(id),
            RowAction::Properties(id) => crate::actions::properties(&self.entry_text(id, 0)),
            RowAction::Sort(k) => {
                if self.sort == k {
                    self.ascending = !self.ascending;
                } else {
                    self.sort = k;
                    // Sizes and dates start with the largest/newest, names with A.
                    self.ascending = matches!(k, SortKey::Name | SortKey::Path | SortKey::Extension);
                }
            }
        }
    }

    fn move_cursor(&mut self, delta: i64, extend: bool) {
        if self.ids.is_empty() {
            return;
        }
        let last = self.ids.len() as i64 - 1;
        let cur = self.cursor.map(|c| c as i64).unwrap_or(-1);
        let next = (cur + delta).clamp(0, last) as usize;
        if extend {
            let from = self.anchor.unwrap_or(next);
            self.selected.clear();
            let (lo, hi) = (from.min(next), from.max(next));
            self.selected.extend(self.ids[lo..=hi].iter().copied());
        } else {
            self.selected.clear();
            self.selected.insert(self.ids[next]);
            self.anchor = Some(next);
        }
        self.cursor = Some(next);
        self.scroll_to = Some(next);
    }

    fn keyboard(&mut self, ctx: &egui::Context) {
        let typing = ctx.memory(|m| m.focused().is_some()) && !self.results_focus;
        let dialog_open = self.rename.is_some() || self.confirm_delete.is_some();
        let (down, up, pgdn, pgup, home, end, enter, shift, cmd, alt, f2, del) = ctx.input(|i| {
            (
                i.key_pressed(Key::ArrowDown),
                i.key_pressed(Key::ArrowUp),
                i.key_pressed(Key::PageDown),
                i.key_pressed(Key::PageUp),
                i.key_pressed(Key::Home),
                i.key_pressed(Key::End),
                i.key_pressed(Key::Enter),
                i.modifiers.shift,
                i.modifiers.command,
                i.modifiers.alt,
                i.key_pressed(Key::F2),
                i.key_pressed(Key::Delete),
            )
        });
        let toggles = ctx.input_mut(|i| {
            [Key::I, Key::B, Key::U, Key::M, Key::R, Key::F, Key::A, Key::C, Key::D, Key::T, Key::W, Key::K]
                .map(|k| i.consume_key(egui::Modifiers::COMMAND, k))
        });
        if toggles[11] {
            self.show_palette = !self.show_palette;
            self.palette_query.clear();
        }
        if toggles[9] {
            self.new_tab();
        }
        if toggles[10] {
            self.close_tab(self.tab);
        }
        if toggles[8] {
            bookmarks::add(&mut self.bookmarks, &self.query, self.filter, FILTERS[self.filter].0);
            self.message = Some(("Bookmark added".into(), Instant::now()));
        }
        if toggles[..5].iter().any(|t| *t) {
            let o = &mut self.opts;
            for (on, flag) in toggles[..5].iter().zip([&mut o.case, &mut o.whole_word, &mut o.path, &mut o.diacritics, &mut o.regex]) {
                if *on {
                    *flag = !*flag;
                }
            }
            self.save_settings();
        }
        if toggles[5] {
            self.focus_search = true;
            self.results_focus = false;
        }
        if self.results_focus && toggles[6] {
            self.selected = self.ids.iter().copied().collect();
        }
        if self.results_focus && toggles[7] {
            ctx.copy_text(self.selected_paths().join("\r\n"));
        }
        if dialog_open {
            return;
        }
        if typing && (down || enter) {
            self.remember_query();
        }
        if typing && down {
            self.results_focus = true;
            ctx.memory_mut(|m| m.request_focus(egui::Id::new("results-focus")));
            self.move_cursor(if self.cursor.is_none() { 1 } else { 0 }, false);
            return;
        }
        if !self.results_focus {
            if enter && !self.ids.is_empty() {
                open(&self.entry_text(self.ids[self.cursor.unwrap_or(0)], 0));
            }
            return;
        }
        if down { self.move_cursor(1, shift); }
        if up {
            if self.cursor == Some(0) && !shift {
                self.results_focus = false;
                self.focus_search = true;
            } else {
                self.move_cursor(-1, shift);
            }
        }
        if pgdn { self.move_cursor(20, shift); }
        if pgup { self.move_cursor(-20, shift); }
        if home && !cmd { self.move_cursor(-(self.ids.len() as i64), shift); }
        if end && !cmd { self.move_cursor(self.ids.len() as i64, shift); }
        if f2 {
            if let Some(id) = self.first_selected() { self.start_rename(id); }
        }
        if del && !self.selected.is_empty() {
            if let Some(id) = self.first_selected() { self.start_delete(id); }
        }
        if enter && alt {
            if let Some(id) = self.first_selected() { crate::actions::properties(&self.entry_text(id, 0)); }
        } else if enter {
            for p in self.selected_paths().into_iter().take(16) {
                open(&p);
            }
        }
    }
}

impl eframe::App for App {
    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        crate::tray::remove();
    }

    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        if self.theme_dirty {
            theme::apply(ctx, self.settings.dark, &self.settings.accent);
            self.theme_dirty = false;
        }
        if self.auto_select && self.selected.is_empty() && !self.ids.is_empty() && self.ids_epoch == self.index.read().unwrap().epoch {
            self.selected.insert(self.ids[0]);
            self.auto_select = false;
        }
        self.pump_search();
        self.keyboard(ctx);
        self.refresh_preview(ctx);
        self.handle_drops(ctx);
        while let Ok(m) = self.task_rx.try_recv() {
            self.message = Some((m, Instant::now()));
        }
        if crate::tray::take_exit() {
            self.quitting = true;
        }
        if ctx.input(|i| i.viewport().close_requested()) && self.settings.background && !self.quitting && self.screenshot.is_none() {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
        }
        let title = if self.query.is_empty() { APP_NAME.to_string() } else { format!("{} - {APP_NAME}", self.query) };
        if title != self.title {
            ctx.send_viewport_cmd(egui::ViewportCommand::Title(title.clone()));
            self.title = title;
        }
        self.layout(ctx);
        self.dialogs(ctx);
        self.palette_window(ctx);
        if self.show_help {
            egui::Window::new("Search Syntax").open(&mut self.show_help).default_width(460.0).show(ctx, |ui| {
                egui::ScrollArea::vertical().max_height(420.0).show(ui, |ui| {
                    ui.monospace(crate::help::SYNTAX);
                });
            });
        }
        if self.show_about {
            egui::Window::new(format!("About {APP_NAME}")).open(&mut self.show_about).collapsible(false).resizable(false).show(ctx, |ui| {
                ui.heading(APP_NAME);
                ui.label(format!("Version {}", env!("CARGO_PKG_VERSION")));
                ui.label("Find files and folders by name, instantly.");
            });
        }
        self.screenshot_step(ctx);
        if self.progress.running.load(Ordering::Relaxed) {
            ctx.request_repaint_after(Duration::from_millis(60));
        }
    }
}

impl App {
    fn screenshot_step(&mut self, ctx: &egui::Context) {
        let Some(path) = self.screenshot.clone() else { return };
        self.frame += 1;
        ctx.request_repaint();
        // About 120 frames, but wait (up to ~15 s) for the loaded index's first results.
        let settled = {
            let idx = self.index.read().unwrap();
            idx.entries.is_empty() || (idx.ready && self.ids_epoch == idx.epoch && self.shown_serial == self.serial)
        };
        if self.frame >= 120 && (settled || self.frame >= 900) && !self.shot_sent {
            ctx.send_viewport_cmd(egui::ViewportCommand::Screenshot(egui::UserData::default()));
            self.shot_sent = true;
        }
        let shot = ctx.input(|i| {
            i.raw.events.iter().find_map(|e| match e {
                egui::Event::Screenshot { image, .. } => Some(image.clone()),
                _ => None,
            })
        });
        if let Some(img) = shot {
            let [w, h] = img.size;
            let bytes: Vec<u8> = img.pixels.iter().flat_map(|c| c.to_array()).collect();
            if let Some(parent) = path.parent() {
                let _ = std::fs::create_dir_all(parent);
            }
            if let Some(buf) = image::RgbaImage::from_raw(w as u32, h as u32, bytes) {
                let _ = buf.save(&path);
            }
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
        }
    }
}
