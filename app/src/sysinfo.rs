//! System helpers: drive labels, start with Windows, run as administrator.

#[cfg(windows)]
fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain([0]).collect()
}

/// "Windows (C:)" style label for a drive root such as "C:".
pub fn volume_label(root: &str) -> String {
    let letter = root.trim_end_matches(['\\', ':']).to_string();
    #[allow(unused_mut)]
    let mut name = String::new();
    #[cfg(windows)]
    unsafe {
        use windows_sys::Win32::Storage::FileSystem::GetVolumeInformationW;
        let path = wide(&format!("{letter}:\\"));
        let mut buf = [0u16; 262];
        let ok = GetVolumeInformationW(
            path.as_ptr(),
            buf.as_mut_ptr(),
            buf.len() as u32,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            0,
        );
        if ok != 0 {
            let n = buf.iter().position(|c| *c == 0).unwrap_or(0);
            name = String::from_utf16_lossy(&buf[..n]);
        }
    }
    if name.is_empty() {
        name = "Local Disk".into();
    }
    format!("{name} ({}:)", letter.to_uppercase())
}

#[cfg(windows)]
fn quiet(cmd: &mut std::process::Command) -> bool {
    use std::os::windows::process::CommandExt;
    cmd.creation_flags(0x0800_0000).output().map(|o| o.status.success()).unwrap_or(false)
}

const RUN_KEY: &str = r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

/// Is the program set to start with Windows?
pub fn startup_enabled() -> bool {
    #[cfg(windows)]
    {
        quiet(std::process::Command::new("reg").args(["query", RUN_KEY, "/v", "OmniFind"]))
    }
    #[cfg(not(windows))]
    false
}

pub fn set_startup(on: bool) {
    #[cfg(windows)]
    {
        if on {
            if let Ok(exe) = std::env::current_exe() {
                let value = format!("\"{}\"", exe.display());
                quiet(std::process::Command::new("reg").args(["add", RUN_KEY, "/v", "OmniFind", "/t", "REG_SZ", "/d", &value, "/f"]));
            }
        } else {
            quiet(std::process::Command::new("reg").args(["delete", RUN_KEY, "/v", "OmniFind", "/f"]));
        }
    }
    #[cfg(not(windows))]
    let _ = on;
}

/// Start a second copy with administrator rights (the caller then exits). False if cancelled.
pub fn run_as_admin() -> bool {
    #[cfg(windows)]
    unsafe {
        use windows_sys::Win32::UI::Shell::ShellExecuteW;
        use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
        let Ok(exe) = std::env::current_exe() else { return false };
        let (verb, file) = (wide("runas"), wide(&exe.to_string_lossy()));
        let r = ShellExecuteW(std::ptr::null_mut(), verb.as_ptr(), file.as_ptr(), std::ptr::null(), std::ptr::null(), SW_SHOWNORMAL);
        return r as usize > 32;
    }
    #[cfg(not(windows))]
    false
}
