namespace MaxDpsCompanion;

internal sealed class MainForm : Form
{
    private const int PauseHotkeyId = 0xA71;
    private const int AdvancedExtraHeight = 700;

    private static string UiFontName()
    {
        foreach (var candidate in new[] { "Segoe UI Variable", "Segoe UI Variable Text", "Segoe UI" })
        {
            try
            {
                using var probe = new FontFamily(candidate);
                if (probe.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            catch (ArgumentException) { /* family missing: try next */ }
        }
        return "Segoe UI";
    }

    private static string UiFont => _uiFont ??= UiFontName();
    private static string? _uiFont;

    /// <summary>UI typeface for owner-drawn controls in <see cref="UiControls"/>.</summary>
    internal static string UiFontPublic => UiFont;

    private readonly AppSettings _settings;
    private readonly RotationEngine _engine;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };

    private EngineStatus _status;
    private bool _hotkeyRegistered;
    private int _collapsedHeight;

    private readonly ClassBadge _badge = new() { Text = "AUTO DETECT" };
    private readonly StatusDot _dot = new();
    private readonly Label _statusValue = new();
    private readonly Label _slotsValue = new();
    private readonly Label _lastKeyValue = new();
    private readonly Label _rawValue = new();
    private readonly LinkLamp _linkLamp = new();
    private readonly Label _linkLabel = new();
    private readonly StripView _stripView = new();

    private readonly ToggleSwitch _cooldowns = new() { Checked = true };
    private readonly ToggleSwitch _defensives = new() { Checked = true };
    private readonly ToggleSwitch _interrupt = new() { Checked = true };
    private readonly ToggleSwitch _consumable = new();
    private readonly ToggleSwitch _outOfCombat = new();
    private readonly ToggleSwitch _autoTarget = new();
    private readonly ToggleSwitch _autoInteract = new();

    private readonly ChamferButton _start = new()
    {
        Text = "Start",
        Role = ButtonRole.Primary,
        AccentColor = Color.FromArgb(35, 126, 246),
    };
    private readonly ChamferButton _stop = new()
    {
        Text = "Stop",
        Role = ButtonRole.Danger,
        AccentColor = Color.FromArgb(202, 64, 68),
        Enabled = false,
    };
    private readonly ChamferButton _recalibrate = new()
    {
        Text = "Recalibrate",
        Role = ButtonRole.Ghost,
        AccentColor = Color.FromArgb(158, 122, 46),
    };
    private readonly ChamferButton _openFolder = new()
    {
        Text = "Open Folder",
        Role = ButtonRole.Ghost,
        AccentColor = Color.FromArgb(75, 91, 104),
    };
    private readonly ChamferButton _launchGame = new()
    {
        Text = "Launch Game",
        Role = ButtonRole.Ghost,
        AccentColor = Color.FromArgb(75, 91, 104),
    };

    /// <summary>Set by the engine thread when it re-locates the block; applied by the UI timer.</summary>
    private BlockLocation? _pendingLocation;

    private NotifyIcon? _tray;
    private ContextMenuStrip? _trayMenu;
    private ToolStripMenuItem? _trayStartStop;

    private readonly System.Windows.Forms.Timer _saveDebounce = new() { Interval = 600 };

    private readonly TextBox _processName = new();
    private readonly TextBox _pauseHotkey = new();
    private readonly NumericUpDown _offsetX = Spin(-4000, 4000);
    private readonly NumericUpDown _offsetY = Spin(-4000, 4000);
    private readonly NumericUpDown _cellSize = Spin(1, 64);
    private readonly NumericUpDown _pollInterval = Spin(10, 1000);
    private readonly NumericUpDown _minKeyInterval = Spin(20, 5000);
    private readonly NumericUpDown _keyPress = Spin(0, 200);
    private readonly NumericUpDown _tolerance = Spin(16, 128);
    private readonly TextBox _targetKey = new();
    private readonly TextBox _interactKey = new();
    private readonly TextBox _bnetPath = new();
    private readonly ChamferButton _bnetBrowse = new()
    {
        Text = "Browse",
        Role = ButtonRole.Ghost,
    };
    private readonly Label _colorStatus = new();
    private readonly ChamferButton _learnColors = new()
    {
        Text = "Calibrate colors",
        Role = ButtonRole.Ghost,
    };
    private readonly ChamferButton _resetColors = new()
    {
        Text = "Reset",
        Role = ButtonRole.Ghost,
    };
    private readonly CheckBox _requireForeground = new() { Text = "Only send mouse while the game window is focused" };
    private readonly CheckBox _allowBackground = new() { Text = "Send keys while the game is in the background" };
    private readonly CheckBox _combatOnly = new() { Text = "Combat-only automatic targeting / interact" };
    private readonly CheckBox _mainSlot = new() { Text = "Main (always on)", Checked = true, Enabled = false };

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _engine = new RotationEngine(settings);
        _engine.StatusChanged += s => _status = s;
        _engine.LocationChanged += location => _pendingLocation = location;

        Text = "MaxDPS Companion";
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 1000);
        MinimumSize = new Size(760, 1000);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(12, 39, 54);
        ForeColor = Color.FromArgb(239, 244, 247);
        Font = new Font(UiFont, 10F);

        BuildLayout();
        LoadFromSettings();
        StyleInputs(this);
        WireAutoSave();
        BuildTray();
        _collapsedHeight = Height;

