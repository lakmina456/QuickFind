//! Recent searches, newest first, kept between runs.

const MAX: usize = 30;

fn file() -> std::path::PathBuf {
    crate::index::cache_path().with_file_name("history.txt")
}

pub fn load() -> Vec<String> {
    std::fs::read_to_string(file()).map(|t| t.lines().filter(|l| !l.is_empty()).map(String::from).collect()).unwrap_or_default()
}

pub fn push(list: &mut Vec<String>, query: &str) {
    let q = query.trim();
    if q.is_empty() || q.len() > 300 {
        return;
    }
    list.retain(|x| x != q);
    list.insert(0, q.to_string());
    list.truncate(MAX);
    save(list);
}

pub fn save(list: &[String]) {
    if let Some(p) = file().parent() {
        let _ = std::fs::create_dir_all(p);
    }
    let _ = std::fs::write(file(), list.join("\n"));
}
