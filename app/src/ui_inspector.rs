//! The inspector card (preview, buttons and details) and the command palette.

use super::App;
use crate::platform::{open, open_path};
use crate::preview::Body;
use crate::theme;
use crate::widgets;
use eframe::egui::{self, pos2, vec2, Align, Align2, Color32, Layout, Margin, RichText, Sense, Stroke};

/// What a command-palette row does.
#[derive(Clone, Copy)]
pub(super) enum Cmd {
    ToggleDark,
    TogglePreview,
    Rebuild,
    RunAdmin,
    Export,
    Help,
    Accent(&'static str),
    ToggleExtra,
    ToggleJunk,
    ToggleStartup,
    FocusSearch,
    Quick(u8),
    NewTab,
    ToggleContent,
    CopyTo,
    MoveTo,
    Zip,
    Batch,
}

const COMMANDS: &[(&str, &str, Cmd)] = &[
    ("Toggle dark mode", "View", Cmd::ToggleDark),
    ("Toggle preview pane", "View", Cmd::TogglePreview),
    ("Rebuild index", "Tools", Cmd::Rebuild),
    ("Run as administrator", "Faster NTFS indexing", Cmd::RunAdmin),
    ("Export results", "File", Cmd::Export),
    ("Search syntax help", "Help", Cmd::Help),
    ("Accent: Blue", "Theme", Cmd::Accent("blue")),
    ("Accent: Slate", "Theme", Cmd::Accent("slate")),
    ("Accent: Violet", "Theme", Cmd::Accent("violet")),
    ("Accent: Orange", "Theme", Cmd::Accent("orange")),
    ("Accent: Teal", "Theme", Cmd::Accent("teal")),
    ("Toggle USB / removable and network indexing", "Indexing", Cmd::ToggleExtra),
    ("Toggle exclude system junk", "Indexing", Cmd::ToggleJunk),
    ("Toggle start with Windows", "Startup", Cmd::ToggleStartup),
    ("Focus search", "Search", Cmd::FocusSearch),
    ("Quick view: Biggest files", "View", Cmd::Quick(0)),
    ("Quick view: Biggest folders", "View", Cmd::Quick(1)),
    ("Quick view: Duplicate names", "View", Cmd::Quick(2)),
    ("Quick view: Empty folders", "View", Cmd::Quick(3)),
    ("Quick view: Changed today", "View", Cmd::Quick(4)),
    ("New search tab", "Tabs", Cmd::NewTab),
    ("Toggle content search", "Search", Cmd::ToggleContent),
    ("Copy to\u{2026}", "Files", Cmd::CopyTo),
    ("Move to\u{2026}", "Files", Cmd::MoveTo),
    ("Zip selected\u{2026}", "Files", Cmd::Zip),
    ("Batch rename\u{2026}", "Files", Cmd::Batch),
];

impl App {
    pub(super) fn inspector(&mut self, ui: &mut egui::Ui) {
        let pal = theme::get(ui.ctx());
        let Some(p) = &self.preview else {
            ui.vertical_centered(|ui| {
                ui.add_space(ui.available_height() * 0.38);
                ui.label(RichText::new("\u{E8A9}").font(theme::icons(28.0)).color(pal.dim));
                ui.add_space(12.0);
                ui.label(RichText::new("Nothing selected").font(theme::semibold(15.0)).color(pal.text));
                ui.add_space(6.0);
                ui.add_sized(
                    vec2(240.0, 40.0),
                    egui::Label::new(RichText::new("Pick a result to see its preview, size, and location.").font(theme::regular(12.5)).color(pal.dim)).wrap(),
                );
            });
            return;
        };
        let path = p.path.clone();
        let mut copied = false;
        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
            ui.add(egui::Label::new(RichText::new(&p.name).font(theme::semibold(18.0)).color(pal.text)).wrap());
            ui.add_space(8.0);
            egui::Frame::new().fill(pal.accent_soft).corner_radius(99).inner_margin(Margin::symmetric(10, 3)).show(ui, |ui| {
                ui.label(RichText::new(&p.kind_label).font(theme::semibold(11.0)).color(pal.accent_ink));
            });
            ui.add_space(12.0);
            match &p.body {
                Body::Image(uri) => {
                    egui::Frame::new().fill(pal.raised).corner_radius(12).inner_margin(Margin::same(10)).show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        ui.add(egui::Image::new(uri.as_str()).max_size(vec2(ui.available_width(), 150.0)).corner_radius(8));
                    });
                    ui.add_space(12.0);
                }
                Body::Text(t) => {
                    egui::Frame::new().fill(pal.raised).corner_radius(12).inner_margin(Margin::same(12)).show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        ui.set_max_height(150.0);
                        ui.add(egui::Label::new(RichText::new(t).font(theme::mono(11.0)).color(pal.text)).wrap());
                    });
                    ui.add_space(12.0);
                }
                Body::Note(n) => {
                    ui.label(RichText::new(n).color(pal.dim));
                    ui.add_space(12.0);
                }
            }
            let w = ui.available_width();
            if widgets::primary(ui, &pal, "Open", Some(w)).clicked() {
                open(&path);
            }
            ui.add_space(8.0);
            if widgets::action(ui, &pal, "Show in folder", Some(w)).clicked() {
                open_path(&path);
            }
            ui.add_space(8.0);
            let label = if self.copied_path { "Copied" } else { "Copy path" };
            if widgets::action(ui, &pal, label, Some(w)).clicked() {
                ui.ctx().copy_text(path.clone());
                copied = true;
            }
            ui.add_space(14.0);
            for (k, v) in &p.rows {
                let line = ui.max_rect().x_range();
                let top = ui.cursor().top();
                ui.painter().hline(line, top, Stroke::new(1.0_f32, pal.line2));
                ui.add_space(11.0);
                if *k == "Location" {
                    ui.label(RichText::new("Location").font(theme::regular(12.5)).color(pal.dim));
                    ui.add_space(6.0);
                    ui.add(egui::Label::new(RichText::new(v).font(theme::mono(12.0)).color(pal.text)).wrap());
                } else {
                    ui.horizontal_top(|ui| {
                        ui.allocate_ui_with_layout(vec2(88.0, 16.0), Layout::left_to_right(Align::Min), |ui| {
                            ui.label(RichText::new(*k).font(theme::regular(12.5)).color(pal.dim));
                        });
                        ui.with_layout(Layout::right_to_left(Align::TOP), |ui| {
                            ui.add(egui::Label::new(RichText::new(v).font(theme::regular(12.5)).color(pal.text)).wrap());
                        });
                    });
                }
                ui.add_space(11.0);
            }
        });
        if copied {
            self.copied_path = true;
        }
    }

    /// Ctrl+K command palette.
    pub(super) fn palette_window(&mut self, ctx: &egui::Context) {
        if !self.show_palette {
            return;
        }
        let pal = theme::get(ctx);
        let screen = ctx.screen_rect();
        egui::Area::new(egui::Id::new("palette-scrim")).order(egui::Order::Foreground).fixed_pos(screen.min).show(ctx, |ui| {
            let (r, resp) = ui.allocate_exact_size(screen.size(), Sense::click());
            ui.painter().rect_filled(r, 0.0, Color32::from_rgba_unmultiplied(0x0A, 0x0F, 0x14, 0x88));
            if resp.clicked() {
                self.show_palette = false;
            }
        });
        let q = self.palette_query.trim().to_lowercase();
        let hits: Vec<usize> = COMMANDS.iter().enumerate().filter(|(_, c)| q.is_empty() || c.0.to_lowercase().contains(&q)).map(|(i, _)| i).collect();
        let (down, up, enter, esc) = ctx.input(|i| {
            (i.key_pressed(egui::Key::ArrowDown), i.key_pressed(egui::Key::ArrowUp), i.key_pressed(egui::Key::Enter), i.key_pressed(egui::Key::Escape))
        });
        if down && !hits.is_empty() {
            self.palette_idx = (self.palette_idx + 1).min(hits.len() - 1);
        }
        if up {
            self.palette_idx = self.palette_idx.saturating_sub(1);
        }
        self.palette_idx = self.palette_idx.min(hits.len().saturating_sub(1));
        let mut run = None;
        if enter {
            run = hits.get(self.palette_idx).copied();
        }
        if esc {
            self.show_palette = false;
        }
        let width = 520.0;
        egui::Area::new(egui::Id::new("palette"))
            .order(egui::Order::Tooltip)
            .fixed_pos(pos2(screen.center().x - width / 2.0, screen.top() + 120.0))
            .show(ctx, |ui| {
                egui::Frame::new()
                    .fill(pal.panel)
                    .stroke(Stroke::new(1.5_f32, pal.accent))
                    .corner_radius(14)
                    .inner_margin(Margin::same(14))
                    .shadow(egui::Shadow { offset: [0, 6], blur: 36, spread: 0, color: Color32::from_black_alpha(90) })
                    .show(ui, |ui| {
                        ui.set_width(width - 30.0);
                        let edit = egui::TextEdit::singleline(&mut self.palette_query)
                            .font(theme::regular(16.0))
                            .desired_width(f32::INFINITY)
                            .margin(Margin::symmetric(8, 8))
                            .hint_text("Type a command\u{2026}");
                        let r = ui.add(edit);
                        r.request_focus();
                        if r.changed() {
                            self.palette_idx = 0;
                        }
                        ui.add_space(10.0);
                        egui::ScrollArea::vertical().max_height(320.0).show(ui, |ui| {
                            for (n, &i) in hits.iter().enumerate() {
                                let (title, hint, _) = COMMANDS[i];
                                let (rect, resp) = ui.allocate_exact_size(vec2(ui.available_width(), 32.0), Sense::click());
                                if n == self.palette_idx || resp.hovered() {
                                    ui.painter().rect_filled(rect, 8.0, pal.sel);
                                }
                                ui.painter().text(pos2(rect.left() + 8.0, rect.center().y), Align2::LEFT_CENTER, title, theme::regular(13.5), pal.text);
                                ui.painter().text(pos2(rect.right() - 8.0, rect.center().y), Align2::RIGHT_CENTER, hint, theme::regular(11.0), pal.dim);
                                if resp.clicked() {
                                    run = Some(i);
                                }
                            }
                        });
                    });
            });
        if let Some(i) = run {
            self.show_palette = false;
            self.run_command(ctx, COMMANDS[i].2);
        }
    }

    fn run_command(&mut self, ctx: &egui::Context, cmd: Cmd) {
        match cmd {
            Cmd::ToggleDark => {
                self.settings.dark = !self.settings.dark;
                theme::apply(ctx, self.settings.dark, &self.settings.accent);
            }
            Cmd::TogglePreview => self.settings.preview = !self.settings.preview,
            Cmd::Rebuild => {
                self.settings.apply_rules();
                crate::startup::rebuild(self.index.clone(), self.progress.clone(), ctx.clone());
            }
            Cmd::RunAdmin => {
                if crate::sysinfo::run_as_admin() {
                    self.quitting = true;
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            }
            Cmd::Export => self.export(),
            Cmd::Help => self.show_help = true,
            Cmd::Accent(id) => {
                self.settings.accent = id.to_string();
                theme::apply(ctx, self.settings.dark, id);
            }
            Cmd::ToggleExtra => {
                self.settings.include_extra = !self.settings.include_extra;
                self.settings.apply_rules();
                self.refresh_drives();
            }
            Cmd::ToggleJunk => {
                self.settings.junk = !self.settings.junk;
                self.settings.apply_rules();
            }
            Cmd::ToggleStartup => {
                self.start_with_windows = !self.start_with_windows;
                crate::sysinfo::set_startup(self.start_with_windows);
            }
            Cmd::FocusSearch => self.focus_search = true,
            Cmd::Quick(n) => self.quick_view(n),
            Cmd::NewTab => self.new_tab(),
            Cmd::ToggleContent => self.settings.content_search = !self.settings.content_search,
            Cmd::CopyTo | Cmd::MoveTo | Cmd::Zip | Cmd::Batch => {
                if let Some(id) = self.first_selected() {
                    let a = match cmd {
                        Cmd::CopyTo => crate::table::RowAction::CopyTo(id),
                        Cmd::MoveTo => crate::table::RowAction::MoveTo(id),
                        Cmd::Zip => crate::table::RowAction::Zip(id),
                        _ => crate::table::RowAction::BatchRename(id),
                    };
                    self.apply(ctx, a);
                } else {
                    self.message = Some(("Select files first".into(), std::time::Instant::now()));
                }
            }
        }
        self.save_settings();
    }
}
