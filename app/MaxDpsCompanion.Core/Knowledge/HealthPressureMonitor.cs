namespace MaxDpsCompanion;

/// <summary>
/// Small bounded observation window for *observed* player health loss.
/// No hidden combat values, enemy-count guesses or DPS estimates are inferred.
/// Midnight secret / curve-only HP explicitly resets this signal to Unknown.
/// Shared by every class and spec through the same scheduler policy memory.
/// </summary>
internal sealed class HealthPressureMonitor
{
    internal const int WindowMs = 1800;
    internal const int MinObservationMs = 250;
    internal const int MinDropPct = 15;
    private readonly List<(long AtMs, int HpPct)> _samples = [];

    public bool FallingFast { get; private set; }
    // v5: advisory, bounded projection from two measured PLAIN HP values.
    // Unknown/curve-only HP, stale samples and out-of-combat reset it.
    public bool CriticalSoon { get; private set; }
    public int? ProjectedHpPct { get; private set; }
    public int ObservedDropPct { get; private set; }
    internal const int ProjectionWindowMs = 1200;
    internal const int ProjectionHorizonMs = 1000;
    internal const int MinimumCriticalDropPct = 20;


    public void Observe(CombatContext context, bool inCombat, long nowMs)
    {
        if (!inCombat || !context.ContextValid || !context.HpValid
            || context.HpSource != HpSource.Plain || context.HpPct is < 0 or > 100)
        {
            Reset();
            return;
        }
        if (_samples.Count > 0 && nowMs < _samples[^1].AtMs)
        {
            Reset();
        }

        _samples.RemoveAll(sample => nowMs - sample.AtMs > WindowMs);
        FallingFast = context.HpPct <= 70 && _samples.Any(sample =>
            nowMs - sample.AtMs >= MinObservationMs
            && sample.HpPct - context.HpPct >= MinDropPct);

        CriticalSoon = false;
        ProjectedHpPct = null;
        ObservedDropPct = 0;
        // Constant-size list, integer-only arithmetic and zero per-tick
        // allocation. The original FallingFast observation stays intact.
        foreach (var sample in _samples)
        {
            var elapsed = nowMs - sample.AtMs;
            if (elapsed < MinObservationMs || elapsed > ProjectionWindowMs) continue;
            var drop = sample.HpPct - context.HpPct;
            if (drop < MinimumCriticalDropPct || drop <= ObservedDropPct) continue;
            ObservedDropPct = drop;
            var projected = Math.Clamp(context.HpPct
                - (int)(drop * (long)ProjectionHorizonMs / elapsed), 0, 100);
            ProjectedHpPct = projected;
            CriticalSoon = context.HpPct is > 35 and <= 65 && projected <= 35;
        }

        // Sample no more than five times a second even at high polling rates.
        if (_samples.Count == 0 || nowMs - _samples[^1].AtMs >= 200)
            _samples.Add((nowMs, context.HpPct));
    }

    public void Reset()
    {
        _samples.Clear();
        FallingFast = false;
        CriticalSoon = false;
        ProjectedHpPct = null;
        ObservedDropPct = 0;
    }
}

/// <summary>
/// Role classification only for defensively curated tank maintenance.
/// All classes/specs share the same offensive/defensive candidate providers;
/// unknown specs never acquire tank-only privileges by accident.
/// </summary>
internal static class CooldownRoles
{
    public static bool IsTank(string? className, string? specName) =>
        (className?.ToUpperInvariant(), specName?.ToUpperInvariant()) switch
        {
            ("DEATHKNIGHT", "BLOOD") => true,
            ("DEMONHUNTER", "VENGEANCE") => true,
            ("DRUID", "GUARDIAN") => true,
            ("MONK", "BREWMASTER") => true,
            ("PALADIN", "PROTECTION") => true,
            ("WARRIOR", "PROTECTION") => true,
            _ => false,
        };
}
