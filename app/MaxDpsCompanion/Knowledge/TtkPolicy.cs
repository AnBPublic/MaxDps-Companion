namespace MaxDpsCompanion;

/// <summary>
/// Frozen TTK gate thresholds and tier defaults (v3.6.0 "dying-trash guard").
/// The estimator owns the *measurement*; this class owns the *decision
/// constants* so the providers stay byte-identical and the defaults are
/// testable in one place.
///
/// A valid estimate always fails open is NOT a rule: an invalid estimate skips
/// every TTK gate (the ability keeps its pre-TTK verdict). Holding on an unknown
/// target is exactly the "holding cooldowns too long" DPS loss the feature
/// exists to avoid.
///
/// v3.6.0 changes (architect spec §2, <c>docs/plans/2026-10-01-ttk-intelligence.md</c>):
/// <list type="bullet">
/// <item><description>Thresholds retuned (MajorBurst 15, Execute 3, AoeOnly/Hybrid 0)
/// and short-cooldown (&lt;=45 s) abilities drop to 3 s. Curated
/// <see cref="AbilityDefinition.MinTtkSec"/> still wins.</description></item>
/// <item><description>The waste guard now also applies to a *provisional*
/// estimate, but only for the majors (MajorBurst/Transformation/Summon/WindowDriven);
/// it never holds minors on a provisional rate.</description></item>
/// <item><description>A documented grace hold protects a fast trash pack:
/// unknown TTK + <see cref="CombatContext.FastPackLatch"/> + age&lt;4 s holds a
/// major burst. <c>[TimeToKill] Fallback=ConserveMajors</c> applies the same
/// hold to any unknown-TTK major without needing the latch.</description></item>
/// <item><description>Kill-secure and execute carve-outs let a confirmed
/// finishing ability bypass the waste guard.</description></item>
/// <item><description>Dying-target defensive gating is now a per-tier / scope
/// lookup (Solo Minor 6 / Major 10 / Immunity 15; Group Minor only) instead of
/// the single <see cref="DyingTargetSec"/> constant.</description></item>
/// </list>
/// All rules remain pure C# over the decoded context; live retail behaviour
/// stays OWED (offline replay parity only).
/// </summary>
internal static class TtkPolicy
{
    // ---- Solo defensive dying-target thresholds (per tier, v3.6.0) ----------

    /// <summary>Solo Minor (or untiered) defensive hold when the target dies sooner than this.</summary>
    public const double SoloMinorDyingTargetSec = 6.0;

    /// <summary>Solo Major defensive hold when the target dies sooner than this.</summary>
    public const double SoloMajorDyingTargetSec = 10.0;

    /// <summary>Solo Immunity hold when the target dies sooner than this.</summary>
    public const double SoloImmunityDyingTargetSec = 15.0;

    /// <summary>Group Minor defensive hold window; group anchors also require a valid rate, below-Orange urgency and the fast-pack latch.</summary>
    public const double GroupMinorDyingTargetSec = 4.0;

    /// <summary>
    /// Legacy single-value Solo defensive window (v3.2.0). Kept as the Solo
    /// Minor baseline so existing callers/replays compile; the per-tier lookup
    /// is <see cref="DyingTargetHolds(AbilityDefinition, CombatContext, bool, bool, int, int)"/>.
    /// </summary>
    public const double DyingTargetSec = SoloMinorDyingTargetSec;

    /// <summary>A second full cooldown use needs this many cooldowns plus its duration.</summary>
    public const int TwoUsesFactor = 2;

    /// <summary>The reason text emitted when the fast-pack grace hold fires (spec §2).</summary>
    public const string GraceHoldReason = "fast pack, waiting for TTK";

    /// <summary>Default unknown-TTK fallback for <see cref="GraceHoldHolds"/> (documented fail-open).</summary>
    public const TtkFallback DefaultFallback = TtkFallback.FailOpen;

