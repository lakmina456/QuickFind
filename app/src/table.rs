//! The results grid: header, rows, icons, match highlighting and the row context menu.

use crate::dates::format_filetime;
use crate::filters::{kind_of, Kind};
use crate::index::{Entry, Index};
use crate::platform::{attrib_text, size_text};
use crate::search::SortKey;
use crate::theme::{self, Pal};
use eframe::egui::{
    self, text::LayoutJob, Align, Color32, CornerRadius, Layout, Rect, RichText, Sense, Shape, Stroke, TextFormat, Vec2,
};
use egui_extras::{Column, TableBuilder};

#[derive(Clone, Copy)]
pub struct Columns {
    pub path: bool,
    pub size: bool,
    pub extension: bool,
    pub modified: bool,
    pub created: bool,
    pub attributes: bool,
}

impl Default for Columns {
    fn default() -> Self {
        Columns { path: true, size: true, extension: false, modified: true, created: false, attributes: false }
    }
}

pub enum RowAction {
    Click { row: usize, id: u32, ctrl: bool, shift: bool },
    Open(u32),
    OpenWith(u32),
    OpenPath(u32),
    CopyFull(u32),
    CopyName(u32),
    CopyPath(u32),
    Rename(u32),
    Delete(u32),
    Zip(u32),
    CopyFiles(u32),
    BatchRename(u32),
    CopyTo(u32),
    MoveTo(u32),
    Properties(u32),
    Sort(SortKey),
}

pub struct TableView<'a> {
    pub index: &'a Index,
    pub ids: &'a [u32],
    pub selected: &'a std::collections::HashSet<u32>,
    pub sort: SortKey,
    pub ascending: bool,
    pub columns: Columns,
    pub scroll_to: Option<usize>,
    pub pal: Pal,
    pub row_h: f32,
    /// Lower-case text pieces of the search, highlighted inside names.
    pub terms: &'a [String],
    pub icons: &'a std::cell::RefCell<crate::icons::IconCache>,
}

struct Col {
    key: SortKey,
    title: &'static str,
    width: f32,
    min: f32,
    right: bool,
}

