namespace MaxDpsCompanion;

/// <summary>
/// Frozen XAML-free model for the 14 hero toggles (P1). It is the exact
/// AppSettings projection MainForm.LoadFromSettings / ApplyToSettings perform
/// (MainForm.cs:2589-2596, :2629-2636 and the behaviour flags :2637-2648),
/// with no controls, timers or engine references, so tests can assert the
/// mask semantics without an STA thread.
///
/// Bit layout is the canonical <see cref="ToggleSync"/> one (LSB first):
/// <code>
///  0 Main       4 Trinket   8  Solo           12 TTK
///  1 Offensive  5 Interrupt 9  OOC            13 CrowdControl
///  2 Defensive  6 Mobility  10 AutoTarget
///  3 Consumable 7 SelfHeal  11 AutoInteract
/// </code>
///
/// ADDON-WINS authority (ToggleSync, 2026-09-30 flip): the mask built here is
/// a diagnostic view only. The model never calls
/// <see cref="ToggleSync.RequestExplicitPush"/> and never overwrites the
/// in-game toggles; the bridge's Ext3 echo is read through
/// <see cref="TryGetToggleMirror"/>.
///
/// The 14 toggles are: Main, Offensive, Defensive, Interrupt, Self-heal,
/// Mobility, Consumable, Trinket, Solo, OOC, Auto-target, Auto-interact, TTK,
/// Crowd-control. Scheduler / Intelligence / Telemetry are not hero toggles and
/// stay outside this model.
/// </summary>
internal sealed class ToggleModel
{
    // ----- SlotEnabled order (AppSettings.SlotEnabled: spell1..spell8) -----

    public bool Main { get; set; }
    public bool Offensive { get; set; }
    public bool Defensive { get; set; }
    public bool Consumable { get; set; }
    public bool Trinket { get; set; }
    public bool Interrupt { get; set; }
    public bool Mobility { get; set; }
    public bool SelfHeal { get; set; }

    // ----- behaviour toggles (hero card rows) -----

    public bool Solo { get; set; }

    /// <summary>Inverse of <see cref="AppSettings.CombatOnly"/> (MainForm's Out-of-combat toggle).</summary>
    public bool OutOfCombat { get; set; }

    public bool AutoTarget { get; set; }
    public bool AutoInteract { get; set; }
    public bool TimeToKill { get; set; }
    public bool CrowdControl { get; set; }

    /// <summary>Builds a model already loaded from <paramref name="settings"/>.</summary>
    public static ToggleModel FromSettings(AppSettings settings)
    {
        var model = new ToggleModel();
        model.LoadFromSettings(settings);
        return model;
    }

    /// <summary>
    /// MainForm.LoadFromSettings parity (:2589-2596 + the flag block): reads all
    /// 14 values. Slots fall back to the current model value when the settings
    /// array is short (MainForm SlotFlag semantics); the CC gate is published to
    /// <see cref="CrowdControlGate"/> on every load, exactly like the form.
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        Main = SlotAt(settings, 0, Main);
        Offensive = SlotAt(settings, 1, Offensive);
        Defensive = SlotAt(settings, 2, Defensive);
        Consumable = SlotAt(settings, 3, Consumable);
        Trinket = SlotAt(settings, 4, Trinket);
        Interrupt = SlotAt(settings, 5, Interrupt);
        Mobility = SlotAt(settings, 6, Mobility);
        SelfHeal = SlotAt(settings, 7, SelfHeal);

        OutOfCombat = !settings.CombatOnly;
        AutoTarget = settings.AutoTargetEnabled;
        AutoInteract = settings.InteractEnabled;
        TimeToKill = settings.TimeToKillEnabled;
        CrowdControl = settings.CrowdControlEnabled;
        CrowdControlGate.Configure(settings.CrowdControlEnabled);
        Solo = settings.SoloEnabled;
    }

    /// <summary>
    /// MainForm.ApplyToSettings parity (:2629-2648): writes all 14 values back
    /// into the SAME order the form does (slots, CombatOnly, targeting,
    /// interact, TTK, CC + gate, Solo). Scheduler / Intelligence / Telemetry
    /// are not part of this model and are left untouched.
    /// </summary>
    public void ApplyToSettings(AppSettings settings)
    {
        var slots = settings.SlotEnabled;
        if (slots.Length > 0) slots[0] = Main;
        if (slots.Length > 1) slots[1] = Offensive;
        if (slots.Length > 2) slots[2] = Defensive;
        if (slots.Length > 3) slots[3] = Consumable;
        if (slots.Length > 4) slots[4] = Trinket;
        if (slots.Length > 5) slots[5] = Interrupt;
        if (slots.Length > 6) slots[6] = Mobility;
        if (slots.Length > 7) slots[7] = SelfHeal;

        settings.CombatOnly = !OutOfCombat;
        settings.AutoTargetEnabled = AutoTarget;
        settings.InteractEnabled = AutoInteract;
        settings.TimeToKillEnabled = TimeToKill;
        settings.CrowdControlEnabled = CrowdControl;
        CrowdControlGate.Configure(CrowdControl);
        settings.SoloEnabled = Solo;
    }

    /// <summary>
    /// Writes the model through <see cref="ApplyToSettings"/> and returns the
    /// canonical <see cref="ToggleSync.BuildMask"/> packing (diagnostic only —
    /// ADDON-WINS never pushes it; the caller owns the scratch settings).
    /// </summary>
    public int BuildMask(AppSettings settings)
    {
        ApplyToSettings(settings);
        return ToggleSync.BuildMask(settings);
    }

    /// <summary>
    /// The engine mirror echo: true only while the bridge publishes a valid Ext3
    /// block, in which case <paramref name="mirrorMask"/> is that echo (the
    /// authoritative mask). ADDON-WINS and fail-closed: when the echo is absent
    /// this returns false with 0 — never the model's own mask — and nothing is
    /// pushed or queued.
    /// </summary>
    public static bool TryGetToggleMirror(ToggleSync sync, out int mirrorMask)
    {
        if (sync.MirrorValid && sync.MirrorMask is { } mirrored)
        {
            mirrorMask = mirrored & ToggleSync.MaskField;
            return true;
        }
        mirrorMask = 0;
        return false;
    }

    private static bool SlotAt(AppSettings settings, int slot, bool fallback) =>
        slot >= 0 && slot < settings.SlotEnabled.Length ? settings.SlotEnabled[slot] : fallback;
}
