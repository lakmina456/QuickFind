//! Real Windows file icons, fetched once per file type and kept as textures.

use eframe::egui::{self, ColorImage, TextureHandle, TextureOptions};
use std::collections::HashMap;

#[derive(Default)]
pub struct IconCache {
    map: HashMap<String, Option<TextureHandle>>,
}

impl IconCache {
    /// The icon for a file extension (lower-case) or "<dir>" for folders; None when unavailable.
    pub fn get(&mut self, ctx: &egui::Context, key: &str) -> Option<TextureHandle> {
        if let Some(t) = self.map.get(key) {
            return t.clone();
        }
        let tex = load(key).map(|img| ctx.load_texture(format!("icon-{key}"), img, TextureOptions::LINEAR));
        self.map.insert(key.to_string(), tex.clone());
        tex
    }
}

#[cfg(windows)]
fn load(key: &str) -> Option<ColorImage> {
    use windows_sys::Win32::Graphics::Gdi::{
        DeleteObject, GetDC, GetDIBits, GetObjectW, ReleaseDC, BITMAP, BITMAPINFO, BITMAPINFOHEADER, BI_RGB, DIB_RGB_COLORS,
    };
    use windows_sys::Win32::Storage::FileSystem::{FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_NORMAL};
    use windows_sys::Win32::UI::Shell::{SHGetFileInfoW, SHFILEINFOW, SHGFI_ICON, SHGFI_SMALLICON, SHGFI_USEFILEATTRIBUTES};
    use windows_sys::Win32::UI::WindowsAndMessaging::{DestroyIcon, GetIconInfo, ICONINFO};

    let (name, attr) = if key == "<dir>" {
        ("folder".to_string(), FILE_ATTRIBUTE_DIRECTORY)
    } else {
        (format!("file.{key}"), FILE_ATTRIBUTE_NORMAL)
    };
    let wide: Vec<u16> = name.encode_utf16().chain([0]).collect();
    unsafe {
        let mut info: SHFILEINFOW = std::mem::zeroed();
        let r = SHGetFileInfoW(
            wide.as_ptr(),
            attr,
            &mut info,
            std::mem::size_of::<SHFILEINFOW>() as u32,
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES,
        );
        if r == 0 || info.hIcon.is_null() {
            return None;
        }
        let mut ii: ICONINFO = std::mem::zeroed();
        if GetIconInfo(info.hIcon, &mut ii) == 0 {
            DestroyIcon(info.hIcon);
            return None;
        }
        let mut bm: BITMAP = std::mem::zeroed();
        GetObjectW(ii.hbmColor, std::mem::size_of::<BITMAP>() as i32, &mut bm as *mut _ as *mut _);
        let (w, h) = (bm.bmWidth.max(0) as usize, bm.bmHeight.max(0) as usize);
        let mut out = None;
        if w > 0 && h > 0 {
            let mut bmi: BITMAPINFO = std::mem::zeroed();
            bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
            bmi.bmiHeader.biWidth = w as i32;
            bmi.bmiHeader.biHeight = -(h as i32); // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = BI_RGB;
            let mut buf = vec![0u8; w * h * 4];
            let dc = GetDC(std::ptr::null_mut());
            let got = GetDIBits(dc, ii.hbmColor, 0, h as u32, buf.as_mut_ptr() as *mut _, &mut bmi, DIB_RGB_COLORS);
            ReleaseDC(std::ptr::null_mut(), dc);
            if got != 0 {
                let opaque_missing = buf.chunks(4).all(|p| p[3] == 0);
                for p in buf.chunks_mut(4) {
                    p.swap(0, 2); // BGRA -> RGBA
                    if opaque_missing {
                        p[3] = if p[0] == 0 && p[1] == 0 && p[2] == 0 { 0 } else { 255 };
                    }
                }
                out = Some(ColorImage::from_rgba_unmultiplied([w, h], &buf));
            }
        }
        DeleteObject(ii.hbmColor);
        DeleteObject(ii.hbmMask);
        DestroyIcon(info.hIcon);
        out
    }
}

#[cfg(not(windows))]
fn load(_key: &str) -> Option<ColorImage> {
    None
}
