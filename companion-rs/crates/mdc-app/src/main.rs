//! `mdc-app` — thin `eframe`/`egui` shell for egui 0.36 (root-`Ui` layout).
//!
//! The UI holds almost no logic: every action routes through the shared
//! [`mdc_commands::CommandRegistry`]. Layout is a dense pro-panel tool frame —
//! top menu, left tool rail, centre dock canvas (`egui_dock`), bottom status
//! bar and a Ctrl+K command palette. Dark theme, no branding.

#![forbid(unsafe_code)]

use eframe::egui;
use egui_dock::{DockArea, DockState, TabViewer};
use mdc_commands::{default_registry, CommandRegistry};
use serde_json::json;

#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
enum Panel {
    Console,
    ClassBrowser,
    Doctor,
    Telemetry,
    Settings,
}

impl Panel {
    const ALL: [Panel; 5] = [
        Panel::Console,
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

    fn command_id(self) -> &'static str {
        match self {
            Panel::Console => "ui.panel.console",
            Panel::ClassBrowser => "ui.panel.class_browser",
            Panel::Doctor => "ui.panel.doctor",
            Panel::Telemetry => "ui.panel.telemetry",
            Panel::Settings => "ui.panel.settings",
        }
    }
}

struct CompanionApp {
    registry: CommandRegistry,
    dock: DockState<Panel>,
    palette_open: bool,
    palette_query: String,
    last_result: String,
    status: String,
}

impl CompanionApp {
    fn new() -> Self {
        let mut dock = DockState::new(vec![Panel::Console]);
        dock.push_to_focused_leaf(Panel::Doctor);
        Self {
            registry: default_registry(),
            dock,
            palette_open: false,
            palette_query: String::new(),
            last_result: "(no command run yet)".to_string(),
            status: "idle".to_string(),
        }
    }

    fn run(&mut self, id: &str, input: serde_json::Value) {
        self.last_result = self.registry.invoke(id, &input.to_string());
        self.status = format!("ran {id}");
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
        for panel in Panel::ALL {
            if ui.button(panel.title()).clicked() {
                self.dock.push_to_focused_leaf(panel);
                let id = panel.command_id();
                self.run(id, json!({}));
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
                            self.run(id, json!({}));
                        }
                    }
                });
                ui.separator();
                ui.monospace(&self.last_result);
            });
        self.palette_open = open;
    }
}

impl eframe::App for CompanionApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        self.toggle_palette(ui.ctx());

        egui::Panel::top("menu").show(ui, |ui| {
            egui::MenuBar::new().ui(ui, |ui| {
                if ui.button("Command Palette").clicked() {
                    self.palette_open = true;
                }
                ui.separator();
                if ui.button("Doctor").clicked() {
                    self.run("doctor", json!({}));
                }
                if ui.button("Audit").clicked() {
                    self.run("audit", json!({}));
                }
            });
        });

        egui::Panel::left("rail").default_size(180.0).show(ui, |ui| self.ui_rail(ui));

        egui::Panel::bottom("status").show(ui, |ui| self.ui_status(ui));

        self.ui_palette(ui.ctx());

        egui::CentralPanel::default().show(ui, |ui| {
            let Self { dock, registry, last_result, status, .. } = self;
            DockArea::new(dock).show_inside(ui, &mut Viewer { registry, last_result, status });
        });
    }
}

/// Dock tab renderer. Panels are stubs that call the registry.
struct Viewer<'a> {
    registry: &'a CommandRegistry,
    last_result: &'a mut String,
    status: &'a mut String,
}

impl TabViewer for Viewer<'_> {
    type Tab = Panel;

    fn id(&mut self, tab: &mut Panel) -> egui::Id {
        egui::Id::new(tab.title())
    }

    fn title(&mut self, tab: &mut Panel) -> egui::WidgetText {
        tab.title().to_string().into()
    }

    fn ui(&mut self, ui: &mut egui::Ui, tab: &mut Panel) {
        let id = tab.command_id();
        ui.heading(tab.title());
        ui.label("Panel stub — routed through the shared command registry.");
        ui.separator();
        if ui.button("Run panel command").clicked() {
            *self.last_result = self.registry.invoke(id, "{}");
            *self.status = format!("ran {id}");
        }
        ui.monospace(&*self.last_result);
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

fn configure_theme(ctx: &egui::Context) {
    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = egui::Color32::from_rgb(24, 26, 30);
    visuals.window_fill = egui::Color32::from_rgb(30, 33, 38);
    visuals.faint_bg_color = egui::Color32::from_rgb(33, 36, 42);
    visuals.selection.bg_fill = egui::Color32::from_rgb(38, 92, 140);
    visuals.selection.stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(120, 180, 240));
    ctx.set_visuals(visuals);
}

fn main() -> eframe::Result {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1280.0, 800.0])
            .with_min_inner_size([900.0, 600.0])
            .with_icon(std::sync::Arc::new(load_window_icon()))
            .with_title("Companion (Rust port)"),
        ..Default::default()
    };
    eframe::run_native(
        "companion-rs",
        options,
        Box::new(|cc| {
            configure_theme(&cc.egui_ctx);
            Ok(Box::new(CompanionApp::new()))
        }),
    )
}
