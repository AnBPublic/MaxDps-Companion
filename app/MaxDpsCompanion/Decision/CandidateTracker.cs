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

    /// <summary>
    /// Candidate lifetime: <c>1.5 * N * dwell * tickMs</c>. A candidate seen at
    /// least once per full rotation stays inside this window; one that the
    /// bridge has stopped offering (the slot rotated to a different pool, the
    /// ability went on cooldown) drops out and is never pressed again.
    /// </summary>
    public static long DefaultTtlMs =>
        (long)Math.Ceiling(1.5 * RotationCandidates * RotationDwellTicks * RenderedTickMs);

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

    /// <summary>
    /// Records one decoded frame. Every present <c>(slot, spellId)</c> pair is
    /// stamped now and marked present; pairs the frame no longer carries keep
    /// their last-seen stamp so the TTL snapshot can still offer a candidate the
    /// bridge rotated out moments ago. A same-spell different-stroke swap (a new
    /// keybind for the same identity) starts a new observation window.
    /// </summary>
    public void Update(BridgeFrame frame, long nowMs)
    {
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
        var candidates = new List<ActionCandidate>(_observed.Count);
        foreach (var ((slot, spellId), observation) in _observed)
        {
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
    public void Reset() => _observed.Clear();
}
