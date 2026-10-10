//! Gallant console — the default `mdc-app` screen
//! (`docs/plans/2026-10-10-gallant-parity.md` §1/§3/§5).
//!
//! The console owns the [`Runtime`] handle plus purely-local UI state
//! (`details_expanded`, the cached [`Snapshot`] and a rolling [`VecDeque`] log
//! mirror). The engine runs on its own thread and only ever publishes through
//! the runtime's shared `Mutex`; egui is **never** touched off the UI thread.
//! The view polls the snapshot at ~10 Hz via `request_repaint_after(100ms)`.
//!
//! Layout: header (title + status pill) · 2-column × 3-row transport grid ·
//! status card (hero, `Details` disclosure, `Now`, rolling `Log`). Enable rules
//! mirror the C# Exp shell (`MainForm.cs:992-997`): `Start` only when stopped,
//! `Pause` toggles running/paused, `Stop` whenever not stopped, `Calibrate`
//! only when stopped or paused (never while running/calibrating), and
//! `OPEN GAME` always. OPEN GAME reuses the runtime launcher, which reads
//! `[Launch] BNetPath` and falls back to the auto-detect hint (`§5`).

use std::collections::VecDeque;

use eframe::egui;
use mdc_runtime::{LogLine, Runtime, Snapshot};

use crate::{theme, widgets};

/// UI log ring size (mirrors `mdc_runtime::LOG_CAPACITY`).
const LOG_CAPACITY: usize = 200;
/// Repaint cadence: 10 Hz.
const REPAINT: std::time::Duration = std::time::Duration::from_millis(100);

/// Placeholder `Now` line while no recommendation is available.
const NOW_IDLE: &str = "idle - waiting for a bridge sample";
/// Placeholder `Log` line while the ring is empty.
const LOG_EMPTY: &str = "No events yet - waiting for bridge samples.";

/// One transport action, wired to a [`Runtime`] method.
#[derive(Clone, Copy)]
enum Action {
    Start,
    Stop,
    TogglePause,
    Calibrate,
    OpenGame,
}

/// Shell actions the console requests after a frame (kept out of the console so
/// it never owns the dock; the shell decides how to open a panel).
#[derive(Clone, Copy, Debug, Default)]
pub struct ConsoleActions {
    /// The header gear button was clicked; open the Settings panel.
    pub open_settings: bool,
}

/// Gallant console view. Holds the engine handle and the local UI state.
pub struct Console {
    runtime: Runtime,
    details_expanded: bool,
    snapshot: Snapshot,
    log: VecDeque<LogLine>,
    config_summary: String,
}

impl Console {
    /// Wraps a live [`Runtime`]. `config_summary` is the `Details` config line
    /// (process / cell / poll / BNet path) resolved once from settings.
    pub fn new(runtime: Runtime, config_summary: String) -> Self {
        let mut console = Self {
            runtime,
            details_expanded: false,
            snapshot: Snapshot::default(),
            log: VecDeque::with_capacity(LOG_CAPACITY),
            config_summary,
        };
        console.refresh();
        console
    }

    /// Polls the engine snapshot and asks egui for the next 10 Hz repaint.
    /// Called once per frame, before any painting; never blocks the engine.
    pub fn tick(&mut self, ctx: &egui::Context) {
        self.refresh();
        ctx.request_repaint_after(REPAINT);
    }

    /// The latest engine snapshot (owned/cached by the console).
    pub fn snapshot(&self) -> &Snapshot {
        &self.snapshot
    }

    fn refresh(&mut self) {
        self.snapshot = self.runtime.snapshot();
        // Reconcile the local rolling ring only when the engine's log changed,
        // so the UI keeps its own capacity-200 `VecDeque`.
        if !self.log.iter().eq(self.snapshot.log.iter()) {
            self.log = self.snapshot.log.iter().cloned().collect();
            while self.log.len() > LOG_CAPACITY {
                self.log.pop_front();
            }
        }
    }

    fn run(&mut self, action: Action) {
        match action {
            Action::Start => self.runtime.start(),
            Action::Stop => self.runtime.stop(),
            Action::TogglePause => self.runtime.toggle_pause(),
            Action::Calibrate => self.runtime.calibrate(),
            Action::OpenGame => self.runtime.open_game(),
        }
        // Read back promptly so the button enablement reflects the request.
        self.refresh();
    }

    /// Renders the whole console into `ui` (the central panel).
    pub fn ui(&mut self, ui: &mut egui::Ui) -> ConsoleActions {
        let mut actions = ConsoleActions::default();
        let state = widgets::RunState::from(self.snapshot.run_state);
        self.header(ui, state, self.snapshot.fps, &mut actions);
        ui.add_space(6.0);
        self.transport(ui, state);
        ui.add_space(6.0);
        self.status_card(ui, state);
        actions
    }

