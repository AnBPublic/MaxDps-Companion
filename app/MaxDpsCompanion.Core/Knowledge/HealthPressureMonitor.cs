namespace MaxDpsCompanion;

/// <summary>
/// Small bounded observation window for *observed* player health loss.
/// No hidden combat values, enemy-count guesses or DPS estimates are inferred.
/// Midnight secret / curve-only HP explicitly resets this signal to Unknown.
/// Shared by every class and spec through the same scheduler policy memory.
/// </summary>
internal sealed class HealthPressureMonitor
{
    internal const int WindowMs = 2800;
    internal const int RapidWindowMs = 1800;
    internal const int MinObservationMs = 250;
    internal const int MinDropPct = 15;
    internal const int SustainedMinObservationMs = 900;
    internal const int SustainedMinDropPct = 8;
    // Extreme short-horizon pressure is a prediction from measured HP,
    // never a read of secret incoming damage.
    internal const int ProjectionWindowMs = 1200;
    internal const int ProjectionHorizonMs = 1000;
    internal const int MinimumCriticalDropPct = 20;
    private readonly List<(long AtMs, int HpPct)> _samples = [];

    public bool FallingFast { get; private set; }
    /// <summary>Observed sustained HP loss, strictly valid plain HP in combat.</summary>
    public bool SustainedPressure { get; private set; }
    /// <summary>A narrow observed-loss projection, not an actual HP value.</summary>
    public bool CriticalSoon { get; private set; }
    public int? ProjectedHpPct { get; private set; }
    public int ObservedDropPct { get; private set; }

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

        // v5: bounded no-LINQ/no-capturing-delegate per-frame scan.
        while (_samples.Count != 0 && nowMs - _samples[0].AtMs > WindowMs)
            _samples.RemoveAt(0);

        var rapid = false;
        var sustained = false;
        CriticalSoon = false;
        ProjectedHpPct = null;
        ObservedDropPct = 0;
        for (var i = 0; i < _samples.Count; i++)
        {
            var prior = _samples[i];
            var age = nowMs - prior.AtMs;
            var drop = prior.HpPct - context.HpPct;
            if (context.HpPct <= 70 && age >= MinObservationMs
                && age <= RapidWindowMs && drop >= MinDropPct) rapid = true;
            if (context.HpPct <= 80 && age >= SustainedMinObservationMs
                && drop >= SustainedMinDropPct) sustained = true;

            // Extreme sustained acceleration across TWO trustworthy plain
            // HP observations. An inaccurate projection is only a guarded
            // risk indicator, never a fabricated future combat fact.
            if (age >= MinObservationMs && age <= ProjectionWindowMs
                && drop >= MinimumCriticalDropPct && drop > ObservedDropPct)
            {
                ObservedDropPct = drop;
                var projected = Math.Clamp(context.HpPct
                    - (int)(drop * (long)ProjectionHorizonMs / age), 0, 100);
                ProjectedHpPct = projected;
                CriticalSoon = context.HpPct is > 35 and <= 65
                    && projected <= 35;
            }
        }
        FallingFast = rapid;
        SustainedPressure = sustained;

        // At most five samples/sec and <=15 retained in the 2.8s window.
        if (_samples.Count == 0 || nowMs - _samples[^1].AtMs >= 200)
            _samples.Add((nowMs, context.HpPct));
    }

    public void Reset()
    {
        _samples.Clear();
        FallingFast = false;
        SustainedPressure = false;
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
