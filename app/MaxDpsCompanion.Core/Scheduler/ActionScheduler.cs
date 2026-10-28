using MaxDpsCompanion.Core.Knowledge;
using MaxDpsCompanion.Core.Scheduler;

namespace MaxDpsCompanion;

/// <summary>
/// Deterministic action scheduler. A pure state machine over the decoded
/// MaxDps candidates, the ability knowledge base, the observed combat context
/// and the companion's own send/attempt history — no I/O, no randomness, no
/// clock reads (the caller passes NowMs), no game state beyond the pixel
/// protocol.
///
/// Pipeline per <see cref="Advance"/> call:
///   link/protocol -> pause -> target -> policy evaluation (USE/HOLD/SKIP) ->
///   rank (emergency survival first) -> duplicate collapse -> stale demotion ->
///   GCD -> min key interval -> repeat / unavailable / retry suppression.
///
/// Design rules (measured by ActionSchedulerTests + --bench-scheduler):
///  * A frozen addon heartbeat (&gt; HeartbeatTimeoutMs) is link loss: NOTHING
///    fires (a stale-but-decodable strip used to keep sending forever).
///  * Hard execution-safety gate (always, intelligence on or off): while the
///    player is casting or channeling, every candidate whose GCD is not
///    verified off-GCD is held (CastHold / ChannelHold) — including the main
///    rotation, because a channel is not ours to clip. Verified off-GCD tools
///    (interrupts, items, curated non-movement cooldowns) stay eligible.
///  * The situational policy (intelligence on) filters candidates using the
///    knowledge base; the scheduler only ever schedules USE verdicts. A policy
///    HOLD is re-evaluated next tick and never blocks the main rotation.
///  * Escape/repositioning utilities are never automatic; only target-reaching
///    gap closers with a confirmed out-of-melee target may fire.
///  * Interrupts are off the GCD and time-critical: they rank first, bypass
///    the GCD gate, and a CHANGED interrupt stroke bypasses the min key
///    interval too (an identical repeated kick stroke still waits it out).
///  * A press that never starts a GCD within RejectDetectMs is treated as a
///    failed action (out of range, immune, no resource): the stroke is
///    suppressed briefly instead of being hammered. The same stroke sent
///    MaxAttemptsPerWindow times inside AttemptWindowMs also backs off.
///  * A candidate rejected by an engine OS gate (movement bind, held key,
///    focus, lost window) is suppressed briefly so it cannot shadow lower
///    ranks or flap the status line every tick.
///  * State transitions (target regained, state change, GCD falling edge)
///    clear OS-gate suppression so a held candidate is not punished after the
///    state heals; FAILURE suppression (`_failedUntil`) deliberately survives
///    transitions for the situational slots — only a successful send resets
///    it. The MAIN slot is the exception: a new fight (a target or combat
///    edge) drops the Main failure memory so a stuck rotation suggestion
///    cannot carry its backoff into the next target. MinKeyInterval and link
///    state persist either way.
///
/// 4.6.0 "Cadence" (all four features default OFF, see
/// <see cref="CadenceOptions"/>; with them off every line below reproduces the
/// 4.5.8 behaviour byte-identically):
///  * interrupt INSURANCE: on a confirmed interruptible cast that the slot-6
///    kick cannot cover, the addon's own bound CC entry (Ext5) is appended to
///    the Interrupt slot - one more candidate on the EXISTING send gates, never
///    a second send path. The press is a GCD-riding CC in the general case, so
///    the interrupt exemption is per-SPELL: it bypasses the GCD only when the
///    registry marks the spell off-GCD. Its DR application, per-cast latch and
///    timing record are committed only when the stroke really reaches the plan.
///  * LANDING: the GCD-riding head is pre-fired `leadMs` before the PREDICTED
///    GCD end (Ext5 cell 53 or the observed-edge estimator) so the press is
///    queued by the client instead of waiting for the strip's flag to fall.
///    One pre-fire per GCD, aborted on any identity/verdict change, and the
///    Fired latch + latency sample are committed only at emission.
///  * KICK MEMORY: a rejected WIRE KICK stroke marks the kick failed for the
///    first suppression rung (<see cref="KickMemory"/>, instance state owned by
///    this scheduler) so insurance can cover the next cast; a CC id never feeds
///    it. Cleared on a target change, out of combat or a confirmed GCD.
///  * OPENER RELEASE: on a BOSS-GRADE pull one ready Offensive major is
///    pre-ranked ahead of the first Main for the engine's 2500 ms window.
///
/// The engine's existing per-slot send gates stay authoritative; the plan is
/// only the order and the pacing decision.
/// </summary>
internal sealed class ActionScheduler
{
    /// <summary>How long a candidate rejected by an OS gate stays suppressed.</summary>
    internal const int UnavailableSuppressMs = 500;

    /// <summary>How long after a press the GCD must appear before it is a failed action.</summary>
    internal const int RejectDetectMs = 600;

    /// <summary>How long a stroke that failed (no GCD / repeated) stays suppressed.</summary>
    internal const int RejectedSuppressMs = 1500;

    /// <summary>Cap for the escalating failure backoff (consecutive windows).</summary>
    internal const int MaxFailedSuppressMs = 10_000;

    /// <summary>
    /// Hard cap for the Main-slot failure backoff (Arms stuck fix): the main
    /// rotation may never be silenced for the full 10 s ladder — a key that
    /// keeps failing is re-armed after at most 3 s (1.5/3/3/3).
    /// </summary>
    internal const int MaxMainSuppressMs = 3000;

    /// <summary>
    /// Silence since the last failure that restarts the escalation ladder: a
    /// new failure after this quiet gap retries at the base window instead of
    /// inheriting a stale streak.
    /// </summary>
    internal const int FailureDecayMs = 6000;

    /// <summary>Sliding window for the send-count backoff.</summary>
    internal const int AttemptWindowMs = 1500;

    /// <summary>More sends of the same stroke inside the window than this is spam.</summary>
    internal const int MaxAttemptsPerWindow = 5;

    /// <summary>
    /// T2 (no-downtime Main): a Main press that keeps failing is re-armed at
    /// this fast floor instead of the escalating 1.5 s/3 s ladder, so the
    /// rotation is never stranded on a silent Main slot.
    /// </summary>
    internal const int MainReprobeMs = 150;

    /// <summary>
    /// T2 (no-downtime Main): consecutive no-op sends of the SAME Main spell
    /// allowed before the RC6 send-count backoff treats it as stuck. Bounds the
    /// fast <see cref="MainReprobeMs"/> re-probe.
    /// </summary>
    internal const int MainSameSpellNoOpCap = 3;

    /// <summary>
    /// Fallback scheduler rank order (policy off or unknown): interrupt,
    /// defensive, main, then the optional cooldowns, items and companion-only
    /// slots. Consumers must not reorder: tests pin this contract.
    /// </summary>
    internal static readonly Slot[] Order =
        [Slot.Interrupt, Slot.Defensive, Slot.Main, Slot.Offensive, Slot.Consumable, Slot.Trinket];

    // Link monitor: the addon increments heartbeat every frame; a value frozen
    // past the timeout means the strip stopped rendering (link lost).
    private bool _heartbeatSeen;
    private int _lastHeartbeat;
    private long _heartbeatChangedAt;

    // Last successful send (the pacing clock + v1 repeat-suppression key).
    private bool _hasSent;
    private long _lastSentAt;
    private Slot _lastSentSlot;
    private KeyStroke _lastSentStroke;
    private int _lastSentSpellId;

    // Diagnostics only: the most recent attempted (not necessarily sent) action.
    private bool _hasAttempt;
    private Slot _lastAttemptSlot;
    private KeyStroke _lastAttemptStroke;
    private long _lastAttemptAt;
    private AttemptOutcome _lastAttemptOutcome;

    // (slot, stroke, spellId) -> suppress-until for engine OS-gate rejections.
    // Cleared on a target/combat/state transition: the condition (range,
    // target, GCD) may have healed.
    private readonly Dictionary<(Slot Slot, KeyStroke Stroke, int SpellId), long> _blockedUntil = new();

    // (slot, stroke, spellId) -> suppress-until for FAILED presses (no GCD after
    // a press, or repeated re-sends). Deliberately NOT cleared by state
    // transitions: a stroke that keeps failing must not be re-armed by every
    // GCD pulse.
    private readonly Dictionary<(Slot Slot, KeyStroke Stroke, int SpellId), long> _failedUntil = new();

    // Consecutive failure windows per (slot, stroke, spellId) (escalating
    // backoff: a permanent failure converges to a low retry rate instead of a
    // fixed 1.5 s loop).
    private readonly Dictionary<(Slot Slot, KeyStroke Stroke, int SpellId), int> _failureStreak = new();

    // Last failure time per (slot, stroke, spellId): a silence longer than
    // FailureDecayMs restarts the ladder (a stale streak must not push a new,
    // unrelated failure straight to the cap).
    private readonly Dictionary<(Slot Slot, KeyStroke Stroke, int SpellId), long> _lastFailureAt = new();

    // (slot, stroke, spellId) -> send count inside the rolling window
    // (retry/backoff). Keying on the spell identity (not just the physical
    // stroke) means a sibling rotation candidate that shares a key does not
    // inherit the failed suggestion's suppression (RC6).
    private readonly Dictionary<(Slot Slot, KeyStroke Stroke, int SpellId), (int Count, long WindowStartMs)> _attempts = new();

    // T2 (no-downtime Main): the Main spell identity that last won a tick. A
    // change means the rotation genuinely moved on, so the superseded
    // identity's failure memory is dropped rather than carried forward.
    private int _lastMainSpellId;

