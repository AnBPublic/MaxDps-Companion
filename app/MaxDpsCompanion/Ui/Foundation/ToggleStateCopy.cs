namespace MaxDpsCompanion;

/// <summary>
/// 4.5.3/4.5.6: human copy for the ADDON-WINS toggle mirror (Ext3, 14 bits).
/// The companion used to show the mask only as a hex witness ("mask 0x1FFF") in
/// Details, so a user whose in-game Main toggle had been switched off saw
/// "no presses" with nothing naming the cause. Details carries the live summary;
/// the companion log gets exactly one line when a toggle is switched OFF in the
/// in-game addon (4.5.6 removed the ON/first-sight/mirror-loss log lines).
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
    /// The single companion-log line for a toggle switched OFF in the in-game
    /// addon, or null when nothing went ON -> OFF. 4.5.6 deliberately stays
    /// quiet for first sight, ON transitions, mirror loss and no-change ticks:
    /// the log is "one line when a toggle is switched off", not a mirror trace.
    /// Main OFF also names the master-switch fix.
    /// </summary>
    public static string? OffDelta(bool previousValid, int previousMask, bool valid, int mask)
    {
        if (!previousValid || !valid) return null;
        if (previousMask == mask) return null;

        var mainOff = On(previousMask, ToggleSync.BitMain) && !On(mask, ToggleSync.BitMain);
        var off = new List<string>();
        foreach (var (bit, name) in Names)
        {
            if (bit == ToggleSync.BitMain) continue;
            if (On(previousMask, bit) && !On(mask, bit)) off.Add(name);
        }

        if (!mainOff && off.Count == 0) return null;

        if (mainOff)
        {
            var also = off.Count == 0 ? "" : $" - also off: {string.Join(", ", off)}";
            return $"Main switched OFF in the in-game addon - rotation suspended (turn it back on: {MainHint}){also}";
        }

        return $"{string.Join(", ", off)} switched OFF in the in-game addon";
    }
}