        OnToggle(_cooldowns, 1);
        OnToggle(_interrupt, 2);
        OnToggle(_defensives, 3);
        OnToggle(_consumable, 4);

        _start.Click += (_, _) => StartEngine();
        _stop.Click += (_, _) => StopEngine();
        _recalibrate.Click += (_, _) => Recalibrate();
        _openFolder.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", Program.AppDir);
        _launchGame.Click += (_, _) => LaunchGame();
        _learnColors.Click += (_, _) => LearnColors();
        _resetColors.Click += (_, _) => ResetColors();
        _bnetBrowse.Click += (_, _) => BrowseBNet();

        _cooldowns.CheckedChanged += (_, _) => SaveNow();
        _interrupt.CheckedChanged += (_, _) => SaveNow();
        _defensives.CheckedChanged += (_, _) => SaveNow();
        _consumable.CheckedChanged += (_, _) => SaveNow();
        _outOfCombat.CheckedChanged += (_, _) => SyncCombatFromCard();
        _autoTarget.CheckedChanged += (_, _) => SaveNow();
        _autoInteract.CheckedChanged += (_, _) => SaveNow();

        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            ApplyToSettings();
            _settings.Save();
            RegisterPauseHotkey();
        };

        _rawValue.Font = new Font(UiFont, 8F);

        _uiTimer.Tick += (_, _) => RefreshStatus();
        _uiTimer.Start();
    }

    private void SaveNow()
    {
        _saveDebounce.Stop();
        ApplyToSettings();
        _settings.Save();
        RegisterPauseHotkey();
    }

    /// <summary>
    /// Auto-save: every change persists to settings.ini. Spinners and
    /// checkboxes save immediately; text fields debounce so typing a
    /// process name saves once instead of per keystroke.
    /// Toggle rows mirror straight into SlotEnabled so the checkbox copy
    /// in Advanced can never disagree with them.
    /// </summary>
    private void WireAutoSave()
    {
        void SaveSoon()
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        foreach (NumericUpDown spin in new[] { _offsetX, _offsetY, _cellSize, _pollInterval, _minKeyInterval, _keyPress, _tolerance })
            spin.ValueChanged += (_, _) => SaveNow();
        foreach (CheckBox box in new[] { _requireForeground, _allowBackground })
            box.CheckedChanged += (_, _) => SaveNow();
        _combatOnly.CheckedChanged += (_, _) => SyncCombatFromAdvanced();
        foreach (TextBox box in new[] { _processName, _pauseHotkey, _targetKey, _interactKey, _bnetPath })
            box.TextChanged += (_, _) => SaveSoon();
    }

    // Card Out-of-combat toggle and Advanced Combat-only checkbox are two
    // views of one setting (CombatOnly = !OutOfCombat). Guard against
    // re-entrancy while syncing the pair.
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

    private void OnToggle(ToggleSwitch toggle, int slot)
    {
        toggle.CheckedChanged += (_, _) =>
        {
            _settings.SlotEnabled[slot] = toggle.Checked;
            _settings.Save();
        };
    }

    // ----- chrome: custom title bar + gradient body + card -----

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
            BackColor = Color.FromArgb(20, 28, 34),
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
            Text = "MaxDPS Companion v1.0.0",
            AutoSize = false,
            Location = new Point(48, 0),
            Size = new Size(220, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(UiFont, 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(228, 235, 239),
            BackColor = Color.Transparent,
        };
        var version = new Label
        {
            Text = Native.BuildVersion,
            AutoSize = false,
            Location = new Point(268, 0),
            Size = new Size(260, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(UiFont, 8F),
            ForeColor = Color.FromArgb(159, 181, 191),
            BackColor = Color.Transparent,
        };
        var closeButton = new TitleBarButton { Text = "x", Dock = DockStyle.Right, HoverColor = Color.FromArgb(196, 43, 48) };
        var minimizeButton = new TitleBarButton { Text = "-", Dock = DockStyle.Right, HoverColor = Color.FromArgb(54, 66, 73) };
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

    private TableLayoutPanel? _bodyLayout;

    private Control BuildBody()
    {
        var canvas = new GradientCanvas { Dock = DockStyle.Fill, Padding = new Padding(48, 24, 48, 28) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // card
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));   // strip + link
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // advanced toggle
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));   // buttons
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // advanced body (0 while hidden)
        _bodyLayout = layout;
        layout.Controls.Add(BuildCard(), 0, 0);
        layout.Controls.Add(BuildStripRow(), 0, 1);
        var advanced = BuildAdvanced(out var advancedToggle);
        layout.Controls.Add(advancedToggle, 0, 2);
        layout.Controls.Add(BuildButtonFlow(), 0, 3);
        layout.Controls.Add(advanced, 0, 4);
        canvas.Controls.Add(layout);
        return canvas;
    }

    private Control BuildCard()
    {
        var card = new RoundedCard { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 10), Padding = new Padding(26, 20, 26, 20) };
        var cardLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Color.Transparent };
        // Status row is taller (dot + status + slots); toggles share the rest.
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 24F));
        for (var i = 0; i < 7; i++) cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 10.86F));

        var stateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        // StatusDot paints its own glow; the extra ClassBadge pill duplicated
        // PRP chrome without carrying a class value, so the hero keeps the
        // status text + dot only.
        _badge.Visible = false;
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _dot.Dock = DockStyle.Fill;
        var statusTextPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = Color.Transparent };
        statusTextPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusTextPanel.Padding = new Padding(2, 4, 0, 0);
        _statusValue.Font = new Font(UiFont, 10.5F, FontStyle.Bold);
        _statusValue.ForeColor = Color.FromArgb(225, 234, 239);
        _statusValue.BackColor = Color.Transparent;
        _statusValue.AutoSize = false;
        _statusValue.Dock = DockStyle.Fill;
        _statusValue.AutoEllipsis = true;
        _statusValue.TextAlign = ContentAlignment.MiddleLeft;
        _statusValue.Text = "Stopped";
        _slotsValue.Font = new Font(UiFont, 8.25F);
        _slotsValue.ForeColor = Color.FromArgb(159, 181, 191);
        _slotsValue.BackColor = Color.Transparent;
        _slotsValue.AutoSize = false;
        _slotsValue.Dock = DockStyle.Fill;
        _slotsValue.AutoEllipsis = true;
        _slotsValue.TextAlign = ContentAlignment.MiddleLeft;
        _slotsValue.Text = "-";
        _lastKeyValue.Font = new Font(UiFont, 8.25F);
        _lastKeyValue.ForeColor = Color.FromArgb(159, 181, 191);
        _lastKeyValue.BackColor = Color.Transparent;
        _lastKeyValue.AutoSize = false;
        _lastKeyValue.Dock = DockStyle.Fill;
        _lastKeyValue.AutoEllipsis = true;
        _lastKeyValue.TextAlign = ContentAlignment.MiddleLeft;
        _lastKeyValue.Text = "-";
        statusTextPanel.Controls.Add(_statusValue, 0, 0);
        // Slot summary + last key live in the Advanced strip readout; the
        // hero row keeps status only so stopped/idle states cannot clip.
        _slotsValue.Visible = false;
        _lastKeyValue.Visible = false;
        leftPanel.Controls.Add(_dot, 0, 0);
        leftPanel.Controls.Add(statusTextPanel, 1, 0);
        stateRow.Controls.Add(leftPanel, 0, 0);
        _badge.Dock = DockStyle.Fill;
        _badge.Margin = new Padding(10, 15, 2, 15);
        stateRow.Controls.Add(_badge, 1, 0);
        cardLayout.Controls.Add(stateRow, 0, 0);
        cardLayout.Controls.Add(new SettingRow("Cooldowns", "Cooldown slot may fire when MaxDps suggests it.", _cooldowns) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 1);
        cardLayout.Controls.Add(new SettingRow("Interrupt", "Interrupt slot may fire when MaxDps suggests it.", _interrupt) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 2);
        cardLayout.Controls.Add(new SettingRow("Defensives", "Defensive slot may fire when MaxDps suggests it.", _defensives) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 3);
        cardLayout.Controls.Add(new SettingRow("Consumable", "Potion / trinket slot - off by default, fire manually.", _consumable) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 4);
        cardLayout.Controls.Add(new SettingRow("Out of combat", "Let suggestions run before combat begins (inverts Combat-only).", _outOfCombat) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 5);
        cardLayout.Controls.Add(new SettingRow("Auto-target", "Kill-switch: press Target key when the bridge asks. Default OFF.", _autoTarget) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 6);
        cardLayout.Controls.Add(new SettingRow("Auto-interact", "Kill-switch: press Interact key when the bridge asks. Default OFF.", _autoInteract) { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5) }, 0, 7);
        card.Controls.Add(cardLayout);
        return card;
    }

    private Control BuildStripRow()
    {
        var row = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(4, 6, 4, 6) };
        _stripView.Location = new Point(4, 6);
        _stripView.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        var lampWrap = new Panel { Dock = DockStyle.Right, Width = 150, BackColor = Color.Transparent };
        _linkLabel.Text = "link idle";
        _linkLabel.AutoSize = false;
        _linkLabel.Dock = DockStyle.Fill;
        _linkLabel.Padding = new Padding(0, 0, 20, 0);
        _linkLabel.TextAlign = ContentAlignment.MiddleRight;
        _linkLabel.Font = new Font(UiFont, 9F);
        _linkLabel.ForeColor = Color.FromArgb(147, 165, 174);
        _linkLabel.BackColor = Color.Transparent;
        _linkLamp.Anchor = AnchorStyles.Right;
        _linkLamp.Location = new Point(130, 15);
        lampWrap.Controls.Add(_linkLabel);
        lampWrap.Controls.Add(_linkLamp);
        row.Controls.Add(_stripView);
        row.Controls.Add(lampWrap);
        _rawValue.Visible = false;
        return row;
    }

    private Control BuildButtonFlow()
    {
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, BackColor = Color.Transparent, Padding = new Padding(0, 5, 0, 5) };
        for (var i = 0; i < 5; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        _start.Dock = DockStyle.Fill;
        _start.Margin = new Padding(6, 0, 6, 0);
        _stop.Dock = DockStyle.Fill;
        _stop.Margin = new Padding(6, 0, 6, 0);
        _recalibrate.Dock = DockStyle.Fill;
        _recalibrate.Margin = new Padding(6, 0, 6, 0);
        _openFolder.Dock = DockStyle.Fill;
        _openFolder.Margin = new Padding(6, 0, 6, 0);
        _launchGame.Dock = DockStyle.Fill;
        _launchGame.Margin = new Padding(6, 0, 6, 0);
        buttons.Controls.Add(_start, 0, 0);
        buttons.Controls.Add(_stop, 1, 0);
        buttons.Controls.Add(_recalibrate, 2, 0);
        buttons.Controls.Add(_openFolder, 3, 0);
        buttons.Controls.Add(_launchGame, 4, 0);
        return buttons;
    }

    // ----- advanced box, tray, hotkey, calibration -----

    private Control BuildAdvanced(out Control toggle)
    {
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            AutoScroll = true,
            Visible = false,
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = true,
            BackColor = Color.Transparent,
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        stack.Controls.Add(BuildSetupBox(), 0, 0);
        stack.Controls.Add(BuildBridgeBox(), 0, 1);
        stack.Controls.Add(BuildTimingBox(), 0, 2);
        stack.Controls.Add(BuildIconsBox(), 0, 3);
        stack.Controls.Add(BuildStripBox(), 0, 4);
        stack.Controls.Add(BuildTargetingBox(), 0, 5);
        stack.Controls.Add(BuildColorBox(), 0, 6);
        stack.Controls.Add(BuildLaunchBox(), 0, 7);
        body.Controls.Add(stack);

        var link = new LinkLabel
        {
            Text = "Advanced",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkColor = ConsolePalette.Tidewash,
            ActiveLinkColor = ConsolePalette.Bone,
            VisitedLinkColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 6, 2, 2),
        };
        link.LinkClicked += (_, _) =>
        {
            body.Visible = !body.Visible;
            link.Text = body.Visible ? "Advanced (hide)" : "Advanced";
            if (_bodyLayout is not null && _bodyLayout.RowStyles.Count > 4)
                _bodyLayout.RowStyles[4] = new RowStyle(SizeType.Absolute, body.Visible ? AdvancedExtraHeight : 0);
            Height = _collapsedHeight + (body.Visible ? AdvancedExtraHeight : 0);
        };
        toggle = link;
        return body;
    }

    private Control BuildSetupBox()
    {
        var section = new RuleSection { SectionTitle = "Setup", Dock = DockStyle.Top, Height = 150 };
        var setupGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        setupGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        setupGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _processName.Width = 220;
        _processName.Anchor = AnchorStyles.Left;
        _pauseHotkey.Width = 220;
        _pauseHotkey.Anchor = AnchorStyles.Left;
        var tip = new ToolTip();
        tip.SetToolTip(_processName, "Which game window to attach to (without .exe)");
        tip.SetToolTip(_pauseHotkey, "Global pause key - works while hidden");

        setupGrid.Controls.Add(Caption("Game process", "Which game window to attach to (without .exe)"), 0, 0);
        setupGrid.Controls.Add(_processName, 1, 0);
        setupGrid.Controls.Add(Caption("Pause hotkey", "Global pause key - works while hidden"), 0, 1);
        setupGrid.Controls.Add(_pauseHotkey, 1, 1);

        _requireForeground.AutoSize = true;
        _requireForeground.ForeColor = ConsolePalette.Bone;
        _requireForeground.BackColor = Color.Transparent;
        _requireForeground.Margin = new Padding(3, 4, 3, 2);
        tip.SetToolTip(_requireForeground, "Mouse slots only send while the game is focused");
        _allowBackground.AutoSize = true;
        _allowBackground.ForeColor = ConsolePalette.Bone;
        _allowBackground.BackColor = Color.Transparent;
        _allowBackground.Margin = new Padding(3, 2, 3, 4);
        tip.SetToolTip(_allowBackground, "Keyboard slots keep working while the game is in the background");
        setupGrid.Controls.Add(_allowBackground, 0, 2);
        setupGrid.SetColumnSpan(_allowBackground, 2);
        setupGrid.Controls.Add(_requireForeground, 0, 3);
        setupGrid.SetColumnSpan(_requireForeground, 2);

        section.Controls.Add(setupGrid);
        return section;
    }

    private Control BuildBridgeBox()
    {
        var section = new RuleSection { SectionTitle = "Pixel bridge", Dock = DockStyle.Top, Height = 150 };
        var bridgeGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        bridgeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        bridgeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        bridgeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        bridgeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        bridgeGrid.Controls.Add(Caption("Cell size (px)", "Pixel size of each strip cell, measured by Recalibrate"), 0, 0);
        bridgeGrid.Controls.Add(_cellSize, 1, 0);
        bridgeGrid.Controls.Add(Caption("Process", "Same window as Setup - shown here for reference"), 2, 0);
        var processRef = new Label
        {
            Text = _settings.ProcessName,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 3),
            ForeColor = ConsolePalette.Bone,
            BackColor = Color.Transparent,
        };
        bridgeGrid.Controls.Add(processRef, 3, 0);
        bridgeGrid.Controls.Add(Caption("Block offset X", "Strip position from the client top-left, found by Recalibrate"), 0, 1);
        bridgeGrid.Controls.Add(_offsetX, 1, 1);
        bridgeGrid.Controls.Add(Caption("Block offset Y"), 2, 1);
        bridgeGrid.Controls.Add(_offsetY, 3, 1);

        var hint = new Label
        {
            Text = "Recalibrate sweeps the client area and the engine re-aligns automatically if the strip moves.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
        };
        bridgeGrid.Controls.Add(hint, 0, 2);
        bridgeGrid.SetColumnSpan(hint, 4);

        section.Controls.Add(bridgeGrid);
        return section;
    }

    private Control BuildTimingBox()
    {
        var section = new RuleSection { SectionTitle = "Timing", Dock = DockStyle.Top, Height = 112 };
        var timing = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        timing.Controls.Add(Caption("Poll (ms)", "How often the strip is sampled"), 0, 0);
        timing.Controls.Add(_pollInterval, 1, 0);
        timing.Controls.Add(Caption("Key hold (ms)", "How long each key is held down"), 2, 0);
        timing.Controls.Add(_keyPress, 3, 0);
        timing.Controls.Add(Caption("Min key gap (ms)", "Minimum gap between two inputs"), 0, 1);
        timing.Controls.Add(_minKeyInterval, 1, 1);

        section.Controls.Add(timing);
        return section;
    }

    private Control BuildIconsBox()
    {
        var section = new RuleSection { SectionTitle = "Slots", Dock = DockStyle.Top, Height = 84 };
        var tip = new ToolTip();
        tip.SetToolTip(_mainSlot, "Main slot toggle - mirrors the card's always-on Main row");
        _mainSlot.AutoSize = true;
        _mainSlot.ForeColor = ConsolePalette.Bone;
        _mainSlot.BackColor = Color.Transparent;
        _mainSlot.Margin = new Padding(2, 4, 18, 4);
        _mainSlot.Checked = true;
        var note = new Label
        {
            Text = "Cooldowns / Interrupt / Defensives / Consumable mirror the card toggles; Main is always on.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
            Font = new Font(UiFont, 9F),
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
        };
        flow.Controls.Add(_mainSlot);
        flow.Controls.Add(note);
        section.Controls.Add(flow);
        return section;
    }

    private Control BuildStripBox()
    {
        var section = new RuleSection { SectionTitle = "Live suggestion", Dock = DockStyle.Top, Height = 100 };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(_slotsValue, 0, 0);
        grid.Controls.Add(_lastKeyValue, 0, 1);
        var tip = new ToolTip();
        tip.SetToolTip(_slotsValue, "Decoded slots: magic, main, cooldown, interrupt, defensive, consumable, status, version.");
        tip.SetToolTip(_lastKeyValue, "Last key the engine sent to the game window.");
        section.Controls.Add(grid);
        return section;
    }

    private Control BuildTargetingBox()
    {
        var section = new RuleSection { SectionTitle = "Targeting", Dock = DockStyle.Top, Height = 170 };
        var targeting = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        var autoTargetBox = new CheckBox { Text = "Auto-target when addon asks (press Target key) - kill-switch, default OFF" };
        var autoInteractBox = new CheckBox { Text = "Auto-interact when addon asks (press Interact key) - kill-switch, default OFF" };
        foreach (var box in new[] { autoTargetBox, autoInteractBox })
        {
            box.AutoSize = true;
            box.ForeColor = ConsolePalette.Bone;
            box.BackColor = Color.Transparent;
        }
        autoTargetBox.Checked = _autoTarget.Checked;
        autoInteractBox.Checked = _autoInteract.Checked;
        autoTargetBox.CheckedChanged += (_, _) => { _autoTarget.Checked = autoTargetBox.Checked; };
        autoInteractBox.CheckedChanged += (_, _) => { _autoInteract.Checked = autoInteractBox.Checked; };
        _autoTarget.CheckedChanged += (_, _) => { if (autoTargetBox.Checked != _autoTarget.Checked) autoTargetBox.Checked = _autoTarget.Checked; };
        _autoInteract.CheckedChanged += (_, _) => { if (autoInteractBox.Checked != _autoInteract.Checked) autoInteractBox.Checked = _autoInteract.Checked; };
        _combatOnly.AutoSize = true;
        _combatOnly.ForeColor = ConsolePalette.Bone;
        _combatOnly.BackColor = Color.Transparent;
        _targetKey.Width = 80;
        _interactKey.Width = 80;
        targeting.Controls.Add(autoTargetBox, 0, 0);
        targeting.SetColumnSpan(autoTargetBox, 2);
        targeting.Controls.Add(autoInteractBox, 0, 1);
        targeting.SetColumnSpan(autoInteractBox, 2);
        targeting.Controls.Add(Caption("Target key", "Pressed when the addon asks for a target; mode lives on the in-game T button"), 0, 2);
        var targetRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent };
        targetRow.Controls.Add(_targetKey);
        targetRow.Controls.Add(Caption("Interact key"));
        targetRow.Controls.Add(_interactKey);
        targeting.Controls.Add(targetRow, 1, 2);
        targeting.Controls.Add(_combatOnly, 0, 3);
        targeting.SetColumnSpan(_combatOnly, 2);

        section.Controls.Add(targeting);
        return section;
    }

    private Control BuildColorBox()
    {
        var section = new RuleSection { SectionTitle = "Color", Dock = DockStyle.Top, Height = 150 };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            BackColor = Color.Transparent,
        };

        _learnColors.Dock = DockStyle.Fill;
        _learnColors.Margin = new Padding(0, 0, 6, 0);
        _resetColors.Dock = DockStyle.Fill;
        _resetColors.Margin = new Padding(6, 0, 0, 0);
        grid.Controls.Add(_learnColors, 0, 0);
        grid.SetColumnSpan(_learnColors, 2);
        grid.Controls.Add(_resetColors, 2, 0);
        grid.SetColumnSpan(_resetColors, 2);

        grid.Controls.Add(Caption("Tolerance", "How far a cell may drift from the learned color and still match"), 0, 1);
        grid.Controls.Add(_tolerance, 1, 1);

        _colorStatus.AutoSize = false;
        _colorStatus.Dock = DockStyle.Fill;
        _colorStatus.ForeColor = ConsolePalette.Tidewash;
        _colorStatus.BackColor = Color.Transparent;
        grid.Controls.Add(_colorStatus, 0, 2);
        grid.SetColumnSpan(_colorStatus, 4);

        section.Controls.Add(grid);
        return section;
    }

    private Control BuildLaunchBox()
    {
        var section = new RuleSection { SectionTitle = "Battle.net", Dock = DockStyle.Top, Height = 120 };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.Controls.Add(Caption("BNet path", "Empty = auto-detect. Remembered account signs in."), 0, 0);
        _bnetPath.Dock = DockStyle.Fill;
        _bnetPath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        grid.Controls.Add(_bnetPath, 1, 0);
        _bnetBrowse.Dock = DockStyle.Fill;
        grid.Controls.Add(_bnetBrowse, 2, 0);
        var hint = new Label
        {
            Text = "Launch Game opens Battle.net for WoW. No credentials are stored - the launcher's remembered account is used.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
            Font = new Font(UiFont, 9F),
        };
        grid.Controls.Add(hint, 0, 1);
        grid.SetColumnSpan(hint, 3);
        section.Controls.Add(grid);
        return section;
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
                    box.BackColor = ConsolePalette.Field;
                    box.ForeColor = ConsolePalette.Bone;
                    box.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown spin:
                    spin.BackColor = ConsolePalette.Field;
                    spin.ForeColor = ConsolePalette.Bone;
                    break;
            }
            if (child.HasChildren) StyleInputs(child);
        }
    }

    private static NumericUpDown Spin(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = 80 };

    private static Label Caption(string text, string? tooltip = null)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 3),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ConsolePalette.Tidewash,
            BackColor = Color.Transparent,
        };
        label.AutoEllipsis = false;
        if (tooltip is not null)
        {
            var tip = new ToolTip();
            tip.SetToolTip(label, tooltip);
        }
        return label;
    }

    // ----- one-click color calibration (cancelable worker) -----

    private Thread? _calThread;
    private volatile bool _calCancel;

    private void LearnColors()
    {
        if (_engine.IsRunning)
        {
            MessageBox.Show(
                "Stop the engine first, then Calibrate colors.\n\n"
                + "The calibrate pattern reports Paused, so a running engine would just hold.",
                "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_calThread is { IsAlive: true })
        {
            _calCancel = true;
            _colorStatus.Text = "Cancelling...";
            return;
        }

        var confirm = MessageBox.Show(
            "This usually takes a few seconds.\n\n"
            + "The game will be focused, your keyboard will be locked while the pattern\n"
            + "is sampled (so stray typing can't corrupt it), then everything is restored.\n"
            + "You can cancel at any time - the pattern is always turned back off.\n\n"
            + "Continue?",
            "MaxDPS Companion", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (confirm != DialogResult.OK) return;

        ApplyToSettings();
        _calCancel = false;
        SetCalibrating(true);
        _calThread = new Thread(CalibrateWorker) { IsBackground = true, Name = "MaxDpsCompanion.Calibrate" };
        _calThread.Start();
    }

    private void SetCalibrating(bool running)
    {
        if (InvokeRequired) { BeginInvoke(new Action<bool>(SetCalibrating), running); return; }
        _learnColors.Text = running ? "Cancel calibration" : "Calibrate colors (automatic)";
        _learnColors.Enabled = true;
        _recalibrate.Enabled = !running;
        _start.Enabled = !running && !_engine.IsRunning;
    }

    private void SetColorStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(SetColorStatus), text); return; }
        _colorStatus.Text = text;
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

            // 1. Turn the pattern on through the game's own chat box.
            SetColorStatus("Calibrating - starting the pattern in game...");
            if (Cancelled()) return;
            if (!ChatCommander.SendChatCommand(game, "mdb calibrate on"))
            {
                SetColorStatus("Calibration failed - could not focus the game window.");
                return;
            }

            // 2. Verify the pattern is actually on screen before learning.
            BlockLocation? block = _settings.OffsetX != 0 || _settings.OffsetY != 0
                ? new BlockLocation(_settings.OffsetX, _settings.OffsetY, _settings.CellSize)
                : BlockLocator.Locate(origin, size, _settings.Color);
            if (Cancelled()) return;
            if (block is not { } known || !PatternVisible(origin, known))
            {
                SetColorStatus("Calibration failed - no pattern on screen. Addon updated? (/reload)");
                BeginInvoke(() => MessageBox.Show(
                    "The addon did not enter calibrate mode.\n\n"
                    + "The installed addon is probably the old version:\n"
                    + "  1. Run .\\install-addon.ps1\n"
                    + "  2. Type /reload in game\n"
                    + "  3. Try Calibrate colors again",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                ChatCommander.SendChatCommand(game, "mdb calibrate off");
                ChatCommander.SendChatCommand(game, "mdb off", settleMs: 400);
                ChatCommander.SendChatCommand(game, "mdb on", settleMs: 400);
                return;
            }

            // Apply location on the UI thread (NumericUpDown is not thread-safe).
            BeginInvoke(() =>
            {
                _settings.OffsetX = known.OffsetX;
                _settings.OffsetY = known.OffsetY;
                _settings.CellSize = known.CellSize;
            });

            // 3. Sample the cycling pattern.
            ColorLearner.LearnResult? result = null;
            using var sampler = new ScreenSampler();
            var learnStop = Environment.TickCount64 + 25_000;
            while (Environment.TickCount64 < learnStop)
            {
                if (Cancelled()) break;
                var cells = sampler.Sample(
                    new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY),
                    known.CellSize);
                if (ColorLearner.Classify(cells, _settings.Color) < 0)
                {
                    SetColorStatus("Calibrating - waiting for the pattern...");
                    Thread.Sleep(400);
                    continue;
                }
                SetColorStatus("Calibrating - sampling (keyboard locked, Cancel to stop)...");
                using (new InputLock().Install())
                {
                    result = ColorLearner.Learn(
                        (at, cell) => sampler.Sample(at, cell),
                        new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY),
                        known.CellSize,
                        _settings.Color,
                        (int)_tolerance.Value,
                        Cancelled);
                }
                if (Cancelled() || result is not null) break;
                Thread.Sleep(400);
            }

            // 4. Always turn the pattern back off, success, failure or cancel.
            ChatCommander.SendChatCommand(game, "mdb calibrate off", settleMs: 400);
            ChatCommander.SendChatCommand(game, "mdb off", settleMs: 400);
            ChatCommander.SendChatCommand(game, "mdb on", settleMs: 400);

            if (Cancelled())
            {
                SetColorStatus("Calibration cancelled - pattern turned off.");
                return;
            }

            if (result is not { } learned)
            {
                SetColorStatus("Calibration failed - pattern not stable.");
                BeginInvoke(() => MessageBox.Show(
                    "No stable pattern was sampled.\n\nCheck that:\n"
                    + "  - the client is windowed or borderless\n"
                    + "  - the strip is not covered by another window",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information));
                return;
            }

            // 5. Save the profile.
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
            _settings.Save();
            BeginInvoke(RefreshColorStatus);
            SetColorStatus(learned.Note + " - saved.");
        }
        finally
        {
            SetCalibrating(false);
        }
    }

    /// <summary>True when the sampled cells look like a calibrate pattern step.</summary>
    private bool PatternVisible(Point origin, BlockLocation known)
    {
        try
        {
            using var sampler = new ScreenSampler();
            var cells = sampler.Sample(
                new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY),
                known.CellSize);
            // Profile-aware: a stale profile may still recognise the magic
            // cell when the legacy band no longer can.
            return ColorLearner.Classify(cells, _settings.Color) >= 0;
        }
        catch
        {
            return false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Never trap the user: a runaway calibration thread must not block exit.
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
        _settings.Save();
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
        if (BattleNetLauncher.TryLaunch(string.IsNullOrWhiteSpace(_settings.BNetPath) ? null : _settings.BNetPath, out var message))
            SetStatus(message, ConsolePalette.Bone);
        else
            MessageBox.Show(message, "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ----- settings <-> controls -----
    //
    // Slot mapping (SlotEnabled index -> UI):
    //   0 Main        - always on (card has no Main row; Advanced checkbox is display-only)
    //   1 Cooldown    - card Cooldowns toggle
    //   2 Interrupt   - card Interrupt toggle
    //   3 Defensive   - card Defensives toggle
    //   4 Consumable  - card Consumable toggle (default off)

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
        _requireForeground.Checked = _settings.RequireForeground;
        _allowBackground.Checked = _settings.AllowBackgroundKeys;
        _mainSlot.Checked = true;
        _settings.SlotEnabled[0] = true;

        _cooldowns.Checked = _settings.SlotEnabled[1];
        _interrupt.Checked = _settings.SlotEnabled[2];
        _defensives.Checked = _settings.SlotEnabled[3];
        _consumable.Checked = _settings.SlotEnabled[4];

        // Engine CombatOnly is the inverse of the card's Out-of-combat display:
        // card ON = allow out of combat = CombatOnly false.
        SetCombatOnly(_settings.CombatOnly);

        _autoTarget.Checked = _settings.AutoTargetEnabled;
        _autoInteract.Checked = _settings.InteractEnabled;
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
        _settings.RequireForeground = _requireForeground.Checked;
        _settings.AllowBackgroundKeys = _allowBackground.Checked;
        _settings.SlotEnabled[0] = true;
        _settings.SlotEnabled[1] = _cooldowns.Checked;
        _settings.SlotEnabled[2] = _interrupt.Checked;
        _settings.SlotEnabled[3] = _defensives.Checked;
        _settings.SlotEnabled[4] = _consumable.Checked;
        _settings.CombatOnly = !_outOfCombat.Checked;
        _settings.AutoTargetEnabled = _autoTarget.Checked;
        _settings.InteractEnabled = _autoInteract.Checked;
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
            SetStatus($"no process named '{_settings.ProcessName}'", ConsolePalette.EmberLight);
            MessageBox.Show(
                $"No process named '{_settings.ProcessName}'.\n\nStart WoW first (Launch Game), then press Start.",
                "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // First run: never calibrated (offsets still 0,0) - sweep once so the
        // engine starts aligned instead of reporting "no pixel block".
        if (_settings.OffsetX == 0 && _settings.OffsetY == 0)
        {
            if (game.TryGetClientOrigin(out var origin, out var size)
                && BlockLocator.Locate(origin, size, _settings.Color) is { } found)
                ApplyLocation(found);
        }

        _engine.Paused = false;
        _engine.Start();
        _start.Enabled = false;
        _stop.Enabled = true;
        if (_trayStartStop is not null) _trayStartStop.Text = "Stop";
    }

    private void StopEngine()
    {
        if (!_engine.IsRunning) return;
        _engine.Stop();
        _start.Enabled = true;
        _stop.Enabled = false;
        if (_trayStartStop is not null) _trayStartStop.Text = "Start";
    }

    /// <summary>
    /// Sweeps the whole client area for the pixel strip and adopts wherever it is.
    /// This is the fastest way to tell a misaligned offset apart from a client that
    /// cannot be captured at all. Uses the learned color profile when present.
    /// </summary>
    private void Recalibrate()
    {
        ApplyToSettings();

        var location = RotationEngine.Locate(_settings, _settings.Color, out var message);
        if (location is { } found)
        {
            ApplyLocation(found);
            SetStatus(message, ConsolePalette.Brass);
            return;
        }

        SetStatus(message, ConsolePalette.EmberLight);
        MessageBox.Show(
            $"{message}.\n\n"
            + "Check that:\n"
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

    private void RefreshStatus()
    {
        if (_pendingLocation is { } located)
        {
            _pendingLocation = null;
            ApplyLocation(located);
        }

        var status = _status;

        if (!_engine.IsRunning)
        {
            if (_statusValue.Text != "Stopped") SetStatus("Stopped", ConsolePalette.Bone);
            var idleDot = Color.FromArgb(83, 98, 106);
            if (_dot.Dot != idleDot) _dot.Dot = idleDot;
            if (_badge.Text != "AUTO DETECT") _badge.Text = "AUTO DETECT";
            if (_linkLamp.Dot != ConsolePalette.Keyline) _linkLamp.Dot = ConsolePalette.Keyline;
            if (_linkLabel.Text != "link idle") _linkLabel.Text = "link idle";
            if (_stripView.Sample != "") _stripView.Sample = "";
            if (_rawValue.Text != "-") _rawValue.Text = "-";
            if (_slotsValue.Text != "-") _slotsValue.Text = "-";
            if (_lastKeyValue.Text != "-") _lastKeyValue.Text = "-";
            return;
        }

        var verb = Capitalise(_engine.Paused ? $"Paused - {status.Message}" : status.Message);
        var verbColor = status.BridgeVisible ? ConsolePalette.Bone : ConsolePalette.EmberLight;
        if (_statusValue.Text != verb || _statusValue.ForeColor != verbColor) SetStatus(verb, verbColor);
        var dot = status.BridgeVisible ? Color.FromArgb(71, 230, 148) : Color.FromArgb(202, 64, 68);
        if (_dot.Dot != dot) _dot.Dot = dot;
        var lamp = status.BridgeVisible ? ConsolePalette.Brass : ConsolePalette.Ember;
        if (_linkLamp.Dot != lamp) _linkLamp.Dot = lamp;
        var link = status.BridgeVisible ? "link alive" : "link lost";
        if (_linkLabel.Text != link) _linkLabel.Text = link;
        var raw = string.IsNullOrEmpty(status.RawSample) ? "" : status.RawSample;
        if (_stripView.Sample != raw) _stripView.Sample = raw;
        // _rawValue retained so the field still updates; hidden from layout.
        if (_rawValue.Text != raw) _rawValue.Text = raw;
        _rawValue.Visible = false;
        var slots = string.IsNullOrEmpty(status.SlotSummary) ? "-" : status.SlotSummary;
        if (_slotsValue.Text != slots) _slotsValue.Text = slots;
        var last = string.IsNullOrEmpty(status.LastKeySent) ? "-" : status.LastKeySent;
        if (_lastKeyValue.Text != last) _lastKeyValue.Text = last;
    }

    private void SetStatus(string message, Color color)
    {
        _statusValue.Text = message;
        _statusValue.ForeColor = color;
    }

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

        if (!TryParseHotkey(_settings.PauseHotkey, out var modifiers, out var key)) return;
        _hotkeyRegistered = Native.RegisterHotKey(Handle, PauseHotkeyId, modifiers, (uint)key);
    }

    private static bool TryParseHotkey(string text, out uint modifiers, out Keys key)
    {
        const uint modAlt = 0x0001, modControl = 0x0002, modShift = 0x0004, modWin = 0x0008;

        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= modControl;
                    break;
                case "alt":
                    modifiers |= modAlt;
                    break;
                case "shift":
                    modifiers |= modShift;
                    break;
                case "win":
                    modifiers |= modWin;
                    break;
                default:
                    if (!Enum.TryParse(part, true, out Keys parsed)) return false;
                    key = parsed;
                    break;
            }
        }

        return key != Keys.None;
    }

    protected override void WndProc(ref Message m)
    {
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
}
