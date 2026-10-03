namespace MaxDpsCompanion;

/// <summary>
/// Per-slot observation history for the decision layer: a <em>last-seen set</em>
/// keyed by <c>(slot, spellId)</c>. Since v3.5 the bridge rotates a slot's
/// ready/bound candidate pool (top, next, next2) across rendered ticks, so the
/// stroke appearing in one slot changes every rotation step. A per-slot
/// single-stroke tracker would reset its window on every rotation and could
/// never tell a stale suggestion from a fresh one; the set keeps an independent
/// last-seen stamp per candidate instead.
///
/// Pure bookkeeping — it never reads game memory and never sends input.
/// </summary>
internal sealed class CandidateTracker
{
    /// <summary>How many candidates one rotating slot offers (top, next, next2).</summary>
    public const int RotationCandidates = 3;

    /// <summary>Bridge rotation dwell in rendered ticks (MaxDpsBridgeDB.RotationDwell default).</summary>
    public const int RotationDwellTicks = 3;

    /// <summary>Rendered-tick length assumed by the bridge (UPDATE_INTERVAL 0.033 s).</summary>
    public const int RenderedTickMs = 33;

    /// <summary>Floor for the candidate TTL: a just-rotated sibling stays eligible.</summary>
    public const int MinTtlMs = 1000;

    /// <summary>Ceiling for the candidate TTL: never hold a candidate longer than a rotation.</summary>
    public const int MaxTtlMs = 2500;

    /// <summary>TTL spans this many measured ticks (Arms stuck: 3x the real tick).</summary>
    public const int TtlTickMultiplier = 3;

    /// <summary>
    /// Candidate lifetime for a measured/assumed tick:
    /// <c>Clamp(max(1000, 3 * tickMs), 1000, 2500)</c>. The floor keeps a
    /// just-rotated sibling eligible across the bridge's rotation dwell; the
    /// ceiling bounds a slow/stalled sampler. <paramref name="tickMs"/> &lt;= 0
    /// falls back to the floor (1000).
    /// </summary>
    public static long TtlMsFor(int tickMs) =>
        Math.Clamp(Math.Max(MinTtlMs, (long)tickMs * TtlTickMultiplier), MinTtlMs, MaxTtlMs);

    /// <summary>
    /// Default TTL at the bridge's assumed rendered tick (33 ms → the 1000 ms
    /// floor). The engine uses <see cref="TtlMs"/> (its measured tick) instead.
    /// </summary>
    public static long DefaultTtlMs => TtlMsFor(RenderedTickMs);

    /// <summary>Entries older than this are pruned outright (bounded memory).</summary>
    private const long PruneAfterMs = 60_000;

    private sealed class Observation
    {
        public KeyStroke Stroke;
        public long FirstSeenMs;
        public long LastSeenMs;
        public long LastPressedMs;
        public bool PressedSinceChange;
        public bool Present;
    }

    private readonly Dictionary<(int Slot, int SpellId), Observation> _observed = new();

    // Measured frame interval: an EMA of the gap between consecutive Update
    // stamps (the engine calls Update once per decoded frame). Smallest
    // plumbing: no engine/clock change, derived only from the NowMs already
    // passed in — Advance and Snapshot stay pure.
    private long _lastUpdateMs;
    private long _measuredTickMs;

    // T3 (no-downtime MAIN) freshness bookkeeping. The bridge repaints its
    // heartbeat every rendered tick, so a repeated heartbeat means the addon
    // stopped painting (the scheduler's link-loss signal). _lastMainSpellId
    // remembers the most recent non-zero Main identity, so a brief empty-Main
    // gap is distinguishable from a switch to a different spell.
    private int _lastHeartbeat = -1;
    private bool _hasHeartbeat;
    private bool _frameFresh;
    private int _lastMainSpellId;

    /// <summary>Smoothed measured frame interval; 0 before the second frame.</summary>
    public long MeasuredTickMs => _measuredTickMs;

    /// <summary>TTL for the currently measured tick (floor 1000, ceiling 2500).</summary>
    public long TtlMs => TtlMsFor((int)Math.Clamp(_measuredTickMs, int.MinValue, int.MaxValue));

    /// <summary>
    /// Records one decoded frame. Every present <c>(slot, spellId)</c> pair is
    /// stamped now and marked present; pairs the frame no longer carries keep
    /// their last-seen stamp so the TTL snapshot can still offer a candidate the
    /// bridge rotated out moments ago. A same-spell different-stroke swap (a new
    /// keybind for the same identity) starts a new observation window.
    /// </summary>
    public void Update(BridgeFrame frame, long nowMs)
    {
        // Measured tick: exponential moving average (alpha 1/4) of the gap
        // between decoded frames. A skipped/idle gap clamps at the TTL ceiling.
        if (_lastUpdateMs > 0 && nowMs > _lastUpdateMs)
        {
            var delta = nowMs - _lastUpdateMs;
            _measuredTickMs = _measuredTickMs == 0 ? delta : (_measuredTickMs * 3 + delta) / 4;
        }
        _lastUpdateMs = nowMs;

        // T3: a fresh frame advances the bridge heartbeat; an identical
        // heartbeat is a frozen paint (stale) and must not keep the sole Main
        // candidate alive. Remember the latest non-zero Main identity.
        _frameFresh = !_hasHeartbeat || frame.Heartbeat != _lastHeartbeat;
        _hasHeartbeat = true;
        _lastHeartbeat = frame.Heartbeat;
        var mainSpell = frame.Slots[(int)Slot.Main] is null ? 0 : frame.SpellId(Slot.Main);
        if (mainSpell != 0) _lastMainSpellId = mainSpell;

        foreach (var observation in _observed.Values) observation.Present = false;

        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            var stroke = frame.Slots[i];
            if (stroke is null) continue;
            var spellId = frame.SpellId((Slot)i);
            var key = (i, spellId);
            if (_observed.TryGetValue(key, out var observation))
            {
                if (observation.Stroke != stroke.Value)
                {
                    observation.FirstSeenMs = nowMs;
                    observation.PressedSinceChange = false;
                }
                observation.Stroke = stroke.Value;
                observation.LastSeenMs = nowMs;
                observation.Present = true;
            }
            else
            {
                _observed[key] = new Observation
                {
                    Stroke = stroke.Value,
                    FirstSeenMs = nowMs,
                    LastSeenMs = nowMs,
                    LastPressedMs = 0,
                    PressedSinceChange = false,
                    Present = true,
                };
            }
        }

