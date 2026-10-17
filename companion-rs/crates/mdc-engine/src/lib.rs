//! `mdc-engine` — deterministic rotation core for the Rust companion.
//!
//! Contracts-first scaffold (plan `docs/plans/2026-10-10-rust-companion.md` §3).
//! Pure: no I/O, no platform dependency, no clock reads — every timestamp is
//! supplied through the injected [`Clock`] or passed by the caller as `now_ms`.
//! Mirrors the C# `Scheduler/**`, `Decision/**` and `RotationEngine` behaviour
//! at the level of ordering, pacing and holds (not the wire decode, which is
//! owned by the disjoint `mdc-protocol` crate).
//!
//! Determinism rules: `BTreeMap`/`BTreeSet` only (no hash iteration order),
//! integer time in milliseconds, threshold comparisons are `>=`.

#![forbid(unsafe_code)]

use std::collections::{BTreeMap, BTreeSet};

use serde::{Deserialize, Serialize};

/// Milliseconds on the injected monotonic clock.
pub type Ms = i64;

/// Number of MaxDps slots the engine reasons about.
pub const SLOT_COUNT: usize = 8;

// ---------------------------------------------------------------------------
// Injected clock
// ---------------------------------------------------------------------------

/// Injected time source. The engine never calls the OS; callers provide the
/// implementation so tests and replay are bit-for-bit reproducible.
pub trait Clock {
    fn now_ms(&self) -> Ms;
}

/// Manually advanced clock for tests and replay.
#[derive(Clone, Copy, Debug, Default)]
pub struct ManualClock {
    now: Ms,
}

impl ManualClock {
    pub const fn new(now: Ms) -> Self {
        Self { now }
    }
    pub fn advance(&mut self, delta_ms: Ms) {
        self.now = self.now.saturating_add(delta_ms);
    }
    pub fn set(&mut self, now: Ms) {
        self.now = now;
    }
}

impl Clock for ManualClock {
    fn now_ms(&self) -> Ms {
        self.now
    }
}

/// Frozen clock returning a constant time (handy in unit tests).
#[derive(Clone, Copy, Debug, Default)]
pub struct FixedClock(pub Ms);

impl Clock for FixedClock {
    fn now_ms(&self) -> Ms {
        self.0
    }
}

// ---------------------------------------------------------------------------
// Core value types (engine contract)
// ---------------------------------------------------------------------------

/// Companion slot identity. Index order is the engine's own contract; the
/// protocol crate maps its wire cells onto these.
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Debug, Serialize, Deserialize)]
#[repr(usize)]
pub enum Slot {
    Interrupt = 0,
    Defensive = 1,
    Main = 2,
    Offensive = 3,
    Consumable = 4,
    Trinket = 5,
    Mobility = 6,
    SelfHeal = 7,
}

impl Slot {
    /// All slots in canonical index order.
    pub const ALL: [Slot; SLOT_COUNT] = [
        Slot::Interrupt,
        Slot::Defensive,
        Slot::Main,
        Slot::Offensive,
        Slot::Consumable,
        Slot::Trinket,
        Slot::Mobility,
        Slot::SelfHeal,
    ];

    pub const fn index(self) -> usize {
        self as usize
    }

    pub const fn from_index(i: usize) -> Option<Slot> {
        match i {
            0 => Some(Slot::Interrupt),
            1 => Some(Slot::Defensive),
            2 => Some(Slot::Main),
            3 => Some(Slot::Offensive),
            4 => Some(Slot::Consumable),
            5 => Some(Slot::Trinket),
            6 => Some(Slot::Mobility),
            7 => Some(Slot::SelfHeal),
            _ => None,
        }
    }

    pub const fn name(self) -> &'static str {
        match self {
            Slot::Interrupt => "Interrupt",
            Slot::Defensive => "Defensive",
            Slot::Main => "Main",
            Slot::Offensive => "Offensive",
            Slot::Consumable => "Consumable",
            Slot::Trinket => "Trinket",
            Slot::Mobility => "Mobility",
            Slot::SelfHeal => "SelfHeal",
        }
    }
}

/// A physical key press identity (virtual key plus modifiers).
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Debug, Default, Serialize, Deserialize)]
pub struct KeyStroke {
    pub virtual_key: u16,
    pub shift: bool,
    pub ctrl: bool,
    pub alt: bool,
}

impl KeyStroke {
    pub const fn new(virtual_key: u16) -> Self {
        Self { virtual_key, shift: false, ctrl: false, alt: false }
    }

    pub const fn with_mods(virtual_key: u16, shift: bool, ctrl: bool, alt: bool) -> Self {
        Self { virtual_key, shift, ctrl, alt }
    }

    /// Human-readable form for status lines and telemetry.
    pub fn describe(&self) -> String {
        let mut prefix = String::new();
        if self.ctrl {
            prefix.push_str("Ctrl+");
        }
        if self.alt {
            prefix.push_str("Alt+");
        }
        if self.shift {
            prefix.push_str("Shift+");
        }
        format!("{prefix}VK{:02X}", self.virtual_key)
    }
}

/// Bridge state flags decoded from the strip.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Serialize, Deserialize)]
pub enum BridgeState {
    #[default]
    Idle,
    Active,
    Paused,
    NeedTarget,
    NeedInteract,
}

/// Engine-side view of one decoded frame. The protocol crate owns the bytes;
/// this is the minimal snapshot the scheduler needs.
#[derive(Clone, Debug, Default)]
pub struct FrameSnapshot {
    /// Wire version (1, 4, 5, 6). v1 carries no GCD/target info.
    pub version: u8,
    pub heartbeat: i32,
    pub state: BridgeState,
    pub has_target: bool,
    pub in_combat: bool,
    pub on_gcd: bool,
    pub slots: [Option<KeyStroke>; SLOT_COUNT],
    pub spell_ids: [i32; SLOT_COUNT],
}

impl FrameSnapshot {
    /// The protocol versions this engine accepts (mirrors `SupportedVersion*`).
    pub fn known_protocol(&self) -> bool {
        matches!(self.version, 1 | 4 | 5 | 6)
    }
}

/// Player cast/channel state (from the situational context).
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum CastState {
    #[default]
    None,
    Casting,
    Channeling,
}

/// Reason codes for a held frame (no action emitted).
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize, Deserialize)]
pub enum ScheduleReason {
    StaleFrame,
    LinkLost,
    ProtocolMismatch,
    Paused,
    OutOfCombat,
    NoTarget,
    NoCandidate,
    CastHold,
    ChannelHold,
    PolicyHold,
    PolicySkip,
    GcdHold,
    MinInterval,
    RetryBackoff,
    Unavailable,
    RepeatSuppressed,
    Scheduled,
}

