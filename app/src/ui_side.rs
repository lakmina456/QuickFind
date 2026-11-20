//! Left sidebar: locations with item counts, saved searches and the Index settings menu.

use super::App;
use crate::platform::thousands;
use crate::theme::{self, Pal};
use eframe::egui::{self, pos2, vec2, Align, Align2, CursorIcon, Layout, Margin, RichText, Sense};
use std::sync::atomic::Ordering;

impl App {
    pub(super) fn sidebar(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        let pal = theme::get(ctx);
        ui.with_layout(Layout::bottom_up(Align::Min), |ui| {
            ui.add_space(0.0);
            self.index_settings_link(ui, ctx, &pal);
            ui.with_layout(Layout::top_down(Align::Min), |ui| {
                egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
                    ui.spacing_mut().item_spacing.y = 2.0;
                    ui.add_space(0.0);
                    ui.label(RichText::new("Locations").font(theme::semibold(12.5)).color(pal.dim));
                    ui.add_space(4.0);
                    let counts = *crate::search::DRIVE_COUNTS.lock().unwrap();
                    let total = crate::search::INDEXED.load(Ordering::Relaxed);
                    let all_text = if total > 0 { thousands(total) } else { String::new() };
                    if nav_row(ui, &pal, "\u{25A5}", "All locations", &all_text, self.location.is_none()).clicked() {
                        self.location = None;
                    }
                    let labels = self.drive_labels.clone();
                    for (root, label) in labels {
                        let letter = root.as_bytes().first().map(|b| b.to_ascii_lowercase()).unwrap_or(b'c');
                        let n = counts.get((letter.wrapping_sub(b'a')) as usize).copied().unwrap_or(0);
                        let text = if n > 0 { thousands(n) } else { String::new() };
                        let key = root.to_ascii_lowercase();
                        let on = self.location.as_deref() == Some(key.as_str());
                        if nav_row(ui, &pal, "\u{25A3}", &label, &text, on).clicked() {
                            self.location = Some(key);
                        }
                    }
                    ui.add_space(14.0);
                    ui.label(RichText::new("Saved searches").font(theme::semibold(12.5)).color(pal.dim));
                    ui.add_space(4.0);
                    let marks = self.bookmarks.clone();
                    let mut open = None;
                    for b in &marks {
                        if nav_row(ui, &pal, "\u{2606}", &b.name, "", false).clicked() {
                            open = Some(b.clone());
                        }
                    }
                    if let Some(b) = open {
                        self.query = b.query;
                        self.filter = b.filter;
                        self.focus_search = true;
                    }
                    if marks.is_empty() {
                        ui.add(
                            egui::Label::new(RichText::new("Bookmark a search with the star in the search bar.").font(theme::regular(11.5)).color(pal.dim))
                                .wrap(),
                        );
                    }
                });
            });
        });
    }

    /// "Index settings" link with a menu of indexing options.
    fn index_settings_link(&mut self, ui: &mut egui::Ui, ctx: &egui::Context, pal: &Pal) {
        let (rect, resp) = ui.allocate_exact_size(vec2(ui.available_width(), 36.0), Sense::click());
        if resp.hovered() {
            ui.painter().rect_filled(rect, 10.0, pal.hover);
        }
        let color = if resp.hovered() { pal.text } else { pal.dim };
        ui.painter().text(pos2(rect.left() + 20.0, rect.center().y), Align2::CENTER_CENTER, "\u{E713}", theme::icons(14.0), color);
        ui.painter().text(pos2(rect.left() + 38.0, rect.center().y), Align2::LEFT_CENTER, "Index settings", theme::regular(13.0), color);
        let resp = resp.on_hover_cursor(CursorIcon::PointingHand);
        let id = egui::Id::new("index-settings-popup");
        if resp.clicked() {
            ui.memory_mut(|m| m.toggle_popup(id));
        }
        egui::popup::popup_above_or_below_widget(ui, id, &resp, egui::AboveOrBelow::Above, egui::PopupCloseBehavior::CloseOnClickOutside, |ui| {
            ui.set_min_width(300.0);
            self.index_menu(ui, ctx);
        });
        ui.add_space(8.0);
    }

    /// Rebuild + indexing toggles (used by the sidebar link and the Tools menu).
    pub(super) fn index_menu(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        if ui.button("Rebuild index").clicked() {
            self.settings.apply_rules();
            crate::startup::rebuild(self.index.clone(), self.progress.clone(), ctx.clone());
            ui.close_menu();
        }
        ui.separator();
        let mut ch = false;
        ch |= ui.checkbox(&mut self.settings.junk, "Exclude system junk (Temp, Recycle Bin, ~$\u{2026})").changed();
        ch |= ui.checkbox(&mut self.settings.include_extra, "Index USB / removable and network drives").changed();
        if ch {
            self.settings.apply_rules();
            self.refresh_drives();
            self.save_settings();
            self.message = Some(("Rebuild the index to apply".into(), std::time::Instant::now()));
        }
    }
}

/// One sidebar row: glyph, label, count (mono). Selected rows use the soft accent.
fn nav_row(ui: &mut egui::Ui, pal: &Pal, glyph: &str, label: &str, count: &str, selected: bool) -> egui::Response {
    let (rect, resp) = ui.allocate_exact_size(vec2(ui.available_width(), 36.0), Sense::click());
    if selected {
        ui.painter().rect_filled(rect, 10.0, pal.accent_soft);
    } else if resp.hovered() {
        ui.painter().rect_filled(rect, 10.0, pal.hover);
    }
    let color = if selected { pal.accent_ink } else { pal.text };
    ui.painter().text(pos2(rect.left() + 19.0, rect.center().y), Align2::CENTER_CENTER, glyph, theme::regular(12.0), color);
    let font = if selected { theme::semibold(13.0) } else { theme::regular(13.0) };
    let mut right = rect.right() - 10.0;
    if !count.is_empty() {
        let g = ui.painter().layout_no_wrap(count.to_string(), theme::mono(12.0), pal.dim.gamma_multiply(0.8));
        right -= g.size().x;
        ui.painter().galley(pos2(right, rect.center().y - g.size().y / 2.0), g, pal.dim);
        right -= 8.0;
    }
    let galley = ui.painter().layout(label.to_string(), font, color, (right - (rect.left() + 38.0)).max(20.0));
    let y = rect.center().y - galley.size().y / 2.0;
    ui.painter().with_clip_rect(egui::Rect::from_min_max(pos2(rect.left() + 38.0, rect.top()), pos2(right, rect.bottom()))).galley(
        pos2(rect.left() + 38.0, y),
        galley,
        color,
    );
    let _ = Margin::ZERO;
    resp.on_hover_cursor(CursorIcon::PointingHand)
}
