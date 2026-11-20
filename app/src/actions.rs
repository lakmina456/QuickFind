//! File actions: open with, properties, recycle bin delete, rename.

#[cfg(windows)]
fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain([0]).collect()
}

/// "Open with..." chooser.
pub fn open_with(path: &str) {
    #[cfg(windows)]
    {
        use windows_sys::Win32::UI::Shell::ShellExecuteW;
        use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
        let (verb, file) = (wide("openas"), wide(path));
        unsafe {
            ShellExecuteW(std::ptr::null_mut(), verb.as_ptr(), file.as_ptr(), std::ptr::null(), std::ptr::null(), SW_SHOWNORMAL);
        }
    }
    #[cfg(not(windows))]
    let _ = path;
}

/// The system Properties window.
pub fn properties(path: &str) {
    #[cfg(windows)]
    {
        use windows_sys::Win32::UI::Shell::{ShellExecuteExW, SEE_MASK_INVOKEIDLIST, SHELLEXECUTEINFOW};
        use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOW;
        let (verb, file) = (wide("properties"), wide(path));
        let mut info: SHELLEXECUTEINFOW = unsafe { std::mem::zeroed() };
        info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        info.fMask = SEE_MASK_INVOKEIDLIST as _;
        info.lpVerb = verb.as_ptr();
        info.lpFile = file.as_ptr();
        info.nShow = SW_SHOW as _;
        unsafe {
            ShellExecuteExW(&mut info);
        }
    }
    #[cfg(not(windows))]
    let _ = path;
}

/// Move files and folders to the Recycle Bin.
pub fn recycle(paths: &[String]) -> Result<(), String> {
    #[cfg(windows)]
    {
        use windows_sys::Win32::UI::Shell::{
            SHFileOperationW, FOF_ALLOWUNDO, FOF_NOCONFIRMATION, FOF_NOERRORUI, FOF_SILENT, FO_DELETE, SHFILEOPSTRUCTW,
        };
        let mut from: Vec<u16> = Vec::new();
        for p in paths {
            from.extend(p.encode_utf16());
            from.push(0);
        }
        from.push(0);
        let mut op: SHFILEOPSTRUCTW = unsafe { std::mem::zeroed() };
        op.wFunc = FO_DELETE as _;
        op.pFrom = from.as_ptr();
        op.fFlags = (FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT) as _;
        let rc = unsafe { SHFileOperationW(&mut op) };
        if rc != 0 {
            return Err(format!("Delete failed (code {rc})"));
        }
        Ok(())
    }
    #[cfg(not(windows))]
    {
        let _ = paths;
        Err("Recycle Bin is only available on Windows".into())
    }
}

/// Rename a file or folder in place.
pub fn rename(path: &str, new_name: &str) -> Result<(), String> {
    let new_name = new_name.trim();
    if new_name.is_empty() || new_name.contains(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) {
        return Err("That name is not allowed".into());
    }
    let p = std::path::Path::new(path);
    let Some(parent) = p.parent() else { return Err("Cannot rename a drive".into()) };
    std::fs::rename(p, parent.join(new_name)).map_err(|e| format!("Rename failed: {e}"))
}

/// Copy or move files and folders into `dest`; never overwrites (adds " - copy"). Returns a summary.
pub fn transfer(paths: &[String], dest: &std::path::Path, mv: bool) -> String {
    let (mut ok, mut failed) = (0, 0);
    for p in paths {
        let src = std::path::Path::new(p);
        let Some(name) = src.file_name() else { continue };
        let mut target = dest.join(name);
        let mut n = 0;
        while target.exists() {
            n += 1;
            let stem = format!("{} - copy{}", name.to_string_lossy(), if n > 1 { format!(" {n}") } else { String::new() });
            target = dest.join(stem);
        }
        let done = if mv {
            std::fs::rename(src, &target).or_else(|_| copy_all(src, &target).and_then(|_| remove_all(src)))
        } else {
            copy_all(src, &target)
        };
        if done.is_ok() { ok += 1 } else { failed += 1 }
    }
    let verb = if mv { "Moved" } else { "Copied" };
    if failed == 0 { format!("{verb} {ok} item(s)") } else { format!("{verb} {ok} item(s), {failed} failed") }
}

