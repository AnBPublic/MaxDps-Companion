//! `settings.ini` compatibility for the Rust companion.
//!
//! Key-for-key and clamp-for-clamp port of `app/MaxDpsCompanion/AppSettings.cs`
//! (`Load`, `Apply`, `Save`). The one deliberate difference: the C#
//! `Save` rewrites a canonical file and **drops** anything it does not know
//! about, while this writer edits the loaded document in place and therefore
//! **keeps unknown keys, comments and ordering**.
//!
//! Design:
//! - [`IniDocument`] is an order-preserving view of the `.ini` text.
//! - [`Settings`] carries the typed, clamped values plus the loaded document.
//!   [`Settings::save`] writes the current typed values back over that document.
//!
//! Section/key names and defaults mirror `settings.ini` and `AppSettings.cs`.

#![forbid(unsafe_code)]

use std::fmt::Write as _;

/// Companion-side rotation preset (`[Rotation] Mode`).
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum RotationPreset {
    /// Historical behaviour: every gate unchanged.
    #[default]
    Full,
    /// Hold majors/consumable/trinket until a boss TTK is measurable.
    Burst,
}

impl RotationPreset {
    fn parse(value: &str) -> Self {
        if value.trim().eq_ignore_ascii_case("burst") {
            RotationPreset::Burst
        } else {
            RotationPreset::Full
        }
    }

    fn as_str(self) -> &'static str {
        match self {
            RotationPreset::Burst => "Burst",
            RotationPreset::Full => "Full",
        }
    }
}

/// Companion-side target preset (`[Rotation] Targets`).
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum TargetPreset {
    /// Historical behaviour.
    #[default]
    SingleTarget,
    /// Conserve catalogued single-target-only offensive cooldowns.
    Aoe,
}

impl TargetPreset {
    fn parse(value: &str) -> Self {
        match value.trim().to_ascii_lowercase().as_str() {
            "aoe" | "ae" | "multi" => TargetPreset::Aoe,
            _ => TargetPreset::SingleTarget,
        }
    }

    fn as_str(self) -> &'static str {
        match self {
            TargetPreset::Aoe => "Aoe",
            TargetPreset::SingleTarget => "SingleTarget",
        }
    }
}

/// `[TimeToKill] Fallback` (spec §4).
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum TtkFallback {
    /// Unknown TTK never holds a cooldown (historical/permissive).
    #[default]
    FailOpen,
    /// Hold any unknown-TTK major without needing a fast-pack latch.
    ConserveMajors,
}

impl TtkFallback {
    fn parse(value: &str) -> Self {
        match value.trim().to_ascii_lowercase().as_str() {
            "conservemajors" | "conserve" => TtkFallback::ConserveMajors,
            _ => TtkFallback::FailOpen,
        }
    }

    fn as_str(self) -> &'static str {
        match self {
            TtkFallback::ConserveMajors => "ConserveMajors",
            TtkFallback::FailOpen => "FailOpen",
        }
    }
}

// ===========================================================================
// Lossless INI document
// ===========================================================================

#[derive(Clone, Debug, PartialEq, Eq)]
enum IniLine {
    Blank,
    Comment(String),
    Section { name: String },
    Entry { section: String, key: String, value: String },
    Other(String),
}

impl IniLine {
    fn render(&self) -> String {
        match self {
            IniLine::Blank => String::new(),
            IniLine::Comment(raw) | IniLine::Other(raw) => raw.clone(),
            IniLine::Section { name } => format!("[{name}]"),
            IniLine::Entry { key, value, .. } => format!("{key}={value}"),
        }
    }
}

/// An order-preserving `.ini` document.
///
/// Unknown keys, comment lines (`;`/`#`) and blank lines survive a
/// parse/render round-trip; whitespace around `=` is normalised (matching
/// `AppSettings.Load`'s key/value trim). [`IniDocument::set`] updates an
/// existing key in place or appends it to the right section.
#[derive(Clone, Debug, PartialEq, Eq, Default)]
pub struct IniDocument {
    lines: Vec<IniLine>,
}

impl IniDocument {
    /// Parses `.ini` text. Tolerant: a malformed line is kept verbatim.
    pub fn parse(text: &str) -> Self {
        let mut lines = Vec::new();
        let mut section = String::new();

        for raw in text.split('\n') {
            let raw = raw.strip_suffix('\r').unwrap_or(raw);
            let trimmed = raw.trim();

            if trimmed.is_empty() {
                lines.push(IniLine::Blank);
            } else if trimmed.starts_with(';') || trimmed.starts_with('#') {
                lines.push(IniLine::Comment(raw.to_string()));
            } else if trimmed.starts_with('[') && trimmed.ends_with(']') {
                let name = trimmed[1..trimmed.len() - 1].trim().to_string();
                section = name.clone();
                lines.push(IniLine::Section { name });
            } else if let Some(eq) = trimmed.find('=') {
                if eq == 0 {
                    lines.push(IniLine::Other(raw.to_string()));
                } else {
                    lines.push(IniLine::Entry {
                        section: section.clone(),
                        key: trimmed[..eq].trim().to_string(),
                        value: trimmed[eq + 1..].trim().to_string(),
                    });
                }
            } else {
                lines.push(IniLine::Other(raw.to_string()));
            }
        }

        Self { lines }
    }