/// Outcome of an attempt recorded by the OS send path.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize, Deserialize)]
pub enum AttemptOutcome {
    Sent,
    MovementBound,
    PhysicalHold,
    FocusRequired,
    WindowLost,
    GcdIgnored,
}

/// A candidate suggestion derived from a decoded frame.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct ActionCandidate {
    pub slot: Slot,
    pub stroke: KeyStroke,
    pub spell_id: i32,
    pub enabled: bool,
    pub first_seen_ms: Ms,
    pub last_changed_ms: Ms,
    pub pressed_since_change: bool,
}

impl ActionCandidate {
    /// Stuck-suggestion test: pressed since it last changed AND unchanged for
    /// the whole stale window.
    pub fn is_stale(&self, now_ms: Ms, stale_after_ms: i32) -> bool {
        self.pressed_since_change
            && now_ms.saturating_sub(self.last_changed_ms) >= (stale_after_ms.max(250) as Ms)
    }
}

/// One scheduled action in a plan.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct ScheduledAction {
    pub slot: Slot,
    pub stroke: KeyStroke,
    pub spell_id: i32,
    pub reason: ScheduleReason,
    pub interrupt: bool,
    pub provider: String,
    pub evidence: Vec<String>,
}

/// Deterministic plan for one tick.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct SchedulePlan {
    pub actions: Vec<ScheduledAction>,
    pub head: Option<Slot>,
    pub reason: ScheduleReason,
    pub confidence: i32,
    pub demoted: bool,
    pub suppressed: u32,
    pub policy_held: u32,
    pub policy_skipped: u32,
    pub detail: Option<String>,
}

impl SchedulePlan {
    pub fn hold(reason: ScheduleReason) -> Self {
        Self {
            actions: Vec::new(),
            head: None,
            reason,
            confidence: 0,
            demoted: false,
            suppressed: 0,
            policy_held: 0,
            policy_skipped: 0,
            detail: None,
        }
    }
}

// ---------------------------------------------------------------------------
// Candidate tracker
// ---------------------------------------------------------------------------

#[derive(Clone, Copy, Debug, Default)]
struct Tracked {
    present: bool,
    stroke: KeyStroke,
    spell_id: i32,
    first_seen_ms: Ms,
    last_changed_ms: Ms,
    pressed_since_change: bool,
}

/// Per-slot suggestion history: first-seen, last-changed, pressed-since-change
/// and TTL expiry. Deterministic; no I/O.
#[derive(Clone, Debug)]
pub struct CandidateTracker {
    slots: [Tracked; SLOT_COUNT],
    ttl_ms: Ms,
}

impl CandidateTracker {
    pub const DEFAULT_TTL_MS: Ms = 4000;

    pub fn new(ttl_ms: Ms) -> Self {
        Self { slots: [Tracked::default(); SLOT_COUNT], ttl_ms }
    }

    pub const fn ttl_ms(&self) -> Ms {
        self.ttl_ms
    }

    pub fn reset(&mut self) {
        self.slots = [Tracked::default(); SLOT_COUNT];
    }

    /// Records one decoded frame's suggestions.
    pub fn update(&mut self, frame: &FrameSnapshot, now_ms: Ms) {
        for i in 0..SLOT_COUNT {
            match frame.slots[i] {
                Some(stroke) => {
                    let spell = frame.spell_ids[i];
                    let t = &mut self.slots[i];
                    if !t.present {
                        t.present = true;
                        t.stroke = stroke;
                        t.spell_id = spell;
                        t.first_seen_ms = now_ms;
                        t.last_changed_ms = now_ms;
                        t.pressed_since_change = false;
                    } else if t.stroke != stroke || t.spell_id != spell {
                        t.stroke = stroke;
                        t.spell_id = spell;
                        t.last_changed_ms = now_ms;
                        t.pressed_since_change = false;
                    }
                }
                None => self.slots[i].present = false,
            }
        }
    }

    /// Marks a slot as pressed since it last changed.
    pub fn note_pressed(&mut self, slot: Slot, now_ms: Ms, spell_id: i32) {
        let t = &mut self.slots[slot.index()];
        if t.present && (spell_id == 0 || t.spell_id == spell_id) {
            t.pressed_since_change = true;
            let _ = now_ms;
        }
    }

    /// Snapshot of live candidates for the scheduler/decision layer.
    pub fn snapshot(
        &self,
        enabled: &[bool; SLOT_COUNT],
        now_ms: Ms,
        ttl_ms: Ms,
    ) -> Vec<ActionCandidate> {
        let mut out = Vec::new();
        for (i, t) in self.slots.iter().copied().enumerate() {
            if !t.present || now_ms.saturating_sub(t.last_changed_ms) > ttl_ms {
                continue;
            }
            let slot = Slot::from_index(i).expect("slot index in range");
            out.push(ActionCandidate {
                slot,
                stroke: t.stroke,
                spell_id: t.spell_id,
                enabled: enabled[i],
                first_seen_ms: t.first_seen_ms,
                last_changed_ms: t.last_changed_ms,
                pressed_since_change: t.pressed_since_change,
            });
        }
        out
    }
}

impl Default for CandidateTracker {
    fn default() -> Self {
        Self::new(Self::DEFAULT_TTL_MS)
    }
}

// ---------------------------------------------------------------------------
// Policy seam (knowledge crate plugs in here)
// ---------------------------------------------------------------------------

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum PolicyVerdict {
    Use,
    Hold,
    Skip,
    Unknown,
}

#[derive(Clone, Debug)]
pub struct PolicyDecision {
    pub verdict: PolicyVerdict,
    pub emergency: bool,
    pub reason: String,
    pub provider: String,
    pub evidence: Vec<String>,
}

impl PolicyDecision {
    pub fn use_now() -> Self {
        Self {
            verdict: PolicyVerdict::Use,
            emergency: false,
            reason: "policy: use".to_string(),
            provider: String::new(),
            evidence: Vec::new(),
        }
    }
}

#[derive(Clone, Copy, Debug)]
pub struct PolicyInput {
    pub slot: Slot,
    pub spell_id: i32,
    pub in_combat: bool,
    pub has_target: bool,
    pub now_ms: Ms,
}

/// Situational policy evaluator (mirrors `PolicyEvaluator.Evaluate`).
pub trait PolicyEvaluator {
    fn evaluate(&self, input: &PolicyInput) -> PolicyDecision;
}

/// Ability knowledge seam (off-GCD / rides-GCD lookups).
pub trait Knowledge {
    fn off_gcd(&self, _spell_id: i32) -> bool {
        false
    }
    fn rides_gcd(&self, _spell_id: i32) -> bool {
        false
    }
}