fn copy_all(src: &std::path::Path, dst: &std::path::Path) -> std::io::Result<()> {
    if src.is_dir() {
        std::fs::create_dir_all(dst)?;
        for e in std::fs::read_dir(src)? {
            let e = e?;
            copy_all(&e.path(), &dst.join(e.file_name()))?;
        }
        Ok(())
    } else {
        std::fs::copy(src, dst).map(|_| ())
    }
}

fn remove_all(p: &std::path::Path) -> std::io::Result<()> {
    if p.is_dir() { std::fs::remove_dir_all(p) } else { std::fs::remove_file(p) }
}

/// Zip files and folders into `zip_path` (uses the system's Compress-Archive). Returns a summary.
pub fn zip(paths: &[String], zip_path: &std::path::Path) -> String {
    let quote = |s: &str| format!("'{}'", s.replace('\'', "''"));
    let list = paths.iter().map(|p| quote(p)).collect::<Vec<_>>().join(",");
    let script = format!("Compress-Archive -Force -LiteralPath {list} -DestinationPath {}", quote(&zip_path.to_string_lossy()));
    let mut cmd = std::process::Command::new("powershell");
    cmd.args(["-NoProfile", "-NonInteractive", "-Command", &script]);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        cmd.creation_flags(0x0800_0000); // no console window
    }
    match cmd.output() {
        Ok(o) if o.status.success() => format!("Zipped {} item(s)", paths.len()),
        Ok(_) => "Zip failed".to_string(),
        Err(e) => format!("Zip failed: {e}"),
    }
}

/// New names for a batch rename: replace `find` with `with` (blank find keeps the name), then
/// optionally add " 1", " 2" ... before the extension.
pub fn batch_names(names: &[String], find: &str, with: &str, number: bool) -> Vec<String> {
    names
        .iter()
        .enumerate()
        .map(|(i, n)| {
            let mut s = if find.is_empty() { n.clone() } else { n.replace(find, with) };
            if number {
                let (stem, ext) = match s.rfind('.') {
                    Some(p) if p > 0 => (s[..p].to_string(), s[p..].to_string()),
                    _ => (s.clone(), String::new()),
                };
                s = format!("{stem} {}{ext}", i + 1);
            }
            s
        })
        .collect()
}

/// Rename every path to the matching new name (same folder). Returns a summary.
pub fn batch_rename(paths: &[String], new_names: &[String]) -> String {
    let (mut ok, mut failed) = (0, 0);
    for (p, n) in paths.iter().zip(new_names) {
        if rename(p, n).is_ok() { ok += 1 } else { failed += 1 }
    }
    if failed == 0 { format!("Renamed {ok} item(s)") } else { format!("Renamed {ok} item(s), {failed} failed") }
}


/// Put the files on the clipboard the way Explorer does, so they can be pasted into
/// Explorer, e-mail or chat (the practical stand-in for dragging results out).
pub fn copy_files_to_clipboard(paths: &[String]) -> Result<(), String> {
    #[cfg(windows)]
    unsafe {
        use windows_sys::Win32::System::DataExchange::{CloseClipboard, EmptyClipboard, OpenClipboard, SetClipboardData};
        use windows_sys::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalUnlock, GMEM_MOVEABLE};
        let mut wide: Vec<u16> = Vec::new();
        for p in paths {
            wide.extend(p.encode_utf16());
            wide.push(0);
        }
        wide.push(0);
        let header = 20usize; // DROPFILES: offset, point (8), nc flag, wide flag
        let total = header + wide.len() * 2;
        let h = GlobalAlloc(GMEM_MOVEABLE, total);
        if h.is_null() {
            return Err("Out of memory".into());
        }
        let p = GlobalLock(h) as *mut u8;
        if p.is_null() {
            return Err("Clipboard unavailable".into());
        }
        std::ptr::write_bytes(p, 0, header);
        *(p as *mut u32) = header as u32;
        *(p.add(16) as *mut i32) = 1; // fWide
        std::ptr::copy_nonoverlapping(wide.as_ptr() as *const u8, p.add(header), wide.len() * 2);
        GlobalUnlock(h);
        if OpenClipboard(std::ptr::null_mut()) == 0 {
            return Err("Clipboard is busy".into());
        }
        EmptyClipboard();
        let ok = !SetClipboardData(15, h).is_null(); // CF_HDROP
        CloseClipboard();
        if ok { Ok(()) } else { Err("Could not copy".into()) }
    }
    #[cfg(not(windows))]
    {
        let _ = paths;
        Err("Only available on Windows".into())
    }
}
