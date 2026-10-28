// Cadence 4.6.0: interrupt INSURANCE (policy half).
//
// The class interrupt (slot 6) can be on cooldown, out of range, or suppressed
// because the last kick failed. The bridge may still have a curated
// casting-only stun/silence bound on the same slot-6 key, and Ext5 now carries
// that key plus its spell id. This predicate answers one narrow question: may
// the scheduler press that CC on a live interruptible cast instead of letting
// the cast land?
//
// Safety shape (nothing here invents eligibility):
//  * the toggle is an absolute gate, and it is the FIRST statement - with
//    InsuranceEnabled false the scheduler never calls this file at all, and the
//    4.5.8 output stays byte-identical;
//  * the companion's CC opt-in (CrowdControlGate, default false) is the SECOND
//    gate: insurance is automatic crowd control, so it may never fire on an
//    install where the user turned CC off (the delegated CC provider enforces
//    the same rule);
//  * only a confirmed, interruptible target cast qualifies (never a blind
//    stun on an unobserved/unreadable cast), and the Ext5 duplicate of that
//    state is cross-checked against the core triple so the two wire sources
//    cannot silently drift;
//  * eligibility comes from the curated CrowdControlCatalog rows already
//    marked AutoEligible (the interrupt-fallback role) for the class/spec the
//    frame reports - read through AbilityCatalog.CrowdControlFor;
//  * every veto lives in CrowdControlVetoes.InterruptFallbackVeto (casting-only,
//    not-interruptible, DR immunity, deep-DR stun, StunIneffective latch) and a
//    veto is echoed verbatim as the non-fire reason. This file cannot bypass
//    one;
//  * the kick-failure answer is a passed-in bool: the scheduler owns that
//    memory (one KickMemory per scheduler instance), so this predicate neither
//    reads nor writes it;
//  * CrowdControlVetoes.NoteUsed is deliberately NOT called here. The scheduler
//    records the application only after a real keystroke was sent, so a
//    vetoed/aborted tick can never poison the DR ladder.
//  * CrowdControlVetoes.NoteUsed is deliberately NOT called here. The scheduler
//    records the application only after a real keystroke was sent, so a
//    vetoed/aborted tick can never poison the DR ladder.
//
// Purity: no statics are written, no clock is read (nowMs comes in), so
// --replay reproduces identical verdicts.

namespace MaxDpsCompanion.Core.Knowledge;

/// <summary>
/// One interrupt-insurance verdict. <see cref="Reason"/> is always populated for
/// a non-fire so it can ride telemetry; <see cref="None"/> is the OFF-path value
/// (empty reason: the feature did not even run).
/// </summary>
internal readonly record struct InsuranceDecision(
    bool Fire, int SpellId, KeyStroke Stroke, string Reason,
    CcDrCategory Dr, int BaseDurationMs)
{
    /// <summary>
    /// The OFF-path / no-opinion value. Returned by the single early return when
    /// <see cref="PolicyOptions.InsuranceEnabled"/> is false: no telemetry
    /// reason exists because no predicate branch ran.
    /// </summary>
    public static readonly InsuranceDecision None = new(false, 0, default, "", default, 0);

    /// <summary>A non-fire that names the branch which refused it (telemetry).</summary>
    internal static InsuranceDecision Deny(string reason) =>
        new(false, 0, default, reason, default, 0);
}

