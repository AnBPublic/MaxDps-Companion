//! Golden protocol vectors for `mdc-protocol` (parity harness).
//!
//! Task 1/3 ships **synthetic, self-consistent** vectors (a tiny in-test
//! encoder) that exercise every decode branch: v1/v4/v5 dispatch, the
//! cells-10/34/39/42 checksums, slots 1-8 and Ext2/Ext3. The real
//! capture-vs-C#-`PixelProtocol.Decode` vectors are **TODO** (see
//! [`GOLDEN_VECTOR_TODO`] and the ignored test at the bottom).

use mdc_protocol::{
    decode, BridgeState, Color, DecodeError, DefensiveUrgency, PlayerCastState, TriState,
    CELL_COUNT, CELL_COUNT_EXT2, CELL_COUNT_EXT3,
};

// --- in-test encoder helpers (inverse of the pure decode, exact n*17 ramp) ---

fn byte(n: i32) -> u8 {
    (n.clamp(0, 15) * 17) as u8
}

/// A cell whose three channels carry the given nibbles.
fn cell(r: i32, g: i32, b: i32) -> Color {
    Color::new(byte(r), byte(g), byte(b))
}

fn black() -> Color {
    cell(0, 0, 0)
}

/// Magic magenta: nibbles (15, 0, 15) -> bytes (255, 0, 255).
fn magic() -> Color {
    cell(15, 0, 15)
}

fn nibbles_of(c: Color) -> (i32, i32, i32) {
    (
        c.r as i32 / 17,
        c.g as i32 / 17,
        c.b as i32 / 17,
    )
}

fn sum_cells(cells: &[Color], from: usize, to: usize) -> i32 {
    let mut sum = 0;
    for c in &cells[from..=to] {
        let (r, g, b) = nibbles_of(*c);
        sum += r + g + b;
    }
    sum
}

/// Fill the v5 checksum/commit cells (10 and 34) for the current cells.
fn finalize_v5(mut cells: Vec<Color>, heartbeat: i32) -> Vec<Color> {
    let core = sum_cells(&cells, 1, 9) & 0xF;
    cells[10] = cell(5, core, heartbeat);
    let ext = sum_cells(&cells, 11, 33) & 0xF;
    cells[34] = cell(0, ext, heartbeat);
    cells
}

/// Build a valid 35-cell v5 core frame with `state`/`status_flags` in cell 9.
fn core_v5(heartbeat: i32, state: i32, status_flags: i32) -> Vec<Color> {
    let mut cells = vec![black(); CELL_COUNT];
    cells[0] = magic();
    cells[9] = cell(state, heartbeat, status_flags);
    finalize_v5(cells, heartbeat)
}

fn finalize_legacy(
    cells: &mut [Color],
    status_index: usize,
    version_index: usize,
    heartbeat: i32,
    state: i32,
    status_flags: i32,
    version: i32,
) {
    cells[status_index] = cell(state, heartbeat, status_flags);
    let sum = sum_cells(cells, 1, status_index) & 0xF;
    cells[version_index] = cell(version, sum, heartbeat);
}

// --- v5 core ---

#[test]
fn v5_empty_frame_decodes() {
    let frame = decode(&core_v5(7, 1, 0)).expect("valid v5 core frame");
    assert_eq!(frame.version, 5);
    assert_eq!(frame.heartbeat, 7);
    assert_eq!(frame.state, BridgeState::Active);
    assert_eq!(frame.status_flags, 0);
    assert!(frame.slots.iter().all(Option::is_none));
    assert!(frame.spell_ids.iter().all(|&id| id == 0));
    assert_eq!(frame.hp_pct, -1);
    // Cell 28 R = 0 is cast-state "none" (PixelProtocol.cs:517), not Unknown.
    assert_eq!(frame.cast, PlayerCastState::None);
    // Cell 29 G = band 0 is a real ~0% reading (band 15 is UNKNOWN).
    assert_eq!(frame.target_hp_pct, 0);
    assert_eq!(frame.defensive_urgency, DefensiveUrgency::Unknown);
    assert_eq!(frame.stagger_urgency, DefensiveUrgency::Unknown);
    assert!(!frame.ext3_present);
    assert!(frame.ext3.is_none());
    assert!(frame.self_heal2.is_none());
}

