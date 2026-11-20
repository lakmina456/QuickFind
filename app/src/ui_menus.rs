//! The menus in the title bar (File, Edit, View, Search, Bookmarks, History, Tools, Help) and export.

use super::{App, APP_NAME};
use crate::bookmarks;
use crate::filters::FILTERS;
use crate::platform::thousands;
use crate::theme;
use eframe::egui;
use std::time::Instant;

impl App {
    pub(super) fn menus(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        egui::menu::bar(ui, |ui| {
            ui.spacing_mut().button_padding = egui::vec2(9.0, 5.0);
            ui.menu_button("File", |ui| {
                if ui.button("Export\u{2026}").clicked() {
                    ui.close_menu();
                    self.export();
                }
                ui.separator();
                if ui.button("Exit").clicked() {
                    self.quitting = true;
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            });
            ui.menu_button("Edit", |ui| {
                if ui.add(egui::Button::new("Copy full path").shortcut_text("Ctrl+C")).clicked() {
                    ctx.copy_text(self.selected_paths().join("\r\n"));
                    ui.close_menu();
                }
                if ui.add(egui::Button::new("Select all").shortcut_text("Ctrl+A")).clicked() {
                    self.selected = self.ids.iter().copied().collect();
                    ui.close_menu();
                }
                ui.separator();
                let first = self.first_selected();
                if ui.add_enabled(first.is_some(), egui::Button::new("Rename\u{2026}").shortcut_text("F2")).clicked() {
                    if let Some(id) = first {
                        self.start_rename(id);
                    }
                    ui.close_menu();
                }
                if ui.add_enabled(first.is_some(), egui::Button::new("Delete").shortcut_text("Del")).clicked() {
                    if let Some(id) = first {
                        self.start_delete(id);
                    }
                    ui.close_menu();
                }
                if ui.add_enabled(first.is_some(), egui::Button::new("Properties").shortcut_text("Alt+Enter")).clicked() {
                    if let Some(id) = first {
                        crate::actions::properties(&self.entry_text(id, 0));
                    }
                    ui.close_menu();
                }
            });
            ui.menu_button("View", |ui| {
                let mut ch = false;
                ch |= toggle(ui, &mut self.settings.preview, "Preview pane", "");
                if toggle(ui, &mut self.settings.dark, "Dark theme", "") {
                    theme::apply(ctx, self.settings.dark, &self.settings.accent);
                    ch = true;
                }
                if ui.add(egui::Button::new("Command palette").shortcut_text("Ctrl+K")).clicked() {
                    self.show_palette = true;
                    self.palette_query.clear();
                    ui.close_menu();
                }
                ui.separator();
                ui.menu_button("Quick views", |ui| {
                    for (i, label) in ["Biggest files", "Biggest folders", "Duplicate names", "Empty folders", "Changed today"].iter().enumerate() {
                        if ui.button(*label).clicked() {
                            self.quick_view(i as u8);
                            ui.close_menu();
                        }
                    }
                });
                ui.separator();
                ui.menu_button("Accent", |ui| {
                    for (id, label, _) in theme::ACCENTS.iter() {
                        if ui.radio(self.settings.accent == *id, *label).clicked() {
                            self.settings.accent = id.to_string();
                            theme::apply(ctx, self.settings.dark, id);
                            ch = true;
                            ui.close_menu();
                        }
                    }
                });
                ui.separator();
                ch |= toggle(ui, &mut self.columns.path, "Path", "");
                ch |= toggle(ui, &mut self.columns.size, "Size", "");
                ch |= toggle(ui, &mut self.columns.extension, "Extension", "");
                ch |= toggle(ui, &mut self.columns.modified, "Date modified", "");
                ch |= toggle(ui, &mut self.columns.created, "Date created", "");
                ch |= toggle(ui, &mut self.columns.attributes, "Attributes", "");
                ui.separator();
                ch |= toggle(ui, &mut self.status_bar, "Status bar", "");
                if ch {
                    self.save_settings();
                }
            });
            ui.menu_button("Search", |ui| {
                let mut ch = false;
                ch |= toggle(ui, &mut self.opts.case, "Match case", "Ctrl+I");
                ch |= toggle(ui, &mut self.opts.whole_word, "Match whole words", "Ctrl+B");
                ch |= toggle(ui, &mut self.opts.path, "Match path", "Ctrl+U");
                ch |= toggle(ui, &mut self.opts.diacritics, "Match diacritics", "Ctrl+M");
                ui.separator();
                ch |= toggle(ui, &mut self.opts.regex, "Enable regex", "Ctrl+R");
                ch |= toggle(ui, &mut self.settings.content_search, "Search file contents", "");
                if ui.add(egui::Button::new("New tab").shortcut_text("Ctrl+T")).clicked() {
                    self.new_tab();
                    ui.close_menu();
                }
                ui.separator();
                for (i, (name, _)) in FILTERS.iter().enumerate() {
                    if ui.radio(self.filter == i, *name).clicked() {
                        self.filter = i;
                        ui.close_menu();
                    }
                }
                if ch {
                    self.save_settings();
                }
            });
            let fname = FILTERS[self.filter].0;
            if let Some(b) = bookmarks::menu(ui, &mut self.bookmarks, &self.query, self.filter, fname) {
                self.query = b.query;
                self.filter = b.filter;
                self.focus_search = true;
            }
            ui.menu_button("History", |ui| {
                let mut picked = None;
                if self.history.is_empty() {
                    ui.label("No searches yet");
                }
                for h in self.history.iter().take(15) {
                    let short: String = h.chars().take(48).collect();
                    if ui.button(short).clicked() {
                        picked = Some(h.clone());
                        ui.close_menu();
                    }
                }
                ui.separator();
                if ui.button("Clear history").clicked() {
                    self.history.clear();
                    crate::history::save(&self.history);
                    ui.close_menu();
                }
                if let Some(h) = picked {
                    self.query = h;
                    self.focus_search = true;
                }
            });
            ui.menu_button("Tools", |ui| {
                if ui.button("Options\u{2026}").clicked() {
                    self.show_options = true;
                    ui.close_menu();
                }
                self.index_menu(ui, ctx);
                if ui.button("Run as administrator\u{2026}").clicked() {
                    if crate::sysinfo::run_as_admin() {
                        self.quitting = true;
                        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                    ui.close_menu();
                }
                ui.separator();
                let mut ch = false;
                if toggle(ui, &mut self.start_with_windows, "Start with Windows", "") {
                    crate::sysinfo::set_startup(self.start_with_windows);
                }
                ch |= toggle(ui, &mut self.settings.background, "Close to tray", "");
                if toggle(ui, &mut self.settings.hotkey, "Global hotkey (Ctrl+Alt+Space)", "") {
                    crate::tray::set_hotkey_enabled(self.settings.hotkey);
                    ch = true;
                }
                if ch {
                    self.save_settings();
                }
            });
            ui.menu_button("Help", |ui| {
                if ui.button("Search syntax").clicked() {
                    self.show_help = true;
                    ui.close_menu();
                }
                if ui.button(format!("About {APP_NAME}")).clicked() {
                    self.show_about = true;
                    ui.close_menu();
                }
            });
        });
    }

    pub(super) fn export(&mut self) {
        let Some(path) = rfd::FileDialog::new().set_file_name("results.efu").add_filter("File List", &["efu", "csv"]).save_file() else {
            return;
        };
        let idx = self.index.read().unwrap();
        let mut out = String::from("Filename,Size,Date Modified,Date Created,Attributes\r\n");
        for &i in &self.ids {
            let Some(e) = idx.entries.get(i as usize) else { continue };
            let size = if e.is_dir() { String::new() } else { e.size.to_string() };
            out.push_str(&format!("\"{}\",{},{},{},{}\r\n", idx.full_path(e).replace('"', "\"\""), size, e.mtime, e.ctime, e.attrs));
        }
        let msg = match std::fs::write(&path, out) {
            Ok(_) => format!("Exported {} results", thousands(self.ids.len() as u64)),
            Err(e) => format!("Export failed: {e}"),
        };
        self.message = Some((msg, Instant::now()));
    }
}

/// A checkable menu entry. Returns true when it was flipped.
fn toggle(ui: &mut egui::Ui, v: &mut bool, label: &str, shortcut: &str) -> bool {
    let text = if *v { format!("\u{2714}  {label}") } else { format!("     {label}") };
    if ui.add(egui::Button::new(text).shortcut_text(shortcut)).clicked() {
        *v = !*v;
        ui.close_menu();
        return true;
    }
    false
}
