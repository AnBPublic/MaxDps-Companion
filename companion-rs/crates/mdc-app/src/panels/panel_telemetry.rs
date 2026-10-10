//! Telemetry panel — local-only JSONL recording, live log tail and replay.
//!
//! `Start` creates a [`mdc_telemetry::JsonlRecorder`] and writes a session-open
//! event; `Stop` writes the session-close event and flushes. The panel shows the
//! runtime snapshot's last ≤50 log lines live, and `Replay` reads a JSONL file
//! back via [`mdc_telemetry::replay`], summarising counts rather than dumping
//! raw records. Telemetry never leaves the machine (`LOCAL_ONLY`).

use eframe::egui;
use mdc_telemetry::{replay, JsonlRecorder, TelemetryEvent, LOCAL_ONLY, SCHEMA_VERSION};

use super::{exe_dir, PanelEnv, APP_VERSION};
use crate::theme;

/// Live log tail length.
const TAIL: usize = 50;

pub struct TelemetryPanel {
    recorder: Option<JsonlRecorder>,
    record_path: String,
    replay_path: String,
    message: String,
    message_ok: bool,
    replay_summary: String,
}

impl Default for TelemetryPanel {
    fn default() -> Self {
        let path = std::path::Path::new(&exe_dir()).join("telemetry-session.jsonl");
        let path = path.to_string_lossy().into_owned();
        Self {
            recorder: None,
            record_path: path.clone(),
            replay_path: path,
            message: String::new(),
            message_ok: true,
            replay_summary: String::new(),
        }
    }
}

impl TelemetryPanel {
    pub fn ui(&mut self, ui: &mut egui::Ui, env: &mut PanelEnv) {
        let recording = self.recorder.is_some();

        ui.heading("Telemetry");
        ui.label(
            egui::RichText::new(format!(
                "local-only={LOCAL_ONLY} \u{00B7} schema v{SCHEMA_VERSION} \u{00B7} no network"
            ))
            .color(theme::SECONDARY),
        );
        ui.separator();

        ui.horizontal(|ui| {
            ui.label("File");
            ui.text_edit_singleline(&mut self.record_path);
        });
        ui.horizontal(|ui| {
            if recording {
                if ui.button("Stop recording").clicked() {
                    self.stop(env);
                }
            } else if ui.button("Start recording").clicked() {
                self.start(env);
            }
            let (label, color) = if recording {
                ("recording", theme::OK)
            } else {
                ("stopped", theme::SECONDARY)
            };
            ui.label(egui::RichText::new(format!("session: {label}")).color(color));
        });
        if let Some(rec) = &self.recorder {
            ui.label(
                egui::RichText::new(format!(
                    "writing {} ({} lines)",
                    rec.path().display(),
                    rec.appended()
                ))
                .font(theme::mono(12.0))
                .color(theme::MONO_STAMP),
            );
        }

        ui.separator();
        ui.label(egui::RichText::new("Live log (\u{2264}50)").strong().color(theme::SECONDARY));
        let log = &env.snapshot.log;
        let start = log.len().saturating_sub(TAIL);
        egui::ScrollArea::vertical()
            .stick_to_bottom(true)
            .max_height(200.0)
            .show(ui, |ui| {
                if log.is_empty() {
                    ui.label(
                        egui::RichText::new("No events yet - waiting for bridge samples.")
                            .font(theme::mono(13.0))
                            .color(theme::MONO_STAMP),
                    );
                }
                for line in &log[start..] {
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

        ui.separator();
        ui.label(egui::RichText::new("Replay").strong().color(theme::SECONDARY));
        ui.horizontal(|ui| {
            ui.text_edit_singleline(&mut self.replay_path);
            if ui.button("Replay").clicked() {
                self.do_replay(env);
            }
        });
        if !self.replay_summary.is_empty() {
            ui.label(egui::RichText::new(&self.replay_summary).color(theme::TEXT));
        }

        ui.separator();
        let color = if self.message_ok { theme::OK } else { theme::DANGER };
        if !self.message.is_empty() {
            ui.label(egui::RichText::new(&self.message).color(color));
        }
    }

    fn start(&mut self, env: &mut PanelEnv) {
        match JsonlRecorder::create(&self.record_path) {
            Ok(mut rec) => {
                let event = TelemetryEvent::session(0, APP_VERSION, 5, 0, true, "");
                match rec.append(&event) {
                    Ok(()) => {
                        let _ = rec.flush();
                        self.message = format!("recording started: {}", self.record_path);
                        self.message_ok = true;
                        self.recorder = Some(rec);
                    }
                    Err(err) => {
                        self.message = format!("write failed: {err}");
                        self.message_ok = false;
                    }
                }
            }
            Err(err) => {
                self.message = format!("open failed: {err}");
                self.message_ok = false;
            }
        }
        env.note(self.message.clone());
    }

    fn stop(&mut self, env: &mut PanelEnv) {
        if let Some(mut rec) = self.recorder.take() {
            let event = TelemetryEvent::session(0, APP_VERSION, 5, 0, false, "");
            let _ = rec.append(&event);
            let _ = rec.flush();
            self.message = format!(
                "recording stopped ({} lines): {}",
                rec.appended(),
                rec.path().display()
            );
            self.message_ok = true;
        }
        env.note(self.message.clone());
    }

    fn do_replay(&mut self, env: &mut PanelEnv) {
        match replay(&self.replay_path) {
            Ok(events) => {
                let sessions = events
                    .iter()
                    .filter(|event| matches!(event, TelemetryEvent::Session { .. }))
                    .count();
                let sent = events.len() - sessions;
                self.replay_summary = format!(
                    "{} events \u{00B7} {} sessions \u{00B7} {} sends",
                    events.len(),
                    sessions,
                    sent
                );
                self.message = format!("replayed {}", self.replay_path);
                self.message_ok = true;
            }
            Err(err) => {
                self.replay_summary.clear();
                self.message = format!("replay failed: {err}");
                self.message_ok = false;
            }
        }
        env.note(self.message.clone());
    }
}