    /// <summary>
    /// Default minimum TTK per offensive usage (§3.3), used when the ability has
    /// no curated <see cref="AbilityDefinition.MinTtkSec"/>. A value of 0 means
    /// "never gate on TTK" (AoE-only and the defensive-offensive hybrid path).
    /// </summary>
    public static double DefaultMinTtkSec(OffensiveUsage usage) => usage switch
    {
        OffensiveUsage.MajorBurst => 15,
        OffensiveUsage.Transformation => 20,
        OffensiveUsage.Summon => 20,
        OffensiveUsage.WindowDriven => 10,
        OffensiveUsage.ShortCooldown => 5,
        OffensiveUsage.ProcDriven => 5,
        OffensiveUsage.AoeOnly => 0,
        OffensiveUsage.SingleTargetOnly => 5,
        OffensiveUsage.ResourceDriven => 5,
        OffensiveUsage.Execute => 3,
        OffensiveUsage.DefensiveOffensiveHybrid => 0,
        _ => 10,
    };

    /// <summary>
    /// The effective minimum TTK for an ability. A curated
    /// <see cref="AbilityDefinition.MinTtkSec"/> always wins; otherwise the
    /// usage default applies, with short-cooldown abilities (&lt;=45 s) using
    /// <c>min(threshold, 3)</c> so a cheap button is not over-conserved.
    /// </summary>
    public static double MinTtkSec(AbilityDefinition ability)
    {
        if (ability.MinTtkSec is { } curated) return curated;
        var threshold = DefaultMinTtkSec(ability.OffensiveUsage);
        if (ability.OffensiveUsage == OffensiveUsage.ShortCooldown
            && ability.CooldownMs > 0 && ability.CooldownMs <= 45_000)
            return Math.Min(threshold, 3.0);
        return threshold;
    }

    /// <summary>
    /// The majors whose provisional rate may gate the waste guard. Provisional
    /// input is noisy; only these high-opportunity-cost abilities are held on it,
    /// and no minor is ever held on a provisional rate (spec §2/§3).
    /// </summary>
    public static bool ProvisionalWasteEligible(AbilityDefinition ability) =>
        ability.OffensiveUsage is OffensiveUsage.MajorBurst
            or OffensiveUsage.Transformation
            or OffensiveUsage.Summon
            or OffensiveUsage.WindowDriven;

    /// <summary>
    /// Kill-secure exception (spec §2): a genuinely dying, long-lived target with
    /// a short-but-real TTK — pop a confirmed major/summon to secure the kill.
    /// Requires a *valid* estimate; a provisional or young target never qualifies.
    /// </summary>
    public static bool KillSecureBypass(AbilityDefinition ability, CombatContext ctx) =>
        (ability.OffensiveUsage is OffensiveUsage.MajorBurst or OffensiveUsage.Summon
            || ability.KillSecure)
        && ctx.TtkValid
        && ctx.TargetAgeSec >= 20.0
        && ctx.TargetHpFrac <= 0.35
        && ctx.TtkSec is >= 3.0 and <= 20.0;

    /// <summary>
    /// Execute carve-out (spec §2): an execute-favored ability in execute range
    /// bypasses the waste guard once its TTK is at least 3 s (or unknown).
    /// Below 3 s the guard still holds — the cooldown cannot pay for itself.
    /// </summary>
    public static bool ExecuteWasteBypass(AbilityDefinition ability, CombatContext ctx) =>
        ExecuteRange(ability, ctx)
        && (ctx.TtkSec >= 3.0 || !ctx.EffectiveTtkKnown);

    /// <summary>
    /// T1 waste guard (v3.6.0): a valid OR provisional TTK below the minimum
    /// means the cooldown will not pay off. Provisional only gates the majors
    /// (<see cref="ProvisionalWasteEligible"/>). The execute and kill-secure
    /// carve-outs are applied first and fail the rule open.
    /// </summary>
    public static bool WasteGuardHolds(AbilityDefinition ability, CombatContext ctx)
    {
        if (ExecuteWasteBypass(ability, ctx)) return false;
        if (KillSecureBypass(ability, ctx)) return false;

        var need = MinTtkSec(ability);
        if (ctx.TtkValid && ctx.TtkSec < need) return true;
        if (ctx.TtkProvisional && ProvisionalWasteEligible(ability) && ctx.TtkSec < need) return true;
        return false;
    }

