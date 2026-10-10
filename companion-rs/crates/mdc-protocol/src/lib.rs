//! `mdc-protocol` — pure, dependency-free decode of the MaxDpsBridge pixel
//! strip. Port target: `app/MaxDpsCompanion/PixelProtocol.cs` (read-only
//! reference) and the normative `docs/PROTOCOL.md`.
//!
//! Scope (scaffold task 1/3): decode v5/v6 core (35 cells), Ext2 (40 cells) and
//! Ext3 (43 cells), plus the legacy v4 (9-cell) and v1 (8-cell) windows, the
//! cells-10/34/39/42 checksums and slot cells 1-8. **Pure functions only** — no
//! file, socket, clock or game-API access lives here; the engine supplies the
//! `Color` samples a capture layer produced.
//!
//! Checksum note: `docs/PROTOCOL.md` calls cell 10 "XOR-sum", but the shipped
//! encoder (`addon/MaxDpsBridgeExp/Bridge.lua:58`, `:403`) and the reference
//! decoder (`PixelProtocol.cs:392`) both use an **arithmetic sum** of R+G+B
//! nibbles mod 16. This port follows the code, not the prose typo.

#![forbid(unsafe_code)]

/// v1 window width (8 cells).
pub const CELL_COUNT_V1: usize = 8;
/// v4 window width (9 cells).
pub const CELL_COUNT_V4: usize = 9;
/// v5 core width (35 cells).
pub const CELL_COUNT: usize = 35;
/// Ext2 width (40 cells): core + HP curve + SelfHeal2.
pub const CELL_COUNT_EXT2: usize = 40;
/// Ext3 width (43 cells): core + Ext2 + mask/epoch/blocked block.
pub const CELL_COUNT_EXT3: usize = 43;

pub const SUPPORTED_VERSION_V1: i32 = 1;
pub const SUPPORTED_VERSION_V4: i32 = 4;
pub const SUPPORTED_VERSION: i32 = 5;
/// Accepted forward-compatically: same 35-cell layout as v5.
pub const SUPPORTED_VERSION_V6: i32 = 6;

/// Slots in the full v5 frame (0-based indices `Main`..`SelfHeal`).
pub const SLOT_COUNT: usize = 8;
pub const SLOT_COUNT_V1: usize = 5;
pub const SLOT_COUNT_V4: usize = 6;

/// Minimum separation between the magic cell's black and white channels.
pub const MIN_CONTRAST: i32 = 96;
/// v5 sentinel nibble for "unknown / not applicable".
pub const UNKNOWN_NIBBLE: i32 = 15;

// --- Status-cell B flag bits (v4+; v5 adds bit3) ---
pub const STATUS_FLAG_IN_COMBAT: i32 = 1;
pub const STATUS_FLAG_ON_GCD: i32 = 2;
pub const STATUS_FLAG_HAS_TARGET: i32 = 4;
pub const STATUS_FLAG_CONTEXT_VALID: i32 = 8;

// --- Fixed cell indices (v5) ---
pub const STATUS_CELL_INDEX: usize = 9;
pub const VERSION_CELL_INDEX: usize = 10;
pub const SPELL_ID_CELL_BASE: usize = 11;
pub const VITALS_CELL_INDEX: usize = 27;
pub const CAST_CELL_INDEX: usize = 28;
pub const TARGET_CELL_INDEX: usize = 29;
pub const RANGE_CELL_INDEX: usize = 30;
pub const RANGE_CELL_INDEX2: usize = 31;
pub const BUFF_CELL_INDEX: usize = 32;
pub const CLASS_SPEC_CELL_INDEX: usize = 33;
pub const EXTENSION_CELL_INDEX: usize = 34;

// --- Ext2 cell indices (present only in a >=40-cell capture) ---
pub const HP_CURVE_CELL_INDEX: usize = 35;
pub const SELF_HEAL2_CELL_INDEX: usize = 36;
pub const SELF_HEAL2_SPELL_ID_CELL_INDEX: usize = 37;
pub const SELF_HEAL2_COMMIT_CELL_INDEX: usize = 39;

