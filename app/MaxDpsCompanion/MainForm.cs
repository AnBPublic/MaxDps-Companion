using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

internal sealed class MainForm : Form
{
    private const int PauseHotkeyId = 0xA71;
    // Width floor: the shell degrades to the narrow layout before clipping.
    private const int MinWindowWidth = 520;
    private const int MinWindowHeight = 560;
    // Classic v3 defaults (A3): 660 wide, content height <= working area.
    private const int ClassicWantWidth = 660;
    private const int ClassicWantHeight = 920;

    private static string UiFont => _uiFont ??= DesignTokens.FamilyName;
    private static string? _uiFont;

    /// <summary>UI typeface for owner-drawn controls in <see cref="UiControls"/>.</summary>
    internal static string UiFontPublic => UiFont;

    private readonly AppSettings _settings;
    private readonly RotationEngine _engine;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };

    private EngineStatus _status;
    private bool _hotkeyRegistered;
    private string _statusMessage = "Stopped";
    private StatusTone _statusMessageTone = StatusTone.Muted;

    // Advanced diagnostics readout (slot summary + last key + decision head).
    private readonly Label _slotsValue = new();
    private readonly Label _lastKeyValue = new();
    private readonly Label _decisionValue = new();
    private readonly Label _rawValue = new();
    private readonly Label _protocolValue = new();
    private readonly Label _bridgeStateValue = new();
    private readonly Label _auditValue = new();

    // Hero toggles (SlotEnabled order, 8-slot model). Two behaviour toggles
    // (auto-target/auto-interact) exist once; combat-only exists once.
    private readonly ToggleSwitch _main = new() { Checked = true };
    private readonly ToggleSwitch _offensive = new() { Checked = true };
    private readonly ToggleSwitch _defensives = new() { Checked = true };
    private readonly ToggleSwitch _consumable = new();
    private readonly ToggleSwitch _trinket = new();
    private readonly ToggleSwitch _outOfCombat = new();
    private readonly ToggleSwitch _autoTarget = new();
    private readonly ToggleSwitch _autoInteract = new();
    private readonly ToggleSwitch _interrupt = new() { Checked = true };
    private readonly ToggleSwitch _mobility = new() { Checked = true };
    private readonly ToggleSwitch _selfHeal = new() { Checked = true };

    private readonly ChamferButton _start = new()
    {
        Text = "Start",
        Role = ButtonRole.Primary,
    };
    private readonly ChamferButton _stop = new()
    {
        Text = "Stop",
        Role = ButtonRole.Danger,
        AccentColor = DesignTokens.Danger,
        Enabled = false,
    };
    private readonly ChamferButton _recalibrate = new()
    {
        Text = "Recalibrate",
        Role = ButtonRole.Ghost,
        AccentColor = DesignTokens.Accent,
    };
    private readonly ChamferButton _openFolder = new() { Text = "Open Folder", Role = ButtonRole.Ghost };
    private readonly ChamferButton _launchGame = new() { Text = "Launch Game", Role = ButtonRole.Ghost };

    private BlockLocation? _pendingLocation;

    private NotifyIcon? _tray;
    private ContextMenuStrip? _trayMenu;
    private ToolStripMenuItem? _trayStartStop;

    private readonly System.Windows.Forms.Timer _saveDebounce = new() { Interval = 600 };

    private readonly TextBox _processName = new();
    private readonly TextBox _pauseHotkey = new();
    private readonly NumericUpDown _offsetX = Spin(-4000, 4000);
    private readonly NumericUpDown _offsetY = Spin(-4000, 4000);
    private readonly NumericUpDown _cellSize = Spin(2, 64);
    private readonly NumericUpDown _pollInterval = Spin(10, 1000);
    private readonly NumericUpDown _minKeyInterval = Spin(20, 5000);
    private readonly NumericUpDown _keyPress = Spin(0, 200);
    private readonly NumericUpDown _tolerance = Spin(16, 128);
    private readonly TextBox _targetKey = new();
    private readonly TextBox _interactKey = new();
    private readonly TextBox _bnetPath = new();
    private readonly ChamferButton _bnetBrowse = new() { Text = "Browse", Role = ButtonRole.Ghost };
    private readonly Label _colorStatus = new();
    private readonly ChamferButton _learnColors = new() { Text = "Calibrate colors", Role = ButtonRole.Ghost };
    private readonly ChamferButton _resetColors = new() { Text = "Reset", Role = ButtonRole.Ghost };
    private readonly CheckBox _allowBackground = new() { Text = "Send keys while the game is in the background" };
    private readonly CheckBox _combatOnly = new() { Text = "Combat-only automatic targeting / interact" };
    private readonly CheckBox _scheduler = new() { Text = "Action scheduler (link-aware, deterministic - recommended)" };
    private readonly CheckBox _intelligence = new() { Text = "Combat intelligence (USE/HOLD/SKIP for situational abilities)" };
    private readonly CheckBox _solo = new() { Text = "Solo / self-sustain mode (survival first, self-heals below 65% HP)" };
    private readonly CheckBox _telemetry = new() { Text = "Record rotation telemetry (in memory, off by default)" };
    private readonly ChamferButton _telemetryExport = new() { Text = "Export...", Role = ButtonRole.Ghost };
    private readonly ChamferButton _telemetryReplay = new() { Text = "Replay...", Role = ButtonRole.Ghost };
    private readonly Label _telemetryStatus = new();
    private TelemetryRecorder? _telemetryRecorder;

    // ----- classic UI (v3.0.0 workstream A) -----
    private Control _mainBody = null!;
    private Panel _advancedOverlay = null!;
    private Panel _abilitiesOverlay = null!;
    private TabControl _advancedTabs = null!;
    private TabControl _abilitiesTabs = null!;
    private readonly LinkLamp _linkLamp = new();
    private readonly Label _stateLabel = new();
    private readonly Label _liveValue = new();
    private readonly Label _statusLine = new();
    private readonly ClassBadge _classBadge = new();
    private readonly StripView _stripView = new();
    private ChamferButton? _advancedEntry;
    private ChamferButton? _abilitiesEntry;
    private bool _advancedContentBuilt;
    private readonly AbilityExplorer _explorer;
    private readonly IntelligencePage _intelligencePage = new();
    private readonly ConfigurationPage _config = new();
    private readonly DiagnosticsPage _diagnostics = new();
    private ClassSkillsView? _classSkills;
    private ChamferButton? _classSkillsEntry;

    private static Image? _appIcon;
    private bool _uiInitialised;

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _engine = new RotationEngine(settings);
        _engine.StatusChanged += s => _status = s;
        _engine.LocationChanged += location => _pendingLocation = location;

        Text = "MaxDPS Companion";
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = DesignTokens.Background;
        ForeColor = DesignTokens.TextPrimary;
        Padding = new Padding(2);
        Font = DesignTokens.Type(DesignTokens.BodySize);
        NameInputs();
        FitToScreen();

        _explorer = new AbilityExplorer(
            ability => _settings.Abilities.IsEnabled(ability),
            (ability, on) =>
            {
                _settings.Abilities = _settings.Abilities.With(ability.SpellId, on, !ability.NeverAutomatic);
                SaveSettings();
            });
        _classSkills = new ClassSkillsView(
            AbilityCatalog.Default,
            ClassSpellBook.Default,
            ability => _settings.Abilities.IsEnabled(ability),
            (ability, on) =>
            {
                _settings.Abilities = _settings.Abilities.With(ability.SpellId, on, !ability.NeverAutomatic);
                SaveSettings();
            });

        BuildLayout();
        LoadFromSettings();
        StyleInputs(this);
        WireAutoSave();
        SyncTelemetryRecorder();
        BuildTray();

        OnToggle(_main, 0);
        OnToggle(_offensive, 1);
        OnToggle(_defensives, 2);
        OnToggle(_consumable, 3);
        OnToggle(_trinket, 4);
        OnToggle(_interrupt, 5);
        OnToggle(_mobility, 6);
        OnToggle(_selfHeal, 7);

        _start.Click += (_, _) => StartEngine();
        _stop.Click += (_, _) => StopEngine();
        _recalibrate.Click += (_, _) => RecalibrateFull();
        _telemetryExport.Click += (_, _) => ExportTelemetry();
        _telemetryReplay.Click += (_, _) => ReplayTelemetry();
        _openFolder.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", Program.AppDir);
        _launchGame.Click += (_, _) => LaunchGame();
        _learnColors.Click += (_, _) => LearnColors();
        _resetColors.Click += (_, _) => ResetColors();
        _bnetBrowse.Click += (_, _) => BrowseBNet();

        _main.CheckedChanged += (_, _) => SaveNow();
        _offensive.CheckedChanged += (_, _) => SaveNow();
        _interrupt.CheckedChanged += (_, _) => SaveNow();
        _defensives.CheckedChanged += (_, _) => SaveNow();
        _consumable.CheckedChanged += (_, _) => SaveNow();
        _trinket.CheckedChanged += (_, _) => SaveNow();
        _mobility.CheckedChanged += (_, _) => SaveNow();
        _selfHeal.CheckedChanged += (_, _) => SaveNow();
        _outOfCombat.CheckedChanged += (_, _) => SyncCombatFromCard();
        _autoTarget.CheckedChanged += (_, _) => SaveNow();
        _autoInteract.CheckedChanged += (_, _) => SaveNow();

        // Hero "Solo" toggle mirrors the Advanced checkbox (one setting).
        _solo2.CheckedChanged += (_, _) =>
        {
            if (_solo.Checked != _solo2.Checked)
            {
                _solo.Checked = _solo2.Checked;
                SaveNow();
            }
        };
        _solo.CheckedChanged += (_, _) =>
        {
            if (_solo2.Checked != _solo.Checked) _solo2.Checked = _solo.Checked;
        };
        _classBadge.TextChanged += (_, _) => _classBadge.AccessibleName = $"Class and spec: {_classBadge.Text}";

        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            ApplyToSettings();
            SaveSettings();
            RegisterPauseHotkey();
        };

        _uiTimer.Tick += (_, _) => RefreshStatus();
        _uiTimer.Start();
        _uiInitialised = true;
    }

    // ----- persistence -----

    /// <summary>Accessibility names for the raw input controls (v2.7 §49).</summary>
    private void NameInputs()
    {
        _processName.AccessibleName = "Game process";
        _pauseHotkey.AccessibleName = "Pause hotkey";
        _targetKey.AccessibleName = "Target key";
        _interactKey.AccessibleName = "Interact key";
        _bnetPath.AccessibleName = "Battle.net path";
        _offsetX.AccessibleName = "Block offset X";
        _offsetY.AccessibleName = "Block offset Y";
        _cellSize.AccessibleName = "Cell size in pixels";
        _pollInterval.AccessibleName = "Poll interval in milliseconds";
        _minKeyInterval.AccessibleName = "Minimum key gap in milliseconds";
        _keyPress.AccessibleName = "Key hold in milliseconds";
        _tolerance.AccessibleName = "Colour tolerance";
        _colorStatus.AccessibleName = "Colour calibration status";
        _telemetryStatus.AccessibleName = "Telemetry status";
        _slotsValue.AccessibleName = "Decoded slots";
        _lastKeyValue.AccessibleName = "Last key sent";
        _decisionValue.AccessibleName = "Decision summary";
        _rawValue.AccessibleName = "Raw sample";
        _protocolValue.AccessibleName = "Protocol version";
        _bridgeStateValue.AccessibleName = "Bridge state";
        _auditValue.AccessibleName = "Registry audit summary";
    }

    // settings.ini is process-wide (the default AppSettings path is relative),
    // so serialize writes: construction-time migration plus debounced saves
    // from several forms must not truncate the same file concurrently.
    private static readonly object SettingsSaveGate = new();

    private void SaveSettings()
    {
        lock (SettingsSaveGate) _settings.Save();
    }

    private void SaveNow()
    {
        _saveDebounce.Stop();
        ApplyToSettings();
        SaveSettings();
        RegisterPauseHotkey();
    }

    private void WireAutoSave()
    {
        void SaveSoon()
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        foreach (NumericUpDown spin in new[] { _offsetX, _offsetY, _cellSize, _pollInterval, _minKeyInterval, _keyPress, _tolerance })
            spin.ValueChanged += (_, _) => SaveNow();
        _allowBackground.CheckedChanged += (_, _) => SaveNow();
        _scheduler.CheckedChanged += (_, _) => SaveNow();
        _intelligence.CheckedChanged += (_, _) => SaveNow();
        _solo.CheckedChanged += (_, _) => SaveNow();
        _telemetry.CheckedChanged += (_, _) => SaveNow();
        _telemetry.CheckedChanged += (_, _) => SyncTelemetryRecorder();
        _combatOnly.CheckedChanged += (_, _) => SyncCombatFromAdvanced();
        foreach (TextBox box in new[] { _processName, _pauseHotkey, _targetKey, _interactKey, _bnetPath })
            box.TextChanged += (_, _) => SaveSoon();
    }

    private bool _syncingCombat;

    private void SyncCombatFromCard()
    {
        if (_syncingCombat) return;
        _syncingCombat = true;
        try { _combatOnly.Checked = !_outOfCombat.Checked; }
        finally { _syncingCombat = false; }
        SaveNow();
    }

    private void SyncCombatFromAdvanced()
    {
        if (_syncingCombat) return;
        _syncingCombat = true;
        try { _outOfCombat.Checked = !_combatOnly.Checked; }
        finally { _syncingCombat = false; }
        SaveNow();
    }

    private void SetCombatOnly(bool combatOnly)
    {
        _syncingCombat = true;
        try
        {
            _combatOnly.Checked = combatOnly;
            _outOfCombat.Checked = !combatOnly;
        }
        finally { _syncingCombat = false; }
    }

    private bool SlotFlag(int slot, bool fallback) =>
        (slot >= 0 && slot < _settings.SlotEnabled.Length) ? _settings.SlotEnabled[slot] : fallback;

    private void OnToggle(ToggleSwitch toggle, int slot)
    {
        toggle.CheckedChanged += (_, _) =>
        {
            if (slot >= 0 && slot < _settings.SlotEnabled.Length)
            {
                _settings.SlotEnabled[slot] = toggle.Checked;
                SaveSettings();
            }
        };
    }

    // ----- chrome: custom title bar + classic fixed body + popups (v3 A3-A5) -----

    // v1.3.9 fixed row budget: status 42 + live 42 + strip 40 + spells header 24
    // + 4×66 + modes header 24 + 2×66 + card padding 24 = 592.
    private const int HeroCardHeight = 42 + 42 + 40 + 24 + 4 * 66 + 24 + 2 * 66 + 24;

    private void BuildLayout()
    {
        var windowLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        windowLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        windowLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        windowLayout.Controls.Add(BuildTitleBar(), 0, 0);
        windowLayout.Controls.Add(BuildBody(), 0, 1);
        Controls.Add(windowLayout);
    }

    private Control BuildTitleBar()
    {
        var titleBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DesignTokens.Surface,
            Padding = new Padding(14, 0, 0, 0),
        };
        var headerIcon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(24, 24),
            Location = new Point(14, 12),
            BackColor = Color.Transparent,
        };
        try
        {
            var iconPath = Path.Combine(Program.AppDir, "assets", "icon-256.png");
            if (File.Exists(iconPath))
            {
                using var stream = File.OpenRead(iconPath);
                headerIcon.Image = Image.FromStream(stream);
            }
        }
        catch { /* loose art missing: title text carries the identity */ }
        var headerTitle = new Label
        {
            Text = $"MaxDPS Companion v{Native.AppVersion}",
            AutoSize = false,
            Location = new Point(48, 0),
            Size = new Size(240, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DesignTokens.Type(9.5F, FontStyle.Bold),
            ForeColor = DesignTokens.TextPrimary,
            BackColor = Color.Transparent,
        };
        var version = new Label
        {
            Text = Native.BuildVersion,
            AutoSize = false,
            Location = new Point(292, 0),
            Size = new Size(300, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DesignTokens.Type(8F),
            ForeColor = DesignTokens.TextMuted,
            BackColor = Color.Transparent,
        };
        var closeButton = new TitleBarButton { Text = "x", Dock = DockStyle.Right, HoverColor = DesignTokens.Danger };
        var minimizeButton = new TitleBarButton { Text = "-", Dock = DockStyle.Right, HoverColor = DesignTokens.SurfaceElevated };
        closeButton.Click += (_, _) => Close();
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        titleBar.Controls.Add(headerIcon);
        titleBar.Controls.Add(headerTitle);
        titleBar.Controls.Add(version);
        titleBar.Controls.Add(minimizeButton);
        titleBar.Controls.Add(closeButton);
        foreach (Control draggable in new Control[] { titleBar, headerIcon, headerTitle, version })
            draggable.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) WindowChrome.BeginDrag(Handle); };
        return titleBar;
    }

    private Control BuildBody()
    {
        var canvas = new GradientCanvas { Dock = DockStyle.Fill, Padding = new Padding(24, 10, 24, 12) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeroCardHeight + 12F)); // card + margins
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        _mainBody = layout;
        layout.Controls.Add(BuildHeroCard(), 0, 0);
        layout.Controls.Add(BuildButtonRow1(), 0, 1);
        layout.Controls.Add(BuildButtonRow2(), 0, 2);
        layout.Controls.Add(BuildStatusLine(), 0, 3);
        canvas.Controls.Add(layout);

        _advancedOverlay = BuildAdvancedOverlay();
        _abilitiesOverlay = BuildAbilitiesOverlay();
        canvas.Controls.Add(_advancedOverlay);
        canvas.Controls.Add(_abilitiesOverlay);
        _advancedOverlay.BringToFront();
        _abilitiesOverlay.BringToFront();
        return canvas;
    }

    private Control BuildHeroCard()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Top,
            Height = HeroCardHeight,
            Margin = new Padding(0, 2, 0, 6),
            Padding = new Padding(18, 12, 18, 12),
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));  // 0 status
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));  // 1 live
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));  // 2 strip
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 3 Spells header
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 8 Modes header
        for (var i = 0; i < 2; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));

        // Status row: lamp + state + class badge.
        _stateLabel.AutoSize = false;
        _stateLabel.Dock = DockStyle.Fill;
        _stateLabel.AutoEllipsis = true;
        _stateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _stateLabel.Font = DesignTokens.Type(10.5F, FontStyle.Bold);
        _stateLabel.ForeColor = DesignTokens.TextPrimary;
        _stateLabel.BackColor = Color.Transparent;
        _stateLabel.Text = "Stopped";
        _stateLabel.AccessibleName = "Engine state";
        _linkLamp.Dock = DockStyle.Left;
        _linkLamp.Width = 26;
        _classBadge.Dock = DockStyle.Right;
        _classBadge.Width = 150;
        _classBadge.Text = "AUTO DETECT";
        _classBadge.Font = DesignTokens.Type(8.25F, FontStyle.Bold);
        var statusRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        statusRow.Controls.Add(_stateLabel);
        statusRow.Controls.Add(_linkLamp);
        statusRow.Controls.Add(_classBadge);
        layout.Controls.Add(statusRow, 0, 0);

        // Live row: what MaxDps suggests and why (ellipsis + tooltip).
        _liveValue.AutoSize = false;
        _liveValue.Dock = DockStyle.Fill;
        _liveValue.AutoEllipsis = true;
        _liveValue.TextAlign = ContentAlignment.MiddleLeft;
        _liveValue.Font = DesignTokens.Type(DesignTokens.BodySize);
        _liveValue.ForeColor = DesignTokens.TextSecondary;
        _liveValue.BackColor = Color.Transparent;
        _liveValue.Text = "Now: -";
        _liveValue.AccessibleName = "Current suggestion";
        var liveTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        liveTip.SetToolTip(_liveValue, "What the companion is about to send and why");
        layout.Controls.Add(_liveValue, 0, 1);

        // Strip row: the actual decoded bridge cells.
        _stripView.Dock = DockStyle.Fill;
        var stripRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        stripRow.Controls.Add(_stripView);
        layout.Controls.Add(stripRow, 0, 2);

        layout.Controls.Add(GroupHeaderFor("Spells"), 0, 3);
        layout.Controls.Add(TwoToggleRow("Main", "Core rotation", _main, "Offensive", "Burst cooldowns", _offensive, alt: false), 0, 4);
        layout.Controls.Add(TwoToggleRow("Defensive", "Mitigation and absorbs", _defensives, "Interrupt", "Kick casts", _interrupt, alt: true), 0, 5);
        layout.Controls.Add(TwoToggleRow("Self-heal", "Solo self-sustain", _selfHeal, "Mobility", "Gap closers", _mobility, alt: false), 0, 6);
        layout.Controls.Add(TwoToggleRow("Consumable", "Potions", _consumable, "Trinket", "On-use trinkets", _trinket, alt: true), 0, 7);

        layout.Controls.Add(GroupHeaderFor("Modes"), 0, 8);
        layout.Controls.Add(TwoToggleRow("Solo", "Self-sustain mode", _solo2, "Out of combat", "Run outside combat", _outOfCombat, alt: false), 0, 9);
        layout.Controls.Add(TwoToggleRow("Auto-target", "Target when needed", _autoTarget, "Auto-interact", "Interact when needed", _autoInteract, alt: true), 0, 10);

        card.Controls.Add(layout);
        return card;
    }

    // Modes "Solo" is a dedicated toggle that mirrors the Advanced checkbox.
    private readonly ToggleSwitch _solo2 = new();

    private static GroupHeader GroupHeaderFor(string title) =>
        new() { Text = title, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0) };

    private static Control TwoToggleRow(
        string titleA, string hintA, ToggleSwitch toggleA,
        string titleB, string hintB, ToggleSwitch toggleB,
        bool alt)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        toggleA.AccessibleName = titleA;
        toggleA.AccessibleDescription = hintA;
        toggleB.AccessibleName = titleB;
        toggleB.AccessibleDescription = hintB;
        grid.Controls.Add(new SettingRow(titleA, hintA, toggleA) { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), AlternateFill = alt }, 0, 0);
        grid.Controls.Add(new SettingRow(titleB, hintB, toggleB) { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), AlternateFill = alt }, 1, 0);
        return grid;
    }

    private Control BuildButtonRow1()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2),
            Padding = new Padding(0, 5, 0, 5),
        };
        for (var i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3F));
        foreach (var button in new[] { _start, _stop, _launchGame })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 0, 4, 0);
            button.Font = DesignTokens.Type(9.5F, FontStyle.Bold);
            button.TrailingGlyph = null;
        }
        _start.AccentColor = DesignTokens.Accent;
        _stop.AccentColor = DesignTokens.Danger;
        _launchGame.AccentColor = ConsolePalette.Brass;
        row.Controls.Add(_start, 0, 0);
        row.Controls.Add(_stop, 1, 0);
        row.Controls.Add(_launchGame, 2, 0);
        return row;
    }

    private Control BuildButtonRow2()
    {
        _advancedEntry = new ChamferButton { Text = "Advanced\u2026", Role = ButtonRole.Ghost, AccentColor = ConsolePalette.Brass };
        _advancedEntry.Click += (_, _) => ShowAdvanced();
        _abilitiesEntry = new ChamferButton { Text = "Abilities\u2026", Role = ButtonRole.Ghost };
        _abilitiesEntry.Click += (_, _) => ShowAbilities();

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2),
            Padding = new Padding(0, 5, 0, 5),
        };
        for (var i = 0; i < 4; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        foreach (var button in new[] { _recalibrate, _abilitiesEntry, _advancedEntry, _openFolder })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 0, 4, 0);
            button.Font = DesignTokens.Type(9.5F, FontStyle.Bold);
            button.TrailingGlyph = null;
        }
        _recalibrate.AccentColor = ConsolePalette.Brass;
        _openFolder.AccentColor = DesignTokens.Surface;
        row.Controls.Add(_recalibrate, 0, 0);
        row.Controls.Add(_abilitiesEntry, 1, 0);
        row.Controls.Add(_advancedEntry, 2, 0);
        row.Controls.Add(_openFolder, 3, 0);
        return row;
    }

    private Control BuildStatusLine()
    {
        _statusLine.AutoSize = false;
        _statusLine.Dock = DockStyle.Fill;
        _statusLine.AutoEllipsis = true;
        _statusLine.TextAlign = ContentAlignment.MiddleLeft;
        _statusLine.Font = DesignTokens.Type(DesignTokens.MetaSize);
        _statusLine.ForeColor = DesignTokens.TextMuted;
        _statusLine.BackColor = Color.Transparent;
        _statusLine.Text = "Stopped";
        _statusLine.AccessibleName = "Status message";
        return _statusLine;
    }

    // ----- Advanced popup (tabs Configuration | Diagnostics | Intelligence) -----

    private Panel BuildAdvancedOverlay()
    {
        var (scrim, tabs) = BuildPopup("Advanced", 620, onClose: HideAdvanced);
        _advancedTabs = tabs;
        AddTab(tabs, "Configuration", _config);
        AddTab(tabs, "Diagnostics", _diagnostics);
        AddTab(tabs, "Intelligence", _intelligencePage);
        return scrim;
    }

    private Panel BuildAbilitiesOverlay()
    {
        var (scrim, tabs) = BuildPopup("Abilities", 900, onClose: HideAbilities);
        _abilitiesTabs = tabs;
        AddTab(tabs, "Class skills", _classSkills!);
        AddTab(tabs, "Explorer", _explorer);
        return scrim;
    }

    private static void AddTab(TabControl tabs, string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = DesignTokens.Background,
            Padding = Padding.Empty,
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
    }

    /// <summary>
    /// Dimmed scrim + centred card (width min(client-40, <paramref name="maxWidth"/>),
    /// height client-60) with a header (title + Back) and a tab host.
    /// </summary>
    private (Panel Scrim, TabControl Tabs) BuildPopup(string title, int maxWidth, Action onClose)
    {
        var scrim = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(228, 7, 9, 11),
            Visible = false,
        };
        var popup = new RoundedCard
        {
            Size = new Size(maxWidth, 520),
            Anchor = AnchorStyles.None,
            Padding = new Padding(6, 6, 6, 6),
        };
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            Font = DesignTokens.Type(11.5F, FontStyle.Bold),
            ForeColor = DesignTokens.TextPrimary,
            BackColor = Color.Transparent,
        };
        var back = new ChamferButton
        {
            Text = "Back",
            Role = ButtonRole.Ghost,
            Dock = DockStyle.Right,
            Width = 96,
            AutoSize = false,
            Margin = new Padding(0, 4, 8, 4),
            TrailingGlyph = null,
        };
        back.Click += (_, _) => onClose();
        header.Controls.Add(titleLabel);
        header.Controls.Add(back);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = DesignTokens.Type(DesignTokens.BodySize) };

        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(tabs, 0, 1);
        popup.Controls.Add(shell);
        scrim.Controls.Add(popup);

        void Center()
        {
            var w = Math.Min(maxWidth, Math.Max(360, scrim.ClientSize.Width - 40));
            var h = Math.Max(280, scrim.ClientSize.Height - 60);
            popup.Size = new Size(w, h);
            popup.Location = new Point(
                Math.Max(0, (scrim.ClientSize.Width - w) / 2),
                Math.Max(0, (scrim.ClientSize.Height - h) / 2));
        }
        scrim.Resize += (_, _) => Center();
        scrim.Layout += (_, _) => Center();
        return (scrim, tabs);
    }

    private void ShowAdvanced()
    {
        _engine.WantDiagnostics = true;
        // A4: each tab is built lazily once (never by a timer), so the window
        // opens fast and the configuration/diagnostics tree is only created
        // when the popup is first opened.
        if (!_advancedContentBuilt)
        {
            _advancedContentBuilt = true;
            BuildConfigurationContent(_config);
            BuildDiagnosticsContent(_diagnostics);
        }
        _intelligencePage.EnsureBuilt();
        if (_mainBody is not null) _mainBody.Visible = false;
        _advancedTabs.SelectedIndex = 0;
        _advancedOverlay.Visible = true;
        _advancedOverlay.BringToFront();
        _advancedOverlay.PerformLayout();
        _advancedOverlay.Focus();
    }

    private void HideAdvanced()
    {
        if (_advancedOverlay is null) return;
        _advancedOverlay.Visible = false;
        _engine.WantDiagnostics = false;
        if (_mainBody is not null) _mainBody.Visible = true;
    }

    private void ShowAbilities()
    {
        if (_mainBody is not null) _mainBody.Visible = false;
        _abilitiesTabs.SelectedIndex = 0;
        _abilitiesOverlay.Visible = true;
        _abilitiesOverlay.BringToFront();
        _classSkills?.Open(
            _engine.TryGetLiveClass(out var cls) ? cls : null,
            _engine.TryGetLiveSpec(out var spec) ? spec : null);
        _abilitiesOverlay.PerformLayout();
        _abilitiesOverlay.Focus();
    }

    private void HideAbilities()
    {
        if (_abilitiesOverlay is null) return;
        _abilitiesOverlay.Visible = false;
        if (_mainBody is not null) _mainBody.Visible = true;
    }

    private bool AnyPopupVisible => (_advancedOverlay?.Visible ?? false) || (_abilitiesOverlay?.Visible ?? false);

    private static void AssignTabOrder(Control parent, ref int index)
    {
        foreach (Control child in parent.Controls)
        {
            child.TabIndex = index++;
            if (child.HasChildren) AssignTabOrder(child, ref index);
        }
    }

    // ----- configuration page content (existing settings, grouped) -----

    private void BuildConfigurationContent(ConfigurationPage page)
    {
        page.AddCard("Automation", "Engine").Add(Stack(
            (CheckRow(_scheduler), 30),
            (Hint("On = deterministic ordering and pacing: frozen-heartbeat link hold, interrupt/defensive urgency, GCD and key-interval gates. Off = legacy loop."), 0),
            (CheckRow(_intelligence), 30),
            (Hint("Evaluates every situational suggestion USE / HOLD / SKIP against the ability knowledge base and live combat context. The main rotation is never gated."), 0)));

        page.AddCard("Combat", "Slots").Add(TwoCol(64,
            RowFor("Main rotation", "Your damage rotation - MaxDps decides", _main),
            RowFor("Offensive", "Burst and damage cooldowns", _offensive),
            RowFor("Defensive", "Mitigation, absorbs, immunities", _defensives),
            RowFor("Consumable", "Potions (health and mana)", _consumable),
            RowFor("Trinket", "On-use trinket effects", _trinket),
            RowFor("Mobility", "Charge in when out of range", _mobility),
            RowFor("Self-heal", "Solo mode self-sustain", _selfHeal),
            RowFor("Interrupt", "Kick interruptible casts", _interrupt)));

        page.AddCard("Safety", "Modes").Add(Stack(
            (CheckRow(_solo), 30),
            (Hint("Requires Combat intelligence. Below 65% HP efficient self-heals become eligible; below 35% HP survival actions outrank damage. Emergency cooldowns are preserved while HP is safe."), 0),
            (CheckRow(_combatOnly), 30),
            (Hint("When on, the companion only acts while you are in combat."), 0)));

        page.AddCard("Targeting", "Assist").Add(Stack(
            (ToggleField("Auto-target (press Target key)", _autoTarget), 38),
            (ToggleField("Auto-interact (press Interact key)", _autoInteract), 38),
            (Ui.FieldRow("Target key", _targetKey), 38),
            (Ui.FieldRow("Interact key", _interactKey), 38)));

        page.AddCard("Input", "Keys").Add(Stack(
            (Ui.FieldRow("Pause hotkey", _pauseHotkey), 38),
            (CheckRow(_allowBackground), 30),
            (Hint("Keyboard slots keep working while the game is in the background; mouse/interact always need focus."), 0)));

        page.AddCard("Bridge", "Attach").Add(Stack(
            (Ui.FieldRow("Game process", _processName), 38),
            (Hint("Which game window to attach to (without .exe). Recalibrate locates the strip; the engine re-aligns automatically if it moves."), 0)));

        page.AddCard("Advanced", "Tools").Add(ButtonsRow(44, _classSkillsEntryBtn(), _openFolder));
    }

    private ChamferButton _classSkillsEntryBtn()
    {
        _classSkillsEntry = new ChamferButton
        {
            Text = "Abilities\u2026",
            Role = ButtonRole.Ghost,
            AccentColor = DesignTokens.Accent,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TrailingGlyph = null,
        };
        _classSkillsEntry.Click += (_, _) => ShowAbilities();
        return _classSkillsEntry;
    }

    // ----- diagnostics page content -----

    private void BuildDiagnosticsContent(DiagnosticsPage page)
    {
        foreach (var label in new[] { _protocolValue, _bridgeStateValue, _slotsValue, _lastKeyValue, _decisionValue, _rawValue })
        {
            label.Font = DesignTokens.Type(DesignTokens.MetaSize);
            label.ForeColor = DesignTokens.TextSecondary;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Text = "-";
        }

        page.AddCard("Protocol / bridge", "Live").Add(Stack(
            (Ui.FieldRow("Protocol", _protocolValue), 26),
            (Ui.FieldRow("Bridge state", _bridgeStateValue), 26),
            (Ui.FieldRow("Slots", _slotsValue), 26),
            (Ui.FieldRow("Last key", _lastKeyValue), 26),
            (Ui.FieldRow("Decision", _decisionValue), 26),
            (Ui.FieldRow("Raw sample", _rawValue), 26)));

        page.AddCard("Telemetry", "Recording").Add(Stack(
            (CheckRow(_telemetry), 30),
            (Hint("Bounded in-memory JSONL ring: decoded keybinds, state flags and timing only. No network, no Blizzard values."), 0),
            (ButtonsRow(40, _telemetryExport, _telemetryReplay), 40),
            (StatusLabel(_telemetryStatus), 24)));

        var findStrip = new ChamferButton { Text = "Find strip", Role = ButtonRole.Ghost, TrailingGlyph = null };
        findStrip.Click += (_, _) => RecalibratePositionOnly();
        page.AddCard("Calibration", "Strip").Add(Stack(
            (ButtonsRow(40, _learnColors, _resetColors), 40),
            (ButtonsRow(40, findStrip, _recalibrate), 40),
            (Ui.FieldRow("Tolerance", _tolerance), 38),
            (StatusLabel(_colorStatus), 24)));

        page.AddCard("Timing", "Cadence").Add(Stack(
            (Ui.FieldRow("Poll (ms)", _pollInterval), 38),
            (Ui.FieldRow("Key hold (ms)", _keyPress), 38),
            (Ui.FieldRow("Min key gap (ms)", _minKeyInterval), 38)));

        page.AddCard("Strip", "Pixels").Add(Stack(
            (Ui.FieldRow("Cell size (px)", _cellSize), 38),
            (Ui.FieldRow("Block offset X", _offsetX), 38),
            (Ui.FieldRow("Block offset Y", _offsetY), 38)));

        page.AddCard("Patch / registry audit", "Provenance").Add(Stack(
            (Hint("Coverage is computed from AbilityCatalog.Default via AbilityCoverage.Build."), 0),
            (StatusLabel(_auditValue), 28)));

        page.AddCard("Battle.net / tools", "Launch").Add(Stack(
            (Ui.FieldRow("BNet path", _bnetPath), 38),
            (ButtonsRow(40, _bnetBrowse, _launchGame, _openFolder), 40),
            (Hint("Launch Game opens Battle.net for WoW. No credentials are stored - the launcher's remembered account is used."), 0)));
    }

    private static Label StatusLabel(Label label)
    {
        label.AutoSize = false;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Font = DesignTokens.Type(DesignTokens.BodySize);
        label.ForeColor = DesignTokens.TextSecondary;
        label.BackColor = Color.Transparent;
        label.Text = "-";
        return label;
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(860, 0),
        Font = DesignTokens.Type(DesignTokens.BodySize),
        ForeColor = DesignTokens.TextSecondary,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 2),
    };

    private static Control CheckRow(CheckBox box)
    {
        box.AutoSize = true;
        box.ForeColor = DesignTokens.TextPrimary;
        box.BackColor = Color.Transparent;
        box.Font = DesignTokens.Type(DesignTokens.BodySize);
        box.AccessibleName = box.Text;
        box.Margin = new Padding(0, 2, 0, 2);
        return box;
    }

    private static Control ToggleField(string caption, ToggleSwitch toggle)
    {
        toggle.Anchor = AnchorStyles.Left;
        toggle.Margin = new Padding(0, 2, 0, 2);
        toggle.AccessibleName = caption;
        return Ui.FieldRow(caption, toggle);
    }

    /// <summary>
    /// Measured vertical stack (v2.8): the tuple height is a MINIMUM, never a
    /// fixed row — content grows with its own measurement (DPI safe).
    /// </summary>
    private static Control Stack(params (Control Control, int Height)[] rows)
    {
        var stack = new VertStack { Gap = 6 };
        foreach (var (control, height) in rows)
        {
            if (height > 0) control.MinimumSize = new Size(control.MinimumSize.Width, height);
            stack.Controls.Add(control);
        }
        return stack;
    }

    private static Control TwoCol(int rowHeight, params Control[] cells)
    {
        var grid = new GridPanel(2) { Gap = 6, RowGap = 6 };
        foreach (var cell in cells)
        {
            if (rowHeight > 0) cell.MinimumSize = new Size(0, rowHeight);
            grid.Controls.Add(cell);
        }
        return grid;
    }

    private static Control ButtonsRow(int height, params ChamferButton[] buttons)
    {
        var grid = new GridPanel(buttons.Length) { Gap = DesignTokens.SpaceS, RowGap = 0 };
        foreach (var button in buttons)
        {
            button.AutoSize = false;
            button.Dock = DockStyle.None;
            button.TrailingGlyph = button.Role == ButtonRole.Primary ? "\u2197" : null;
            if (height > 0) button.MinimumSize = new Size(0, height);
            grid.Controls.Add(button);
        }
        return grid;
    }

    private static SettingRow RowFor(string title, string subtitle, ToggleSwitch toggle)
    {
        toggle.AccessibleName = title;
        toggle.AccessibleDescription = subtitle;
        return new SettingRow(title, subtitle, toggle) { Dock = DockStyle.None, Margin = Padding.Empty };
    }

    private void ShowClassSkills()
    {
        if (_classSkills is null) return;
        ShowAbilities();
        _abilitiesTabs.SelectedIndex = 0;
        _classSkills.Open(
            _engine.TryGetLiveClass(out var className) ? className : null,
            _engine.TryGetLiveSpec(out var specName) ? specName : null);
    }

    // ----- app icon -----

    /// <summary>The generated app icon for other full-screen views (Class skills).</summary>
    internal static Image? AppIconForUi() => AppIcon();

    private static Image? AppIcon()
    {
        if (_appIcon is not null) return _appIcon;
        try
        {
            var iconPath = Path.Combine(Program.AppDir, "assets", "icon-256.png");
            if (File.Exists(iconPath))
            {
                using var stream = File.OpenRead(iconPath);
                using var raw = Image.FromStream(stream);
                _appIcon = new Bitmap(raw);
            }
        }
        catch { /* loose art missing: the title text carries the identity */ }
        return _appIcon;
    }

    // ----- tray (minimize-only; close really exits) -----

    private void BuildTray()
    {
        _trayMenu = new ContextMenuStrip();
        _trayStartStop = new ToolStripMenuItem("Start", null, (_, _) => ToggleEngine());
        var show = new ToolStripMenuItem("Show", null, (_, _) => Restore());
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => Close());
        _trayMenu.Items.Add(_trayStartStop);
        _trayMenu.Items.Add(show);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exit);

        _tray = new NotifyIcon
        {
            Text = "MaxDPS Companion",
            ContextMenuStrip = _trayMenu,
            Visible = false,
        };
        try
        {
            var ico = Path.Combine(Program.AppDir, "assets", "companion.ico");
            if (File.Exists(ico)) _tray.Icon = new Icon(ico);
            else _tray.Icon = Icon;
        }
        catch { try { _tray.Icon = Icon; } catch { /* headless test host: tray is best-effort */ } }
        _tray.DoubleClick += (_, _) => Restore();
    }

    private void Restore()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        _tray!.Visible = false;
        Activate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _tray is not null)
        {
            Hide();
            ShowInTaskbar = false;
            _tray.Visible = true;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.EnableModernCorners(Handle);
        RegisterPauseHotkey();
    }

    private static void StyleInputs(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            switch (child)
            {
                case TextBox box:
                    box.BackColor = DesignTokens.SurfaceElevated;
                    box.ForeColor = DesignTokens.TextPrimary;
                    box.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown spin:
                    spin.BackColor = DesignTokens.SurfaceElevated;
                    spin.ForeColor = DesignTokens.TextPrimary;
                    break;
                case CheckBox check:
                    check.ForeColor = DesignTokens.TextPrimary;
                    check.BackColor = Color.Transparent;
                    break;
            }
            if (child.HasChildren) StyleInputs(child);
        }
    }

    private static WheelSafeNumeric Spin(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = 90, AccessibleName = $"numeric {min} to {max}" };

    // ----- one-click color calibration (cancelable worker) -----

    private Thread? _calThread;
    private volatile bool _calCancel;
    private volatile bool _calibrating;

    private void LearnColors() => RecalibrateFull();

    private void SetCalibrating(bool running)
    {
        if (InvokeRequired) { BeginInvoke(new Action<bool>(SetCalibrating), running); return; }
        _calibrating = running;
        _recalibrate.Text = running ? "Cancel" : "Recalibrate";
        _learnColors.Text = running ? "Cancel calibration" : "Calibrate colors";
        _learnColors.Enabled = true;
        _recalibrate.Enabled = true;
        _start.Enabled = !running && !_engine.IsRunning;
    }

    private void SetColorStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(SetColorStatus), text); return; }
        _colorStatus.Text = text;
        if (_calibrating || text.StartsWith("Calibrat", StringComparison.Ordinal))
            SetStatus(text, DesignTokens.Accent);
    }

    private void CalibrateWorker()
    {
        var game = new WowWindow();
        bool Cancelled() => _calCancel;
        try
        {
            if (!game.Refresh(_settings.ProcessName) || !game.TryGetClientOrigin(out var origin, out var size))
            {
                SetColorStatus($"Calibration failed - no process named '{_settings.ProcessName}'.");
                return;
            }

            SetColorStatus("Calibrating - starting the pattern in game...");
            if (Cancelled()) return;
            if (!ChatCommander.SendChatCommandSilent(game, "mdb calibrate on"))
            {
                SetColorStatus("Calibration failed - could not focus the game window.");
                return;
            }
            Thread.Sleep(600);
            if (Cancelled()) return;

            BlockLocation? block = BlockLocator.Locate(origin, size, _settings.Color);
            if (Cancelled()) return;
            if (block is not { } found || !PatternVisible(origin, found))
            {
                SetColorStatus("Calibration failed - no pattern on screen. Addon updated? (/reload)");
                BeginInvoke(() => MessageBox.Show(
                    "No calibrate pattern on screen. Check in order:\n\n"
                    + "  1. In game, type: /mdb calibrate on - you must see\n"
                    + "     'MDB: calibrate pattern ON'. If not: /reload first.\n"
                    + "  2. Display Mode must be Windowed or Borderless\n"
                    + "     (exclusive Fullscreen is invisible to capture).\n"
                    + "  3. The game must be visible, not covered.\n"
                    + "  4. Then Calibrate colors again.",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                ChatCommander.SendChatCommandSilent(game, "mdb calibrate off");
                return;
            }
            var known = found;

            _settings.OffsetX = known.OffsetX;
            _settings.OffsetY = known.OffsetY;
            _settings.CellSize = known.CellSize;
            BeginInvoke(() =>
            {
                _offsetX.Value = known.OffsetX;
                _offsetY.Value = known.OffsetY;
                _cellSize.Value = known.CellSize;
            });

            ColorLearner.LearnResult? result = null;
            var sampledVersion = 0;
            using var sampler = new ScreenSampler();
            SetColorStatus("Calibrating - sampling (keyboard locked, Cancel to stop)...");
            using (new InputLock().Install())
            {
                var lastNote = "";
                Color[] SampleBoth(Point at, int cell)
                {
                    var full = sampler.Sample(at, cell);
                    if (ColorLearner.Classify(full, _settings.Color) >= 0)
                    {
                        sampledVersion = PixelProtocol.SupportedVersion;
                        return full;
                    }
                    var v4 = PixelProtocol.TrimToV4(full);
                    if (ColorLearner.Classify(v4, _settings.Color) >= 0)
                    {
                        sampledVersion = PixelProtocol.SupportedVersionV4;
                        return v4;
                    }
                    var old = PixelProtocol.TrimToV1(full);
                    if (ColorLearner.Classify(old, _settings.Color) >= 0)
                        sampledVersion = PixelProtocol.SupportedVersionV1;
                    return old;
                }
                result = ColorLearner.Learn(
                    SampleBoth,
                    new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY),
                    known.CellSize,
                    _settings.Color,
                    (int)_tolerance.Value,
                    Cancelled,
                    note => { if (note != lastNote) { lastNote = note; SetColorStatus(note); } });
            }
            if (result is not null && sampledVersion != PixelProtocol.SupportedVersion)
            {
                var which = sampledVersion == PixelProtocol.SupportedVersionV1 ? "v1" : "v4";
                SetColorStatus($"Calibrated against the OLD ({which}) addon - /reload + reinstall the addon, then recalibrate.");
                BeginInvoke(() => MessageBox.Show(
                    $"Calibration succeeded against the OLD {which} bridge addon.\n\n"
                    + "Your in-game addon is stale: run install-addon.ps1, then\n"
                    + "/reload in game, then Recalibrate again so the current v5\n"
                    + "strip (with the mobility/self-heal slots and combat context)\n"
                    + "is what gets learned.",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }

            ChatCommander.SendChatCommandSilent(game, "mdb calibrate off", settleMs: 400);

            if (Cancelled())
            {
                SetStatus("Calibration cancelled - pattern turned off.", DesignTokens.TextPrimary);
                SetColorStatus("Calibration cancelled - pattern turned off.");
                return;
            }

            if (result is not { } learned)
            {
                SetStatus("Calibration failed - pattern not stable.", DesignTokens.Danger);
                SetColorStatus("Calibration failed - pattern not stable.");
                BeginInvoke(() => MessageBox.Show(
                    "No stable pattern was sampled.\n\nCheck that:\n"
                    + "  - the client is windowed or borderless\n"
                    + "  - the strip is not covered by another window",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information));
                return;
            }

            var profile = _settings.Color;
            profile.MagicR = learned.Profile.MagicR;
            profile.MagicG = learned.Profile.MagicG;
            profile.MagicB = learned.Profile.MagicB;
            for (var i = 0; i < 3; i++)
            {
                profile.Black[i] = learned.Profile.Black[i];
                profile.White[i] = learned.Profile.White[i];
            }
            profile.Tolerance = learned.Profile.Tolerance;
            profile.LearnedAt = learned.Profile.LearnedAt;
            SaveSettings();
            BeginInvoke(RefreshColorStatus);
            SetStatus(learned.Note + " - saved.", DesignTokens.TextPrimary);
            SetColorStatus(learned.Note + " - saved.");
        }
        finally
        {
            SetCalibrating(false);
        }
    }

    private bool PatternVisible(Point origin, BlockLocation known)
    {
        try
        {
            using var sampler = new ScreenSampler();
            var at = new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY);
            var full = sampler.Sample(at, known.CellSize);
            if (ColorLearner.Classify(full, _settings.Color) >= 0) return true;
            if (ColorLearner.Classify(PixelProtocol.TrimToV4(full), _settings.Color) >= 0) return true;
            return ColorLearner.Classify(PixelProtocol.TrimToV1(full), _settings.Color) >= 0;
        }
        catch
        {
            return false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _calCancel = true;
        base.OnFormClosing(e);
    }

    private void ResetColors()
    {
        var profile = _settings.Color;
        profile.MagicR = 255; profile.MagicG = 0; profile.MagicB = 255;
        for (var i = 0; i < 3; i++) { profile.Black[i] = 0; profile.White[i] = 255; }
        profile.Tolerance = 64;
        profile.LearnedAt = "";
        SaveSettings();
        RefreshColorStatus();
    }

    private void RefreshColorStatus()
    {
        var profile = _settings.Color;
        _tolerance.Value = Math.Clamp(profile.Tolerance, (int)_tolerance.Minimum, (int)_tolerance.Maximum);
        _colorStatus.Text = profile.IsLearned
            ? $"Colors {profile.LearnedAt} - separation {profile.Separation()}"
            : "No color profile - legacy magenta detection.";
    }

    private void BrowseBNet()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Battle.net|Battle.net.exe|All files|*.*",
            FileName = "Battle.net.exe",
        };
        try
        {
            var detected = string.IsNullOrWhiteSpace(_bnetPath.Text) ? BattleNetLauncher.InstallPath : _bnetPath.Text;
            if (!string.IsNullOrWhiteSpace(detected))
            {
                var dir = Path.GetDirectoryName(detected);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
            }
        }
        catch { /* best effort */ }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _bnetPath.Text = dialog.FileName;
            SaveNow();
        }
    }

    private void LaunchGame()
    {
        ApplyToSettings();
        var path = string.IsNullOrWhiteSpace(_settings.BNetPath) ? null : _settings.BNetPath;
        var (ok, message) = Launcher(path);
        if (ok)
            SetStatus(message, DesignTokens.TextPrimary);
        else
            MessageBox.Show(message, "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ----- settings <-> controls -----

    private void LoadFromSettings()
    {
        _processName.Text = _settings.ProcessName;
        _offsetX.Value = _settings.OffsetX;
        _offsetY.Value = _settings.OffsetY;
        _cellSize.Value = _settings.CellSize;
        _pollInterval.Value = _settings.PollIntervalMs;
        _minKeyInterval.Value = _settings.MinKeyIntervalMs;
        _keyPress.Value = _settings.KeyPressMs;
        _pauseHotkey.Text = _settings.PauseHotkey;
        _allowBackground.Checked = _settings.AllowBackgroundKeys;

        _main.Checked = SlotFlag(0, _main.Checked);
        _offensive.Checked = SlotFlag(1, _offensive.Checked);
        _defensives.Checked = SlotFlag(2, _defensives.Checked);
        _consumable.Checked = SlotFlag(3, _consumable.Checked);
        _trinket.Checked = SlotFlag(4, _trinket.Checked);
        _interrupt.Checked = SlotFlag(5, _interrupt.Checked);
        _mobility.Checked = SlotFlag(6, _mobility.Checked);
        _selfHeal.Checked = SlotFlag(7, _selfHeal.Checked);

        SetCombatOnly(_settings.CombatOnly);

        _autoTarget.Checked = _settings.AutoTargetEnabled;
        _autoInteract.Checked = _settings.InteractEnabled;
        _scheduler.Checked = _settings.SchedulerEnabled;
        _intelligence.Checked = _settings.IntelligenceEnabled;
        _solo.Checked = _settings.SoloEnabled;
        _solo2.Checked = _settings.SoloEnabled;
        _telemetry.Checked = _settings.TelemetryEnabled;
        _targetKey.Text = _settings.TargetKey;
        _interactKey.Text = _settings.InteractKey;
        _bnetPath.Text = _settings.BNetPath;
        RefreshColorStatus();
    }

    private void ApplyToSettings()
    {
        _settings.ProcessName = _processName.Text.Trim();
        _settings.OffsetX = (int)_offsetX.Value;
        _settings.OffsetY = (int)_offsetY.Value;
        _settings.CellSize = (int)_cellSize.Value;
        _settings.PollIntervalMs = (int)_pollInterval.Value;
        _settings.MinKeyIntervalMs = (int)_minKeyInterval.Value;
        _settings.KeyPressMs = (int)_keyPress.Value;
        _settings.PauseHotkey = _pauseHotkey.Text.Trim();
        _settings.AllowBackgroundKeys = _allowBackground.Checked;
        _settings.SlotEnabled[0] = _main.Checked;
        _settings.SlotEnabled[1] = _offensive.Checked;
        _settings.SlotEnabled[2] = _defensives.Checked;
        _settings.SlotEnabled[3] = _consumable.Checked;
        _settings.SlotEnabled[4] = _trinket.Checked;
        _settings.SlotEnabled[5] = _interrupt.Checked;
        _settings.SlotEnabled[6] = _mobility.Checked;
        _settings.SlotEnabled[7] = _selfHeal.Checked;
        _settings.CombatOnly = !_outOfCombat.Checked;
        _settings.AutoTargetEnabled = _autoTarget.Checked;
        _settings.InteractEnabled = _autoInteract.Checked;
        _settings.SchedulerEnabled = _scheduler.Checked;
        _settings.IntelligenceEnabled = _intelligence.Checked;
        _settings.SoloEnabled = _solo.Checked;
        _settings.TelemetryEnabled = _telemetry.Checked;
        var targetKey = _targetKey.Text.Trim();
        _settings.TargetKey = string.IsNullOrWhiteSpace(targetKey) ? "Tab" : targetKey;
        var interactKey = _interactKey.Text.Trim();
        _settings.InteractKey = string.IsNullOrWhiteSpace(interactKey) ? "F" : interactKey;
        _settings.BNetPath = _bnetPath.Text.Trim();
        _settings.Color.Tolerance = (int)_tolerance.Value;
    }

    // ----- engine -----

    private void ToggleEngine()
    {
        if (_engine.IsRunning) StopEngine();
        else StartEngine();
    }

    private void StartEngine()
    {
        if (_engine.IsRunning) return;
        ApplyToSettings();

        var game = new WowWindow();
        if (!game.Refresh(_settings.ProcessName))
        {
            SetStatus($"no process named '{_settings.ProcessName}'", DesignTokens.Danger);
            MessageBox.Show(
                $"No process named '{_settings.ProcessName}'.\n\nStart WoW first (Launch Game), then press Start.",
                "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (game.TryGetClientOrigin(out var origin, out var size)
            && BlockLocator.Locate(origin, size, _settings.Color) is { } found)
            ApplyLocation(found);

        _engine.Paused = false;
        _engine.Start();
        _start.Enabled = false;
        _stop.Enabled = true;
        if (_trayStartStop is not null) _trayStartStop.Text = "Stop";
        SetStatus("Engine started.", DesignTokens.Success);
    }

    private void StopEngine()
    {
        if (!_engine.IsRunning) return;
        _engine.Stop();
        _start.Enabled = true;
        _stop.Enabled = false;
        if (_trayStartStop is not null) _trayStartStop.Text = "Start";
        SetStatus("Engine stopped.", DesignTokens.TextMuted);
    }

    // ----- local rotation telemetry -----

    private void SyncTelemetryRecorder()
    {
        if (_telemetry.Checked)
        {
            _telemetryRecorder ??= new TelemetryRecorder(_settings.TelemetryCapacity);
            _engine.Telemetry = _telemetryRecorder;
            if (_engine.IsRunning)
                _telemetryRecorder.Append(TelemetryEvent.Session(
                    _engine.ElapsedMs, Native.AppVersion, PixelProtocol.SupportedVersion,
                    _telemetryRecorder.Capacity, recording: true, note: "attached"));
        }
        else
        {
            _engine.Telemetry = null;
        }
        UpdateTelemetryStatus();
    }

    private void UpdateTelemetryStatus()
    {
        var recorder = _telemetryRecorder;
        var text = recorder switch
        {
            null => "off - enable recording, run the engine, then Export",
            { Count: 0 } => _telemetry.Checked ? "armed - press Start to capture" : "buffer empty",
            _ => $"{recorder.Count} events buffered ({recorder.Dropped} dropped)" + (_telemetry.Checked ? " - recording" : " - recording off"),
        };
        if (_telemetryStatus.Text != text) _telemetryStatus.Text = text;
    }

    private void SetTelemetryStatus(string text) => _telemetryStatus.Text = text;

    private void ExportTelemetry()
    {
        if (_telemetryRecorder is not { } recorder || recorder.Count == 0)
        {
            SetTelemetryStatus("nothing to export - enable recording and Start the engine first");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Export rotation telemetry",
            Filter = "JSONL telemetry (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            InitialDirectory = Program.AppDir,
            FileName = $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = recorder.Export(dialog.FileName);
            SetTelemetryStatus($"exported {count} events -> {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetTelemetryStatus($"export failed: {ex.Message}");
        }
    }

    private void ReplayTelemetry()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Replay rotation telemetry",
            Filter = "JSONL telemetry (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            InitialDirectory = Program.AppDir,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = ReplayRunner.RunFile(dialog.FileName);
            SetTelemetryStatus($"replayed {result.Ticks} ticks ({result.Errors} errors, {result.Mismatches} mismatches) -> {result.ReportPath}");
        }
        catch (Exception ex)
        {
            SetTelemetryStatus($"replay failed: {ex.Message}");
        }
    }

    private void RecalibrateFull()
    {
        if (_calThread is { IsAlive: true })
        {
            _calCancel = true;
            return;
        }
        if (_engine.IsRunning)
        {
            MessageBox.Show(
                "Stop the engine first, then Recalibrate.\n\n"
                + "The calibrate pattern reports Paused, so a running engine would just hold.",
                "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplyToSettings();
        _calCancel = false;
        SetCalibrating(true);
        _calThread = new Thread(CalibrateWorker) { IsBackground = true, Name = "MaxDpsCompanion.Calibrate" };
        _calThread.Start();
    }

    private void RecalibratePositionOnly()
    {
        ApplyToSettings();

        var location = RotationEngine.Locate(_settings, _settings.Color, out var message);
        if (location is { } found)
        {
            ApplyLocation(found);
            SetStatus(message, DesignTokens.Accent);
            return;
        }

        SetStatus(message, DesignTokens.Danger);
        MessageBox.Show(
            $"{message}.\n\nCheck that:\n"
            + "  - the addon is loaded ( /mdb status in game )\n"
            + "  - the client is windowed or borderless, not exclusive fullscreen\n"
            + "  - the strip is not covered by another window",
            "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ApplyLocation(BlockLocation location)
    {
        _settings.OffsetX = location.OffsetX;
        _settings.OffsetY = location.OffsetY;
        _settings.CellSize = location.CellSize;
        _offsetX.Value = location.OffsetX;
        _offsetY.Value = location.OffsetY;
        _cellSize.Value = location.CellSize;
    }

    // ----- status refresh (UI timer; independent of the engine tick) -----

    private void RefreshStatus()
    {
        UpdateWindowBorder();

        // Rule, enforced for life: no AutoScrollPosition writes on any timer
        // path. The v2.8.1 NormalizeScroll reset a user's scroll offset on
        // every refresh (A1 regression: ClassicUiTests.ClassicUi_ScrollSurvivesRefresh).

        if (_pendingLocation is { } located)
        {
            _pendingLocation = null;
            ApplyLocation(located);
            SetStatus("Block found, re-aligned.", DesignTokens.Accent);
            return;
        }

        if (_calibrating) return;

        var status = _status;
        if (!_engine.IsRunning)
        {
            if (_statusMessage != "Stopped") SetStatus("Stopped", DesignTokens.TextMuted);
        }
        else
        {
            var verb = Capitalise(status.Message);
            SetStatus(_engine.Paused ? $"Paused - {verb}" : verb, status.BridgeVisible ? DesignTokens.TextPrimary : DesignTokens.Danger);
        }

        var slots = string.IsNullOrEmpty(status.SlotSummary) ? "-" : status.SlotSummary;
        if (_slotsValue.Text != slots) _slotsValue.Text = slots;
        var last = string.IsNullOrEmpty(status.LastKeySent) ? "-" : status.LastKeySent;
        if (_lastKeyValue.Text != last) _lastKeyValue.Text = last;
        var decision = string.IsNullOrEmpty(status.Decision) ? "-" : status.Decision;
        if (_decisionValue.Text != decision) _decisionValue.Text = decision;
        var raw = string.IsNullOrEmpty(status.RawSample) ? "-" : status.RawSample;
        if (_rawValue.Text != raw) _rawValue.Text = raw;
        var bridge = _engine.IsRunning ? status.State.ToString() : "stopped";
        if (_bridgeStateValue.Text != bridge) _bridgeStateValue.Text = bridge;
        if (_protocolValue.Text == "-") _protocolValue.Text = $"v{PixelProtocol.SupportedVersion} (supported)";

        UpdateHero();
    }

    private void UpdateHero()
    {
        if (!_uiInitialised) return;
        var status = _status;
        var running = _engine.IsRunning;

        var (stateText, stateColor) = !running
            ? ("Stopped", DesignTokens.TextMuted)
            : _engine.Paused ? ("Paused", DesignTokens.Warning)
            : status.BridgeVisible ? ("Running", DesignTokens.Success)
            : ("No bridge / window", DesignTokens.Danger);
        if (_stateLabel.Text != stateText) _stateLabel.Text = stateText;
        _stateLabel.ForeColor = stateColor;

        _linkLamp.Dot = !running ? DesignTokens.TextMuted
            : status.BridgeVisible ? DesignTokens.Success : DesignTokens.Danger;

        var classSpec = _engine.TryGetLiveClass(out var cls) && cls is { Length: > 0 }
            ? cls + (_engine.TryGetLiveSpec(out var spec) && spec is { Length: > 0 } ? $" {spec}" : "")
            : "AUTO DETECT";
        if (_classBadge.Text != classSpec) _classBadge.Text = classSpec;

        var current = _engine.CurrentPlanHead;
        var lastAction = _engine.LastAction;
        var action = current?.Action ?? lastAction?.Action ?? "-";
        var why = lastAction is { Why.Count: > 0 } ? string.Join(" \u00b7 ", lastAction.Why)
            : current is { Why.Count: > 0 } ? string.Join(" \u00b7 ", current.Why)
            : "no reasons recorded";
        var live = $"Now: {action} \u2014 {why}";
        if (_liveValue.Text != live) _liveValue.Text = live;
        _liveValue.AccessibleName = live;

        var strip = string.IsNullOrEmpty(status.RawSample) ? "" : status.RawSample;
        if (_stripView.Sample != strip) _stripView.Sample = strip;

        if (_statusLine.Text != _statusMessage) _statusLine.Text = _statusMessage;
        _statusLine.ForeColor = DesignTokens.StatusColor(_statusMessageTone);
    }

    private void SetStatus(string message, Color color)
    {
        _statusMessage = message;
        _statusMessageTone = ToneFor(color);
        if (_uiInitialised)
        {
            // Cheap: only recomputes when the shared key changes.
            UpdateHero();
        }
    }

    private static StatusTone ToneFor(Color color)
        => color == DesignTokens.Danger ? StatusTone.Danger
        : color == DesignTokens.Accent ? StatusTone.Warning
        : color == DesignTokens.TextMuted ? StatusTone.Muted
        : color == DesignTokens.Success ? StatusTone.Success
        : StatusTone.Neutral;

    private static string Capitalise(string? message) =>
        string.IsNullOrEmpty(message) ? "-" : char.ToUpperInvariant(message[0]) + message[1..];

    // ----- global pause hotkey -----

    private void RegisterPauseHotkey()
    {
        if (_hotkeyRegistered)
        {
            Native.UnregisterHotKey(Handle, PauseHotkeyId);
            _hotkeyRegistered = false;
        }
        if (!IsHandleCreated) return;
        if (!MovementGuard.TryParseHotkey(_settings.PauseHotkey, out var modifiers, out var vk)) return;
        _hotkeyRegistered = Native.RegisterHotKey(Handle, PauseHotkeyId, modifiers, vk);
    }

    // ----- dynamic sizing -----

    private const int EdgeGrip = 8;

    private void FitToScreen()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        // Remembered geometry is honoured only under the classic3 layout; a
        // v2 shell size would otherwise open the new chrome at the wrong size.
        var classic = string.Equals(_settings.WindowLayout, "classic3", StringComparison.OrdinalIgnoreCase);
        var wantW = classic && _settings.WindowWidth > 0 ? _settings.WindowWidth : ClassicWantWidth;
        var wantH = classic && _settings.WindowHeight > 0 ? _settings.WindowHeight : ClassicWantHeight;
        var w = Math.Max(MinWindowWidth, Math.Min(wantW, area.Width));
        var h = Math.Max(MinWindowHeight, Math.Min(wantH, area.Height));
        MinimumSize = new Size(MinWindowWidth, MinWindowHeight);
        ClientSize = new Size(w, h);
        if (!classic)
        {
            // Size migration (v3 classic shell): stamp the layout tag at
            // construction and persist it, so a remembered v2 shell size is
            // never re-honoured on the next launch. Writes through the normal
            // AppSettings save path, serialized by SaveSettings.
            _settings.WindowLayout = "classic3";
            SaveSettings();
        }
    }

    private void SaveWindowSize()
    {
        if (WindowState != FormWindowState.Normal) return;
        var size = ClientSize;
        if (size.Width < MinWindowWidth || size.Height < 200) return;
        if (_settings.WindowWidth == size.Width && _settings.WindowHeight == size.Height) return;
        _settings.WindowWidth = size.Width;
        _settings.WindowHeight = size.Height;
        SaveSettings();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SaveWindowSize();
    }

    private void UpdateWindowBorder()
    {
        var color = _engine.IsRunning ? DesignTokens.Success : DesignTokens.Danger;
        if (_borderColor == color) return;
        _borderColor = color;
        Invalidate();
    }

    private Color _borderColor = DesignTokens.Danger;

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(_borderColor, 2F);
        e.Graphics.DrawRectangle(pen, 1, 1,
            Math.Max(1, ClientSize.Width - 3), Math.Max(1, ClientSize.Height - 3));
        base.OnPaint(e);
    }

    /// <summary>Esc closes the topmost popup (classic single-view shell).</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_abilitiesOverlay is { Visible: true }) { HideAbilities(); return true; }
            if (_advancedOverlay is { Visible: true }) { HideAdvanced(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        const int wmNcHitTest = 0x0084;
        const int htClient = 1, htBottom = 15, htLeft = 10, htRight = 11;
        const int htBottomLeft = 16, htBottomRight = 17;
        if (m.Msg == wmNcHitTest)
        {
            base.WndProc(ref m);
            if ((int)m.Result == htClient)
            {
                var cursor = PointToClient(Cursor.Position);
                var corner = EdgeGrip * 2;
                var left = cursor.X < EdgeGrip;
                var right = cursor.X >= ClientSize.Width - EdgeGrip;
                var bottom = cursor.Y >= ClientSize.Height - EdgeGrip;
                m.Result = (IntPtr)(bottom
                    ? (cursor.X < corner ? htBottomLeft
                        : cursor.X >= ClientSize.Width - corner ? htBottomRight : htBottom)
                    : (IntPtr)(left ? htLeft : right ? htRight : htClient));
                return;
            }
            return;
        }
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == PauseHotkeyId)
        {
            _engine.Paused = !_engine.Paused;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Stop();
        _saveDebounce.Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, PauseHotkeyId);
        _engine.Dispose();
        base.OnFormClosed(e);
    }

    // ----- snapshot + smoke hooks (v3 classic) -----

    /// <summary>Snapshot/test hook: show the Advanced popup (Configuration tab).</summary>
    internal void OpenAdvancedForSnapshot(int scrollY = -1)
    {
        ShowAdvanced();
        PerformLayout();
        if (scrollY < 0) return;
        var y = scrollY == int.MaxValue ? _config.ScrollArea.VerticalScroll.Maximum : scrollY;
        _config.ScrollArea.AutoScrollPosition = new Point(0, y);
        _config.ScrollArea.PerformLayout();
        _config.ScrollArea.Update();
    }

    /// <summary>Snapshot/test hook: open a named classic page/tab.</summary>
    internal void OpenPageForSnapshot(string page)
    {
        switch (page.Trim().ToLowerInvariant())
        {
            case "main":
                break;
            case "advanced-config" or "configuration" or "config":
                ShowAdvanced();
                _advancedTabs.SelectedIndex = 0;
                _config.PerformLayout();
                break;
            case "advanced-diag" or "diagnostics" or "diag":
                ShowAdvanced();
                _advancedTabs.SelectedIndex = 1;
                _diagnostics.PerformLayout();
                break;
            case "advanced-intel" or "intelligence" or "intel":
                _intelligencePage.EnsureBuilt();
                ShowAdvanced();
                _advancedTabs.SelectedIndex = 2;
                _intelligencePage.PerformLayout();
                break;
            case "abilities" or "abilities-class":
                ShowAbilities();
                _abilitiesTabs.SelectedIndex = 0;
                break;
            case "abilities-explorer":
                ShowAbilities();
                _abilitiesTabs.SelectedIndex = 1;
                _explorer.PerformLayout();
                break;
        }
        PerformLayout();
    }

    /// <summary>Prepare an offscreen snapshot at a narrow width.</summary>
    internal void PrepareOffscreenSnapshot(int width, int height)
    {
        MinimumSize = new Size(320, 400);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        ShowInTaskbar = false;
        var target = Math.Max(320, width);
        ClientSize = new Size(target, Math.Max(500, height));
    }

    internal void OpenClassSkillsForSnapshot(string className, string specName)
    {
        if (_classSkills is null) return;
        ShowAbilities();
        _abilitiesTabs.SelectedIndex = 0;
        _classSkills.Open(className, specName);
        _classSkills.SnapToShown();
        PerformLayout();
    }

    internal string ClassSkillsDebugState => _classSkills?.DebugState ?? "null";

    internal AbilityExplorer ExplorerForTest => _explorer;
    internal IntelligencePage IntelligenceForTest => _intelligencePage;
    internal ConfigurationPage ConfigurationForTest => _config;
    internal TabControl AdvancedTabsForTest => _advancedTabs;
    internal TabControl AbilitiesTabsForTest => _abilitiesTabs;
    internal bool AnyPopupVisibleForTest => AnyPopupVisible;
    internal Size MinimumSizeForTest => MinimumSize;
    internal Size DefaultClientSizeForTest => new(ClassicWantWidth, ClassicWantHeight);
    internal ToggleSwitch SoloToggleForTest => _solo2;
    internal ToggleSwitch MainToggleForTest => _main;
    internal bool StartEnabledForTest => _start.Enabled;
    internal bool StopEnabledForTest => _stop.Enabled;
    internal string StatusLineForTest => _statusLine.Text;
    internal void InvokeLaunchForTest() => LaunchGame();
    internal IReadOnlyList<string> BottomButtonLabelsForTest => new[]
    {
        _start.Text, _stop.Text, _launchGame.Text,
        _recalibrate.Text, _abilitiesEntry?.Text ?? "", _advancedEntry?.Text ?? "", _openFolder.Text,
    };

    /// <summary>Launcher seam: tests must never start the real Battle.net.</summary>
    internal Func<string?, (bool Ok, string Message)> Launcher { get; set; } =
        path => BattleNetLauncher.TryLaunch(path, out var message) ? (true, message) : (false, message);

    /// <summary>Test seam: drive the Esc handler exactly as WinForms would.</summary>
    internal bool HandleEscapeForTest()
    {
        var message = new Message();
        return ProcessCmdKey(ref message, Keys.Escape);
    }

    internal void OpenAdvancedForTest() => ShowAdvanced();
    internal void HideAdvancedForTest() => HideAdvanced();
    internal void OpenAbilitiesForTest() => ShowAbilities();
    internal void HideAbilitiesForTest() => HideAbilities();

    /// <summary>Test seam: runs one live status-refresh tick (A1 regression).</summary>
    internal void RefreshStatusForTest() => RefreshStatus();

    /// <summary>
    /// Test seam (A7): push <paramref name="count"/> distinct decoded snapshots
    /// through the timer body. Every field changes each tick but no control is
    /// added, removed, resized or re-parented, so the refresh must be value-only.
    /// </summary>
    internal void PumpChangingSnapshotsForTest(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _status = new EngineStatus(
                $"tick-{i}",
                BridgeVisible: true,
                State: BridgeState.Active,
                SlotSummary: $"10 20 30 40 50 60 70 80 {i}",
                LastKeySent: $"key-{i}",
                RawSample: $"AA{i % 16:X}BB",
                Decision: $"decision-{i}");
            RefreshStatus();
        }
    }

    /// <summary>
    /// Meaningful headless smoke test (v3 classic): lays out the main body and
    /// every popup tab and runs the structural invariants. Returns findings.
    /// </summary>
    internal List<string> RunSmokeTest()
    {
        var findings = new List<string>();
        MinimumSize = new Size(520, 560);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        ShowInTaskbar = false;
        ClientSize = new Size(660, 920);
        Show();
        Application.DoEvents();
        PerformLayout();
        Application.DoEvents();

        _intelligencePage.EnsureBuilt();
        findings.AddRange(UiShellValidation.Validate(_mainBody, "Main"));

        // Each tab must be selected so WinForms lays its content out (a
        // never-shown TabPage keeps its children at 0×0).
        ShowAdvanced();
        ValidateTabs(_advancedTabs, "Advanced", findings);
        HideAdvanced();

        ShowAbilities();
        ValidateTabs(_abilitiesTabs, "Abilities", findings);
        HideAbilities();

        Hide();
        return findings;
    }

    private static void ValidateTabs(TabControl tabs, string prefix, List<string> findings)
    {
        for (var i = 0; i < tabs.TabPages.Count; i++)
        {
            tabs.SelectedIndex = i;
            var page = tabs.TabPages[i];
            var content = page.Controls.Count > 0 ? page.Controls[0] : null;
            content?.PerformLayout();
            Application.DoEvents();
            if (content is not null)
                findings.AddRange(UiShellValidation.Validate(content, $"{prefix}/{page.Text}"));
        }
    }
}
