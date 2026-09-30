using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

// ---------------------------------------------------------------------------
// S7 Console home. One self-contained, opaque view mounted over the classic
// body by MainForm. It owns: a top bar (Pause / Folder / Console / Binds /
// Settings / Rotation / Debug), a spec header with an inferred role badge and
// summary, the five named preset bundles, a left status console with a rolling
// log, and an update log (current spec + last change).
//
// Honesty contract: the companion only relays what the addon encodes and
// presses keys through PostMessage. Nothing here claims to hide from anything;
// no copy promises invisibility to any system.
//
// Test-safety: none of the new control types are ToggleSwitch, ChamferButton
// or SettingRow, so the classic-UI tests that enumerate those types inside the
// classic body do not see them. Live updates are value-only (no Add/Remove or
// Bounds writes), so the 250 ms refresh still performs no layout.
// ---------------------------------------------------------------------------

/// <summary>Owner-drawn top-bar item. Deliberately not a <see cref="ChamferButton"/>.</summary>
internal sealed class ConsoleNavItem : UiClickable
{
    private bool _active;

    public ConsoleNavItem(string text)
    {
        Text = text;
        Font = DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold);
        Height = 30;
        Width = RequiredTextWidth;
        Margin = new Padding(2, 0, 2, 0);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = text;
        AccessibleDescription = text;
    }

    public int RequiredTextWidth =>
        TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 26;

    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; Invalidate(); }
    }

    /// <summary>Brass emphasis for the primary item (Pause).</summary>
    public bool Emphasised { get; set; }

    public void ResizeToText() => Width = RequiredTextWidth;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        if (_active)
        {
            using var fill = new SolidBrush(DesignTokens.Tint(ConsolePalette.Brass, ConsolePalette.TitleBar));
            e.Graphics.FillRectangle(fill, bounds);
        }
        else if (Hovered)
        {
            using var fill = new SolidBrush(DesignTokens.Blend(Color.White, ConsolePalette.TitleBar, 0.06f));
            e.Graphics.FillRectangle(fill, bounds);
        }
        var colour = _active ? ConsolePalette.Bone
            : Emphasised ? ConsolePalette.Brass
            : Hovered ? DesignTokens.TextPrimary
            : DesignTokens.TextSecondary;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (_active)
        {
            using var tick = new SolidBrush(ConsolePalette.Brass);
            e.Graphics.FillRectangle(tick, 8, Height - 3, Math.Max(8, Width - 16), 2);
        }
    }
}

/// <summary>One named preset bundle chip in the preset strip.</summary>
internal sealed class PresetChip : UiClickable
{
    private bool _active;

    public PresetChip(ConsolePresetBundle bundle)
    {
        Preset = bundle.Preset;
        Text = bundle.Name;
        Font = DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold);
        Height = 28;
        Width = RequiredTextWidth + 26;
        Margin = new Padding(3, 0, 3, 0);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Preset " + bundle.Name;
        AccessibleDescription = bundle.Summary;
    }

    public ConsolePreset Preset { get; }

    public int RequiredTextWidth =>
        TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;

    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; Invalidate(); }
    }

    public void ApplyScale(UiScale scale)
    {
        Font = DesignTokens.Type(scale.BaseFont, FontStyle.Bold);
        Width = RequiredTextWidth + 26;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var path = ConsolePalette.Chamfer(bounds, 7);
        var fillColour = _active
            ? ConsolePalette.Brass
            : Hovered ? DesignTokens.SurfaceElevated : ConsolePalette.Panel;
        using var fill = new SolidBrush(fillColour);
        e.Graphics.FillPath(fill, path);
        using var edge = new Pen(_active ? ConsolePalette.Brass : ConsolePalette.Keyline, 1F);
        e.Graphics.DrawPath(edge, path);
        var textColour = _active ? DesignTokens.Background : DesignTokens.TextPrimary;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, textColour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Inferred-role capsule for the spec header.</summary>
internal sealed class RoleBadge : Control
{
    private string _role = "AUTO";

    public string Role
    {
        get => _role;
        set { if (_role == value) return; _role = value; Invalidate(); }
    }

    public RoleBadge()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        Font = DesignTokens.Type(9F, FontStyle.Bold);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Inferred role";
    }

    private Color Tone() => _role switch
    {
        "TANK" => ConsolePalette.Brass,
        "HEALER" => DesignTokens.Success,
        "DPS" => ConsolePalette.EmberLight,
        _ => DesignTokens.TextMuted,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        var tone = Tone();
        using var path = new GraphicsPath();
        var radius = Math.Min(11, bounds.Height / 2);
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(DesignTokens.Tint(tone, DesignTokens.Background));
        e.Graphics.FillPath(fill, path);
        using var edge = new Pen(tone, 1F);
        e.Graphics.DrawPath(edge, path);
        TextRenderer.DrawText(e.Graphics, _role, Font, bounds, DesignTokens.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// Owner-drawn rolling log. Newest line at the bottom. Append only mutates a
/// list and invalidates — it never adds a child or changes bounds, so it is
/// safe on the status-timer path.
/// </summary>
internal sealed class RollingLog : Control
{
    private readonly List<(string Text, Color Tone)> _lines = new();

    public int Capacity { get; set; } = 200;

    public RollingLog()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        Font = DesignTokens.Type(DesignTokens.MetaSize);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Log";
    }

    public IReadOnlyList<(string Text, Color Tone)> Lines => _lines;

    public void Append(string text, Color tone)
    {
        _lines.Add((text, tone));
        while (_lines.Count > Capacity) _lines.RemoveAt(0);
        Invalidate();
    }

    public void Clear()
    {
        if (_lines.Count == 0) return;
        _lines.Clear();
        Invalidate();
    }

    public void ApplyScale(UiScale scale) => Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 2f));

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var lineHeight = Math.Max(12, Font.Height + 3);
        var visible = Math.Max(1, Height / lineHeight);
        var first = Math.Max(0, _lines.Count - visible);
        var y = Height - (Math.Min(visible, _lines.Count) * lineHeight);
        for (var i = first; i < _lines.Count; i++)
        {
            var (text, tone) = _lines[i];
            TextRenderer.DrawText(e.Graphics, text, Font,
                new Rectangle(0, y, Width, lineHeight), tone,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += lineHeight;
        }
    }
}