    // Post-send GCD confirmation: a press that never starts the GCD failed.
    private (Slot Slot, KeyStroke Stroke, int SpellId, long SentAt, bool SawGcd, bool RidesGcd)? _pendingConfirm;

    // The policy memory (own send history for defensive sequencing / pairings).
    private readonly PolicyMemory _policyMemory = new();
    private AbilityCatalog? _catalogForNotes;

    // R2 (sustain-cd): the last time the SelfHeal slot was attempted/sent.
    // Diagnostics only — it explains the cooldown wait and never gates a send.
    private long _lastSelfHealTriedMs;

    // Previous-frame edge tracking (state transitions clear local suppression).
    private bool _hasFrame;
    private BridgeState _lastState;
    private bool _lastHasTarget;
    private bool _lastInCombat;
    private bool _lastOnGcd;

    // v4.2.0 CC intelligence: the last observed target cast state, plus the
    // "stun ineffective" probe. A stun sent on a confirmed cast that leaves the
    // target still casting ~800 ms later is treated as ineffective (the game's
    // immune flag is unobservable). Cleared on target loss / out of combat.
    private TriState _lastObservedTargetCasting = TriState.Unknown;
    private long _stunAttemptMs;
    private int _stunAttemptSpellId;

    // ---- 4.6.0 "Cadence" -------------------------------------------------

    // Feature switches (all OFF by default, see CadenceOptions.Off).
    private CadenceOptions _cadence = CadenceOptions.Off;

    // GCD landing planner: the estimator + per-GCD pre-fire latch. Observed on
    // every decoded frame even with Landing=Off (it only *feeds* the prediction
    // the engines' adaptive polling reads); it never changes a plan unless
    // CadenceOptions.LandingAuto is set.
    private readonly LandingPlanner _landing = new();

    // Predicted end of the current GCD (0 = unknown). Read by the engines'
    // adaptive polling via PredictedGcdEndMs.
    private long _predictedGcdEndMs;

    // This tick's timing decision (telemetry; null = none).
    private SchedulerTiming? _lastTiming;

    // Insurance: the fired decision awaiting its send, so the DR application is
    // recorded ONLY after a real keystroke went out (never for a vetoed or
    // engine-rejected tick).
    private bool _insuranceAwaitsSend;
    private int _insuranceSpellId;
    private CcDrCategory _insuranceDr;
    private int _insuranceBaseDurationMs;

    // Insurance per-CAST latch: after one press, the same still-running cast is
    // never pressed again (cast age is monotonic inside one cast). Cleared when
    // the cast ends, the age bucket restarts (a new cast) or LatchGraceMs
    // elapses. A bool + timestamps (never a 0 sentinel) so a press at engine
    // ms 0 is still latched.
    private bool _insuranceLatchedCast;
    private long _insuranceLatchAtMs;
    private int _insuranceLatchCastBucket;

    /// <summary>Failed-action rejections detected (diagnostics; telemetry reads the plan).</summary>
    public int RejectionsDetected { get; private set; }

    /// <summary>Last attempted action (diagnostics/tests); null-ish until the first attempt.</summary>
    public (Slot Slot, KeyStroke Stroke, long AtMs, AttemptOutcome Outcome)? LastAttempt =>
        _hasAttempt ? (_lastAttemptSlot, _lastAttemptStroke, _lastAttemptAt, _lastAttemptOutcome) : null;

    /// <summary>Last successful send; null-ish until the first send.</summary>
    public (Slot Slot, KeyStroke Stroke, long AtMs)? LastSent =>
        _hasSent ? (_lastSentSlot, _lastSentStroke, _lastSentAt) : null;

    /// <summary>The policy memory (used by replay/tests to inspect sequencing state).</summary>
    public PolicyMemory Policy => _policyMemory;

    /// <summary>
    /// The predicted end of the current GCD in engine ms (0 = unknown). Fed by
    /// the landing planner on every decoded frame, so a pre-4.6 addon still
    /// yields an estimator-based prediction. The engines' adaptive polling
    /// reads this; it never changes a plan by itself.
    /// </summary>
    internal long PredictedGcdEndMs => _predictedGcdEndMs;

    /// <summary>
    /// This tick's Cadence timing decision (insurance / landing / opener), or
    /// null when this tick made none. Exactly one is recorded per tick; the
    /// precedence is insurance, landing pre-fire, landing abort, opener.
    /// </summary>
    internal SchedulerTiming? LastTiming => _lastTiming;

    /// <summary>The landing planner (replay/test seam: state + estimators).</summary>
    internal LandingPlanner Landing => _landing;

    /// <summary>
    /// 4.6.0 kick-failure memory. INSTANCE state owned by this scheduler: the
    /// only writer is <see cref="NoteFailure"/> and the only reader is the
    /// insurance predicate, so two schedulers (a replay, a test, a second
    /// engine) can never share, inherit or clear each other's ladder.
    /// </summary>
    internal KickMemory Kick { get; } = new();

    /// <summary>
    /// Applies the 4.6.0 feature switches. Optional by design: an unconfigured
    /// scheduler (every pre-4.6 caller, every test that does not opt in) keeps
    /// <see cref="CadenceOptions.Off"/>, which is byte-identical to 4.5.8.
    /// </summary>
    internal void Configure(CadenceOptions options) => _cadence = options;

    /// <summary>
    /// Records a decoded frame's heartbeat (cheap; called for every frame so
    /// link loss is detected even while the engine holds for other gates).
    /// </summary>
    public void Observe(BridgeFrame frame, long nowMs)
    {
        if (!_heartbeatSeen || frame.Heartbeat != _lastHeartbeat)
        {
            _heartbeatSeen = true;
            _lastHeartbeat = frame.Heartbeat;
            _heartbeatChangedAt = nowMs;
        }

        ObserveCrowdControl(frame, nowMs);
    }

    /// <summary>
    /// v4.2.0 per-frame crowd-control bookkeeping. Called automatically by
    /// <see cref="Observe"/> (scheduler mode) and directly by the engines with
    /// the legacy <see cref="PolicyMemory"/> when the scheduler is off, so the
    /// stun-ineffective latch, the break guard clearing and the DR reset work
    /// in both modes. Target gone / combat over clears everything: no CC state
    /// may be carried to a different creature.
    /// </summary>
    public void ObserveCrowdControl(BridgeFrame frame, long nowMs, PolicyMemory? memory = null)
    {
        var guardMemory = memory ?? _policyMemory;
        _lastObservedTargetCasting = frame.TargetCasting;

        if (!frame.HasTarget || !frame.InCombat)
        {
            guardMemory.ClearCcGuards();
            CrowdControlVetoes.ResetForTarget();
            _stunAttemptMs = 0;
            _stunAttemptSpellId = 0;
            return;
        }

        if (_stunAttemptMs > 0)
        {
            if (frame.TargetCasting == TriState.No)
            {
                // The cast stopped after the stun: it worked.
                _stunAttemptMs = 0;
                _stunAttemptSpellId = 0;
            }
            else if (nowMs - _stunAttemptMs >= 800)
            {
                // Still casting after the stun had time to land. Most likely an
                // immune target; never auto-stun it again on this target.
                CrowdControlVetoes.NoteStunIneffective();
                _stunAttemptMs = 0;
                _stunAttemptSpellId = 0;
            }
        }
    }

    /// <summary>
    /// Arms the stun-ineffective probe for a stun sent on a confirmed cast.
    /// Scheduler sends call this from <see cref="NoteSent"/>; the legacy path
    /// calls it directly after its own send so the latch also works with the
    /// scheduler disabled.
    /// </summary>
    public void ArmStunProbe(int spellId, long nowMs)
    {
        if (spellId <= 0 || _lastObservedTargetCasting != TriState.Yes) return;
        if (CrowdControlCatalog.FindAny(spellId) is not { Dr: CcDrCategory.Stun, AutoEligible: true })
            return;
        _stunAttemptMs = nowMs;
        _stunAttemptSpellId = spellId;
    }

    /// <summary>True when the addon heartbeat has been frozen past the timeout.</summary>
    public bool IsLinkLost(long nowMs, int heartbeatTimeoutMs) =>
        _heartbeatSeen && nowMs - _heartbeatChangedAt >= Math.Max(1, heartbeatTimeoutMs);

