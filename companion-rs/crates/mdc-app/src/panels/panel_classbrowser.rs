//! Class Browser panel — the 8 action slots with enable state + suggestion.
//!
//! `mdc-engine` exposes only the slot contract (no class/spec registry yet), so
//! this is the honest view: the wire slot order used by `[Spells]`
//! (`Main, Offensive, Defensive, Consumable, Trinket, Interrupt, Mobility,
//! SelfHeal`), each with an enable checkbox writing `settings.slot_enabled`
//! back, and a `now` marker on the slot the runtime last selected
//! (`Snapshot::suggested_slot`). No fake class data and no code dump.

use eframe::egui;

use super::{PanelEnv, SLOT_NAMES};
use crate::theme;

#[derive(Default)]
pub struct ClassBrowserPanel {
    suggested: Option<String>,
}

impl ClassBrowserPanel {
    pub fn ui(&mut self, ui: &mut egui::Ui, env: &mut PanelEnv) {
        self.suggested = env.snapshot.suggested_slot.clone();

        ui.heading("Class Browser");
        ui.label(
            egui::RichText::new(
                "Class-specific spell knowledge is not ported to the Rust engine yet \
                 (mdc-engine exposes the 8-slot contract only). Showing the action slots.",
            )
            .color(theme::SECONDARY),
        );
        ui.separator();

        let now = if env.snapshot.now.trim().is_empty() {
            "idle - waiting for a bridge sample".to_string()
        } else {
            env.snapshot.now.clone()
        };
        ui.horizontal(|ui| {
            ui.label(egui::RichText::new("Current:").strong().color(theme::SECONDARY));
            ui.label(egui::RichText::new(now).color(theme::TEXT));
        });
        ui.separator();

        egui::Grid::new("class_browser_slots")
            .num_columns(3)
            .spacing([16.0, 6.0])
            .show(ui, |ui| {
                ui.label(egui::RichText::new("Slot").strong().color(theme::SECONDARY));
                ui.label(egui::RichText::new("Enabled").strong().color(theme::SECONDARY));
                ui.label(egui::RichText::new("Suggested").strong().color(theme::SECONDARY));
                ui.end_row();

                for (i, name) in SLOT_NAMES.iter().enumerate() {
                    ui.label(format!("{}. {name}", i + 1));
                    ui.checkbox(&mut env.settings.slot_enabled[i], "");
                    if self.suggested.as_deref() == Some(*name) {
                        ui.label(egui::RichText::new("now").color(theme::OK));
                    } else {
                        ui.label("");
                    }
                    ui.end_row();
                }
            });

        ui.separator();
        ui.label(
            egui::RichText::new(
                "Enable flags write [Spells] spell1..spell8 on the Settings panel's Save.",
            )
            .font(theme::mono(12.0))
            .color(theme::MONO_STAMP),
        );
    }
}
