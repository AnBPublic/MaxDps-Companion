namespace MaxDpsCompanion;

/// <summary>
/// Named settings bundles for the Console home (S7). Each preset is a NAMED
/// set of the SAME toggles the user can flip by hand — applying one is a
/// shortcut that moves those switches, never a hidden mode and never a wire
/// change. Presets only restrict: the in-game addon can still further restrict
/// (effective = companion AND addon), and a missing addon key reads as ON.
/// </summary>
internal enum ConsolePreset
{
    Solo,
    Levelling,
    Dungeon,
    Raid,
    Tank,
}

/// <summary>
/// One preset as explicit switch values. Booleans mirror the 14 hero toggles
/// one-for-one, so MainForm applies a bundle by writing each existing
/// <see cref="ToggleSwitch.Checked"/> (which persists through the existing
/// SaveNow path). No new settings schema.
/// </summary>
internal sealed record ConsolePresetBundle(
    ConsolePreset Preset,
    string Name,
    string Summary,
    bool Main,
    bool Offensive,
    bool Defensives,
    bool Consumable,
    bool Trinket,
    bool OutOfCombat,
    bool AutoTarget,
    bool AutoInteract,
    bool TimeToKill,
    bool CrowdControl,
    bool Interrupt,
    bool Mobility,
    bool SelfHeal,
    bool Solo);

internal static class ConsolePresets
{
    /// <summary>Order shown in the preset strip: solo play first, group play last.</summary>
    public static readonly IReadOnlyList<ConsolePresetBundle> All = new[]
    {
        new ConsolePresetBundle(ConsolePreset.Solo, "Solo",
            "Survival first: self-sustain and mitigation lead; group tools stay available.",
            Main: true, Offensive: true, Defensives: true, Consumable: true, Trinket: true,
            OutOfCombat: true, AutoTarget: true, AutoInteract: true, TimeToKill: true,
            CrowdControl: false, Interrupt: true, Mobility: true, SelfHeal: true, Solo: true),
        new ConsolePresetBundle(ConsolePreset.Levelling, "Levelling",
            "Open-world pace: self-sustain and mobility on, crowd control off unless you opt in.",
            Main: true, Offensive: true, Defensives: true, Consumable: true, Trinket: true,
            OutOfCombat: true, AutoTarget: true, AutoInteract: true, TimeToKill: true,
            CrowdControl: false, Interrupt: true, Mobility: true, SelfHeal: true, Solo: true),
        new ConsolePresetBundle(ConsolePreset.Dungeon, "Dungeon",
            "Group content: interrupts and crowd control on, self-sustain stays available.",
            Main: true, Offensive: true, Defensives: true, Consumable: true, Trinket: true,
            OutOfCombat: false, AutoTarget: true, AutoInteract: true, TimeToKill: true,
            CrowdControl: true, Interrupt: true, Mobility: true, SelfHeal: true, Solo: false),
        new ConsolePresetBundle(ConsolePreset.Raid, "Raid",
            "Raid content: cooldowns gated by time-to-kill, crowd control off by default.",
            Main: true, Offensive: true, Defensives: true, Consumable: true, Trinket: true,
            OutOfCombat: false, AutoTarget: true, AutoInteract: true, TimeToKill: true,
            CrowdControl: false, Interrupt: true, Mobility: true, SelfHeal: true, Solo: false),
        new ConsolePresetBundle(ConsolePreset.Tank, "Tank",
            "Tank stance: mitigation, interrupts and control lead; the rotation is MaxDps's.",
            Main: true, Offensive: true, Defensives: true, Consumable: true, Trinket: true,
            OutOfCombat: false, AutoTarget: true, AutoInteract: true, TimeToKill: true,
            CrowdControl: true, Interrupt: true, Mobility: true, SelfHeal: true, Solo: false),
    };

    public static ConsolePresetBundle Get(ConsolePreset preset) =>
        All.First(b => b.Preset == preset);
}

/// <summary>
/// Role badge + one-line summary for the spec header. The role is INFERRED
/// from the detected spec name (no role bit exists on the frozen wire), so the
/// copy says "inferred" rather than presenting it as decoded data.
/// </summary>
internal static class ConsoleRole
{
    private static readonly HashSet<string> TankSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Protection", "Blood", "Guardian", "Brewmaster", "Vengeance",
    };

    private static readonly HashSet<string> HealerSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Holy", "Discipline", "Restoration", "Mistweaver", "Preservation",
    };

    public static (string Role, string Summary) ForSpec(string? className, string? specName)
    {
        if (string.IsNullOrWhiteSpace(specName) || string.IsNullOrWhiteSpace(className))
            return ("AUTO", "Waiting for the bridge to report a class and spec. The companion suggests only if the addon encodes it.");

        var role = TankSpecs.Contains(specName) ? "TANK"
            : HealerSpecs.Contains(specName) ? "HEALER"
            : "DPS";

        var summary = role switch
        {
            "TANK" => $"{className} {specName} (role inferred from spec). Mitigation and control lead; group toggles can restrict further.",
            "HEALER" => $"{className} {specName} (role inferred from spec). Self-sustain and defensives lead; the addon can only restrict.",
            _ => $"{className} {specName} (role inferred from spec). The companion relays MaxDps's suggestion and never presses on its own.",
        };
        return (role, summary);
    }
}