/// Empty knowledge base: nothing is known to be off-GCD.
#[derive(Clone, Copy, Debug, Default)]
pub struct DefaultKnowledge;

impl Knowledge for DefaultKnowledge {}

/// Execution-safety reason for the cast/channel hold.
pub const CAST_REASON: &str = "player is casting";
pub const CHANNEL_REASON: &str = "player is channeling";

// ---------------------------------------------------------------------------
// Decision engine (legacy order evaluator)
// ---------------------------------------------------------------------------

/// Legacy priority (intelligence disabled) — DO NOT reorder (pinned contract).
pub const FALLBACK_ORDER: [Slot; 6] =
    [Slot::Main, Slot::Offensive, Slot::Interrupt, Slot::Defensive, Slot::Consumable, Slot::Trinket];

/// Enabled priority with the two companion-only slots inserted.
pub const ENABLED_ORDER: [Slot; 8] = [
    Slot::Interrupt,
    Slot::SelfHeal,
    Slot::Main,
    Slot::Defensive,
    Slot::Mobility,
    Slot::Offensive,
    Slot::Consumable,
    Slot::Trinket,
];

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize, Deserialize)]
pub enum DecisionReason {
    InterruptUrgency,
    DefensiveUrgency,
    SelfSustain,
    MainRotation,
    MobilityUplift,
    OffensiveCooldown,
    Consumable,
    Trinket,
    GcdHold,
    NoCandidate,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct DecisionResult {
    pub order: Vec<Slot>,
    pub head: Option<Slot>,
    pub reason: DecisionReason,
    pub confidence: i32,
    pub demoted: bool,
}

impl DecisionResult {
    pub fn fallback() -> Self {
        Self {
            order: FALLBACK_ORDER.to_vec(),
            head: None,
            reason: DecisionReason::NoCandidate,
            confidence: 0,
            demoted: false,
        }
    }
}

pub struct DecisionContext {
    pub candidates: Vec<ActionCandidate>,
    pub now_ms: Ms,
    pub stale_after_ms: i32,
    pub on_gcd: bool,
}

/// Deterministic order evaluator (mirrors C# `DecisionEngine.Evaluate`).
pub struct DecisionEngine;

impl DecisionEngine {
    pub fn evaluate(ctx: &DecisionContext) -> DecisionResult {
        let mut by_slot: [Option<ActionCandidate>; SLOT_COUNT] = [None; SLOT_COUNT];
        for c in &ctx.candidates {
            by_slot[c.slot.index()] = Some(*c);
        }
        let live: Vec<ActionCandidate> = ENABLED_ORDER
            .iter()
            .filter_map(|s| by_slot[s.index()])
            .filter(|c| c.enabled)
            .collect();
        if live.is_empty() {
            return DecisionResult {
                order: Vec::new(),
                head: None,
                reason: DecisionReason::NoCandidate,
                confidence: 0,
                demoted: false,
            };
        }

        let mut unique: Vec<ActionCandidate> = Vec::new();
        for c in live {
            if !unique.iter().any(|k| k.stroke == c.stroke) {
                unique.push(c);
            }
        }

        let mut fresh = Vec::new();
        let mut stale = Vec::new();
        for c in unique {
            if !matches!(c.slot, Slot::SelfHeal | Slot::Main) && c.is_stale(ctx.now_ms, ctx.stale_after_ms)
            {
                stale.push(c);
            } else {
                fresh.push(c);
            }
        }
        let demoted = !stale.is_empty() && !fresh.is_empty();
        let mut order = fresh;
        order.extend(stale);

        let head = order[0];
        let mut reason = Self::reason_for(head.slot);
        let mut confidence = Self::confidence_for(reason);
        if !head.enabled {
            confidence -= 20;
        }
        if head.slot != Slot::Main && head.is_stale(ctx.now_ms, ctx.stale_after_ms) {
            confidence -= 40;
        }
        if ctx.on_gcd && head.slot != Slot::Interrupt {
            reason = DecisionReason::GcdHold;
            confidence = confidence.min(20);
        }

        DecisionResult {
            order: order.iter().map(|c| c.slot).collect(),
            head: Some(head.slot),
            reason,
            confidence: confidence.clamp(0, 100),
            demoted,
        }
    }

    fn reason_for(slot: Slot) -> DecisionReason {
        match slot {
            Slot::Interrupt => DecisionReason::InterruptUrgency,
            Slot::Defensive => DecisionReason::DefensiveUrgency,
            Slot::SelfHeal => DecisionReason::SelfSustain,
            Slot::Main => DecisionReason::MainRotation,
            Slot::Mobility => DecisionReason::MobilityUplift,
            Slot::Offensive => DecisionReason::OffensiveCooldown,
            Slot::Consumable => DecisionReason::Consumable,
            Slot::Trinket => DecisionReason::Trinket,
        }
    }

    fn confidence_for(reason: DecisionReason) -> i32 {
        match reason {
            DecisionReason::InterruptUrgency => 95,
            DecisionReason::DefensiveUrgency => 80,
            DecisionReason::SelfSustain => 75,
            DecisionReason::MainRotation => 70,
            DecisionReason::MobilityUplift => 65,
            DecisionReason::OffensiveCooldown => 60,
            DecisionReason::Consumable => 55,
            DecisionReason::Trinket => 50,
            _ => 50,
        }
    }
}

// ---------------------------------------------------------------------------
// Action scheduler
// ---------------------------------------------------------------------------

/// Input for one scheduling tick. Borrowed policy/knowledge seams keep the
/// scheduler free of any knowledge-crate dependency.
pub struct ScheduleInput<'a> {
    pub frame: Option<&'a FrameSnapshot>,
    pub candidates: Vec<ActionCandidate>,
    pub now_ms: Ms,
    pub min_key_interval_ms: i32,
    pub stale_after_ms: i32,
    pub heartbeat_timeout_ms: i32,
    pub repeat_suppress_ms: i32,
    pub out_of_combat_permitted: bool,
    pub cast: CastState,
    pub policy: Option<&'a dyn PolicyEvaluator>,
    pub knowledge: &'a dyn Knowledge,
}

#[derive(Clone, Copy, Debug)]
struct PendingConfirm {
    slot: Slot,
    stroke: KeyStroke,
    spell_id: i32,
    sent_at: Ms,
    saw_gcd: bool,
    rides_gcd: bool,
}

#[derive(Clone, Copy, Debug)]
struct AttemptRecord {
    slot: Slot,
    stroke: KeyStroke,
    at_ms: Ms,
    outcome: AttemptOutcome,
}

