// Embeds assets/QuickFind.ico as the .exe icon (Windows only).
// If no resource compiler (rc.exe / windres) is available, the build still succeeds, just without the icon.
fn main() {
    println!("cargo:rerun-if-changed=assets/QuickFind.ico");
    #[cfg(windows)]
    {
        let mut res = winres::WindowsResource::new();
        res.set_icon("assets/QuickFind.ico");
        res.set("ProductName", "QuickFind");
        res.set("FileDescription", "QuickFind");
        if let Err(e) = res.compile() {
            println!("cargo:warning=could not embed exe icon: {e}");
        }
    }
}