/// <summary>
/// The S7 Console home. Opaque, full-client view over the classic body.
/// </summary>
internal sealed class ConsoleHome : Panel
{
    private const int TopBarHeight = 46;
    private const int SpecHeaderHeight = 82;
    private const int PresetStripHeight = 54;

    private readonly ConsoleNavItem _pause = new("Pause");
    private readonly ConsoleNavItem _folder = new("Folder");
    private readonly ConsoleNavItem _console = new("Console");
    private readonly ConsoleNavItem _binds = new("Binds");
    private readonly ConsoleNavItem _settings = new("Settings");
    private readonly ConsoleNavItem _rotation = new("Rotation");
    private readonly ConsoleNavItem _debug = new("Debug");

    private readonly RoleBadge _roleBadge = new();
    private readonly Label _specTitle = new();
    private readonly Label _specSummary = new();
    private readonly RollingLog _statusLog = new();
    private readonly RollingLog _updateLog = new();
    private readonly Label _stateValue = new();
    private readonly Label _bridgeValue = new();
    private readonly Label _nowValue = new();
    private readonly Label _logSpecValue = new();
    private readonly Label _lastChangeValue = new();
    private readonly Label _presetNote = new();
    private readonly LinkLamp _lamp = new();
    private readonly List<PresetChip> _presetChips = new();

    private string _className = "";
    private string _specName = "";
    private bool _paused;

    public ConsoleHome()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = DesignTokens.Background;