    /// <summary>
    /// Grace hold (spec §2, the only fail-open exception): a major burst on a
    /// fast trash pack, before any estimate exists. Fires when the usage is a
    /// major, no estimate (valid or provisional) exists, the fast-pack latch is
    /// set, and the target is younger than 4 s. With
    /// <paramref name="fallback"/> = <see cref="TtkFallback.ConserveMajors"/> the
    /// same hold applies to any unknown-TTK major and needs no latch.
    /// Never fires without the latch under the default fail-open fallback.
    /// </summary>
    public static bool GraceHoldHolds(
        AbilityDefinition ability,
        CombatContext ctx,
        TtkFallback fallback = DefaultFallback)
    {
        if (ability.OffensiveUsage is not (OffensiveUsage.MajorBurst
            or OffensiveUsage.Transformation
            or OffensiveUsage.Summon))
            return false;
        if (ctx.EffectiveTtkKnown) return false;
        if (fallback == TtkFallback.ConserveMajors) return true;
        return ctx.FastPackLatch && ctx.TargetAgeSec < 4.0;
    }

    /// <summary>
    /// T2 two-uses: the fight is long enough for a second full cooldown (plus
    /// its duration), so a paired-window hold must not block the first press.
    /// Requires a curated cooldown; an absent cooldown skips the rule.
    /// </summary>
    public static bool TwoUsesAvailable(AbilityDefinition ability, CombatContext ctx) =>
        ctx.TtkValid
        && ability.CooldownMs > 0
        && ctx.TtkSec >= (TwoUsesFactor * (double)ability.CooldownMs + ability.DurationMs) / 1000.0;

    /// <summary>
    /// T3 execute: the burst is confirmed favored in execute range and the
    /// target is at/below the curated threshold. Bypasses the pairing hold
    /// (and, via <see cref="ExecuteWasteBypass"/>, the waste guard).
    /// </summary>
    public static bool ExecuteRange(AbilityDefinition ability, CombatContext ctx) =>
        ability.ExecuteFavored
        && ability.ExecuteBelowPct is { } pct
        && ctx.TargetHpValid
        && ctx.TargetHpPct <= pct;

    /// <summary>
    /// T4 dying target (v3.6.0 per-tier / scope lookup). Solo: Minor 6 s,
    /// Major 10 s, Immunity 15 s. Group: only Minor is gated, and only on a
    /// valid rate below 4 s with the fast-pack latch and urgency below Orange —
    /// enemy count is unobservable so majors/immunities are never conserved.
    /// Emergency always overrides. Solo ladder-band carve-out: a Major inside
    /// <paramref name="soloMajorHpPct"/> (or an Immunity inside
    /// <paramref name="soloImmunityHpPct"/>) is needed now and is not held.
    /// </summary>
    public static bool DyingTargetHolds(
        AbilityDefinition ability,
        CombatContext ctx,
        bool soloEnabled,
        bool emergency,
        int soloMajorHpPct = 50,
        int soloImmunityHpPct = 30)
    {
        if (emergency) return false;

        var isImmunity = ability.Tier == DefensiveTier.Immunity
            || ability.Purpose == AbilityPurpose.Immunity;
        var isMajor = !isImmunity && ability.Tier == DefensiveTier.Major;

        if (!soloEnabled)
        {
            // Group: majors/immunities are never gated; Minor only, and only
            // when the pack is confirmed fast and the target is nearly dead.
            if (isMajor || isImmunity) return false;
            return ctx.TtkValid
                && ctx.TtkSec < GroupMinorDyingTargetSec
                && ctx.FastPackLatch
                && ctx.DefensiveUrgency < DefensiveUrgency.Orange;
        }

        // Solo ladder-band carve-out: inside the tier's own escalation band the
        // defensive is needed, so it is never conserved for a dying target.
        if (ctx.HpValid)
        {
            if (isImmunity && ctx.HpPct <= soloImmunityHpPct) return false;
            if (isMajor && ctx.HpPct <= soloMajorHpPct) return false;
        }

        var holdSec = isImmunity ? SoloImmunityDyingTargetSec
            : isMajor ? SoloMajorDyingTargetSec
            : SoloMinorDyingTargetSec;
        return ctx.TtkValid && ctx.TtkSec < holdSec;
    }

    /// <summary>
    /// Legacy v3.2.0 Solo-only dying-target hold retained for existing callers
    /// (provider wiring is upgraded by the CandidateProviders task). Equivalent
    /// to the Solo Minor tier: non-emergency, valid TTK below
    /// <see cref="DyingTargetSec"/>.
    /// </summary>
    public static bool DyingTargetHolds(CombatContext ctx, bool soloEnabled, bool emergency) =>
        soloEnabled && !emergency && ctx.TtkValid && ctx.TtkSec < DyingTargetSec;
}