/// Deterministic action scheduler: link -> pause -> target -> policy/execution
/// safety -> rank -> duplicate collapse -> stale demotion -> GCD -> min key
/// interval -> retry/backoff. Pure state machine over plain values.
#[derive(Clone, Debug)]
pub struct ActionScheduler {
    heartbeat_seen: bool,
    last_heartbeat: i32,
    heartbeat_changed_at: Ms,

    has_sent: bool,
    last_sent_at: Ms,
    last_sent_slot: Option<Slot>,
    last_sent_stroke: KeyStroke,
    #[allow(dead_code)]
    last_sent_spell: i32,

    #[allow(dead_code)]
    has_attempt: bool,
    last_attempt: Option<AttemptRecord>,

    blocked_until: BTreeMap<(Slot, KeyStroke, i32), Ms>,
    failed_until: BTreeMap<(Slot, KeyStroke, i32), Ms>,
    failure_streak: BTreeMap<(Slot, KeyStroke, i32), i32>,
    last_failure_at: BTreeMap<(Slot, KeyStroke, i32), Ms>,
    attempts: BTreeMap<(Slot, KeyStroke, i32), (u32, Ms)>,

    last_main_spell: i32,
    pending_confirm: Option<PendingConfirm>,
    #[allow(dead_code)]
    last_self_heal_tried: Ms,
    rejections_detected: u32,

    has_frame: bool,
    last_state: BridgeState,
    last_has_target: bool,
    last_in_combat: bool,
    last_on_gcd: bool,
}

/// Time a candidate rejected by an OS gate stays suppressed.
pub const UNAVAILABLE_SUPPRESS_MS: Ms = 500;
/// Time after a press the GCD must appear before it is a failed action.
pub const REJECT_DETECT_MS: Ms = 600;
/// Base suppression window for a failed stroke.
pub const REJECTED_SUPPRESS_MS: Ms = 1500;
/// Cap for the escalating failure backoff.
pub const MAX_FAILED_SUPPRESS_MS: Ms = 10_000;
/// Hard cap for the Main-slot failure backoff.
pub const MAX_MAIN_SUPPRESS_MS: Ms = 3000;
/// Silence that restarts the escalation ladder.
pub const FAILURE_DECAY_MS: Ms = 6000;
/// Sliding window for the send-count backoff.
pub const ATTEMPT_WINDOW_MS: Ms = 1500;
/// More sends of the same stroke inside the window than this is spam.
pub const MAX_ATTEMPTS_PER_WINDOW: u32 = 5;
/// Fast re-arm floor for the Main slot.
pub const MAIN_REPROBE_MS: Ms = 150;
/// Consecutive no-op Main sends allowed before backoff.
pub const MAIN_SAME_SPELL_NO_OP_CAP: u32 = 3;

impl Default for ActionScheduler {
    fn default() -> Self {
        Self::new()
    }
}

impl ActionScheduler {
    pub fn new() -> Self {
        Self {
            heartbeat_seen: false,
            last_heartbeat: 0,
            heartbeat_changed_at: 0,
            has_sent: false,
            last_sent_at: 0,
            last_sent_slot: None,
            last_sent_stroke: KeyStroke::new(0),
            last_sent_spell: 0,
            has_attempt: false,
            last_attempt: None,
            blocked_until: BTreeMap::new(),
            failed_until: BTreeMap::new(),
            failure_streak: BTreeMap::new(),
            last_failure_at: BTreeMap::new(),
            attempts: BTreeMap::new(),
            last_main_spell: 0,
            pending_confirm: None,
            last_self_heal_tried: 0,
            rejections_detected: 0,
            has_frame: false,
            last_state: BridgeState::Idle,
            last_has_target: false,
            last_in_combat: false,
            last_on_gcd: false,
        }
    }

    pub fn rejections_detected(&self) -> u32 {
        self.rejections_detected
    }

    pub fn last_attempt(&self) -> Option<(Slot, KeyStroke, Ms, AttemptOutcome)> {
        self.last_attempt.map(|a| (a.slot, a.stroke, a.at_ms, a.outcome))
    }

    pub fn last_sent(&self) -> Option<(Slot, KeyStroke, Ms)> {
        self.last_sent_slot.map(|s| (s, self.last_sent_stroke, self.last_sent_at))
    }

    pub fn reset(&mut self) {
        *self = Self::new();
    }

    /// Records a decoded frame's heartbeat (called for every frame).
    pub fn observe(&mut self, frame: &FrameSnapshot, now_ms: Ms) {
        if !self.heartbeat_seen || frame.heartbeat != self.last_heartbeat {
            self.heartbeat_seen = true;
            self.last_heartbeat = frame.heartbeat;
            self.heartbeat_changed_at = now_ms;
        }
    }

    /// True when the addon heartbeat has been frozen past the timeout.
    pub fn is_link_lost(&self, now_ms: Ms, heartbeat_timeout_ms: i32) -> bool {
        self.heartbeat_seen
            && now_ms - self.heartbeat_changed_at >= Ms::from(heartbeat_timeout_ms.max(1))
    }

    /// Records a successful send.
    pub fn note_sent(&mut self, now_ms: Ms, slot: Slot, stroke: KeyStroke, spell_id: i32) {
        self.has_sent = true;
        self.last_sent_at = now_ms;
        self.last_sent_slot = Some(slot);
        self.last_sent_stroke = stroke;
        self.last_sent_spell = spell_id;

        let key = (slot, stroke, spell_id);
        self.failed_until.remove(&key);
        if slot == Slot::Main {
            remove_main_entries(&mut self.failure_streak);
        } else {
            self.failure_streak.remove(&key);
        }
        if slot == Slot::Main && self.pending_confirm.map(|p| p.saw_gcd).unwrap_or(false) {
            self.attempts.remove(&key);
        } else if let Some(&(count, start)) = self.attempts.get(&key) {
            if now_ms - start <= ATTEMPT_WINDOW_MS {
                self.attempts.insert(key, (count + 1, start));
            } else {
                self.attempts.insert(key, (1, now_ms));
            }
        } else {
            self.attempts.insert(key, (1, now_ms));
        }

        self.pending_confirm = Some(PendingConfirm {
            slot,
            stroke,
            spell_id,
            sent_at: now_ms,
            saw_gcd: false,
            rides_gcd: self.rides_gcd_heuristic(slot, spell_id),
        });
        if slot == Slot::SelfHeal {
            self.last_self_heal_tried = now_ms;
        }
    }

