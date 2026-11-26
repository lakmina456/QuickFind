// Embed assets/QuickFind.ico (and version info) into the Windows .exe.
// Uses win-resource-builder so no rc.exe / windres is required — GNU and MSVC both work.
fn main() {
    println!("cargo:rerun-if-changed=assets/QuickFind.ico");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }

    use std::num::NonZeroU16;
    use win_resource_builder::image::{Icon, PngOptimization};
    use win_resource_builder::resource::{CoffMachine, LanguageId, ResourceFile};
    use win_resource_builder::version_info::{Version, VersionInfo};

    let icon = Icon::options()
        .png_optimization(PngOptimization::Retain)
        .create_from_file("assets/QuickFind.ico")
        .expect("assets/QuickFind.ico must be a valid Windows icon");

    let mut file = ResourceFile::new();
    // Icon 1 is what Explorer / Start Menu show for the .exe.
    file.insert_icon(NonZeroU16::MIN, LanguageId::NEUTRAL, &icon)
        .expect("insert app icon");

    let ver = std::env::var("CARGO_PKG_VERSION").unwrap_or_else(|_| "0.1.0".into());
    let parts: Vec<u16> = ver
        .split(|c: char| !c.is_ascii_digit())
        .filter(|s| !s.is_empty())
        .take(4)
        .filter_map(|s| s.parse().ok())
        .collect();
    let (a, b, c, d) = (
        parts.first().copied().unwrap_or(0),
        parts.get(1).copied().unwrap_or(0),
        parts.get(2).copied().unwrap_or(0),
        parts.get(3).copied().unwrap_or(0),
    );
    if let Ok(vi) = VersionInfo::options()
        .file_description("QuickFind")
        .company_name("lakmina456")
        .legal_copyright("Copyright (c) lakmina456")
        .create("QuickFind", "QuickFind.exe", Version::new(a, b, c, d))
    {
        let _ = file.insert_version_info(&vi);
    }

    let out = std::path::PathBuf::from(std::env::var("OUT_DIR").unwrap()).join("quickfind_resources.obj");
    file.write_coff(
        std::fs::File::create(&out).expect("create resource object"),
        CoffMachine::cargo_target().expect("coff machine for target"),
    )
    .expect("write resource COFF object");
    println!("cargo:rustc-link-arg-bins={}", out.display());
}