impl<'a> TableView<'a> {
    pub fn show(&self, ui: &mut egui::Ui, actions: &mut Vec<RowAction>) {
        let c = self.columns;
        // Name and Location share the free width 1.2 : 1.8, like the original's star columns.
        let fixed = 24.0 + if c.size { 100.0 } else { 0.0 } + if c.extension { 64.0 } else { 0.0 }
            + if c.modified { 152.0 } else { 0.0 } + if c.created { 152.0 } else { 0.0 } + if c.attributes { 64.0 } else { 0.0 };
        let flex = (ui.available_width() - fixed - 152.0).max(420.0);
        let (name_w, loc_w) = if c.path { (flex * 0.4, flex * 0.6) } else { (flex, 0.0) };
        let mut cols = vec![Col { key: SortKey::Name, title: "Name", width: name_w, min: 120.0, right: false }];
        if c.path {
            cols.push(Col { key: SortKey::Path, title: "Location", width: loc_w, min: 100.0, right: false });
        }
        if c.size {
            cols.push(Col { key: SortKey::Size, title: "Size", width: 100.0, min: 88.0, right: true });
        }
        if c.extension {
            cols.push(Col { key: SortKey::Extension, title: "Ext", width: 64.0, min: 56.0, right: false });
        }
        if c.modified {
            cols.push(Col { key: SortKey::Modified, title: "Modified", width: 152.0, min: 140.0, right: false });
        }
        if c.created {
            cols.push(Col { key: SortKey::Created, title: "Created", width: 152.0, min: 140.0, right: false });
        }
        if c.attributes {
            cols.push(Col { key: SortKey::Attributes, title: "Attr", width: 64.0, min: 56.0, right: false });
        }
        let n_cols = cols.len();
        ui.spacing_mut().item_spacing = Vec2::ZERO;
        let table_x = ui.max_rect().x_range();
        let mut table = TableBuilder::new(ui)
            .striped(false)
            .resizable(true)
            .sense(Sense::click())
            .cell_layout(Layout::left_to_right(Align::Center))
            .min_scrolled_height(0.0)
            .auto_shrink([false, false]);
        for (i, col) in cols.iter().enumerate() {
            let spec = if i + 1 == n_cols { Column::remainder().at_least(col.min) } else { Column::initial(col.width).at_least(col.min) };
            table = table.column(spec.clip(true));
        }
        if let Some(r) = self.scroll_to {
            table = table.scroll_to_row(r, None);
        }
        let pal = self.pal;
        table
            .header(42.0, |mut header| {
                for (i, col) in cols.iter().enumerate() {
                    header.col(|ui| {
                        let rect = ui.max_rect();
                        let radius = CornerRadius {
                            nw: if i == 0 { 15 } else { 0 },
                            ne: if i + 1 == n_cols { 15 } else { 0 },
                            sw: 0,
                            se: 0,
                        };
                        ui.painter().rect_filled(rect, radius, pal.panel2);
                        let resp = ui.interact(rect, ui.id().with(col.title), Sense::click());
                        ui.painter().hline(rect.x_range(), rect.bottom() - 0.5, Stroke::new(1.0_f32, pal.line));
                        let sorted = col.key == self.sort;
                        let color = if resp.hovered() || sorted { pal.text } else { pal.dim };
                        let layout = if col.right { Layout::right_to_left(Align::Center) } else { Layout::left_to_right(Align::Center) };
                        ui.with_layout(layout, |ui| {
                            ui.add_space(if col.right { 18.0 } else { 14.0 });
                            ui.label(RichText::new(col.title).font(theme::semibold(12.5)).color(color));
                            if sorted {
                                ui.add_space(6.0);
                                let (r, _) = ui.allocate_exact_size(Vec2::new(8.0, 5.0), Sense::hover());
                                let pts = if self.ascending {
                                    vec![r.left_bottom(), r.right_bottom(), egui::pos2(r.center().x, r.top())]
                                } else {
                                    vec![r.left_top(), r.right_top(), egui::pos2(r.center().x, r.bottom())]
                                };
                                ui.painter().add(Shape::convex_polygon(pts, pal.accent, Stroke::NONE));
                            }
                        });
                        if resp.clicked() {
                            actions.push(RowAction::Sort(col.key));
                        }
                    });
                }
            })
            .body(|body| {
                body.rows(self.row_h, self.ids.len(), |mut row| {
                    let r = row.index();
                    let id = self.ids[r];
                    let Some(e) = self.index.entries.get(id as usize) else { return };
                    let selected = self.selected.contains(&id);
                    for (i, col) in cols.iter().enumerate() {
                        row.col(|ui| {
                            self.paint_row_bg(ui, i, n_cols, selected, table_x);
                            self.cell(ui, col, e);
                        });
                    }
                    let resp = row.response();
                    if resp.double_clicked() {
                        actions.push(RowAction::Open(id));
                    } else if resp.clicked() || resp.secondary_clicked() {
                        let m = resp.ctx.input(|i| i.modifiers);
                        if !(resp.secondary_clicked() && selected) {
                            actions.push(RowAction::Click { row: r, id, ctrl: m.command, shift: m.shift });
                        }
                    }
                    resp.context_menu(|ui| context_menu(ui, id, actions));
                });
            });
    }

    /// Rounded selection / hover band across the row (4px side margin, like the original's rows).
    fn paint_row_bg(&self, ui: &mut egui::Ui, i: usize, n: usize, selected: bool, table_x: egui::Rangef) {
        let r = ui.max_rect();
        let hovered = ui.input(|inp| inp.pointer.hover_pos().is_some_and(|p| r.y_range().contains(p.y) && table_x.contains(p.x)));
        if !(selected || hovered) {
            return;
        }
        let first = i == 0;
        let last = i + 1 == n;
        let radius = CornerRadius { nw: if first { 8 } else { 0 }, sw: if first { 8 } else { 0 }, ne: if last { 8 } else { 0 }, se: if last { 8 } else { 0 } };
        let mut band = r.shrink2(Vec2::new(0.0, 1.0));
        if first {
            band.min.x += 4.0;
        }
        if last {
            band.max.x -= 4.0;
        }
        let fill = if selected { self.pal.sel } else { self.pal.hover };
        ui.painter().rect_filled(band, radius, fill);
    }