    /// Records an OS-gate rejection so lower ranks get a turn.
    pub fn note_attempt(
        &mut self,
        now_ms: Ms,
        slot: Slot,
        stroke: KeyStroke,
        outcome: AttemptOutcome,
        spell_id: i32,
    ) {
        self.has_attempt = true;
        self.last_attempt = Some(AttemptRecord { slot, stroke, at_ms: now_ms, outcome });
        if slot == Slot::SelfHeal {
            self.last_self_heal_tried = now_ms;
        }
        if outcome != AttemptOutcome::Sent {
            let ms = if slot == Slot::Main { MAIN_REPROBE_MS } else { UNAVAILABLE_SUPPRESS_MS };
            self.blocked_until.insert((slot, stroke, spell_id), now_ms + ms);
        }
    }

    fn rides_gcd_heuristic(&self, slot: Slot, _spell_id: i32) -> bool {
        matches!(slot, Slot::Main | Slot::Offensive | Slot::Mobility)
    }

    /// Records a failed press with escalating suppression.
    pub fn note_failure(&mut self, slot: Slot, stroke: KeyStroke, now_ms: Ms, spell_id: i32) {
        let key = (slot, stroke, spell_id);
        if slot == Slot::SelfHeal {
            self.failed_until.insert(key, now_ms + REJECTED_SUPPRESS_MS);
            self.failure_streak.remove(&key);
            self.last_failure_at.remove(&key);
            return;
        }
        let last = self.last_failure_at.get(&key).copied().unwrap_or(Ms::MIN);
        let mut streak = if now_ms - last > FAILURE_DECAY_MS {
            0
        } else {
            self.failure_streak.get(&key).copied().unwrap_or(0)
        };
        self.last_failure_at.insert(key, now_ms);
        streak += 1;
        self.failure_streak.insert(key, streak);
        if slot == Slot::Main {
            return;
        }
        let shift = ((streak - 1).clamp(0, 3)) as u32;
        let window = REJECTED_SUPPRESS_MS
            .saturating_mul(1 << shift)
            .min(MAX_FAILED_SUPPRESS_MS);
        self.failed_until.insert(key, now_ms + window);
    }

    fn resolve_pending_confirm(&mut self, input: &ScheduleInput, frame: &FrameSnapshot) {
        let mut pending = match self.pending_confirm {
            Some(p) => p,
            None => return,
        };
        if frame.on_gcd {
            pending.saw_gcd = true;
            self.pending_confirm = Some(pending);
            return;
        }
        let mut found = false;
        for c in &input.candidates {
            if c.slot == pending.slot && c.spell_id == pending.spell_id {
                found = true;
                if c.stroke != pending.stroke {
                    self.pending_confirm = None;
                }
                break;
            }
        }
        if !found {
            self.pending_confirm = None;
        }
        let still = match self.pending_confirm {
            Some(p) => p,
            None => return,
        };
        let has_gcd_info = frame.version != 1;
        if !has_gcd_info || !still.rides_gcd {
            self.pending_confirm = None;
            return;
        }
        if still.saw_gcd {
            if input.now_ms - still.sent_at >= REJECT_DETECT_MS {
                self.pending_confirm = None;
            }
            return;
        }
        if input.now_ms - still.sent_at >= REJECT_DETECT_MS {
            self.note_failure(still.slot, still.stroke, input.now_ms, still.spell_id);
            self.pending_confirm = None;
            self.rejections_detected += 1;
        }
    }

    fn prune_attempts(&mut self, now_ms: Ms) {
        self.attempts.retain(|_, &mut (_, start)| now_ms - start <= ATTEMPT_WINDOW_MS);
    }

    fn rank_for(slot: Slot, policy: Option<&PolicyDecision>) -> i32 {
        if policy.map(|p| p.emergency).unwrap_or(false) {
            return 1;
        }
        match slot {
            Slot::Interrupt => 0,
            Slot::Defensive => 2,
            Slot::SelfHeal => 3,
            Slot::Main => 4,
            Slot::Mobility => 5,
            Slot::Offensive => 6,
            Slot::Consumable => 7,
            Slot::Trinket => 8,
        }
    }

    fn bypasses_gcd(
        slot: Slot,
        policy: Option<&PolicyDecision>,
        spell_id: i32,
        knowledge: &dyn Knowledge,
    ) -> bool {
        slot == Slot::Interrupt
            || policy.map(|p| p.emergency).unwrap_or(false)
            || knowledge.off_gcd(spell_id)
    }

    fn min_interval_applies(&self, slot: Slot, cand: &ActionCandidate, interval_elapsed: bool) -> bool {
        if interval_elapsed {
            return false;
        }
        // Interrupt and Main bypass the interval for any DIFFERENT press; only
        // an identical repeat inside the interval is held.
        if matches!(slot, Slot::Interrupt | Slot::Main) {
            let same_slot = self.last_sent_slot == Some(slot);
            return same_slot && self.last_sent_stroke == cand.stroke;
        }
        true
    }

    fn note_hold(slot: &mut Option<ScheduleReason>, reason: ScheduleReason) {
        if slot.is_none() {
            *slot = Some(reason);
        }
    }

    fn reason_for(_slot: Slot, _policy: Option<&PolicyDecision>) -> ScheduleReason {
        ScheduleReason::Scheduled
    }

    fn confidence_for(_reason: ScheduleReason) -> i32 {
        70
    }