    /// True when a `[Section]` header exists (case-insensitive).
    pub fn has_section(&self, section: &str) -> bool {
        self.lines.iter().any(|line| {
            matches!(line, IniLine::Section { name } if name.eq_ignore_ascii_case(section))
        })
    }

    /// Looks up a key inside `section` (both case-insensitive). First match wins.
    pub fn get(&self, section: &str, key: &str) -> Option<&str> {
        self.lines.iter().find_map(|line| match line {
            IniLine::Entry { section: s, key: k, value }
                if s.eq_ignore_ascii_case(section) && k.eq_ignore_ascii_case(key) =>
            {
                Some(value.as_str())
            }
            _ => None,
        })
    }

    /// Sets `key` in `section`: updates the existing entry, or appends one to
    /// that section (creating the section at the end when missing).
    pub fn set(&mut self, section: &str, key: &str, value: &str) {
        for line in &mut self.lines {
            if let IniLine::Entry { section: s, key: k, value: v } = line {
                if s.eq_ignore_ascii_case(section) && k.eq_ignore_ascii_case(key) {
                    *v = value.to_string();
                    return;
                }
            }
        }

        match self.section_header_index(section) {
            Some(header) => {
                // Insert before the next section header, skipping trailing blanks
                // so a separator blank line stays attached to the next section.
                let end = self
                    .lines
                    .iter()
                    .enumerate()
                    .skip(header + 1)
                    .find(|(_, line)| matches!(line, IniLine::Section { .. }))
                    .map(|(i, _)| i)
                    .unwrap_or(self.lines.len());
                let mut at = end;
                while at > header + 1 && matches!(self.lines[at - 1], IniLine::Blank) {
                    at -= 1;
                }
                self.lines.insert(
                    at,
                    IniLine::Entry {
                        section: section.to_string(),
                        key: key.to_string(),
                        value: value.to_string(),
                    },
                );
            }
            None => {
                if !self.lines.is_empty() && !matches!(self.lines.last(), Some(IniLine::Blank)) {
                    self.lines.push(IniLine::Blank);
                }
                self.lines.push(IniLine::Section { name: section.to_string() });
                self.lines.push(IniLine::Entry {
                    section: section.to_string(),
                    key: key.to_string(),
                    value: value.to_string(),
                });
            }
        }
    }

    fn section_header_index(&self, section: &str) -> Option<usize> {
        self.lines.iter().position(|line| {
            matches!(line, IniLine::Section { name } if name.eq_ignore_ascii_case(section))
        })
    }

    /// Renders the document back to `.ini` text.
    pub fn render(&self) -> String {
        let mut out = String::new();
        for (i, line) in self.lines.iter().enumerate() {
            if i > 0 {
                out.push('\n');
            }
            out.push_str(&line.render());
        }
        out
    }
}

// ===========================================================================
// Typed settings
// ===========================================================================

const COMBAT_ONLY_WARNING: &str =
    "CombatOnly=0: out-of-combat authority follows the in-game overlay — the companion is \
     permissive and permits OOC only while the addon echoes the Ext3 OOC bit; set CombatOnly=1 \
     to always hold out of combat.";

/// `[Color]` display-chain profile (mirrors `ColorProfile.cs`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ColorSettings {
    /// Measured magic-cell red.
    pub magic_r: i32,
    /// Measured magic-cell green.
    pub magic_g: i32,
    /// Measured magic-cell blue.
    pub magic_b: i32,
    /// Per-channel measured black (pattern level 0).
    pub black: [i32; 3],
    /// Per-channel measured white (pattern level 15).
    pub white: [i32; 3],
    /// Per-channel magic-match tolerance.
    pub tolerance: i32,
    /// When the profile was learned (informational, empty = unlearned).
    pub learned_at: String,
}

impl Default for ColorSettings {
    fn default() -> Self {
        Self {
            magic_r: 255,
            magic_g: 0,
            magic_b: 255,
            black: [0, 0, 0],
            white: [255, 255, 255],
            tolerance: 64, // ColorProfile.DefaultTolerance
            learned_at: String::new(),
        }
    }
}

