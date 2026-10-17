using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C Advanced "Configuration" tab: real content replacing the placeholder.
/// WinUI port of ConfigurationPage + SoloBandEditor + CrowdControlToggle
/// (old Ui/SettingsPages.cs :3-148) filled with the MainForm configuration
/// cards (BuildConfigurationContent :1866-1909).
///
/// Behaviour parity:
/// <list type="bullet">
///   <item>the Scheduler/Intelligence/Solo/Combat-only/CC/Solo-band/keys/
///     process fields write the SAME <see cref="AppSettings"/> the WinForms
///     page wrote (old ApplyToSettings :2618-2655); the write path is
///     single-sourced through <see cref="ApplyTo"/>, called on every change by
///     the shell's debounced SaveNow;</item>
///   <item>the Combat card shows the read-only ADDON-WINS effective-mask pills
///     (ToggleSync.EffectiveMask) instead of the old editable popup mirrors —
///     fail-closed, never pushed;</item>
///   <item>Auto-target / Auto-interact stay editable and mirror the hero
///     toggles two-way (old WireModeMirror :638-642 and _autoTargetAdv).</item>
/// </list>
/// </summary>
internal sealed class AdvancedConfigurationView : UserControl
{
    private readonly CheckBox _scheduler;
    private readonly CheckBox _intelligence;
    private readonly CheckBox _solo;
    private readonly CheckBox _combatOnly;
    private readonly CheckBox _crowdControl;
    private readonly NumberBox _soloMinor;
    private readonly NumberBox _soloMajor;
    private readonly NumberBox _soloImmunity;
    private readonly TextBox _targetKey;
    private readonly TextBox _interactKey;
    private readonly TextBox _pauseHotkey;
    private readonly TextBox _processName;
    private readonly CheckBox _allowBackground;
    private readonly CheckBox _userQueue;
    private readonly List<MaskPillRow> _maskPills = new();
    private bool _loading;

    public ToggleSwitch AutoTargetToggle { get; } = new();
    public ToggleSwitch AutoInteractToggle { get; } = new();

    /// <summary>Advanced-side controls the hero toggles mirror (old _solo/_combatOnly/_ccToggle).</summary>
    public CheckBox SoloToggle => _solo;
    public CheckBox CombatOnlyToggle => _combatOnly;
    public CheckBox CrowdControlToggle => _crowdControl;

    /// <summary>Raised by every interactive change; the shell runs SaveNow.</summary>
    public event Action? Changed;

    /// <summary>Combat-only checkbox moved (inverted mirror of the hero Out-of-combat toggle).</summary>
    public event Action<bool>? CombatOnlyChanged;

