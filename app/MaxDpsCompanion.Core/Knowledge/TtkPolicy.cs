namespace MaxDpsCompanion;

/// <summary>
/// Frozen policy thresholds (v3.6.0 "dying-trash guard", slimmed in v3.7.9).
/// The estimator owns the *measurement*; this class owns the *decision
/// constants* so the providers stay byte-identical and the defaults are
/// testable in one place.
///
/// v3.7.9 (MaxDps authority): the companion's offensive TTK *conservation*
/// cluster was removed. A MaxDps-offered or companion gap-fill offensive is
/// used unless a game-truth/structural gate applies, so the old T1 waste guard,
/// fast-pack grace hold, warmup hold and adaptive-history branch no longer
/// exist. What remains here:
/// <list type="bullet">
/// <item><description><see cref="SubFiftyBypass"/> — a sub-60s rotational
/// ability is never treated as a major (still consumed by the Burst preset).</description></item>
/// <item><description><see cref="TwoUsesAvailable"/> / <see cref="ExecuteRange"/>
/// — the paired-window second-use / execute bypass.</description></item>
/// <item><description><see cref="DyingTargetHolds(AbilityDefinition, CombatContext, bool, bool, int, int)"/>
/// — the defensive dying-target gate.</description></item>
/// </list>
///
/// <see cref="MinTtkSec"/> / <see cref="DefaultMinTtkSec"/> are retained only to
/// render the curated values in the ability inspector (<c>--ability-info</c>);
/// the v3.7.9 policy no longer enforces a minimum TTK. All rules remain pure
/// C# over the decoded context; live retail behaviour stays OWED (offline
/// replay parity only).
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

    /// <summary>
    /// Warmup window default (seconds) mirrored from <c>[TimeToKill] WarmupSec</c>.
    /// Retained for replay/telemetry schema; not enforced since 3.7.9 (the
    /// offensive warmup hold was removed). Kept as the typed default for
    /// <c>AppSettings.TimeToKillWarmupSec</c> so the recorded <c>ttkw</c> field
    /// and older recordings still parse.
    /// </summary>
    public const double DefaultWarmupSec = 3.0;

    /// <summary>Default unknown-TTK fallback mirrored from <c>[TimeToKill] Fallback</c>; retained for replay/telemetry schema, not enforced since 3.7.9.</summary>
    public const TtkFallback DefaultFallback = TtkFallback.FailOpen;

    /// <summary>
    /// Default minimum TTK per offensive usage (§3.3), used when the ability has
    /// no curated <see cref="AbilityDefinition.MinTtkSec"/>. A value of 0 means
    /// "never gate on TTK" (AoE-only and the defensive-offensive hybrid path).
    /// Display/curation only since 3.7.9 — the policy no longer enforces it.
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
    /// The curated/usage-default minimum TTK for an ability. Retained for the
    /// ability inspector (<c>--ability-info</c>) and the curation parse tests;
    /// not enforced by the policy since 3.7.9. A curated
    /// <see cref="AbilityDefinition.MinTtkSec"/> always wins; otherwise the
    /// usage default applies, with short-cooldown abilities (&lt;=45 s) using
    /// <c>min(threshold, 3)</c>.
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
    /// Sub-60s bypass (2026-10-04, all classes/specs): a companion-gated
    /// ability with a known cooldown of 60 s or shorter is a rotational button,
    /// not a major — it must never be conserved by the Burst preset. Curated
    /// warrior <c>cdMs=45000</c> rows (Colossus Smash 167105, Warbreaker 262161,
    /// Demolish 436358, Odyn's Fury 385059, Shield Charge 385952, Demoralizing
    /// Shout 1160) and any other curated sub-60s row therefore fire on the next
    /// tick regardless of target TTK. <c>CooldownMs == 0</c> (unknown /
    /// uncurated) is deliberately <em>not</em> bypassed — fail-closed, never a
    /// blanket bypass of unknowns. Exception: <see cref="OffensiveUsage.Summon"/>
    /// is a true 60 s summon major (e.g. Summon Demonic Tyrant 265187) and stays
    /// gated by the Burst preset, while the 60 s rotational Divine Toll 375576
    /// (<see cref="OffensiveUsage.ShortCooldown"/>) still bypasses.
    /// </summary>
    public static bool SubFiftyBypass(AbilityDefinition ability) =>
        ability != null
        && ability.OffensiveUsage != OffensiveUsage.Summon
        && ability.CooldownMs > 0 && ability.CooldownMs <= 60_000;

    /// <summary>
    /// Default adaptive-need duration factor (v3.7 spec §14). Retained for the
    /// replay/telemetry schema (<c>PolicyOptions.TtkHistoryDurFactor</c>);
    /// the adaptive-need hold itself was removed in 3.7.9.
    /// </summary>
    public const double DefaultNeedDurFactor = 0.5;

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
    /// target is at/below the curated threshold. Bypasses the pairing hold.
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