    /// <summary>Records a successful send (slot + stroke + spell + time).</summary>
    public void NoteSent(long nowMs, Slot slot, KeyStroke stroke, int spellId = 0)
    {
        _hasSent = true;
        _lastSentAt = nowMs;
        _lastSentSlot = slot;
        _lastSentStroke = stroke;
        _lastSentSpellId = spellId;

        var key = (slot, stroke, spellId);
        // A successful send proves the stroke works: reset the failure state.
        _failedUntil.Remove(key);
        if (slot == Slot.Main)
        {
            // The Main rotation rotates a pool of sibling identities. A send
            // on any one of them proves the rotation is alive, so EVERY Main
            // identity drops its ladder — the stuck key is re-armed at the
            // base 1.5 s window rather than inheriting a long backoff.
            RemoveMainStreaks();
        }
        else
        {
            _failureStreak.Remove(key);
        }
        // T2 (main-immediate): a Main send only counts toward the no-op cap
        // when the PREVIOUS Main press never produced a GCD. A GCD-observed
        // press landed (the rotation advanced), so it resets the streak instead
        // of accumulating toward MainSameSpellNoOpCap.
        if (slot == Slot.Main && _pendingConfirm is { SawGcd: true })
        {
            _attempts.Remove(key);
        }
        else if (_attempts.TryGetValue(key, out var attempt) && nowMs - attempt.WindowStartMs <= AttemptWindowMs)
        {
            _attempts[key] = (attempt.Count + 1, attempt.WindowStartMs);
        }
        else
        {
            _attempts[key] = (1, nowMs);
        }

        _pendingConfirm = (slot, stroke, spellId, nowMs, SawGcd: false, RidesGcd: RidesGcdHeuristic(slot, spellId));
        if (slot == Slot.SelfHeal) _lastSelfHealTriedMs = nowMs;

        if (spellId > 0 && _catalogForNotes?.TryGet(spellId) is { } ability)
            _policyMemory.NoteUse(ability, nowMs);
        if (slot == Slot.Trinket) _policyMemory.NoteTrinketUse(nowMs);

        if (spellId > 0 && CrowdControlCatalog.FindAny(spellId) is { } cc)
        {
            // A break-on-damage CC sent by the companion starts the attack
            // guard (manual player presses are noted by the engine).
            if (cc.BreaksOnDamage)
                _policyMemory.NoteCcBreakApplied(cc.SpellId, cc.Name, nowMs, cc.BaseDurationMs);

            // If this was an automatic stun on a confirmed cast, arm the
            // ineffective probe. The next Observe frames decide.
            ArmStunProbe(spellId, nowMs);
        }

        // 4.6.0 interrupt insurance: the DR ladder is written HERE, after the
        // stroke really went out - never at plan time - so a vetoed or
        // engine-rejected insurance press cannot poison the shared CC memory.
        if (_insuranceAwaitsSend && slot == Slot.Interrupt && spellId == _insuranceSpellId)
        {
            _insuranceAwaitsSend = false;
            CrowdControlVetoes.NoteUsed(_insuranceDr, nowMs, _insuranceBaseDurationMs);
        }
    }

    /// <summary>
    /// Records a send that did not come from the scheduler (auto-target /
    /// auto-interact fallback) so the min key interval still spans all input.
    /// </summary>
    public void NoteExternalSend(long nowMs)
    {
        _hasSent = true;
        _lastSentAt = nowMs;
    }

    /// <summary>
    /// Records a candidate that passed scheduling but was rejected by an
    /// engine OS gate. The (slot, stroke) pair is suppressed briefly so lower
    /// ranks get a turn and the status line does not flap every tick.
    /// </summary>
    public void NoteAttempt(long nowMs, Slot slot, KeyStroke stroke, AttemptOutcome outcome, int spellId = 0)
    {
        _hasAttempt = true;
        _lastAttemptSlot = slot;
        _lastAttemptStroke = stroke;
        _lastAttemptAt = nowMs;
        _lastAttemptOutcome = outcome;
        if (slot == Slot.SelfHeal) _lastSelfHealTriedMs = nowMs;
        if (outcome != AttemptOutcome.Sent)
        {
            // T2 (no-downtime Main): an OS-gate rejection re-arms the Main slot
            // at the fast MainReprobeMs floor instead of the 500 ms situational
            // suppression; every other slot keeps UnavailableSuppressMs.
            var ms = slot == Slot.Main ? MainReprobeMs : UnavailableSuppressMs;
            _blockedUntil[(slot, stroke, spellId)] = nowMs + ms;
        }
    }

    /// <summary>Drops all history (engine Start; a fresh session must not inherit state).</summary>
    public void Reset()
    {
        _heartbeatSeen = false;
        _lastHeartbeat = 0;
        _heartbeatChangedAt = 0;
        CrowdControlVetoes.ResetForTarget();
        _hasSent = false;
        _lastSentAt = 0;
        _lastSentSlot = default;
        _lastSentStroke = default;
        _lastSentSpellId = 0;
        _hasAttempt = false;
        _lastAttemptSlot = default;
        _lastAttemptStroke = default;
        _lastAttemptAt = 0;
        _lastAttemptOutcome = AttemptOutcome.Sent;
        _blockedUntil.Clear();
        _failedUntil.Clear();
        _failureStreak.Clear();
        _lastFailureAt.Clear();
        _attempts.Clear();
        _lastMainSpellId = 0;
        _pendingConfirm = null;
        _policyMemory.Reset();
        _catalogForNotes = null;
        _lastSelfHealTriedMs = 0;
        RejectionsDetected = 0;
        _hasFrame = false;
        _lastState = default;
        _lastHasTarget = false;
        _lastInCombat = false;
        _lastObservedTargetCasting = TriState.Unknown;
        _stunAttemptMs = 0;
        _stunAttemptSpellId = 0;
        _lastOnGcd = false;
        // 4.6.0: a fresh session must not inherit a GCD prediction, a landing
        // latch or a kick-failure rung from the previous one.
        _landing.Reset();
        _predictedGcdEndMs = 0;
        _lastTiming = null;
        _insuranceAwaitsSend = false;
        _insuranceSpellId = 0;
        _insuranceDr = default;
        _insuranceBaseDurationMs = 0;
        _insuranceLatchedCast = false;
        _insuranceLatchAtMs = 0;
        _insuranceLatchCastBucket = 0;
        Kick.Reset();
    }

