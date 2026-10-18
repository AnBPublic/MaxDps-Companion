using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C WinUI port of the S7 console home (old Ui/ConsoleHome.cs :348-975),
/// mounted over the classic body exactly like MainForm.BuildBody :801-802.
/// It owns: the top bar (Pause / Folder / Console / Binds / Settings /
/// Rotation / Debug), the spec header with an inferred-role badge, the five
/// named preset bundles, the left status console with a rolling log and the
/// update log (spec + last change). The nav item click targets surface as
/// events, wired by <see cref="ShellWindow"/> exactly like
/// MainForm.BuildConsoleHome :1023-1035.
///
/// All live updates are value-only (text writes / visibility), so the 250 ms
/// status pump never performs layout here — the WinForms contract carried over.
/// </summary>
internal sealed class ConsoleHomeView : UserControl
{
    private const double TopBarHeight = 46;
    private const double SpecHeaderHeight = 82;
    private const double PresetStripHeight = 54;

    private readonly ConsoleNavItem _pause = new("Pause");
    private readonly ConsoleNavItem _ccHold = new("CC Hold");
    private readonly ConsoleNavItem _folder = new("Folder");
    private readonly ConsoleNavItem _console = new("Console");
    private readonly ConsoleNavItem _binds = new("Binds");
    private readonly ConsoleNavItem _settings = new("Settings");
    private readonly ConsoleNavItem _rotation = new("Rotation");
    private readonly ConsoleNavItem _debug = new("Debug");

    private readonly RoleBadgeView _roleBadge = new();
    private readonly TextBlock _specTitle;
    private readonly TextBlock _specSummary;
    private readonly RollingLogView _statusLog = new();
    private readonly RollingLogView _updateLog = new();
    private readonly TextBlock _stateValue;
    private readonly TextBlock _bridgeValue;
    private readonly TextBlock _nowValue;
    private readonly TextBlock _logSpecValue;
    private readonly TextBlock _lastChangeValue;
    private readonly TextBlock _presetNote;
    private readonly LinkLamp _lamp = new();
    private readonly List<PresetChipView> _presetChips = new();

    private string _className = "";
    private string _specName = "";
    private bool _paused;

    public ConsoleHomeView()
    {
        // WinUI's UserControl ControlTemplate forwards only Content — it never
        // paints Control.Background. The opaque ground must therefore live on the
        // root element (old ConsoleHome.StretchToParent :561-566 + grid.BackColor
        // = DesignTokens.Background :570-579, an opaque full-client view).
        // Setting it here would be a silent no-op and let the hero card ghost
        // through; see the root Grid below.
        AutomationProperties.SetName(this, "Console home");

        _specTitle = ValueLabel("AUTO DETECT", ControlTheme.Dip(DesignTokens.BodySize + 4f), ControlTheme.TextPrimary, bold: true);
        _specTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetName(_specTitle, "Detected class and spec");
        _specSummary = ValueLabel("Waiting for the bridge to report a class and spec.",
            ControlTheme.Dip(DesignTokens.BodySize - 1f), ControlTheme.TextSecondary);
        _specSummary.TextWrapping = TextWrapping.Wrap;
        _specSummary.MaxLines = 2;
        AutomationProperties.SetName(_specSummary, "Spec summary");

        _stateValue = ValueLabel("Stopped", ControlTheme.Dip(DesignTokens.BodySize), ControlTheme.TextPrimary);
        AutomationProperties.SetName(_stateValue, "Engine state");
        _bridgeValue = ValueLabel("-", ControlTheme.Dip(DesignTokens.BodySize - 1f), ControlTheme.TextSecondary);
        AutomationProperties.SetName(_bridgeValue, "Bridge state");
        _nowValue = ValueLabel("Now: idle - waiting for a MaxDps suggestion.",
            ControlTheme.Dip(DesignTokens.BodySize - 1f), ControlTheme.TextSecondary);
        AutomationProperties.SetName(_nowValue, "Current suggestion");
        _logSpecValue = ValueLabel("Spec: waiting for the bridge.", ControlTheme.Dip(DesignTokens.BodySize), ControlTheme.TextSecondary);
        AutomationProperties.SetName(_logSpecValue, "Update log spec");
        _lastChangeValue = ValueLabel("Last change: none yet.", ControlTheme.Dip(DesignTokens.BodySize), ControlTheme.TextSecondary);
        AutomationProperties.SetName(_lastChangeValue, "Update log last change");
        _presetNote = ValueLabel("Presets are named bundles of the toggles below; apply one, then adjust by hand.",
            ControlTheme.Dip(DesignTokens.BodySize - 1.5f), ControlTheme.TextMuted);
        _presetNote.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetName(_presetNote, "Preset note");

        // Opaque deep ground: the console home is a full-body panel mounted OVER
        // the classic hero body (old ConsoleHome was a Panel sized to the parent
        // client rect via StretchToParent, BackColor = DesignTokens.Background).
        // UserControl.Background does not paint, so the fill is on the root; the
        // Stretch content alignment makes the root fill the body like the legacy
        // Dock=Fill / StretchToParent contract, so nothing ghosts through.
        var root = new Grid { Background = ControlTheme.Brush(ControlTheme.Background) };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TopBarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SpecHeaderHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PresetStripHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var topBar = BuildTopBar();
        var specHeader = BuildSpecHeader();
        var presetStrip = BuildPresetStrip();
        var body = BuildBody();
        Grid.SetRow(topBar, 0);
        Grid.SetRow(specHeader, 1);
        Grid.SetRow(presetStrip, 2);
        Grid.SetRow(body, 3);
        root.Children.Add(topBar);
        root.Children.Add(specHeader);
        root.Children.Add(presetStrip);
        root.Children.Add(body);
        Content = root;

        SeedLog();
    }

