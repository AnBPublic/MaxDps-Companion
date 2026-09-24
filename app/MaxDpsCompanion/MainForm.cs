namespace MaxDpsCompanion;

internal sealed class MainForm : Form
{
    private const int PauseHotkeyId = 0xA71;
    // Width floor removed per user call: window may go narrower, hero
    // buttons wrap instead of clipping (fixed 5-col grid + 9.5pt keeps
    // text inside each share down to ~520px).
    private const int MinWindowWidth = 520;
    // Collapsed chrome budget (v1.3.8): 48 title + (732 card + 20 margins)
    // + 46 Advanced button + 52 buttons + 22 canvas padding ≈ 920.
    // FitToScreen crops to the working area on short screens.
    private const int CollapsedWantHeight = 920;


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


    private readonly ClassBadge _badge = new() { Text = "AUTO DETECT" };
    private readonly StatusDot _dot = new();
    private readonly Label _statusValue = new();
    private readonly Label _slotsValue = new();
    private readonly Label _lastKeyValue = new();
    private readonly Label _rawValue = new();
    private readonly LinkLamp _linkLamp = new();
    private readonly Label _linkLabel = new();
    private readonly StripView _stripView = new();

    // Hero toggles, Spell Frame naming (MaxDps Options.lua / SpellFrame.lua):
    // Group A = spell slots incl. the MAIN rotation toggle (5-icon model:
    // main/off/def/cons/trinket), Group B = companion behaviour (combat +
    // auto-target + auto-interact), Group C = interrupt kill-switch.
    private readonly ToggleSwitch _main = new() { Checked = true };
    private readonly ToggleSwitch _offensive = new() { Checked = true };
    private readonly ToggleSwitch _defensives = new() { Checked = true };
    private readonly ToggleSwitch _consumable = new();
    private readonly ToggleSwitch _trinket = new();
    private readonly ToggleSwitch _outOfCombat = new();
    private readonly ToggleSwitch _autoTarget = new();
    private readonly ToggleSwitch _autoInteract = new();
    private readonly ToggleSwitch _interrupt = new() { Checked = true };

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
    private readonly Label _calStatus = new();
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
    private readonly NumericUpDown _cellSize = Spin(2, 64);
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
    // v1.3.3: mirrored Main toggle (was dead "always on" display-only —
    // diverged from SlotEnabled[0] whenever the card toggled it; the Sep-2026
    // stuck-rotation report had main ON in one place and OFF in the other).
    private readonly CheckBox _mainSlot = new() { Text = "Main rotation", Checked = true, Enabled = true };

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _engine = new RotationEngine(settings);
        _engine.StatusChanged += s => _status = s;
        _engine.LocationChanged += location => _pendingLocation = location;

        Text = "MaxDPS Companion";
        // Resizable borderless chrome: the window opens fitted to the
        // working area (never taller than the screen) and stays user-
        // resizable via the invisible edge grip below — FormBorderStyle.None
        // windows otherwise cannot be resized at all.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(12, 39, 54);
        ForeColor = Color.FromArgb(239, 244, 247);
        Font = new Font(UiFont, 10F);
        FitToScreen();

        BuildLayout();
        LoadFromSettings();
        StyleInputs(this);
        WireAutoSave();
        BuildTray();

        // SlotEnabled index -> toggle wiring (Slot enum order, 5-icon model):
        // 0 Main, 1 Offensive, 2 Defensive, 3 Consumable, 4 Trinket,
        // 5 Interrupt (situational kill-switch).
        OnToggle(_main, 0);
        OnToggle(_offensive, 1);
        OnToggle(_defensives, 2);
        OnToggle(_consumable, 3);
        OnToggle(_trinket, 4);
        OnToggle(_interrupt, 5);

        _start.Click += (_, _) => StartEngine();
        _stop.Click += (_, _) => StopEngine();
        _recalibrate.Click += (_, _) => RecalibrateFull();
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

    private bool SlotFlag(int slot, bool fallback) =>
        (slot >= 0 && slot < _settings.SlotEnabled.Length) ? _settings.SlotEnabled[slot] : fallback;

