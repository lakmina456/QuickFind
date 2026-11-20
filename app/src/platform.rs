//! Opening files and folders, and number formatting.

pub fn open(path: &str) {
    #[cfg(windows)]
    {
        use windows_sys::Win32::UI::Shell::ShellExecuteW;
        use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
        let wide = |s: &str| s.encode_utf16().chain([0]).collect::<Vec<u16>>();
        let op = wide("open");
        let target = if path.ends_with(':') { format!("{path}\\") } else { path.to_string() };
        let file = wide(&target);
        unsafe {
            ShellExecuteW(std::ptr::null_mut(), op.as_ptr(), file.as_ptr(), std::ptr::null(), std::ptr::null(), SW_SHOWNORMAL);
        }
    }
    #[cfg(not(windows))]
    {
        let _ = std::process::Command::new("xdg-open").arg(path).spawn();
    }
}

/// Show the item selected in its folder.
pub fn open_path(path: &str) {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        let _ = std::process::Command::new("explorer.exe").raw_arg(format!("/select,\"{path}\"")).spawn();
    }
    #[cfg(not(windows))]
    {
        if let Some(p) = std::path::Path::new(path).parent() {
            let _ = std::process::Command::new("xdg-open").arg(p).spawn();
        }
    }
}

pub fn thousands(n: u64) -> String {
    let s = n.to_string();
    let mut out = String::with_capacity(s.len() + s.len() / 3);
    for (i, c) in s.chars().enumerate() {
        if i > 0 && (s.len() - i) % 3 == 0 {
            out.push(',');
        }
        out.push(c);
    }
    out
}

/// Size column text: KB rounded up (estimated, see MECHANICS.md).
pub fn size_text(size: u64, is_dir: bool) -> String {
    if size == crate::index::NO_SIZE {
        return String::new();
    }
    if is_dir && size == 0 {
        return "0 KB".into();
    }
    human_size(size)
}

/// "12.4 MB" style size for the preview pane and status bar.
pub fn human_size(n: u64) -> String {
    if n < 1024 {
        return format!("{n} B");
    }
    const UNITS: [&str; 5] = ["KB", "MB", "GB", "TB", "PB"];
    let mut v = n as f64;
    let mut u = 0;
    loop {
        v /= 1024.0;
        if v < 1024.0 || u == UNITS.len() - 1 {
            break;
        }
        u += 1;
    }
    if v >= 100.0 {
        format!("{v:.0} {}", UNITS[u])
    } else if v >= 10.0 {
        format!("{v:.1} {}", UNITS[u])
    } else {
        format!("{v:.2} {}", UNITS[u])
    }
}

pub fn attrib_text(a: u32) -> String {
    let mut s = String::new();
    for (bit, c) in [(0x1, 'R'), (0x2, 'H'), (0x4, 'S'), (0x10, 'D'), (0x20, 'A'), (0x800, 'C'), (0x4000, 'E')] {
        if a & bit != 0 {
            s.push(c);
        }
    }
    s
}
