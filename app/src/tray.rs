//! Notification-area (tray) icon and the Ctrl+Alt+Space hotkey, on one hidden message window.

use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::OnceLock;

#[derive(Clone, Copy)]
pub enum Event {
    Show,
    Exit,
}

static CALLBACK: OnceLock<Box<dyn Fn(Event) + Send + Sync>> = OnceLock::new();
static HWND_SAVED: AtomicIsize = AtomicIsize::new(0);
static EXIT: AtomicBool = AtomicBool::new(false);
static HOTKEY_ON: AtomicBool = AtomicBool::new(true);

/// Turn the Ctrl+Alt+Space hotkey on or off.
pub fn set_hotkey_enabled(on: bool) {
    HOTKEY_ON.store(on, Ordering::SeqCst);
}

/// True when another copy of the program is already running (it is asked to show itself).
pub fn already_running() -> bool {
    #[cfg(windows)]
    unsafe {
        use windows_sys::Win32::Foundation::GetLastError;
        use windows_sys::Win32::System::Threading::CreateMutexW;
        use windows_sys::Win32::UI::WindowsAndMessaging::{FindWindowExW, PostMessageW, HWND_MESSAGE};
        let name: Vec<u16> = "OmniFind.SingleInstance".encode_utf16().chain([0]).collect();
        let _h = CreateMutexW(std::ptr::null(), 0, name.as_ptr());
        if GetLastError() != 183 {
            return false; // first copy: the mutex lives until the process ends
        }
        let class: Vec<u16> = "OmniFindTray".encode_utf16().chain([0]).collect();
        let w = FindWindowExW(HWND_MESSAGE, std::ptr::null_mut(), class.as_ptr(), std::ptr::null());
        if !w.is_null() {
            PostMessageW(w, 0x8000 + 2, 0, 0);
        }
        true
    }
    #[cfg(not(windows))]
    false
}

/// True once (after "Exit" was chosen in the tray menu).
pub fn take_exit() -> bool {
    EXIT.swap(false, Ordering::SeqCst)
}

#[cfg(windows)]
pub fn start(on_event: impl Fn(Event) + Send + Sync + 'static) {
    let _ = CALLBACK.set(Box::new(on_event));
    std::thread::spawn(|| unsafe { run() });
}

#[cfg(not(windows))]
pub fn start(_on_event: impl Fn(Event) + Send + Sync + 'static) {}

/// Take the icon out of the tray (call when the program exits).
pub fn remove() {
    #[cfg(windows)]
    unsafe {
        use windows_sys::Win32::UI::Shell::{Shell_NotifyIconW, NIM_DELETE, NOTIFYICONDATAW};
        let h = HWND_SAVED.load(Ordering::SeqCst);
        if h != 0 {
            let mut nid: NOTIFYICONDATAW = std::mem::zeroed();
            nid.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
            nid.hWnd = h as _;
            nid.uID = 1;
            Shell_NotifyIconW(NIM_DELETE, &nid);
        }
    }
}

#[cfg(windows)]
const WM_TRAY: u32 = 0x8000 + 1; // WM_APP + 1

#[cfg(windows)]
unsafe extern "system" fn wndproc(
    hwnd: windows_sys::Win32::Foundation::HWND,
    msg: u32,
    wp: windows_sys::Win32::Foundation::WPARAM,
    lp: windows_sys::Win32::Foundation::LPARAM,
) -> windows_sys::Win32::Foundation::LRESULT {
    use windows_sys::Win32::Foundation::POINT;
    use windows_sys::Win32::UI::WindowsAndMessaging::*;
    let fire = |e: Event| {
        if let Some(cb) = CALLBACK.get() {
            cb(e);
        }
    };
    match msg {
        WM_HOTKEY => {
            if HOTKEY_ON.load(Ordering::SeqCst) {
                fire(Event::Show);
            }
        }
        m if m == 0x8000 + 2 => fire(Event::Show),
        WM_TRAY => match (lp & 0xFFFF) as u32 {
            WM_LBUTTONUP => fire(Event::Show),
            WM_RBUTTONUP => {
                let menu = CreatePopupMenu();
                let show: Vec<u16> = "Show OmniFind".encode_utf16().chain([0]).collect();
                let exit: Vec<u16> = "Exit".encode_utf16().chain([0]).collect();
                AppendMenuW(menu, MF_STRING, 1, show.as_ptr());
                AppendMenuW(menu, MF_STRING, 2, exit.as_ptr());
                let mut pt: POINT = std::mem::zeroed();
                GetCursorPos(&mut pt);
                SetForegroundWindow(hwnd);
                let cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, std::ptr::null());
                DestroyMenu(menu);
                match cmd {
                    1 => fire(Event::Show),
                    2 => {
                        EXIT.store(true, Ordering::SeqCst);
                        fire(Event::Exit);
                    }
                    _ => {}
                }
            }
            _ => {}
        },
        _ => return DefWindowProcW(hwnd, msg, wp, lp),
    }
    0
}

#[cfg(windows)]
unsafe fn run() {
    use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{RegisterHotKey, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT, VK_SPACE};
    use windows_sys::Win32::UI::Shell::{Shell_NotifyIconW, NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NOTIFYICONDATAW};
    use windows_sys::Win32::UI::WindowsAndMessaging::*;

    let hinst = GetModuleHandleW(std::ptr::null());
    let class: Vec<u16> = "OmniFindTray".encode_utf16().chain([0]).collect();
    let mut wc: WNDCLASSW = std::mem::zeroed();
    wc.lpfnWndProc = Some(wndproc);
    wc.hInstance = hinst;
    wc.lpszClassName = class.as_ptr();
    RegisterClassW(&wc);
    let hwnd = CreateWindowExW(0, class.as_ptr(), class.as_ptr(), 0, 0, 0, 0, 0, HWND_MESSAGE, std::ptr::null_mut(), hinst, std::ptr::null());
    if hwnd.is_null() {
        return;
    }
    HWND_SAVED.store(hwnd as isize, Ordering::SeqCst);
    RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE as u32);

    let mut nid: NOTIFYICONDATAW = std::mem::zeroed();
    nid.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
    nid.hWnd = hwnd;
    nid.uID = 1;
    nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    nid.uCallbackMessage = WM_TRAY;
    nid.hIcon = LoadIconW(std::ptr::null_mut(), IDI_APPLICATION);
    for (i, c) in "OmniFind  (Ctrl+Alt+Space)".encode_utf16().enumerate().take(127) {
        nid.szTip[i] = c;
    }
    Shell_NotifyIconW(NIM_ADD, &nid);

    let mut msg: MSG = std::mem::zeroed();
    while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
}
