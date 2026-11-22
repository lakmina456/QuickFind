//! The frame layout: title bar, sidebar, main column (tabs, search card, pills, results | inspector).

use super::App;
use crate::table::{RowAction, TableView};
use crate::theme;
use crate::widgets;
use eframe::egui::{self, pos2, vec2, Color32, Margin, Rect, RichText, Stroke, StrokeKind, UiBuilder};
use std::sync::atomic::Ordering;

impl App {
    /// Re-read the drive list shown in the sidebar.
    pub(super) fn refresh_drives(&mut self) {
        self.drive_labels = crate::scan::active_roots().into_iter().map(|r| (r.clone(), crate::sysinfo::volume_label(&r))).collect();
    }

    /// Title and message for the empty results card, following the original's rules.
    fn overlay(&self) -> Option<(String, String)> {
        if !self.ids.is_empty() || self.shown_serial < self.serial {
            return None;
        }
        let indexed = self.indexed_count();
        let running = self.progress.running.load(Ordering::Relaxed);
        let loaded = self.index.read().unwrap().ready;
        let q = self.query.trim();
        if running && indexed == 0 {
            return Some(("Indexing your drives".into(), "QuickFind is building a fast local catalog. Search will light up as soon as the first pass finishes.".into()));
        }
        if !loaded && indexed == 0 {
            return Some(("Loading index".into(), "Reading the cached catalog from disk\u{2026}".into()));
        }
        if indexed > 0 {
            return Some(if self.opts.regex && !q.is_empty() && self.error.is_some() {
                ("That regular expression isn't valid".into(), "Check for an unclosed bracket or group, or turn off .* to search for the text literally.".into())
            } else if !q.is_empty() {
                (format!("No matches for \u{201c}{q}\u{201d}"), "Try fewer characters, or turn on Path to match folder names too.".into())
            } else {
                ("Nothing matches these filters".into(), "Try a wider size or date range, or another file type.".into())
            });
        }
        if !running && loaded && indexed == 0 {
            return Some(("No index yet".into(), "Use Tools \u{2192} Rebuild Index if scanning never starts.".into()));
        }
        None
    }

