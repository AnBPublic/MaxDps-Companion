namespace MaxDpsCompanion;

/// <summary>
/// Companion-side CC opt-in gate (v3.4.0 CC appendix). Default OFF — CC is an
/// explicit opt-in (Solo-like safety), so a fresh install or a manually-built
/// <see cref="PolicyOptions"/> never auto-fires crowd control.
///
/// Effective authority is intended to be <c>companion AND addon</c> with an
/// addon OFF winning. The addon-side key exists (<c>MaxDpsBridgeDB.Toggles.CC</c>,
/// missing = ON, restrict-only) but no wire field carries it; until the bridge
/// gates its CC encoding on it, the companion uses this companion-side gate.
/// That cross-surface wiring is OWED live (see HANDOVER.md), not claimed here.
/// </summary>
internal static class CrowdControlGate
{
    /// <summary>Companion-side CC auto-use switch. Default false (opt-in).</summary>
    public static bool Enabled { get; set; }

    public static void Configure(bool enabled) => Enabled = enabled;

    /// <summary>Test hygiene: return to the opt-in default.</summary>
    public static void Reset() => Enabled = false;
}

/// <summary>
/// The result of asking the DR ladder what the NEXT application would do.
/// <see cref="Applications"/> is the number already applied inside the current
/// window; <see cref="ExpectedDurationMs"/> is the duration of the next one
/// after the 12.1.5 multiplier. <see cref="Immune"/> is the conservative
/// PvP rule (two applications -> the third is immune).
/// </summary>
internal readonly record struct CcDrState(
    int Applications,
    double Multiplier,
    int ExpectedDurationMs,
    bool Immune,
    long WindowEndsMs)
{
    public bool TooWeak(int minimumUsefulMs) =>
        ExpectedDurationMs > 0 && ExpectedDurationMs < minimumUsefulMs;
}

/// <summary>
/// The companion's OWN recent-CC memory (never a read of the target's live DR
/// state, which Midnight does not expose). It models the 12.1.5 ladder:
///
///  * the DR window starts when the application ENDS and lasts 20 s
///    (12.1.0 changed 16 s -> 20 s; 12.1.5 did not change it again);
///  * each subsequent application halves: 1, 1/2, 1/4, 1/8, then the 1/16
///    floor;
///  * PvP is immune after two applications -> a third inside the window is
///    never generated;
///  * PvE never becomes fully immune since 12.0, but a stun whose expected
///    duration falls below <see cref="MinimumUsefulStunMs"/> is not worth the
///    combo points / global cooldown and is held.
///
/// The companion cannot see other players' applications. It therefore uses the
/// conservative PvP two-application rule for every target; deep-DR stuns are
/// low value in PvE too. Unknown/zero-duration data fails open.
/// </summary>
internal sealed class CrowdControlDiminishing
{
    /// <summary>
    /// 12.1.5 reset window. The 12.1.0 patch raised it from 16 s to 20 s.
    /// </summary>
    public const long DefaultWindowMs = 20_000;

    /// <summary>PvP is fully immune after this many applications (12.1.0+).</summary>
    public const int PvpImmuneAfter = 2;

    /// <summary>
    /// A stun shorter than this is not considered a useful interrupt. A 2 s
    /// Kidney Shot is 1 s on the second application and 0.5 s on the third.
    /// </summary>
    public const int MinimumUsefulStunMs = 1000;

    private sealed class Track
    {
        public int Applications { get; set; }
        public long UntilMs { get; set; }
        public long LastEndMs { get; set; }
    }

    private readonly Dictionary<CcDrCategory, Track> _tracks = new();

    public long WindowMs { get; init; } = DefaultWindowMs;

    public int MinimumUsefulMs { get; init; } = MinimumUsefulStunMs;