/// INI-backed settings, compatible with `AppSettings.cs`.
#[derive(Clone, Debug, PartialEq)]
pub struct Settings {
    /// Path this settings object was loaded from / saves to.
    pub path: String,
    /// Non-fatal findings recorded at load time (mirrors `AppSettings.LoadWarnings`).
    pub load_warnings: Vec<String>,

    // [Bridge]
    /// Pixel strip cell size (must match `/mdb cellsize`).
    pub cell_size: i32,
    /// Strip X offset.
    pub offset_x: i32,
    /// Strip Y offset.
    pub offset_y: i32,

    // [Window]
    /// Game process name (`Wow`, `WowClassic`, `Legion`).
    pub process_name: String,
    /// Post keys to a background window.
    pub allow_background_keys: bool,
    /// Saved client width (0 = default).
    pub window_width: i32,
    /// Saved client height (0 = default).
    pub window_height: i32,
    /// Layout tag (empty = default, `classic3` = v3 classic shell).
    pub window_layout: String,
    /// Exp in-game-config shell flag.
    pub in_game_config_mode: bool,

    // [Pause]
    /// Global pause hotkey name.
    pub pause_hotkey: String,

    // [Spells]
    /// Per-slot enable flags: Main, Offensive, Defensive, Consumable, Trinket,
    /// Interrupt, Mobility, SelfHeal.
    pub slot_enabled: [bool; 8],

    // [Timing]
    /// Sampler cadence in ms.
    pub poll_interval_ms: i32,
    /// Minimum gap between keys in ms.
    pub min_key_interval_ms: i32,
    /// Key hold duration in ms.
    pub key_press_ms: i32,

    // [Targeting]
    /// Allow the target-key fallback.
    pub auto_target_enabled: bool,
    /// Fail-closed out-of-combat kill switch.
    pub combat_only: bool,
    /// Target key name.
    pub target_key: String,

    // [Interact]
    /// Allow the interact-key fallback.
    pub interact_enabled: bool,
    /// Interact key name.
    pub interact_key: String,

    // [Scheduler]
    /// Deterministic scheduler on/off.
    pub scheduler_enabled: bool,
    /// Frozen-heartbeat link-loss timeout in ms.
    pub scheduler_heartbeat_timeout_ms: i32,
    /// Repeat suppression window in ms.
    pub scheduler_repeat_suppress_ms: i32,

    // [Intelligence]
    /// Situational intelligence on/off.
    pub intelligence_enabled: bool,
    /// Stale-candidate demotion window in ms.
    pub intelligence_stale_after_ms: i32,
    /// Paint the player HP curve strip.
    pub hp_curve: bool,

    // [TimeToKill]
    /// TTK estimator on/off.
    pub ttk_enabled: bool,
    /// Behaviour when no estimate is available.
    pub ttk_fallback: TtkFallback,
    /// Adaptive real-data history on/off.
    pub ttk_history: bool,
    /// Rolling window size (kills).
    pub ttk_history_kills: i32,
    /// Minimum kills before the learned rate binds.
    pub ttk_history_min_kills: i32,
    /// Maximum history age in seconds.
    pub ttk_history_max_age_sec: i32,
    /// Pessimistic burn-rate percentile.
    pub ttk_history_quantile: i32,
    /// Fraction of an active duration the fight must cover.
    pub ttk_history_dur_factor: f64,
    /// Warmup hold for majors while TTK is unknown.
    pub ttk_warmup_sec: f64,

    // [CrowdControl]
    /// Companion-side CC appendix opt-in.
    pub crowd_control_enabled: bool,

    // [Rotation]
    /// Restrict-only rotation preset.
    pub mode_preset: RotationPreset,
    /// Restrict-only target preset.
    pub target_mode: TargetPreset,

    // [Solo]
    /// Solo / self-sustain mode on/off.
    pub solo_enabled: bool,
    /// Emergency HP% threshold.
    pub solo_emergency_hp_pct: i32,
    /// Self-sustain HP% threshold.
    pub solo_self_sustain_hp_pct: i32,
    /// Defensive-escalation HP% threshold.
    pub solo_defensive_escalate_hp_pct: i32,
    /// Solo HP-banded escalation switch.
    pub solo_escalation_enabled: bool,
    /// Minor-defensive HP% threshold.
    pub solo_minor_hp_pct: i32,
    /// Major-defensive HP% threshold.
    pub solo_major_hp_pct: i32,
    /// Immunity HP% threshold.
    pub solo_immunity_hp_pct: i32,

    // [Abilities]
    /// Explicitly-enabled spell ids (comma-separated).
    pub abilities_on: String,
    /// Explicitly-disabled spell ids (comma-separated).
    pub abilities_off: String,
    /// Optional `id:Mode` pairs.
    pub abilities_modes: String,

