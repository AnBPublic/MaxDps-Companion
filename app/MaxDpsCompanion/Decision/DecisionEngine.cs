namespace MaxDpsCompanion;

/// <summary>
/// Deterministic rotation decision layer. A pure function of the decoded
/// frame, the candidate tracker snapshot and settings — no I/O, no randomness,
/// no clock reads (the caller passes NowMs).
///
/// It never invents game state: every input comes from the pixel protocol
/// (slot suggestions, in-combat / on-GCD / has-target flags) or from the
/// companion's own press history. It never compares, converts or manipulates
/// Blizzard secret values — the decoded frame contains plain keybinds only.
///
/// The evaluator produces an ORDER; the existing per-slot send gates (slot
/// enabled, GCD, movement bind, physical hold, focus) stay authoritative in
/// <c>RotationEngine.TrySendOne</c>. With IntelligenceEnabled off the result
/// is <see cref="FallbackOrder"/> verbatim, so behaviour is unchanged.
/// </summary>
internal static class DecisionEngine
{
    /// <summary>
    /// Legacy priority (intelligence disabled): Main first — the exact array
    /// the send loop shipped before the decision layer existed. DO NOT
    /// reorder: tests pin this as the fallback contract.
    /// </summary>
    internal static readonly Slot[] FallbackOrder =
        [Slot.Main, Slot.Offensive, Slot.Interrupt, Slot.Defensive, Slot.Consumable, Slot.Trinket];

    /// <summary>
    /// Enabled priority: the interrupt is off the GCD and time-critical, the
    /// solo self-sustain slot is policy-gated (it is only a USE below the
    /// configured HP thresholds, so it may preempt the rotation exactly like
    /// it does on the scheduler path), then the core rotation, then the
    /// optional cooldowns and items. The companion-only Mobility slot sits
    /// behind the defensive and ahead of the optional cooldowns; its policy
    /// only lets a confirmed out-of-melee gap closer through.
    ///
    /// The relative order of the six MaxDps slots is unchanged from the v1.4
    /// contract (Interrupt &lt; Main &lt; Defensive &lt; Offensive &lt;
    /// Consumable &lt; Trinket); the two companion slots were inserted — see
    /// <see cref="FallbackOrder"/> for the intelligence-off array, which stays
    /// byte-identical to the pre-intelligence engine.
    /// </summary>
    internal static readonly Slot[] EnabledOrder =
        [Slot.Interrupt, Slot.SelfHeal, Slot.Main, Slot.Defensive, Slot.Mobility, Slot.Offensive, Slot.Consumable, Slot.Trinket];

    public static DecisionResult Evaluate(DecisionContext ctx)
    {
        // 1. Normalize: enabled candidates only, in enabled priority order.
        var bySlot = new ActionCandidate?[PixelProtocol.SlotCount];
        foreach (var candidate in ctx.Candidates) bySlot[(int)candidate.Slot] = candidate;

        var live = new List<ActionCandidate>(PixelProtocol.SlotCount);
        foreach (var slot in EnabledOrder)
        {
            if (bySlot[(int)slot] is { Enabled: true } candidate) live.Add(candidate);
        }
        if (live.Count == 0)
            return new DecisionResult([], null, DecisionReason.NoCandidate, 0, false);

        // 2. Duplicate collapse: two categories pointing at the same physical
        //    stroke would press it twice. Keep the highest-ranked occurrence
        //    (the list is already rank-ordered).
        var unique = new List<ActionCandidate>(live.Count);
        foreach (var candidate in live)
        {
            var duplicate = false;
            foreach (var kept in unique)
            {
                if (kept.Stroke == candidate.Stroke) { duplicate = true; break; }
            }
            if (!duplicate) unique.Add(candidate);
        }

        // 3. Stale demotion: a candidate already pressed since it last changed,
        //    and unchanged for the whole stale window, is a stuck suggestion —
        //    move it behind any fresh alternative. Never removed: if every
        //    candidate is stale the order is unchanged (no deadlock), and the
        //    send gates still apply.
        var fresh = new List<ActionCandidate>(unique.Count);
        var stale = new List<ActionCandidate>(unique.Count);
        foreach (var candidate in unique)
        {
            // R2 (sustain-cd): a ready SelfHeal is never stale-demoted — after
            // a cooldown the same spell is re-suggested with unchanged slot
            // content, and the pressed-since-change bookkeeping must not
            // swallow the new opportunity (parity with ActionScheduler).
            if (candidate.Slot is not (Slot.SelfHeal or Slot.Main) && candidate.IsStale(ctx.NowMs, ctx.StaleAfterMs))
                stale.Add(candidate);
            else fresh.Add(candidate);
        }
        var demoted = stale.Count > 0 && fresh.Count > 0;
        var order = new List<ActionCandidate>(unique.Count);
        order.AddRange(fresh);
        order.AddRange(stale);

        // 4. Reason + confidence for the head. GCD is a temporary state: only
        //    the off-GCD interrupt can ride it (the send loop enforces that;
        //    the reason just names it).
        var head = order[0];
        var reason = ReasonFor(head.Slot);
        var confidence = ConfidenceFor(reason);
        if (!head.Actionable) confidence -= 20;
        if (head.Slot != Slot.Main && head.IsStale(ctx.NowMs, ctx.StaleAfterMs)) confidence -= 40;
        if (ctx.OnGcd && head.Slot != Slot.Interrupt)
        {
            reason = DecisionReason.GcdHold;
            confidence = Math.Min(confidence, 20);
        }

        var slots = new Slot[order.Count];
        for (var i = 0; i < order.Count; i++) slots[i] = order[i].Slot;
        return new DecisionResult(slots, head.Slot, reason, Math.Clamp(confidence, 0, 100), demoted);
    }

    private static DecisionReason ReasonFor(Slot slot) => slot switch
    {
        Slot.Interrupt => DecisionReason.InterruptUrgency,
        Slot.Defensive => DecisionReason.DefensiveUrgency,
        Slot.SelfHeal => DecisionReason.SelfSustain,
        Slot.Main => DecisionReason.MainRotation,
        Slot.Mobility => DecisionReason.MobilityUplift,
        Slot.Offensive => DecisionReason.OffensiveCooldown,
        Slot.Consumable => DecisionReason.Consumable,
        Slot.Trinket => DecisionReason.Trinket,
        _ => DecisionReason.NoCandidate,
    };

    private static int ConfidenceFor(DecisionReason reason) => reason switch
    {
        DecisionReason.InterruptUrgency => 95,
        DecisionReason.DefensiveUrgency => 80,
        DecisionReason.SelfSustain => 75,
        DecisionReason.MainRotation => 70,
        DecisionReason.MobilityUplift => 65,
        DecisionReason.OffensiveCooldown => 60,
        DecisionReason.Consumable => 55,
        DecisionReason.Trinket => 50,
        _ => 50,
    };
}
