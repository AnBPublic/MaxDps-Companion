namespace MaxDpsCompanion;

/// <summary>
/// 4.5.3: human copy for the ADDON-WINS toggle mirror (Ext3, 14 bits). The
/// companion used to show the mask only as a hex witness ("mask 0x1FFF") in
/// Details, so a user whose in-game Main toggle had been switched off saw
/// "no presses" with nothing naming the cause. The mirror is now logged when it
/// changes and summarised in Details.
///
/// Pure and XAML-free so the copy is unit-tested.
/// </summary>
internal static class ToggleStateCopy
{
    /// <summary>Canonical bit order (matches ToggleSync.Bit*).</summary>
    public static readonly (int Bit, string Name)[] Names =
    {
        (ToggleSync.BitMain, "Main"),
        (ToggleSync.BitOffensive, "Offensive"),
        (ToggleSync.BitDefensive, "Defensive"),
        (ToggleSync.BitConsumable, "Consumable"),
        (ToggleSync.BitTrinket, "Trinket"),
        (ToggleSync.BitInterrupt, "Interrupt"),
        (ToggleSync.BitMobility, "Mobility"),
        (ToggleSync.BitSelfHeal, "SelfHeal"),
        (ToggleSync.BitSolo, "Solo"),
        (ToggleSync.BitOutOfCombat, "OOC"),
        (ToggleSync.BitAutoTarget, "AutoTarget"),
        (ToggleSync.BitAutoInteract, "AutoInteract"),
        (ToggleSync.BitTimeToKill, "TTK"),
        (ToggleSync.BitCrowdControl, "CrowdControl"),
    };

    public const string MainHint = "/mcb t Main on";

    public static string Name(int bit)
    {
        foreach (var (value, name) in Names)
            if (value == bit) return name;
        return "?";
    }

    private static bool On(int mask, int bit) => (mask & (1 << bit)) != 0;

    /// <summary>
    /// One-line state: "Main ON · all others ON", "Main ON · OFF: Trinket, OOC",
    /// "Main OFF (master switch) · also off: Trinket" or the no-mirror line.
    /// </summary>
    public static string Summary(bool valid, int mask)
    {
        if (!valid) return "addon not reporting (no toggle mirror)";

        var off = new List<string>();
        foreach (var (bit, name) in Names)
            if (!On(mask, bit)) off.Add(name);

        if (off.Count == 0) return "Main ON · all others ON";

        var mainOn = On(mask, ToggleSync.BitMain);
        var rest = off.Where(n => n != "Main").ToList();
        if (!mainOn)
        {
            var also = rest.Count == 0 ? "" : $" · also off: {string.Join(", ", rest)}";
            return $"Main OFF (master switch){also}";
        }

        return $"Main ON · OFF: {string.Join(", ", rest)}";
    }

    /// <summary>
    /// Log line for a mirror transition, or null when there is nothing worth
    /// printing (no previous state, no change, or still no mirror).
    /// </summary>
    public static string? Delta(bool previousValid, int previousMask, bool valid, int mask)
    {
        if (!previousValid) return valid ? Summary(true, mask) : null;
        if (!valid) return "addon stopped reporting toggles";
        if (previousMask == mask) return null;

        var mainBefore = On(previousMask, ToggleSync.BitMain);
        var mainAfter = On(mask, ToggleSync.BitMain);

        var changes = new List<string>();
        foreach (var (bit, name) in Names)
        {
            if (bit == ToggleSync.BitMain) continue;
            if (On(previousMask, bit) == On(mask, bit)) continue;
            changes.Add($"{name} {(On(mask, bit) ? "ON" : "OFF")}");
        }

        var tail = changes.Count == 0 ? "" : $" · {string.Join(", ", changes)}";
        if (mainBefore != mainAfter)
        {
            return mainAfter
                ? "Main ON - rotation resumed" + tail
                : $"Main OFF - rotation suspended (turn it back on: {MainHint})" + tail;
        }

        return string.Join(", ", changes);
    }
}