    // [AbilityOverrides]
    /// Per-machine override store on/off.
    pub ability_overrides_enabled: bool,
    /// Override store file name.
    pub ability_overrides_file: String,

    // [Telemetry]
    /// Local telemetry opt-in.
    pub telemetry_enabled: bool,
    /// Telemetry ring capacity.
    pub telemetry_capacity: i32,

    // [Color]
    /// Display-chain colour profile.
    pub color: ColorSettings,

    // [Launch]
    /// Battle.net override path (empty = auto-detect).
    pub bnet_path: String,

    // [Meta]
    /// Settings schema version.
    pub config_version: i32,

    /// The loaded document, preserved so unknown keys/comments survive a save.
    pub doc: IniDocument,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            path: "settings.ini".to_string(),
            load_warnings: Vec::new(),
            cell_size: 8,
            offset_x: 0,
            offset_y: 0,
            process_name: "Wow".to_string(),
            allow_background_keys: true,
            window_width: 0,
            window_height: 0,
            window_layout: String::new(),
            in_game_config_mode: false,
            pause_hotkey: "Pause".to_string(),
            slot_enabled: [true, true, true, true, false, false, true, true],
            poll_interval_ms: 33,
            min_key_interval_ms: 120,
            key_press_ms: 25,
            auto_target_enabled: false,
            combat_only: true,
            target_key: "Tab".to_string(),
            interact_enabled: false,
            interact_key: "F".to_string(),
            scheduler_enabled: true,
            scheduler_heartbeat_timeout_ms: 500,
            scheduler_repeat_suppress_ms: 900,
            intelligence_enabled: true,
            intelligence_stale_after_ms: 1500,
            hp_curve: true,
            ttk_enabled: true,
            ttk_fallback: TtkFallback::FailOpen,
            ttk_history: true,
            ttk_history_kills: 8,
            ttk_history_min_kills: 3,
            ttk_history_max_age_sec: 240,
            ttk_history_quantile: 75,
            ttk_history_dur_factor: 0.5,
            ttk_warmup_sec: 3.0, // TtkPolicy.DefaultWarmupSec
            crowd_control_enabled: false,
            mode_preset: RotationPreset::Full,
            target_mode: TargetPreset::SingleTarget,
            solo_enabled: false,
            solo_emergency_hp_pct: 35,
            solo_self_sustain_hp_pct: 65,
            solo_defensive_escalate_hp_pct: 60,
            solo_escalation_enabled: true,
            solo_minor_hp_pct: 75,
            solo_major_hp_pct: 50,
            solo_immunity_hp_pct: 30,
            abilities_on: String::new(),
            abilities_off: String::new(),
            abilities_modes: String::new(),
            ability_overrides_enabled: true,
            ability_overrides_file: "ability-overrides.json".to_string(),
            telemetry_enabled: false,
            telemetry_capacity: 10_000,
            color: ColorSettings::default(),
            bnet_path: String::new(),
            config_version: 1, // AppSettings.ConfigVersion
            doc: IniDocument::default(),
        }
    }
}

impl Settings {
    /// Loads from `path`. A missing file yields the defaults (`AppSettings.Load`
    /// semantics), including the one-time v3.5 `[Spells]`-without-`[Meta]`
    /// migration.
    pub fn load(path: impl Into<String>) -> Self {
        let path = path.into();
        let mut settings = Settings {
            path: path.clone(),
            ..Settings::default()
        };

        let doc = std::fs::read_to_string(&path)
            .map(|text| IniDocument::parse(&text))
            .unwrap_or_default();

        let has_spells = doc.has_section("Spells");
        let has_meta = doc.has_section("Meta");

        settings.apply_from_doc(&doc);

        // v3.5 S1 one-time migration (RC1/RC2): a genuine pre-3.5 config has
        // [Spells] but no [Meta]; turn Mobility and CC ON exactly once.
        if has_spells && !has_meta {
            settings.slot_enabled[6] = true;
            settings.crowd_control_enabled = true;
        }

        if !settings.combat_only {
            settings.load_warnings.push(COMBAT_ONLY_WARNING.to_string());
        }

        settings.doc = doc;
        settings
    }

    /// Writes the current values over the loaded document (preserving unknown
    /// keys/comments) to [`Settings::path`].
    pub fn save(&self) -> std::io::Result<()> {
        std::fs::write(&self.path, self.build_document().render())
    }