// --- Ext3 cell indices (present only in a 43-cell capture) ---
pub const EXT3_MASK_CELL_INDEX: usize = 40;
pub const EXT3_FLAGS_CELL_INDEX: usize = 41;
pub const EXT3_COMMIT_CELL_INDEX: usize = 42;
/// Ext3 mask bit width: bits 0-11 in cell 40, bits 12-13 in cell 41 R.
pub const EXT3_MASK_BIT_COUNT: u32 = 14;

// --- Cell-28 B bits: Ext3 presence + Ext2 SelfHeal2 range tri-state (bits0-1) ---
pub const CAST_FLAG_EXT3_PRESENT: i32 = 4;
// --- Cell-33 B bits ---
pub const CLASS_FLAG_CLASS_SPEC_VALID: i32 = 1;
pub const CLASS_FLAG_BUFF_PROBE_VALID: i32 = 2;
pub const CLASS_FLAG_EXT2_PRESENT: i32 = 4;
pub const CLASS_FLAG_HP_CURVE_ACTIVE: i32 = 8;

// --- Slot-cell flag bits ---
const FLAG_SHIFT: i32 = 1;
const FLAG_CTRL: i32 = 2;
const FLAG_ALT: i32 = 4;
const FLAG_VALID: i32 = 8;

// --- Cast-state nibbles ---
pub const CAST_STATE_NONE: i32 = 0;
pub const CAST_STATE_CASTING: i32 = 1;
pub const CAST_STATE_CHANNELING: i32 = 2;
pub const CAST_STATE_UNKNOWN: i32 = 15;

// --- Range tri-state packed values ---
pub const RANGE_UNKNOWN: i32 = 0;
pub const RANGE_IN: i32 = 1;
pub const RANGE_OUT: i32 = 2;

/// One sampled cell: the centre pixel of a physical `CellSize` square.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Color {
    pub r: u8,
    pub g: u8,
    pub b: u8,
}

impl Color {
    pub const fn new(r: u8, g: u8, b: u8) -> Self {
        Self { r, g, b }
    }
}

/// Engine state carried by cell 9 R.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BridgeState {
    Idle = 0,
    Active = 1,
    Paused = 2,
    NeedTarget = 3,
    NeedInteract = 4,
}

impl BridgeState {
    fn from_nibble(n: i32) -> Option<Self> {
        match n {
            0 => Some(BridgeState::Idle),
            1 => Some(BridgeState::Active),
            2 => Some(BridgeState::Paused),
            3 => Some(BridgeState::NeedTarget),
            4 => Some(BridgeState::NeedInteract),
            _ => None,
        }
    }
}

/// Three-valued observation. `Unknown` is first-class (Midnight secrets).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum TriState {
    #[default]
    Unknown,
    Yes,
    No,
}

/// Additive defensive urgency (cells 31 G / 32 B).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum DefensiveUrgency {
    #[default]
    Unknown,
    White,
    Yellow,
    Orange,
    Red,
}

/// What the player is doing right now (v5 cast sensor, cell 28 R).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum PlayerCastState {
    #[default]
    Unknown,
    None,
    Casting,
    Channeling,
}

/// Slot order mirrors the user icon model; 0-based here, wire slots 1-8.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Slot {
    Main = 0,
    Offensive = 1,
    Defensive = 2,
    Consumable = 3,
    Trinket = 4,
    Interrupt = 5,
    Mobility = 6,
    SelfHeal = 7,
}

/// Decoded key binding for one slot cell.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct KeyStroke {
    pub virtual_key: u8,
    pub shift: bool,
    pub ctrl: bool,
    pub alt: bool,
}

/// Ext2 (v3.0.0) second self-sustain candidate decoded from cells 36-38.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SelfHeal2Slot {
    pub stroke: KeyStroke,
    pub spell_id: i32,
    pub range: TriState,
}

/// Ext3 (v3.5) block decoded from cells 40-42.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Ext3Block {
    pub mask: u16,
    pub epoch: i32,
    pub blocked: i32,
}

/// Why a sample did not decode; classification order mirrors [`decode`]'s checks.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DecodeError {
    /// Capture width is not a supported window.
    Length,
    /// Cell 0 is not the magic magenta.
    Magic,
    /// Learned-profile black/white contrast below [`MIN_CONTRAST`].
    Contrast,
    /// Version nibble is not one [`decode`] accepts for this width.
    Version,
    /// Cell 9 R is outside `0..=4`.
    State,
    /// A commit channel does not equal the heartbeat.
    Commit,
    /// A checksum does not match its covered nibbles.
    Checksum,
}