    private void OnToggle(ToggleSwitch toggle, int slot)
    {
        toggle.CheckedChanged += (_, _) =>
        {
            if (slot >= 0 && slot < _settings.SlotEnabled.Length)
            {
                _settings.SlotEnabled[slot] = toggle.Checked;
                _settings.Save();
            }
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
            Text = $"MaxDPS Companion v{Native.AppVersion}",
            AutoSize = false,
            Location = new Point(48, 0),
            Size = new Size(220, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(UiFont, 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(228, 235, 239),
            BackColor = Color.Transparent,
        };
        // Stamp is generated at build time (ThisAssembly.Gen.cs) and must
        // never show a stale date: build.ps1 rewrites it on every publish,
        // so a mismatch here means the exe was not rebuilt.
        var version = new Label
        {
            Text = Native.BuildVersion,
            AutoSize = false,
            Location = new Point(268, 0),
            Size = new Size(300, 48),
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
        // Epoch-7 end-user pass: narrower blocks (sides 40 → 24) and the
        // debug strip/link row REMOVED (the 8-cell strip view + "link
        // alive" were developer readouts, not end-user UI — the hero status
        // line carries connection state).
        var canvas = new GradientCanvas { Dock = DockStyle.Fill, Padding = new Padding(24, 10, 24, 12) };
        // Fixed-height rows: card, Advanced entry button, action buttons.
        // The Advanced POPUP is a separate layer added last (on top), so it
        // never pushes the window size around. No FlowLayoutPanel: it
        // mis-measured Dock.Fill children as zero-height.
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, CardFixedHeight + 20F));  // card + margins
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));   // Advanced entry button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));   // buttons
        _bodyLayout = layout;
        layout.Controls.Add(BuildCard(), 0, 0);
        layout.Controls.Add(BuildAdvancedEntry(), 0, 1);
        layout.Controls.Add(BuildButtonFlow(), 0, 2);
        canvas.Controls.Add(layout);
        // Popup layer: fills the body and floats OVER the main frame while
        // Advanced is active. Show/hide swaps the two layers (the body is
        // hidden while the popup is up) so there is never any z-order
        // ambiguity — it reads as a screen flow with a Back.
        var overlay = BuildAdvancedOverlay();
        canvas.Controls.Add(overlay);
        overlay.BringToFront();
        return canvas;
    }

    private Control BuildAdvancedEntry()
    {
        _advancedEntry = new ChamferButton
        {
            Text = "Advanced…",
            Role = ButtonRole.Ghost,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(4, 6, 4, 2),
            Font = new Font(UiFont, 9.5F, FontStyle.Bold),
        };
        _advancedEntry.Click += (_, _) => ShowAdvanced();
        return _advancedEntry;
    }

    // v1.3.1 card rows: status + 3 headers + 9 toggles (5 spell incl.
    // main + trinket, 3 behaviour, 1 interrupt) = 13.
    private const int CardRowCount = 13;

    private Control BuildCard()
    {
        // Epoch-4: back to a TableLayoutPanel, but with FIXED row heights
        // (absolute, measured from content) instead of percent rows. Fixed
        // rows can't starve each other, and the table stretches children
        // to full width — which fixes both earlier regressions at once:
        // the percent-row squeeze (subtitles clipped) and the hand-stacked
        // card that never laid out (empty card, buttons crushed).
        var card = new RoundedCard
        {
            Dock = DockStyle.Top,
            Height = CardFixedHeight,
            Margin = new Padding(0, 2, 0, 6),
            Padding = new Padding(18, 12, 18, 12),
        };
        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = CardRowCount,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // v1.3.1 budget (5-icon model: 5 spell rows incl. main + trinket):
        // status 40 + headers 3x22 + rows 9x64 = 682 content + 24 card
        // padding = 706 card. Compact 64px rows still fit title(22) +
        // gap(4) + 2 subtitle lines(30) with margin — verified against the
        // epoch-2 type scale (11.5pt/9pt).
        // v1.3.8: rows 60 → 66 so the title + hint pair always has room
        // (the clipping complaint). 24px longer card, no other change.
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));  // 0 status
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 1 group header
        for (var i = 0; i < 5; i++) cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));  // 2-6 spells
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 7 group header
        for (var i = 0; i < 3; i++) cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));  // 8-10 behaviour
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 11 group header
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));  // 12 interrupt

        // Epoch-6: flat status row (dot Dock.Left + label Dock.Fill). The
        // previous triple-nested TableLayoutPanels silently collapsed to
        // zero height inside the card table and the status line vanished
        // (iter5-7 snapshots). No nested tables here: nothing to mismeasure.
        // The ClassBadge pill stays unparented (PRP chrome without a class
        // value); RefreshStatus still writes its Text harmlessly.
        _badge.Visible = false;
        var stateRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _dot.Dock = DockStyle.Left;
        _dot.Width = 24;
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
        // Slot summary + last key live in the Advanced strip readout; the
        // hero row keeps status only so stopped/idle states cannot clip.
        // (_slotsValue/_lastKeyValue are initialised in BuildStripBox.)
        _slotsValue.Visible = false;
        _lastKeyValue.Visible = false;
        stateRow.Controls.Add(_statusValue);
        stateRow.Controls.Add(_dot);
        // Fixed-height rows, full-width children: each row reserves its own
        // measured space (status 48, headers 26, toggles 76) so subtitles
        // can never be squeezed, and the table stretches every child to
        // the card width at any window size. The outer BODY flow scrolls
        // on short screens (FitToScreen clamps the window) — the card
        // itself never scrolls.
        stateRow.Dock = DockStyle.Fill;
        cardLayout.Controls.Add(stateRow, 0, 0);
        // Group A — the user's 5-icon model: main rotation + offensive /
        // defensive / consumable / trinket. Zebra alternates within each
        // group (resets per header) so the eye can scan rows without the
        // groups blending into one block.
        // v1.3.7 end-user pass: fewer words (one short hint per row, or
        // none), higher-contrast group headers (see GroupHeader), and
        // narrower rows (card padding 26 → 18). Behaviour is unchanged.
        cardLayout.Controls.Add(GroupHeaderFor("Spells"), 0, 1);
        cardLayout.Controls.Add(RowFor("Main rotation", "Core rotation", _main, alt: false), 0, 2);
        cardLayout.Controls.Add(RowFor("Offensive", "Offensive cooldowns", _offensive, alt: true), 0, 3);
        cardLayout.Controls.Add(RowFor("Defensive", "Defensive abilities", _defensives, alt: false), 0, 4);
        cardLayout.Controls.Add(RowFor("Consumable", "Potions", _consumable, alt: true), 0, 5);
        cardLayout.Controls.Add(RowFor("Trinket", "On-use trinkets", _trinket, alt: false), 0, 6);
        // Group B — companion behaviour (combat + targeting kill-switches).
        cardLayout.Controls.Add(GroupHeaderFor("Combat"), 0, 7);
        cardLayout.Controls.Add(RowFor("Out of combat", "Run outside combat", _outOfCombat, alt: false), 0, 8);
        cardLayout.Controls.Add(RowFor("Auto-target", "Target when needed", _autoTarget, alt: true), 0, 9);
        cardLayout.Controls.Add(RowFor("Auto-interact", "Interact when needed", _autoInteract, alt: false), 0, 10);
        // Group C — interrupt kill-switch (no Spell Frame equivalent).
        cardLayout.Controls.Add(GroupHeaderFor("Interrupt"), 0, 11);
        cardLayout.Controls.Add(RowFor("Interrupt", "Interrupt casts", _interrupt), 0, 12);
        card.Controls.Add(cardLayout);
        return card;
    }

    /// <summary>
    /// Fixed card height: status(42) + 3 headers(24) + 9 rows(66) +
    /// card padding(24) = 732. FitToScreen clamps on short screens.
    /// </summary>
    private const int CardFixedHeight = 42 + 3 * 24 + 9 * 66 + 24;

    private static GroupHeader GroupHeaderFor(string title) =>
        new() { Text = title, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, 0) };

    private static SettingRow RowFor(string title, string subtitle, ToggleSwitch toggle, bool alt = false) =>
        new(title, subtitle, toggle) { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), AlternateFill = alt };

    private Control BuildButtonFlow()
    {
        // Fixed 5-column grid: every hero button keeps an equal share, so
        // text never clips and Launch Game never wraps off the row.
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2),
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 5, 0, 5),
        };
        for (var i = 0; i < 5; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        foreach (var button in new[] { _start, _stop, _recalibrate, _openFolder, _launchGame })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 0, 4, 0);
            button.Font = new Font(UiFont, 9.5F, FontStyle.Bold);
        }
        buttons.Controls.Add(_start, 0, 0);
        buttons.Controls.Add(_stop, 1, 0);
        buttons.Controls.Add(_recalibrate, 2, 0);
        buttons.Controls.Add(_openFolder, 3, 0);
        buttons.Controls.Add(_launchGame, 4, 0);
        return buttons;
    }

    // ----- advanced popup, tray, hotkey, calibration -----

    private Panel? _advancedOverlay;
    private ChamferButton? _advancedEntry;

    /// <summary>
    /// Advanced is a POPUP DIALOG floating over the main frame (user
    /// request), not an in-place accordion: a dimmed scrim fills the body
    /// and a centred card holds the settings stack. "Back" (or Esc) returns
    /// to the main flow. The scrim intercepts input so the frame underneath
    /// is inert while the dialog is up, exactly like a modal.
    /// </summary>
    private Control BuildAdvancedOverlay()
    {
        // Opaque scrim (Control.BackColor ignores alpha unless the control
        // opts into transparency) — a solid deep tone reads as "the frame
        // behind is dimmed", and behaves identically in DrawToBitmap.
        var scrim = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 24, 32),
            Visible = false,
        };

        var popup = new RoundedCard
        {
            Size = new Size(560, 520),
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
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Header: title + Back (returns to the main flow).
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var title = new Label
        {
            Text = "Advanced",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = new Font(UiFont, 11.5F, FontStyle.Bold),
            ForeColor = ConsolePalette.Bone,
            BackColor = Color.Transparent,
        };
        var back = new ChamferButton
        {
            Text = "Back",
            Role = ButtonRole.Ghost,
            Dock = DockStyle.Right,
            Width = 96,
            Margin = new Padding(0, 4, 8, 4),
        };
        back.Click += (_, _) => HideAdvanced();
        header.Controls.Add(title);
        header.Controls.Add(back);

        // Scrollable settings stack: sections size to their content so no
        // caption can be cut off; the popup scrolls if the screen is short.
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 6, 0),
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = true,
            BackColor = Color.Transparent,
        };
        // Generous fixed row heights (v1.3.8): every Advanced section was
        // re-measured so captions, hints and inputs never clip; the popup
        // scrolls for the rest.
        foreach (var h in new[] { 190, 168, 124, 122, 96, 196, 168, 130 })
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        stack.Controls.Add(BuildSetupBox(), 0, 0);
        stack.Controls.Add(BuildBridgeBox(), 0, 1);
        stack.Controls.Add(BuildTimingBox(), 0, 2);
        stack.Controls.Add(BuildIconsBox(), 0, 3);
        stack.Controls.Add(BuildStripBox(), 0, 4);
        stack.Controls.Add(BuildTargetingBox(), 0, 5);
        stack.Controls.Add(BuildColorBox(), 0, 6);
        stack.Controls.Add(BuildLaunchBox(), 0, 7);
        scroll.Controls.Add(stack);

        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(scroll, 0, 1);
        popup.Controls.Add(shell);

        // Center the popup on resize; keep it inside the scrim.
        void Center()
        {
            var w = Math.Min(560, Math.Max(360, scrim.ClientSize.Width - 40));
            var h = Math.Min(520, Math.Max(280, scrim.ClientSize.Height - 40));
            popup.Size = new Size(w, h);
            popup.Location = new Point(
                Math.Max(0, (scrim.ClientSize.Width - w) / 2),
                Math.Max(0, (scrim.ClientSize.Height - h) / 2));
        }
        scrim.Resize += (_, _) => Center();
        scrim.Controls.Add(popup);
        scrim.Layout += (_, _) => Center();

        _advancedOverlay = scrim;
        return scrim;
    }

    private void ShowAdvanced()
    {
        if (_advancedOverlay is null) return;
        if (_bodyLayout is not null) _bodyLayout.Visible = false;
        _advancedOverlay.Visible = true;
        _advancedOverlay.BringToFront();
        _advancedOverlay.PerformLayout();
        _advancedOverlay.Update();
        _advancedOverlay.Focus();
    }

    private void HideAdvanced()
    {
        if (_advancedOverlay is null) return;
        _advancedOverlay.Visible = false;
        if (_bodyLayout is not null) _bodyLayout.Visible = true;
    }

    /// <summary>Snapshot/test hook: show the Advanced popup.</summary>
    internal void OpenAdvancedForSnapshot()
    {
        ShowAdvanced();
        PerformLayout();
    }

    private Control BuildSetupBox()
    {
        var section = new RuleSection { SectionTitle = "Setup", Dock = DockStyle.Top, Height = 190 };
        var setupGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        setupGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        setupGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        setupGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
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
        var section = new RuleSection { SectionTitle = "Pixel bridge", Dock = DockStyle.Top, Height = 168 };
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
        var section = new RuleSection { SectionTitle = "Timing", Dock = DockStyle.Top, Height = 124 };
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
        var section = new RuleSection { SectionTitle = "Slots", Dock = DockStyle.Top, Height = 122 };
        var tip = new ToolTip();
        tip.SetToolTip(_mainSlot, "Main rotation toggle - mirrors the card's Show main rotation row");
        _mainSlot.AutoSize = true;
        _mainSlot.ForeColor = ConsolePalette.Bone;
        _mainSlot.BackColor = Color.Transparent;
        _mainSlot.Margin = new Padding(2, 4, 18, 4);
        _mainSlot.CheckedChanged += (_, _) => { _main.Checked = _mainSlot.Checked; };
        _main.CheckedChanged += (_, _) => { if (_mainSlot.Checked != _main.Checked) _mainSlot.Checked = _main.Checked; };
        var note = new Label
        {
            Text = "Main / Offensive / Defensive / Consumable / Trinket / Interrupt mirror the card toggles.",
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
        var section = new RuleSection { SectionTitle = "Live suggestion", Dock = DockStyle.Top, Height = 96 };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
        grid.Controls.Add(_slotsValue, 0, 0);
        grid.Controls.Add(_lastKeyValue, 0, 1);
        var tip = new ToolTip();
        tip.SetToolTip(_slotsValue, "Decoded slots: magic, main, offensive, interrupt, defensive, consumable, trinket, status, version.");
        tip.SetToolTip(_lastKeyValue, "Last key the engine sent to the game window.");
        section.Controls.Add(grid);
        return section;
    }

    private Control BuildTargetingBox()
    {
        var section = new RuleSection { SectionTitle = "Targeting", Dock = DockStyle.Top, Height = 196 };
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
        _targetKey.Width = 110;
        _interactKey.Width = 110;
        var interactTip = new ToolTip();
        interactTip.SetToolTip(_interactKey, "Keyboard (F) or mouse (MB5, Alt+MB5). Mouse needs the game focused.");
        targeting.Controls.Add(autoTargetBox, 0, 0);
        targeting.SetColumnSpan(autoTargetBox, 2);
        targeting.Controls.Add(autoInteractBox, 0, 1);
        targeting.SetColumnSpan(autoInteractBox, 2);
        targeting.Controls.Add(Caption("Target key", "Pressed when the addon asks for a target; mode lives on the in-game T button. Mouse buttons work too (MB5, Alt+MB5)."), 0, 2);
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
        var section = new RuleSection { SectionTitle = "Color", Dock = DockStyle.Top, Height = 168 };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            BackColor = Color.Transparent,
        };

        _learnColors.Dock = DockStyle.Fill;
        _learnColors.Margin = new Padding(0, 0, 6, 0);
        _learnColors.Text = "Calibrate colors";
        _learnColors.Click -= LearnColorsRelay;
        _learnColors.Click += LearnColorsRelay;
        _resetColors.Dock = DockStyle.Fill;
        _resetColors.Margin = new Padding(6, 0, 0, 0);
        grid.Controls.Add(_learnColors, 0, 0);
        grid.SetColumnSpan(_learnColors, 2);
        grid.Controls.Add(_resetColors, 2, 0);
        grid.SetColumnSpan(_resetColors, 2);

        // Fast position-only sweep for moved strips; full pattern+learn
        // runs from the yellow Recalibrate hero button.
        var posOnly = new ChamferButton { Text = "Find strip", Role = ButtonRole.Ghost, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        posOnly.Click += (_, _) => RecalibratePositionOnly();
        grid.Controls.Add(Caption("Tolerance", "How far a cell may drift from the learned color and still match"), 0, 1);
        grid.Controls.Add(_tolerance, 1, 1);
        grid.Controls.Add(posOnly, 2, 1);
        grid.SetColumnSpan(posOnly, 2);

        _colorStatus.AutoSize = false;
        _colorStatus.Dock = DockStyle.Fill;
        _colorStatus.ForeColor = ConsolePalette.Tidewash;
        _colorStatus.BackColor = Color.Transparent;
        grid.Controls.Add(_colorStatus, 0, 2);
        grid.SetColumnSpan(_colorStatus, 4);

        section.Controls.Add(grid);
        return section;
    }

    private void LearnColorsRelay(object? sender, EventArgs e) => LearnColors();

    private Control BuildLaunchBox()
    {
        var section = new RuleSection { SectionTitle = "Battle.net", Dock = DockStyle.Top, Height = 130 };
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
    // True while the calibrate worker runs. RefreshStatus must not stomp the
    // hero line then — the worker owns it via SetColorStatus.
    private volatile bool _calibrating;

    private void LearnColors()
    {
        // Advanced button mirrors the yellow hero button: one flow, one place.
        RecalibrateFull();
    }

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
        // Mirror onto the hero line while calibrating so the flow is
        // visible without opening Advanced. RefreshStatus yields to the
        // worker via _calibrating, so this survives between ticks.
        if (_calibrating || text.StartsWith("Calibrat", StringComparison.Ordinal))
            SetStatus(text, ConsolePalette.Brass);
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
            // Silent path (Escape-close): a stuck edit box with the user's
            // half-typed whisper can never be sent by our cleanup.
            SetColorStatus("Calibrating - starting the pattern in game...");
            if (Cancelled()) return;
            if (!ChatCommander.SendChatCommandSilent(game, "mdb calibrate on"))
            {
                SetColorStatus("Calibration failed - could not focus the game window.");
                return;
            }
            // Chat-command round trip needs a beat before the strip repaints:
            // without this the sweep samples the pre-pattern frame and the
            // whole run fails at step 2 on fast machines.
            Thread.Sleep(600);
            if (Cancelled()) return;

            // 2. Verify the pattern is actually on screen before learning.
            // Offsets 0,0 mean "never located" (fresh settings), so always
            // sweep then: stale 0,0 from a previous session would otherwise
            // sample the screen corner instead of the strip.
            // ALWAYS sweep, even with stored offsets: the strip may have
            // moved (addon autocal, UI edits, resolution change) and the
            // learned profile goes stale exactly when the display chain
            // changes — which is when you recalibrate. A sweep that finds
            // the strip at the stored spot costs one pass; a trusted stale
            // offset costs the whole run ("no pixel block" on a visible
            // strip, because the profile learned at the wrong place can
            // never decode the right place).
            BlockLocation? block = BlockLocator.Locate(origin, size, _settings.Color);
            if (Cancelled()) return;
            if (block is not { } found || !PatternVisible(origin, found))
            {
                SetColorStatus("Calibration failed - no pattern on screen. Addon updated? (/reload)");
                BeginInvoke(() => MessageBox.Show(
                    "No calibrate pattern on screen. Check in order:\n\n"
                    + "  1. In game, type: /mdb calibrate on — you must see\n"
                    + "     'MDB: calibrate pattern ON'. If not: /reload first.\n"
                    + "  2. Display Mode must be Windowed or Borderless\n"
                    + "     (exclusive Fullscreen is invisible to capture).\n"
                    + "  3. The game must be visible, not covered.\n"
                    + "  4. Then Calibrate colors again.",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                // Single cleanup: leave the bridge exactly as found (on).
                ChatCommander.SendChatCommandSilent(game, "mdb calibrate off");
                return;
            }
            var known = found;

            // Commit synchronously on THIS thread: the old BeginInvoke could
            // still be queued when StartEngine ran seconds later, so Start
            // sampled stale 0,0 while the learned profile expected the new
            // spot ("No pixel block" right after a 5/5 learn). The sweep
            // gave us plain ints; _settings fields are safe to set here —
            // only the NumericUpDown controls need the UI thread.
            _settings.OffsetX = known.OffsetX;
            _settings.OffsetY = known.OffsetY;
            _settings.CellSize = known.CellSize;
            BeginInvoke(() =>
            {
                _offsetX.Value = known.OffsetX;
                _offsetY.Value = known.OffsetY;
                _cellSize.Value = known.CellSize;
            });

            // 3. Sample the cycling pattern. Learn polls Classify itself and
            // returns when anchors are known, so call it once and let ITS
            // status drive the hero line — the wait-then-learn two-phase
            // below used to sit silent on "starting" until first Classify.
            //
            // VERSION SKEW (Sep-2026 outage): the sampler always captures
            // the v2 width (9 cells), but a stale in-game addon renders the
            // v1 pattern (8 cells + status at 6). Learn probes BOTH widths
            // per tick (v2 first, v1 fallback) so a missed /reload degrades
            // to a warning, never to "no pattern on screen".
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
                    // Fast path: live v2 pattern classifies as-is.
                    if (ColorLearner.Classify(full, _settings.Color) >= 0)
                    {
                        sampledVersion = PixelProtocol.SupportedVersion;
                        return full;
                    }
                    // Fallback: stale v1 pattern (first 8 cells). Classify
                    // rejects by length, so pass the explicit v1 window.
                    var old = sampler.SampleV1(at, cell);
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
            if (result is not null && sampledVersion == PixelProtocol.SupportedVersionV1)
            {
                SetColorStatus("Calibrated against the OLD (v1) addon - /reload + reinstall the addon, then recalibrate.");
                BeginInvoke(() => MessageBox.Show(
                    "Calibration succeeded against the OLD v1 bridge addon.\n\n"
                    + "Your in-game addon is stale: run install-addon.ps1, then\n"
                    + "/reload in game, then Recalibrate again so the v2 strip\n"
                    + "(with the trinket slot) is what gets learned.",
                    "MaxDPS Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }

            // 4. Always turn the pattern back off: ONE command only. The old
            // triple (calibrate off + off + on) toggled the bridge twice and
            // left "bridge paused" in chat plus a Paused strip behind — the
            // next Start then held instead of sending.
            ChatCommander.SendChatCommandSilent(game, "mdb calibrate off", settleMs: 400);

            if (Cancelled())
            {
                SetStatus("Calibration cancelled - pattern turned off.", ConsolePalette.Bone);
                SetColorStatus("Calibration cancelled - pattern turned off.");
                return;
            }

            if (result is not { } learned)
            {
                SetStatus("Calibration failed - pattern not stable.", ConsolePalette.EmberLight);
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
            SetStatus(learned.Note + " - saved.", ConsolePalette.Bone);
            SetColorStatus(learned.Note + " - saved.");
        }
        finally
        {
            SetCalibrating(false);
        }
    }

    /// <summary>
    /// True when the sampled cells look like a calibrate pattern step.
    /// Probes v2 first, then the v1 window (stale addon renders 8 cells;
    /// the 9th captured cell is background and v2 Classify rejects it).
    /// </summary>
    private bool PatternVisible(Point origin, BlockLocation known)
    {
        try
        {
            using var sampler = new ScreenSampler();
            var at = new Point(origin.X + known.OffsetX, origin.Y + known.OffsetY);
            // Profile-aware: a stale profile may still recognise the magic
            // cell when the legacy band no longer can.
            if (ColorLearner.Classify(sampler.Sample(at, known.CellSize), _settings.Color) >= 0)
                return true;
            return ColorLearner.Classify(sampler.SampleV1(at, known.CellSize), _settings.Color) >= 0;
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
    // Slot mapping (SlotEnabled index -> UI, 5-icon model):
    //   0 Main        - card "Show main rotation" toggle (core functionality)
    //   1 Offensive   - card "Show offensive spells" toggle
    //   2 Defensive   - card "Show defensive spells" toggle
    //   3 Consumable  - card "Show consumable spells" toggle (default off)
    //   4 Trinket     - card "Show trinket spells" toggle (default off)
    //   5 Interrupt   - card Interrupt toggle (situational kill-switch)

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
        _mainSlot.Checked = SlotFlag(0, _main.Checked);
        _main.Checked = SlotFlag(0, _main.Checked);

        // Old settings.ini files stored the v2 order (1=Off 2=Int 3=Def
        // 4=Cons 5=Trin): missing entries keep their toggle defaults
        // (main/off/def/interrupt on, item slots off) instead of throwing.
        _main.Checked = SlotFlag(0, _main.Checked);
        _offensive.Checked = SlotFlag(1, _offensive.Checked);
        _defensives.Checked = SlotFlag(2, _defensives.Checked);
        _consumable.Checked = SlotFlag(3, _consumable.Checked);
        _trinket.Checked = SlotFlag(4, _trinket.Checked);
        _interrupt.Checked = SlotFlag(5, _interrupt.Checked);

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
        _settings.SlotEnabled[0] = _main.Checked;
        _settings.SlotEnabled[1] = _offensive.Checked;
        _settings.SlotEnabled[2] = _defensives.Checked;
        _settings.SlotEnabled[3] = _consumable.Checked;
        _settings.SlotEnabled[4] = _trinket.Checked;
        _settings.SlotEnabled[5] = _interrupt.Checked;
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
        // Re-sweep on EVERY Start, not just 0,0: the strip moves (UI edits,
        // resolution change, autocal) and a stale offset + fresh profile
        // reads as "no pixel block" on a visible strip. One sweep pass is
        // ~1 s; a blind start costs the whole session.
        if (game.TryGetClientOrigin(out var origin, out var size)
            && BlockLocator.Locate(origin, size, _settings.Color) is { } found)
            ApplyLocation(found);

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
    /// Yellow-button one-click flow, no Advanced needed: finds the strip,
    /// learns the display-chain color profile from the in-game calibrate
    /// pattern, saves both. Progress lands on the hero status line so the
    /// user never opens Advanced to watch it.
    /// </summary>
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

    /// <summary>
    /// Advanced-only fast path: sweep for the strip without the color
    /// pattern. Kept for cases where only the position changed.
    /// </summary>
    private void RecalibratePositionOnly()
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
            // A relocate mid-run changed the sampling point: say so once so
            // "block found, re-aligned" is visible instead of a flicker.
            SetStatus("Block found, re-aligned.", ConsolePalette.Brass);
            return;
        }

        // Calibrate worker owns the hero line while it runs (progress via
        // SetColorStatus). Never stomp it back to Stopped here.
        if (_calibrating) return;

        var status = _status;

        if (!_engine.IsRunning)
        {
            if (_statusValue.Text != "Stopped") SetStatus("Stopped", ConsolePalette.Bone);
            var idleDot = Color.FromArgb(83, 98, 106);
            if (_dot.Dot != idleDot) _dot.Dot = idleDot;
            if (_badge.Text != "AUTO DETECT") _badge.Text = "AUTO DETECT";
            // End-user pass: no "link alive"/strip/raw debug readouts in the
            // main UI. Those hidden fields keep their Advanced diagnostics.
            if (_slotsValue.Text != "-") _slotsValue.Text = "-";
            if (_lastKeyValue.Text != "-") _lastKeyValue.Text = "-";
            return;
        }

        // Hero line is the single end-user status: a coloured dot + plain
        // language ("Sending", "Waiting for target", "Out of combat"). The
        // dot mirrors the same state (green = connected, red = not) so it is
        // never colour-alone — the words carry the meaning too.
        var verb = Capitalise(_engine.Paused ? $"Paused - {status.Message}" : status.Message);
        var verbColor = status.BridgeVisible ? ConsolePalette.Bone : ConsolePalette.EmberLight;
        if (_statusValue.Text != verb || _statusValue.ForeColor != verbColor) SetStatus(verb, verbColor);
        var dot = status.BridgeVisible ? Color.FromArgb(71, 230, 148) : Color.FromArgb(202, 64, 68);
        if (_dot.Dot != dot) _dot.Dot = dot;
        // Advanced-only diagnostics (not shown in the main UI).
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

        // v1.3.3: unified parser (MovementGuard.TryParseHotkey shares the
        // canonical KeyNames table — mouse/OEM/alias coverage included).
        if (!MovementGuard.TryParseHotkey(_settings.PauseHotkey, out var modifiers, out var vk)) return;
        _hotkeyRegistered = Native.RegisterHotKey(Handle, PauseHotkeyId, modifiers, vk);
    }

    // ----- dynamic sizing: fit the working area, stay resizable -----

    private const int EdgeGrip = 8;

    /// <summary>
    /// Opens the window no larger than the working area (taskbar excluded),
    /// so a short screen never gets a window that stretches past it. Called
    /// once at startup; the user can resize freely afterwards.
    /// </summary>
    private void FitToScreen()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        // v1.3.7: narrower default (760 → 660) so the setting blocks are not
        // so wide; buttons still fit 5-across (660-48)/5 ≈ 122px each.
        var wantW = 660;
        var wantH = CollapsedWantHeight;
        var w = Math.Max(MinWindowWidth, Math.Min(wantW, area.Width));
        var h = Math.Max(560, Math.Min(wantH, area.Height));
        MinimumSize = new Size(MinWindowWidth, 560);
        ClientSize = new Size(w, h);
    }

    /// <summary>Esc closes the Advanced popup (return to the main flow).</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _advancedOverlay is { Visible: true })
        {
            HideAdvanced();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        // Invisible resize grip for the borderless window: HTBOTTOMRIGHT in
        // the corner square, HTBOTTOM/RIGHT/LEFT/BOTTOM on the edges.
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
}
