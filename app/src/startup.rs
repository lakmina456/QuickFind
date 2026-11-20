//! Start-up: fonts and colours, loading the saved index, scanning and rebuilding.

use crate::index::{self, Index};
use crate::scan::{self, Progress};
use eframe::egui::{self, FontData, FontFamily};
use std::path::PathBuf;
use std::sync::atomic::Ordering;
use std::sync::{Arc, RwLock};
use std::time::Duration;

/// Instrument Sans (4 weights), JetBrains Mono and the Segoe MDL2 icon font, plus colours.
pub fn setup_style(ctx: &egui::Context, dark: bool, accent: &str) {
    let mut fonts = egui::FontDefinitions::default();
    let mut add = |name: &str, bytes: &'static [u8]| {
        fonts.font_data.insert(name.into(), Arc::new(FontData::from_static(bytes)));
    };
    add("is-regular", include_bytes!("../assets/fonts/InstrumentSans-Regular.ttf"));
    add("is-medium", include_bytes!("../assets/fonts/InstrumentSans-Medium.ttf"));
    add("is-semibold", include_bytes!("../assets/fonts/InstrumentSans-SemiBold.ttf"));
    add("is-bold", include_bytes!("../assets/fonts/InstrumentSans-Bold.ttf"));
    add("jbmono", include_bytes!("../assets/fonts/JetBrainsMono-Regular.ttf"));
    // System fonts fill in glyphs the bundled fonts lack, and supply the icon font.
    let windir = PathBuf::from(std::env::var("WINDIR").unwrap_or_else(|_| "C:\\Windows".into())).join("Fonts");
    let mut fallback: Vec<String> = Vec::new();
    for (name, file) in [("segoeui", "segoeui.ttf"), ("seguisym", "seguisym.ttf")] {
        if let Ok(bytes) = std::fs::read(windir.join(file)) {
            fonts.font_data.insert(name.into(), Arc::new(FontData::from_owned(bytes)));
            fallback.push(name.into());
        }
    }
    let has_icons = match std::fs::read(windir.join("segmdl2.ttf")) {
        Ok(bytes) => {
            fonts.font_data.insert("mdl2".into(), Arc::new(FontData::from_owned(bytes)));
            true
        }
        Err(_) => false,
    };
    let family = |first: &str| {
        let mut v = vec![first.to_string()];
        v.extend(fallback.iter().cloned());
        v
    };
    fonts.families.insert(FontFamily::Proportional, family("is-regular"));
    fonts.families.insert(FontFamily::Name(crate::theme::MEDIUM.into()), family("is-medium"));
    fonts.families.insert(FontFamily::Name(crate::theme::SEMIBOLD.into()), family("is-semibold"));
    fonts.families.insert(FontFamily::Name(crate::theme::BOLD.into()), family("is-bold"));
    fonts.families.insert(FontFamily::Monospace, family("jbmono"));
    let mut icon_family = if has_icons { vec!["mdl2".to_string()] } else { Vec::new() };
    icon_family.extend(fallback.iter().cloned());
    fonts.families.insert(FontFamily::Name(crate::theme::ICONS.into()), icon_family);
    ctx.set_fonts(fonts);
    crate::theme::apply(ctx, dark, accent);
    ctx.style_mut(|s| {
        s.text_styles.insert(egui::TextStyle::Body, egui::FontId::proportional(14.0));
        s.text_styles.insert(egui::TextStyle::Button, egui::FontId::proportional(13.5));
        s.text_styles.insert(egui::TextStyle::Small, egui::FontId::proportional(12.0));
        s.text_styles.insert(egui::TextStyle::Monospace, egui::FontId::monospace(12.0));
        s.spacing.item_spacing = egui::vec2(6.0, 4.0);
        s.spacing.button_padding = egui::vec2(10.0, 5.0);
        s.interaction.selectable_labels = false;
    });
}

/// Where the index came from, shown at the right of the status bar.
pub static MODE: std::sync::Mutex<&'static str> = std::sync::Mutex::new("");

/// Load the saved index (instant results), then refresh it from disk and watch for changes.
pub fn start_indexing(index: Arc<RwLock<Index>>, progress: Arc<Progress>, ctx: egui::Context, screenshot: bool) {
    std::thread::spawn(move || {
        let roots = scan::active_roots();
        if let Some(mut l) = index::load() {
            let mut live = index.write().unwrap();
            l.epoch = live.epoch + 1;
            *live = l;
            drop(live);
            *MODE.lock().unwrap() = "Cache";
            ctx.request_repaint();
            if screenshot {
                return;
            }
            refresh(&index, &progress, &roots, &ctx);
        } else {
            scan::scan_into(&index, &roots, &progress);
            ctx.request_repaint();
            if screenshot {
                return;
            }
        }
        *MODE.lock().unwrap() = "Walk";
        let _ = index::save(&index.read().unwrap());
        crate::watch::start(index.clone(), roots, ctx.clone());
        // Save live changes now and then.
        let mut saved = index.read().unwrap().generation;
        loop {
            std::thread::sleep(Duration::from_secs(600));
            let idx = index.read().unwrap();
            if idx.generation != saved && !progress.running.load(Ordering::Relaxed) {
                let _ = index::save(&idx);
                saved = idx.generation;
            }
        }
    });
}

/// Scan into a fresh index, then swap it in whole.
fn refresh(index: &Arc<RwLock<Index>>, progress: &Arc<Progress>, roots: &[String], ctx: &egui::Context) {
    let fresh = Arc::new(RwLock::new(Index::default()));
    scan::scan_into(&fresh, roots, progress);
    let mut new_index = std::mem::take(&mut *fresh.write().unwrap());
    let mut live = index.write().unwrap();
    new_index.epoch = live.epoch + 1;
    new_index.generation = live.generation + 1;
    *live = new_index;
    drop(live);
    *MODE.lock().unwrap() = "Walk";
    ctx.request_repaint();
}

pub fn rebuild(index: Arc<RwLock<Index>>, progress: Arc<Progress>, ctx: egui::Context) {
    if progress.running.load(Ordering::Relaxed) {
        return;
    }
    std::thread::spawn(move || {
        let roots = scan::active_roots();
        refresh(&index, &progress, &roots, &ctx);
        let _ = index::save(&index.read().unwrap());
    });
}
