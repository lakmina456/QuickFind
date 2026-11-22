//! Colours, accents and fonts, taken from the QuickFind light/dark tokens.

use eframe::egui::{self, Color32, CornerRadius, FontFamily, FontId, Stroke, Visuals};

#[derive(Clone, Copy)]
pub struct Pal {
    pub dark: bool,
    pub bg: Color32,
    pub panel: Color32,
    pub panel2: Color32,
    pub raised: Color32,
    pub text: Color32,
    pub dim: Color32,
    /// Accent for text, rings and borders.
    pub accent: Color32,
    /// Accent for filled buttons.
    pub accent_fill: Color32,
    pub accent_soft: Color32,
    pub accent_ink: Color32,
    pub on_accent: Color32,
    pub sel: Color32,
    pub hover: Color32,
    pub line: Color32,
    pub line2: Color32,
    pub hl: Color32,
    pub hl_ink: Color32,
    pub danger: Color32,
}

const fn c(r: u8, g: u8, b: u8) -> Color32 {
    Color32::from_rgb(r, g, b)
}

/// id, label, then (light, dark) pairs for: accent, fill, ink, soft, on-accent.
pub const ACCENTS: [(&str, &str, [(Color32, Color32); 5]); 5] = [
    ("blue", "Blue", [
        (c(0x1F, 0x57, 0xD1), c(0x7F, 0xA8, 0xFF)),
        (c(0x2A, 0x62, 0xE0), c(0x6B, 0x9B, 0xFF)),
        (c(0x1A, 0x47, 0xB3), c(0xA9, 0xC4, 0xFF)),
        (c(0xE3, 0xEC, 0xFF), c(0x1B, 0x2A, 0x4D)),
        (c(0xFF, 0xFF, 0xFF), c(0x06, 0x12, 0x2B)),
    ]),
    ("slate", "Slate", [
        (c(0x47, 0x55, 0x69), c(0x9A, 0xA4, 0xB2)),
        (c(0x4A, 0x5A, 0x70), c(0x8A, 0x96, 0xA8)),
        (c(0x34, 0x40, 0x52), c(0xC0, 0xC8, 0xD4)),
        (c(0xE8, 0xEC, 0xF1), c(0x2A, 0x32, 0x3E)),
        (c(0xFF, 0xFF, 0xFF), c(0x0E, 0x12, 0x18)),
    ]),
    ("violet", "Violet", [
        (c(0x6D, 0x28, 0xD9), c(0xA0, 0x8F, 0xD4)),
        (c(0x7C, 0x3A, 0xED), c(0x9B, 0x8A, 0xE8)),
        (c(0x5B, 0x21, 0xB6), c(0xC4, 0xB5, 0xFD)),
        (c(0xED, 0xE9, 0xFE), c(0x2E, 0x1F, 0x4D)),
        (c(0xFF, 0xFF, 0xFF), c(0x12, 0x0A, 0x28)),
    ]),
    ("orange", "Orange", [
        (c(0xC2, 0x41, 0x0C), c(0xD4, 0x9A, 0x6A)),
        (c(0xEA, 0x58, 0x0C), c(0xE0, 0xA8, 0x70)),
        (c(0x9A, 0x34, 0x12), c(0xF5, 0xD0, 0xA9)),
        (c(0xFF, 0xED, 0xD5), c(0x3D, 0x28, 0x14)),
        (c(0xFF, 0xFF, 0xFF), c(0x1A, 0x0E, 0x06)),
    ]),
    ("teal", "Teal", [
        (c(0x0F, 0x76, 0x6E), c(0x4A, 0x9B, 0x94)),
        (c(0x14, 0x8A, 0x80), c(0x5A, 0xB0, 0xA8)),
        (c(0x0D, 0x5C, 0x56), c(0x99, 0xE0, 0xD8)),
        (c(0xCC, 0xF2, 0xEF), c(0x16, 0x3A, 0x36)),
        (c(0xFF, 0xFF, 0xFF), c(0x06, 0x16, 0x14)),
    ]),
];

