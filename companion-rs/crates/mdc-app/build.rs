//! Build script for `mdc-app`.
//!
//! Embeds the Windows executable icon (resource id 1) from
//! `assets/companion.ico` so the `.exe` shows the companion icon in Explorer
//! and the taskbar. This targets the `*-pc-windows-gnu` host, where `winres`
//! drives `windres` (the WinLibs mingw64 `bin` must be on `PATH`).
//!
//! No-op on non-Windows targets; contains no `unsafe`.

fn main() {
    println!("cargo:rerun-if-changed=assets/companion.ico");

    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    if target_os != "windows" {
        return;
    }

    let mut res = winres::WindowsResource::new();
    res.set_icon("assets/companion.ico");
    if let Err(err) = res.compile() {
        panic!("winres failed to embed assets/companion.ico: {err}");
    }
}
