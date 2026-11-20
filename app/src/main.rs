#![cfg_attr(windows, windows_subsystem = "windows")]

mod actions;
mod icons;
mod tray;
mod history;
mod sysinfo;
mod widgets;
mod bookmarks;
mod preview;
mod settings;
mod theme;
mod dates;
mod filters;
mod help;
mod index;
mod platform;
mod query;
mod scan;
mod search;
mod startup;
mod table;
mod ui;
mod watch;

use eframe::egui;
use std::path::PathBuf;

fn main() -> eframe::Result<()> {
    let args: Vec<String> = std::env::args().collect();
    if args.iter().any(|a| a == "--build-index") {
        // Scan every fixed drive, save the index, report and exit (no window).
        let start = std::time::Instant::now();
        let index = std::sync::Arc::new(std::sync::RwLock::new(index::Index::default()));
        let progress = scan::Progress::default();
        scan::scan_into(&index, &scan::fixed_drives(), &progress);
        let scanned = start.elapsed();
        let idx = index.read().unwrap();
        let saved = index::save(&idx).is_ok();
        let (n, dirs) = (idx.entries.len(), idx.dirs.len());
        drop(idx);
        // Time searches on the index as reloaded from disk, like a normal start.
        let t = std::time::Instant::now();
        let reloaded = index::load().unwrap_or_default();
        let loaded = reloaded.entries.len();
        let load_secs = t.elapsed().as_secs_f32();
        *index.write().unwrap() = reloaded;
        let mut report = format!(
            "entries {n} folders {dirs} scan {:.1}s saved {saved} reload {loaded} entries in {load_secs:.1}s\n",
            scanned.as_secs_f32()
        );
        for q in ["a", "report", "*.png", "ext:jpg;png", "windows\\ system32\\ .dll", "size:>100mb", "dm:thisweek", "<foo|bar> !node_modules"] {
            let (n, ms) = search::time_query(&index, q);
            report.push_str(&format!("{q:>28}  {n:>9} results  {ms:>5} ms\n"));
        }
        let _ = std::fs::write(index::cache_path().with_file_name("last-build.txt"), &report);
        eprint!("{report}");
        return Ok(());
    }
    let screenshot = args
        .iter()
        .position(|a| a == "--screenshot")
        .and_then(|i| args.get(i + 1))
        .map(PathBuf::from);

    if screenshot.is_none() && tray::already_running() {
        return Ok(()); // the running copy was asked to show itself
    }
    let mut viewport = egui::ViewportBuilder::default()
        .with_title(ui::APP_NAME)
        .with_decorations(false)
        .with_inner_size([1280.0, 820.0])
        .with_min_inner_size([900.0, 480.0]);
    if let Ok(img) = image::load_from_memory(include_bytes!("../assets/OmniFind.png")) {
        let rgba = img.to_rgba8();
        let (w, h) = rgba.dimensions();
        viewport = viewport.with_icon(egui::IconData { rgba: rgba.into_raw(), width: w, height: h });
    }
    if screenshot.is_some() {
        viewport = viewport.with_active(false);
        #[cfg(windows)]
        {
            viewport = viewport.with_window_level(egui::WindowLevel::AlwaysOnBottom);
        }
    }
    #[allow(unused_mut)]
    let mut options = eframe::NativeOptions { viewport, ..Default::default() };
    #[cfg(target_os = "macos")]
    if screenshot.is_some() {
        options.event_loop_builder = Some(Box::new(|b| {
            use winit::platform::macos::EventLoopBuilderExtMacOS;
            b.with_activate_ignoring_other_apps(false);
        }));
    }
    let start = ui::StartOptions::from_args(&args);
    eframe::run_native(ui::APP_NAME, options, Box::new(move |cc| Ok(Box::new(ui::App::new(cc, screenshot, start)))))
}