    /// <summary>What the next application would do at <paramref name="nowMs"/>.</summary>
    public CcDrState Evaluate(
        CcDrCategory category, long nowMs, int baseDurationMs, bool pvp = true)
    {
        if (category == CcDrCategory.Unknown || baseDurationMs <= 0)
            return new CcDrState(0, 1.0, baseDurationMs, false, 0);

        if (!_tracks.TryGetValue(category, out var track) || nowMs >= track.UntilMs)
        {
            if (track is not null) _tracks.Remove(category);
            return new CcDrState(0, 1.0, baseDurationMs, false, 0);
        }

        var applications = track.Applications;
        var multiplier = MultiplierFor(applications);
        var expected = ExpectedDuration(baseDurationMs, applications);
        var immune = pvp && applications >= PvpImmuneAfter;
        return new CcDrState(applications, multiplier, expected, immune, track.UntilMs);
    }

    /// <summary>Records one application the companion (or the player) just made.</summary>
    public void NoteUse(
        CcDrCategory category, long nowMs, int baseDurationMs, bool pvp = true)
    {
        if (category == CcDrCategory.Unknown || baseDurationMs <= 0) return;

        // Evaluate first so an expired window restarts at application 1.
        var current = Evaluate(category, nowMs, baseDurationMs, pvp);
        var applications = current.Applications + 1;
        var expected = ExpectedDuration(baseDurationMs, applications - 1);
        var end = nowMs + expected;
        _tracks[category] = new Track
        {
            Applications = applications,
            LastEndMs = end,
            UntilMs = end + WindowMs,
        };
    }

    /// <summary>Forget one category (target loss / out of combat).</summary>
    public void Clear(CcDrCategory category)
    {
        if (category != CcDrCategory.Unknown) _tracks.Remove(category);
    }

    public void Reset() => _tracks.Clear();

    private static double MultiplierFor(int applications) => applications switch
    {
        <= 0 => 1.0,
        1 => 0.5,
        2 => 0.25,
        3 => 0.125,
        _ => 1.0 / 16.0,
    };

    private static int ExpectedDuration(int baseDurationMs, int applications)
    {
        var multiplier = MultiplierFor(applications);
        var expected = (int)Math.Round(baseDurationMs * multiplier);
        return Math.Max(1, expected);
    }
}

/// <summary>
/// The single CC entry point consulted by <see cref="PolicyEvaluator"/> (one
/// call-site). It returns a fully stamped decision only when the CC appendix
/// applies — companion CC gate ON, a curated AUTO-ELIGIBLE row for the current
/// class/spec, and a meaningful registry status. In every other case it returns
/// null, so the pre-existing path (manual-by-design / MaxDps authority) runs
/// byte-identically.
///
/// v4.2.0 safety: AutoEligible is now only the interrupt-fallback role
/// (single-target stun/silence). Blind/Gouge/Sap/Polymorph/Fear/Hex and the
/// other break-on-damage tools are manual-only and are never generated here,
/// even when the global CC toggle is ON.
/// </summary>
internal static class CrowdControlVetoes
{
    /// <summary>
    /// The companion's own DR memory. Non-null by default (safe: empty ⇒ fail
    /// open). Replaceable/clearable for tests. Engine-side recording is
    /// inherently local to the provider (it records on its own Use decision),
    /// so no scheduler wiring is required.
    /// </summary>
    public static CrowdControlDiminishing? Memory { get; set; } = new();

    /// <summary>
    /// Self-observed \"stun ineffective\" latch. Set by the scheduler when a stun
    /// sent on a confirmed cast leaves the target still casting ~800 ms later
    /// (the game's immune flag is unobservable). Global rather than per-policy
    /// memory so it also covers the legacy (scheduler-off) path. Cleared on
    /// target loss / out of combat / reset.
    /// </summary>
    public static bool StunIneffective { get; private set; }

    public static void NoteStunIneffective() => StunIneffective = true;

    public static void ClearStunIneffective() => StunIneffective = false;

    /// <summary>Target loss / out of combat: forget DR applications and the latch.</summary>
    public static void ResetForTarget()
    {
        Memory?.Reset();
        StunIneffective = false;
    }