    /// <summary>Deterministic scheduling decision for one tick.</summary>
    public SchedulePlan Advance(ScheduleInput input)
    {
        _catalogForNotes = input.Catalog;
        // 4.6.0: exactly one timing decision per tick; an early return (stale
        // frame / link lost / paused / no target) records none.
        _lastTiming = null;

        // 1. Link / protocol: a stale or unknown strip must never fire.
        if (input.Frame is not { } frame)
            return SchedulePlan.Hold(ScheduleReason.StaleFrame);
        Observe(frame, input.NowMs);
        if (IsLinkLost(input.NowMs, input.HeartbeatTimeoutMs))
        {
            // A press sent right before the link froze cannot be confirmed;
            // clearing the pending check prevents a false rejection when the
            // link heals.
            _pendingConfirm = null;
            return SchedulePlan.Hold(ScheduleReason.LinkLost);
        }
        var knownProtocol = frame.Version is PixelProtocol.SupportedVersion
            or PixelProtocol.SupportedVersionV6
            or PixelProtocol.SupportedVersionV4
            or PixelProtocol.SupportedVersionV1;
        if (!knownProtocol)
            return SchedulePlan.Hold(ScheduleReason.ProtocolMismatch);

        // 2. Failure recovery: a press that never produced a GCD is a failed
        //    action; suppress the stroke briefly instead of hammering it.
        var hasGcdInfo = frame.Version != PixelProtocol.SupportedVersionV1;
        ResolvePendingConfirm(input, frame, hasGcdInfo);
        _policyMemory.ClearExpired(input.NowMs);
        PruneAttempts(input.NowMs);

        // 2b. 4.6.0 Cadence timing. Observe the GCD edges for the length /
        //     latency EWMAs and predict the current GCD's end. Passive by
        //     design: with Landing=Off the prediction only feeds the engines'
        //     adaptive polling (and this is still cheaper than a plan change),
        //     so the plan below stays byte-identical to 4.5.8.
        _landing.Observe(frame.OnGcd, input.NowMs);
        _predictedGcdEndMs = _landing.PredictEnd(frame.Ext5, input.NowMs);

        // 3. State transitions (before the gates so a held plan still sees
        //    edges). Target regained / state change / GCD release clears local
        //    suppression: a candidate blocked in one state must be re-tried
        //    once the state heals.
        if (_hasFrame)
        {
            var targetChanged = frame.HasTarget != _lastHasTarget;
            var combatChanged = frame.InCombat != _lastInCombat;
            var gcdReleased = _lastOnGcd && !frame.OnGcd;
            // 4.6.0: a confirmed GCD means the last press landed, so the kick
            // failure rung is cleared (the kick is no longer "known broken").
            if (_lastOnGcd != frame.OnGcd && frame.OnGcd) Kick.Reset();
            if (frame.State != _lastState || targetChanged || combatChanged || gcdReleased)
                _blockedUntil.Clear();
            // A target change or a combat transition is a new fight state: the
            // rolling send-count backoff (RC6) is re-armed so a situational
            // ability that failed against the previous target is not silenced
            // against the new one.
            // T2 (no-downtime Main): a CHANGED Main spell id means the rotation
            // moved on to a different identity; drop the superseded identity's
            // failure memory in addition to the new-fight edge below.
            var currentMainSpell = CurrentMainSpellId(input);
            var mainSpellChanged = currentMainSpell != 0
                && _lastMainSpellId != 0
                && currentMainSpell != _lastMainSpellId;
            if (targetChanged || combatChanged)
            {
                _attempts.Clear();
                // 4.6.0: a new fight state also forgets a failed kick (target
                // change) - insurance may cover the new target immediately.
                Kick.Reset();
                // A new fight state is a new Main rotation: drop the Main
                // slot's failure memory (suppression + ladder + decay stamp)
                // so a stuck suggestion from the previous target cannot keep
                // the rotation silent. Situational slots keep their
                // suppression: a failed cooldown stays failed.
            }
            if (targetChanged || combatChanged || mainSpellChanged)
            {
                RemoveMainEntries(_failedUntil);
                RemoveMainEntries(_failureStreak);
                RemoveMainEntries(_lastFailureAt);
            }
        }
        _hasFrame = true;
        _lastState = frame.State;
        _lastHasTarget = frame.HasTarget;
        _lastInCombat = frame.InCombat;
        _lastOnGcd = frame.OnGcd;
        // 4.6.0: out of combat (or no target left) forgets a failed kick: there
        // is no fight to kick in, and the next target is a new attempt. The v1
        // protocol has no target flag, so its absence is UNKNOWN (fail open),
        // exactly like the NoTarget gate below.
        if (!frame.InCombat
            || (!frame.HasTarget && frame.Version != PixelProtocol.SupportedVersionV1))
            Kick.Reset();

        // 4. Hard state gates (the engine applies the same gates before the
        //    send path; modelled here so the scheduler is complete for tests
        //    and belt-and-braces on the engine path).
        if (frame.State == BridgeState.Paused)
            return SchedulePlan.Hold(ScheduleReason.Paused);
        // 4b. Fail-closed out-of-combat gate (2026-09-30): the engine computes
        //     the same predicate as the RotationEngine gate; a false value (out
        //     of combat with no OOC toggle + Ext3 mirror permission) holds
        //     before any candidate is scheduled. Required input, so a caller
        //     can never omit the gate and fail open. Placed after Paused so a
        //     paused bridge keeps its reason.
        if (!input.OutOfCombatPermitted)
            return SchedulePlan.Hold(ScheduleReason.OutOfCombat);
        // v4+ encode a target flag. A v1 frame has no such flag, so its
        // absence is UNKNOWN rather than "no target": fail open there (exactly
        // the legacy loop's behaviour). The policy still gates target-requiring
        // abilities, and the engine's auto-target state machine is driven by
        // the bridge state, not this flag.
        if (!frame.HasTarget && frame.Version != PixelProtocol.SupportedVersionV1)
            return SchedulePlan.Hold(ScheduleReason.NoTarget);

        // 5. Normalize: enabled candidates only. Since v3.5 a rotating slot
        //    (RC4) can expose more than one recently-seen candidate (the bridge
        //    rotates top/next/next2), so each slot carries a list; the policy
        //    verdict and rank below choose among them.
        var bySlot = new List<ActionCandidate>?[PixelProtocol.SlotCount];
        foreach (var candidate in input.Candidates)
        {
            if (!candidate.Enabled) continue;
            (bySlot[(int)candidate.Slot] ??= []).Add(candidate);
        }

        // 6. Situational policy (intelligence on): evaluate every candidate,
        //    drop non-USE verdicts, rank the survivors. Emergency survival
        //    actions outrank the rotation; everything else keeps its slot rank.
        var policyOn = input.Options is not null;
        var catalog = input.Catalog ?? AbilityCatalog.Default;
        var context = input.Context ?? CombatContext.Unknown();
        var policyHeld = 0;
        var policySkipped = 0;
        var castHeld = 0;
        var channelHeld = 0;
        string? policyDetail = null;
        List<PolicyVerdictEntry>? verdicts = input.CollectPolicyVerdicts ? [] : null;

        // R2 (sustain-cd): explain the wait when the player is inside the
        // sustain window but the bridge has no ready self-heal to encode (a
        // heal is only encoded when off cooldown). Owned by the SelfSustain
        // provider; purely diagnostic — it never changes an action or order.
        var hasReadySelfHeal = bySlot[(int)Slot.SelfHeal] is { Count: > 0 };
        var selfHealCoolingDown = policyOn
            && SelfSustainCandidateProvider.CoolingDown(context, input.Options!, catalog, hasReadySelfHeal);

        // R2: stamp the SelfHeal diagnostics onto its verdict entries (additive
        // telemetry fields; the replay never compares them).
        PolicyVerdictEntry StampSelfHeal(PolicyVerdictEntry entry)
        {
            if (entry.Slot != Slot.SelfHeal) return entry;
            var hint = entry.SpellId > 0 ? catalog.TryGet(entry.SpellId)?.ResetHint : null;
            return entry with { LastTriedMs = _lastSelfHealTriedMs, ResetHint = hint };
        }

        var ranked = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(PixelProtocol.SlotCount);

        // 5b. 4.6.0 interrupt INSURANCE (default OFF). Evaluated before the
        //     normal selection so an interruptible cast the slot-6 kick cannot
        //     cover is answered with the addon's own bound CC entry (Ext5 cell
        //     54-56). The press is appended as ONE MORE Interrupt candidate
        //     AFTER the wire candidates, so it ties LAST on the Interrupt rank:
        //     a live, usable kick always wins the tie (the CC is the fallback,
        //     never the first answer). It rides the existing bypass (GCD + min
        //     key interval), the existing OS gates and the existing failure
        //     machinery - there is no second send path - and with the toggle off
        //     nothing here runs.
        //
        //     The verdict is a PLAN only: the per-cast latch, the DR/latch
        //     arming and the timing record are committed in the send loop below,
        //     after the stroke actually reached `actions` (review m2). Arming
        //     them here is how a press that the send gates hold (a live GCD on a
        //     GCD-riding CC, an OS/failure suppression) used to report a press
        //     and block its own retry without ever sending a keystroke.
        var insurancePlan = InsuranceCandidate(input, frame, context, catalog,
            interruptCandidatePresent: bySlot[(int)Slot.Interrupt] is { Count: > 0 });

        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            if (bySlot[i] is not { Count: > 0 } slotCandidates) continue;
            var slot = (Slot)i;

            // Companion-only slots (Mobility / SelfHeal) exist solely because
            // the knowledge base curated them; without intelligence they are
            // never allowed to fire.
            if (!policyOn && slot is Slot.Mobility or Slot.SelfHeal) continue;

            // The bridge rotates a slot's candidate pool; evaluate every
            // recently-seen candidate so a held top does not hide the next.
            // The Ext2 SelfHeal2 alternate is tried once per slot.
            var selfHealAlternateTried = false;
            foreach (var slotCandidate in slotCandidates)
            {
                var candidate = slotCandidate;
                PolicyDecision? decision = null;
                var alternateRecorded = false;
                if (policyOn)
                {
                    decision = PolicyEvaluator.Evaluate(new PolicyInput
                    {
                        Slot = slot,
                        SpellId = candidate.SpellId,
                        Context = context,
                        Options = input.Options!,
                        Memory = _policyMemory,
                        NowMs = input.NowMs,
                        InCombat = frame.InCombat,
                        HasTarget = frame.HasTarget,
                    }, catalog);

                    // Ext2 (v3.0.0): when the primary SelfHeal candidate does not
                    // Use but the bridge encoded a second distinct candidate,
                    // evaluate the alternate with its own range (cell 28 B) and let
                    // a Use win the slot. Rank, the one-action-per-tick rule and the
                    // rest of the pipeline are unchanged: this is one more candidate
                    // for the same SelfHeal slot.
                    if (!selfHealAlternateTried
                        && slot == Slot.SelfHeal
                        && decision.Value.Verdict != PolicyVerdict.Use
                        && frame.SelfHeal2 is { } sh2
                        && sh2.SpellId > 0
                        && sh2.Stroke.VirtualKey != 0
                        && sh2.SpellId != candidate.SpellId)
                    {
                        var altContext = context.WithSlotRange((int)Slot.SelfHeal, sh2.Range);
                        var alt = PolicyEvaluator.Evaluate(new PolicyInput
                        {
                            Slot = Slot.SelfHeal,
                            SpellId = sh2.SpellId,
                            Context = altContext,
                            Options = input.Options!,
                            Memory = _policyMemory,
                            NowMs = input.NowMs,
                            InCombat = frame.InCombat,
                            HasTarget = frame.HasTarget,
                        }, catalog);
                        if (alt.Verdict == PolicyVerdict.Use)
                        {
                            candidate = new ActionCandidate(Slot.SelfHeal, sh2.Stroke, true, true,
                                input.NowMs, input.NowMs, 0, false, sh2.SpellId);
                            decision = alt;
                            alternateRecorded = true;
                            verdicts?.Add(StampSelfHeal(new PolicyVerdictEntry(Slot.SelfHeal, sh2.SpellId, alt.Verdict, alt.Reason)
                            {
                                Provider = alt.Provider,
                                Source = alt.Source,
                                Evidence = alt.Evidence,
                                Alternate = true,
                                Range = sh2.Range,
                            }));
                        }
                    }
                    selfHealAlternateTried = true;

                    if (decision.Value.Verdict != PolicyVerdict.Use)
                    {
                        // Five-state policy (registry §12): HOLD and UNKNOWN are
                        // "condition may clear — retry next tick" (counted as held);
                        // SKIP and UNAVAILABLE are structural/deliberate exclusions
                        // for this situation (counted as skipped). Only USE reaches
                        // the plan.
                        if (decision.Value.Verdict is PolicyVerdict.Hold or PolicyVerdict.Unknown)
                        {
                            policyHeld++;
                            if (decision.Value.Reason == ExecutionSafety.ChannelReason) channelHeld++;
                            else if (decision.Value.Reason == ExecutionSafety.CastReason) castHeld++;
                            NoteFirst(ref policyDetail, decision.Value.Reason);
                        }
                        else
                        {
                            policySkipped++;
                            NoteFirst(ref policyDetail, decision.Value.Reason);
                        }
                        verdicts?.Add(StampSelfHeal(new PolicyVerdictEntry(slot, candidate.SpellId, decision.Value.Verdict, decision.Value.Reason)
                        {
                            Provider = decision.Value.Provider,
                            Source = decision.Value.Source,
                            Evidence = decision.Value.Evidence,
                        }));
                        continue;
                    }
                    if (!alternateRecorded)
                        verdicts?.Add(StampSelfHeal(new PolicyVerdictEntry(slot, candidate.SpellId, PolicyVerdict.Use, decision.Value.Reason)
                        {
                            Provider = decision.Value.Provider,
                            Source = decision.Value.Source,
                            Evidence = decision.Value.Evidence,
                        }));
                }
                else
                {
                    // Intelligence off: the hard execution-safety gate still runs
                    // (it is not knowledge filtering). Same predicate as the policy
                    // uses, so the two modes can never disagree.
                    var execHold = ExecutionSafety.CastHoldReason(slot, candidate.SpellId, context.Cast, catalog);
                    if (execHold is not null)
                    {
                        if (execHold == ExecutionSafety.ChannelReason) channelHeld++;
                        else castHeld++;
                        NoteFirst(ref policyDetail, execHold);
                        verdicts?.Add(new PolicyVerdictEntry(slot, candidate.SpellId, PolicyVerdict.Hold, execHold));
                        continue;
                    }
                }

                ranked.Add((candidate, decision));
            }
        }

