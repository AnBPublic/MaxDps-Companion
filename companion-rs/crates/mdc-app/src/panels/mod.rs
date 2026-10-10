//! Real dock panels for `mdc-app` — Settings, Doctor, Class Browser, Telemetry.
//!
//! Replaces the previous `Panel stub — routed through the shared command
//! registry` placeholders (`docs/plans/2026-10-10-gallant-parity.md`). Each
//! panel edits or reports real state: Settings edits `settings.ini` via
//! [`mdc_settings::Settings`], Doctor probes the live client, Class Browser
//! shows the 8-slot contract + enable flags, Telemetry drives
//! [`mdc_telemetry::JsonlRecorder`] and replay. No JSON is ever shown to the
//! user; panels render labelled rows and controls.
//!
//! egui thread-safety: panels run on the UI thread only. They read a cloned
//! [`mdc_runtime::Snapshot`] (never touch the engine thread) and ask egui for
//! repaints through the app's existing 10 Hz `request_repaint_after`.

mod panel_classbrowser;
mod panel_doctor;
mod panel_settings;
mod panel_telemetry;

pub use panel_classbrowser::ClassBrowserPanel;
pub use panel_doctor::DoctorPanel;
pub use panel_settings::SettingsPanel;
pub use panel_telemetry::TelemetryPanel;

use eframe::egui;
use mdc_runtime::Snapshot;
use mdc_settings::Settings;

use crate::theme;

/// Version banner shared by the window title, console and Doctor panel.
pub const APP_VERSION: &str = "v3.7.7 Gallant";

/// Wire slot order (`[Spells] spell1..spell8` / `AppSettings.SlotEnabled`).
pub const SLOT_NAMES: [&str; 8] = [
    "Main",
    "Offensive",
    "Defensive",
    "Consumable",
    "Trinket",
    "Interrupt",
    "Mobility",
    "SelfHeal",
];

/// Shared state every panel reads/writes for one frame.
pub struct PanelEnv<'a> {
    /// Latest engine snapshot (read-only; the engine thread owns the source).
    pub snapshot: &'a Snapshot,
    /// The app's live settings model. Panels edit this in place; `Save` persists.
    pub settings: &'a mut Settings,
    /// `settings.ini` path the app loaded (exe directory).
    pub settings_path: &'a str,
    /// Number of commands in the shared registry (Doctor row).
    pub command_count: usize,
    /// Bottom status-bar line.
    pub status: &'a mut String,
}

impl PanelEnv<'_> {
    /// Sets the bottom status line.
    pub fn note(&mut self, text: impl Into<String>) {
        *self.status = text.into();
    }
}

/// Per-panel UI state, owned by the app (one struct per panel).
#[derive(Default)]
pub struct Panels {
    pub settings: SettingsPanel,
    pub doctor: DoctorPanel,
    pub class_browser: ClassBrowserPanel,
    pub telemetry: TelemetryPanel,
}

/// Tone for a labelled diagnostic row.
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Tone {
    Ok,
    Warn,
    Info,
}

impl Tone {
    fn color(self) -> egui::Color32 {
        match self {
            Tone::Ok => theme::OK,
            Tone::Warn => theme::DANGER,
            Tone::Info => theme::SECONDARY,
        }
    }
}

/// One labelled diagnostic row (`label` -> `value`).
pub struct InfoRow {
    pub label: &'static str,
    pub value: String,
    pub tone: Tone,
}

impl InfoRow {
    pub fn new(label: &'static str, value: impl Into<String>, tone: Tone) -> Self {
        Self { label, value: value.into(), tone }
    }
}

/// Renders a row into the current two-column [`egui::Grid`] cell pair.
pub fn info_row(ui: &mut egui::Ui, row: &InfoRow) {
    ui.label(egui::RichText::new(row.label).color(theme::SECONDARY));
    ui.label(egui::RichText::new(&row.value).color(row.tone.color()));
}

/// Finds the game window for `process_name`; `Ok(hwnd)` or a human reason.
pub fn probe_wow(process_name: &str) -> Result<u64, String> {
    #[cfg(target_os = "windows")]
    {
        use mdc_platform::Platform;
        mdc_platform_win::WinPlatform::new()
            .find_wow(process_name)
            .map(|window| window.0)
            .map_err(|err| err.to_string())
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = process_name;
        Err("unsupported on this platform".to_string())
    }
}

/// The exe directory (where `settings.ini` lives), or `.` when unavailable.
pub fn exe_dir() -> String {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|dir| dir.to_path_buf()))
        .map(|dir| dir.to_string_lossy().into_owned())
        .unwrap_or_else(|| ".".to_string())
}

/// Compile-time build profile label for the Doctor panel.
pub fn build_profile() -> &'static str {
    if cfg!(debug_assertions) {
        "debug"
    } else {
        "release"
    }
}