    // ----- events (wired by ShellWindow; old ConsoleHome :414-421) -----

    public event Action? PauseRequested;
    public event Action? CcHoldRequested;
    public event Action? FolderRequested;
    public event Action? ConsoleToggleRequested;
    public event Action? BindsRequested;
    public event Action? SettingsRequested;
    public event Action? RotationRequested;
    public event Action? DebugRequested;
    public event Action<ConsolePreset>? PresetRequested;

    /// <summary>Anchor for the Rotation MenuFlyout (old ShowRotationMenu used the cursor).</summary>
    public FrameworkElement RotationAnchor => _rotation;

    // ----- public update API (all value-only; old :425-549) -----

    public RollingLogView StatusLog => _statusLog;
    public RollingLogView UpdateLog => _updateLog;
    public string SpecTitleForTest => _specTitle.Text;
    public string RoleForTest => _roleBadge.Role;
    public string LastChangeForTest => _lastChangeValue.Text;

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        _pause.Text = paused ? "Resume" : "Pause";
        _pause.Emphasised = paused;
    }

    /// <summary>v4.2.0 manual party-CC hold indicator (value-only).</summary>
    public void SetCcHold(bool active, int remainingMs)
    {
        var text = active
            ? $"CC Hold {Math.Max(1, (int)Math.Ceiling(remainingMs / 1000.0))}s"
            : "CC Hold";
        if (_ccHold.Text != text) _ccHold.Text = text;
        _ccHold.Emphasised = active;
    }

    public void SetSpec(string? className, string? specName)
    {
        var cls = className ?? "";
        var spec = specName ?? "";
        if (_className == cls && _specName == spec) return;
        _className = cls;
        _specName = spec;

        var (role, summary) = ConsoleRole.ForSpec(className, specName);
        _roleBadge.Role = role;
        _specTitle.Text = string.IsNullOrEmpty(spec) ? "AUTO DETECT" : $"{cls} \u00b7 {spec}";
        _specSummary.Text = summary;
        _logSpecValue.Text = string.IsNullOrEmpty(spec)
            ? "Waiting for the bridge to report a class and spec."
            : $"{cls} \u00b7 {spec}  ({role})";
        AppendLog($"[spec] {_specTitle.Text} - role {role}", ControlTheme.Info);
        NoteChange($"Spec detected: {_specTitle.Text} (role inferred: {role}).");
    }

    public void SetState(string state, WColor tone)
    {
        if (_stateValue.Text != state)
        {
            _stateValue.Text = state;
            _stateValue.Foreground = ControlTheme.Brush(tone);
        }
        _lamp.Dot = tone;
    }

    public void SetBridge(string text)
    {
        if (_bridgeValue.Text != text) _bridgeValue.Text = text;
    }

    public void SetNow(string action, string why)
    {
        var text = string.IsNullOrEmpty(action) || action == "-"
            ? "Now: idle - waiting for a MaxDps suggestion."
            : $"Now: {action} \u2014 {why}";
        if (_nowValue.Text != text) _nowValue.Text = text;
    }

    public void AppendLog(string text, WColor tone)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _statusLog.Append($"{stamp}  {text}", tone);
    }

    public void NoteChange(string text)
    {
        _lastChangeValue.Text = text;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _updateLog.Append($"{stamp}  {text}", ControlTheme.TextSecondary);
    }

    public void SetActivePreset(ConsolePreset? preset)
    {
        foreach (var chip in _presetChips) chip.Active = preset == chip.Preset;
        _presetNote.Text = preset is { } p
            ? ConsolePresets.Get(p).Summary
            : "Presets are named bundles of the toggles below; apply one, then adjust by hand.";
    }

    public void ApplyScale(UiScale scale)
    {
        _specTitle.FontSize = ControlTheme.Dip(scale.BaseFont + 4f);
        _specSummary.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 1f));
        _stateValue.FontSize = ControlTheme.Dip(scale.BaseFont);
        _bridgeValue.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 1f));
        _nowValue.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 1f));
        _presetNote.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 1.5f));
        _statusLog.ApplyScale(scale);
        _updateLog.ApplyScale(scale);
        foreach (var chip in _presetChips) chip.ApplyScale(scale);
    }

    // ----- construction (old Build/BuildTopBar/... :570-952) -----

    private FrameworkElement BuildTopBar()
    {
        var flow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _pause.Emphasised = true;
        _pause.Click += (_, _) => PauseRequested?.Invoke();
        _ccHold.Click += (_, _) => CcHoldRequested?.Invoke();
        _folder.Click += (_, _) => FolderRequested?.Invoke();
        _console.Active = true;
        _console.Click += (_, _) => ConsoleToggleRequested?.Invoke();
        _binds.Click += (_, _) => BindsRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();
        _rotation.Click += (_, _) => RotationRequested?.Invoke();
        _debug.Click += (_, _) => DebugRequested?.Invoke();
        flow.Children.Add(_pause);
        flow.Children.Add(_ccHold);
        flow.Children.Add(_folder);
        flow.Children.Add(_console);
        flow.Children.Add(_binds);
        flow.Children.Add(_settings);
        flow.Children.Add(_rotation);
        flow.Children.Add(_debug);

        return new Border
        {
            Background = ControlTheme.Brush(ControlTheme.ConsoleTitleBar),
            Padding = new Thickness(12, 8, 12, 8),
            Child = flow,
        };
    }

    private FrameworkElement BuildSpecHeader()
    {
        var titleColumn = new Grid();
        titleColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        titleColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_specTitle, 0);
        Grid.SetRow(_specSummary, 1);
        titleColumn.Children.Add(_specTitle);
        titleColumn.Children.Add(_specSummary);

        var grid = new Grid { Padding = new Thickness(18, 10, 18, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _roleBadge.VerticalAlignment = VerticalAlignment.Top;
        _roleBadge.Margin = new Thickness(0, 6, 12, 0);
        Grid.SetColumn(_roleBadge, 0);
        Grid.SetColumn(titleColumn, 1);
        grid.Children.Add(_roleBadge);
        grid.Children.Add(titleColumn);
        return grid;
    }

    private FrameworkElement BuildPresetStrip()
    {
        var label = new TextBlock
        {
            Text = "Presets",
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.Bone),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var flow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var bundle in ConsolePresets.All)
        {
            var chip = new PresetChipView(bundle);
            var captured = bundle.Preset;
            chip.Click += (_, _) => PresetRequested?.Invoke(captured);
            _presetChips.Add(chip);
            flow.Children.Add(chip);
        }
        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        top.Children.Add(label);
        top.Children.Add(flow);

        var grid = new Grid { Padding = new Thickness(14, 2, 14, 4) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_presetNote, 1);
        grid.Children.Add(top);
        grid.Children.Add(_presetNote);
        return new Border
        {
            Background = ControlTheme.Brush(ControlTheme.Iron),
            Child = grid,
        };
    }

    private FrameworkElement BuildBody()
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(14, 0, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42, GridUnitType.Star) });

        var statusCard = new RoundedCard { Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 6, 0, 0) };
        statusCard.Add(BuildStatusConsole());
        var updateCard = new RoundedCard { Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 6, 0, 0) };
        updateCard.Add(BuildUpdateLog());
        Grid.SetColumn(statusCard, 0);
        Grid.SetColumn(updateCard, 1);
        grid.Children.Add(statusCard);
        grid.Children.Add(updateCard);
        return grid;
    }

    private FrameworkElement BuildStatusConsole()
    {
        var table = new Grid();
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = CardHeader("Status console", ControlTheme.Bone);
        Grid.SetRow(header, 0);
        table.Children.Add(header);

        _lamp.VerticalAlignment = VerticalAlignment.Center;
        _lamp.Margin = new Thickness(2, 0, 8, 0);
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stateRow.Children.Add(_lamp);
        stateRow.Children.Add(_stateValue);
        Grid.SetRow(stateRow, 1);
        table.Children.Add(stateRow);

        Grid.SetRow(_bridgeValue, 2);
        table.Children.Add(_bridgeValue);

        _nowValue.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(_nowValue, 3);
        table.Children.Add(_nowValue);

        var logHeader = CardHeader("Rolling log", ControlTheme.TextMuted);
        Grid.SetRow(logHeader, 4);
        table.Children.Add(logHeader);

        Grid.SetRow(_statusLog, 5);
        table.Children.Add(_statusLog);
        return table;
    }

    private FrameworkElement BuildUpdateLog()
    {
        var table = new Grid();
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = CardHeader("Update log", ControlTheme.Bone);
        Grid.SetRow(header, 0);
        table.Children.Add(header);

        _logSpecValue.TextWrapping = TextWrapping.Wrap;
        _lastChangeValue.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(_logSpecValue, 1);
        Grid.SetRow(_lastChangeValue, 2);
        table.Children.Add(_logSpecValue);
        table.Children.Add(_lastChangeValue);

        var logHeader = CardHeader("History", ControlTheme.TextMuted);
        Grid.SetRow(logHeader, 3);
        table.Children.Add(logHeader);

        Grid.SetRow(_updateLog, 4);
        table.Children.Add(_updateLog);
        return table;
    }

    private static TextBlock CardHeader(string text, WColor color) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(DesignTokens.LabelSize),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = ControlTheme.Brush(color),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock ValueLabel(string text, double size, WColor color, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = ControlTheme.Brush(color),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private void SeedLog()
    {
        AppendLog("Console ready. Suggest-only: the companion relays the addon's strip.", ControlTheme.TextMuted);
        NoteChange("Console home loaded. Presets are named bundles of the existing toggles.");
    }
}