    /// <summary>Returns a stamped CC decision, or null when the normal path must run.</summary>
    public static PolicyDecision? Evaluate(PolicyInput input, AbilityDefinition ability, AbilityCatalog catalog)
    {
        if (!CrowdControlGate.Enabled) return null;
        if (ability.Status is IntelligenceStatus.Incomplete or IntelligenceStatus.Unknown
            or IntelligenceStatus.UnsafeToAutomate) return null;

        // An explicit user OFF (or Manual/Never mode) stays absolute: the CC
        // gate can only ever *add* eligibility to a default-off row, never
        // override a user's decision. Returning null lets the normal path Skip.
        if (input.Options.Abilities is { } policy)
        {
            var mode = policy.ModeOf(ability.SpellId);
            if (mode is UserAbilityMode.Never or UserAbilityMode.Manual) return null;
            if (policy.OverrideOf(ability.SpellId) == false) return null;
        }

        var entry = catalog.CrowdControlFor(input.Context.Class, input.Context.Spec, ability.SpellId);
        // v4.2.0: AutoEligible is now the interrupt-fallback role only. The
        // global CC toggle IS the explicit opt-in for these stun/silence rows,
        // even though abilities.json marks many of them NeverAutomatic (that
        // flag keeps them out of the ordinary rotation path; it does not veto
        // the dedicated interrupt-fallback appendix). Manual/proactive rows
        // return null here and can never be auto-generated.
        if (entry is null || !entry.AutoEligible) return null;

        var pinput = new ProviderInput
        {
            Input = input,
            Ability = ability,
            Catalog = catalog,
            Range = input.Context.SlotRange[(int)input.Slot],
            Fallback = input.Options.TimeToKillFallback,
        };
        return CandidateProviders.CrowdControl.Evaluate(pinput);
    }

    /// <summary>
    /// DR / immunity veto shared by the curated CC provider and the delegated
    /// Interrupt path (Kidney Shot, Storm Bolt, ...). Returns null when the
    /// candidate is not a curated interrupt-fallback row.
    /// </summary>
    public static PolicyDecision? InterruptFallbackVeto(
        PolicyInput input, AbilityDefinition ability, AbilityCatalog catalog)
    {
        if (input.Slot != Slot.Interrupt) return null;
        var entry = catalog.CrowdControlFor(input.Context.Class, input.Context.Spec, ability.SpellId);
        if (entry is null || !entry.AutoEligible) return null;

        // Never a blind stun: the delegated path may see an old wire frame with
        // the cast state UNKNOWN. For an interrupt-fallback row, only a
        // confirmed cast is allowed.
        if (entry.Kind is CcKind.Stun or CcKind.Silence
            && input.Context.TargetCasting != TriState.Yes)
            return PolicyDecision.Hold(
                $"casting-only crowd control {entry.Name}; no target cast observed");

        if (input.Context.TargetCastInterruptible == TriState.No)
            return PolicyDecision.Skip("observed target cast is not interruptible");

        // The 12.1.5 rule is conservative across PvP/PvE because the wire has
        // no target-is-player bit: after two applications, no third stun.
        if (entry.Dr == CcDrCategory.Stun && StunIneffective)
            return PolicyDecision.Hold(
                "stun was ineffective on this target; not re-stunning (target may be immune)");

        if (Memory is { } memory && entry.BaseDurationMs > 0)
        {
            var state = memory.Evaluate(entry.Dr, input.NowMs, entry.BaseDurationMs, pvp: true);
            if (state.Immune)
                return PolicyDecision.Hold(
                    $"target immune to {entry.Kind} (DR {state.Applications}/{CrowdControlDiminishing.PvpImmuneAfter})");
            if (entry.Dr == CcDrCategory.Stun && state.TooWeak(memory.MinimumUsefulMs))
                return PolicyDecision.Hold(
                    $"stun DR too deep ({state.ExpectedDurationMs} ms < {memory.MinimumUsefulMs} ms)");
        }

        return null;
    }

    /// <summary>Records a CC application for the DR ladder.</summary>
    internal static void NoteUsed(
        CcDrCategory category, long nowMs, int baseDurationMs)
    {
        Memory?.NoteUse(category, nowMs, baseDurationMs, pvp: true);
    }

}
