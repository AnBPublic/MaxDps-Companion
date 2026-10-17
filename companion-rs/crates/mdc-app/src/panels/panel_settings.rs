//! Settings panel — a real form over `settings.ini` (`AppSettings.cs` parity).
//!
//! Edits the app's live [`mdc_settings::Settings`] in place, clamps the same
//! fields the C# shell clamps (`MainForm.cs:114-117`: CellSize 2..64, Poll
//! 10..1000, MinKeyInterval 20..5000, KeyPress 0..200), and writes with
//! [`Settings::save`] (preserving unknown keys/comments via [`mdc_settings::IniDocument`]).
//! `Revert` reloads from disk. Status is a plain sentence, never JSON.

use eframe::egui;
use mdc_settings::{Settings, TtkFallback};

use super::{PanelEnv, SLOT_NAMES};
use crate::theme;

#[derive(Default)]
pub struct SettingsPanel {
    message: String,
    message_ok: bool,
}

impl SettingsPanel {
    /// Renders the form. `Save` persists; `Revert` reloads from `settings.ini`.
    pub fn ui(&mut self, ui: &mut egui::Ui, env: &mut PanelEnv) {
        ui.horizontal(|ui| {
            ui.heading("Settings");
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                if ui.button("Save").clicked() {
                    self.save(env);
                }
                if ui.button("Revert").clicked() {
                    self.revert(env);
                }
            });
        });
        ui.label(
            egui::RichText::new(format!("file: {}", env.settings_path))
                .font(theme::mono(12.0))
                .color(theme::MONO_STAMP),
        );
        ui.separator();

        egui::ScrollArea::vertical().show(ui, |ui| self.form(ui, env));

        ui.separator();
        let (text, color) = if self.message.is_empty() {
            ("no changes saved yet".to_string(), theme::SECONDARY)
        } else if self.message_ok {
            (self.message.clone(), theme::OK)
        } else {
            (self.message.clone(), theme::DANGER)
        };
        ui.label(egui::RichText::new(text).color(color));
    }

    fn form(&mut self, ui: &mut egui::Ui, env: &mut PanelEnv) {
        let s = &mut *env.settings;

        section(ui, "Window");
        field(ui, "Process name", |ui| {
            ui.text_edit_singleline(&mut s.process_name);
        });

        section(ui, "Bridge");
        int_field(ui, "Cell size (px)", &mut s.cell_size, 2..=64);

        section(ui, "Timing");
        int_field(ui, "Poll interval (ms)", &mut s.poll_interval_ms, 10..=1000);
        int_field(ui, "Min key gap (ms)", &mut s.min_key_interval_ms, 20..=5000);
        int_field(ui, "Key hold (ms)", &mut s.key_press_ms, 0..=200);

        section(ui, "Spells (action slots)");
        for (i, name) in SLOT_NAMES.iter().enumerate() {
            ui.checkbox(&mut s.slot_enabled[i], *name);
        }

        section(ui, "Targeting");
        ui.checkbox(&mut s.combat_only, "CombatOnly (hold out of combat)");

        section(ui, "Scheduler");
        ui.checkbox(&mut s.scheduler_enabled, "Scheduler enabled");
        int_field(
            ui,
            "Heartbeat timeout (ms)",
            &mut s.scheduler_heartbeat_timeout_ms,
            100..=5000,
        );
        int_field(
            ui,
            "Repeat suppress (ms)",
            &mut s.scheduler_repeat_suppress_ms,
            100..=5000,
        );

        section(ui, "Intelligence");
        ui.checkbox(&mut s.intelligence_enabled, "Intelligence enabled");
        ui.checkbox(&mut s.hp_curve, "Paint HP curve strip");

        section(ui, "Time to Kill");
        ui.checkbox(&mut s.ttk_enabled, "TTK estimator enabled");
        ui.checkbox(&mut s.ttk_history, "Adaptive TTK history");
        ui.horizontal(|ui| {
            ui.label("Unknown-TTK fallback");
            let current = match s.ttk_fallback {
                TtkFallback::FailOpen => "FailOpen",
                TtkFallback::ConserveMajors => "ConserveMajors",
            };
            egui::ComboBox::from_id_salt("ttk_fallback")
                .selected_text(current)
                .show_ui(ui, |ui| {
                    ui.selectable_value(&mut s.ttk_fallback, TtkFallback::FailOpen, "FailOpen");
                    ui.selectable_value(
                        &mut s.ttk_fallback,
                        TtkFallback::ConserveMajors,
                        "ConserveMajors",
                    );
                });
        });

        section(ui, "Launch");
        field(ui, "Battle.net path", |ui| {
            ui.text_edit_singleline(&mut s.bnet_path);
        });
    }

    fn save(&mut self, env: &mut PanelEnv) {
        clamp(env.settings);
        match env.settings.save() {
            Ok(()) => {
                self.message = format!("saved {}", env.settings.path);
                self.message_ok = true;
            }
            Err(err) => {
                self.message = format!("save failed: {err}");
                self.message_ok = false;
            }
        }
        env.note(self.message.clone());
    }

    fn revert(&mut self, env: &mut PanelEnv) {
        *env.settings = Settings::load(env.settings_path.to_string());
        self.message = "reverted to settings.ini".to_string();
        self.message_ok = true;
        env.note(self.message.clone());
    }
}

/// Section heading in the accent colour.
fn section(ui: &mut egui::Ui, title: &str) {
    ui.add_space(6.0);
    ui.label(egui::RichText::new(title).strong().color(theme::ACCENT));
}

/// A label + free-text field row.
fn field(ui: &mut egui::Ui, label: &str, add: impl FnOnce(&mut egui::Ui)) {
    ui.horizontal(|ui| {
        ui.label(label);
        add(ui);
    });
}

/// A label + clamped integer spinner row (`DragValue`).
fn int_field(ui: &mut egui::Ui, label: &str, value: &mut i32, range: std::ops::RangeInclusive<i32>) {
    ui.horizontal(|ui| {
        ui.label(label);
        ui.add(egui::DragValue::new(value).range(range).speed(1.0));
    });
}

/// Applies the C# shell's field clamps before persisting (`MainForm.cs:114-117`).
fn clamp(s: &mut Settings) {
    s.cell_size = s.cell_size.clamp(2, 64);
    s.poll_interval_ms = s.poll_interval_ms.clamp(10, 1000);
    s.min_key_interval_ms = s.min_key_interval_ms.clamp(20, 5000);
    s.key_press_ms = s.key_press_ms.clamp(0, 200);
    s.scheduler_heartbeat_timeout_ms = s.scheduler_heartbeat_timeout_ms.clamp(100, 5000);
    s.scheduler_repeat_suppress_ms = s.scheduler_repeat_suppress_ms.clamp(100, 5000);
}