/// <summary>Owner-drawn top-bar item port (old ConsoleNavItem :23-84).</summary>
internal sealed class ConsoleNavItem : UiClickable
{
    private readonly Border _background = new();
    private readonly TextBlock _text;
    private readonly ShapeRect _tick;
    private bool _active;
    private bool _emphasised;

    public ConsoleNavItem(string text)
    {
        _text = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _text.Text = text;
        _tick = new ShapeRect
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 8, 0),
            Fill = ControlTheme.Brush(ControlTheme.Accent),
            Visibility = Visibility.Collapsed,
        };
        Height = 30;
        Margin = new Thickness(2, 0, 2, 0);
        Padding = new Thickness(13, 0, 13, 0);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        AutomationProperties.SetName(this, text);
        AutomationProperties.SetHelpText(this, text);

        var grid = new Grid();
        grid.Children.Add(_background);
        grid.Children.Add(_text);
        grid.Children.Add(_tick);
        Content = grid;
        Refresh();
    }

    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; Refresh(); }
    }

    /// <summary>Brass emphasis for the primary item (Pause).</summary>
    public bool Emphasised
    {
        get => _emphasised;
        set { if (_emphasised == value) return; _emphasised = value; Refresh(); }
    }

    public string Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value;
            AutomationProperties.SetName(this, value);
            AutomationProperties.SetHelpText(this, value);
        }
    }

    protected override void OnHoverChanged() => Refresh();

    private void Refresh()
    {
        _background.Background = _active
            ? ControlTheme.Brush(ControlTheme.Tint(ControlTheme.Accent, ControlTheme.ConsoleTitleBar))
            : Hovered
                ? ControlTheme.Brush(ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.ConsoleTitleBar, 0.06f))
                : null;
        _text.Foreground = ControlTheme.Brush(_active ? ControlTheme.Bone
            : _emphasised ? ControlTheme.Accent
            : Hovered ? ControlTheme.TextPrimary
            : ControlTheme.TextSecondary);
        _tick.Visibility = _active ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>One named preset bundle chip (old PresetChip :86-142).</summary>