pub fn pal(dark: bool, accent_id: &str) -> Pal {
    let a = ACCENTS.iter().find(|a| a.0 == accent_id).unwrap_or(&ACCENTS[0]);
    let pick = |p: (Color32, Color32)| if dark { p.1 } else { p.0 };
    let blue = a.0 == "blue";
    let soft = pick(a.2[3]);
    if dark {
        Pal {
            dark,
            bg: c(0x0B, 0x0F, 0x17),
            panel: c(0x12, 0x18, 0x26),
            panel2: c(0x17, 0x1F, 0x30),
            raised: c(0x17, 0x1F, 0x30),
            text: c(0xE8, 0xED, 0xF7),
            dim: c(0x98, 0xA4, 0xBA),
            accent: pick(a.2[0]),
            accent_fill: pick(a.2[1]),
            accent_soft: soft,
            accent_ink: pick(a.2[2]),
            on_accent: pick(a.2[4]),
            sel: if blue { c(0x19, 0x28, 0x4A) } else { soft },
            hover: c(0x16, 0x1F, 0x33),
            line: c(0x24, 0x2E, 0x44),
            line2: c(0x1D, 0x26, 0x39),
            hl: c(0x5C, 0x48, 0x12),
            hl_ink: c(0xFF, 0xE9, 0xA8),
            danger: c(0xC4, 0x2B, 0x1C),
        }
    } else {
        Pal {
            dark,
            bg: c(0xEF, 0xF2, 0xF7),
            panel: c(0xFF, 0xFF, 0xFF),
            panel2: c(0xF6, 0xF8, 0xFC),
            raised: c(0xF6, 0xF8, 0xFC),
            text: c(0x0F, 0x1B, 0x2D),
            dim: c(0x56, 0x63, 0x7A),
            accent: pick(a.2[0]),
            accent_fill: pick(a.2[1]),
            accent_soft: soft,
            accent_ink: pick(a.2[2]),
            on_accent: pick(a.2[4]),
            sel: if blue { c(0xE6, 0xEE, 0xFF) } else { soft },
            hover: c(0xF2, 0xF5, 0xFA),
            line: c(0xDA, 0xE1, 0xEC),
            line2: c(0xE7, 0xEC, 0xF4),
            hl: c(0xFF, 0xE7, 0xA0),
            hl_ink: c(0x3B, 0x2A, 0x00),
            danger: c(0xC4, 0x2B, 0x1C),
        }
    }
}

pub fn apply(ctx: &egui::Context, dark: bool, accent_id: &str) {
    let p = pal(dark, accent_id);
    let mut v = if dark { Visuals::dark() } else { Visuals::light() };
    v.panel_fill = p.bg;
    v.window_fill = p.panel;
    v.extreme_bg_color = p.raised;
    v.faint_bg_color = p.panel2;
    v.window_stroke = Stroke::new(1.0_f32, p.line);
    v.selection.bg_fill = p.accent_soft;
    v.selection.stroke = Stroke::new(1.0_f32, p.accent);
    v.hyperlink_color = p.accent;
    v.window_corner_radius = CornerRadius::same(14);
    v.menu_corner_radius = CornerRadius::same(10);
    let r = CornerRadius::same(9);
    let w = &mut v.widgets;
    for s in [&mut w.noninteractive, &mut w.inactive, &mut w.hovered, &mut w.active, &mut w.open] {
        s.corner_radius = r;
        s.fg_stroke = Stroke::new(1.0_f32, p.text);
    }
    w.noninteractive.bg_stroke = Stroke::new(1.0_f32, p.line);
    w.inactive.weak_bg_fill = p.panel2;
    w.inactive.bg_fill = p.panel2;
    w.inactive.bg_stroke = Stroke::new(1.0_f32, p.line);
    w.hovered.weak_bg_fill = p.hover;
    w.hovered.bg_fill = p.hover;
    w.hovered.bg_stroke = Stroke::new(1.0_f32, p.line);
    w.active.weak_bg_fill = p.accent_soft;
    w.active.bg_fill = p.accent_soft;
    w.open.weak_bg_fill = p.hover;
    w.open.bg_fill = p.hover;
    ctx.set_visuals(v);
    ctx.data_mut(|d| d.insert_temp(egui::Id::new("quickfind-pal"), p));
}

pub fn get(ctx: &egui::Context) -> Pal {
    ctx.data(|d| d.get_temp(egui::Id::new("quickfind-pal"))).unwrap_or_else(|| pal(false, "blue"))
}

// ---- fonts (Instrument Sans in four weights, JetBrains Mono, Segoe MDL2 icons) ----

pub const MEDIUM: &str = "is-medium";
pub const SEMIBOLD: &str = "is-semibold";
pub const BOLD: &str = "is-bold";
pub const ICONS: &str = "mdl2";

pub fn regular(size: f32) -> FontId {
    FontId::new(size, FontFamily::Proportional)
}
pub fn medium(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(MEDIUM.into()))
}
pub fn semibold(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(SEMIBOLD.into()))
}
pub fn mono(size: f32) -> FontId {
    FontId::new(size, FontFamily::Monospace)
}
pub fn icons(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name(ICONS.into()))
}