    /// Builds the document that [`Settings::save`] would write, without I/O.
    pub fn build_document(&self) -> IniDocument {
        let mut doc = self.doc.clone();

        doc.set("Meta", "ConfigVersion", &self.config_version.to_string());

        doc.set("Bridge", "CellSize", &self.cell_size.to_string());
        doc.set("Bridge", "OffsetX", &self.offset_x.to_string());
        doc.set("Bridge", "OffsetY", &self.offset_y.to_string());

        doc.set("Window", "ProcessName", &self.process_name);
        doc.set("Window", "AllowBackgroundKeys", b01(self.allow_background_keys));
        doc.set("Window", "Width", &self.window_width.to_string());
        doc.set("Window", "Height", &self.window_height.to_string());
        doc.set("Window", "Layout", &self.window_layout);
        doc.set("Window", "InGameConfigMode", b01(self.in_game_config_mode));

        doc.set("Pause", "Button", &self.pause_hotkey);

        for (i, enabled) in self.slot_enabled.iter().enumerate() {
            doc.set("Spells", &format!("spell{}", i + 1), b01(*enabled));
        }

        doc.set("Timing", "PollIntervalMs", &self.poll_interval_ms.to_string());
        doc.set("Timing", "MinKeyIntervalMs", &self.min_key_interval_ms.to_string());
        doc.set("Timing", "KeyPressMs", &self.key_press_ms.to_string());

        doc.set("Targeting", "AutoTargetEnabled", b01(self.auto_target_enabled));
        doc.set("Targeting", "CombatOnly", b01(self.combat_only));
        doc.set("Targeting", "TargetKey", &self.target_key);

        doc.set("Interact", "InteractEnabled", b01(self.interact_enabled));
        doc.set("Interact", "InteractKey", &self.interact_key);

        doc.set("Scheduler", "Enabled", b01(self.scheduler_enabled));
        doc.set(
            "Scheduler",
            "HeartbeatTimeoutMs",
            &self.scheduler_heartbeat_timeout_ms.to_string(),
        );
        doc.set(
            "Scheduler",
            "RepeatSuppressMs",
            &self.scheduler_repeat_suppress_ms.to_string(),
        );

        doc.set("Intelligence", "Enabled", b01(self.intelligence_enabled));
        doc.set(
            "Intelligence",
            "StaleAfterMs",
            &self.intelligence_stale_after_ms.to_string(),
        );
        doc.set("Intelligence", "HpCurve", b01(self.hp_curve));

        doc.set("TimeToKill", "Enabled", b01(self.ttk_enabled));
        doc.set("TimeToKill", "Fallback", self.ttk_fallback.as_str());
        doc.set("TimeToKill", "History", b01(self.ttk_history));
        doc.set("TimeToKill", "HistoryKills", &self.ttk_history_kills.to_string());
        doc.set("TimeToKill", "HistoryMinKills", &self.ttk_history_min_kills.to_string());
        doc.set("TimeToKill", "HistoryMaxAgeSec", &self.ttk_history_max_age_sec.to_string());
        doc.set("TimeToKill", "HistoryQuantile", &self.ttk_history_quantile.to_string());
        doc.set(
            "TimeToKill",
            "HistoryDurFactor",
            &fmt_f64(self.ttk_history_dur_factor),
        );
        doc.set("TimeToKill", "WarmupSec", &fmt_f64(self.ttk_warmup_sec));

        doc.set("CrowdControl", "Enabled", b01(self.crowd_control_enabled));

        doc.set("Rotation", "Mode", self.mode_preset.as_str());
        doc.set("Rotation", "Targets", self.target_mode.as_str());

        doc.set("Solo", "Enabled", b01(self.solo_enabled));
        doc.set("Solo", "EmergencyHpPct", &self.solo_emergency_hp_pct.to_string());
        doc.set("Solo", "SelfSustainHpPct", &self.solo_self_sustain_hp_pct.to_string());
        doc.set(
            "Solo",
            "DefensiveEscalateHpPct",
            &self.solo_defensive_escalate_hp_pct.to_string(),
        );
        doc.set("Solo", "EscalationEnabled", b01(self.solo_escalation_enabled));
        doc.set("Solo", "MinorHpPct", &self.solo_minor_hp_pct.to_string());
        doc.set("Solo", "MajorHpPct", &self.solo_major_hp_pct.to_string());
        doc.set("Solo", "ImmunityHpPct", &self.solo_immunity_hp_pct.to_string());

        doc.set("Abilities", "On", &self.abilities_on);
        doc.set("Abilities", "Off", &self.abilities_off);
        doc.set("Abilities", "Modes", &self.abilities_modes);

        doc.set("AbilityOverrides", "Enabled", b01(self.ability_overrides_enabled));
        doc.set("AbilityOverrides", "File", &self.ability_overrides_file);

        doc.set("Telemetry", "Enabled", b01(self.telemetry_enabled));
        doc.set("Telemetry", "Capacity", &self.telemetry_capacity.to_string());

        doc.set(
            "Color",
            "Magic",
            &format!("{},{},{}", self.color.magic_r, self.color.magic_g, self.color.magic_b),
        );
        doc.set(
            "Color",
            "Black",
            &format!("{},{},{}", self.color.black[0], self.color.black[1], self.color.black[2]),
        );
        doc.set(
            "Color",
            "White",
            &format!("{},{},{}", self.color.white[0], self.color.white[1], self.color.white[2]),
        );
        doc.set("Color", "Tolerance", &self.color.tolerance.to_string());
        doc.set("Color", "LearnedAt", &self.color.learned_at);

        doc.set("Launch", "BNetPath", &self.bnet_path);

        doc
    }