internal sealed class PresetChipView : UiClickable
{
    private readonly Border _frame = new();
    private readonly TextBlock _text;
    private bool _active;

    public PresetChipView(ConsolePresetBundle bundle)
    {
        Preset = bundle.Preset;
        _text = new TextBlock
        {
            Text = bundle.Name,
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Height = 28;
        Margin = new Thickness(3, 0, 3, 0);
        Padding = new Thickness(13, 0, 13, 0);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        AutomationProperties.SetName(this, "Preset " + bundle.Name);
        AutomationProperties.SetHelpText(this, bundle.Summary);
        OwnedToolTip.SetToolTip(this, bundle.Summary);

        _frame.CornerRadius = new CornerRadius(7);
        _frame.BorderThickness = new Thickness(1);
        _frame.Child = _text;
        Content = _frame;
        Refresh();
    }

    public ConsolePreset Preset { get; }

    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; Refresh(); }
    }

    public void ApplyScale(UiScale scale) => _text.FontSize = ControlTheme.Dip(scale.BaseFont);

    protected override void OnHoverChanged() => Refresh();

    private void Refresh()
    {
        _frame.Background = ControlTheme.Brush(_active
            ? ControlTheme.Accent
            : Hovered ? ControlTheme.SurfaceElevated : ControlTheme.ConsolePanel);
        _frame.BorderBrush = ControlTheme.Brush(_active ? ControlTheme.Accent : ControlTheme.Keyline);
        _text.Foreground = ControlTheme.Brush(_active ? ControlTheme.Background : ControlTheme.TextPrimary);
    }
}

