//! Gallant widgets: status pill, transport button and card frame.
//!
//! Shared by the console layout (task C). Colours, radii and the control
//! height come from [`crate::theme`] so the pill, buttons and cards cannot
//! drift from the tokens.

use eframe::egui;

use crate::theme;

/// Transport state shown by the status pill. Mirrors the plan §3 state set.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum RunState {
    Stopped,
    Calibrating,
    Running,
    Paused,
}

impl RunState {
    /// Display label, e.g. `Running`.
    pub fn label(self) -> &'static str {
        match self {
            Self::Stopped => "Stopped",
            Self::Calibrating => "Calibrating",
            Self::Running => "Running",
            Self::Paused => "Paused",
        }
    }

    /// Filled dot for `Running`/`Paused`, hollow for the rest.
    pub fn filled(self) -> bool {
        matches!(self, Self::Running | Self::Paused)
    }

    /// Dot colour: [`theme::OK`] running, [`theme::ACCENT`] paused/calibrating,
    /// [`theme::MONO_STAMP`] stopped.
    pub fn dot_color(self) -> egui::Color32 {
        match self {
            Self::Running => theme::OK,
            Self::Paused | Self::Calibrating => theme::ACCENT,
            Self::Stopped => theme::MONO_STAMP,
        }
    }
}

/// The runtime engine state maps 1:1 onto the console state set (plan §3).
impl From<mdc_runtime::RunState> for RunState {
    fn from(state: mdc_runtime::RunState) -> Self {
        match state {
            mdc_runtime::RunState::Stopped => RunState::Stopped,
            mdc_runtime::RunState::Calibrating => RunState::Calibrating,
            mdc_runtime::RunState::Running => RunState::Running,
            mdc_runtime::RunState::Paused => RunState::Paused,
        }
    }
}

/// Rounded card frame: [`theme::CARD`] fill, [`theme::BORDER`] stroke,
/// radius [`theme::RADIUS_OUTER`].
pub fn card() -> egui::Frame {
    egui::Frame::default()
        .fill(theme::CARD)
        .stroke(egui::Stroke::new(1.0, theme::BORDER))
        .corner_radius(egui::CornerRadius::same(theme::RADIUS_OUTER))
        .inner_margin(egui::Margin::same(12))
}

/// Status pill: `{●|○} {State}·{fps}` inside a rounded card frame.
pub fn pill(ui: &mut egui::Ui, state: RunState, fps: u32) -> egui::Response {
    let dot = if state.filled() { "●" } else { "○" };
    card()
        .corner_radius(egui::CornerRadius::same(theme::RADIUS_INNER))
        .inner_margin(egui::Margin::symmetric(10, 4))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(egui::RichText::new(dot).color(state.dot_color()));
                ui.label(
                    egui::RichText::new(format!(" {}·{}", state.label(), fps))
                        .color(theme::TEXT),
                );
            });
        })
        .response
}

/// Transport button, [`theme::CONTROL_HEIGHT`] high with an optional glyph.
///
/// The button fills the full width available in its column (no fixed 96px floor,
/// so a 2-column grid has no dead gutter), and the glyph is dropped when the
/// loaded fonts cannot render it (see [`theme::has_glyphs`]) so a missing
/// system symbol font shows the text label rather than a tofu box.
///
/// `accent` paints the emphasized treatment (e.g. `OPEN GAME`); it is ignored
/// while `enabled` is false so a disabled button stays visually muted.
pub fn transport_button(
    ui: &mut egui::Ui,
    label: &str,
    glyph: &str,
    enabled: bool,
    accent: bool,
) -> egui::Response {
    let glyph = if glyph.is_empty() || !theme::has_glyphs(ui.ctx(), glyph) {
        ""
    } else {
        glyph
    };
    let text = if glyph.is_empty() {
        label.to_string()
    } else {
        format!("{glyph} {label}")
    };
    let mut button = egui::Button::new(egui::RichText::new(text).size(14.0))
        .min_size(egui::vec2(ui.available_width(), theme::CONTROL_HEIGHT))
        .corner_radius(egui::CornerRadius::same(theme::RADIUS_OUTER));
    if accent && enabled {
        button = button
            .fill(theme::ACCENT)
            .stroke(egui::Stroke::new(1.0, theme::ACCENT));
    }
    ui.add_enabled(enabled, button)
}
