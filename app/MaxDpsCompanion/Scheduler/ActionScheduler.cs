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
        if (_attempts.TryGetValue(key, out var attempt) && nowMs - attempt.WindowStartMs <= AttemptWindowMs)
            _attempts[key] = (attempt.Count + 1, attempt.WindowStartMs);
        else
            _attempts[key] = (1, nowMs);

        _pendingConfirm = (slot, stroke, spellId, nowMs, SawGcd: false, RidesGcd: RidesGcdHeuristic(slot, spellId));
        if (slot == Slot.SelfHeal) _lastSelfHealTriedMs = nowMs;

        if (spellId > 0 && _catalogForNotes?.TryGet(spellId) is { } ability)
            _policyMemory.NoteUse(ability, nowMs);
        if (slot == Slot.Trinket) _policyMemory.NoteTrinketUse(nowMs);
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
            _blockedUntil[(slot, stroke, spellId)] = nowMs + UnavailableSuppressMs;
    }

    /// <summary>Drops all history (engine Start; a fresh session must not inherit state).</summary>
    public void Reset()
    {
        _heartbeatSeen = false;
        _lastHeartbeat = 0;
        _heartbeatChangedAt = 0;
        _hasSent = false;
        _lastSentAt = 0;
        _lastSentSlot = default;
        _lastSentStroke = default;
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
        _pendingConfirm = null;
        _policyMemory.Reset();
        _catalogForNotes = null;
        _lastSelfHealTriedMs = 0;
        RejectionsDetected = 0;
        _hasFrame = false;
        _lastState = default;
        _lastHasTarget = false;
        _lastInCombat = false;
        _lastOnGcd = false;
    }

    /// <summary>Deterministic scheduling decision for one tick.</summary>
    public SchedulePlan Advance(ScheduleInput input)
    {
        _catalogForNotes = input.Catalog;

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

        // 3. State transitions (before the gates so a held plan still sees
        //    edges). Target regained / state change / GCD release clears local
        //    suppression: a candidate blocked in one state must be re-tried
        //    once the state heals.
        if (_hasFrame)
        {
            var targetChanged = frame.HasTarget != _lastHasTarget;
            var combatChanged = frame.InCombat != _lastInCombat;
            var gcdReleased = _lastOnGcd && !frame.OnGcd;
            if (frame.State != _lastState || targetChanged || combatChanged || gcdReleased)
                _blockedUntil.Clear();
            // A target change or a combat transition is a new fight state: the
            // rolling send-count backoff (RC6) is re-armed so a situational
            // ability that failed against the previous target is not silenced
            // against the new one.
            if (targetChanged || combatChanged)
            {
                _attempts.Clear();
                // A new fight state is a new Main rotation: drop the Main
                // slot's failure memory (suppression + ladder + decay stamp)
                // so a stuck suggestion from the previous target cannot keep
                // the rotation silent. Situational slots keep their
                // suppression: a failed cooldown stays failed.
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
        var ordered = ranked
            .OrderBy(t => RankFor(t.Candidate.Slot, t.Policy))
            .ToList();

        // 7. Duplicate collapse: one physical stroke, one press (highest
        //    rank wins; the list is already rank-ordered).
        var unique = new List<(ActionCandidate Candidate, PolicyDecision? Policy)>(ordered.Count);
        foreach (var entry in ordered)
        {
            var duplicate = false;
            foreach (var kept in unique)
            {
                if (kept.Candidate.Stroke == entry.Candidate.Stroke) { duplicate = true; break; }
            }
            if (!duplicate) unique.Add(entry);
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
            if (entry.Candidate.Slot != Slot.SelfHeal
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

        // 9. Timing gates. GCD: v4+ encode it; the interrupt is off the GCD
        //    and always bypasses. Min key interval: one press per interval —
        //    the interrupt bypasses it too (time-critical kick must not wait
        //    behind a recent DPS press). v1 frames carry no GCD flag, so an
        //    identical repeat is suppressed for RepeatSuppressMs instead.
        var gcdUnknown = frame.Version is not (PixelProtocol.SupportedVersion
            or PixelProtocol.SupportedVersionV6
            or PixelProtocol.SupportedVersionV4);
        var minInterval = Math.Max(1, input.MinKeyIntervalMs);
        var repeatWindow = Math.Max(1, input.RepeatSuppressMs);
        var intervalElapsed = !_hasSent || input.NowMs - _lastSentAt >= minInterval;

        var actions = new List<ScheduledAction>(final.Count);
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
            // GCD bypass (RC6): the interrupt is off-GCD by design; v3.5 also
            // exempts an emergency survival verdict and any ability the
            // registry marks off-GCD, so a time-critical situational action is
            // not lost to the brief GCD-off window.
            if (frame.OnGcd && !BypassesGcd(slot, policy, candidate.SpellId))
            {
                NoteHold(ScheduleReason.GcdHold);
                continue;
            }
            // Bounded retry/backoff first: the same (slot, stroke, spell)
            // sent too many times inside the window means the action keeps
            // failing (immune, invalid, out of resource) — suppress it with an
            // escalating window, then retry at a decaying rate. Keying on the
            // spell identity keeps a rotated-in sibling candidate pressable.
            if (_attempts.TryGetValue((slot, candidate.Stroke, candidate.SpellId), out var attempt)
                && attempt.Count >= MaxAttemptsPerWindow
                && input.NowMs - attempt.WindowStartMs <= AttemptWindowMs)
            {
                NoteFailure(slot, candidate.Stroke, input.NowMs, candidate.SpellId);
                _attempts.Remove((slot, candidate.Stroke, candidate.SpellId));
                suppressed++;
                NoteHold(ScheduleReason.RetryBackoff);
                continue;
            }
            // The interrupt bypasses the min interval only when its stroke
            // CHANGED (a genuine new kick must land immediately, even right
            // after a DPS press). An identical repeated interrupt suggestion
            // is the same physical key again — held for the interval so a
            // failed/persisting kick cannot machine-gun at poll rate.
            if (!intervalElapsed
                && (slot != Slot.Interrupt
                    || (_hasSent && slot == _lastSentSlot && candidate.Stroke == _lastSentStroke)))
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
                slot == Slot.Interrupt)
            {
                Provider = policy?.Provider ?? "",
                Evidence = policy?.Evidence ?? [],
            });
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
        var headCandidate = HeadCandidate(bySlot[(int)head.Slot]!, head.Stroke, head.SpellId);
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
    /// </summary>
    private static int RankFor(Slot slot, PolicyDecision? policy)
    {
        if (policy is { Emergency: true }) return 1;
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
    /// failure — Main 1.5/3/3/3 s (hard 3 s cap), every other slot
    /// 1.5/3/6/10 s — reset by the next successful send or a &gt;6 s silence.
    /// </summary>
    internal void NoteFailure(Slot slot, KeyStroke stroke, long nowMs, int spellId = 0)
    {
        var key = (slot, stroke, spellId);
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
        // Main (Arms stuck fix): 1.5 / 3 / 3 / 3 s — the rotation key is
        // re-armed hard at 3 s. Every other slot keeps the escalating
        // 1.5 / 3 / 6 / 10 s ladder.
        long windowMs;
        if (slot == Slot.Main)
        {
            windowMs = streak <= 1 ? RejectedSuppressMs : MaxMainSuppressMs;
        }
        else
        {
            var shift = Math.Min(Math.Max(0, streak - 1), 3);
            windowMs = Math.Min(RejectedSuppressMs << shift, MaxFailedSuppressMs);
        }
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
    /// it is the interrupt (off-GCD by design), an emergency survival verdict,
    /// or an ability the registry marks <see cref="GcdKind.OffGcd"/>. Everything
    /// else waits for the GCD to fall, as before.
    /// </summary>
    private bool BypassesGcd(Slot slot, PolicyDecision? policy, int spellId)
    {
        if (slot == Slot.Interrupt) return true;
        if (policy is { Emergency: true }) return true;
        if (spellId > 0 && _catalogForNotes?.TryGet(spellId) is { Gcd: GcdKind.OffGcd }) return true;
        return false;
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
}
