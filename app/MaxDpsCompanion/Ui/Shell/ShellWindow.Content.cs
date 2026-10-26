using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WColor = Windows.UI.Color;
using MediaFontFamily = Microsoft.UI.Xaml.Media.FontFamily;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C main screen (docs/plans/2026-10-11-winui3-v4-migration.md §7-P2-C):
/// hero card (status row, live label, 14 toggles under Rotation/Automation
/// headers), the two button rows, the status line, the Exp in-game-config
/// transport shell and the Advanced/Abilities overlay hosts. Control-map port
/// of MainForm.BuildHeroCard :1108-1198, BuildButtonRow1 :1235-1262,
/// BuildButtonRow2 :1264-1296, BuildStatusLine :1298-1310, the Exp layout
/// :822-963 and the popup show/hide pair :1647/:1688. Engine behavior lives in
/// ShellWindow.Engine.cs; this file is layout + visual state only.
/// </summary>
public sealed partial class ShellWindow
{
    private const string AppTitle = "MaxDPS Companion";

    /// <summary>Icon font for the Fluent chip glyphs (MDL2 fallback on Win10).</summary>
    private const string FluentIcons = "Segoe Fluent Icons, Segoe MDL2 Assets";

    // ----- frozen model + the 14 hero toggles (SlotEnabled order, 8-slot model) -----

    private readonly ToggleModel _toggles = new();
    private readonly List<ToggleSwitch> _heroToggles = new();
    private readonly List<SettingRow> _heroSettingRows = new();
    private readonly List<GroupHeader> _heroHeaders = new();

    private readonly ToggleSwitch _main = new() { IsOn = true };
    private readonly ToggleSwitch _offensive = new() { IsOn = true };
    private readonly ToggleSwitch _defensives = new() { IsOn = true };
    private readonly ToggleSwitch _consumable = new();
    private readonly ToggleSwitch _trinket = new();
    private readonly ToggleSwitch _outOfCombat = new();
    private readonly ToggleSwitch _autoTarget = new();
    private readonly ToggleSwitch _autoInteract = new();
    private readonly ToggleSwitch _timeToKill = new() { IsOn = true };
    private readonly ToggleSwitch _crowdControl = new();
    private readonly ToggleSwitch _interrupt = new() { IsOn = true };
    private readonly ToggleSwitch _mobility = new() { IsOn = true };
    private readonly ToggleSwitch _selfHeal = new() { IsOn = true };
    private readonly ToggleSwitch _solo = new();

    private bool _applyingPreset;

    // ----- classic hero chrome -----

    private RoundedCard? _heroCard;
    private readonly LinkLamp _linkLamp = new();
    private TextBlock? _stateLabel;
    private TextBlock? _liveValue;
    private TextBlock? _statusLine;
    private ClassBadge? _classBadge;

    // Shared transport buttons (MainForm reused the same five controls in both
    // shells; only one branch is ever built, so the sharing is safe).
    private ChamferButton? _start;
    private ChamferButton? _stop;
    private ChamferButton? _launchGame;
    private ChamferButton? _recalibrate;
    private ChamferButton? _abilitiesEntry;
    private ChamferButton? _advancedEntry;
    private ChamferButton? _openFolder;

    // ----- Exp in-game-config shell -----

    private ChamferButton? _pauseButton;
    private ChamferButton? _expLaunchGame;
    private ChamferButton? _expRestartGame;
    private Run? _expPillState;
    private Run? _expPillSep;
    private Run? _expPillFps;
    private ExpConsoleView? _expConsole;

    // ----- classic console home (mounted over the classic body, old :801-802) -----

    private ConsoleHomeView? _consoleHome;
    private bool _consoleHomeHiddenByPopup;

    // ----- overlays + lazily built page content -----

    private OverlayShell? _advancedOverlay;
    private OverlayShell? _abilitiesOverlay;
    private SegmentedTabs? _advancedTabs;
    private SegmentedTabs? _abilitiesTabs;
    private bool _advancedContentBuilt;
    private bool _abilitiesContentBuilt;
    private AdvancedConfigurationView? _advancedConfig;
    private AdvancedDiagnosticsView? _advancedDiagnostics;
    private AdvancedIntelligenceView? _advancedIntelligence;

    private UiTier _tier = (UiTier)(-1);
    private bool _uiReady;

    /// <summary>
    /// Builds the engine wiring and then the screen for the configured shell
    /// (classic body or Exp transport), loads the persisted toggles and starts
    /// the 250 ms status pump. Called once from the window constructor.
    /// </summary>
    private void InitializeContent()
    {
        InitializeEngine(_settings);
        CreateSharedButtons();
        if (_settings.InGameConfigMode) BuildExpContent();
        else BuildClassicContent();
        BuildOverlays();
        LoadHeroFromSettings();
        WireToggleHandlers();
        AttachEscapeAccelerator();
        _uiReady = true;
        RootGrid.SizeChanged += (_, _) => ApplyTier();
        ApplyTier(force: true);
        StartStatusPump();
    }

    // ----- classic body (MainForm.BuildBody :767-809 + :1108-1310) -----

    private void BuildClassicContent()
    {
        // Gradient ground behind the scrolling body (UiControls GradientCanvas).
        BodyHost.Children.Add(new GradientCanvas());

        var stack = new VertStack { Gap = 12, Padding = new Thickness(24, 10, 24, 12) };
        stack.Children.Add(BuildHeroCard());
        stack.Children.Add(BuildButtonRow1());
        stack.Children.Add(BuildButtonRow2());
        stack.Children.Add(BuildStatusLine());

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
        };
        scroll.Content = stack;
        BodyHost.Children.Add(scroll);