        Prune(nowMs);
    }

    /// <summary>
    /// Marks a candidate as acted on (called only after a successful send). A
    /// spell id selects the exact <c>(slot, spellId)</c> pair (the scheduler may
    /// press a candidate the current frame no longer shows); zero marks the
    /// slot's present candidate.
    /// </summary>
    public void NotePressed(Slot slot, long nowMs, int spellId = 0)
    {
        Observation? target = null;
        foreach (var ((s, id), observation) in _observed)
        {
            if (s != (int)slot) continue;
            if (spellId != 0 ? id == spellId : observation.Present)
            {
                target = observation;
                if (spellId != 0) break;
            }
        }
        if (target is null) return;
        target.LastPressedMs = nowMs;
        target.PressedSinceChange = true;
    }

    /// <summary>
    /// Snapshot of the candidates present in the latest frame. Retained for the
    /// legacy path/telemetry and every pre-v3.5 consumer.
    /// </summary>
    public ActionCandidate[] Snapshot(bool[] slotEnabled) => Build(slotEnabled, nowMs: 0, ttlMs: -1);

    /// <summary>
    /// TTL snapshot: every candidate in the last-seen set whose stamp is still
    /// inside <paramref name="ttlMs"/> (plus, always, the candidates present in
    /// the latest frame). Candidates are ordered by slot then spell id so the
    /// scheduler's rank is deterministic. A candidate the bridge has stopped
    /// offering falls outside the window and is dropped — stale candidates are
    /// never handed to the scheduler, so they are never pressed.
    /// </summary>
    public ActionCandidate[] Snapshot(bool[] slotEnabled, long nowMs, long ttlMs) =>
        Build(slotEnabled, nowMs, ttlMs);

    private ActionCandidate[] Build(bool[] slotEnabled, long nowMs, long ttlMs)
    {
        // T3 (no-downtime MAIN): while a fresh frame still carries the SAME
        // sole Main identity, refresh its TTL every tick instead of letting it
        // fall out of the 1000-2500 ms window (TtlMsFor). The bridge blanks
        // Main while it re-probes a cooldown/power-gated glow; without this the
        // scheduler would see NoCandidate in that gap where a usable filler
        // exists. Limited to the sole Main candidate: any other slot, a second
        // Main identity, or a stale (frozen-heartbeat) frame refreshes nothing
        // and expires exactly as before.
        var soleMainSpellId = SoleMainSpellId();
        var refreshMain = ttlMs >= 0
            && _frameFresh
            && soleMainSpellId != 0
            && soleMainSpellId == _lastMainSpellId;

        var candidates = new List<ActionCandidate>(_observed.Count);
        foreach (var ((slot, spellId), observation) in _observed)
        {
            if (refreshMain && slot == (int)Slot.Main && spellId == soleMainSpellId)
                observation.LastSeenMs = nowMs;

            var included = ttlMs < 0
                ? observation.Present
                : observation.Present || nowMs - observation.LastSeenMs < ttlMs;
            if (!included) continue;
            var enabled = slot < slotEnabled.Length && slotEnabled[slot];
            candidates.Add(new ActionCandidate(
                (Slot)slot, observation.Stroke, enabled,
                Actionable: !MovementGuard.IsMovementStroke(observation.Stroke),
                observation.FirstSeenMs, observation.FirstSeenMs,
                observation.LastPressedMs, observation.PressedSinceChange,
                spellId));
        }
        candidates.Sort(static (a, b) =>
        {
            var bySlot = ((int)a.Slot).CompareTo((int)b.Slot);
            return bySlot != 0 ? bySlot : a.SpellId.CompareTo(b.SpellId);
        });
        return candidates.ToArray();
    }

    /// <summary>
    /// The Main slot's tracked spell id when the last-seen set holds exactly
    /// one Main identity, else 0. T3's no-downtime refresh is limited to that
    /// sole candidate (a second identity means the bridge switched spells, so
    /// the old one must be allowed to expire).
    /// </summary>
    private int SoleMainSpellId()
    {
        var found = 0;
        foreach (var (slot, spellId) in _observed.Keys)
        {
            if (slot != (int)Slot.Main) continue;
            if (found != 0) return 0;
            found = spellId;
        }
        return found;
    }

    private void Prune(long nowMs)
    {
        if (_observed.Count == 0) return;
        List<(int, int)>? dead = null;
        foreach (var (key, observation) in _observed)
        {
            if (observation.Present || nowMs - observation.LastSeenMs <= PruneAfterMs) continue;
            (dead ??= []).Add(key);
        }
        if (dead is null) return;
        foreach (var key in dead) _observed.Remove(key);
    }

    /// <summary>Drops all history (engine Start; a fresh session must not inherit stale timestamps).</summary>
    public void Reset()
    {
        _observed.Clear();
        _lastUpdateMs = 0;
        _measuredTickMs = 0;
        _lastHeartbeat = -1;
        _hasHeartbeat = false;
        _frameFresh = false;
        _lastMainSpellId = 0;
    }
}