/// <summary>Inferred-role capsule (old RoleBadge :190-240).</summary>
internal sealed class RoleBadgeView : ContentControl
{
    private readonly Border _frame = new();
    private readonly TextBlock _text;
    private string _role = "AUTO";

    public RoleBadgeView()
    {
        _text = new TextBlock
        {
            FontSize = ControlTheme.Dip(9f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _frame.CornerRadius = new CornerRadius(11);
        _frame.BorderThickness = new Thickness(1);
        _frame.Padding = new Thickness(10, 3, 10, 3);
        _frame.MinHeight = 30;
        _frame.MinWidth = 88;
        _frame.Child = _text;
        Content = _frame;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(this, "Inferred role");
        Refresh();
    }

    public string Role
    {
        get => _role;
        set { if (_role == value) return; _role = value; Refresh(); }
    }

    private void Refresh()
    {
        var tone = _role switch
        {
            "TANK" => ControlTheme.Accent,
            "HEALER" => ControlTheme.Success,
            "DPS" => ControlTheme.EmberLight,
            _ => ControlTheme.TextMuted,
        };
        _text.Text = _role;
        _frame.Background = ControlTheme.Brush(ControlTheme.Tint(tone, ControlTheme.Background));
        _frame.BorderBrush = ControlTheme.Brush(tone);
        AutomationProperties.SetName(this, $"Inferred role: {_role}");
    }
}
