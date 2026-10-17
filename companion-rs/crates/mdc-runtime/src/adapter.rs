//! Decode adapter: maps a protocol [`BridgeFrame`] onto the engine's
//! [`FrameSnapshot`] contract.
//!
//! The two crates deliberately define their own `Slot`, `KeyStroke` and
//! `BridgeState` types, and their slot **orders differ**: the wire order is
//! `Main, Offensive, Defensive, Consumable, Trinket, Interrupt, Mobility,
//! SelfHeal`, while the engine orders slots by decision rank
//! (`Interrupt, Defensive, Main, Offensive, Consumable, Trinket, Mobility,
//! SelfHeal`). This adapter is the single place that remaps indices so neither
//! crate has to know the other's layout.

use mdc_engine::{
    BridgeState as EngineBridgeState, CastState, FrameSnapshot, KeyStroke as EngineKeyStroke,
};
use mdc_protocol::{BridgeFrame, BridgeState as ProtocolBridgeState, PlayerCastState};

/// Wire slot index -> engine slot index.
///
/// Wire: Main 0, Offensive 1, Defensive 2, Consumable 3, Trinket 4, Interrupt 5,
/// Mobility 6, SelfHeal 7.
const PROTOCOL_TO_ENGINE: [usize; 8] = [2, 3, 1, 4, 5, 0, 6, 7];

/// Converts a decoded wire frame into the engine's snapshot, remapping slot
/// indices and translating the two local `BridgeState` enums.
pub fn to_snapshot(frame: &BridgeFrame) -> FrameSnapshot {
    let mut slots: [Option<EngineKeyStroke>; 8] = [None; 8];
    let mut spell_ids = [0i32; 8];

    for (protocol_index, slot) in frame.slots.iter().enumerate() {
        let engine_index = PROTOCOL_TO_ENGINE[protocol_index];
        slots[engine_index] = slot.map(|ks| EngineKeyStroke {
            virtual_key: u16::from(ks.virtual_key),
            shift: ks.shift,
            ctrl: ks.ctrl,
            alt: ks.alt,
        });
        spell_ids[engine_index] = frame.spell_ids[protocol_index];
    }

    FrameSnapshot {
        version: frame.version as u8,
        heartbeat: frame.heartbeat,
        state: map_state(frame.state),
        has_target: frame.has_target(),
        in_combat: frame.in_combat(),
        on_gcd: frame.on_gcd(),
        slots,
        spell_ids,
    }
}

/// Maps the wire cast/channel sensor onto the engine's cast state (`Unknown`
/// collapses to `None`, matching the engine contract).
pub fn cast_state(frame: &BridgeFrame) -> CastState {
    match frame.cast {
        PlayerCastState::Casting => CastState::Casting,
        PlayerCastState::Channeling => CastState::Channeling,
        PlayerCastState::None | PlayerCastState::Unknown => CastState::None,
    }
}

fn map_state(state: ProtocolBridgeState) -> EngineBridgeState {
    match state {
        ProtocolBridgeState::Idle => EngineBridgeState::Idle,
        ProtocolBridgeState::Active => EngineBridgeState::Active,
        ProtocolBridgeState::Paused => EngineBridgeState::Paused,
        ProtocolBridgeState::NeedTarget => EngineBridgeState::NeedTarget,
        ProtocolBridgeState::NeedInteract => EngineBridgeState::NeedInteract,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use mdc_protocol::{DefensiveUrgency, KeyStroke, Slot as ProtocolSlot, TriState};

    fn blank_frame() -> BridgeFrame {
        BridgeFrame {
            state: ProtocolBridgeState::Active,
            heartbeat: 1,
            version: 5,
            status_flags: 0,
            slots: [None; 8],
            spell_ids: [0; 8],
            hp_pct: -1,
            cast: PlayerCastState::Unknown,
            cast_remaining_band: 15,
            target_casting: TriState::Unknown,
            target_cast_interruptible: TriState::Unknown,
            target_in_melee: TriState::Unknown,
            target_hp_pct: -1,
            slot_range: [TriState::Unknown; 8],
            slot_buff_active: [TriState::Unknown; 8],
            buff_probe_valid: false,
            defensive_urgency: DefensiveUrgency::Unknown,
            stagger_urgency: DefensiveUrgency::Unknown,
            defensive_catalog_source: false,
            hp_curve_valid: false,
            hp_curve_band: -1,
            self_heal2: None,
            ext3_present: false,
            ext3: None,
        }
    }

    #[test]
    fn remaps_wire_slots_into_engine_order() {
        let mut frame = blank_frame();
        frame.slots[ProtocolSlot::Main as usize] =
            Some(KeyStroke { virtual_key: 0x52, shift: false, ctrl: false, alt: false });
        frame.slots[ProtocolSlot::Interrupt as usize] =
            Some(KeyStroke { virtual_key: 0x50, shift: true, ctrl: false, alt: false });
        frame.slots[ProtocolSlot::SelfHeal as usize] =
            Some(KeyStroke { virtual_key: 0x51, shift: false, ctrl: false, alt: false });
        frame.spell_ids[ProtocolSlot::Main as usize] = 100;

        let snap = to_snapshot(&frame);
        assert_eq!(snap.version, 5);
        assert_eq!(snap.slots[mdc_engine::Slot::Main.index()].unwrap().virtual_key, 0x52);
        assert!(snap.slots[mdc_engine::Slot::Interrupt.index()].unwrap().shift);
        assert_eq!(snap.slots[mdc_engine::Slot::SelfHeal.index()].unwrap().virtual_key, 0x51);
        assert_eq!(snap.spell_ids[mdc_engine::Slot::Main.index()], 100);
        // Wire Offensive must not land where the engine expects Main.
        assert!(snap.slots[mdc_engine::Slot::Offensive.index()].is_none());
    }

    #[test]
    fn carries_status_flags_and_cast_state() {
        let mut frame = blank_frame();
        frame.status_flags =
            mdc_protocol::STATUS_FLAG_IN_COMBAT | mdc_protocol::STATUS_FLAG_HAS_TARGET | mdc_protocol::STATUS_FLAG_ON_GCD;
        frame.cast = PlayerCastState::Channeling;
        let snap = to_snapshot(&frame);
        assert!(snap.in_combat);
        assert!(snap.has_target);
        assert!(snap.on_gcd);
        assert_eq!(cast_state(&frame), CastState::Channeling);
    }
}