    fn header(
        &self,
        ui: &mut egui::Ui,
        state: widgets::RunState,
        fps: u32,
        actions: &mut ConsoleActions,
    ) {
        ui.horizontal(|ui| {
            ui.label(
                egui::RichText::new("MaxDPS Companion")
                    .size(18.0)
                    .strong()
                    .color(theme::TEXT),
            );
            if ui
                .button(egui::RichText::new("\u{2699} Settings"))
                .clicked()
            {
                actions.open_settings = true;
            }
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                widgets::pill(ui, state, fps);
            });
        });
    }

    fn transport(&mut self, ui: &mut egui::Ui, state: widgets::RunState) {
        let start_enabled = state == widgets::RunState::Stopped;
        let pause_enabled = matches!(
            state,
            widgets::RunState::Running | widgets::RunState::Paused
        );
        let stop_enabled = state != widgets::RunState::Stopped;
        let calibrate_enabled = matches!(
            state,
            widgets::RunState::Stopped | widgets::RunState::Paused
        );
        let paused = state == widgets::RunState::Paused;

        let mut action = None;
        // Tight 2-column x 3-row grid: [Start | Pause] [Stop | Calibrate]
        // [OPEN GAME | -]. A small local item_spacing (saved/restored) plus
        // full-width buttons removes the wide gutter the old fixed 96px
        // min_size + default spacing left between the columns.
        let prev_spacing = ui.spacing().item_spacing;
        ui.spacing_mut().item_spacing = egui::vec2(6.0, 6.0);
        ui.columns(2, |cols| {
            cols[0].vertical(|ui| {
                if start_button(ui, start_enabled).clicked() {
                    action = Some(Action::Start);
                }
                if widgets::transport_button(ui, "Stop", "\u{25A0}", stop_enabled, false).clicked() {
                    action = Some(Action::Stop);
                }
                if widgets::transport_button(ui, "OPEN GAME", "\u{2922}", true, true).clicked() {
                    action = Some(Action::OpenGame);
                }
            });
            cols[1].vertical(|ui| {
                let (label, glyph) = if paused {
                    ("Resume", "\u{25B6}")
                } else {
                    ("Pause", "\u{23F8}")
                };
                if widgets::transport_button(ui, label, glyph, pause_enabled, false).clicked() {
                    action = Some(Action::TogglePause);
                }
                if widgets::transport_button(ui, "Calibrate", "\u{25C9}", calibrate_enabled, false)
                    .clicked()
                {
                    action = Some(Action::Calibrate);
                }
                // Row 3, right cell stays empty on purpose (plan §1).
            });
        });
        ui.spacing_mut().item_spacing = prev_spacing;
        if let Some(action) = action {
            self.run(action);
        }
    }

    fn status_card(&mut self, ui: &mut egui::Ui, state: widgets::RunState) {
        let (hero, hero_color) = self.hero(state);
        let now = if self.snapshot.now.trim().is_empty() {
            NOW_IDLE.to_string()
        } else {
            self.snapshot.now.clone()
        };
        let summary = format!(
            "{} \u{00B7} state {} \u{00B7} {} fps \u{00B7} {} log lines",
            self.config_summary,
            state.label(),
            self.snapshot.fps,
            self.log.len()
        );
        let log_empty = self.log.is_empty();
        let mut toggle = self.details_expanded;

        widgets::card().show(ui, |ui| {
            ui.label(egui::RichText::new(hero).size(18.0).color(hero_color));

            let arrow = if toggle { "\u{25BE}" } else { "\u{25B8}" };
            if ui.button(format!("Details {arrow}")).clicked() {
                toggle = !toggle;
            }
            if toggle {
                ui.label(egui::RichText::new(summary).color(theme::SECONDARY));
            }

            ui.add_space(4.0);
            ui.separator();
            ui.label(
                egui::RichText::new("Now")
                    .strong()
                    .color(theme::SECONDARY),
            );
            ui.label(egui::RichText::new(now).color(theme::TEXT));

            ui.separator();
            ui.label(
                egui::RichText::new("Log")
                    .strong()
                    .color(theme::SECONDARY),
            );
            if log_empty {
                ui.label(
                    egui::RichText::new(LOG_EMPTY)
                        .font(theme::mono(13.0))
                        .color(theme::MONO_STAMP),
                );
            } else {
                egui::ScrollArea::vertical()
                    .stick_to_bottom(true)
                    .max_height(220.0)
                    .show(ui, |ui| {
                        for line in &self.log {
                            ui.horizontal(|ui| {
                                ui.label(
                                    egui::RichText::new(line.ts.as_str())
                                        .font(theme::mono(13.0))
                                        .color(theme::MONO_STAMP),
                                );
                                ui.label(
                                    egui::RichText::new(line.text.as_str())
                                        .font(theme::mono(13.0))
                                        .color(theme::TEXT),
                                );
                            });
                        }
                    });
            }
        });

        self.details_expanded = toggle;
    }

    /// Short human hero phrase, mirroring `MainForm.cs:3094-3099`.
    fn hero(&self, state: widgets::RunState) -> (String, egui::Color32) {
        match state {
            widgets::RunState::Stopped => ("stopped".to_string(), theme::SECONDARY),
            widgets::RunState::Calibrating => ("calibrating\u{2026}".to_string(), theme::ACCENT),
            widgets::RunState::Paused => ("paused".to_string(), theme::ACCENT),
            widgets::RunState::Running => {
                if self.snapshot.now.trim().is_empty() {
                    ("waiting for suggestion".to_string(), theme::OK)
                } else {
                    (self.snapshot.now.clone(), theme::OK)
                }
            }
        }
    }
}

/// `\u{25B6} Start` in the emphasized accent treatment with **dark** text
/// (plan §1). Fills its column width and drops the glyph when the font cannot
/// render it (no tofu); see [`theme::has_glyphs`].
fn start_button(ui: &mut egui::Ui, enabled: bool) -> egui::Response {
    let glyph = if theme::has_glyphs(ui.ctx(), "\u{25B6}") {
        "\u{25B6} "
    } else {
        ""
    };
    let button = egui::Button::new(
        egui::RichText::new(format!("{glyph}Start"))
            .size(14.0)
            .color(theme::BG),
    )
    .fill(theme::ACCENT)
    .stroke(egui::Stroke::new(1.0, theme::ACCENT))
    .min_size(egui::vec2(ui.available_width(), theme::CONTROL_HEIGHT))
    .corner_radius(egui::CornerRadius::same(theme::RADIUS_OUTER));
    ui.add_enabled(enabled, button)
}