    pub(super) fn layout(&mut self, ctx: &egui::Context) {
        let pal = theme::get(ctx);
        if !self.progress.running.load(Ordering::Relaxed) {
            self.prev_total = self.indexed_count();
        }
        egui::TopBottomPanel::top("title")
            .exact_height(44.0)
            .show_separator_line(false)
            .frame(egui::Frame::new().fill(pal.panel))
            .show(ctx, |ui| {
                self.title_bar(ui, ctx);
                let r = ui.max_rect();
                ui.painter().hline(r.x_range(), r.bottom() - 0.5, Stroke::new(1.0_f32, pal.line));
            });
        if self.status_bar {
            egui::TopBottomPanel::bottom("status")
                .exact_height(34.0)
                .show_separator_line(false)
                .frame(egui::Frame::new().fill(pal.panel).inner_margin(Margin::symmetric(20, 0)))
                .show(ctx, |ui| {
                    let r = ui.max_rect();
                    ui.painter().hline(r.x_range().expand(20.0), r.top() - 0.5, Stroke::new(1.0_f32, pal.line));
                    self.status(ui);
                });
        }
        egui::SidePanel::left("sidebar")
            .exact_width(236.0)
            .resizable(false)
            .show_separator_line(false)
            .frame(egui::Frame::new().fill(pal.bg).inner_margin(Margin { left: 12, right: 12, top: 14, bottom: 12 }))
            .show(ctx, |ui| self.sidebar(ui, ctx));

        let mut actions = Vec::new();
        let terms = self.highlight_terms();
        egui::CentralPanel::default().frame(egui::Frame::new().fill(pal.bg).inner_margin(Margin { left: 8, right: 20, top: 16, bottom: 16 })).show(ctx, |ui| {
            self.tab_bar(ui, &pal);
            ui.add_space(12.0);
            self.search_card(ui, ctx);
            ui.add_space(8.0);
            self.pills_row(ui);
            ui.add_space(12.0);
            let rect = ui.available_rect_before_wrap();
            let (insp_w, gap) = if self.settings.preview { (340.0, 12.0) } else { (0.0, 0.0) };
            let left = Rect::from_min_max(rect.min, pos2(rect.max.x - insp_w - gap, rect.max.y));
            let right = Rect::from_min_max(pos2(rect.max.x - insp_w, rect.min.y), rect.max);
            let overlay = self.overlay();

            ui.allocate_new_ui(UiBuilder::new().max_rect(left), |ui| {
                let card = widgets::card(&pal, 16).show(ui, |ui| {
                    ui.set_min_size(left.size() - vec2(2.0, 2.0));
                    ui.set_max_size(left.size() - vec2(2.0, 2.0));
                    if let Some((title, message)) = &overlay {
                        ui.vertical_centered(|ui| {
                            ui.add_space((left.height() * 0.5 - 130.0).max(20.0));
                            let (r, _) = ui.allocate_exact_size(vec2(48.0, 48.0), egui::Sense::hover());
                            ui.painter().rect_filled(r, 14.0, pal.accent_soft);
                            ui.painter().text(r.center(), egui::Align2::CENTER_CENTER, "\u{E721}", theme::icons(20.0), pal.accent);
                            ui.add_space(16.0);
                            ui.label(RichText::new(title).font(theme::semibold(20.0)).color(pal.text));
                            ui.add_space(10.0);
                            ui.add_sized(vec2(440.0_f32.min(left.width() - 40.0), 44.0), egui::Label::new(RichText::new(message).font(theme::regular(13.5)).color(pal.dim)).wrap());
                            ui.add_space(14.0);
                            if widgets::action(ui, &pal, "Clear search and filters", None).clicked() {
                                self.query.clear();
                                self.filter = 0;
                                self.size_f = 0;
                                self.date_f = 0;
                                self.location = None;
                                self.settings.content_search = false;
                            }
                        });
                    } else {
                        let idx = self.index.read().unwrap();
                        let ids: &[u32] = if self.ids_epoch == idx.epoch { &self.ids } else { &[] };
                        let inner = ui.max_rect();
                        ui.set_clip_rect(ui.clip_rect().intersect(inner));
                        let view = TableView {
                            index: &idx,
                            ids,
                            selected: &self.selected,
                            sort: self.sort,
                            ascending: self.ascending,
                            columns: self.columns,
                            scroll_to: self.scroll_to.take(),
                            pal,
                            row_h: if self.settings.compact { 34.0 } else { 48.0 },
                            terms: &terms,
                            icons: &self.icons,
                        };
                        view.show(ui, &mut actions);
                    }
                });
                let _ = card;
            });
            if self.settings.preview {
                ui.allocate_new_ui(UiBuilder::new().max_rect(right), |ui| {
                    widgets::card(&pal, 16).inner_margin(Margin::symmetric(18, 16)).show(ui, |ui| {
                        ui.set_min_size(right.size() - vec2(38.0, 34.0));
                        ui.set_max_size(right.size() - vec2(38.0, 34.0));
                        self.inspector(ui);
                    });
                });
            }
        });
        for a in actions {
            self.apply(ctx, a);
        }
        // 1px window border (the window has no system frame)
        if !ctx.input(|i| i.viewport().maximized.unwrap_or(false)) {
            let p = ctx.layer_painter(egui::LayerId::new(egui::Order::Foreground, egui::Id::new("window-border")));
            p.rect_stroke(ctx.screen_rect().shrink(0.5), 0.0, Stroke::new(1.0_f32, pal.line), StrokeKind::Inside);
        }
        let _ = Color32::WHITE;
    }
}

#[allow(dead_code)]
fn _unused(_: RowAction) {}