        // 5c. Append the insured cast AFTER the wire candidates (see 5b): a live
        //     kick keeps the Interrupt rank tie, so the CC only surfaces when the
        //     kick is absent or its own gates refuse it.
        if (insurancePlan is { } insuredPlan)
            ranked.Add((insuredPlan.Candidate, insuredPlan.Policy));

        if (ranked.Count == 0)
        {
            // R2 (sustain-cd): HP is in the sustain window, the spec owns a
            // curated self-heal, but none is ready this tick. Say so instead of
            // a generic "no candidate" so the "Now:" line and telemetry explain
            // the wait. The next tick that carries a ready heal fires normally
            // with no extra delay (the otherwise-empty plan holds).
            if (selfHealCoolingDown)
            {
                return SchedulePlan.Hold(ScheduleReason.SelfHealCoolingDown, 0, policyHeld, policySkipped,
                    SelfSustainCandidateProvider.CooldownWaitReason) with
                {
                    Verdicts = verdicts?.ToArray() ?? [],
                    SelfHealCoolingDown = true,
                    SelfHealLastTriedMs = _lastSelfHealTriedMs,
                };
            }
            if (policyHeld > 0 || policySkipped > 0 || castHeld > 0 || channelHeld > 0)
            {
                // Cast/channel holds keep their own reason codes so telemetry
                // distinguishes "waiting for the cast to finish" from a
                // knowledge-based hold.
                var reason = castHeld > 0 ? ScheduleReason.CastHold
                    : channelHeld > 0 ? ScheduleReason.ChannelHold
                    : policyHeld > 0 ? ScheduleReason.PolicyHold
                    : ScheduleReason.PolicySkip;
                return SchedulePlan.Hold(reason, 0, policyHeld, policySkipped, policyDetail) with
                {
                    Verdicts = verdicts?.ToArray() ?? [],
                };
            }
            return SchedulePlan.Hold(ScheduleReason.NoCandidate);
        }

        // Stable rank order: interrupt first, then emergency survival, then the
        // prescribed slot ranks. Policy-off rank table reproduces the legacy
        // interrupt/defensive/main/... order exactly.
        // 4.6.0 opener release (default OFF): inside the engine's boss-grade
        // pull window ONE ready Offensive major is pre-ranked ahead of the
        // first Main (rank 3, i.e. behind Interrupt/emergency/Defensive). Rank
        // elite/rare/normal/UNKNOWN never opens the window, so their order is
        // unchanged; with the toggle off this is the exact 4.5.8 ordering.
        var openerPromoted = _cadence.OpenerPromotes ? FirstOffensiveMajor(ranked) : -1;
        var ordered = ranked
            .Select((entry, index) => (entry, promoted: index == openerPromoted))
            .OrderBy(t => RankFor(t.entry.Candidate.Slot, t.entry.Policy, t.promoted))
            .Select(t => t.entry)
            .ToList();

        // 7. Duplicate collapse: one physical stroke, one press (highest
        //    rank wins; the list is already rank-ordered).
        var unique = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(ordered.Count);
        // T2 (main-immediate): a Main collapsed into a higher-ranked candidate
        // that shares its physical stroke is kept aside (not discarded) and is
        // re-admitted in the send loop below when that higher-ranked press is
        // held rather than emitted. It still passes every timing/suppression gate.
        var collapsedMains = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>();
        foreach (var entry in ordered)
        {
            var duplicate = false;
            foreach (var kept in unique)
            {
                if (kept.Candidate.Stroke == entry.Candidate.Stroke) { duplicate = true; break; }
            }
            if (!duplicate) unique.Add(entry);
            else if (entry.Candidate.Slot == Slot.Main) collapsedMains.Add(entry);
        }

        // 8. Stale demotion: pressed-since-change AND unchanged for the whole
        //    window is a stuck suggestion — move it behind fresh alternatives.
        //    Never removed: all-stale keeps the order (no deadlock).
        var fresh = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(unique.Count);
        var stale = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(unique.Count);
        foreach (var entry in unique)
        {
            // R2 (sustain-cd): a SelfHeal is NEVER stale-demoted. After a
            // cooldown (or a reset proc) the SAME spell is re-suggested while
            // the wire slot content never changed, so the pressed-since-change
            // bookkeeping would otherwise swallow the new opportunity and demote
            // the heal behind Main. A heal that is ready again is a fresh action.
            // T2 (main-immediate): the Main rotation is NEVER stale-demoted
            // either — the WHITE core rotation is the player's own spell-queue
            // behaviour and must stay ahead of fresh situational alternatives.
            if (entry.Candidate.Slot is not (Slot.SelfHeal or Slot.Main)
                && entry.Candidate.IsStale(input.NowMs, Math.Max(250, input.StaleAfterMs)))
                stale.Add(entry);
            else fresh.Add(entry);
        }
        var demoted = stale.Count > 0 && fresh.Count > 0;
        var final = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(unique.Count);
        final.AddRange(fresh);
        final.AddRange(stale);

        // 8b. Pending-confirm demotion: a just-pressed SITUATIONAL stroke that
        //     is still suggested while its GCD confirmation is unresolved may
        //     have failed silently. Demote it behind fresh alternatives so a
        //     dead situational action cannot shadow the rotation for the whole
        //     RejectDetect window (the failure itself is still detected and
        //     suppressed by ResolvePendingConfirm). The Main rotation is never
        //     demoted: re-pressing a main key is the player's own spell-queue
        //     behaviour, and a possibly-successful main means the GCD is
        //     starting — the GCD gate (next) already covers that case.
        //     R2 (sustain-cd): SelfHeal is never demoted at all — a heal that
        //     is ready must fire this tick, not be pushed behind Main while an
        //     unconfirmed press is pending. Its transient failure is still
        //     detected and capped at 1.5 s by ResolvePendingConfirm.
        if (_pendingConfirm is { } pending
            && pending.Slot != Slot.Main
            && pending.Slot != Slot.SelfHeal
            && input.NowMs - pending.SentAt < RejectDetectMs)
        {
            var ahead = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(final.Count);
            var behind = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(final.Count);
            foreach (var entry in final)
            {
                if (entry.Candidate.Slot == pending.Slot && entry.Candidate.Stroke == pending.Stroke)
                    behind.Add(entry);
                else
                    ahead.Add(entry);
            }
            if (ahead.Count > 0)
            {
                final.Clear();
                final.AddRange(ahead);
                final.AddRange(behind);
            }
        }

        // T2 (main-immediate): append the collapsed Mains behind the surviving
        // candidates. The send loop only acts on one when no earlier candidate
        // emitted the same stroke, so a held higher-rank press cannot strand Main.
        if (collapsedMains.Count > 0) final.AddRange(collapsedMains);

        // 8c. 4.6.0 LANDING (default OFF, [Scheduler] Landing=Auto only).
        //     Inside a LIVE GCD the head that rides it is pre-fired `leadMs`
        //     before the PREDICTED end, so the client's spell queue lands it the
        //     instant the GCD falls instead of waiting for the strip's flag.
        //     One pre-fire per GCD; the armed identity/verdict is compared on
        //     every tick (an Abort costs nothing - the next tick re-plans).
        //     With the toggle off `landingFire` stays null, the GCD gate below
        //     is the unchanged GcdHold and no timing is recorded.
        //
        //     Plan and COMMIT are split (review m3): `Plan` returns the intent
        //     only. The Fired latch, the latency sample and the timing record are
        //     committed in the send loop when the stroke is actually emitted, so
        //     a pre-fire that a later gate holds can neither be latched without a
        //     keystroke nor feed the latency EWMA a sample for a press that never
        //     happened.
        LandingCandidate? landingFired = null;
        LandingDecision landingDecision = LandingDecision.None;
        if (_cadence.LandingAuto && frame.OnGcd)
        {
            var landingHead = FirstGcdRiding(final);
            if (landingHead is { } armable)
            {
                landingDecision = _landing.Plan(armable, frame.OnGcd, frame.Ext5, input.NowMs, enabled: true);
                if (landingDecision.Action == LandingAction.Fire) landingFired = armable;
                // An Abort is a DECISION, not a press: it is recorded here (it
                // costs nothing and the next tick re-plans). The pre-fire's own
                // timing record is committed at emission below.
                if (landingDecision.Action == LandingAction.Abort && _lastTiming is null)
                {
                    _lastTiming = new SchedulerTiming(
                        SchedulerTiming.KindLandAbort,
                        landingDecision.Reason,
                        landingDecision.PredictedGcdEndMs,
                        landingDecision.LeadMs,
                        landingDecision.CandidateSpell);
                }
            }
        }

        // 9. Timing gates. GCD: v4+ encode it; the interrupt is off the GCD
        //    and always bypasses. Min key interval: one press per interval —
        //    the interrupt bypasses it too (time-critical kick must not wait
        //    behind a recent DPS press). v1 frames carry no GCD flag, so an
        //    identical repeat is suppressed for RepeatSuppressMs instead.
        //    The landing pre-fire is a SCHEDULED stroke: it is exempt from both
        //    of those two gates for this one GCD. Every UNSCHEDULED stroke is
        //    still gated by MinKeyIntervalMs exactly as before.
        var gcdUnknown = frame.Version is not (PixelProtocol.SupportedVersion
            or PixelProtocol.SupportedVersionV6
            or PixelProtocol.SupportedVersionV4);
        var minInterval = Math.Max(1, input.MinKeyIntervalMs);
        var repeatWindow = Math.Max(1, input.RepeatSuppressMs);
        var intervalElapsed = !_hasSent || input.NowMs - _lastSentAt >= minInterval;