    /// Deterministic scheduling decision for one tick.
    pub fn advance(&mut self, input: &ScheduleInput) -> SchedulePlan {
        // 1. Link / protocol: a stale or unknown strip must never fire.
        let frame = match input.frame {
            Some(f) => f,
            None => return SchedulePlan::hold(ScheduleReason::StaleFrame),
        };
        self.observe(frame, input.now_ms);
        if self.is_link_lost(input.now_ms, input.heartbeat_timeout_ms) {
            self.pending_confirm = None;
            return SchedulePlan::hold(ScheduleReason::LinkLost);
        }
        if !frame.known_protocol() {
            return SchedulePlan::hold(ScheduleReason::ProtocolMismatch);
        }

        // 2. Failure recovery.
        self.resolve_pending_confirm(input, frame);
        self.prune_attempts(input.now_ms);

        // 3. State transitions clear local suppression.
        if self.has_frame {
            let target_changed = frame.has_target != self.last_has_target;
            let combat_changed = frame.in_combat != self.last_in_combat;
            let gcd_released = self.last_on_gcd && !frame.on_gcd;
            if frame.state != self.last_state || target_changed || combat_changed || gcd_released {
                self.blocked_until.clear();
            }
            let current_main = current_main_spell(input);
            let main_changed =
                current_main != 0 && self.last_main_spell != 0 && current_main != self.last_main_spell;
            if target_changed || combat_changed {
                self.attempts.clear();
            }
            if target_changed || combat_changed || main_changed {
                remove_main_entries(&mut self.failed_until);
                remove_main_entries(&mut self.failure_streak);
                remove_main_entries(&mut self.last_failure_at);
            }
        }
        self.has_frame = true;
        self.last_state = frame.state;
        self.last_has_target = frame.has_target;
        self.last_in_combat = frame.in_combat;
        self.last_on_gcd = frame.on_gcd;

        // 4. Hard state gates.
        if frame.state == BridgeState::Paused {
            return SchedulePlan::hold(ScheduleReason::Paused);
        }
        if !input.out_of_combat_permitted {
            return SchedulePlan::hold(ScheduleReason::OutOfCombat);
        }
        if !frame.has_target && frame.version != 1 {
            return SchedulePlan::hold(ScheduleReason::NoTarget);
        }

        // 5. Normalize enabled candidates per slot.
        let mut by_slot: [Vec<ActionCandidate>; SLOT_COUNT] = Default::default();
        for c in &input.candidates {
            if c.enabled {
                by_slot[c.slot.index()].push(*c);
            }
        }

        // 6. Policy / execution safety.
        let policy_on = input.policy.is_some();
        let mut policy_held = 0u32;
        let mut policy_skipped = 0u32;
        let mut cast_held = 0u32;
        let mut channel_held = 0u32;
        let mut detail: Option<String> = None;
        let mut ranked: Vec<(ActionCandidate, Option<PolicyDecision>)> = Vec::new();

        for (i, candidates) in by_slot.iter().enumerate() {
            if candidates.is_empty() {
                continue;
            }
            let slot = Slot::from_index(i).expect("slot index in range");
            if !policy_on && matches!(slot, Slot::Mobility | Slot::SelfHeal) {
                continue;
            }
            for candidate in candidates {
                let candidate = *candidate;
                let mut decision: Option<PolicyDecision> = None;
                if let Some(policy) = input.policy {
                    let d = policy.evaluate(&PolicyInput {
                        slot,
                        spell_id: candidate.spell_id,
                        in_combat: frame.in_combat,
                        has_target: frame.has_target,
                        now_ms: input.now_ms,
                    });
                    if d.verdict != PolicyVerdict::Use {
                        match d.verdict {
                            PolicyVerdict::Hold | PolicyVerdict::Unknown => {
                                policy_held += 1;
                                if d.reason == CHANNEL_REASON {
                                    channel_held += 1;
                                } else if d.reason == CAST_REASON {
                                    cast_held += 1;
                                }
                            }
                            _ => policy_skipped += 1,
                        }
                        if detail.is_none() {
                            detail = Some(d.reason.clone());
                        }
                        continue;
                    }
                    decision = Some(d);
                } else if let Some(reason) =
                    execution_hold(slot, candidate.spell_id, input.cast, input.knowledge)
                {
                    if reason == CHANNEL_REASON {
                        channel_held += 1;
                    } else {
                        cast_held += 1;
                    }
                    if detail.is_none() {
                        detail = Some(reason.to_string());
                    }
                    continue;
                }
                ranked.push((candidate, decision));
            }
        }

        if ranked.is_empty() {
            if policy_held > 0 || policy_skipped > 0 || cast_held > 0 || channel_held > 0 {
                let reason = if cast_held > 0 {
                    ScheduleReason::CastHold
                } else if channel_held > 0 {
                    ScheduleReason::ChannelHold
                } else if policy_held > 0 {
                    ScheduleReason::PolicyHold
                } else {
                    ScheduleReason::PolicySkip
                };
                let mut plan = SchedulePlan::hold(reason);
                plan.policy_held = policy_held;
                plan.policy_skipped = policy_skipped;
                plan.detail = detail;
                return plan;
            }
            return SchedulePlan::hold(ScheduleReason::NoCandidate);
        }

        // Stable rank order.
        ranked.sort_by_key(|(c, p)| Self::rank_for(c.slot, p.as_ref()));

        // 7. Duplicate collapse: one stroke, one press.
        let mut unique: Vec<(ActionCandidate, Option<PolicyDecision>)> = Vec::new();
        let mut collapsed_mains: Vec<(ActionCandidate, Option<PolicyDecision>)> = Vec::new();
        for entry in ranked {
            if unique.iter().any(|k| k.0.stroke == entry.0.stroke) {
                if entry.0.slot == Slot::Main {
                    collapsed_mains.push(entry);
                }
            } else {
                unique.push(entry);
            }
        }

        // 8. Stale demotion (never for SelfHeal / Main).
        let mut fresh = Vec::new();
        let mut stale = Vec::new();
        for entry in unique {
            if !matches!(entry.0.slot, Slot::SelfHeal | Slot::Main)
                && entry.0.is_stale(input.now_ms, input.stale_after_ms)
            {
                stale.push(entry);
            } else {
                fresh.push(entry);
            }
        }
        let demoted = !stale.is_empty() && !fresh.is_empty();
        let mut final_list = fresh;
        final_list.extend(stale);

        // 8b. Pending-confirm demotion (situational only).
        if let Some(pending) = self.pending_confirm {
            if pending.slot != Slot::Main
                && pending.slot != Slot::SelfHeal
                && input.now_ms - pending.sent_at < REJECT_DETECT_MS
            {
                let mut ahead = Vec::new();
                let mut behind = Vec::new();
                for entry in final_list.drain(..) {
                    if entry.0.slot == pending.slot && entry.0.stroke == pending.stroke {
                        behind.push(entry);
                    } else {
                        ahead.push(entry);
                    }
                }
                if !ahead.is_empty() {
                    final_list.extend(ahead);
                }
                final_list.extend(behind);
            }
        }
        final_list.extend(collapsed_mains);

        // 9. Timing gates.
        let gcd_unknown = !matches!(frame.version, 4..=6);
        let min_interval = Ms::from(input.min_key_interval_ms.max(1));
        let repeat_window = Ms::from(input.repeat_suppress_ms.max(1));
        let interval_elapsed = !self.has_sent || input.now_ms - self.last_sent_at >= min_interval;

        let mut actions: Vec<ScheduledAction> = Vec::new();
        let mut emitted: BTreeSet<KeyStroke> = BTreeSet::new();
        let mut suppressed = 0u32;
        let mut first_hold: Option<ScheduleReason> = None;

        for (candidate, policy) in &final_list {
            if emitted.contains(&candidate.stroke) {
                continue;
            }
            if frame.on_gcd
                && !Self::bypasses_gcd(candidate.slot, policy.as_ref(), candidate.spell_id, input.knowledge)
            {
                Self::note_hold(&mut first_hold, ScheduleReason::GcdHold);
                continue;
            }
            let send_cap = if candidate.slot == Slot::Main {
                MAIN_SAME_SPELL_NO_OP_CAP
            } else {
                MAX_ATTEMPTS_PER_WINDOW
            };
            let key = (candidate.slot, candidate.stroke, candidate.spell_id);
            if let Some(&(count, start)) = self.attempts.get(&key) {
                if count >= send_cap && input.now_ms - start <= ATTEMPT_WINDOW_MS {
                    self.attempts.remove(&key);
                    if candidate.slot == Slot::Main {
                        self.failed_until.insert(key, input.now_ms + MAIN_REPROBE_MS);
                    } else {
                        self.note_failure(candidate.slot, candidate.stroke, input.now_ms, candidate.spell_id);
                    }
                    suppressed += 1;
                    Self::note_hold(&mut first_hold, ScheduleReason::RetryBackoff);
                    continue;
                }
            }
            if self.min_interval_applies(candidate.slot, candidate, interval_elapsed) {
                Self::note_hold(&mut first_hold, ScheduleReason::MinInterval);
                continue;
            }
            let blocked = self
                .blocked_until
                .get(&key)
                .map(|&until| input.now_ms < until)
                .unwrap_or(false)
                || self
                    .failed_until
                    .get(&key)
                    .map(|&until| input.now_ms < until)
                    .unwrap_or(false);
            if blocked {
                suppressed += 1;
                Self::note_hold(&mut first_hold, ScheduleReason::Unavailable);
                continue;
            }
            if gcd_unknown
                && self.has_sent
                && self.last_sent_slot == Some(candidate.slot)
                && self.last_sent_stroke == candidate.stroke
                && input.now_ms - self.last_sent_at < repeat_window
            {
                suppressed += 1;
                Self::note_hold(&mut first_hold, ScheduleReason::RepeatSuppressed);
                continue;
            }

            actions.push(ScheduledAction {
                slot: candidate.slot,
                stroke: candidate.stroke,
                spell_id: candidate.spell_id,
                reason: Self::reason_for(candidate.slot, policy.as_ref()),
                interrupt: candidate.slot == Slot::Interrupt,
                provider: policy.as_ref().map(|p| p.provider.clone()).unwrap_or_default(),
                evidence: policy.as_ref().map(|p| p.evidence.clone()).unwrap_or_default(),
            });
            emitted.insert(candidate.stroke);
        }

        // Remember the Main identity that won this tick.
        for action in &actions {
            if action.slot == Slot::Main {
                self.last_main_spell = action.spell_id;
                break;
            }
        }

        if actions.is_empty() {
            let mut plan = SchedulePlan::hold(first_hold.unwrap_or(ScheduleReason::NoCandidate));
            plan.suppressed = suppressed;
            plan.policy_held = policy_held;
            plan.policy_skipped = policy_skipped;
            plan.detail = detail;
            return plan;
        }

        // 10. Head metadata.
        let head = &actions[0];
        let head_candidate = by_slot[head.slot.index()]
            .iter()
            .find(|c| c.stroke == head.stroke && c.spell_id == head.spell_id)
            .copied();
        let mut confidence = Self::confidence_for(head.reason);
        if let Some(hc) = head_candidate {
            if !hc.enabled {
                confidence -= 20;
            }
            if hc.is_stale(input.now_ms, input.stale_after_ms) {
                confidence -= 40;
            }
        }
        SchedulePlan {
            head: Some(head.slot),
            reason: head.reason,
            confidence: confidence.clamp(0, 100),
            demoted,
            suppressed,
            policy_held,
            policy_skipped,
            detail,
            actions,
        }
    }
}