    public AdvancedConfigurationView()
    {
        _scheduler = AdvancedUi.Check("Action scheduler (link-aware, deterministic - recommended)",
            "Smooth, ordered casts. Off = old loop. On = deterministic ordering and pacing: frozen-heartbeat link hold, interrupt/defensive urgency, GCD and key-interval gates.");
        _intelligence = AdvancedUi.Check("Combat intelligence (USE/HOLD/SKIP for situational abilities)",
            "Grades situational abilities USE/HOLD/SKIP. Evaluates every situational suggestion against the ability knowledge base and live combat context. The main rotation is never gated.");
        _solo = AdvancedUi.Check("Solo / self-sustain mode (survival first, self-heals below 65% HP)",
            "Requires Combat intelligence. Below 65% HP efficient self-heals become eligible; below 35% HP survival actions outrank damage. Emergency cooldowns are preserved while HP is safe.");
        _combatOnly = AdvancedUi.Check("Combat-only automatic targeting / interact",
            "When on, the companion only acts while you are in combat.");
        _crowdControl = AdvancedUi.Check("Allow curated crowd control on a confirmed target",
            "Crowd control is opt-in (default off). ON allows curated stuns on a confirmed target with DR protection; the in-game toggle can only restrict.");

        _soloMinor = AdvancedUi.Number(75, 40, 99, "Minor absorb at/below HP percent (40-99)");
        _soloMajor = AdvancedUi.Number(50, 20, 90, "Major defensive at/below HP percent (20-90)");
        _soloImmunity = AdvancedUi.Number(30, 5, 60, "Immunity at/below HP percent (5-60)");
        _targetKey = AdvancedUi.Field("", "Target key");
        _interactKey = AdvancedUi.Field("", "Interact key");
        _pauseHotkey = AdvancedUi.Field("", "Pause hotkey");
        _processName = AdvancedUi.Field("", "Game process");
        _allowBackground = AdvancedUi.Check("Send keys while the game is in the background",
            "Keyboard slots keep working while the game is in the background; mouse/interact always need focus.");
        _userQueue = AdvancedUi.Check("Follow my hotbar keypresses (queue them into the GCD window)",
            "Watches the keys bound to your action bars (spells, items and macros via the v4.0.3 Ext4 addon map) and delivers a press into the current GCD window instead of letting the rotation overwrite it. UI binds, movement keys and unbound keys are never watched.");

        var maskCaption = new TextBlock
        {
            Text = "ADDON EFFECTIVE MASK (READ-ONLY)",
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            CharacterSpacing = 180,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var maskFlow = new WrapFlow { Gap = 6, RowGap = 6 };
        AddMaskPill(maskFlow, "Main", ToggleSync.BitMain, "Main rotation");
        AddMaskPill(maskFlow, "Off", ToggleSync.BitOffensive, "Offensive cooldowns");
        AddMaskPill(maskFlow, "Def", ToggleSync.BitDefensive, "Defensive cooldowns");
        AddMaskPill(maskFlow, "Cons", ToggleSync.BitConsumable, "Consumables");
        AddMaskPill(maskFlow, "Trink", ToggleSync.BitTrinket, "On-use trinkets");
        AddMaskPill(maskFlow, "Inter", ToggleSync.BitInterrupt, "Interrupt");
        AddMaskPill(maskFlow, "Mob", ToggleSync.BitMobility, "Mobility");
        AddMaskPill(maskFlow, "Heal", ToggleSync.BitSelfHeal, "Self-heal");
        AddMaskPill(maskFlow, "Tgt", ToggleSync.BitAutoTarget, "Auto-target");
        AddMaskPill(maskFlow, "Intrct", ToggleSync.BitAutoInteract, "Auto-interact");

        var abilities = AdvancedUi.Button("Abilities\u2026", ButtonRole.Ghost, () => AbilitiesRequested?.Invoke());
        var openFolder = AdvancedUi.Button("Open Folder", ButtonRole.Ghost, () => OpenFolderRequested?.Invoke());

        var content = new VertStack { Gap = 12 };
        content.Children.Add(AdvancedUi.Card("Automation", "Engine",
            _scheduler,
            AdvancedUi.HintTip("Smooth, ordered casts. Off = old loop.",
                "On = deterministic ordering and pacing: frozen-heartbeat link hold, interrupt/defensive urgency, GCD and key-interval gates. Off = legacy loop."),
            _intelligence,
            AdvancedUi.HintTip("Grades situational abilities USE/HOLD/SKIP.",
                "Evaluates every situational suggestion USE / HOLD / SKIP against the ability knowledge base and live combat context. The main rotation is never gated.")));
        content.Children.Add(AdvancedUi.Card("Combat", "Slots",
            maskCaption,
            maskFlow,
            AdvancedUi.HintTip(
                "Pills show the addon's effective mask (companion AND addon).",
                "ADDON-WINS: the effective mask is the bridge Ext3 echo. Fail-closed - no valid echo means no bit is assumed ON. Nothing here is ever pushed to the addon.")));
        content.Children.Add(AdvancedUi.Card("Safety", "Modes",
            _solo,
            AdvancedUi.Row("Minor absorb at/below HP% (40-99)", _soloMinor),
            AdvancedUi.Row("Major defensive at/below HP% (20-90)", _soloMajor),
            AdvancedUi.Row("Immunity at/below HP% (5-60)", _soloImmunity),
            AdvancedUi.Hint("A validated ladder: an inverted band is corrected on write, never saved silently."),
            _combatOnly,
            _crowdControl));
        content.Children.Add(AdvancedUi.Card("Targeting", "Assist",
            AdvancedUi.ToggleRow("Auto-target (press Target key)", "Targets the nearest valid enemy when needed.", AutoTargetToggle),
            AdvancedUi.ToggleRow("Auto-interact (press Interact key)", "Interacts with quest/loot targets when needed.", AutoInteractToggle),
            AdvancedUi.Row("Target key", _targetKey),
            AdvancedUi.Row("Interact key", _interactKey)));
        content.Children.Add(AdvancedUi.Card("Input", "Keys",
            AdvancedUi.Row("Pause hotkey", _pauseHotkey),
            _allowBackground,
            _userQueue));
        content.Children.Add(AdvancedUi.Card("Bridge", "Attach",
            AdvancedUi.Row("Game process", _processName),
            AdvancedUi.HintTip("Game window to attach to. Recalibrate finds the strip.",
                "Which game window to attach to (without .exe). Recalibrate locates the strip; the engine re-aligns automatically if it moves.")));
        content.Children.Add(AdvancedUi.Card("Advanced", "Tools",
            AdvancedUi.ButtonRow(abilities, openFolder)));

        var host = new ThemedScrollHost();
        AdvancedUi.Scroll(content, host);
        Content = host;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Configuration page");

        WireChanges();
    }

    public event Action? AbilitiesRequested;
    public event Action? OpenFolderRequested;

    // ----- test/review seams (old SlidersForTest / ToggleForTest) -----

    internal (NumberBox Minor, NumberBox Major, NumberBox Immunity) SlidersForTest =>
        (_soloMinor, _soloMajor, _soloImmunity);

    internal CheckBox CrowdControlForTest => _crowdControl;

    internal CheckBox SchedulerForTest => _scheduler;
    internal CheckBox IntelligenceForTest => _intelligence;

    /// <summary>Pushes the current settings into the controls (old LoadFromSettings + LoadFrom).</summary>
    public void LoadFrom(AppSettings settings)
    {
        _loading = true;
        try
        {
            _scheduler.IsChecked = settings.SchedulerEnabled;
            _intelligence.IsChecked = settings.IntelligenceEnabled;
            _solo.IsChecked = settings.SoloEnabled;
            _combatOnly.IsChecked = settings.CombatOnly;
            _crowdControl.IsChecked = settings.CrowdControlEnabled;
            _soloMinor.Value = Math.Clamp(settings.SoloMinorHpPct, 40, 99);
            _soloMajor.Value = Math.Clamp(settings.SoloMajorHpPct, 20, 90);
            _soloImmunity.Value = Math.Clamp(settings.SoloImmunityHpPct, 5, 60);
            _targetKey.Text = settings.TargetKey;
            _interactKey.Text = settings.InteractKey;
            _pauseHotkey.Text = settings.PauseHotkey;
            _processName.Text = settings.ProcessName;
            _allowBackground.IsChecked = settings.AllowBackgroundKeys;
            _userQueue.IsChecked = settings.UserQueueEnabled;
            AutoTargetToggle.IsOn = settings.AutoTargetEnabled;
            AutoInteractToggle.IsOn = settings.InteractEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Writes the page's fields back into the SAME settings (old ApplyToSettings
    /// :2618-2655): solo bands validate the ladder first; empty key fields keep
    /// the Tab/F defaults; CC publishes the gate exactly like the form.
    /// </summary>
    public void ApplyTo(AppSettings settings)
    {
        settings.SchedulerEnabled = _scheduler.IsChecked == true;
        settings.IntelligenceEnabled = _intelligence.IsChecked == true;
        settings.SoloEnabled = _solo.IsChecked == true;
        settings.CombatOnly = _combatOnly.IsChecked == true;
        settings.CrowdControlEnabled = _crowdControl.IsChecked == true;
        CrowdControlGate.Configure(settings.CrowdControlEnabled);

        var (minor, major, immunity) = PolicyOptions.ValidateSoloBands(
            (int)NumberOr(_soloMinor, settings.SoloMinorHpPct),
            (int)NumberOr(_soloMajor, settings.SoloMajorHpPct),
            (int)NumberOr(_soloImmunity, settings.SoloImmunityHpPct));
        // Reflect the correction in the controls so the user sees the saved state.
        _soloMinor.Value = minor;
        _soloMajor.Value = major;
        _soloImmunity.Value = immunity;
        settings.SoloMinorHpPct = minor;
        settings.SoloMajorHpPct = major;
        settings.SoloImmunityHpPct = immunity;

        settings.AutoTargetEnabled = AutoTargetToggle.IsOn;
        settings.InteractEnabled = AutoInteractToggle.IsOn;
        var targetKey = _targetKey.Text.Trim();
        settings.TargetKey = string.IsNullOrWhiteSpace(targetKey) ? "Tab" : targetKey;
        var interactKey = _interactKey.Text.Trim();
        settings.InteractKey = string.IsNullOrWhiteSpace(interactKey) ? "F" : interactKey;
        settings.PauseHotkey = _pauseHotkey.Text.Trim();
        settings.ProcessName = _processName.Text.Trim();
        settings.AllowBackgroundKeys = _allowBackground.IsChecked == true;
        settings.UserQueueEnabled = _userQueue.IsChecked == true;
    }

    public void SetCombatOnly(bool combatOnly) => _combatOnly.IsChecked = combatOnly;

    /// <summary>Read-only mask pills (effective echo; fail-closed).</summary>
    public void SetMask(bool valid, int mask)
    {
        foreach (var pill in _maskPills) pill.Update(valid, mask);
    }

    private void AddMaskPill(WrapFlow flow, string shortName, int bit, string fullName)
    {
        var row = new MaskPillRow(shortName, bit, fullName);
        _maskPills.Add(row);
        flow.Children.Add(row);
    }

    private static double NumberOr(NumberBox box, double fallback) =>
        double.IsNaN(box.Value) ? fallback : box.Value;

    private void WireChanges()
    {
        void On() { if (!_loading) Changed?.Invoke(); }

        _scheduler.Checked += (_, _) => On();
        _scheduler.Unchecked += (_, _) => On();
        _intelligence.Checked += (_, _) => On();
        _intelligence.Unchecked += (_, _) => On();
        _solo.Checked += (_, _) => On();
        _solo.Unchecked += (_, _) => On();
        _combatOnly.Checked += (_, _) => { On(); if (!_loading) CombatOnlyChanged?.Invoke(true); };
        _combatOnly.Unchecked += (_, _) => { On(); if (!_loading) CombatOnlyChanged?.Invoke(false); };
        _crowdControl.Checked += (_, _) => On();
        _crowdControl.Unchecked += (_, _) => On();
        _allowBackground.Checked += (_, _) => On();
        _allowBackground.Unchecked += (_, _) => On();
        _userQueue.Checked += (_, _) => On();
        _userQueue.Unchecked += (_, _) => On();
        _soloMinor.ValueChanged += (_, _) => On();
        _soloMajor.ValueChanged += (_, _) => On();
        _soloImmunity.ValueChanged += (_, _) => On();
        _targetKey.TextChanged += (_, _) => On();
        _interactKey.TextChanged += (_, _) => On();
        _pauseHotkey.TextChanged += (_, _) => On();
        _processName.TextChanged += (_, _) => On();
    }
}
