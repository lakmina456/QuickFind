//! Title bar, tabs, search card, file-type pills and the status bar (the OmniFind layout).

use super::App;
use crate::platform::thousands;
use crate::theme::{self, Pal};
use crate::widgets;
use eframe::egui::{self, pos2, vec2, Align, Align2, Color32, CornerRadius, Layout, Margin, Rect, RichText, Sense, Stroke, StrokeKind};
use std::sync::atomic::Ordering;
use std::time::Duration;

/// Pill label -> index into FILTERS.
pub(super) const PILLS: [(&str, usize); 8] =
    [("All", 0), ("Folders", 5), ("Documents", 3), ("Pictures", 6), ("Audio", 1), ("Video", 7), ("Archives", 2), ("Apps", 4)];

/// Size chips: label, search added.
pub(super) const SIZES: [(&str, &str); 4] = [("Any size", ""), ("> 10 MB", "size:>10mb"), ("> 100 MB", "size:>100mb"), ("> 1 GB", "size:>1gb")];
/// Date chips: label, search added.
pub(super) const DATES: [(&str, &str); 4] = [("Any date", ""), ("Today", "dm:today"), ("This week", "dm:thisweek"), ("This month", "dm:thismonth")];

impl App {
    /// Custom caption: logo, name, menus | theme toggle, minimize, maximize, close.
    pub(super) fn title_bar(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        let pal = theme::get(ctx);
        let rect = ui.max_rect();
        let drag = ui.interact(rect, egui::Id::new("title-drag"), Sense::click_and_drag());
        let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
        if drag.drag_started_by(egui::PointerButton::Primary) {
            ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
        }
        if drag.double_clicked() {
            ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
        }
        ui.horizontal_centered(|ui| {
            ui.spacing_mut().item_spacing.x = 0.0;
            ui.add_space(14.0);
            let (r, _) = ui.allocate_exact_size(vec2(24.0, 24.0), Sense::hover());
            ui.painter().rect_filled(r, 7.0, pal.accent_fill);
            ui.painter().text(r.center(), Align2::CENTER_CENTER, "\u{E721}", theme::icons(12.0), pal.on_accent);
            ui.add_space(9.0);
            ui.label(RichText::new("OmniFind").font(theme::semibold(15.0)).color(pal.text));
            ui.add_space(14.0);
            self.menus(ui, ctx);
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                ui.spacing_mut().item_spacing.x = 0.0;
                let size = vec2(46.0, 44.0);
                let hov = pal.hover;
                if caption(ui, &pal, "\u{E8BB}", size, pal.danger, true, "Close").clicked() {
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
                let (glyph, tip) = if maximized { ("\u{E923}", "Restore") } else { ("\u{E922}", "Maximize") };
                if caption(ui, &pal, glyph, size, hov, false, tip).clicked() {
                    ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
                }
                if caption(ui, &pal, "\u{E921}", size, hov, false, "Minimize").clicked() {
                    ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
                }
                let (g, tip) = if self.settings.dark { ("\u{E706}", "Switch to light theme") } else { ("\u{E708}", "Switch to dark theme") };
                if caption(ui, &pal, g, size, hov, false, tip).clicked() {
                    self.settings.dark = !self.settings.dark;
                    theme::apply(ctx, self.settings.dark, &self.settings.accent);
                    self.save_settings();
                }
            });
        });
    }

    /// Search tabs and the Inspector toggle.
    pub(super) fn tab_bar(&mut self, ui: &mut egui::Ui, pal: &Pal) {
        let (mut go, mut close, mut add) = (None, None, false);
        let n = self.tabs.len();
        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 4.0;
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                let on = self.settings.preview;
                let look = widgets::Look {
                    fill: if on { pal.accent_soft } else { pal.panel },
                    fill_hover: if on { pal.accent_soft } else { pal.hover },
                    stroke: Stroke::new(1.0_f32, if on { pal.accent } else { pal.line }),
                    text: if on { pal.accent_ink } else { pal.text },
                    text_hover: if on { pal.accent_ink } else { pal.text },
                    font: theme::semibold(12.5),
                    radius: 10,
                    height: 34.0,
                    pad_x: 14.0,
                    min_w: 0.0,
                };
                if widgets::button(ui, "\u{25A5}  Inspector", &look).on_hover_text("Show or hide inspector").clicked() {
                    self.settings.preview = !self.settings.preview;
                    self.save_settings();
                }
                ui.with_layout(Layout::left_to_right(Align::Center), |ui| {
                    ui.spacing_mut().item_spacing.x = 4.0;
                    for i in 0..n {
                        let text = if i == self.tab { self.query.clone() } else { self.tabs[i].query.clone() };
                        let mut label: String = text.trim().chars().take(22).collect();
                        if label.is_empty() {
                            label = "Search".into();
                        }
                        let active = i == self.tab;
                        let look = widgets::Look {
                            fill: if active { pal.panel } else { Color32::TRANSPARENT },
                            fill_hover: if active { pal.panel } else { Color32::TRANSPARENT },
                            stroke: Stroke::new(1.0_f32, if active { pal.line } else { Color32::TRANSPARENT }),
                            text: if active { pal.accent_ink } else { pal.dim },
                            text_hover: if active { pal.accent_ink } else { pal.text },
                            font: if active { theme::semibold(13.0) } else { theme::medium(13.0) },
                            radius: 10,
                            height: 34.0,
                            pad_x: 14.0,
                            min_w: 0.0,
                        };
                        if widgets::button(ui, &label, &look).clicked() {
                            go = Some(i);
                        }
                        if n > 1 && widgets::icon_button(ui, pal, "\u{E711}", vec2(22.0, 22.0), pal.hover, "Close tab (Ctrl+W)").clicked() {
                            close = Some(i);
                        }
                    }
                    if widgets::icon_button(ui, pal, "\u{E710}", vec2(30.0, 30.0), pal.hover, "New tab (Ctrl+T)").clicked() {
                        add = true;
                    }
                });
            });
        });
        if let Some(i) = close {
            self.close_tab(i);
        } else if add {
            self.new_tab();
        } else if let Some(i) = go {
            self.switch_tab(i);
        }
    }

    /// The big rounded search card: input row, option toggles and the Size / Modified chips.
    pub(super) fn search_card(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        let pal = theme::get(ctx);
        let search_id = egui::Id::new("search-box");
        let focused = ctx.memory(|m| m.has_focus(search_id));
        let mut changed = false;
        let ring = ui.painter().add(egui::Shape::Noop);
        let outer = Rect::from_min_size(ui.cursor().min, vec2(0.0, 0.0));
        let _ = outer;
        let shadow = egui::Shadow { offset: [0, 8], blur: 30, spread: 0, color: Color32::from_rgba_unmultiplied(0x14, 0x23, 0x46, if pal.dark { 90 } else { 36 }) };
        let inner = egui::Frame::new().inner_margin(Margin::same(4)).show(ui, |ui| {
            let width = ui.available_width();
            widgets::card(&pal, 16).shadow(shadow).show(ui, |ui| {
                ui.set_width(width - 2.0);
                ui.spacing_mut().item_spacing = vec2(0.0, 0.0);
                // --- search row
                egui::Frame::new().inner_margin(Margin { left: 20, right: 10, top: 6, bottom: 6 }).show(ui, |ui| {
                    ui.set_min_height(48.0);
                    ui.horizontal(|ui| {
                        ui.spacing_mut().item_spacing.x = 0.0;
                        ui.label(RichText::new("\u{E721}").font(theme::icons(18.0)).color(pal.accent));
                        ui.add_space(10.0);
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            // option toggles (Aa Word Path é .*)
                            egui::Frame::new()
                                .fill(pal.panel2)
                                .stroke(Stroke::new(1.0_f32, pal.line2))
                                .corner_radius(11)
                                .inner_margin(Margin::same(3))
                                .show(ui, |ui| {
                                    ui.horizontal(|ui| {
                                        ui.spacing_mut().item_spacing.x = 2.0;
                                        let o = &mut self.opts;
                                        for (label, tip, flag) in [
                                            (".*", "Regex (Ctrl+R)", &mut o.regex),
                                            ("\u{e9}", "Diacritics (Ctrl+M)", &mut o.diacritics),
                                            ("Path", "Match path (Ctrl+U)", &mut o.path),
                                            ("Word", "Whole words (Ctrl+B)", &mut o.whole_word),
                                            ("Aa", "Match case (Ctrl+I)", &mut o.case),
                                        ] {
                                            if widgets::option(ui, &pal, label, *flag).on_hover_text(tip).clicked() {
                                                *flag = !*flag;
                                                changed = true;
                                            }
                                        }
                                    });
                                });
                            ui.add_space(8.0);
                            let label = self.match_count_label();
                            ui.add_sized(vec2(110.0, 20.0), egui::Label::new(RichText::new(label).font(theme::regular(13.0)).color(pal.dim)));
                            ui.add_space(4.0);
                            if widgets::icon_button(ui, &pal, "\u{E734}", vec2(30.0, 30.0), pal.hover, "Bookmark this search (Ctrl+D)").clicked() {
                                crate::bookmarks::add(&mut self.bookmarks, &self.query, self.filter, crate::filters::FILTERS[self.filter].0);
                                self.message = Some(("Search bookmarked".into(), std::time::Instant::now()));
                            }
                            if !self.query.is_empty() && widgets::icon_button(ui, &pal, "\u{E711}", vec2(30.0, 30.0), pal.hover, "Clear search").clicked() {
                                self.query.clear();
                                self.focus_search = true;
                            }
                            let edit = egui::TextEdit::singleline(&mut self.query)
                                .hint_text(RichText::new("Search files and folders").font(theme::regular(18.0)).color(pal.dim))
                                .font(theme::regular(18.0))
                                .text_color(pal.text)
                                .frame(false)
                                .desired_width(f32::INFINITY)
                                .margin(Margin::symmetric(0, 8))
                                .id(search_id);
                            let resp = ui.add(edit);
                            if self.focus_search {
                                resp.request_focus();
                                self.focus_search = false;
                            }
                            if resp.gained_focus() || resp.changed() {
                                self.results_focus = false;
                            }
                        });
                    });
                });
                // --- refine row
                let refine = egui::Frame::new()
                    .fill(pal.panel2)
                    .corner_radius(CornerRadius { nw: 0, ne: 0, sw: 15, se: 15 })
                    .inner_margin(Margin::symmetric(20, 10))
                    .show(ui, |ui| {
                        ui.set_min_width(ui.available_width());
                        ui.horizontal(|ui| {
                            ui.spacing_mut().item_spacing.x = 8.0;
                            ui.label(RichText::new("Size").font(theme::semibold(12.5)).color(pal.dim));
                            for i in 1..SIZES.len() {
                                if widgets::chip(ui, &pal, SIZES[i].0, self.size_f == i).on_hover_text(SIZES[i].1).clicked() {
                                    self.size_f = if self.size_f == i { 0 } else { i };
                                }
                            }
                            let (r, _) = ui.allocate_exact_size(vec2(1.0, 20.0), Sense::hover());
                            ui.painter().rect_filled(r, 0.0, pal.line);
                            ui.label(RichText::new("Modified").font(theme::semibold(12.5)).color(pal.dim));
                            for i in 1..DATES.len() {
                                if widgets::chip(ui, &pal, DATES[i].0, self.date_f == i).on_hover_text(DATES[i].1).clicked() {
                                    self.date_f = if self.date_f == i { 0 } else { i };
                                }
                            }
                            if self.size_f != 0 || self.date_f != 0 {
                                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                    let link = egui::Button::new(RichText::new("Clear filters").font(theme::medium(12.5)).color(pal.accent)).frame(false);
                                    if ui.add(link).clicked() {
                                        self.size_f = 0;
                                        self.date_f = 0;
                                    }
                                });
                            }
                        });
                    });
                ui.painter().hline(refine.response.rect.x_range(), refine.response.rect.top(), Stroke::new(1.0_f32, pal.line2));
            });
        });
        let rect = inner.response.rect;
        let color = if focused { pal.accent_soft } else { Color32::TRANSPARENT };
        ui.painter().set(ring, egui::Shape::rect_stroke(rect.shrink(2.0), 18.0, Stroke::new(4.0_f32, color), StrokeKind::Middle));
        if changed {
            self.save_settings();
        }
    }

    /// File-type segmented pills and the row-density buttons.
    pub(super) fn pills_row(&mut self, ui: &mut egui::Ui) {
        let pal = theme::get(ui.ctx());
        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 0.0;
            let seg = |ui: &mut egui::Ui, add: &mut dyn FnMut(&mut egui::Ui)| {
                egui::Frame::new()
                    .fill(pal.panel)
                    .stroke(Stroke::new(1.0_f32, pal.line))
                    .corner_radius(12)
                    .inner_margin(Margin::same(3))
                    .show(ui, |ui| {
                        ui.horizontal(|ui| {
                            ui.spacing_mut().item_spacing.x = 6.0;
                            add(ui)
                        });
                    });
            };
            seg(ui, &mut |ui| {
                for (label, idx) in PILLS {
                    if widgets::segment(ui, &pal, label, self.filter == idx, 0.0).clicked() {
                        self.filter = idx;
                    }
                }
            });
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                seg(ui, &mut |ui| {
                    ui.spacing_mut().item_spacing.x = 2.0;
                    // right_to_left parent: draw compact first so comfortable ends up on the left
                    if widgets::segment(ui, &pal, "\u{2630}", self.settings.compact, 36.0).on_hover_text("Compact rows").clicked() {
                        self.settings.compact = true;
                        self.save_settings();
                    }
                    if widgets::segment(ui, &pal, "\u{25A4}", !self.settings.compact, 36.0).on_hover_text("Comfortable rows").clicked() {
                        self.settings.compact = false;
                        self.save_settings();
                    }
                });
            });
        });
    }

    pub(super) fn indexed_count(&self) -> u64 {
        let n = crate::search::INDEXED.load(Ordering::Relaxed);
        if self.progress.running.load(Ordering::Relaxed) { n.max(self.progress.found.load(Ordering::Relaxed)) } else { n }
    }

    pub(super) fn has_active_search(&self) -> bool {
        !self.query.trim().is_empty() || self.filter != 0 || self.size_f != 0 || self.date_f != 0 || self.location.is_some() || self.settings.content_search
    }

    fn match_count_label(&self) -> String {
        if self.has_active_search() {
            if self.ids.len() == 1 { "1 result".into() } else { format!("{} results", thousands(self.ids.len() as u64)) }
        } else {
            format!("{} items", thousands(self.indexed_count().max(self.ids.len() as u64)))
        }
    }

    fn status_left(&self) -> String {
        let indexed = self.indexed_count();
        let running = self.progress.running.load(Ordering::Relaxed);
        let loaded = self.index.read().unwrap().ready;
        if let Some((m, at)) = &self.message {
            if at.elapsed() < Duration::from_secs(5) {
                return m.clone();
            }
        }
        if let Some(e) = &self.error {
            return e.clone();
        }
        let sel = self.selected.len();
        if indexed == 0 && running {
            "Indexing\u{2026}".into()
        } else if indexed == 0 && !loaded {
            "Loading index\u{2026}".into()
        } else if sel == 1 {
            "1 item selected".into()
        } else if sel > 1 {
            let mut t = format!("{} items selected", thousands(sel as u64));
            if sel <= 20_000 {
                let idx = self.index.read().unwrap();
                let total: u64 = self.selected.iter().filter_map(|i| idx.entries.get(*i as usize)).filter(|e| !e.is_dir()).map(|e| e.size).sum();
                if total > 0 {
                    t.push_str(&format!("  \u{b7}  {}", crate::platform::human_size(total)));
                }
            }
            t
        } else if self.has_active_search() {
            format!("{} of {} items", thousands(self.ids.len() as u64), thousands(indexed))
        } else if self.ids.len() == 1 {
            "1 item".into()
        } else {
            format!("{} items", thousands(self.ids.len().max(indexed as usize) as u64))
        }
    }

    pub(super) fn status(&self, ui: &mut egui::Ui) {
        let pal = theme::get(ui.ctx());
        ui.horizontal_centered(|ui| {
            ui.label(RichText::new(self.status_left()).font(theme::regular(12.5)).color(pal.dim));
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                if self.progress.running.load(Ordering::Relaxed) {
                    let found = self.progress.found.load(Ordering::Relaxed);
                    ui.label(RichText::new(format!("{} found", thousands(found))).font(theme::medium(12.5)).color(pal.text));
                    ui.add_space(10.0);
                    progress_bar(ui, &pal, self.progress_fraction(found));
                    ui.add_space(10.0);
                    let cur = self.progress.current.lock().map(|c| c.clone()).unwrap_or_default();
                    let label = if cur.is_empty() {
                        "Indexing\u{2026}".to_string()
                    } else {
                        let short: String = cur.chars().take(42).collect();
                        format!("Indexing {short}{}", if cur.chars().count() > 42 { "\u{2026}" } else { "" })
                    };
                    ui.label(RichText::new(label).font(theme::regular(12.5)).color(pal.dim));
                    ui.add_space(10.0);
                    let t = ui.ctx().input(|i| i.time) as f32;
                    let (r, _) = ui.allocate_exact_size(vec2(8.0, 8.0), Sense::hover());
                    let a = 0.35 + 0.65 * (0.5 + 0.5 * (t * std::f32::consts::TAU / 2.2).cos());
                    ui.painter().circle_filled(r.center(), 4.0, pal.accent.gamma_multiply(a));
                } else {
                    let indexed = self.indexed_count();
                    let text = if indexed > 0 {
                        format!("{} indexed  {}", thousands(indexed), crate::startup::MODE.lock().map(|m| *m).unwrap_or(""))
                    } else {
                        "Waiting for index\u{2026}".to_string()
                    };
                    ui.label(RichText::new(text).font(theme::regular(12.5)).color(pal.dim));
                }
            });
        });
    }

    /// 0..1 guess at how far the scan is (relative to the previous index, else a soft curve).
    fn progress_fraction(&self, found: u64) -> f32 {
        let prev = self.prev_total.max(1) as f32;
        if self.prev_total > 0 {
            (found as f32 / prev).min(0.97)
        } else {
            1.0 - (-(found as f32) / 1_500_000.0).exp()
        }
    }
}

fn progress_bar(ui: &mut egui::Ui, pal: &Pal, fraction: f32) {
    let (r, _) = ui.allocate_exact_size(vec2(84.0, 5.0), Sense::hover());
    ui.painter().rect_filled(r, 3.0, pal.line);
    let w = (r.width() * fraction.clamp(0.02, 1.0)).max(4.0);
    ui.painter().rect_filled(Rect::from_min_size(r.min, vec2(w, 5.0)), 3.0, pal.accent);
}

/// One caption button (46x44): glyph, hover fill (red for close).
fn caption(ui: &mut egui::Ui, pal: &Pal, glyph: &str, size: egui::Vec2, hover: Color32, close: bool, tip: &str) -> egui::Response {
    let (rect, resp) = ui.allocate_exact_size(size, Sense::click());
    let hovered = resp.hovered();
    if hovered {
        ui.painter().rect_filled(rect, 0.0, hover);
    }
    let color = if hovered && close { Color32::WHITE } else if hovered { pal.text } else { pal.dim };
    ui.painter().text(pos2(rect.center().x, rect.center().y), Align2::CENTER_CENTER, glyph, theme::icons(10.0), color);
    resp.on_hover_text(tip)
}
