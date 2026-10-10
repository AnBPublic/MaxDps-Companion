//! `mdc-app` — thin `eframe`/`egui` shell for egui 0.36 (root-`Ui` layout).
//!
//! The UI holds almost no logic: the Gallant console owns the runtime transport
//! and every action routes through the shared [`mdc_commands::CommandRegistry`].
//! The dock hosts the real Settings / Doctor / Class Browser / Telemetry panels
//! ([`panels`]) — no stubs, and no raw JSON is ever shown to the user. Layout is
//! a dense pro-panel tool frame — top menu, left tool rail, centre dock canvas
//! (`egui_dock`), bottom status bar and a Ctrl+K command palette. Dark theme,
//! no branding.

#![forbid(unsafe_code)]

use eframe::egui;
use egui_dock::{DockArea, DockState, TabViewer};
use mdc_commands::{default_registry, CommandRegistry};
use mdc_runtime::{Runtime, Snapshot};
use mdc_settings::Settings;

mod console;
mod panels;
mod theme;
mod widgets;

use console::{Console, ConsoleActions};
use panels::{PanelEnv, Panels};

/// Window title (plan §1): `MaxDPS Companion v3.7.7 Gallant`.
const WINDOW_TITLE: &str = "MaxDPS Companion v3.7.7 Gallant";

#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
enum Panel {
    Console,
    ClassBrowser,
    Doctor,
    Telemetry,
    Settings,
}

impl Panel {
    /// Panels the tool rail offers (Console is reached by leaving Tools).
    const DOCK: [Panel; 4] = [
        Panel::ClassBrowser,
        Panel::Doctor,
        Panel::Telemetry,
        Panel::Settings,
    ];

    fn title(self) -> &'static str {
        match self {
            Panel::Console => "Console",
            Panel::ClassBrowser => "Class Browser",
            Panel::Doctor => "Doctor",
            Panel::Telemetry => "Telemetry",
            Panel::Settings => "Settings",
        }
    }

    /// Maps a `ui.panel.*` registry command id to the panel it opens.
    fn from_command(id: &str) -> Option<Panel> {
        match id {
            "ui.panel.console" => Some(Panel::Console),
            "ui.panel.class_browser" => Some(Panel::ClassBrowser),
            "ui.panel.doctor" => Some(Panel::Doctor),
            "ui.panel.telemetry" => Some(Panel::Telemetry),
            "ui.panel.settings" => Some(Panel::Settings),
            _ => None,
        }
    }
}

struct CompanionApp {
    registry: CommandRegistry,
    dock: DockState<Panel>,
    palette_open: bool,
    palette_query: String,
    status: String,
    console: Console,
    /// Last polled engine snapshot, cloned once per frame for the panels.
    snapshot: Snapshot,
    panels: Panels,
    /// Live settings model edited by the Settings / Class Browser panels.
    settings: Settings,
    settings_path: String,
    /// Tools rail + dock are kept behind a default-off flag; the Gallant
    /// console is the default view.
    show_tools: bool,
}

impl CompanionApp {
    fn new() -> Self {
        let settings_path = settings_path();
        let settings = Settings::load(settings_path.clone());
        let config_summary = config_summary(&settings);
        let runtime = Runtime::new(default_platform(), settings.clone());
        let mut dock = DockState::new(vec![Panel::Doctor]);
        dock.push_to_focused_leaf(Panel::ClassBrowser);
        dock.push_to_focused_leaf(Panel::Telemetry);
        dock.push_to_focused_leaf(Panel::Settings);
        Self {
            registry: default_registry(),
            dock,
            palette_open: false,
            palette_query: String::new(),
            status: "idle".to_string(),
            console: Console::new(runtime, config_summary),
            snapshot: Snapshot::default(),
            panels: Panels::default(),
            settings,
            settings_path,
            show_tools: false,
        }
    }

    /// Shows the tools dock and focuses `panel`.
    fn open_panel(&mut self, panel: Panel) {
        self.show_tools = true;
        self.dock.push_to_focused_leaf(panel);
        self.status = format!("opened {}", panel.title());
    }