/// One decoded reading of the addon's pixel block.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BridgeFrame {
    pub state: BridgeState,
    pub heartbeat: i32,
    pub version: i32,
    pub status_flags: i32,
    /// Per-slot key binding; `None` = slot invalid/absent.
    pub slots: [Option<KeyStroke>; SLOT_COUNT],
    /// v5: 24-bit spell id per slot (0 = unknown). All zero on v4/v1.
    pub spell_ids: [i32; SLOT_COUNT],
    /// Player health percent; `-1` when UNKNOWN.
    pub hp_pct: i32,
    pub cast: PlayerCastState,
    /// Cast remaining band (0..=14), 15 = unknown.
    pub cast_remaining_band: i32,
    pub target_casting: TriState,
    pub target_cast_interruptible: TriState,
    pub target_in_melee: TriState,
    /// Target health percent; `-1` when UNKNOWN.
    pub target_hp_pct: i32,
    pub slot_range: [TriState; SLOT_COUNT],
    pub slot_buff_active: [TriState; SLOT_COUNT],
    /// v2.7 cell-33 B bit1: the self-buff probe block ran cleanly.
    pub buff_probe_valid: bool,
    pub defensive_urgency: DefensiveUrgency,
    pub stagger_urgency: DefensiveUrgency,
    /// True when the Defensive slot was filled from the catalog gap-fill.
    pub defensive_catalog_source: bool,
    /// Ext2: a live HP curve was decoded from cell 35.
    pub hp_curve_valid: bool,
    /// Ext2: HP-curve band (cell 35 R), `-1` when not valid.
    pub hp_curve_band: i32,
    /// Ext2: second self-sustain candidate; `None` when absent/invalid.
    pub self_heal2: Option<SelfHeal2Slot>,
    /// Ext3: cell 28 B bit2 says a 43-cell Ext3 block is present.
    pub ext3_present: bool,
    /// Ext3: decoded cells 40-42; `None` when absent or checksum/commit failed.
    pub ext3: Option<Ext3Block>,
}

impl BridgeFrame {
    pub fn in_combat(&self) -> bool {
        self.status_flags & STATUS_FLAG_IN_COMBAT != 0
    }
    pub fn on_gcd(&self) -> bool {
        self.status_flags & STATUS_FLAG_ON_GCD != 0
    }
    pub fn has_target(&self) -> bool {
        self.status_flags & STATUS_FLAG_HAS_TARGET != 0
    }
    pub fn context_valid(&self) -> bool {
        self.status_flags & STATUS_FLAG_CONTEXT_VALID != 0
    }
    /// Per-slot spell id (0 = unknown).
    pub fn spell_id(&self, slot: Slot) -> i32 {
        self.spell_ids[slot as usize]
    }
}

/// A learned `/mdb calibrate` colour profile. `learned == false` reproduces the
/// legacy single black/white ramp taken from the magic cell.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ColorProfile {
    pub magic_r: i32,
    pub magic_g: i32,
    pub magic_b: i32,
    pub black: [i32; 3],
    pub white: [i32; 3],
    pub tolerance: i32,
    pub learned: bool,
}

impl ColorProfile {
    /// The default unlearned profile (legacy fixed ramp).
    pub const fn unlearned() -> Self {
        Self {
            magic_r: 255,
            magic_g: 0,
            magic_b: 255,
            black: [0, 0, 0],
            white: [255, 255, 255],
            tolerance: 64,
            learned: false,
        }
    }

    pub fn is_magic(&self, color: Color) -> bool {
        let (r, g, b) = (color.r as i32, color.g as i32, color.b as i32);
        if !self.learned {
            return r - g >= MIN_CONTRAST && b - g >= MIN_CONTRAST;
        }
        if (r - self.magic_r).abs() > self.tolerance
            || (g - self.magic_g).abs() > self.tolerance
            || (b - self.magic_b).abs() > self.tolerance
        {
            return false;
        }
        let ref_r = self.magic_r - self.magic_g;
        let ref_b = self.magic_b - self.magic_g;
        if ref_r <= 0 || ref_b <= 0 {
            return true;
        }
        r - g >= ref_r / 2 && b - g >= ref_b / 2
    }