    fn cell(&self, ui: &mut egui::Ui, col: &Col, e: &Entry) {
        let pal = self.pal;
        match col.key {
            SortKey::Name => {
                ui.add_space(14.0);
                draw_icon(ui, e, self.index.extension(e), &pal, self.icons);
                ui.add_space(10.0);
                let job = name_job(self.index.name(e), self.terms, theme::regular(13.0), &pal);
                ui.add(egui::Label::new(job).truncate().selectable(false));
            }
            SortKey::Path => {
                ui.add_space(12.0);
                ui.add(egui::Label::new(RichText::new(self.index.dir_path(e)).font(theme::mono(12.0)).color(pal.dim)).truncate().selectable(false));
            }
            key => {
                let (text, font) = match key {
                    SortKey::Size => (size_text(e.size, e.is_dir()), theme::regular(12.0)),
                    SortKey::Extension => (self.index.extension(e).to_lowercase(), theme::regular(13.0)),
                    SortKey::Modified => (format_filetime(e.mtime), theme::regular(12.0)),
                    SortKey::Created => (format_filetime(e.ctime), theme::regular(12.0)),
                    _ => (attrib_text(e.attrs), theme::mono(12.0)),
                };
                let layout = if col.right { Layout::right_to_left(Align::Center) } else { Layout::left_to_right(Align::Center) };
                ui.with_layout(layout, |ui| {
                    ui.add_space(if col.right { 16.0 } else { 12.0 });
                    ui.add(egui::Label::new(RichText::new(text).font(font).color(pal.dim)).selectable(false));
                });
            }
        }
    }
}

fn context_menu(ui: &mut egui::Ui, id: u32, actions: &mut Vec<RowAction>) {
    let mut item = |ui: &mut egui::Ui, label: &str, shortcut: &str, strong: bool, act: RowAction| {
        let text = if strong { RichText::new(label).strong() } else { RichText::new(label) };
        if ui.add(egui::Button::new(text).shortcut_text(shortcut).frame(false).min_size(Vec2::new(220.0, 24.0))).clicked() {
            actions.push(act);
            ui.close_menu();
        }
    };
    item(ui, "Open", "Enter", true, RowAction::Open(id));
    item(ui, "Open with\u{2026}", "", false, RowAction::OpenWith(id));
    item(ui, "Open path", "", false, RowAction::OpenPath(id));
    ui.separator();
    item(ui, "Copy file(s)", "", false, RowAction::CopyFiles(id));
    item(ui, "Copy full name", "Ctrl+C", false, RowAction::CopyFull(id));
    item(ui, "Copy name", "", false, RowAction::CopyName(id));
    item(ui, "Copy path", "", false, RowAction::CopyPath(id));
    ui.separator();
    item(ui, "Copy to\u{2026}", "", false, RowAction::CopyTo(id));
    item(ui, "Move to\u{2026}", "", false, RowAction::MoveTo(id));
    item(ui, "Zip selected\u{2026}", "", false, RowAction::Zip(id));
    item(ui, "Batch rename\u{2026}", "", false, RowAction::BatchRename(id));
    ui.separator();
    item(ui, "Rename\u{2026}", "F2", false, RowAction::Rename(id));
    item(ui, "Delete (Recycle Bin)", "Del", false, RowAction::Delete(id));
    ui.separator();
    item(ui, "Properties", "Alt+Enter", false, RowAction::Properties(id));
}

