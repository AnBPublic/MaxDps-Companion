//! Gallant theme tokens for the Rust companion.
//!
//! One source of truth for the dark terminal palette and geometry so the
//! status pill, transport buttons and console card cannot drift. Mirrors
//! `app/MaxDpsCompanion/Ui/ExpTheme.cs`; the wire contract is untouched.

use eframe::egui;

// ----- colour (dark terminal) -----
/// Window background.
pub const BG: egui::Color32 = egui::Color32::from_rgb(0x0B, 0x11, 0x10);
/// Card / panel surface.
pub const CARD: egui::Color32 = egui::Color32::from_rgb(0x13, 0x1C, 0x1A);
/// Hairline border.
pub const BORDER: egui::Color32 = egui::Color32::from_rgb(0x25, 0x31, 0x2E);
/// Primary accent (selection, emphasized controls).
pub const ACCENT: egui::Color32 = egui::Color32::from_rgb(0xE8, 0xC5, 0x58);
/// Error / destructive.
pub const DANGER: egui::Color32 = egui::Color32::from_rgb(0xE5, 0x60, 0x4F);
/// Healthy / running.
pub const OK: egui::Color32 = egui::Color32::from_rgb(0x4C, 0xC3, 0x8A);
/// Primary text.
pub const TEXT: egui::Color32 = egui::Color32::from_rgb(0xED, 0xEF, 0xF0);
/// Secondary text.
pub const SECONDARY: egui::Color32 = egui::Color32::from_rgb(0x9A, 0xA5, 0xA1);
/// Muted monospace log stamps (`HH:mm:ss`).
pub const MONO_STAMP: egui::Color32 = egui::Color32::from_rgb(0x5E, 0x6B, 0x67);

// ----- geometry -----
/// Outer corner radius (cards, transport controls).
pub const RADIUS_OUTER: u8 = 12;
/// Inner corner radius (pill).
pub const RADIUS_INNER: u8 = 10;
/// Transport control height.
pub const CONTROL_HEIGHT: f32 = 42.0;

/// Proportional body font id.
pub fn body(size: f32) -> egui::FontId {
    egui::FontId::proportional(size)
}

/// Monospace font id for the log view / timestamps.
pub fn mono(size: f32) -> egui::FontId {
    egui::FontId::monospace(size)
}

/// Install the Gallant dark theme on `ctx`.
///
/// Replaces the previous blue selection (`(38, 92, 140)`) with [`ACCENT`],
/// applies the token palette, and pins proportional body text plus a
/// monospace face for the log view.
pub fn apply_theme(ctx: &egui::Context) {
    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = BG;
    visuals.window_fill = CARD;
    visuals.extreme_bg_color = BG;
    visuals.faint_bg_color = CARD;
    visuals.window_stroke = egui::Stroke::new(1.0, BORDER);
    visuals.override_text_color = Some(TEXT);
    visuals.selection.bg_fill = ACCENT;
    visuals.selection.stroke = egui::Stroke::new(1.0, ACCENT);

    let widgets = &mut visuals.widgets;
    widgets.noninteractive.bg_fill = CARD;
    widgets.noninteractive.weak_bg_fill = CARD;
    widgets.noninteractive.bg_stroke = egui::Stroke::new(1.0, BORDER);
    widgets.noninteractive.fg_stroke = egui::Stroke::new(1.0, SECONDARY);
    widgets.inactive.bg_fill = CARD;
    widgets.inactive.weak_bg_fill = CARD;
    widgets.inactive.bg_stroke = egui::Stroke::new(1.0, BORDER);
    widgets.hovered.weak_bg_fill = BORDER;
    widgets.hovered.bg_stroke = egui::Stroke::new(1.0, ACCENT);
    widgets.active.weak_bg_fill = BORDER;
    widgets.active.bg_stroke = egui::Stroke::new(1.0, ACCENT);
    ctx.set_visuals(visuals);

    install_fonts(ctx);
    ctx.all_styles_mut(|style| {
        style.text_styles.insert(egui::TextStyle::Body, body(14.0));
        style.text_styles.insert(egui::TextStyle::Monospace, mono(13.0));
    });
}

/// Builds the font set from the egui defaults, then appends the Windows system
/// symbol faces (if present) to the end of both families.
///
/// The bundled egui faces lack several glyphs the console uses
/// (`\u{25B6}` `\u{25A0}` `\u{23F8}` `\u{25C9}` `\u{2699}` `\u{2922}`), so they
/// would paint as tofu. `seguisym.ttf` ("Segoe UI Symbol") covers all of them;
/// `segmdl2.ttf` ("Segoe MDL2 Assets") is the Win11 icon face kept as a second
/// fallback. Appending (not replacing) keeps the normal body/emoji faces first.
/// Missing files are skipped, and [`has_glyphs`] lets callers drop the icon
/// rather than render a box.
fn install_fonts(ctx: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    const CANDIDATES: [(&str, &str); 2] = [
        ("segoe-ui-symbol", r"C:\Windows\Fonts\seguisym.ttf"),
        ("segoe-mdl2", r"C:\Windows\Fonts\segmdl2.ttf"),
    ];
    let mut loaded: Vec<String> = Vec::new();
    for (name, path) in CANDIDATES {
        if let Ok(bytes) = std::fs::read(path) {
            fonts.font_data.insert(
                name.to_owned(),
                std::sync::Arc::new(egui::FontData::from_owned(bytes)),
            );
            loaded.push(name.to_owned());
        }
    }
    for family in [egui::FontFamily::Proportional, egui::FontFamily::Monospace] {
        let list = fonts.families.entry(family).or_default();
        for name in &loaded {
            if !list.contains(name) {
                list.push(name.clone());
            }
        }
    }
    ctx.set_fonts(fonts);
}

/// Whether the proportional body font (including the appended symbol face) can
/// render every glyph in `s`. Button labels call this so a missing system font
/// degrades to text-only instead of a tofu box.
pub fn has_glyphs(ctx: &egui::Context, s: &str) -> bool {
    ctx.fonts_mut(|fonts| fonts.has_glyphs(&egui::FontId::proportional(14.0), s))
}
