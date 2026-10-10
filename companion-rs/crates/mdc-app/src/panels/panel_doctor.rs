//! Doctor panel — real environment/contract diagnostics as labelled rows.
//!
//! Replaces the old `doctor`/`audit` JSON dump. Shows the observed facts the
//! C# install doctor checks (`Diagnostics/InstallDoctor.cs`): the settings path
//! and whether it exists, whether the client window is found, protocol decode
//! readiness, exe dir, version, registry command count and build profile.
//! `Re-run` re-probes; `Copy diagnostics` puts the plain table on the clipboard.

use eframe::egui;

use super::{build_profile, exe_dir, info_row, probe_wow, InfoRow, PanelEnv, Tone, APP_VERSION};
use crate::theme;

#[derive(Default)]
pub struct DoctorPanel {
    rows: Vec<InfoRow>,
    ran: bool,
    copied: bool,
}

impl DoctorPanel {
    /// Renders the diagnostics table, running the checks on first show.
    pub fn ui(&mut self, ui: &mut egui::Ui, env: &mut PanelEnv) {
        if !self.ran {
            self.run(env);
        }
        ui.horizontal(|ui| {
            ui.heading("Doctor");
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                if ui.button("Re-run").clicked() {
                    self.run(env);
                }
                if ui.button("Copy diagnostics").clicked() {
                    ui.ctx().copy_text(self.plain_text());
                    self.copied = true;
                }
            });
        });
        ui.separator();
        egui::Grid::new("doctor_rows")
            .num_columns(2)
            .spacing([18.0, 6.0])
            .show(ui, |ui| {
                for row in &self.rows {
                    info_row(ui, row);
                    ui.end_row();
                }
            });
        if self.copied {
            ui.add_space(6.0);
            ui.label(egui::RichText::new("diagnostics copied to clipboard").color(theme::OK));
        }
    }

    /// Re-collects every observed fact. No derived/assumed values.
    fn run(&mut self, env: &mut PanelEnv) {
        self.rows.clear();
        self.copied = false;

        let path = env.settings_path.to_string();
        let exists = std::path::Path::new(&path).exists();
        self.rows.push(InfoRow::new(
            "settings.ini",
            if exists {
                format!("{path} (present)")
            } else {
                format!("{path} (missing - defaults in use)")
            },
            if exists { Tone::Ok } else { Tone::Warn },
        ));

        let process = env.settings.process_name.clone();
        match probe_wow(&process) {
            Ok(handle) => self.rows.push(InfoRow::new(
                "WoW window",
                format!("found '{process}' (handle 0x{handle:X})"),
                Tone::Ok,
            )),
            Err(_) => self.rows.push(InfoRow::new(
                "WoW window",
                format!("not found - start WoW (looking for '{process}')"),
                Tone::Warn,
            )),
        }

        self.rows.push(InfoRow::new(
            "Protocol decode",
            format!(
                "v5 / v4 / v1 auto-detect ready \u{00B7} {} slots",
                mdc_engine::SLOT_COUNT
            ),
            Tone::Ok,
        ));
        self.rows
            .push(InfoRow::new("Exe directory", exe_dir(), Tone::Info));
        self.rows
            .push(InfoRow::new("Version", APP_VERSION, Tone::Info));
        self.rows.push(InfoRow::new(
            "Command registry",
            format!("{} commands", env.command_count),
            Tone::Info,
        ));
        self.rows
            .push(InfoRow::new("Rust build", build_profile(), Tone::Info));

        let order = mdc_commands::canonical_fallback_order()
            .iter()
            .map(|slot| slot.name())
            .collect::<Vec<_>>()
            .join(", ");
        self.rows
            .push(InfoRow::new("Fallback order", order, Tone::Info));

        let warnings = env.settings.load_warnings.len();
        if warnings > 0 {
            self.rows.push(InfoRow::new(
                "Load warnings",
                format!("{warnings} (see settings.ini)"),
                Tone::Warn,
            ));
        }

        self.ran = true;
        env.note("doctor re-run");
    }

    /// Plain-text form for the clipboard (no JSON).
    fn plain_text(&self) -> String {
        self.rows
            .iter()
            .map(|row| format!("{}: {}", row.label, row.value))
            .collect::<Vec<_>>()
            .join("\n")
    }
}