    /// Legacy shared-ramp levels read from the magic cell.
    pub fn legacy_levels(magic: Color) -> (i32, i32) {
        (magic.g as i32, (magic.r as i32 + magic.b as i32) / 2)
    }

    fn nibble(&self, channel: u8, axis: usize, black: i32, white: i32) -> i32 {
        if self.learned {
            let (b, w) = (self.black[axis], self.white[axis]);
            if w <= b {
                return if channel as i32 >= b { 15 } else { 0 };
            }
            let scaled = (channel as i32 - b) as f64 * 15.0 / (w - b) as f64;
            return (scaled.round() as i32).clamp(0, 15);
        }
        let scaled = (channel as i32 - black) as f64 * 15.0 / (white - black) as f64;
        (scaled.round() as i32).clamp(0, 15)
    }
}

/// True for any capture width the v5 decoder understands.
pub fn is_v5_length(length: usize) -> bool {
    length == CELL_COUNT || length == CELL_COUNT_EXT2 || length == CELL_COUNT_EXT3
}

/// PERF: derive the v4 (9-cell) window from an existing capture.
pub fn trim_to_v4(cells: &[Color]) -> Vec<Color> {
    cells.iter().copied().take(CELL_COUNT_V4).collect()
}

/// PERF: derive the v1 (8-cell) window from an existing capture.
pub fn trim_to_v1(cells: &[Color]) -> Vec<Color> {
    cells.iter().copied().take(CELL_COUNT_V1).collect()
}

/// Decode a capture with the default unlearned profile.
pub fn decode(cells: &[Color]) -> Result<BridgeFrame, DecodeError> {
    decode_with_profile(cells, None)
}

/// Decode a capture, optionally with a learned colour profile.
///
/// Dispatch mirrors `PixelProtocol.Decode`: v5 width first (current), then v4
/// (stale addon), then v1 (older stale addon).
pub fn decode_with_profile(
    cells: &[Color],
    profile: Option<&ColorProfile>,
) -> Result<BridgeFrame, DecodeError> {
    if is_v5_length(cells.len()) {
        return decode_v5(cells, profile);
    }
    if cells.len() == CELL_COUNT_V4 {
        return decode_legacy(cells, 7, 8, None, SUPPORTED_VERSION_V4, profile);
    }
    if cells.len() == CELL_COUNT_V1 {
        return decode_legacy(cells, 6, 7, Some(&V1_TO_V4), SUPPORTED_VERSION_V1, profile);
    }
    Err(DecodeError::Length)
}

/// v1 slot -> v4 slot remap (Interrupt 3->5, Defensive 4->2, Consumable 5->3).
const V1_TO_V4: [usize; 5] = [0, 1, 5, 2, 3];