#[test]
fn v5_decodes_slot_and_spell_id() {
    let mut cells = core_v5(3, 1, 0);
    // Main slot (cell 1): vk 0x11, Shift held, valid.
    cells[1] = cell(1, 1, 8 | 1);
    // Main spell id 0x123456 across cells 11-12.
    cells[11] = cell(1, 2, 3);
    cells[12] = cell(4, 5, 6);
    let cells = finalize_v5(cells, 3);

    let frame = decode(&cells).expect("valid frame");
    let main = frame.slots[0].expect("main slot present");
    assert_eq!(main.virtual_key, 0x11);
    assert!(main.shift);
    assert!(!main.ctrl && !main.alt);
    assert_eq!(frame.spell_ids[0], 0x123456);
    assert!(frame.slots[1..].iter().all(Option::is_none));
}

#[test]
fn v5_vitals_and_target_decode() {
    let mut cells = core_v5(9, 1, 0);
    cells[27] = cell(6, 4, 1); // hp 6*16+4 = 100
    cells[29] = cell(1, 7, 1 | 4); // melee yes, band 7, casting+interruptible
    let cells = finalize_v5(cells, 9);

    let frame = decode(&cells).expect("valid frame");
    assert_eq!(frame.hp_pct, 100);
    assert_eq!(frame.target_in_melee, TriState::Yes);
    assert_eq!(frame.target_hp_pct, 47); // round(7*100/15)
    assert_eq!(frame.target_casting, TriState::Yes);
    assert_eq!(frame.target_cast_interruptible, TriState::Yes);
}

// --- Ext2 (40 cells) ---

#[test]
fn v5_ext2_hp_curve_and_self_heal2() {
    let mut cells = vec![black(); CELL_COUNT_EXT2];
    cells[0] = magic();
    cells[9] = cell(1, 5, 0);
    cells[28] = cell(0, 0, 0); // no Ext3, SelfHeal2 range Unknown
    cells[33] = cell(0, 0, 4 | 8); // Ext2 present + HP curve active
    cells[35] = cell(10, 5, 0); // nR + nG = 15 (in band 14..=16)
    cells[36] = cell(1, 2, 8); // SelfHeal2 key vk 0x12, valid
    cells[37] = cell(1, 2, 3);
    cells[38] = cell(4, 5, 6); // spell id 0x123456
    let cells = finalize_v5(cells, 5);
    // finalize_v5 only rewrote cell 34; cell 39 needs its own checksum.
    let mut cells = cells;
    let heal2 = sum_cells(&cells, 36, 38) & 0xF;
    cells[39] = cell(0, heal2, 5);

    let frame = decode(&cells).expect("valid Ext2 frame");
    assert!(frame.hp_curve_valid);
    assert_eq!(frame.hp_curve_band, 10);
    let sh2 = frame.self_heal2.expect("SelfHeal2 decoded");
    assert_eq!(sh2.spell_id, 0x123456);
    assert_eq!(sh2.stroke.virtual_key, 0x12);
    assert_eq!(sh2.range, TriState::Unknown);
}

// --- Ext3 (43 cells) ---

#[test]
fn v5_ext3_mask_epoch_blocked() {
    let mut cells = vec![black(); CELL_COUNT_EXT3];
    cells[0] = magic();
    cells[9] = cell(1, 4, 0);
    cells[28] = cell(0, 0, 4); // Ext3 present (bit2)
    cells[40] = cell(4, 3, 2); // mask bits 0-11
    cells[41] = cell(1, 7, 0); // bits 12-13 = 1, epoch 7, blocked 0
    let mut cells = finalize_v5(cells, 4);
    let ext3_sum = sum_cells(&cells, 40, 41) & 0xF;
    cells[42] = cell(0, ext3_sum, 4);

    let frame = decode(&cells).expect("valid Ext3 frame");
    assert!(frame.ext3_present);
    let ext3 = frame.ext3.expect("Ext3 decoded");
    assert_eq!(ext3.mask, 0x1234);
    assert_eq!(ext3.epoch, 7);
    assert_eq!(ext3.blocked, 0);
}