        var actions = new List<ScheduledAction>(final.Count);
        // T2 (main-immediate): strokes already emitted this tick. A re-admitted
        // collapsed Main whose stroke was emitted by the press that replaced it
        // is skipped; one whose replacement was held is allowed through the gates.
        var emittedStrokes = new HashSet<KeyStroke>();
        var suppressed = 0;
        var firstHold = ScheduleReason.NoCandidate;
        var hasHold = false;
        void NoteHold(ScheduleReason reason)
        {
            if (!hasHold) { firstHold = reason; hasHold = true; }
        }

        foreach (var (candidate, policy) in final)
        {
            var slot = candidate.Slot;
            if (emittedStrokes.Contains(candidate.Stroke)) continue;
            // 4.6.0 landing: the one stroke the planner pre-fired this GCD is
            // the only candidate allowed through the GCD gate below.
            var landingScheduled = landingFired is { } fired
                && fired.Slot == slot
                && fired.Stroke == candidate.Stroke
                && fired.SpellId == candidate.SpellId;
            // GCD bypass (RC6): the interrupt is off-GCD by design; v3.5 also
            // exempts an emergency survival verdict and any ability the
            // registry marks off-GCD, so a time-critical situational action is
            // not lost to the brief GCD-off window. The landing pre-fire is the
            // scheduled exception (see 8c).
            if (frame.OnGcd && !landingScheduled && !BypassesGcd(slot, policy, candidate.SpellId))
            {
                NoteHold(ScheduleReason.GcdHold);
                continue;
            }
            // Bounded retry/backoff first: the same (slot, stroke, spell)
            // sent too many times inside the window means the action keeps
            // failing (immune, invalid, out of resource) — suppress it with an
            // escalating window, then retry at a decaying rate. Keying on the
            // spell identity keeps a rotated-in sibling candidate pressable.
            // T2 (no-downtime Main): the Main slot uses the tighter
            // MainSameSpellNoOpCap and is re-armed at the fast MainReprobeMs
            // floor instead of the escalating failure ladder; every other slot
            // keeps the 5-sends/1.5 s rule and its ladder.
            var sendCap = slot == Slot.Main ? MainSameSpellNoOpCap : MaxAttemptsPerWindow;
            if (_attempts.TryGetValue((slot, candidate.Stroke, candidate.SpellId), out var attempt)
                && attempt.Count >= sendCap
                && input.NowMs - attempt.WindowStartMs <= AttemptWindowMs)
            {
                _attempts.Remove((slot, candidate.Stroke, candidate.SpellId));
                if (slot == Slot.Main)
                    _failedUntil[(slot, candidate.Stroke, candidate.SpellId)] = input.NowMs + MainReprobeMs;
                else
                    NoteFailure(slot, candidate.Stroke, input.NowMs, candidate.SpellId);
                suppressed++;
                NoteHold(ScheduleReason.RetryBackoff);
                continue;
            }
            // T2 (main-immediate): the min key interval is a double-fire guard
            // only. The interrupt (a genuine changed kick must land immediately,
            // even right after a DPS press) and the Main rotation (WHITE
            // core-rotation executes immediately) bypass it for any DIFFERENT
            // press; only an identical slot+stroke+spell repeat inside the
            // interval is held, which covers the pre-GCD-flip window. Every
            // other slot keeps the one-press-per-interval pacing.
            // 4.6.0: the landing pre-fire is SCHEDULED (armed for the predicted
            // GCD edge), so it is exempt too. Unscheduled strokes keep the
            // exact 4.5.8 pacing.
            if (MinIntervalApplies(slot, candidate, intervalElapsed, landingScheduled))
            {
                NoteHold(ScheduleReason.MinInterval);
                continue;
            }
            // OS-gate suppression (cleared by state transitions) and failure
            // suppression (not cleared — a persistently failing stroke stays
            // quiet while lower ranks proceed).
            if ((_blockedUntil.TryGetValue((slot, candidate.Stroke, candidate.SpellId), out var until) && input.NowMs < until)
                || (_failedUntil.TryGetValue((slot, candidate.Stroke, candidate.SpellId), out var failedUntil) && input.NowMs < failedUntil))
            {
                suppressed++;
                NoteHold(ScheduleReason.Unavailable);
                continue;
            }
            if (gcdUnknown && _hasSent
                && slot == _lastSentSlot && candidate.Stroke == _lastSentStroke
                && input.NowMs - _lastSentAt < repeatWindow)
            {
                suppressed++;
                NoteHold(ScheduleReason.RepeatSuppressed);
                continue;
            }
            actions.Add(new ScheduledAction(
                slot,
                candidate.Stroke,
                candidate.SpellId,
                ReasonFor(slot, policy),
                BypassesGcd(slot, policy, candidate.SpellId))
            {
                Provider = policy?.Provider ?? "",
                Evidence = policy?.Evidence ?? [],
            });
            emittedStrokes.Add(candidate.Stroke);

            // 4.6.0 EMISSION COMMITS. This is the first point in the tick where
            // the stroke provably reached the plan (every GCD/min-interval/
            // suppression gate above said yes), so it is the only point where
            // the latches, the DR application and the timing record may be
            // armed (review m2/m3). A held press commits nothing: no bogus
            // telemetry, no blocked retry, no latency sample for a keystroke
            // that never happened.
            if (landingScheduled && landingDecision.Action == LandingAction.Fire)
            {
                _landing.CommitFire(input.NowMs);
                if (_lastTiming is null)
                {
                    _lastTiming = new SchedulerTiming(
                        SchedulerTiming.KindLandPrefire,
                        landingDecision.Reason,
                        landingDecision.PredictedGcdEndMs,
                        landingDecision.LeadMs,
                        landingDecision.CandidateSpell);
                }
            }
            if (insurancePlan is { } insuredCommit
                && slot == insuredCommit.Candidate.Slot
                && candidate.Stroke == insuredCommit.Candidate.Stroke
                && candidate.SpellId == insuredCommit.Candidate.SpellId)
            {
                CommitInsurance(input.NowMs, insuredCommit);
            }
        }

        // T2 (no-downtime Main): remember the Main identity that won this tick;
        // the next tick compares against it to drop a superseded identity's
        // failure memory.
        foreach (var action in actions)
        {
            if (action.Slot != Slot.Main) continue;
            _lastMainSpellId = action.SpellId;
            break;
        }

        if (actions.Count == 0)
            return SchedulePlan.Hold(hasHold ? firstHold : ScheduleReason.NoCandidate, suppressed, policyHeld, policySkipped, policyDetail) with
            {
                Verdicts = verdicts?.ToArray() ?? [],
                SelfHealCoolingDown = selfHealCoolingDown,
                SelfHealLastTriedMs = _lastSelfHealTriedMs,
            };