fn decode_legacy(
    cells: &[Color],
    status_index: usize,
    version_index: usize,
    remap: Option<&[usize; 5]>,
    version: i32,
    profile: Option<&ColorProfile>,
) -> Result<BridgeFrame, DecodeError> {
    let owned;
    let profile = match profile {
        Some(p) => p,
        None => {
            owned = ColorProfile::unlearned();
            &owned
        }
    };

    if !profile.is_magic(cells[0]) {
        return Err(DecodeError::Magic);
    }
    let (black, white) = levels(profile, cells[0])?;

    let n = |i: usize| nibbles(&cells[i], profile, black, white);
    let (state, heartbeat, status_flags) = n(status_index);
    let (ver, checksum, commit) = n(version_index);
    if ver != version {
        return Err(DecodeError::Version);
    }
    let state = BridgeState::from_nibble(state).ok_or(DecodeError::State)?;
    if commit != heartbeat {
        return Err(DecodeError::Commit);
    }
    let mut sum = 0;
    for i in 1..=status_index {
        let (r, g, b) = n(i);
        sum += r + g + b;
    }
    if (sum & 0xF) != checksum {
        return Err(DecodeError::Checksum);
    }

    let mut slots = [None; SLOT_COUNT];
    let count = remap.map_or(SLOT_COUNT_V4, |r| r.len());
    for i in 0..count {
        let (hi, lo, flags) = n(i + 1);
        if flags & FLAG_VALID == 0 {
            continue;
        }
        let stroke = KeyStroke {
            virtual_key: ((hi << 4) | lo) as u8,
            shift: flags & FLAG_SHIFT != 0,
            ctrl: flags & FLAG_CTRL != 0,
            alt: flags & FLAG_ALT != 0,
        };
        slots[remap.map_or(i, |r| r[i])] = Some(stroke);
    }

    Ok(BridgeFrame {
        state,
        heartbeat,
        version: ver,
        status_flags,
        slots,
        spell_ids: [0; SLOT_COUNT],
        hp_pct: -1,
        cast: PlayerCastState::Unknown,
        cast_remaining_band: UNKNOWN_NIBBLE,
        target_casting: TriState::Unknown,
        target_cast_interruptible: TriState::Unknown,
        target_in_melee: TriState::Unknown,
        target_hp_pct: -1,
        slot_range: [TriState::Unknown; SLOT_COUNT],
        slot_buff_active: [TriState::Unknown; SLOT_COUNT],
        buff_probe_valid: false,
        defensive_urgency: DefensiveUrgency::Unknown,
        stagger_urgency: DefensiveUrgency::Unknown,
        defensive_catalog_source: false,
        hp_curve_valid: false,
        hp_curve_band: -1,
        self_heal2: None,
        ext3_present: false,
        ext3: None,
    })
}

fn levels(profile: &ColorProfile, magic: Color) -> Result<(i32, i32), DecodeError> {
    if profile.learned {
        return Ok((-1, -1));
    }
    let (black, white) = ColorProfile::legacy_levels(magic);
    if white - black < MIN_CONTRAST {
        return Err(DecodeError::Contrast);
    }
    Ok((black, white))
}

fn nibbles(cell: &Color, profile: &ColorProfile, black: i32, white: i32) -> (i32, i32, i32) {
    (
        profile.nibble(cell.r, 0, black, white),
        profile.nibble(cell.g, 1, black, white),
        profile.nibble(cell.b, 2, black, white),
    )
}

