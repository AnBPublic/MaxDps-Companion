namespace MaxDpsCompanion;

/// <summary>
/// Frozen TTK gate thresholds and tier defaults (v3.2.0). The estimator owns
/// the *measurement*; this class owns the *decision constants* so the providers
/// stay byte-identical and the defaults are testable in one place.
///
/// A valid estimate always fails open is NOT a rule: an invalid estimate skips
/// every TTK gate (the ability keeps its pre-TTK verdict). Holding on an unknown
/// target is exactly the "holding cooldowns too long" DPS loss the feature
/// exists to avoid.
///
/// Stream 3 §3.1 review (2026-09-29): the estimator constants (3 s EWMA seed,
/// &gt;10 s unknown reset, &gt;0.12 upward-jump reset, (band+0.5)/15 frac) and the
/// T1–T4 thresholds were re-run against every checked-in replay
/// (<c>ttk-warrior-burst</c> + 7 more): all recompute with 0 decision
/// mismatches and 0 verdict mismatches. No replay produces an outcome metric
/// that a threshold tweak could improve, so the constants are deliberately
/// UNCHANGED — changing them would be an unverifiable live-behaviour claim.
/// Live retail target-dummy / M+ review stays OWED.
/// </summary>
internal static class TtkPolicy
{
    /// <summary>Solo defensive hold when the current target dies sooner than this.</summary>
    public const double DyingTargetSec = 6.0;

    /// <summary>A second full cooldown use needs this many cooldowns plus its duration.</summary>
    public const int TwoUsesFactor = 2;

    /// <summary>
    /// Default minimum TTK per offensive usage (§3.3), used when the ability has
    /// no curated <see cref="AbilityDefinition.MinTtkSec"/>. A value of 0 means
    /// "never gate on TTK" (the defensive-offensive hybrid path).
    /// </summary>
    public static double DefaultMinTtkSec(OffensiveUsage usage) => usage switch
    {
        OffensiveUsage.MajorBurst => 12,
        OffensiveUsage.Transformation => 20,
        OffensiveUsage.Summon => 20,
        OffensiveUsage.WindowDriven => 10,
        OffensiveUsage.ShortCooldown => 5,
        OffensiveUsage.ProcDriven => 5,
        OffensiveUsage.AoeOnly => 5,
        OffensiveUsage.SingleTargetOnly => 5,
        OffensiveUsage.ResourceDriven => 5,
        OffensiveUsage.Execute => 5,
        OffensiveUsage.DefensiveOffensiveHybrid => 0,
        _ => 10,
    };

    /// <summary>The effective minimum TTK for an ability (curated wins, else the usage default).</summary>
    public static double MinTtkSec(AbilityDefinition ability) =>
        ability.MinTtkSec ?? DefaultMinTtkSec(ability.OffensiveUsage);

    /// <summary>T1 waste guard: a valid TTK below the minimum means the cooldown will not pay off.</summary>
    public static bool WasteGuardHolds(AbilityDefinition ability, CombatContext ctx) =>
        ctx.TtkValid && ctx.TtkSec < MinTtkSec(ability);

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
    /// T4 dying target: Solo mode only, non-emergency, and the target dies
    /// sooner than <see cref="DyingTargetSec"/>. A hold saved for a trash mob
    /// is not wasted; the emergency HP path always overrides.
    /// </summary>
    public static bool DyingTargetHolds(CombatContext ctx, bool soloEnabled, bool emergency) =>
        soloEnabled && !emergency && ctx.TtkValid && ctx.TtkSec < DyingTargetSec;
}