        // 10. Head metadata (diagnostics/tests only; the action list is the order).
        var head = actions[0];
        // 4.6.0: the head can be a SYNTHESIZED candidate (the interrupt insurance
        // press) for a slot the wire carried nothing in, so the slot's candidate
        // list may be empty. Fall back to the scheduled identity instead of
        // dereferencing a missing list.
        var headCandidate = bySlot[(int)head.Slot] is { } headSlot
            ? HeadCandidate(headSlot, head.Stroke, head.SpellId)
            : new ActionCandidate(head.Slot, head.Stroke, Enabled: true, Actionable: true,
                FirstSeenMs: input.NowMs, LastChangedMs: input.NowMs,
                LastPressedMs: 0, EverPressed: false, SpellId: head.SpellId);
        var confidence = ConfidenceFor(head.Reason);
        if (!headCandidate.Actionable) confidence -= 20;
        if (headCandidate.IsStale(input.NowMs, Math.Max(250, input.StaleAfterMs))) confidence -= 40;
        return new SchedulePlan(actions.ToArray(), head.Slot, head.Reason,
            Math.Clamp(confidence, 0, 100), demoted, suppressed, policyHeld, policySkipped, policyDetail)
        {
            Verdicts = verdicts?.ToArray() ?? [],
            SelfHealCoolingDown = selfHealCoolingDown,
            SelfHealLastTriedMs = _lastSelfHealTriedMs,
        };
    }

    /// <summary>
    /// Gravitational rank: interrupt, emergency survival, defensive, solo
    /// self-sustain, main, mobility, offensive, consumable, trinket. The
    /// policy-off path yields the exact legacy order for the six MaxDps slots.
    /// <paramref name="openerPromoted"/> lifts the one Offensive major the
    /// 4.6.0 opener window released to rank 3: ahead of the first Main, behind
    /// Interrupt / emergency / Defensive (never past a survival action).
    /// </summary>
    private static int RankFor(Slot slot, PolicyDecision? policy, bool openerPromoted = false)
    {
        if (policy is { Emergency: true }) return 1;
        if (openerPromoted) return 3;
        return slot switch
        {
            Slot.Interrupt => 0,
            Slot.Defensive => 2,
            Slot.SelfHeal => 3,
            Slot.Main => 4,
            Slot.Mobility => 5,
            Slot.Offensive => 6,
            Slot.Consumable => 7,
            Slot.Trinket => 8,
            _ => 9,
        };
    }

    private static void NoteFirst(ref string? detail, string reason)
    {
        detail ??= reason;
    }

    /// <summary>
    /// Reads the pending GCD confirmation: a GCD-riding press that never
    /// produced a GCD (while the addon is alive) failed. Non-GCD abilities and
    /// v1 frames cannot be confirmed this way.
    /// </summary>
    private void ResolvePendingConfirm(ScheduleInput input, BridgeFrame frame, bool hasGcdInfo)
    {
        if (_pendingConfirm is not { } pending) return;

        if (frame.OnGcd)
        {
            _pendingConfirm = pending with { SawGcd = true };
            return;
        }

        // The candidate vanished or changed: the press did something (or the
        // rotation moved on) — nothing failed. An empty slot means MaxDps has
        // nothing to suggest (a successful press advances the rotation), so a
        // vanished candidate must NOT be treated as a silent failure.
        var found = false;
        foreach (var candidate in input.Candidates)
        {
            if (candidate.Slot != pending.Slot || candidate.SpellId != pending.SpellId) continue;
            found = true;
            if (candidate.Stroke != pending.Stroke) _pendingConfirm = null;
            break;
        }
        if (!found) _pendingConfirm = null;
        if (_pendingConfirm is not { } stillPending) return;

        if (!hasGcdInfo || !stillPending.RidesGcd)
        {
            _pendingConfirm = null;
            return;
        }

        // A confirmed press needs no further tracking.
        if (stillPending.SawGcd)
        {
            if (input.NowMs - stillPending.SentAt >= RejectDetectMs) _pendingConfirm = null;
            return;
        }

        if (input.NowMs - stillPending.SentAt >= RejectDetectMs)
        {
            NoteFailure(stillPending.Slot, stillPending.Stroke, input.NowMs, stillPending.SpellId);
            _pendingConfirm = null;
            RejectionsDetected++;
        }
    }

    /// <summary>
    /// Records a failed press: escalating suppression window per consecutive
    /// failure — every non-Main slot 1.5/3/6/10 s, reset by the next successful
    /// send or a &gt;6 s silence. Main is never written to suppression (the
    /// WHITE core rotation re-arms next tick); its streak/decay counters still
    /// advance so telemetry and replay diagnostics stay truthful.
    /// </summary>
    internal void NoteFailure(Slot slot, KeyStroke stroke, long nowMs, int spellId = 0)
    {
        var key = (slot, stroke, spellId);
        // 4.6.0: a rejected slot-6 WIRE KICK stroke is a failed kick. This
        // scheduler's own memory (read by the insurance predicate) records the
        // first suppression rung so the next interruptible cast may be covered
        // by the Ext5 crowd control instead of a kick that is known to fail.
        // Written here - the single "this press failed" funnel - so every
        // failure path (no GCD after a press, repeated sends) feeds it
        // identically.
        //
        // Spell-aware on purpose (4.6.0 review): the interrupt slot also carries
        // SYNTHESIZED insurance presses and wire-delegated crowd control, whose
        // spell id is a curated CC entry. A CC that fails (out of range, DR
        // immunity, a target that moved) says nothing about the kick, so it must
        // never poison the ladder that decides whether insurance may cover the
        // next cast - the same conflation also fed a failing CC into the retry
        // state of a healthy kick. Only the wire kick (an id that is not a
        // curated crowd-control row) writes this memory.
        if (slot == Slot.Interrupt && !InterruptInsurance.IsCrowdControlPress(spellId))
            Kick.NoteFailure(nowMs);
        // R2 (sustain-cd): a SelfHeal press that failed for a TRANSIENT reason
        // (out of range, no resource yet, GCD timing) is re-polled every tick
        // and must fire as soon as the heal is ready again. Its suppression is
        // capped at the base reject window (1.5 s) with NO escalation and no
        // streak accumulation, so a reset/proc that makes the heal ready cannot
        // be held behind a long decaying backoff. Permanent reasons (policy
        // OFF, unbound, unknown spell) never reach here — they are policy
        // skips. All other slots keep the escalating 1.5/3/6/10 s backoff.
        if (slot == Slot.SelfHeal)
        {
            _failedUntil[key] = nowMs + RejectedSuppressMs;
            _failureStreak.Remove(key);
            _lastFailureAt.Remove(key);
            return;
        }
        // A long silence since the last failure restarts the ladder: the
        // diagnosis is stale, so this is a fresh failure at the base window.
        var streak = nowMs - _lastFailureAt.GetValueOrDefault(key) > FailureDecayMs
            ? 0
            : _failureStreak.GetValueOrDefault(key);
        _lastFailureAt[key] = nowMs;
        streak++;
        _failureStreak[key] = streak;
        // T2 (main-immediate): a failed Main press is NEVER written to
        // _failedUntil — the WHITE core rotation is re-pressed on the very
        // next tick (only the no-op send cap above may hold it briefly at
        // MainReprobeMs). The streak/decay counters still advance so telemetry
        // and replay diagnostics stay truthful. Every other slot keeps the
        // escalating 1.5 / 3 / 6 / 10 s ladder.
        if (slot == Slot.Main) return;
        var shift = Math.Min(Math.Max(0, streak - 1), 3);
        var windowMs = Math.Min(RejectedSuppressMs << shift, MaxFailedSuppressMs);
        _failedUntil[key] = nowMs + windowMs;
    }

    /// <summary>Drops every Main-slot ladder entry: a successful Main send re-arms the slot.</summary>
    private void RemoveMainStreaks() => RemoveMainEntries(_failureStreak);

    /// <summary>Removes every Main-slot key from a per-key failure map (target/combat edge).</summary>
    private static void RemoveMainEntries<TValue>(
        Dictionary<(Slot Slot, KeyStroke Stroke, int SpellId), TValue> map)
    {
        if (map.Count == 0) return;
        List<(Slot, KeyStroke, int)>? dead = null;
        foreach (var key in map.Keys)
            if (key.Slot == Slot.Main) (dead ??= []).Add(key);
        if (dead is null) return;
        foreach (var key in dead) map.Remove(key);
    }

    /// <summary>
    /// T2 (no-downtime Main): the enabled Main candidate's spell identity this
    /// tick (0 = none). Used to detect when the rotation moved on to a
    /// different Main spell so the superseded identity's failure memory can be
    /// dropped.
    /// </summary>
    private static int CurrentMainSpellId(ScheduleInput input)
    {
        foreach (var candidate in input.Candidates)
        {
            if (candidate.Slot != Slot.Main || !candidate.Enabled) continue;
            return candidate.SpellId;
        }
        return 0;
    }

    /// <summary>
    /// 4.6.0 interrupt INSURANCE. Returns the synthetic Interrupt candidate when
    /// the policy's <see cref="InterruptInsurance"/> says the Ext5 crowd-control
    /// entry may cover the target's live interruptible cast, or null.
    ///
    /// Everything is delegated: the toggle, the CC opt-in, the casting-only
    /// rule, the interruptibility rule, the curated eligibility and every veto
    /// (DR, immunity, deep-DR stun, the stun-ineffective latch) live in the
    /// policy file. The scheduler only decides WHERE the press goes (the
    /// existing Interrupt slot), records the DR application AFTER the keystroke
    /// really went out (see <see cref="NoteSent"/>), and keeps the per-cast
    /// latch that stops a still-running cast from being pressed twice.
    ///
    /// Plan-only (review m2): nothing is armed here. The per-cast latch, the
    /// DR/reason fields and the timing record are committed by
    /// <see cref="CommitInsurance"/> from the send loop, i.e. only when the
    /// candidate was actually emitted into the plan's actions.
    ///
    /// The candidate is fresh by construction (first-seen = now), so the stale
    /// demotion below can never bury an insured cast mid-cast.
    /// </summary>
    private InsurancePlan? InsuranceCandidate(
        ScheduleInput input, BridgeFrame frame, CombatContext context, AbilityCatalog catalog,
        bool interruptCandidatePresent)
    {
        // Two independent switches must BOTH be on: the scheduler's configured
        // [Interrupt] InsuranceEnabled (CadenceOptions) and the policy option the
        // engine fed in (PolicyOptions.FromSettings carries the same setting).
        // Either one off = the 4.5.8 path, checked before any other work so the
        // OFF path cannot even allocate a candidate.
        if (!_cadence.InsuranceEnabled) return null;
        var options = input.Options ?? new PolicyOptions
        {
            // Intelligence off: a minimal options object carrying just the
            // [Interrupt] values the predicate needs. Every other policy knob
            // stays at its default and the predicate does not consult them.
            InsuranceEnabled = true,
            InsuranceMinCastAgeMs = _cadence.InsuranceMinCastAgeMs,
        };
        if (!options.InsuranceEnabled) return null;

        // Per-cast latch: one insurance press per observed cast. The cast age
        // bucket is monotonic inside one cast, so a lower bucket (or a cast
        // that ended) means the previous press's cast is over. Only a COMMITTED
        // press (a real emission, see CommitInsurance) ever sets this latch.
        if (_insuranceLatchedCast)
        {
            var ext = frame.Ext5;
            var sameCast = context.TargetCasting == TriState.Yes
                && ext is { CastAgeBucket: > 0 }
                && ext.CastAgeBucket >= _insuranceLatchCastBucket
                && input.NowMs - _insuranceLatchAtMs <= LandingPlanner.LatchGraceMs;
            if (sameCast) return null;
            _insuranceLatchedCast = false;
        }

        var decision = InterruptInsurance.Evaluate(
            context, options, catalog, _policyMemory,
            frame.InCombat, frame.HasTarget, input.NowMs,
            interruptCandidatePresent, Kick.Failed(input.NowMs));
        if (!decision.Fire) return null;

        var candidate = new ActionCandidate(
            Slot.Interrupt, decision.Stroke, Enabled: true, Actionable: true,
            FirstSeenMs: input.NowMs, LastChangedMs: input.NowMs,
            LastPressedMs: 0, EverPressed: false, SpellId: decision.SpellId);
        // USE without the emergency flag: the interrupt's rank already puts it
        // first, and the emergency flag would additionally bypass the GCD
        // (insurance is a CC press, not a survival action - it bypasses the GCD
        // only when the registry says the spell itself is off-GCD).
        var policy = PolicyDecision.Use(decision.Reason);
        return new InsurancePlan(candidate, policy, decision,
            frame.Ext5?.CastAgeBucket ?? 0);
    }

    /// <summary>
    /// 4.6.0 insurance COMMIT, called from the send loop once the insured stroke
    /// is in `actions`. Only here does the tick become a real press: the DR
    /// application is armed for <see cref="NoteSent"/>, the per-cast latch arms
    /// and the timing record names the press. A press that any gate held
    /// commits nothing, so it can be retried on the next tick (review m2).
    /// </summary>
    private void CommitInsurance(long nowMs, InsurancePlan plan)
    {
        _insuranceAwaitsSend = true;
        _insuranceSpellId = plan.Decision.SpellId;
        _insuranceDr = plan.Decision.Dr;
        _insuranceBaseDurationMs = plan.Decision.BaseDurationMs;
        _insuranceLatchedCast = true;
        _insuranceLatchAtMs = nowMs;
        _insuranceLatchCastBucket = plan.CastAgeBucket;
        _lastTiming ??= new SchedulerTiming(
            SchedulerTiming.KindInsurance, plan.Decision.Reason, _predictedGcdEndMs, 0,
            plan.Decision.SpellId);
    }

    /// <summary>
    /// 4.6.0 landing: the highest-ranked candidate that RIDES the GCD (i.e. is
    /// not exempt by <see cref="BypassesGcd"/>). That is the only stroke the
    /// planner may pre-fire; everything off-GCD already sends immediately.
    /// </summary>
    private LandingCandidate? FirstGcdRiding(List<(ActionCandidate Candidate, PolicyDecision? Policy)> final)
    {
        foreach (var (candidate, policy) in final)
        {
            if (BypassesGcd(candidate.Slot, policy, candidate.SpellId)) continue;
            return new LandingCandidate(
                candidate.Slot,
                candidate.Stroke,
                candidate.SpellId,
                policy?.Verdict ?? PolicyVerdict.Use,
                ReasonFor(candidate.Slot, policy),
                policy is { Emergency: true });
        }
        return null;
    }

    /// <summary>
    /// 4.6.0 opener: index of the FIRST ready Offensive major in the ranked
    /// list (-1 = none). "Major" = a curated major offensive purpose or a
    /// cooldown of at least 60 s; an unknown identity is never promoted (a
    /// wrong major on a pull is the cooldown waste this window exists to avoid).
    /// </summary>
    private int FirstOffensiveMajor(List<(ActionCandidate Candidate, PolicyDecision? Policy)> ranked)
    {
        for (var i = 0; i < ranked.Count; i++)
        {
            var candidate = ranked[i].Candidate;
            if (candidate.Slot != Slot.Offensive) continue;
            if (candidate.SpellId <= 0) continue;
            if (_catalogForNotes?.TryGet(candidate.SpellId) is not { } ability) continue;
            if (ability.Purpose == AbilityPurpose.MajorOffensive || ability.CooldownMs >= 60_000) return i;
        }
        return -1;
    }

    /// <summary>Test/diagnostic seam: the failure-suppression window set for a key (0 = none).</summary>
    internal long FailedUntilFor(Slot slot, KeyStroke stroke, int spellId = 0) =>
        _failedUntil.GetValueOrDefault((slot, stroke, spellId));

    private void PruneAttempts(long nowMs)
    {
        if (_attempts.Count == 0) return;
        List<(Slot, KeyStroke, int)>? dead = null;
        foreach (var (key, attempt) in _attempts)
        {
            if (nowMs - attempt.WindowStartMs > AttemptWindowMs)
                (dead ??= []).Add(key);
        }
        if (dead is not null)
            foreach (var key in dead) _attempts.Remove(key);
    }

    /// <summary>
    /// Does a press of this slot/ability start the global cooldown? Known
    /// abilities answer from the knowledge base; unknown rotation-slot
    /// identities conservatively assume yes (failed-action detection only).
    /// </summary>
    private bool RidesGcdHeuristic(Slot slot, int spellId)
    {
        if (_catalogForNotes?.TryGet(spellId) is { } ability) return ability.RidesGcd;
        return slot is Slot.Main or Slot.Offensive or Slot.Mobility;
    }

    /// <summary>
    /// v3.5 GCD bypass: a candidate may be attempted during an active GCD when
    /// it is a catalogued OFF-GCD ability (the fail-open legacy path for the
    /// interrupt), an emergency survival verdict, or a catalogued
    /// <see cref="GcdKind.OffGcd"/> ability in any other slot.
    ///
    /// 4.6.0 (review M1): the exemption is per SPELL, not per slot. The
    /// interrupt slot is no longer a blanket bypass, because the same slot now
    /// carries the synthesized INSURANCE press - a crowd control that usually
    /// rides the GCD. Sending it mid-GCD makes the client drop it while the
    /// caller believes it landed (the DR ladder advanced and the per-cast latch
    /// suppressed the retry). So:
    ///  * a catalogued ability answers from its <see cref="GcdKind"/>;
    ///  * an uncatalogued / unknown interrupt identity keeps the pre-4.6
    ///    exemption (a kick the registry does not know must still land), which
    ///    is also the exact 4.5.8 behaviour.
    /// </summary>
    private bool BypassesGcd(Slot slot, PolicyDecision? policy, int spellId)
    {
        if (policy is { Emergency: true }) return true;
        if (spellId > 0 && _catalogForNotes?.TryGet(spellId) is { } ability)
            return ability.Gcd == GcdKind.OffGcd;
        return slot == Slot.Interrupt;
    }

    /// <summary>
    /// T2 (main-immediate): does the min key interval hold this candidate?
    /// The interrupt and the Main rotation are exempt — any different press may
    /// land immediately (the engine's per-slot OS gates stay authoritative).
    /// Only an identical slot+stroke+spell repeat inside the interval is held,
    /// covering the pre-GCD-flip double-fire window. Every other slot keeps the
    /// original one-press-per-interval pacing.
    ///
    /// 4.6.0 landing: <paramref name="scheduled"/> marks the ONE stroke the
    /// landing planner pre-fired for this GCD. It is exempt (it was armed for
    /// the predicted GCD edge, exactly like a kick). Every UNSCHEDULED stroke
    /// keeps the pacing above unchanged.
    /// </summary>
    private bool MinIntervalApplies(Slot slot, ActionCandidate candidate, bool intervalElapsed, bool scheduled)
    {
        if (intervalElapsed) return false;
        if (scheduled) return false;
        if (slot is not (Slot.Interrupt or Slot.Main)) return true;
        return _hasSent
            && slot == _lastSentSlot
            && candidate.Stroke == _lastSentStroke
            && candidate.SpellId == _lastSentSpellId;
    }

    /// <summary>The exact candidate behind the scheduled head action (fallback: the slot's first).</summary>
    private static ActionCandidate HeadCandidate(List<ActionCandidate> candidates, KeyStroke stroke, int spellId)
    {
        foreach (var candidate in candidates)
            if (candidate.Stroke == stroke && candidate.SpellId == spellId) return candidate;
        return candidates[0];
    }

    private static ScheduleReason ReasonFor(Slot slot, PolicyDecision? policy)
    {
        if (policy is { Emergency: true }) return ScheduleReason.EmergencySurvival;
        return slot switch
        {
            Slot.Interrupt => ScheduleReason.InterruptUrgency,
            Slot.Defensive => ScheduleReason.DefensiveUrgency,
            Slot.SelfHeal => ScheduleReason.SelfSustain,
            Slot.Main => ScheduleReason.MainRotation,
            Slot.Mobility => ScheduleReason.MobilityUplift,
            Slot.Offensive => ScheduleReason.OffensiveCooldown,
            Slot.Consumable => ScheduleReason.Consumable,
            Slot.Trinket => ScheduleReason.Trinket,
            _ => ScheduleReason.NoCandidate,
        };
    }

    private static int ConfidenceFor(ScheduleReason reason) => reason switch
    {
        ScheduleReason.InterruptUrgency => 95,
        ScheduleReason.EmergencySurvival => 90,
        ScheduleReason.DefensiveUrgency => 80,
        ScheduleReason.SelfSustain => 75,
        ScheduleReason.MainRotation => 70,
        ScheduleReason.MobilityUplift => 65,
        ScheduleReason.OffensiveCooldown => 60,
        ScheduleReason.Consumable => 55,
        ScheduleReason.Trinket => 50,
        _ => 50,
    };

    /// <summary>
    /// 4.6.0 insurance PLAN: the synthetic candidate the policy approved this
    /// tick plus everything the commit needs. It is a local value, never a
    /// field: a plan that the send gates hold leaves no scheduler state behind
    /// (review m2). <see cref="CastAgeBucket"/> is the Ext5 bucket the plan was
    /// approved with, so the per-cast latch compares against the same cast.
    /// </summary>
    private readonly record struct InsurancePlan(
        ActionCandidate Candidate,
        PolicyDecision Policy,
        InsuranceDecision Decision,
        int CastAgeBucket);
}