    /// Applies every known key from `doc` (case-insensitive), mirroring
    /// `AppSettings.Apply` including its clamps.
    fn apply_from_doc(&mut self, doc: &IniDocument) {
        macro_rules! apply {
            ($section:expr, $key:expr, $body:expr) => {
                if let Some(value) = doc.get($section, $key) {
                    $body(value);
                }
            };
        }

        apply!("bridge", "cellsize", |v: &str| self.cell_size = parse_int(v, self.cell_size));
        apply!("bridge", "offsetx", |v: &str| self.offset_x = parse_int(v, self.offset_x));
        apply!("bridge", "offsety", |v: &str| self.offset_y = parse_int(v, self.offset_y));

        apply!("window", "processname", |v: &str| self.process_name = v.to_string());
        apply!("window", "allowbackgroundkeys", |v: &str| {
            self.allow_background_keys = parse_bool(v, self.allow_background_keys)
        });
        apply!("window", "width", |v: &str| {
            self.window_width = clamp_int(parse_int(v, self.window_width), 0, 8000)
        });
        apply!("window", "height", |v: &str| {
            self.window_height = clamp_int(parse_int(v, self.window_height), 0, 8000)
        });
        apply!("window", "layout", |v: &str| self.window_layout = v.trim().to_string());
        apply!("window", "ingameconfigmode", |v: &str| {
            self.in_game_config_mode = parse_bool(v, self.in_game_config_mode)
        });

        apply!("pause", "button", |v: &str| self.pause_hotkey = v.to_string());

        for i in 0..8 {
            let key = format!("spell{}", i + 1);
            if let Some(value) = doc.get("spells", &key) {
                self.slot_enabled[i] = parse_bool(value, self.slot_enabled[i]);
            }
        }

        apply!("timing", "pollintervalms", |v: &str| {
            self.poll_interval_ms = parse_int(v, self.poll_interval_ms)
        });
        apply!("timing", "minkeyintervalms", |v: &str| {
            self.min_key_interval_ms = parse_int(v, self.min_key_interval_ms)
        });
        apply!("timing", "keypressms", |v: &str| {
            self.key_press_ms = parse_int(v, self.key_press_ms)
        });

        apply!("targeting", "autotargetenabled", |v: &str| {
            self.auto_target_enabled = parse_bool(v, self.auto_target_enabled)
        });
        apply!("targeting", "combatonly", |v: &str| {
            self.combat_only = parse_bool(v, self.combat_only)
        });
        apply!("targeting", "targetkey", |v: &str| {
            if !v.trim().is_empty() {
                self.target_key = v.trim().to_string();
            }
        });

        apply!("interact", "interactenabled", |v: &str| {
            self.interact_enabled = parse_bool(v, self.interact_enabled)
        });
        apply!("interact", "interactkey", |v: &str| {
            if !v.trim().is_empty() {
                self.interact_key = v.trim().to_string();
            }
        });

        apply!("scheduler", "enabled", |v: &str| {
            self.scheduler_enabled = parse_bool(v, self.scheduler_enabled)
        });
        apply!("scheduler", "heartbeattimeoutms", |v: &str| {
            self.scheduler_heartbeat_timeout_ms =
                clamp_int(parse_int(v, self.scheduler_heartbeat_timeout_ms), 100, 5000)
        });
        apply!("scheduler", "repeatsuppressms", |v: &str| {
            self.scheduler_repeat_suppress_ms =
                clamp_int(parse_int(v, self.scheduler_repeat_suppress_ms), 100, 5000)
        });

        apply!("intelligence", "enabled", |v: &str| {
            self.intelligence_enabled = parse_bool(v, self.intelligence_enabled)
        });
        apply!("intelligence", "staleafterms", |v: &str| {
            self.intelligence_stale_after_ms = parse_int(v, self.intelligence_stale_after_ms)
        });
        apply!("intelligence", "hpcurve", |v: &str| {
            self.hp_curve = parse_bool(v, self.hp_curve)
        });

        apply!("timetokill", "enabled", |v: &str| {
            self.ttk_enabled = parse_bool(v, self.ttk_enabled)
        });
        apply!("timetokill", "fallback", |v: &str| self.ttk_fallback = TtkFallback::parse(v));
        apply!("timetokill", "history", |v: &str| {
            self.ttk_history = parse_bool(v, self.ttk_history)
        });
        apply!("timetokill", "historykills", |v: &str| {
            self.ttk_history_kills = clamp_int(parse_int(v, self.ttk_history_kills), 3, 20)
        });
        apply!("timetokill", "historyminkills", |v: &str| {
            self.ttk_history_min_kills = clamp_int(parse_int(v, self.ttk_history_min_kills), 2, 10)
        });
        apply!("timetokill", "historymaxagesec", |v: &str| {
            self.ttk_history_max_age_sec =
                clamp_int(parse_int(v, self.ttk_history_max_age_sec), 30, 900)
        });
        apply!("timetokill", "historyquantile", |v: &str| {
            self.ttk_history_quantile = clamp_int(parse_int(v, self.ttk_history_quantile), 50, 95)
        });
        apply!("timetokill", "historydurfactor", |v: &str| {
            self.ttk_history_dur_factor =
                clamp_f64(parse_double(v, self.ttk_history_dur_factor), 0.0, 1.0)
        });
        apply!("timetokill", "warmupsec", |v: &str| {
            self.ttk_warmup_sec = clamp_f64(parse_double(v, self.ttk_warmup_sec), 0.0, 10.0)
        });

        apply!("crowdcontrol", "enabled", |v: &str| {
            self.crowd_control_enabled = parse_bool(v, self.crowd_control_enabled)
        });

        apply!("rotation", "mode", |v: &str| self.mode_preset = RotationPreset::parse(v));
        apply!("rotation", "targets", |v: &str| self.target_mode = TargetPreset::parse(v));

        apply!("solo", "enabled", |v: &str| {
            self.solo_enabled = parse_bool(v, self.solo_enabled)
        });
        apply!("solo", "emergencyhppct", |v: &str| {
            self.solo_emergency_hp_pct = clamp_int(parse_int(v, self.solo_emergency_hp_pct), 5, 90)
        });
        apply!("solo", "selfsustainhppct", |v: &str| {
            self.solo_self_sustain_hp_pct =
                clamp_int(parse_int(v, self.solo_self_sustain_hp_pct), 10, 99)
        });
        apply!("solo", "defensiveescalatehppct", |v: &str| {
            self.solo_defensive_escalate_hp_pct =
                clamp_int(parse_int(v, self.solo_defensive_escalate_hp_pct), 5, 99)
        });
        apply!("solo", "escalationenabled", |v: &str| {
            self.solo_escalation_enabled = parse_bool(v, self.solo_escalation_enabled)
        });
        apply!("solo", "minorhppct", |v: &str| {
            self.solo_minor_hp_pct = clamp_int(parse_int(v, self.solo_minor_hp_pct), 40, 99)
        });
        apply!("solo", "majorhppct", |v: &str| {
            self.solo_major_hp_pct = clamp_int(parse_int(v, self.solo_major_hp_pct), 20, 90)
        });
        apply!("solo", "immunityhppct", |v: &str| {
            self.solo_immunity_hp_pct = clamp_int(parse_int(v, self.solo_immunity_hp_pct), 5, 60)
        });

        apply!("abilities", "on", |v: &str| self.abilities_on = v.to_string());
        apply!("abilities", "off", |v: &str| self.abilities_off = v.to_string());
        apply!("abilities", "modes", |v: &str| self.abilities_modes = v.to_string());

        apply!("abilityoverrides", "enabled", |v: &str| {
            self.ability_overrides_enabled = parse_bool(v, self.ability_overrides_enabled)
        });
        apply!("abilityoverrides", "file", |v: &str| {
            if !v.trim().is_empty() {
                self.ability_overrides_file = v.trim().to_string();
            }
        });

        apply!("telemetry", "enabled", |v: &str| {
            self.telemetry_enabled = parse_bool(v, self.telemetry_enabled)
        });
        apply!("telemetry", "capacity", |v: &str| {
            self.telemetry_capacity = clamp_int(parse_int(v, self.telemetry_capacity), 64, 1_000_000)
        });

        apply!("color", "magic", |v: &str| {
            let [r, g, b] = parse_triple(v, [self.color.magic_r, self.color.magic_g, self.color.magic_b]);
            self.color.magic_r = r;
            self.color.magic_g = g;
            self.color.magic_b = b;
        });
        apply!("color", "black", |v: &str| self.color.black = parse_triple(v, self.color.black));
        apply!("color", "white", |v: &str| self.color.white = parse_triple(v, self.color.white));
        apply!("color", "tolerance", |v: &str| {
            self.color.tolerance = parse_int(v, self.color.tolerance)
        });
        apply!("color", "learnedat", |v: &str| self.color.learned_at = v.trim().to_string());

        apply!("launch", "bnetpath", |v: &str| self.bnet_path = v.trim().to_string());

        apply!("meta", "configversion", |v: &str| {
            self.config_version = parse_int(v, self.config_version)
        });
    }
}