        // S7 console home: an opaque full-body view mounted OVER the classic
        // body (MainForm.BuildBody :801-802); the Console nav item toggles it
        // off to reveal the hero card underneath. Event wiring mirrors
        // MainForm.BuildConsoleHome :1023-1035.
        _consoleHome = new ConsoleHomeView();
        _consoleHome.PauseRequested += TogglePauseFromConsole;
        _consoleHome.CcHoldRequested += ToggleCcHoldFromConsole;
        _consoleHome.FolderRequested += OpenAppFolder;
        _consoleHome.ConsoleToggleRequested += ToggleConsoleHome;
        _consoleHome.BindsRequested += () => ShowAdvancedTab(0);
        _consoleHome.SettingsRequested += () => ShowAdvancedTab(0);
        _consoleHome.RotationRequested += () => ShowRotationMenu(_consoleHome?.RotationAnchor);
        _consoleHome.DebugRequested += () => ShowAdvancedTab(1);
        _consoleHome.PresetRequested += ApplyPreset;
        BodyHost.Children.Add(_consoleHome);
    }

    /// <summary>Old ConsoleToggleRequested: hide/show the console over the classic body.</summary>
    internal void ToggleConsoleHome()
    {
        if (_consoleHome is null) return;
        _consoleHome.Visibility = _consoleHome.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private RoundedCard BuildHeroCard()
    {
        _heroCard = new RoundedCard
        {
            Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(0, 2, 0, 6),
            CornerRadius = DesignTokens.RadiusOuter,
        };
        var stack = new VertStack { Gap = 6 };

        // Status row: lamp + state + class badge (MainForm :1130-1150).
        var statusRow = new Grid { MinHeight = 30 };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _linkLamp.VerticalAlignment = VerticalAlignment.Center;
        _linkLamp.Margin = new Thickness(4, 0, 8, 0);

        var stateLabel = new TextBlock
        {
            Text = "Stopped",
            FontSize = ControlTheme.Dip(10.5f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetName(stateLabel, "Engine state");
        _stateLabel = stateLabel;

        var classBadge = new ClassBadge
        {
            Text = "AUTO DETECT",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _classBadge = classBadge;

        Grid.SetColumn(_linkLamp, 0);
        Grid.SetColumn(stateLabel, 1);
        Grid.SetColumn(classBadge, 2);
        statusRow.Children.Add(_linkLamp);
        statusRow.Children.Add(stateLabel);
        statusRow.Children.Add(classBadge);
        stack.Children.Add(statusRow);

        // Live row: what MaxDps suggests and why (MainForm :1152-1164).
        var liveValue = new TextBlock
        {
            Text = "Now: -",
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(liveValue, "Current suggestion");
        OwnedToolTip.SetToolTip(liveValue, "What the companion is about to send and why");
        _liveValue = liveValue;
        stack.Children.Add(liveValue);

        stack.Children.Add(HeaderFor("Rotation"));
        stack.Children.Add(TwoToggleRow("Main", "Master switch", _main, "Offensive", "Burst cooldowns", _offensive, alt: false,
            "Master switch: OFF suspends all sends (all slots, targeting, interact, queued presses); other toggles keep their state.", "Burst cooldowns fire in burst windows / TTK-valid boss."));
        stack.Children.Add(TwoToggleRow("Defensive", "Mitigation and absorbs", _defensives, "Interrupt", "Kick casts", _interrupt, alt: true,
            "Mitigation at urgency Yellow+, majors at Red.", "Fires on interruptible cast in range."));
        stack.Children.Add(TwoToggleRow("Self-heal", "Solo self-sustain", _selfHeal, "Mobility", "Gap closers", _mobility, alt: false,
            "Solo: below 65% HP.", "Gap closer when target outside melee."));
        stack.Children.Add(TwoToggleRow("Consumable", "Potions", _consumable, "Trinket", "On-use trinkets", _trinket, alt: true,
            "Burst window.", "Burst window."));

        stack.Children.Add(HeaderFor("Automation"));
        stack.Children.Add(TwoToggleRow("Solo", "Self-sustain mode", _solo, "Out of combat", "Run outside combat", _outOfCombat, alt: false,
            "Self-sustain mode; survival first.", "Runs out-of-combat actions outside combat."));
        stack.Children.Add(TwoToggleRow("Auto-target", "Target when needed", _autoTarget, "Auto-interact", "Interact when needed", _autoInteract, alt: true,
            "Targets the nearest valid enemy when needed.", "Interacts with quest/loot targets when needed."));
        stack.Children.Add(TwoToggleRow("TTK guard", "Estimate target kill time", _timeToKill, "Crowd control", "Stuns a confirmed target", _crowdControl, alt: false,
            "OFF = cooldowns fire without dying-target protection; target HP band is hidden from the companion.", "Opt-in DR-safe control: stuns a confirmed target."));
        stack.Children.Add(BuildMirrorPills());

        _heroCard.Add(stack);
        return _heroCard;
    }

    // ----- Advanced mirror pills (read-only, ToggleSync.EffectiveMask) -----

    private readonly List<(int Bit, StatusPill Pill)> _mirrorPills = new();
    private bool _mirrorPillsValid;
    private int _mirrorPillsMask = -1;

    /// <summary>
    /// Read-only witness of the ADDON-WINS effective mask: one pill per toggle
    /// bit, lit only while the bridge Ext3 echo is valid AND the bit is set
    /// (ToggleSync is fail-closed: no echo = no bit assumed ON). MainForm
    /// exposed this state through the Advanced mirrors; the pills make it
    /// visible without touching toggle authority.
    /// </summary>
    private VertStack BuildMirrorPills()
    {
        var caption = new TextBlock
        {
            Text = "ADDON EFFECTIVE MASK (READ-ONLY)",
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            CharacterSpacing = 180,
            Margin = new Thickness(4, 6, 0, 0),
        };
        var flow = new WrapFlow { Gap = 6, RowGap = 6, Margin = new Thickness(4, 0, 0, 0) };
        AddMirrorPill(flow, "Main", ToggleSync.BitMain, "Main rotation");
        AddMirrorPill(flow, "Off", ToggleSync.BitOffensive, "Offensive cooldowns");
        AddMirrorPill(flow, "Def", ToggleSync.BitDefensive, "Defensive cooldowns");
        AddMirrorPill(flow, "Cons", ToggleSync.BitConsumable, "Consumables");
        AddMirrorPill(flow, "Trink", ToggleSync.BitTrinket, "On-use trinkets");
        AddMirrorPill(flow, "Inter", ToggleSync.BitInterrupt, "Interrupt");
        AddMirrorPill(flow, "Mob", ToggleSync.BitMobility, "Mobility");
        AddMirrorPill(flow, "Heal", ToggleSync.BitSelfHeal, "Self-heal");
        AddMirrorPill(flow, "Solo", ToggleSync.BitSolo, "Solo / self-sustain mode");
        AddMirrorPill(flow, "OOC", ToggleSync.BitOutOfCombat, "Out of combat");
        AddMirrorPill(flow, "Tgt", ToggleSync.BitAutoTarget, "Auto-target");
        AddMirrorPill(flow, "Intrct", ToggleSync.BitAutoInteract, "Auto-interact");
        AddMirrorPill(flow, "TTK", ToggleSync.BitTimeToKill, "Time-to-kill guard");
        AddMirrorPill(flow, "CC", ToggleSync.BitCrowdControl, "Crowd control");

        var stack = new VertStack { Gap = 5 };
        stack.Children.Add(caption);
        stack.Children.Add(flow);
        return stack;
    }

    private void AddMirrorPill(WrapFlow flow, string shortName, int bit, string fullName)
    {
        var pill = new StatusPill(shortName, StatusTone.Muted);
        OwnedToolTip.SetToolTip(pill, fullName);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pill, $"Effective mask: {fullName}");
        flow.Children.Add(pill);
        _mirrorPills.Add((bit, pill));
    }

    /// <summary>250 ms tick: value-only writes, and only when the mask changed.</summary>
    private void UpdateMirrorPills(bool running)
    {
        var valid = _toggleSync.EffectiveFromMirror;
        var mask = _toggleSync.EffectiveMask;
        if (valid == _mirrorPillsValid && mask == _mirrorPillsMask) return;

        var previousValid = _mirrorPillsValid;
        var previousMask = _mirrorPillsMask;
        _mirrorPillsValid = valid;
        _mirrorPillsMask = mask;

        foreach (var (bit, pill) in _mirrorPills)
            pill.Tone = valid && (mask & (1 << bit)) != 0 ? StatusTone.Success : StatusTone.Muted;
        // Advanced > Configuration carries the same read-only witness
        // (MainForm's Advanced mirrors :193-206, ADDON-WINS echo semantics).
        _advancedConfig?.SetMask(valid, mask);

        // 4.5.3/4.5.6: name the toggle state in Details, and log exactly one
        // line in the COMPANION log when a toggle is switched OFF in the
        // in-game addon. No in-game chat print; no ON/first-sight/mirror-loss
        // lines.
        _expConsole?.SetToggles(running ? ToggleStateCopy.Summary(valid, mask) : "engine stopped");
        if (ToggleStateCopy.OffDelta(previousValid, previousMask, valid, mask) is { } delta)
        {
            var tone = delta.StartsWith("Main switched OFF", StringComparison.Ordinal)
                ? ControlTheme.Warning
                : ControlTheme.Info;
            LogMirror($"[toggles] {delta}", tone);
        }
    }

    /// <summary>Appends a line to whichever console the active shell owns.</summary>
    private void LogMirror(string text, WColor tone)
    {
        if (_expConsole is not null) _expConsole.AppendLog(text, tone);
        else _consoleHome?.AppendLog(text, tone);
    }

    private GroupHeader HeaderFor(string title)
    {
        var header = new GroupHeader(title) { Margin = new Thickness(4, 0, 4, 0) };
        _heroHeaders.Add(header);
        return header;
    }

    /// <summary>
    /// One two-toggle setting row (MainForm.TwoToggleRow :1212-1230): measured
    /// side-by-side SettingRows, alternating fills, full trigger condition in
    /// the hover tooltip.
    /// </summary>
    private ToggleRowPanel TwoToggleRow(
        string titleA, string subtitleA, ToggleSwitch toggleA,
        string titleB, string subtitleB, ToggleSwitch toggleB,
        bool alt, string? tooltipA = null, string? tooltipB = null)
    {
        var left = new SettingRow(titleA, subtitleA, toggleA) { AlternateFill = alt };
        var right = new SettingRow(titleB, subtitleB, toggleB) { AlternateFill = alt };
        // Name the switch itself too (MainForm.TwoToggleRow set AccessibleName
        // on the toggle; SettingRow names only the row).
        AutomationProperties.SetName(toggleA, titleA);
        AutomationProperties.SetName(toggleB, titleB);
        if (tooltipA is not null) left.Hint = tooltipA;
        if (tooltipB is not null) right.Hint = tooltipB;
        _heroSettingRows.Add(left);
        _heroSettingRows.Add(right);
        _heroToggles.Add(toggleA);
        _heroToggles.Add(toggleB);
        return new ToggleRowPanel(left, right);
    }

    private void CreateSharedButtons()
    {
        // v4.0.2: the classic Start is the same success/running green as the Exp
        // Start (was brass/Accent); other brass elements are untouched.
        _start = HeroButton("Start", ButtonRole.Primary, ControlTheme.Success);
        _stop = HeroButton("Stop", ButtonRole.Danger, ControlTheme.Danger);
        _stop.IsEnabled = false;
        _launchGame = HeroButton("Launch Game", ButtonRole.Ghost, ControlTheme.Accent);
        _recalibrate = HeroButton("Recalibrate", ButtonRole.Ghost, ControlTheme.Accent);
        _openFolder = HeroButton("Open Folder", ButtonRole.Ghost, ControlTheme.Surface);

        _start.Click += (_, _) => StartEngine();
        _stop.Click += (_, _) => StopEngine();
        _launchGame.Click += (_, _) => LaunchGame();
        _recalibrate.Click += (_, _) => RecalibrateFull();
        _openFolder.Click += (_, _) => OpenAppFolder();
    }

    /// <summary>MainForm button parity: 9.5 pt bold, stretching, no trailing glyph.</summary>
    private static ChamferButton HeroButton(string text, ButtonRole role, WColor accent) => new()
    {
        Content = text,
        Role = role,
        AccentColor = accent,
        TrailingGlyph = null,
        FontSize = ControlTheme.Dip(9.5f),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    // Button row 1: Start / Stop / Launch Game (MainForm :1235-1262).
    private Grid BuildButtonRow1()
    {
        var row = EqualColumns(3);
        var start = _start!;
        var stop = _stop!;
        var launch = _launchGame!;
        Grid.SetColumn(start, 0);
        Grid.SetColumn(stop, 1);
        Grid.SetColumn(launch, 2);
        row.Children.Add(start);
        row.Children.Add(stop);
        row.Children.Add(launch);
        return row;
    }

    // Button row 2: Recalibrate / Abilities… / Advanced… / Open Folder (:1264-1296).
    private Grid BuildButtonRow2()
    {
        _advancedEntry = HeroButton("Advanced\u2026", ButtonRole.Ghost, ControlTheme.Accent);
        _advancedEntry.Click += (_, _) => ShowAdvanced();
        _abilitiesEntry = HeroButton("Abilities\u2026", ButtonRole.Ghost, ControlTheme.Surface);
        _abilitiesEntry.Click += (_, _) => ShowAbilities();

        var row = EqualColumns(4);
        var recalibrate = _recalibrate!;
        var abilities = _abilitiesEntry;
        var advanced = _advancedEntry;
        var folder = _openFolder!;
        Grid.SetColumn(recalibrate, 0);
        Grid.SetColumn(abilities, 1);
        Grid.SetColumn(advanced, 2);
        Grid.SetColumn(folder, 3);
        row.Children.Add(recalibrate);
        row.Children.Add(abilities);
        row.Children.Add(advanced);
        row.Children.Add(folder);
        return row;
    }

    private static Grid EqualColumns(int columns)
    {
        var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 2, 0, 2) };
        for (var i = 0; i < columns; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return row;
    }

    private TextBlock BuildStatusLine()
    {
        var statusLine = new TextBlock
        {
            Text = "Stopped",
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 2, 4, 0),
        };
        AutomationProperties.SetName(statusLine, "Status message");
        _statusLine = statusLine;
        return statusLine;
    }

    // ----- Exp (in-game-config) shell (MainForm.BuildExpLayout :822-963) -----

    private void BuildExpContent()
    {
        // 4.5.0: the Exp ground is the SAME Midnight gradient as the classic
        // body (violet-to-void + aurora), not a flat terminal token - the app
        // should read as one product in either shell.
        BodyHost.Children.Add(new GradientCanvas());

        var grid = new Grid { Padding = new Thickness(18, 12, 18, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // One read-only status pill: state + sample rate only (:883-903).
        // v4.0.1 (brief §5): hairline ring, full radius; hierarchy = state
        // 13px white/80, separator dot white/25, fps 12px white/45.
        var pillState = new Run { Text = "\u25CB Stopped", FontSize = 13, Foreground = ControlTheme.Brush(ControlTheme.TextWhite80) };
        var pillSep = new Run { Text = " \u00B7 ", FontSize = 13, Foreground = ControlTheme.Brush(ControlTheme.TextWhite25) };
        var pillFps = new Run { Text = "0 fps", FontSize = 12, Foreground = ControlTheme.Brush(ControlTheme.TextWhite45) };
        var pillText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        pillText.Inlines.Add(pillState);
        pillText.Inlines.Add(pillSep);
        pillText.Inlines.Add(pillFps);
        _expPillState = pillState;
        _expPillSep = pillSep;
        _expPillFps = pillFps;
        AutomationProperties.SetName(pillText, "Exp status pill");
        AutomationProperties.SetHelpText(pillText, "Read-only running state and sample rate");
        var pill = new Border
        {
            Background = ControlTheme.Vertical(
                ControlTheme.Lighten(ToUi(ExpTheme.Card), 0.06f),
                ToUi(ExpTheme.Card)),
            BorderBrush = ControlTheme.Brush(ControlTheme.AccentRing),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 0, 14, 0),
            MinHeight = 36,
            Margin = new Thickness(0, 2, 0, 2),
            Child = pillText,
        };
        Grid.SetRow(pill, 0);
        grid.Children.Add(pill);

        // Two transport rows; both rows run flush left+right with the status
        // card (brief §2): row 1 = Start (primary, 1.4fr) / Pause / Stop at
        // 1fr each (UNCHANGED in 4.4.0); row 2 = Open Game / Restart Game in
        // two EQUAL columns. Calibrate moved into the Details disclosure of the
        // status card, so it is no longer mounted here (4.4.0 "Wayfinder").
        // Uniform 10px gaps.
        var start = _start!;
        var stop = _stop!;
        var pause = HeroButton("Pause", ButtonRole.Ghost, ToUi(ExpTheme.Accent));
        var launch = HeroButton("Open Game", ButtonRole.Ghost, ToUi(ExpTheme.Secondary));
        var restart = HeroButton("Restart Game", ButtonRole.Ghost, ToUi(ExpTheme.Accent));
        _pauseButton = pause;
        _expLaunchGame = launch;
        _expRestartGame = restart;

        // Glyph chip + label (button-in-button); SetLabel keeps the legacy
        // combined accessible name ("▶ Start" etc. — UIA parity) while the
        // chip renders the Segoe Fluent Icons codepoint (the legacy ⏸ char
        // falls back to a colour-emoji box at any size).
        start.SetLabel("\uE768", "Start", "\u25B6", FluentIcons);     // Fluent Play
        pause.SetLabel("\uE769", "Pause", "\u23F8", FluentIcons);     // Fluent Pause
        stop.SetLabel("\uE71A", "Stop", "\u25A0", FluentIcons);       // Fluent Stop
        launch.SetLabel("\u2922", "Open Game");                       // ⤢
        restart.SetLabel("\uE72C", "Restart Game", "\u21BB", FluentIcons); // Fluent Refresh ↻

        start.Role = ButtonRole.Primary;
        // 4.0.2: primary Start is the success/running green, not the accent;
        // 4.5.0 uses the deeper OkFill base so the gradient keeps light-text
        // contrast at both ends.
        start.AccentColor = ToUi(ExpTheme.OkFill);
        stop.Role = ButtonRole.Danger;
        stop.AccentColor = ToUi(ExpTheme.DangerFill);
        pause.Role = ButtonRole.Ghost;
        pause.AccentColor = ToUi(ExpTheme.Accent);
        launch.Role = ButtonRole.Ghost;
        launch.AccentColor = ToUi(ExpTheme.Secondary);
        restart.Role = ButtonRole.Ghost;
        restart.AccentColor = ToUi(ExpTheme.Accent);
        // Sunwell gold hairline: relaunch is the "Light" action of the pair,
        // amethyst stays the app accent. Restrained to the ring only.
        restart.RingColor = ControlTheme.WithAlpha(ControlTheme.Warning, 0x66);
        foreach (var button in new[] { start, pause, stop, launch, restart })
            button.GhostSurface = ToUi(ExpTheme.Card);

        // Initial transport copy/help. UpdateExpTransportState rewrites the Open
        // Game label/glyph/help as the process appears; Restart stays disabled
        // until a game process is observed.
        ToolTipService.SetToolTip(launch, "Open World of Warcraft through Battle.net");
        AutomationProperties.SetHelpText(launch, "Open World of Warcraft through Battle.net");
        ToolTipService.SetToolTip(restart, "Close the running game and relaunch it through Battle.net");
        AutomationProperties.SetHelpText(restart,
            "Close the running World of Warcraft process and relaunch it through Battle.net");
        restart.IsEnabled = false;

        pause.Click += (_, _) =>
        {
            TogglePauseFromConsole();
            UpdateExpReadouts(_status);
        };
        launch.Click += (_, _) => OpenGameFromExp();
        restart.Click += (_, _) => _ = RestartGameAsync();

        var row1 = new Grid { ColumnSpacing = 10 };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(start, 0);
        Grid.SetColumn(pause, 1);
        Grid.SetColumn(stop, 2);
        row1.Children.Add(start);
        row1.Children.Add(pause);
        row1.Children.Add(stop);

        var row2 = new Grid { ColumnSpacing = 10 };
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(launch, 0);
        Grid.SetColumn(restart, 1);
        row2.Children.Add(launch);
        row2.Children.Add(restart);

        var transport = new Grid { RowSpacing = 10, Margin = new Thickness(0, 6, 0, 6) };
        transport.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        transport.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(row1, 0);
        Grid.SetRow(row2, 1);
        transport.Children.Add(row1);
        transport.Children.Add(row2);
        Grid.SetRow(transport, 1);
        grid.Children.Add(transport);

        // Console host: the minimal status card with the collapsed Details
        // witness (bridge + mask + rate) and the single mono rolling log
        // (old ConsoleHome(minimal: true) :846-847 + UpdateExpReadouts).
        _expConsole = new ExpConsoleView();
        // 4.4.0: Calibrate lives inside the Details disclosure now; the card
        // raises the request and the shell owns the worker/lifecycle.
        _expConsole.CalibrateRequested += RecalibrateFull;
        Grid.SetRow(_expConsole, 2);
        grid.Children.Add(_expConsole);

        // Apply the real transport state before the first paint: the initial
        // process probe ran in InitializeEngine, so an already-running client
        // must show "Game Running" immediately (no 250 ms "Open Game" flash).
        UpdateExpTransportState();

        // Entrance (brief §3): fade-up, staggered 0 / 80 / 160 ms across the
        // three rows (pill / transport / status card).
        pill.Loaded += (_, _) => Motion.FadeUp(pill, 0);
        transport.Loaded += (_, _) => Motion.FadeUp(transport, 80);
        _expConsole.Loaded += (_, _) => Motion.FadeUp(_expConsole, 160);

        // Contract: the 14 hero toggles never appear in this shell
        // (MainForm.BuildExpLayout :854-857). Here they are never built: the
        // classic body (and every SettingRow holding them) is skipped above.
        BodyHost.Children.Add(grid);
    }

    /// <summary>
    /// v4.0.1 Exp status pill: three runs with the brief's hierarchy — state
    /// 13px (semantic tone), separator dot white/25, fps 12px white/45.
    /// </summary>
    private void SetExpPill(string glyph, string state, int fps, WColor tone)
    {
        if (_expPillState is null || _expPillSep is null || _expPillFps is null) return;
        _expPillState.Text = $"{glyph} {state}";
        _expPillState.Foreground = ControlTheme.Brush(tone);
        _expPillSep.Foreground = ControlTheme.Brush(ControlTheme.TextWhite25);
        _expPillFps.Text = $"{fps} fps";
        _expPillFps.Foreground = ControlTheme.Brush(ControlTheme.TextWhite45);
    }

    // ----- overlays (MainForm.ShowAdvanced :1647 / ShowAbilities :1688) -----

    private void BuildOverlays()
    {
        // Exp builds NO popups: the in-game overlay owns toggle authority (:859).
        if (_settings.InGameConfigMode) return;

        _advancedOverlay = new OverlayShell("Advanced", 620, HideAdvanced);
        _abilitiesOverlay = new OverlayShell("Class browser", 900, HideAbilities);
        _advancedTabs = _advancedOverlay.Tabs;
        _abilitiesTabs = _abilitiesOverlay.Tabs;
        OverlayHost.Children.Add(_advancedOverlay);
        OverlayHost.Children.Add(_abilitiesOverlay);
    }

    /// <summary>MainForm.ShowAdvanced parity: lazy content, engine diagnostics flag, Exp suppressed.</summary>
    internal void ShowAdvanced()
    {
        if (_settings.InGameConfigMode || _advancedOverlay is null) return;
        if (_engine is not null) _engine.WantDiagnostics = true;
        if (!_advancedContentBuilt)
        {
            _advancedContentBuilt = true;
            BuildAdvancedContent();
        }
        _intelligenceEnsureBuilt();
        _abilitiesOverlay?.Close();
        HideConsoleBehindPopup();
        _advancedOverlay.Open();
        if (_advancedTabs is not null) _advancedTabs.SelectedIndex = 0;
    }

    /// <summary>Opens the Advanced overlay on a tab (MainForm.OpenAdvancedTab :1037-1043).</summary>
    internal void ShowAdvancedTab(int index)
    {
        ShowAdvanced();
        if (_advancedTabs is not null && index >= 0 && index < _advancedTabs.TabPages.Count)
            _advancedTabs.SelectedIndex = index;
    }

    internal void HideAdvanced()
    {
        if (_advancedOverlay is null) return;
        _advancedOverlay.Close();
        if (_engine is not null) _engine.WantDiagnostics = false;
        RestoreConsoleAfterPopup();
    }

    internal void ShowAbilities()
    {
        if (_settings.InGameConfigMode || _abilitiesOverlay is null) return;
        if (!_abilitiesContentBuilt)
        {
            _abilitiesContentBuilt = true;
            BuildAbilitiesContent();
        }
        _advancedOverlay?.Close();
        HideConsoleBehindPopup();
        _abilitiesOverlay.Open();
        if (_abilitiesTabs is not null) _abilitiesTabs.SelectedIndex = 0;
        OpenAbilitiesViews();
    }

    /// <summary>
    /// WS-C drill-through seam (old ShowAbilitiesWithPreset :1766-1784): the
    /// Intelligence tiles and hero bubbles open the Abilities overlay filtered
    /// to their category set on both the browser and the explorer.
    /// </summary>
    internal void ShowAbilitiesWithPreset(string tag)
    {
        if (_settings.InGameConfigMode) return;
        ShowAbilities();
        ApplyAbilitiesPreset(tag);
    }

    internal void HideAbilities()
    {
        _abilitiesOverlay?.Close();
        RestoreConsoleAfterPopup();
    }

    /// <summary>
    /// Exactly one full-window overlay may be visible at a time (old
    /// HideConsoleBehindPopup :1726-1740): the console home is hidden while a
    /// popup is open and restored only when the user had not hidden it.
    /// </summary>
    private void HideConsoleBehindPopup()
    {
        if (_consoleHome is { Visibility: Visibility.Visible })
        {
            _consoleHomeHiddenByPopup = true;
            _consoleHome.Visibility = Visibility.Collapsed;
        }
    }

    private void RestoreConsoleAfterPopup()
    {
        if (!_consoleHomeHiddenByPopup) return;
        _consoleHomeHiddenByPopup = false;
        if (_consoleHome is not null) _consoleHome.Visibility = Visibility.Visible;
    }

    private void _intelligenceEnsureBuilt() => _advancedIntelligence?.EnsureBuilt();

    /// <summary>
    /// Lazy Advanced content (MainForm :1659-1664): the three real tabs mount
    /// here on first open, loaded from the live settings; every change persists
    /// through the shell's SaveNow (old WireAutoSave :571-590).
    /// </summary>
    private void BuildAdvancedContent()
    {
        // Configuration: ConfigurationPage + SoloBandEditor + CrowdControlToggle.
        _advancedConfig = new AdvancedConfigurationView();
        _advancedConfig.LoadFrom(_settings);
        _advancedConfig.SetMask(_toggleSync.EffectiveFromMirror, _toggleSync.EffectiveMask);
        _advancedConfig.Changed += SaveSoon;
        _advancedConfig.AbilitiesRequested += ShowAbilities;
        _advancedConfig.OpenFolderRequested += OpenAppFolder;

        // Diagnostics: DiagnosticsPage + InstallDoctor readouts + SemanticBanner.
        _advancedDiagnostics = new AdvancedDiagnosticsView();
        _advancedDiagnostics.LoadFrom(_settings);
        _advancedDiagnostics.Changed += SaveSoon;
        _advancedDiagnostics.TelemetryChanged += SyncTelemetryFromUi;
        _advancedDiagnostics.CalibrateRequested += RecalibrateFull;
        _advancedDiagnostics.ResetColorsRequested += ResetColors;
        _advancedDiagnostics.FindStripRequested += RecalibratePositionOnly;
        _advancedDiagnostics.ExportRequested += () => _ = ExportTelemetryAsync();
        _advancedDiagnostics.ReplayRequested += () => _ = ReplayTelemetryAsync();
        _advancedDiagnostics.LoadAuditRequested += () => _ = LoadCastAuditAsync();
        _advancedDiagnostics.BrowseRequested += () => _ = BrowseBattleNetAsync();
        _advancedDiagnostics.LaunchRequested += () => LaunchGame();
        _advancedDiagnostics.OpenFolderRequested += OpenAppFolder;
        _advancedDiagnostics.DoctorRefreshRequested += RefreshInstallDoctor;
        RefreshColorStatus();
        RefreshInstallDoctor();
        UpdateTelemetryStatus();

        // Intelligence: IntelligencePage + ProportionBar.
        _advancedIntelligence = new AdvancedIntelligenceView();
        _advancedIntelligence.DrillRequested += ShowAbilitiesWithPreset;

        _advancedOverlay!.AddTab("Configuration", _advancedConfig);
        _advancedOverlay.AddTab("Diagnostics", _advancedDiagnostics);
        _advancedOverlay.AddTab("Intelligence", _advancedIntelligence);

        // ADDON-WINS two-way mirrors for the editable mode toggles (old
        // WireModeMirror :342-343 and the solo/combat/CC mirrors :374-386).
        MirrorHeroToggle(_autoTarget, _advancedConfig.AutoTargetToggle);
        MirrorHeroToggle(_autoInteract, _advancedConfig.AutoInteractToggle);
        MirrorHeroCheck(_solo, _advancedConfig.SoloToggle);
        MirrorHeroCheck(_crowdControl, _advancedConfig.CrowdControlToggle);
        MirrorCombatOnly();
    }

    /// <summary>Old SyncCombatFromCard/SyncCombatFromAdvanced :594-610 (inverted pair).</summary>
    private void MirrorCombatOnly()
    {
        if (_advancedConfig is null) return;
        var syncing = false;
        _outOfCombat.Toggled += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { _advancedConfig.SetCombatOnly(_outOfCombat.IsOn == false); }
            finally { syncing = false; }
        };
        // The hero toggle's own handler already persists (OnHeroToggleChanged).
        _advancedConfig.CombatOnlyChanged += combatOnly =>
        {
            if (syncing) return;
            syncing = true;
            try { _outOfCombat.IsOn = !combatOnly; }
            finally { syncing = false; }
        };
    }

    /// <summary>One shared setting, two controls (old WireMirror :649-660).</summary>
    private static void MirrorHeroToggle(ToggleSwitch hero, ToggleSwitch mirror)
    {
        var syncing = false;
        hero.Toggled += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { mirror.IsOn = hero.IsOn; }
            finally { syncing = false; }
        };
        mirror.Toggled += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { hero.IsOn = mirror.IsOn; }
            finally { syncing = false; }
        };
    }

    /// <summary>ToggleSwitch hero mirror onto a Configuration CheckBox (old _solo/_ccToggle pairing).</summary>
    private static void MirrorHeroCheck(ToggleSwitch hero, CheckBox mirror)
    {
        var syncing = false;
        hero.Toggled += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { mirror.IsChecked = hero.IsOn; }
            finally { syncing = false; }
        };
        mirror.Checked += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { hero.IsOn = true; }
            finally { syncing = false; }
        };
        mirror.Unchecked += (_, _) =>
        {
            if (syncing || mirror.IsChecked == true) return;
            syncing = true;
            try { hero.IsOn = false; }
            finally { syncing = false; }
        };
    }

    private void SyncTelemetryFromUi()
    {
        ApplyToSettings();
        SyncTelemetryRecorder();
        SaveSettings();
    }

    // ----- rotation menu (MainForm.ShowRotationMenu :1089-1106) -----

    /// <summary>
    /// Rotation source + preset bundles + the Class skills entry, as a WinUI
    /// MenuFlyout. The nav trigger lives on the ConsoleHome pass; this method
    /// is its call target (and the Exp guard matches :1092).
    /// </summary>
    internal void ShowRotationMenu(FrameworkElement? anchor = null)
    {
        if (_settings.InGameConfigMode) return;
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Rotation source: MaxDps (suggest-only)", IsEnabled = false });
        flyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var bundle in ConsolePresets.All)
        {
            var item = new MenuFlyoutItem { Text = $"Preset: {bundle.Name}" };
            ToolTipService.SetToolTip(item, bundle.Summary);
            var captured = bundle.Preset;
            item.Click += (_, _) => ApplyPreset(captured);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var classSkills = new MenuFlyoutItem { Text = "Class skills\u2026" };
        classSkills.Click += (_, _) => ShowAbilities();
        flyout.Items.Add(classSkills);
        flyout.ShowAt(anchor ?? RootGrid);
    }

    /// <summary>
    /// Applies a named preset bundle to the SAME 14 toggles the user can flip
    /// (MainForm.ApplyPreset :1057-1087). ADDON-WINS: this only writes
    /// settings.ini; nothing is pushed to the addon.
    /// </summary>
    internal void ApplyPreset(ConsolePreset preset)
    {
        if (_settings.InGameConfigMode) return;
        var bundle = ConsolePresets.Get(preset);
        _applyingPreset = true;
        try
        {
            _main.IsOn = bundle.Main;
            _offensive.IsOn = bundle.Offensive;
            _defensives.IsOn = bundle.Defensives;
            _consumable.IsOn = bundle.Consumable;
            _trinket.IsOn = bundle.Trinket;
            _outOfCombat.IsOn = bundle.OutOfCombat;
            _autoTarget.IsOn = bundle.AutoTarget;
            _autoInteract.IsOn = bundle.AutoInteract;
            _timeToKill.IsOn = bundle.TimeToKill;
            _crowdControl.IsOn = bundle.CrowdControl;
            _interrupt.IsOn = bundle.Interrupt;
            _mobility.IsOn = bundle.Mobility;
            _selfHeal.IsOn = bundle.SelfHeal;
            _solo.IsOn = bundle.Solo;
        }
        finally
        {
            _applyingPreset = false;
        }
        SyncToggleModelFromControls();
        _toggles.ApplyToSettings(_settings);
        SaveSettings();
        // Old ApplyPreset :1082-1086: record the bundle on the console home.
        _consoleHome?.SetActivePreset(preset);
        _consoleHome?.AppendLog($"Preset \"{bundle.Name}\" applied.", ControlTheme.Accent);
        _consoleHome?.NoteChange($"Preset \"{bundle.Name}\" applied. {bundle.Summary}");
        SetStatus($"Preset: {bundle.Name}. {bundle.Summary}", ControlTheme.Accent);
    }

    // ----- Esc (plan §3: KeyboardAccelerator on the root grid) -----

    private void AttachEscapeAccelerator()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            if (CloseTopOverlay()) args.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(escape);
    }

    /// <summary>Topmost popup first, exactly like MainForm.ProcessCmdKey :3199-3207.</summary>
    internal bool CloseTopOverlay()
    {
        if (_openDialogs > 0) return false; // the ContentDialog owns Esc while open
        if (_abilitiesOverlay is { IsOpen: true })
        {
            HideAbilities();
            return true;
        }
        if (_advancedOverlay is { IsOpen: true })
        {
            HideAdvanced();
            return true;
        }
        return false;
    }

    // ----- settings <-> toggles (MainForm.LoadFromSettings / ApplyToSettings) -----

    private void LoadHeroFromSettings()
    {
        _toggles.LoadFromSettings(_settings);
        SetHeroTogglesFromModel();
        RefreshColorStatus();
    }

    private void WireToggleHandlers()
    {
        foreach (var toggle in _heroToggles)
            toggle.Toggled += (_, _) => OnHeroToggleChanged();
    }

    /// <summary>
    /// Hero toggle change: persist through the frozen <see cref="ToggleModel"/>
    /// (slots + behaviour flags, CC gate included). ADDON-WINS: no
    /// <see cref="ToggleSync.RequestExplicitPush"/> call site exists here.
    /// </summary>
    private void OnHeroToggleChanged()
    {
        if (_applyingPreset || !_uiReady) return;
        SyncToggleModelFromControls();
        _toggles.ApplyToSettings(_settings);
        SaveSettings();
        RegisterPauseHotkey(); // MainForm.SaveNow parity (:486-492)
    }

    private void SyncToggleModelFromControls()
    {
        _toggles.Main = _main.IsOn;
        _toggles.Offensive = _offensive.IsOn;
        _toggles.Defensive = _defensives.IsOn;
        _toggles.Consumable = _consumable.IsOn;
        _toggles.Trinket = _trinket.IsOn;
        _toggles.Interrupt = _interrupt.IsOn;
        _toggles.Mobility = _mobility.IsOn;
        _toggles.SelfHeal = _selfHeal.IsOn;
        _toggles.Solo = _solo.IsOn;
        _toggles.OutOfCombat = _outOfCombat.IsOn;
        _toggles.AutoTarget = _autoTarget.IsOn;
        _toggles.AutoInteract = _autoInteract.IsOn;
        _toggles.TimeToKill = _timeToKill.IsOn;
        _toggles.CrowdControl = _crowdControl.IsOn;
    }

    private void SetHeroTogglesFromModel()
    {
        _main.IsOn = _toggles.Main;
        _offensive.IsOn = _toggles.Offensive;
        _defensives.IsOn = _toggles.Defensive;
        _consumable.IsOn = _toggles.Consumable;
        _trinket.IsOn = _toggles.Trinket;
        _interrupt.IsOn = _toggles.Interrupt;
        _mobility.IsOn = _toggles.Mobility;
        _selfHeal.IsOn = _toggles.SelfHeal;
        _solo.IsOn = _toggles.Solo;
        _outOfCombat.IsOn = _toggles.OutOfCombat;
        _autoTarget.IsOn = _toggles.AutoTarget;
        _autoInteract.IsOn = _toggles.AutoInteract;
        _timeToKill.IsOn = _toggles.TimeToKill;
        _crowdControl.IsOn = _toggles.CrowdControl;
    }

    // ----- width tiers (MainForm.ApplyScale D5) -----

    /// <summary>
    /// Applies the width tier to the hero rows, headers, labels and buttons
    /// (MainForm.ApplyScale :1336-1375; WinUI content is measured by the
    /// layout, so only the type scale / toggle sizes need the pass).
    /// </summary>
    private void ApplyTier(bool force = false)
    {
        var width = (int)Math.Max(320, ContentRoot.ActualWidth > 0 ? ContentRoot.ActualWidth : 660);
        var tier = UiScale.TierFor(width);
        if (!force && tier == _tier) return;
        _tier = tier;
        var scale = UiScale.For(width);

        foreach (var row in _heroSettingRows) row.ApplyScale(scale);
        foreach (var header in _heroHeaders) header.ApplyScale(scale);
        foreach (var button in new[] { _start, _stop, _launchGame, _recalibrate, _abilitiesEntry, _advancedEntry, _openFolder, _pauseButton, _expLaunchGame, _expRestartGame })
            button?.ApplyScale(scale);
        if (_stateLabel is not null) _stateLabel.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont + 0.5f));
        if (_liveValue is not null) _liveValue.FontSize = ControlTheme.Dip(scale.BaseFont);
        if (_statusLine is not null) _statusLine.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 1.5f));
        // v3.5 S7: the console home / Exp card track the same width tiers
        // (old ApplyScale :1418/:1437).
        _consoleHome?.ApplyScale(scale);
        _expConsole?.ApplyScale(scale);
    }

    /// <summary>System.Drawing -> Windows.UI.Color for the ExpTheme tokens (plan §1).</summary>
    private static WColor ToUi(System.Drawing.Color color) =>
        WColor.FromArgb(color.A, color.R, color.G, color.B);
}