/// The name with every search term marked (highlight colours of the original).
fn name_job(name: &str, terms: &[String], font: egui::FontId, pal: &Pal) -> LayoutJob {
    let lower = name.to_ascii_lowercase();
    let mut ranges: Vec<(usize, usize)> = Vec::new();
    for t in terms {
        if t.is_empty() {
            continue;
        }
        for (s, m) in lower.match_indices(t.as_str()) {
            ranges.push((s, s + m.len()));
        }
    }
    ranges.sort_unstable();
    let mut merged: Vec<(usize, usize)> = Vec::new();
    for (s, e) in ranges {
        match merged.last_mut() {
            Some(l) if s <= l.1 => l.1 = l.1.max(e),
            _ => merged.push((s, e)),
        }
    }
    let plain = TextFormat { font_id: font.clone(), color: pal.text, ..Default::default() };
    let marked = TextFormat { font_id: font, color: pal.hl_ink, background: pal.hl, ..Default::default() };
    let mut job = LayoutJob::default();
    let mut at = 0;
    for (s, e) in merged {
        if !name.is_char_boundary(s) || !name.is_char_boundary(e) {
            continue;
        }
        if s > at {
            job.append(&name[at..s], 0.0, plain.clone());
        }
        job.append(&name[s..e], 0.0, marked.clone());
        at = e;
    }
    if at < name.len() {
        job.append(&name[at..], 0.0, plain);
    }
    job
}

/// The Windows shell icon for the file type (16x16), or a drawn folder / page when unavailable.
pub fn draw_icon(ui: &mut egui::Ui, e: &Entry, ext: &str, pal: &Pal, icons: &std::cell::RefCell<crate::icons::IconCache>) {
    let (rect, _) = ui.allocate_exact_size(Vec2::new(16.0, 16.0), Sense::hover());
    let key = if e.is_dir() { "<dir>".to_string() } else { ext.to_ascii_lowercase() };
    let tex = icons.borrow_mut().get(ui.ctx(), &key);
    if let Some(t) = tex {
        let uv = Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0));
        ui.painter().image(t.id(), rect, uv, Color32::WHITE);
        return;
    }
    let p = ui.painter();
    if e.is_dir() {
        let tab = Rect::from_min_max(rect.min + Vec2::new(0.0, 1.5), rect.min + Vec2::new(7.0, 5.0));
        let body = Rect::from_min_max(rect.min + Vec2::new(0.0, 3.5), rect.max - Vec2::new(0.0, 1.0));
        p.rect_filled(tab, 2.0, Color32::from_rgb(226, 160, 52));
        p.rect_filled(body, 2.5, Color32::from_rgb(251, 191, 74));
        return;
    }
    let page = Rect::from_min_max(rect.min + Vec2::new(2.5, 0.5), rect.max - Vec2::new(2.5, 0.5));
    let (fill, edge) = if pal.dark { (Color32::from_rgb(52, 56, 70), Color32::from_rgb(86, 92, 112)) } else { (Color32::WHITE, Color32::from_gray(170)) };
    p.rect_filled(page, 2.0, fill);
    p.rect_stroke(page, 2.0, Stroke::new(1.0_f32, edge), egui::StrokeKind::Inside);
    let band = match kind_of(ext) {
        Kind::Exe => Some(Color32::from_rgb(79, 129, 230)),
        Kind::Picture => Some(Color32::from_rgb(52, 180, 110)),
        Kind::Audio => Some(Color32::from_rgb(220, 90, 170)),
        Kind::Video => Some(Color32::from_rgb(150, 90, 230)),
        Kind::Archive => Some(Color32::from_rgb(214, 150, 50)),
        Kind::Document => Some(Color32::from_rgb(99, 130, 240)),
        Kind::Other => None,
    };
    if let Some(c) = band {
        let b = Rect::from_min_max(page.left_bottom() + Vec2::new(1.5, -5.5), page.right_bottom() - Vec2::new(1.5, 1.5));
        p.rect_filled(b, 1.5, c);
    }
}