fn decode_v5(cells: &[Color], profile: Option<&ColorProfile>) -> Result<BridgeFrame, DecodeError> {
    let owned;
    let profile = match profile {
        Some(p) => p,
        None => {
            owned = ColorProfile::unlearned();
            &owned
        }
    };

    if !profile.is_magic(cells[0]) {
        return Err(DecodeError::Magic);
    }
    let (black, white) = levels(profile, cells[0])?;
    let n = |i: usize| nibbles(&cells[i], profile, black, white);

    let (state_n, heartbeat, status_flags) = n(STATUS_CELL_INDEX);
    let (ver, checksum, commit) = n(VERSION_CELL_INDEX);
    if ver != SUPPORTED_VERSION && ver != SUPPORTED_VERSION_V6 {
        return Err(DecodeError::Version);
    }
    let state = BridgeState::from_nibble(state_n).ok_or(DecodeError::State)?;
    if commit != heartbeat {
        return Err(DecodeError::Commit);
    }

    // Core checksum over cells 1..=9 (identical formula to v4).
    let mut sum = 0;
    for i in 1..=STATUS_CELL_INDEX {
        let (r, g, b) = n(i);
        sum += r + g + b;
    }
    if (sum & 0xF) != checksum {
        return Err(DecodeError::Checksum);
    }

    // Extension checksum over cells 11..=33 plus a second commit.
    let mut ext_sum = 0;
    for i in SPELL_ID_CELL_BASE..=CLASS_SPEC_CELL_INDEX {
        let (r, g, b) = n(i);
        ext_sum += r + g + b;
    }
    let (_, ext_checksum, ext_commit) = n(EXTENSION_CELL_INDEX);
    if ext_commit != heartbeat {
        return Err(DecodeError::Commit);
    }
    if (ext_sum & 0xF) != ext_checksum {
        return Err(DecodeError::Checksum);
    }

    let mut slots = [None; SLOT_COUNT];
    let mut spell_ids = [0; SLOT_COUNT];
    for i in 0..SLOT_COUNT {
        let (hi, lo, flags) = n(i + 1);
        if flags & FLAG_VALID != 0 {
            slots[i] = Some(KeyStroke {
                virtual_key: ((hi << 4) | lo) as u8,
                shift: flags & FLAG_SHIFT != 0,
                ctrl: flags & FLAG_CTRL != 0,
                alt: flags & FLAG_ALT != 0,
            });
        }
        let (r0, g0, b0) = n(SPELL_ID_CELL_BASE + i * 2);
        let (r1, g1, b1) = n(SPELL_ID_CELL_BASE + i * 2 + 1);
        spell_ids[i] = (r0 << 20) | (g0 << 16) | (b0 << 12) | (r1 << 8) | (g1 << 4) | b1;
    }

    let (hp_hi, hp_lo, vitals_flags) = n(VITALS_CELL_INDEX);
    let hp_pct = if vitals_flags & 1 != 0 && hp_hi <= 6 {
        hp_hi * 16 + hp_lo
    } else {
        -1
    };

    let (cast_state_n, cast_band, cast_flags_raw) = n(CAST_CELL_INDEX);
    let cast = match cast_state_n {
        CAST_STATE_NONE => PlayerCastState::None,
        CAST_STATE_CASTING => PlayerCastState::Casting,
        CAST_STATE_CHANNELING => PlayerCastState::Channeling,
        _ => PlayerCastState::Unknown,
    };

    let (target_flags, target_hp_band, target_cast_flags) = n(TARGET_CELL_INDEX);
    let target_in_melee = if target_flags & 1 != 0 {
        TriState::Yes
    } else if target_flags & 2 != 0 {
        TriState::Unknown
    } else {
        TriState::No
    };
    let target_casting = if target_cast_flags & 1 != 0 {
        TriState::Yes
    } else if target_cast_flags & 2 != 0 {
        TriState::Unknown
    } else {
        TriState::No
    };
    let target_cast_interruptible = if target_cast_flags & 4 != 0 {
        TriState::Yes
    } else if target_cast_flags & 8 != 0 {
        TriState::No
    } else {
        TriState::Unknown
    };
    let target_hp_pct = if (0..=14).contains(&target_hp_band) {
        (target_hp_band as f64 * 100.0 / 15.0).round() as i32
    } else {
        -1
    };

    let mut slot_range = [TriState::Unknown; SLOT_COUNT];
    let (r30, g30, b30) = n(RANGE_CELL_INDEX);
    let (r31, urgency_nibble, defensive_source_bits) = n(RANGE_CELL_INDEX2);
    slot_range[0] = decode_range(r30 & 3);
    slot_range[1] = decode_range((r30 >> 2) & 3);
    slot_range[2] = decode_range(g30 & 3);
    slot_range[3] = decode_range((g30 >> 2) & 3);
    slot_range[4] = decode_range(b30 & 3);
    slot_range[5] = decode_range((b30 >> 2) & 3);
    slot_range[6] = decode_range(r31 & 3);
    slot_range[7] = decode_range((r31 >> 2) & 3);

    let (buff_r, buff_g, stagger_nibble) = n(BUFF_CELL_INDEX);
    let (class_id, _spec_id, class_flags) = n(CLASS_SPEC_CELL_INDEX);
    let _ = class_id; // class/spec name resolution is the knowledge layer's job.
    let buff_probe_valid = class_flags & CLASS_FLAG_BUFF_PROBE_VALID != 0;
    let mut slot_buff_active = [TriState::Unknown; SLOT_COUNT];
    for (i, slot) in slot_buff_active.iter_mut().enumerate() {
        let active = if i < 4 {
            (buff_r >> i) & 1 != 0
        } else {
            (buff_g >> (i - 4)) & 1 != 0
        };
        *slot = if !buff_probe_valid {
            TriState::Unknown
        } else if active {
            TriState::Yes
        } else {
            TriState::No
        };
    }

    let defensive_urgency = decode_urgency(urgency_nibble);
    let stagger_urgency = decode_urgency(stagger_nibble);
    let defensive_catalog_source = defensive_source_bits & 1 != 0;

    // --- Ext2 (cells 35-39) ---
    let ext2_present = class_flags & CLASS_FLAG_EXT2_PRESENT != 0 && cells.len() >= CELL_COUNT_EXT2;
    let mut hp_curve_valid = false;
    let mut hp_curve_band = -1;
    let mut self_heal2 = None;
    if ext2_present {
        let (curve_r, curve_g, _) = n(HP_CURVE_CELL_INDEX);
        if class_flags & CLASS_FLAG_HP_CURVE_ACTIVE != 0 && (14..=16).contains(&(curve_r + curve_g)) {
            hp_curve_valid = true;
            hp_curve_band = curve_r;
        }

        let mut heal2_sum = 0;
        for i in SELF_HEAL2_CELL_INDEX..=SELF_HEAL2_SPELL_ID_CELL_INDEX + 1 {
            let (r, g, b) = n(i);
            heal2_sum += r + g + b;
        }
        let (_, heal2_checksum, heal2_commit) = n(SELF_HEAL2_COMMIT_CELL_INDEX);
        if heal2_commit == heartbeat && (heal2_sum & 0xF) == heal2_checksum {
            let (h2hi, h2lo, h2flags) = n(SELF_HEAL2_CELL_INDEX);
            if h2flags & FLAG_VALID != 0 {
                let (s0, s1, s2) = n(SELF_HEAL2_SPELL_ID_CELL_INDEX);
                let (s3, s4, s5) = n(SELF_HEAL2_SPELL_ID_CELL_INDEX + 1);
                let spell_id = (s0 << 20) | (s1 << 16) | (s2 << 12) | (s3 << 8) | (s4 << 4) | s5;
                self_heal2 = Some(SelfHeal2Slot {
                    stroke: KeyStroke {
                        virtual_key: ((h2hi << 4) | h2lo) as u8,
                        shift: h2flags & FLAG_SHIFT != 0,
                        ctrl: h2flags & FLAG_CTRL != 0,
                        alt: h2flags & FLAG_ALT != 0,
                    },
                    spell_id,
                    range: decode_range(cast_flags_raw & 3),
                });
            }
        }
    }

    // --- Ext3 (cells 40-42) ---
    let ext3_present =
        cast_flags_raw & CAST_FLAG_EXT3_PRESENT != 0 && cells.len() >= CELL_COUNT_EXT3;
    let mut ext3 = None;
    if ext3_present {
        let (m0, m1, m2) = n(EXT3_MASK_CELL_INDEX);
        let (m_hi, epoch, blocked) = n(EXT3_FLAGS_CELL_INDEX);
        let ext3_sum = m0 + m1 + m2 + m_hi + epoch + blocked;
        let (_, ext3_checksum, ext3_commit) = n(EXT3_COMMIT_CELL_INDEX);
        if ext3_commit == heartbeat && (ext3_sum & 0xF) == ext3_checksum {
            ext3 = Some(Ext3Block {
                mask: (m0 | (m1 << 4) | (m2 << 8) | ((m_hi & 3) << 12)) as u16,
                epoch,
                blocked,
            });
        }
    }

    Ok(BridgeFrame {
        state,
        heartbeat,
        version: ver,
        status_flags,
        slots,
        spell_ids,
        hp_pct,
        cast,
        cast_remaining_band: cast_band,
        target_casting,
        target_cast_interruptible,
        target_in_melee,
        target_hp_pct,
        slot_range,
        slot_buff_active,
        buff_probe_valid,
        defensive_urgency,
        stagger_urgency,
        defensive_catalog_source,
        hp_curve_valid,
        hp_curve_band,
        self_heal2,
        ext3_present,
        ext3,
    })
}

fn decode_urgency(nibble: i32) -> DefensiveUrgency {
    match nibble {
        1 => DefensiveUrgency::White,
        2 => DefensiveUrgency::Yellow,
        3 => DefensiveUrgency::Orange,
        4 => DefensiveUrgency::Red,
        _ => DefensiveUrgency::Unknown,
    }
}

fn decode_range(value: i32) -> TriState {
    match value {
        RANGE_IN => TriState::Yes,
        RANGE_OUT => TriState::No,
        _ => TriState::Unknown,
    }
}