    /// Opens a `ui.panel.*` command's view, or runs any other command and keeps
    /// only a short human status (never the raw JSON envelope).
    fn invoke_command(&mut self, id: &str) {
        if let Some(panel) = Panel::from_command(id) {
            self.open_panel(panel);
            return;
        }
        let out = self.registry.invoke(id, "{}");
        self.status = summarize(&out);
    }

    fn toggle_palette(&mut self, ctx: &egui::Context) {
        if ctx.input(|i| i.modifiers.command && i.key_pressed(egui::Key::K)) {
            self.palette_open = !self.palette_open;
        }
    }

    fn ui_rail(&mut self, ui: &mut egui::Ui) {
        ui.add_space(4.0);
        ui.heading("Tools");
        ui.separator();
        for panel in Panel::DOCK {
            if ui.button(panel.title()).clicked() {
                self.open_panel(panel);
            }
        }
        ui.separator();
        ui.label("Ctrl+K palette");
        ui.label(format!("commands: {}", self.registry.ids().len()));
    }

    fn ui_status(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.label(format!("status: {}", self.status));
            ui.separator();
            ui.label("ready");
        });
    }

    fn ui_palette(&mut self, ctx: &egui::Context) {
        if !self.palette_open {
            return;
        }
        let mut open = self.palette_open;
        let mut chosen: Option<String> = None;
        egui::Window::new("Command Palette")
            .open(&mut open)
            .resizable(true)
            .show(ctx, |ui| {
                ui.label("Type a command id (Ctrl+K to toggle)");
                ui.text_edit_singleline(&mut self.palette_query);
                ui.separator();
                let query = self.palette_query.to_lowercase();
                let ids = self.registry.ids();
                egui::ScrollArea::vertical().max_height(220.0).show(ui, |ui| {
                    for id in ids
                        .iter()
                        .filter(|id| query.is_empty() || id.to_lowercase().contains(&query))
                    {
                        if ui.button(id).clicked() {
                            chosen = Some(id.clone());
                        }
                    }
                });
                ui.separator();
                ui.label(format!("status: {}", self.status));
            });
        self.palette_open = open;
        if let Some(id) = chosen {
            self.invoke_command(&id);
        }
    }
}

impl eframe::App for CompanionApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        // Poll the engine snapshot + schedule the next 10 Hz repaint. The
        // engine thread only ever touches its own mpsc/Mutex, never egui.
        self.console.tick(ui.ctx());
        self.snapshot = self.console.snapshot().clone();
        self.toggle_palette(ui.ctx());

        egui::Panel::top("menu").show(ui, |ui| {
            egui::MenuBar::new().ui(ui, |ui| {
                if ui.button("Command Palette").clicked() {
                    self.palette_open = true;
                }
                ui.separator();
                if ui.button("Doctor").clicked() {
                    self.open_panel(Panel::Doctor);
                }
                if ui.button("Audit").clicked() {
                    self.open_panel(Panel::Doctor);
                }
                ui.separator();
                ui.checkbox(&mut self.show_tools, "Tools");
            });
        });

        self.ui_palette(ui.ctx());

        let mut open_settings = false;
        if self.show_tools {
            egui::Panel::left("rail").default_size(180.0).show(ui, |ui| self.ui_rail(ui));
            egui::Panel::bottom("status").show(ui, |ui| self.ui_status(ui));
            egui::CentralPanel::default().show(ui, |ui| {
                let Self {
                    dock,
                    registry,
                    status,
                    snapshot,
                    settings,
                    settings_path,
                    panels,
                    console,
                    ..
                } = self;
                let mut env = PanelEnv {
                    snapshot,
                    settings,
                    settings_path: settings_path.as_str(),
                    command_count: registry.ids().len(),
                    status,
                };
                let mut viewer = Viewer {
                    panels,
                    console,
                    env: &mut env,
                    open_settings: &mut open_settings,
                };
                DockArea::new(dock).show_inside(ui, &mut viewer);
            });
        } else {
            egui::CentralPanel::default().show(ui, |ui| {
                let ConsoleActions { open_settings: requested } = self.console.ui(ui);
                if requested {
                    open_settings = true;
                }
            });
        }

        if open_settings {
            self.open_panel(Panel::Settings);
        }
    }
}