// ===========================================================================
// Parse helpers (mirror AppSettings private helpers)
// ===========================================================================

fn b01(value: bool) -> &'static str {
    if value {
        "1"
    } else {
        "0"
    }
}

fn fmt_f64(value: f64) -> String {
    let mut out = String::new();
    // `Display` for f64 already yields the shortest round-tripping form ("3",
    // "0.5"), matching C# InvariantCulture for these values.
    let _ = write!(out, "{value}");
    out
}

fn parse_int(value: &str, fallback: i32) -> i32 {
    value.trim().parse::<i32>().unwrap_or(fallback)
}

fn parse_double(value: &str, fallback: f64) -> f64 {
    // NaN/Infinity parse successfully but must never reach a clamp (Clamp(NaN)
    // is NaN in C#); a non-finite token falls back like an unparseable one.
    match value.trim().parse::<f64>() {
        Ok(parsed) if parsed.is_finite() => parsed,
        _ => fallback,
    }
}

fn parse_bool(value: &str, fallback: bool) -> bool {
    match value.trim().to_ascii_lowercase().as_str() {
        "1" | "true" | "yes" | "on" => true,
        "0" | "false" | "no" | "off" => false,
        _ => fallback,
    }
}

fn clamp_int(value: i32, min: i32, max: i32) -> i32 {
    value.clamp(min, max)
}