fn execution_hold(
    slot: Slot,
    spell_id: i32,
    cast: CastState,
    knowledge: &dyn Knowledge,
) -> Option<&'static str> {
    match cast {
        CastState::None => None,
        CastState::Casting => {
            if slot == Slot::Interrupt || knowledge.off_gcd(spell_id) {
                None
            } else {
                Some(CAST_REASON)
            }
        }
        CastState::Channeling => {
            if slot == Slot::Interrupt || knowledge.off_gcd(spell_id) {
                None
            } else {
                Some(CHANNEL_REASON)
            }
        }
    }
}

fn current_main_spell(input: &ScheduleInput) -> i32 {
    for c in &input.candidates {
        if c.slot == Slot::Main && c.enabled {
            return c.spell_id;
        }
    }
    0
}

fn remove_main_entries<V>(map: &mut BTreeMap<(Slot, KeyStroke, i32), V>) {
    let dead: Vec<(Slot, KeyStroke, i32)> =
        map.keys().filter(|k| k.0 == Slot::Main).copied().collect();
    for key in dead {
        map.remove(&key);
    }
}

// ---------------------------------------------------------------------------
// Rotation engine (orchestrator with injected clock)
// ---------------------------------------------------------------------------

/// Engine configuration derived from `settings.ini`.
#[derive(Clone, Debug)]
pub struct EngineConfig {
    pub scheduler_enabled: bool,
    pub intelligence_enabled: bool,
    pub slot_enabled: [bool; SLOT_COUNT],
    pub min_key_interval_ms: i32,
    pub stale_after_ms: i32,
    pub heartbeat_timeout_ms: i32,
    pub repeat_suppress_ms: i32,
    pub combat_only: bool,
}

impl Default for EngineConfig {
    fn default() -> Self {
        Self {
            scheduler_enabled: true,
            intelligence_enabled: false,
            slot_enabled: [true; SLOT_COUNT],
            min_key_interval_ms: 1500,
            stale_after_ms: 1500,
            heartbeat_timeout_ms: 1500,
            repeat_suppress_ms: 1500,
            combat_only: true,
        }
    }
}

/// Orchestrates the candidate tracker, scheduler and decision layer over an
/// injected [`Clock`]. Contains no I/O and no platform code.
#[derive(Clone, Debug)]
pub struct RotationEngine<C: Clock> {
    clock: C,
    config: EngineConfig,
    tracker: CandidateTracker,
    scheduler: ActionScheduler,
    last_plan: SchedulePlan,
}

impl<C: Clock> RotationEngine<C> {
    pub fn new(clock: C, config: EngineConfig) -> Self {
        Self {
            clock,
            config,
            tracker: CandidateTracker::default(),
            scheduler: ActionScheduler::new(),
            last_plan: SchedulePlan::hold(ScheduleReason::NoCandidate),
        }
    }

    pub fn clock(&self) -> &C {
        &self.clock
    }

    pub fn config(&self) -> &EngineConfig {
        &self.config
    }

    pub fn tracker(&self) -> &CandidateTracker {
        &self.tracker
    }

    pub fn scheduler(&self) -> &ActionScheduler {
        &self.scheduler
    }

