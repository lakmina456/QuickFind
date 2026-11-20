//! The Options, Rename, Delete and Batch Rename windows.

use super::App;

use crate::scan;
use crate::startup::rebuild;
use crate::theme;
use eframe::egui::{self, Align2, Color32, RichText};
use std::time::Instant;

impl App {
    pub(super) fn dialogs(&mut self, ctx: &egui::Context) {
        self.options_window(ctx);
        self.rename_window(ctx);
        self.delete_window(ctx);
        self.batch_window(ctx);
    }

    fn options_window(&mut self, ctx: &egui::Context) {
        if !self.show_options {
            return;
        }
        let pal = theme::get(ctx);
        let mut open = true;
        let mut changed = false;
        let mut rules_changed = false;
        let mut rebuild_now = false;
        egui::Window::new("Options").open(&mut open).collapsible(false).resizable(false).default_width(440.0).show(ctx, |ui| {
            ui.horizontal(|ui| {
                for (i, name) in ["General", "Indexing", "Columns"].iter().enumerate() {
                    ui.selectable_value(&mut self.opt_tab, i as u8, *name);
                }
            });
            ui.separator();
            match self.opt_tab {
                0 => {
                    ui.label(RichText::new("Appearance").strong());
                    ui.horizontal(|ui| {
                        changed |= ui.radio_value(&mut self.settings.dark, true, "Dark").changed();
                        changed |= ui.radio_value(&mut self.settings.dark, false, "Light").changed();
                    });
                    ui.horizontal(|ui| {
                        changed |= ui.radio_value(&mut self.settings.compact, false, "Comfortable rows").changed();
                        changed |= ui.radio_value(&mut self.settings.compact, true, "Compact rows").changed();
                    });
                    changed |= ui.checkbox(&mut self.settings.preview, "Show preview pane").changed();
                    changed |= ui.checkbox(&mut self.status_bar, "Show status bar").changed();
                    changed |= ui.checkbox(&mut self.settings.background, "Closing the window minimizes it (Ctrl+Alt+Space brings it back)").changed();
                    ui.add_space(8.0);
                    ui.label(RichText::new("Search defaults").strong());
                    let o = &mut self.opts;
                    changed |= ui.checkbox(&mut o.case, "Match case").changed();
                    changed |= ui.checkbox(&mut o.whole_word, "Match whole words").changed();
                    changed |= ui.checkbox(&mut o.path, "Match path").changed();
                    changed |= ui.checkbox(&mut o.diacritics, "Match accents").changed();
                    changed |= ui.checkbox(&mut o.regex, "Enable regex").changed();
                }
                1 => {
                    ui.label(RichText::new("Drives to index").strong());
                    if ui.checkbox(&mut self.settings.include_extra, "Include USB and network drives").changed() {
                        rules_changed = true;
                    }
                    for d in scan::fixed_drives() {
                        let mut on = !self.settings.drives_off.iter().any(|x| x.eq_ignore_ascii_case(&d));
                        if ui.checkbox(&mut on, &d).changed() {
                            self.settings.drives_off.retain(|x| !x.eq_ignore_ascii_case(&d));
                            if !on {
                                self.settings.drives_off.push(d.clone());
                            }
                            rules_changed = true;
                        }
                    }
                    ui.add_space(8.0);
                    ui.label(RichText::new("Excluded folders").strong());
                    ui.label(RichText::new("One per line: a folder name such as node_modules, or a full path.").size(12.0).color(pal.dim));
                    rules_changed |= ui
                        .add(egui::TextEdit::multiline(&mut self.settings.exclude).desired_rows(5).desired_width(f32::INFINITY))
                        .changed();
                    rules_changed |= ui.checkbox(&mut self.settings.hide_hidden, "Leave out hidden files and folders").changed();
                    rules_changed |= ui.checkbox(&mut self.settings.hide_system, "Leave out system files and folders").changed();
                    ui.add_space(8.0);
                    if ui.button("Apply and rebuild index").clicked() {
                        rebuild_now = true;
                    }
                    ui.label(RichText::new("Changes here take effect when the index is rebuilt.").size(12.0).color(pal.dim));
                }
                _ => {
                    let c = &mut self.columns;
                    ui.label(RichText::new("Show columns").strong());
                    changed |= ui.checkbox(&mut c.path, "Path").changed();
                    changed |= ui.checkbox(&mut c.size, "Size").changed();
                    changed |= ui.checkbox(&mut c.extension, "Extension").changed();
                    changed |= ui.checkbox(&mut c.modified, "Date Modified").changed();
                    changed |= ui.checkbox(&mut c.created, "Date Created").changed();
                    changed |= ui.checkbox(&mut c.attributes, "Attributes").changed();
                }
            }
        });
        self.show_options = open;
        if changed || rules_changed {
            theme::apply(ctx, self.settings.dark, &self.settings.accent);
            self.settings.apply_rules();
            self.save_settings();
        }
        if rebuild_now {
            self.settings.apply_rules();
            self.save_settings();
            rebuild(self.index.clone(), self.progress.clone(), ctx.clone());
            self.message = Some(("Rebuilding the index...".into(), Instant::now()));
        }
    }