fn clamp_f64(value: f64, min: f64, max: f64) -> f64 {
    value.clamp(min, max)
}

fn parse_triple(value: &str, fallback: [i32; 3]) -> [i32; 3] {
    let parts: Vec<&str> = value
        .split(',')
        .map(str::trim)
        .filter(|part| !part.is_empty())
        .collect();
    if parts.len() != 3 {
        return fallback;
    }
    let mut out = fallback;
    for (i, part) in parts.iter().enumerate() {
        if let Ok(parsed) = part.parse::<i32>() {
            out[i] = parsed.clamp(0, 255);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn preserves_unknown_keys_comments_and_sections() {
        let text = "; top\n[Bridge]\nCellSize=8\nFoo=bar\n; a note\n[ExtraStuff]\nX=1\n";
        let mut doc = IniDocument::parse(text);
        doc.set("Bridge", "CellSize", "16");
        doc.set("Bridge", "NewKey", "42");

        let out = doc.render();
        assert!(out.contains("; top"));
        assert!(out.contains("; a note"));
        assert!(out.contains("Foo=bar"));
        assert!(out.contains("[ExtraStuff]"));
        assert!(out.contains("X=1"));
        assert!(out.contains("CellSize=16"));
        assert!(out.contains("NewKey=42"));
    }

    #[test]
    fn roundtrip_preserves_structure_when_untouched() {
        // Normalised `key=value`; comments, blanks and section order survive.
        let text = "; c\n[A]\nk=v\n\n[B]\nx=1";
        let doc = IniDocument::parse(text);
        assert_eq!(doc.render(), text);
    }

    #[test]
    fn load_applies_defaults_and_parses_like_appsettings() {
        let text = "[Spells]\nspell1=0\nspell7=0\n\n[Timing]\nPollIntervalMs=16\n\n[Meta]\nConfigVersion=1\n";
        let mut doc = IniDocument::parse(text);
        doc.set("Bridge", "CellSize", "huh"); // unparseable -> keeps default
        // Emulate load without I/O by applying to a default Settings.
        let mut settings = Settings {
            doc: doc.clone(),
            ..Settings::default()
        };
        settings.apply_from_doc(&doc);
        settings.path = String::new();

        assert_eq!(settings.cell_size, 8);
        assert!(!settings.slot_enabled[0]);
        assert!(!settings.slot_enabled[6]);
        assert_eq!(settings.poll_interval_ms, 16);
    }

    #[test]
    fn bool_and_double_parse_edges() {
        assert!(parse_bool(" ON ", false));
        assert!(!parse_bool("Off", true));
        assert!(parse_bool("nonsense", true));
        assert_eq!(parse_double("NaN", 2.5), 2.5);
        assert_eq!(parse_double("1.25", 0.0), 1.25);
        assert_eq!(fmt_f64(3.0), "3");
        assert_eq!(fmt_f64(0.5), "0.5");
    }
}