/// Dock tab renderer: dispatches each tab to its real panel.
struct Viewer<'a, 'e> {
    panels: &'a mut Panels,
    console: &'a mut Console,
    env: &'a mut PanelEnv<'e>,
    open_settings: &'a mut bool,
}

impl TabViewer for Viewer<'_, '_> {
    type Tab = Panel;

    fn id(&mut self, tab: &mut Panel) -> egui::Id {
        egui::Id::new(tab.title())
    }

    fn title(&mut self, tab: &mut Panel) -> egui::WidgetText {
        tab.title().to_string().into()
    }

    fn ui(&mut self, ui: &mut egui::Ui, tab: &mut Panel) {
        match tab {
            Panel::Console => {
                if self.console.ui(ui).open_settings {
                    *self.open_settings = true;
                }
            }
            Panel::Settings => self.panels.settings.ui(ui, self.env),
            Panel::Doctor => self.panels.doctor.ui(ui, self.env),
            Panel::ClassBrowser => self.panels.class_browser.ui(ui, self.env),
            Panel::Telemetry => self.panels.telemetry.ui(ui, self.env),
        }
    }
}

/// Short human status for a non-panel command result (never the raw JSON).
fn summarize(json: &str) -> String {
    match serde_json::from_str::<serde_json::Value>(json) {
        Ok(value) => {
            if value.get("ok").and_then(|ok| ok.as_bool()).unwrap_or(false) {
                let command = value.get("command").and_then(|c| c.as_str()).unwrap_or("");
                format!("ran {command}")
            } else {
                let error = value
                    .get("error")
                    .and_then(|e| e.as_str())
                    .unwrap_or("unknown error");
                format!("error: {error}")
            }
        }
        Err(_) => "error: invalid command response".to_string(),
    }
}

/// Decode the bundled 256x256 PNG into an `egui::IconData` for the window and
/// taskbar icon. The bytes are embedded at compile time, so this has no I/O.
fn load_window_icon() -> egui::IconData {
    let bytes = include_bytes!("../assets/icon-256.png");
    let rgba = image::load_from_memory(bytes)
        .expect("bundled assets/icon-256.png must decode")
        .into_rgba8();
    let (width, height) = rgba.dimensions();
    egui::IconData {
        rgba: rgba.into_raw(),
        width,
        height,
    }
}

/// `settings.ini` next to the executable (the C# `dist\` convention), falling
/// back to the working directory when the exe path is unavailable.
fn settings_path() -> String {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|dir| dir.join("settings.ini")))
        .map(|path| path.to_string_lossy().into_owned())
        .unwrap_or_else(|| "settings.ini".to_string())
}

/// One-line config summary for the console `Details` disclosure.
fn config_summary(settings: &Settings) -> String {
    let bnet = if settings.bnet_path.trim().is_empty() {
        "bnet auto-detect".to_string()
    } else {
        format!("bnet {}", settings.bnet_path)
    };
    format!(
        "process {} \u{00B7} cell {}px \u{00B7} poll {}ms \u{00B7} {bnet}",
        settings.process_name, settings.cell_size, settings.poll_interval_ms
    )
}

/// The platform backend for this build: Win32 on Windows, the macOS stub
/// elsewhere (both compile everywhere; the non-native one returns
/// `Unsupported` at runtime).
#[cfg(target_os = "windows")]
fn default_platform() -> mdc_platform_win::WinPlatform {
    mdc_platform_win::WinPlatform::new()
}

#[cfg(not(target_os = "windows"))]
fn default_platform() -> mdc_platform_mac::MacPlatform {
    mdc_platform_mac::MacPlatform::new()
}

fn main() -> eframe::Result {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([720.0, 780.0])
            .with_min_inner_size([460.0, 400.0])
            .with_resizable(true)
            .with_icon(std::sync::Arc::new(load_window_icon()))
            .with_title(WINDOW_TITLE),
        ..Default::default()
    };
    eframe::run_native(
        "companion-rs",
        options,
        Box::new(|cc| {
            theme::apply_theme(&cc.egui_ctx);
            Ok(Box::new(CompanionApp::new()))
        }),
    )
}