    fn batch_window(&mut self, ctx: &egui::Context) {
        let Some(mut b) = self.batch.take() else { return };
        let names: Vec<String> = b.paths.iter().map(|p| std::path::Path::new(p).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()).collect();
        let (mut apply, mut cancel) = (false, ctx.input(|i| i.key_pressed(egui::Key::Escape)));
        let pal = theme::get(ctx);
        egui::Window::new("Batch Rename").collapsible(false).resizable(false).anchor(Align2::CENTER_CENTER, [0.0, 0.0]).show(ctx, |ui| {
            ui.label(format!("{} item(s)", names.len()));
            egui::Grid::new("batch").num_columns(2).spacing([10.0, 6.0]).show(ui, |ui| {
                ui.label("Find");
                ui.add(egui::TextEdit::singleline(&mut b.find).desired_width(260.0));
                ui.end_row();
                ui.label("Replace with");
                ui.add(egui::TextEdit::singleline(&mut b.with).desired_width(260.0));
                ui.end_row();
            });
            ui.checkbox(&mut b.number, "Add a number (1, 2, 3 ...) before the extension");
            ui.add_space(4.0);
            ui.label(RichText::new("Preview").size(12.0).color(pal.dim));
            let new = crate::actions::batch_names(&names, &b.find, &b.with, b.number);
            for (o, n) in names.iter().zip(&new).take(5) {
                ui.label(RichText::new(format!("{o}  ->  {n}")).size(12.0));
            }
            if names.len() > 5 {
                ui.label(RichText::new(format!("... and {} more", names.len() - 5)).size(12.0).color(pal.dim));
            }
            ui.add_space(6.0);
            ui.horizontal(|ui| {
                apply = ui.button("Rename all").clicked();
                cancel |= ui.button("Cancel").clicked();
            });
        });
        if apply {
            let new = crate::actions::batch_names(&names, &b.find, &b.with, b.number);
            let msg = crate::actions::batch_rename(&b.paths, &new);
            self.message = Some((msg, Instant::now()));
        } else if !cancel {
            self.batch = Some(b);
        }
    }

    fn rename_window(&mut self, ctx: &egui::Context) {
        let Some((id, mut text)) = self.rename.take() else { return };
        let (mut ok, mut cancel) = (false, ctx.input(|i| i.key_pressed(egui::Key::Escape)));
        egui::Window::new("Rename").collapsible(false).resizable(false).anchor(Align2::CENTER_CENTER, [0.0, 0.0]).show(ctx, |ui| {
            let r = ui.add(egui::TextEdit::singleline(&mut text).desired_width(340.0));
            if r.lost_focus() && ctx.input(|i| i.key_pressed(egui::Key::Enter)) {
                ok = true;
            } else if !r.has_focus() && !r.lost_focus() {
                r.request_focus();
            }
            ui.add_space(6.0);
            ui.horizontal(|ui| {
                ok |= ui.button("Rename").clicked();
                cancel |= ui.button("Cancel").clicked();
            });
        });
        if ok {
            let msg = match crate::actions::rename(&self.entry_text(id, 0), &text) {
                Ok(()) => "Renamed".to_string(),
                Err(e) => e,
            };
            self.message = Some((msg, Instant::now()));
        } else if !cancel {
            self.rename = Some((id, text));
        }
    }

    fn delete_window(&mut self, ctx: &egui::Context) {
        let Some(paths) = self.confirm_delete.take() else { return };
        let (mut yes, mut cancel) = (false, ctx.input(|i| i.key_pressed(egui::Key::Escape)));
        egui::Window::new("Delete").collapsible(false).resizable(false).anchor(Align2::CENTER_CENTER, [0.0, 0.0]).show(ctx, |ui| {
            ui.label(format!("Move {} item(s) to the Recycle Bin?", paths.len()));
            ui.add_space(4.0);
            for p in paths.iter().take(5) {
                ui.label(RichText::new(p).size(12.0).color(theme::get(ctx).dim));
            }
            if paths.len() > 5 {
                ui.label(format!("... and {} more", paths.len() - 5));
            }
            ui.add_space(8.0);
            ui.horizontal(|ui| {
                if ui.add(egui::Button::new(RichText::new("Delete").color(Color32::WHITE)).fill(Color32::from_rgb(200, 60, 70))).clicked() {
                    yes = true;
                }
                cancel |= ui.button("Cancel").clicked();
            });
        });
        if yes {
            let msg = match crate::actions::recycle(&paths) {
                Ok(()) => format!("Moved {} item(s) to the Recycle Bin", paths.len()),
                Err(e) => e,
            };
            self.message = Some((msg, Instant::now()));
            self.selected.clear();
        } else if !cancel {
            self.confirm_delete = Some(paths);
        }
    }
}