#[test]
fn ext3_block_checksum_failure_drops_only_the_block() {
    let mut cells = vec![black(); CELL_COUNT_EXT3];
    cells[0] = magic();
    cells[9] = cell(1, 4, 0);
    cells[28] = cell(0, 0, 4);
    cells[40] = cell(4, 3, 2);
    cells[41] = cell(1, 7, 0);
    let mut cells = finalize_v5(cells, 4);
    cells[42] = cell(0, 0x0F, 4); // deliberately wrong checksum

    let frame = decode(&cells).expect("frame kept, block dropped");
    assert!(frame.ext3_present);
    assert!(frame.ext3.is_none());
}

// --- legacy windows ---

#[test]
fn v4_frame_decodes() {
    let mut cells = vec![black(); 9];
    cells[0] = magic();
    cells[1] = cell(1, 1, 8);
    finalize_legacy(&mut cells, 7, 8, 2, 1, 0, 4);

    let frame = decode(&cells).expect("valid v4 frame");
    assert_eq!(frame.version, 4);
    assert_eq!(frame.heartbeat, 2);
    assert_eq!(frame.slots[0].expect("main").virtual_key, 0x11);
    assert_eq!(frame.hp_pct, -1);
}

#[test]
fn v1_frame_decodes_with_slot_remap() {
    let mut cells = vec![black(); 8];
    cells[0] = magic();
    cells[1] = cell(0x2, 0x3, 8); // Main -> slot 0
    cells[4] = cell(0x4, 0x5, 8); // v1 slot 3 -> v4 Defensive (slot 2)
    finalize_legacy(&mut cells, 6, 7, 6, 1, 0, 1);

    let frame = decode(&cells).expect("valid v1 frame");
    assert_eq!(frame.version, 1);
    assert_eq!(frame.slots[0].expect("main").virtual_key, 0x23);
    assert_eq!(frame.slots[2].expect("defensive remap").virtual_key, 0x45);
    assert!(frame.slots[1].is_none());
}

// --- rejections ---

#[test]
fn core_checksum_mismatch_is_rejected() {
    let mut cells = core_v5(7, 1, 0);
    cells[10] = cell(5, 0xF, 7); // corrupt the core checksum nibble
    assert_eq!(decode(&cells), Err(DecodeError::Checksum));
}

#[test]
fn wrong_version_is_rejected() {
    let mut cells = core_v5(7, 1, 0);
    cells[10] = cell(4, 0, 7); // v4 nibble in a 35-cell capture
    assert_eq!(decode(&cells), Err(DecodeError::Version));
}

#[test]
fn commit_mismatch_is_rejected() {
    let mut cells = core_v5(7, 1, 0);
    cells[10] = cell(5, sum_cells(&cells, 1, 9) & 0xF, 8); // commit != heartbeat
    assert_eq!(decode(&cells), Err(DecodeError::Commit));
}

#[test]
fn unsupported_length_is_rejected() {
    let cells = vec![black(); 10];
    assert_eq!(decode(&cells), Err(DecodeError::Length));
}

#[test]
fn missing_magic_is_rejected() {
    let mut cells = core_v5(7, 1, 0);
    cells[0] = black();
    assert_eq!(decode(&cells), Err(DecodeError::Magic));
}

// --- real-capture golden vectors (TODO, task 2/3) ---

/// TODO(task 2/3): replace with real captured frames exported from the C#
/// harness / live client and asserted byte-for-byte against
/// `PixelProtocol.Decode`. One entry per supported width: 43 (Ext3), 40 (Ext2),
/// 35 (core), 9 (v4), 8 (v1).
const GOLDEN_VECTOR_TODO: &[&str] = &[
    "TODO: v5/Ext3 43-cell capture -> tests/fixtures/imprint/ext3-43.raw",
    "TODO: v5/Ext2 40-cell capture -> tests/fixtures/imprint/ext2-40.raw",
    "TODO: v5 core 35-cell capture -> tests/fixtures/imprint/core-35.raw",
    "TODO: v4 9-cell stale-addon capture -> tests/fixtures/imprint/v4-9.raw",
    "TODO: v1 8-cell stale-addon capture -> tests/fixtures/imprint/v1-8.raw",
];

#[test]
#[ignore = "placeholder: wired to real captured frames in task 2/3"]
fn golden_vectors_match_reference_decoder() {
    assert_eq!(GOLDEN_VECTOR_TODO.len(), 5);
}