        Build();
        SeedLog();
    }

    // ----- events (wired by MainForm) -----

    public event Action? PauseRequested;
    public event Action? FolderRequested;
    public event Action? ConsoleToggleRequested;
    public event Action? BindsRequested;
    public event Action? SettingsRequested;
    public event Action? RotationRequested;
    public event Action? DebugRequested;
    public event Action<ConsolePreset>? PresetRequested;

    // ----- public update API (all value-only) -----

    public IReadOnlyList<PresetChip> PresetChips => _presetChips;
    public RollingLog StatusLog => _statusLog;
    public RollingLog UpdateLog => _updateLog;
    public string SpecTitleForTest => _specTitle.Text;
    public string RoleForTest => _roleBadge.Role;
    public string LastChangeForTest => _lastChangeValue.Text;

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        _pause.Text = paused ? "Resume" : "Pause";
        _pause.ResizeToText();
        _pause.Emphasised = paused;
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
        AppendLog($"[spec] {_specTitle.Text} - role {role}", DesignTokens.Info);
        NoteChange($"Spec detected: {_specTitle.Text} (role inferred: {role}).");
    }

    public void SetState(string state, Color tone)
    {
        if (_stateValue.Text != state)
        {
            _stateValue.Text = state;
            _stateValue.ForeColor = tone;
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

    public void AppendLog(string text, Color tone)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _statusLog.Append($"{stamp}  {text}", tone);
    }

    public void NoteChange(string text)
    {
        _lastChangeValue.Text = text;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _updateLog.Append($"{stamp}  {text}", DesignTokens.TextSecondary);
    }

    public void SetActivePreset(ConsolePreset? preset)
    {
        foreach (var chip in _presetChips) chip.Active = preset == chip.Preset;
        _presetNote.Text = preset is { } p
            ? $"{ConsolePresets.Get(p).Summary}"
            : "Presets are named bundles of the toggles below; apply one, then adjust by hand.";
    }

    public void ApplyScale(UiScale scale)
    {
        _specTitle.Font = DesignTokens.Type(scale.BaseFont + 4f, FontStyle.Bold);
        _specSummary.Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 1f));
        _stateValue.Font = DesignTokens.Type(scale.BaseFont);
        _bridgeValue.Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 1f));
        _nowValue.Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 1f));
        _logSpecValue.Font = DesignTokens.Type(DesignTokens.BodySize);
        _lastChangeValue.Font = DesignTokens.Type(DesignTokens.BodySize);
        _presetNote.Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 1.5f));
        _statusLog.ApplyScale(scale);
        _updateLog.ApplyScale(scale);
        foreach (var chip in _presetChips) chip.ApplyScale(scale);
    }

    // ----- view stretch: fill the whole canvas, ignoring its padding -----

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent is null) return;
        Parent.Resize += (_, _) => StretchToParent();
        StretchToParent();
    }

    private void StretchToParent()
    {
        if (Parent is null) return;
        var rect = Parent.ClientRectangle;
        if (Bounds != rect) Bounds = rect;
    }

    // ----- construction -----

    private void Build()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = DesignTokens.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, TopBarHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, SpecHeaderHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, PresetStripHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        grid.Controls.Add(BuildTopBar(), 0, 0);
        grid.Controls.Add(BuildSpecHeader(), 0, 1);
        grid.Controls.Add(BuildPresetStrip(), 0, 2);
        grid.Controls.Add(BuildBody(), 0, 3);
        Controls.Add(grid);
    }

    private Control BuildTopBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = ConsolePalette.TitleBar };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = ConsolePalette.TitleBar,
            Padding = new Padding(12, 8, 12, 8),
        };
        _pause.Emphasised = true;
        _pause.Click += (_, _) => PauseRequested?.Invoke();
        _folder.Click += (_, _) => FolderRequested?.Invoke();
        _console.Active = true;
        _console.Click += (_, _) => ConsoleToggleRequested?.Invoke();
        _binds.Click += (_, _) => BindsRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();
        _rotation.Click += (_, _) => RotationRequested?.Invoke();
        _debug.Click += (_, _) => DebugRequested?.Invoke();
        foreach (var item in new[] { _pause, _folder, _console, _binds, _settings, _rotation, _debug })
            flow.Controls.Add(item);
        bar.Controls.Add(flow);
        return bar;
    }

    private Control BuildSpecHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = DesignTokens.Background };
        _roleBadge.SetBounds(18, 24, 88, 30);

        _specTitle.AutoSize = false;
        _specTitle.TextAlign = ContentAlignment.MiddleLeft;
        _specTitle.Font = DesignTokens.Type(DesignTokens.BodySize + 4f, FontStyle.Bold);
        _specTitle.ForeColor = DesignTokens.TextPrimary;
        _specTitle.BackColor = Color.Transparent;
        _specTitle.AutoEllipsis = true;
        _specTitle.Text = "AUTO DETECT";
        _specTitle.AccessibleName = "Detected class and spec";
        _specTitle.SetBounds(118, 10, 600, 28);

        _specSummary.AutoSize = false;
        _specSummary.TextAlign = ContentAlignment.TopLeft;
        _specSummary.Font = DesignTokens.Type(DesignTokens.BodySize - 1f);
        _specSummary.ForeColor = DesignTokens.TextSecondary;
        _specSummary.BackColor = Color.Transparent;
        _specSummary.Text = "Waiting for the bridge to report a class and spec.";
        _specSummary.AccessibleName = "Spec summary";
        _specSummary.SetBounds(118, 38, 600, 40);

        header.Controls.Add(_roleBadge);
        header.Controls.Add(_specTitle);
        header.Controls.Add(_specSummary);
        header.Resize += (_, _) =>
        {
            var w = Math.Max(160, header.ClientSize.Width - 140);
            _specTitle.Width = w;
            _specSummary.Width = w;
        };
        return header;
    }

    private Control BuildPresetStrip()
    {
        var strip = new Panel { Dock = DockStyle.Fill, BackColor = ConsolePalette.Iron, Padding = new Padding(14, 8, 14, 8) };
        var label = new Label
        {
            Text = "Presets",
            AutoSize = false,
            Font = DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold),
            ForeColor = ConsolePalette.Bone,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        label.SetBounds(0, 8, 66, 26);

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent,
            Location = new Point(70, 8),
            Height = 30,
            Width = 640,
        };
        foreach (var bundle in ConsolePresets.All)
        {
            var chip = new PresetChip(bundle);
            var captured = bundle.Preset;
            chip.Click += (_, _) => PresetRequested?.Invoke(captured);
            _presetChips.Add(chip);
            flow.Controls.Add(chip);
        }
        _presetNote.AutoSize = false;
        _presetNote.Font = DesignTokens.Type(DesignTokens.BodySize - 1.5f);
        _presetNote.ForeColor = DesignTokens.TextMuted;
        _presetNote.BackColor = Color.Transparent;
        _presetNote.TextAlign = ContentAlignment.MiddleLeft;
        _presetNote.AutoEllipsis = true;
        _presetNote.Text = "Presets are named bundles of the toggles below; apply one, then adjust by hand.";
        _presetNote.AccessibleName = "Preset note";

        strip.Controls.Add(label);
        strip.Controls.Add(flow);
        strip.Controls.Add(_presetNote);
        strip.Resize += (_, _) =>
        {
            label.Left = 14;
            flow.Left = 84;
            _presetNote.SetBounds(14, 34, Math.Max(120, strip.ClientSize.Width - 28), 16);
        };
        return strip;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = DesignTokens.Background,
            Padding = new Padding(14, 0, 14, 12),
            Margin = Padding.Empty,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        body.Controls.Add(BuildStatusConsole(), 0, 0);
        body.Controls.Add(BuildUpdateLog(), 1, 0);
        return body;
    }

    private Control BuildStatusConsole()
    {
        var card = new RoundedCard { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, 6, 6, 0) };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = new Label
        {
            Text = "Status console",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = DesignTokens.Type(DesignTokens.LabelSize, FontStyle.Bold),
            ForeColor = ConsolePalette.Bone,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _lamp.Dock = DockStyle.Left;
        _lamp.Width = 20;
        _stateValue.AutoSize = false;
        _stateValue.Dock = DockStyle.Fill;
        _stateValue.AutoEllipsis = true;
        _stateValue.TextAlign = ContentAlignment.MiddleLeft;
        _stateValue.BackColor = Color.Transparent;
        _stateValue.Text = "Stopped";
        _stateValue.AccessibleName = "Engine state";
        var stateRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        stateRow.Controls.Add(_stateValue);
        stateRow.Controls.Add(_lamp);

        StyleValue(_bridgeValue, "-");
        _bridgeValue.AccessibleName = "Bridge state";
        StyleValue(_nowValue, "Now: idle - waiting for a MaxDps suggestion.");
        _nowValue.AccessibleName = "Current suggestion";

        var logHeader = new Label
        {
            Text = "Rolling log",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold),
            ForeColor = DesignTokens.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft,
        };
        _statusLog.Dock = DockStyle.Fill;

        table.Controls.Add(header, 0, 0);
        table.Controls.Add(stateRow, 0, 1);
        table.Controls.Add(_bridgeValue, 0, 2);
        table.Controls.Add(_nowValue, 0, 3);
        table.Controls.Add(logHeader, 0, 4);
        table.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Height = 0 }, 0, 5);
        table.Controls.Add(_statusLog, 0, 6);
        card.Controls.Add(table);
        return card;
    }

    private Control BuildUpdateLog()
    {
        var card = new RoundedCard { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(6, 6, 0, 0) };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = new Label
        {
            Text = "Update log",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = DesignTokens.Type(DesignTokens.LabelSize, FontStyle.Bold),
            ForeColor = ConsolePalette.Bone,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        StyleValue(_logSpecValue, "Spec: waiting for the bridge.");
        _logSpecValue.AutoEllipsis = false;
        _logSpecValue.AccessibleName = "Update log spec";
        StyleValue(_lastChangeValue, "Last change: none yet.");
        _lastChangeValue.AutoEllipsis = false;
        _lastChangeValue.AccessibleName = "Update log last change";

        var logHeader = new Label
        {
            Text = "History",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold),
            ForeColor = DesignTokens.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft,
        };
        _updateLog.Dock = DockStyle.Fill;

        table.Controls.Add(header, 0, 0);
        table.Controls.Add(_logSpecValue, 0, 1);
        table.Controls.Add(_lastChangeValue, 0, 2);
        table.Controls.Add(logHeader, 0, 3);
        table.Controls.Add(_updateLog, 0, 4);
        card.Controls.Add(table);
        return card;
    }

    private static void StyleValue(Label label, string text)
    {
        label.AutoSize = false;
        label.Dock = DockStyle.Fill;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.TopLeft;
        label.Font = DesignTokens.Type(DesignTokens.BodySize);
        label.ForeColor = DesignTokens.TextSecondary;
        label.BackColor = Color.Transparent;
        label.Text = text;
    }

    private void SeedLog()
    {
        AppendLog("Console ready. Suggest-only: the companion relays the addon's strip.", DesignTokens.TextMuted);
        NoteChange("Console home loaded. Presets are named bundles of the existing toggles.");
    }
}
