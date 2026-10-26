namespace MaxDpsCompanion;

/// <summary>
/// 4.5.3: short human copy for an engine HOLD, derived from the engine's status
/// line. The Exp card used to read "waiting for suggestion" (green) while the
/// engine was actually holding every send path - e.g. when the in-game Main
/// toggle is OFF (the 4.0.4 master switch). That looked like "the companion is
/// broken / not pressing" instead of "Main is off in game".
///
/// Pure and XAML-free so the mapping is unit-tested.
/// </summary>
internal static class HoldCopy
{
    /// <summary>The hold reason, or null when the status is not a hold.</summary>
    public static string? Reason(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var text = status.Trim();

        // "holding (<reason>)" -> "<reason>"; the master gate gets plain words.
        if (text.StartsWith("holding (", StringComparison.OrdinalIgnoreCase)
            && text.EndsWith(")", StringComparison.Ordinal))
        {
            var inner = text[9..^1].Trim();
            if (inner.StartsWith("addon Main off", StringComparison.OrdinalIgnoreCase))
                return "Main off in game";
            return inner;
        }

        if (text.StartsWith("link lost", StringComparison.OrdinalIgnoreCase))
            return "link lost - addon heartbeat";

        return null;
    }
}