    pub fn last_plan(&self) -> &SchedulePlan {
        &self.last_plan
    }

    pub fn reset(&mut self) {
        self.tracker.reset();
        self.scheduler.reset();
        self.last_plan = SchedulePlan::hold(ScheduleReason::NoCandidate);
    }

    /// Evaluates the legacy decision order over the current suggestions.
    pub fn decision(&self, on_gcd: bool) -> DecisionResult {
        let now = self.clock.now_ms();
        let candidates = self
            .tracker
            .snapshot(&self.config.slot_enabled, now, self.tracker.ttl_ms());
        DecisionEngine::evaluate(&DecisionContext {
            candidates,
            now_ms: now,
            stale_after_ms: self.config.stale_after_ms,
            on_gcd,
        })
    }

    /// One deterministic engine tick.
    pub fn tick(
        &mut self,
        frame: Option<&FrameSnapshot>,
        policy: Option<&dyn PolicyEvaluator>,
        knowledge: &dyn Knowledge,
        cast: CastState,
        out_of_combat_permitted: bool,
    ) -> &SchedulePlan {
        let now = self.clock.now_ms();
        if let Some(f) = frame {
            self.tracker.update(f, now);
        }
        let candidates = self
            .tracker
            .snapshot(&self.config.slot_enabled, now, self.tracker.ttl_ms());
        let input = ScheduleInput {
            frame,
            candidates,
            now_ms: now,
            min_key_interval_ms: self.config.min_key_interval_ms,
            stale_after_ms: self.config.stale_after_ms,
            heartbeat_timeout_ms: self.config.heartbeat_timeout_ms,
            repeat_suppress_ms: self.config.repeat_suppress_ms,
            out_of_combat_permitted,
            cast,
            policy: if self.config.intelligence_enabled { policy } else { None },
            knowledge,
        };
        self.last_plan = self.scheduler.advance(&input);
        &self.last_plan
    }

    /// Records a successful send back into the tracker + scheduler.
    pub fn note_sent(&mut self, slot: Slot, stroke: KeyStroke, spell_id: i32) {
        let now = self.clock.now_ms();
        self.tracker.note_pressed(slot, now, spell_id);
        self.scheduler.note_sent(now, slot, stroke, spell_id);
    }
}

// ---------------------------------------------------------------------------
// Tests (determinism + contract pins)
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    fn frame(vk: u16) -> FrameSnapshot {
        let mut f = FrameSnapshot {
            version: 5,
            heartbeat: 1,
            state: BridgeState::Active,
            has_target: true,
            in_combat: true,
            on_gcd: false,
            ..Default::default()
        };
        f.slots[Slot::Main.index()] = Some(KeyStroke::new(vk));
        f.spell_ids[Slot::Main.index()] = 100;
        f
    }

    #[test]
    fn tracker_marks_stale_after_press() {
        let mut t = CandidateTracker::new(4000);
        let f = frame(0x52);
        t.update(&f, 0);
        t.note_pressed(Slot::Main, 0, 100);
        assert!(!t.snapshot(&[true; SLOT_COUNT], 0, 4000)[0].is_stale(0, 1000));
        let snap = t.snapshot(&[true; SLOT_COUNT], 1000, 4000);
        assert!(snap[0].is_stale(1000, 1000));
    }

    #[test]
    fn fallback_order_is_pinned() {
        assert_eq!(
            FALLBACK_ORDER,
            [Slot::Main, Slot::Offensive, Slot::Interrupt, Slot::Defensive, Slot::Consumable, Slot::Trinket]
        );
    }

    #[test]
    fn link_loss_holds_everything() {
        let mut s = ActionScheduler::new();
        let f = frame(0x52);
        let input = ScheduleInput {
            frame: Some(&f),
            candidates: vec![],
            now_ms: 0,
            min_key_interval_ms: 1500,
            stale_after_ms: 1500,
            heartbeat_timeout_ms: 1000,
            repeat_suppress_ms: 1500,
            out_of_combat_permitted: true,
            cast: CastState::None,
            policy: None,
            knowledge: &DefaultKnowledge,
        };
        assert_eq!(s.advance(&input).reason, ScheduleReason::NoCandidate);
        let later = ScheduleInput { now_ms: 1001, ..input };
        assert_eq!(s.advance(&later).reason, ScheduleReason::LinkLost);
    }

    #[test]
    fn scheduler_emits_main_when_available() {
        let mut s = ActionScheduler::new();
        let f = frame(0x52);
        let cand = ActionCandidate {
            slot: Slot::Main,
            stroke: KeyStroke::new(0x52),
            spell_id: 100,
            enabled: true,
            first_seen_ms: 0,
            last_changed_ms: 0,
            pressed_since_change: false,
        };
        let input = ScheduleInput {
            frame: Some(&f),
            candidates: vec![cand],
            now_ms: 0,
            min_key_interval_ms: 1500,
            stale_after_ms: 1500,
            heartbeat_timeout_ms: 5000,
            repeat_suppress_ms: 1500,
            out_of_combat_permitted: true,
            cast: CastState::None,
            policy: None,
            knowledge: &DefaultKnowledge,
        };
        let plan = s.advance(&input);
        assert_eq!(plan.actions.len(), 1);
        assert_eq!(plan.actions[0].slot, Slot::Main);
        assert_eq!(plan.head, Some(Slot::Main));
    }

    #[test]
    fn cast_gate_holds_gcd_riders() {
        let mut s = ActionScheduler::new();
        let f = frame(0x52);
        let main = ActionCandidate {
            slot: Slot::Main,
            stroke: KeyStroke::new(0x52),
            spell_id: 100,
            enabled: true,
            first_seen_ms: 0,
            last_changed_ms: 0,
            pressed_since_change: false,
        };
        let input = ScheduleInput {
            frame: Some(&f),
            candidates: vec![main],
            now_ms: 0,
            min_key_interval_ms: 1500,
            stale_after_ms: 1500,
            heartbeat_timeout_ms: 5000,
            repeat_suppress_ms: 1500,
            out_of_combat_permitted: true,
            cast: CastState::Casting,
            policy: None,
            knowledge: &DefaultKnowledge,
        };
        assert_eq!(s.advance(&input).reason, ScheduleReason::CastHold);
    }

    #[test]
    fn engine_uses_injected_clock_deterministically() {
        let clock = ManualClock::new(0);
        let mut engine = RotationEngine::new(clock, EngineConfig::default());
        let f = frame(0x52);
        let plan = engine.tick(Some(&f), None, &DefaultKnowledge, CastState::None, true);
        assert_eq!(plan.actions.len(), 1);
        assert_eq!(engine.clock().now_ms(), 0);
    }
}