/// <summary>
/// The insurance predicate (4.6.0 "Cadence"). Stateless and deterministic; the
/// scheduler supplies everything it observed and records the DR use itself.
/// </summary>
internal static class InterruptInsurance
{
    /// <summary>
    /// Decides whether the Ext5 crowd-control entry may be pressed to stop the
    /// target's live cast.
    /// </summary>
    /// <param name="ctx">The decoded tick context (Ext5 included).</param>
    /// <param name="options">Mirrors <c>[Interrupt]</c>; the toggle is authoritative.</param>
    /// <param name="catalog">The curated knowledge base (CC rows + abilities).</param>
    /// <param name="memory">The caller's own policy memory (recorded by the caller only).</param>
    /// <param name="inCombat">Player in combat this tick.</param>
    /// <param name="hasTarget">A live enemy target exists this tick.</param>
    /// <param name="nowMs">Engine clock, for the veto's DR ladder.</param>
    /// <param name="interruptCandidatePresent">The normal interrupt is a usable candidate this tick.</param>
    /// <param name="interruptSuppressed">KickMemory says the last kick failed (suppression rung).</param>
    internal static InsuranceDecision Evaluate(
        CombatContext ctx, PolicyOptions options, AbilityCatalog catalog,
        PolicyMemory memory, bool inCombat, bool hasTarget, long nowMs,
        bool interruptCandidatePresent, bool interruptSuppressed)
    {
        // 1. OFF: one early return, so the OFF path is provably inert and the
        // scheduler's byte-identical 4.5.8 behaviour is preserved.
        if (!options.InsuranceEnabled) return InsuranceDecision.None;

        // 2. The companion's CC opt-in is the absolute gate for every automatic
        // crowd control, and insurance is one (review m1): the delegated CC
        // appendix already requires CrowdControlGate.Enabled
        // (CrowdControlVetoes.cs:200), so an insurance press that ignored it
        // would fire CC on an install where the user turned CC off. Default is
        // false, which makes insurance inert until the user opts in twice.
        if (!CrowdControlGate.Enabled)
            return InsuranceDecision.Deny("insurance: companion CC is off");

        // 3. A press needs a fight and a target.
        if (!inCombat) return InsuranceDecision.Deny("insurance: not in combat");
        if (!hasTarget) return InsuranceDecision.Deny("insurance: no target");

        // 4. Never act on an all-UNKNOWN pre-Ext5 frame.
        if (!ctx.ContextValid)
            return InsuranceDecision.Deny("insurance: pre-Ext5 frame (context invalid)");

        // 5. Casting-only: a stun/silence is an interrupt substitute, never a
        // blind opener. UNKNOWN is not a confirmation.
        if (ctx.TargetCasting != TriState.Yes)
            return InsuranceDecision.Deny("insurance: no confirmed target cast");

        // 6. Only a confirmed "not interruptible" blocks; UNKNOWN fails open
        // (the bridge may not expose the flag at all).
        if (ctx.TargetCastInterruptible == TriState.No)
            return InsuranceDecision.Deny("insurance: observed target cast is not interruptible");

        // 7. The CC entry must be pressable: a real key AND a real spell id.
        var ext5 = ctx.Ext5;
        if (ext5 is null)
            return InsuranceDecision.Deny("insurance: no Ext5 block");
        if (!ext5.CcValid)
            return InsuranceDecision.Deny("insurance: Ext5 crowd-control entry is not usable (key or spell id missing)");

        // 8. CONSISTENCY of the two wire sources (review m4). Ext5 duplicates
        // the core cast state (cell 52 R/B) so the insurance input is complete
        // when the core nibbles are UNKNOWN; the duplicate must never silently
        // drift from the source it mirrors. A frame that says "no cast" (kind 1)
        // or "not interruptible" (1) in Ext5 is refused even when the core
        // triple is UNKNOWN: the two sources disagree, so the press fails
        // closed instead of trusting whichever one is stale.
        if (ext5.CastKind == 1)
            return InsuranceDecision.Deny("insurance: Ext5 reports no target cast (cast kind none)");
        if (ext5.Interruptible == 1)
            return InsuranceDecision.Deny("insurance: Ext5 reports the target cast is not interruptible");

        // 9. Timing: with a live interrupt candidate the insurance press waits
        // for a cast old enough to be worth covering; no candidate, or a failed
        // kick, fires immediately. CastAgeMinMs == -1 (UNKNOWN/none) falsifies
        // only the age term, never the whole predicate.
        var ageMs = ext5.CastAgeMinMs;
        var minAgeMs = options.InsuranceMinCastAgeMs;
        var ageProven = ageMs >= 0 && ageMs >= minAgeMs;
        if (interruptCandidatePresent && !interruptSuppressed && !ageProven)
            return InsuranceDecision.Deny(ageMs < 0
                ? "insurance: interrupt candidate present and the cast age is unknown"
                : $"insurance: interrupt candidate present and the cast is {ageMs} ms old (< {minAgeMs} ms)");

        // 10a. Curated eligibility only: an AutoEligible row for THIS class/spec.
        var entry = catalog.CrowdControlFor(ctx.Class, ctx.Spec, ext5.CcSpellId);
        if (entry is null)
            return InsuranceDecision.Deny(
                $"insurance: spell {ext5.CcSpellId} is not a curated crowd-control row for {ctx.Class}/{ctx.Spec}");
        if (!entry.AutoEligible)
            return InsuranceDecision.Deny($"insurance: {entry.Name} is not an interrupt-fallback row");

        var ability = catalog.TryGet(ext5.CcSpellId);
        if (ability is null)
            return InsuranceDecision.Deny($"insurance: {entry.Name} has no ability-catalog entry");

        // 10b. Every safety rule (casting-only, DR/immunity, deep-DR, the
        // StunIneffective latch) is the shared veto. A veto is final here.
        var input = new PolicyInput
        {
            Slot = Slot.Interrupt,
            SpellId = ext5.CcSpellId,
            Context = ctx,
            Options = options,
            Memory = memory,
            NowMs = nowMs,
            InCombat = inCombat,
            HasTarget = hasTarget,
        };
        if (CrowdControlVetoes.InterruptFallbackVeto(input, ability, catalog) is { } veto)
            return InsuranceDecision.Deny(veto.Reason);

        var why = !interruptCandidatePresent
            ? "no interrupt candidate"
            : interruptSuppressed
                ? "interrupt suppressed after a failed kick"
                : $"cast age {ageMs} ms";
        return new InsuranceDecision(
            true, ext5.CcSpellId, ext5.CcKey,
            $"insurance: {entry.Name} covers an interruptible cast ({why})",
            entry.Dr, entry.BaseDurationMs);
    }

    /// <summary>
    /// True when <paramref name="spellId"/> is a curated crowd-control row - i.e.
    /// a synthesized insurance press or a wire-delegated CC, never the class
    /// kick. The scheduler uses it to keep the kick-failure memory, the retry
    /// budget and the DR application keyed on the WIRE KICK: a CC that fails says
    /// nothing about the kick (review M1), and a CC id must never advance the
    /// kick's suppression ladder.
    /// </summary>
    internal static bool IsCrowdControlPress(int spellId) =>
        spellId > 0 && CrowdControlCatalog.FindAny(spellId) is not null;
}
