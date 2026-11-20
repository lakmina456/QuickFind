//! Small drawn controls shared by the window: pill buttons, icon buttons, links.

use crate::theme::Pal;
use eframe::egui::{self, vec2, Align2, Color32, CornerRadius, CursorIcon, FontId, Response, Sense, Stroke, StrokeKind, Ui, Vec2};

pub struct Look {
    pub fill: Color32,
    pub fill_hover: Color32,
    pub stroke: Stroke,
    pub text: Color32,
    pub text_hover: Color32,
    pub font: FontId,
    pub radius: u8,
    pub height: f32,
    pub pad_x: f32,
    pub min_w: f32,
}

/// A rounded button that draws itself (no egui button chrome).
pub fn button(ui: &mut Ui, label: &str, look: &Look) -> Response {
    let galley = ui.painter().layout_no_wrap(label.to_string(), look.font.clone(), look.text);
    let w = (galley.size().x + look.pad_x * 2.0).max(look.min_w);
    let (rect, resp) = ui.allocate_exact_size(vec2(w, look.height), Sense::click());
    let hovered = resp.hovered();
    let fill = if hovered { look.fill_hover } else { look.fill };
    let color = if hovered { look.text_hover } else { look.text };
    let r = CornerRadius::same(look.radius);
    ui.painter().rect_filled(rect, r, fill);
    if look.stroke.width > 0.0 {
        ui.painter().rect_stroke(rect, r, look.stroke, StrokeKind::Inside);
    }
    let galley = ui.painter().layout_no_wrap(label.to_string(), look.font.clone(), color);
    let pos = rect.center() - galley.size() / 2.0;
    ui.painter().galley(pos, galley, color);
    resp.on_hover_cursor(CursorIcon::PointingHand)
}

/// Chip (Size / Modified filters): outlined pill, soft accent when on.
pub fn chip(ui: &mut Ui, pal: &Pal, label: &str, on: bool) -> Response {
    let look = Look {
        fill: if on { pal.accent_soft } else { pal.panel },
        fill_hover: if on { pal.accent_soft } else { pal.panel },
        stroke: Stroke::new(1.0_f32, if on { pal.accent } else { pal.line }),
        text: if on { pal.accent_ink } else { pal.text },
        text_hover: if on { pal.accent_ink } else { pal.text },
        font: if on { crate::theme::semibold(13.0) } else { crate::theme::medium(13.0) },
        radius: 15,
        height: 30.0,
        pad_x: 13.0,
        min_w: 0.0,
    };
    button(ui, label, &look)
}

/// Segment inside a segmented control (file-type pills): accent fill when selected.
pub fn segment(ui: &mut Ui, pal: &Pal, label: &str, on: bool, min_w: f32) -> Response {
    let look = Look {
        fill: if on { pal.accent_fill } else { Color32::TRANSPARENT },
        fill_hover: if on { pal.accent_fill } else { pal.hover },
        stroke: Stroke::NONE,
        text: if on { pal.on_accent } else { pal.dim },
        text_hover: if on { pal.on_accent } else { pal.text },
        font: if on { crate::theme::semibold(13.0) } else { crate::theme::medium(13.0) },
        radius: 9,
        height: 34.0,
        pad_x: 14.0,
        min_w,
    };
    button(ui, label, &look)
}

/// Option toggle inside the search card (Aa, Word, Path, é, .*).
pub fn option(ui: &mut Ui, pal: &Pal, label: &str, on: bool) -> Response {
    let look = Look {
        fill: if on { pal.accent_fill } else { Color32::TRANSPARENT },
        fill_hover: if on { pal.accent_fill } else { pal.hover },
        stroke: Stroke::NONE,
        text: if on { pal.on_accent } else { pal.dim },
        text_hover: if on { pal.on_accent } else { pal.text },
        font: crate::theme::semibold(13.0),
        radius: 8,
        height: 32.0,
        pad_x: 10.0,
        min_w: 42.0,
    };
    button(ui, label, &look)
}

/// Secondary action button (panel2 fill, outlined, radius 10).
pub fn action(ui: &mut Ui, pal: &Pal, label: &str, width: Option<f32>) -> Response {
    let look = Look {
        fill: pal.panel2,
        fill_hover: pal.hover,
        stroke: Stroke::new(1.0_f32, pal.line),
        text: pal.text,
        text_hover: pal.text,
        font: crate::theme::medium(13.0),
        radius: 10,
        height: 38.0,
        pad_x: 16.0,
        min_w: width.unwrap_or(0.0),
    };
    button(ui, label, &look)
}

/// Filled accent action button.
pub fn primary(ui: &mut Ui, pal: &Pal, label: &str, width: Option<f32>) -> Response {
    let look = Look {
        fill: pal.accent_fill,
        fill_hover: pal.accent_fill.gamma_multiply(0.92),
        stroke: Stroke::NONE,
        text: pal.on_accent,
        text_hover: pal.on_accent,
        font: crate::theme::semibold(13.0),
        radius: 10,
        height: 38.0,
        pad_x: 16.0,
        min_w: width.unwrap_or(0.0),
    };
    button(ui, label, &look)
}

/// A square icon button (Segoe MDL2 glyph).
pub fn icon_button(ui: &mut Ui, pal: &Pal, glyph: &str, size: Vec2, hover_fill: Color32, tip: &str) -> Response {
    let (rect, resp) = ui.allocate_exact_size(size, Sense::click());
    let hovered = resp.hovered();
    if hovered {
        ui.painter().rect_filled(rect, 8.0, hover_fill);
    }
    let color = if hovered { pal.text } else { pal.dim };
    ui.painter().text(rect.center(), Align2::CENTER_CENTER, glyph, crate::theme::icons(14.0), color);
    resp.on_hover_text(tip).on_hover_cursor(CursorIcon::PointingHand)
}

/// A small `egui::Frame` look used for cards.
pub fn card(pal: &Pal, radius: u8) -> egui::Frame {
    egui::Frame::new().fill(pal.panel).stroke(Stroke::new(1.0_f32, pal.line)).corner_radius(radius)
}
