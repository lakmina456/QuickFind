//! Bookmarks: saved searches (search text + filter), kept between runs.

use eframe::egui;

#[derive(Clone)]
pub struct Bookmark {
    pub name: String,
    pub query: String,
    pub filter: usize,
}

fn file() -> std::path::PathBuf {
    crate::index::cache_path().with_file_name("bookmarks.txt")
}

pub fn load() -> Vec<Bookmark> {
    let Ok(text) = std::fs::read_to_string(file()) else { return Vec::new() };
    text.lines()
        .filter_map(|l| {
            let mut parts = l.splitn(3, '\t');
            let filter = parts.next()?.parse().ok()?;
            let name = parts.next()?.to_string();
            let query = parts.next().unwrap_or("").to_string();
            Some(Bookmark { name, query, filter })
        })
        .collect()
}

fn save(list: &[Bookmark]) {
    let text: String = list.iter().map(|b| format!("{}\t{}\t{}\n", b.filter, b.name.replace('\t', " "), b.query.replace(['\t', '\n'], " "))).collect();
    if let Some(p) = file().parent() {
        let _ = std::fs::create_dir_all(p);
    }
    let _ = std::fs::write(file(), text);
}

/// Draw the Bookmarks menu. Returns a bookmark to open.
pub fn menu(ui: &mut egui::Ui, list: &mut Vec<Bookmark>, query: &str, filter: usize, filter_name: &str) -> Option<Bookmark> {
    let mut open = None;
    ui.menu_button("Bookmarks", |ui| {
        let can_add = !query.trim().is_empty() || filter != 0;
        if ui.add_enabled(can_add, egui::Button::new("Add to Bookmarks").shortcut_text("Ctrl+D")).clicked() {
            add(list, query, filter, filter_name);
            ui.close_menu();
        }
        let current = list.iter().position(|b| b.query == query && b.filter == filter);
        if ui.add_enabled(current.is_some(), egui::Button::new("Remove This Bookmark")).clicked() {
            if let Some(i) = current {
                list.remove(i);
                save(list);
            }
            ui.close_menu();
        }
        if !list.is_empty() {
            ui.separator();
        }
        for b in list.iter() {
            if ui.button(&b.name).clicked() {
                open = Some(b.clone());
                ui.close_menu();
            }
        }
    });
    open
}

pub fn add(list: &mut Vec<Bookmark>, query: &str, filter: usize, filter_name: &str) {
    if query.trim().is_empty() && filter == 0 {
        return;
    }
    if list.iter().any(|b| b.query == query && b.filter == filter) {
        return;
    }
    let name = match (query.trim().is_empty(), filter) {
        (true, _) => filter_name.to_string(),
        (false, 0) => query.trim().to_string(),
        (false, _) => format!("{} ({filter_name})", query.trim()),
    };
    list.push(Bookmark { name, query: query.to_string(), filter });
    save(list);
}
