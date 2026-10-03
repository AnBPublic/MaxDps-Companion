using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

// ----- console set (iron-and-brass instrument panel) -----

/// <summary>
/// Shared palette for the MaxDPS Companion console.
/// Iron-and-brass instrument panel: quiet ground, one brass accent.
/// </summary>
internal static class ConsolePalette
{
    public static readonly Color Iron = Color.FromArgb(0x1A, 0x26, 0x2C);
    public static readonly Color TitleBar = Color.FromArgb(0x14, 0x1E, 0x23);
    public static readonly Color Panel = Color.FromArgb(0x21, 0x2F, 0x36);
    public static readonly Color Field = Color.FromArgb(0x24, 0x34, 0x3C);
    public static readonly Color Keyline = Color.FromArgb(0x3A, 0x4A, 0x52);
    public static readonly Color Bone = Color.FromArgb(0xE9, 0xE3, 0xD3);
    public static readonly Color Tidewash = Color.FromArgb(0x93, 0xA5, 0xAE);
    // M-route: one brass accent. The console palette now reuses the shell
    // token so ConsoleHome / ChamferButton and the popups cannot drift apart.
    public static readonly Color Brass = DesignTokens.Accent;
    public static readonly Color Ember = Color.FromArgb(0xB2, 0x3A, 0x2C);
    public static readonly Color EmberLight = Color.FromArgb(0xE0, 0x68, 0x4E);

    public static GraphicsPath Chamfer(Rectangle r, int cut)
    {
        var path = new GraphicsPath();
        path.AddLine(r.Left + cut, r.Top, r.Right - cut, r.Top);
        path.AddLine(r.Right - cut, r.Top, r.Right, r.Top + cut);
        path.AddLine(r.Right, r.Top + cut, r.Right, r.Bottom - cut);
        path.AddLine(r.Right, r.Bottom - cut, r.Right - cut, r.Bottom);
        path.AddLine(r.Right - cut, r.Bottom, r.Left + cut, r.Bottom);
        path.AddLine(r.Left + cut, r.Bottom, r.Left, r.Bottom - cut);
        path.AddLine(r.Left, r.Bottom - cut, r.Left, r.Top + cut);
        path.AddLine(r.Left, r.Top + cut, r.Left + cut, r.Top);
        path.CloseFigure();
        return path;
    }
}

internal enum ButtonRole { Primary, Ghost, Danger }

/// <summary>
/// Flat action button with a single chamfered corner cut. The chamfer marks
/// "you can press this" - panels stay square. Colour is never the only signal:
/// the primary action is always the leftmost button.
/// AccentColor is a MaxDPS addition: when set it replaces the role colour
/// (PRP chrome hex) while the role still drives text colour and semantics.
/// </summary>
internal sealed class ChamferButton : Button
{
    private ButtonRole _role = ButtonRole.Ghost;
    private bool _hover;
    private bool _pressed;

    public ButtonRole Role
    {
        get => _role;
        set
        {
            _role = value;
            ForeColor = value == ButtonRole.Primary ? DesignTokens.Background : DesignTokens.TextPrimary;
            Invalidate();
        }
    }

    /// <summary>Optional explicit fill (PRP chrome: Start #237EF6 etc.). Null = role colour.</summary>
    public Color? AccentColor
    {
        get => _accent;
        set { _accent = value; Invalidate(); }
    }

    private Color? _accent;

    private float _pressScale = 1f;
    private System.Windows.Forms.Timer? _pressTimer;

    public ChamferButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        Font = DesignTokens.Type(DesignTokens.LabelSize, FontStyle.Bold);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        BackColor = DesignTokens.Surface;
        ForeColor = DesignTokens.TextPrimary;
        Height = 40;
        // AutoSize lets FlowLayoutPanel measure the text instead of
        // clipping it at a fixed width — the narrow-window clip source.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18, 0, 18, 0);
    }

    private Color BaseColor() => AccentColor ?? _role switch
    {
        ButtonRole.Primary => DesignTokens.Accent,
        ButtonRole.Danger => DesignTokens.Danger,
        _ => DesignTokens.Surface,
    };

    /// <summary>Trailing glyph inside a nested circle (button-in-button). Primary only.</summary>
    public string? TrailingGlyph { get; set; } = "\u2197"; // ↗

    // Magnetic press physics: pointer-down shrinks toward 0.98 instantly,
    // release springs back - no linear fades, transform-only (paint offset).
    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        AnimatePressScale(1f);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        SetPressScale(0.98f);
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        AnimatePressScale(1f);
        base.OnMouseUp(e);
    }

    private void SetPressScale(float value)
    {
        _pressTimer?.Stop();
        _pressScale = value;
        Invalidate();
    }

    private void AnimatePressScale(float target)
    {
        _pressTimer ??= new System.Windows.Forms.Timer { Interval = 16 };
        _pressTimer.Stop();
        _pressTimer.Tick -= PressTick;
        _pressTarget = target;
        _pressTimer.Tick += PressTick;
        _pressTimer.Start();
    }

    private float _pressTarget = 1f;

    private void PressTick(object? sender, EventArgs e)
    {
        // Critically damped approach: fast, no overshoot on a pressable.
        _pressScale += (_pressTarget - _pressScale) * 0.45f;
        if (Math.Abs(_pressTarget - _pressScale) < 0.001f)
        {
            _pressScale = _pressTarget;
            _pressTimer?.Stop();
        }
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var glyph = _role == ButtonRole.Primary && !string.IsNullOrEmpty(TrailingGlyph) ? 34 : 0;
        return new Size(text.Width + Padding.Horizontal + glyph + 8, Math.Max(38, text.Height + 16));
    }

    /// <summary>Width-tier type step (D5); the owning row owns the height.</summary>
    public void ApplyScale(UiScale scale)
    {
        Font = DesignTokens.Type(Math.Max(8f, scale.BaseFont - 0.5f), FontStyle.Bold);
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0) return;
        using var path = Ui.Rounded(new Rectangle(0, 0, Width, Height), Math.Max(8, Height / 2));
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = BaseColor();
        if (!Enabled) color = DesignTokens.Disabled;
        else if (_pressed) color = DesignTokens.Darken(color, 0.12f);
        else if (_hover) color = DesignTokens.Lighten(color, 0.10f);

        // Transform-only press scale: shrink the painted shape toward center.
        var w = (int)(Width * _pressScale);
        var h = (int)(Height * _pressScale);
        var bounds = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        var radius = Math.Max(8, Math.Min(bounds.Height / 2, DesignTokens.RadiusOuter));
        using var path = Ui.Rounded(bounds, radius);

        var ghost = _role == ButtonRole.Ghost;
        if (ghost)
        {
            using var fill = new SolidBrush(DesignTokens.Blend(Color.White, DesignTokens.Surface, _hover ? 0.07f : 0.035f));
            using var border = new Pen(Focused ? DesignTokens.Accent : DesignTokens.Hairline, Focused ? 2F : 1F);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
        else
        {
            using var fill = new SolidBrush(color);
            e.Graphics.FillPath(fill, path);
            // Machined inner top highlight (double-bezel edge).
            using var hl = new Pen(DesignTokens.InnerHighlight, 1F);
            using var hlPath = Ui.Rounded(
                new Rectangle(bounds.X + 2, bounds.Y + 1, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 2)),
                Math.Max(6, radius - 2));
            e.Graphics.DrawPath(hl, hlPath);
        }

        var textColor = !Enabled ? DesignTokens.TextMuted
            : _role == ButtonRole.Primary ? DesignTokens.Background
            : DesignTokens.TextPrimary;
        var glyphSpace = _role == ButtonRole.Primary && !string.IsNullOrEmpty(TrailingGlyph) ? 34 : 0;
        var textRect = new Rectangle(bounds.X + 16, bounds.Y,
            Math.Max(10, bounds.Width - 16 - 16 - glyphSpace), bounds.Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        if (glyphSpace > 0)
        {
            // The "button-in-button" trailing icon: its own circular wrapper,
            // flush with the right inner padding, kinetic on hover.
            var d = Math.Min(bounds.Height - 10, 26);
            var shift = _hover ? 2 : 0;
            var cx = bounds.Right - 7 - d + shift;
            var cy = bounds.Y + (bounds.Height - d) / 2;
            using var circle = new SolidBrush(Color.FromArgb(46, 0, 0, 0));
            e.Graphics.FillEllipse(circle, cx, cy, d, d);
            using var glyphFont = DesignTokens.Type(10F, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, TrailingGlyph!, glyphFont,
                new Rectangle(cx, cy, d, d), textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        if (Focused && Enabled && ghost)
        {
            var focusBounds = new Rectangle(3, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 6));
            using var focusPath = Ui.Rounded(focusBounds, Math.Max(8, focusBounds.Height / 2));
            using var focusPen = new Pen(DesignTokens.Accent, 2F);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }
}

// M-route cleanup: RuleSection removed (zero call sites; GlassCard/section
// headers own this surface now).

internal sealed class TitleBarButton : UiClickable
{
    public Color HoverColor { get; set; } = Color.FromArgb(54, 66, 73);

    public TitleBarButton()
    {
        Size = new Size(48, 48);
        Font = new Font(MainForm.UiFontPublic, 14F, FontStyle.Regular);
        ForeColor = Color.FromArgb(215, 224, 229);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Hovered)
        {
            using var brush = new SolidBrush(HoverColor);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal static class WindowChrome
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void BeginDrag(IntPtr handle)
    {
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    public static void EnableModernCorners(IntPtr handle)
    {
        var preference = DwmwcpRound;
        try { DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int)); }
        catch { }
    }
}

// ----- PRP chrome set -----

internal sealed class GradientCanvas : Panel
{
    public GradientCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(15, 56, 76), Color.FromArgb(48, 47, 19), 38F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var overlay = new LinearGradientBrush(ClientRectangle, Color.FromArgb(0, 8, 31, 42), Color.FromArgb(150, 8, 35, 49), 145F);
        e.Graphics.FillRectangle(overlay, ClientRectangle);
    }
}

internal sealed class RoundedCard : Panel
{
    public RoundedCard()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = Rounded(bounds, 20);
        using var fill = new SolidBrush(Color.FromArgb(78, 244, 247, 249));
        using var border = new Pen(Color.FromArgb(52, 255, 255, 255), 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }

    private static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var r = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
        var d = r * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, d, d, 180, 90);
        path.AddArc(rectangle.Right - d - 1, rectangle.Top, d, d, 270, 90);
        path.AddArc(rectangle.Right - d - 1, rectangle.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Group header: brass marker + Title Case label + hairline rule. Segments
/// the card into Spells / Combat / Interrupt.
///
/// Design pass (skill: high-end-visual-design §4 eyebrow, frontend-design
/// "labels are information, not decoration"; apple-design §15 tracking):
/// the old ALL-CAPS micro-label at Tidewash contrast read as a generated
/// eyebrow and was hard to scan — a named tell of templated UI and the
/// user's "higher contrast group descriptions" complaint. Now: sentence
/// case (language, not chrome), near-white weight-emphasised type
/// (hierarchy from weight + contrast, per §16 simplicity), a slightly
/// taller brass marker aligned to the text's optical centre, and the
/// hairline is a soft Keyline rule so the grouping is structural — it
/// encodes proximity, per "structural devices encode useful information".
/// </summary>
internal sealed class GroupHeader : Control
{
    public GroupHeader()
    {
        Height = 24;
        Margin = new Padding(4, 0, 4, 0);
        Font = new Font(MainForm.UiFontPublic, 9.5F, FontStyle.Bold);
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        // High-contrast group label: Bone (near-white), not the muted
        // Tidewash that made the segments disappear into the card.
        ForeColor = ConsolePalette.Bone;
    }

    public GroupHeader(string title) : this()
    {
        Text = title;
    }

    /// <summary>Width-tier type step (D5).</summary>
    public void ApplyScale(UiScale scale)
    {
        Font = new Font(MainForm.UiFontPublic, Math.Max(8f, scale.BaseFont - 0.5f), FontStyle.Bold);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        // Brass marker, optically centred on the text (8px, not 6).
        using var tick = new SolidBrush(ConsolePalette.Brass);
        e.Graphics.FillRectangle(tick, 14, 9, 5, 5);
        var label = Text;  // sentence case as authored — no shouting caps
        using var text = new SolidBrush(ForeColor);
        e.Graphics.DrawString(label, Font, text, 26, 4);
        var size = e.Graphics.MeasureString(label, Font);
        var y = 15;
        var x1 = 26 + (int)Math.Ceiling(size.Width) + 12;
        if (x1 < Width - 14)
        {
            using var pen = new Pen(ConsolePalette.Keyline, 1F);
            e.Graphics.DrawLine(pen, x1, y, Width - 14, y);
        }
    }
}

internal sealed class SettingRow : Panel, IUiMeasured
{
    private readonly Label titleLabel;
    private readonly Label subtitleLabel;
    private readonly ToggleSwitch toggle;

    public SettingRow(string title, string subtitle, ToggleSwitch toggle)
    {
        this.toggle = toggle;
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
        Margin = Padding.Empty;

        titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DesignTokens.Type(DesignTokens.LabelSize, FontStyle.Bold),
            ForeColor = DesignTokens.TextPrimary,
            BackColor = Color.Transparent
        };
        subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.TopLeft,
            Font = DesignTokens.Type(DesignTokens.BodySize),
            ForeColor = DesignTokens.TextSecondary,
            BackColor = Color.Transparent
        };
        toggle.Anchor = AnchorStyles.None;
        toggle.Margin = Padding.Empty;
        Controls.Add(titleLabel);
        Controls.Add(subtitleLabel);
        Controls.Add(toggle);
        // The labels ellipsise at narrow tiers; always expose their full text on
        // hover. A later Hint assignment overrides this with the longer
        // condition description.
        _tip.SetToolTip(titleLabel, title);
        _tip.SetToolTip(subtitleLabel, subtitle);
    }

    /// <summary>Measured height: fits title + up to three wrapped subtitle lines.</summary>
    public int MeasuredHeight(int width)
    {
        var textWidth = TextWidthFor(width);
        MeasureHeights(textWidth, out var titleH, out var subH);
        return 9 + titleH + 2 + subH + 12;
    }

    /// <summary>
    /// Re-applies a width tier (D5): title/subtitle type scale and the toggle
    /// size. Re-measures, so the owning row must be laid out again afterwards.
    /// </summary>
    public void ApplyScale(UiScale scale)
    {
        titleLabel.Font = DesignTokens.Type(scale.BaseFont + 1f, FontStyle.Bold);
        subtitleLabel.Font = DesignTokens.Type(scale.BaseFont);
        toggle.Size = scale.ToggleSize;
        PerformLayout();
        Invalidate();
    }

    // S5: per-row owned tooltip. The old static shared ToolTip was a native
    // control touched from several STA threads at once (and a native bubble
    // over the themed surface); one owned tip per row is thread-safe and themed.
    private readonly OwnedToolTip _tip = new();

    private string? _hint;

    /// <summary>
    /// Longer description shown on hover (the visible subtitle stays short so
    /// the bubble never crams). Set by the card builder after construction.
    /// </summary>
    internal string? Hint
    {
        get => _hint;
        set
        {
            _hint = value;
            if (string.IsNullOrWhiteSpace(value)) return;
            _tip.SetToolTip(this, value);
            _tip.SetToolTip(titleLabel, value);
            _tip.SetToolTip(subtitleLabel, value);
            _tip.SetToolTip(toggle, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }

    private int TextWidthFor(int width)
    {
        const int left = 18;
        const int right = 18;
        const int toggleGap = 22;
        return Math.Max(40, width - left - right - toggle.Width - toggleGap);
    }

    private void MeasureHeights(int textWidth, out int titleH, out int subH)
    {
        titleH = TextRenderer.MeasureText(titleLabel.Text, titleLabel.Font,
            new Size(textWidth, 0), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
        subH = TextRenderer.MeasureText(subtitleLabel.Text, subtitleLabel.Font,
            new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        // Cap the subtitle at three lines: the taller pair-row bubble fits
        // title + three wrapped lines even at the minimum window width; the
        // full-length description is always available on hover (Hint).
        var lineH = Math.Max(12, subtitleLabel.Font.Height);
        subH = Math.Min(subH, lineH * 3 + 4);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // Epoch-8 bubble (84px row, two toggles per row): title at top-9,
        // subtitle wraps to at most three lines, toggle vertically centred.
        const int left = 18;
        const int right = 14;
        var textWidth = TextWidthFor(ClientSize.Width);
        MeasureHeights(textWidth, out var titleH, out var subH);
        titleLabel.Bounds = new Rectangle(left, 9, textWidth, titleH);
        subtitleLabel.Bounds = new Rectangle(left, 9 + titleH + 2, textWidth, subH);
        toggle.Location = new Point(ClientSize.Width - right - toggle.Width, Math.Max(0, (ClientSize.Height - toggle.Height) / 2));
    }

    private static readonly Color RowFill = DesignTokens.Blend(Color.White, DesignTokens.Surface, 0.035f);
    private static readonly Color RowFillAlt = DesignTokens.Blend(Color.White, DesignTokens.Surface, 0.060f);

    /// <summary>Alternating row depth for group scanning (zebra). Set by the parent.</summary>
    internal bool AlternateFill { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = RoundedCardPath(bounds, DesignTokens.RadiusControl);
        using var fill = new SolidBrush(AlternateFill ? RowFillAlt : RowFill);
        using var border = new Pen(DesignTokens.Hairline, 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }

    private static GraphicsPath RoundedCardPath(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// One "Rotation"/"Automation" row: two <see cref="SettingRow"/>s side by side. It is
/// <see cref="IUiMeasured"/>, so the hero card sizes the row to the real
/// wrapped text (D3) instead of a fixed 66 px literal that clipped subtitles.
/// </summary>
internal sealed class ToggleRowPanel : Panel, IUiMeasured
{
    private readonly SettingRow _left;
    private readonly SettingRow _right;

    public int Gap { get; set; } = DesignTokens.SpaceS;

    public ToggleRowPanel(SettingRow left, SettingRow right)
    {
        _left = left;
        _right = right;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Controls.Add(left);
        Controls.Add(right);
    }

    public int MeasuredHeight(int width)
    {
        var half = Math.Max(80, (width - Gap) / 2);
        return Math.Max(_left.MeasuredHeight(half), _right.MeasuredHeight(half));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var half = Math.Max(80, (ClientSize.Width - Gap) / 2);
        _left.Bounds = new Rectangle(0, 0, half, ClientSize.Height);
        _right.Bounds = new Rectangle(half + Gap, 0, Math.Max(40, ClientSize.Width - half - Gap), ClientSize.Height);
        _left.PerformLayout();
        _right.PerformLayout();
    }
}

internal sealed class ToggleSwitch : UiClickable
{
    private bool isChecked;
    public event EventHandler? CheckedChanged;
    // Fluid motion (skill: apple-design §1 response, §4 springs): the thumb
    // does NOT jump. It animates from its CURRENT on-screen value toward the
    // new one with a critically-damped approach (no overshoot by default —
    // §4 "start most UI at damping 1.0"), so rapid toggling is interruptible
    // and redirectable mid-flight. Transform/paint only (no layout), per
    // §11 frame smoothness. Default state has no timer running (zero cost).
    private float _pos;          // 0 = off, 1 = on (animated)
    private System.Windows.Forms.Timer? _anim;

    private static readonly Color OffTrack = DesignTokens.Border;
    private static readonly Color OnTrack = DesignTokens.Accent;

    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            // Animate only once the switch is on screen: the initial
            // settings load (construction time) must SNAP to its value — a
            // control that animates itself on first paint looks broken
            // (skill: apple-design §1 — motion answers a user action, it is
            // not decoration). User clicks animate; programmatic first-set
            // does not.
            if (Visible && IsHandleCreated)
            {
                StartAnim();
            }
            else
            {
                _pos = value ? 1f : 0f;
                Invalidate();
            }
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        Size = new Size(52, 30);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    private void StartAnim()
    {
        _anim ??= new System.Windows.Forms.Timer { Interval = 15 };
        _anim.Stop();
        _anim.Tick -= Tick;
        _anim.Tick += Tick;
        _anim.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var target = isChecked ? 1f : 0f;
        _pos += (target - _pos) * 0.35f;           // critically damped
        if (Math.Abs(target - _pos) < 0.002f)
        {
            _pos = target;
            _anim?.Stop();
        }
        Invalidate();
    }



    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    // §49: Space/Enter activate the switch when it has focus; TabStop is the
    // Control default and the visual focus ring is painted below.
    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Settle instantly on first paint if no animation is queued.
        if (_anim is null || !_anim.Enabled) _pos = isChecked ? 1f : 0f;

        var track = new Rectangle(1, 3, Width - 2, Height - 6);
        using var trackPath = Capsule(track);
        // State is luminance + hue + thumb position, never hue alone
        // (colour-vision safe, skill §16). Hover brightens the OFF track a
        // touch — feedback belongs on hover, per §1.
        var off = Hovered && !isChecked ? ControlPaint.Light(OffTrack, 0.15F) : OffTrack;
        using var trackBrush = new SolidBrush(Lerp(off, OnTrack, _pos));
        e.Graphics.FillPath(trackBrush, trackPath);
        var diameter = Height - 10;
        var x = (int)(5 + (Width - diameter - 10) * _pos);
        // Soft under-thumb shadow for material depth (double-bezel layered
        // feel), then the thumb.
        using var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
        e.Graphics.FillEllipse(shadow, x, 6, diameter, diameter);
        using var thumb = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(thumb, x, 5, diameter, diameter);

        if (Focused && Enabled)
        {
            using var focus = new Pen(DesignTokens.Accent, 2F);
            e.Graphics.DrawRectangle(focus, 0, 1, Width - 1, Height - 2);
        }
    }

    private static GraphicsPath Capsule(Rectangle rectangle)
    {
        var path = new GraphicsPath();
        var d = rectangle.Height;
        path.AddArc(rectangle.Left, rectangle.Top, d, d, 90, 180);
        path.AddArc(rectangle.Right - d, rectangle.Top, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }
}

// M-route cleanup: StatusDot removed (zero call sites; LinkLamp/pills carry
// status with a glyph or word, never colour alone).

// ----- classic (v1.3.9) restored primitives -----

/// <summary>Round link lamp. Always paired with a text word, never colour-alone.</summary>
internal sealed class LinkLamp : Control
{
    private Color _dot = ConsolePalette.Keyline;

    public Color Dot
    {
        get => _dot;
        set { if (_dot != value) { _dot = value; Invalidate(); } }
    }

    public LinkLamp()
    {
        Size = new Size(14, 14);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Link lamp";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var fill = new SolidBrush(_dot);
        e.Graphics.FillEllipse(fill, bounds);
    }
}

/// <summary>
/// Renders the MaxDps pixel-bridge cells from the sampled hex string, so the
/// hero shows the actual strip the addon is drawing. The magic cell carries a
/// brass tick underneath. Unparseable samples draw as empty outlines.
/// </summary>
internal sealed class StripView : Control
{
    private string _sample = "";

    public string Sample
    {
        get => _sample;
        set { if (_sample != value) { _sample = value; Invalidate(); } }
    }

    public StripView()
    {
        Height = 30;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Bridge strip";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        const int sw = 26, h = 18, gap = 4, y = 2;
        var tokens = _sample.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < PixelProtocol.CellCount; i++)
        {
            var x = i * (sw + gap);
            if (x + sw > Width) break;
            var rect = new Rectangle(x, y, sw, h);
            if (i < tokens.Length && TryCell(tokens[i], out var color))
            {
                using var fill = new SolidBrush(color);
                e.Graphics.FillRectangle(fill, rect);
                using var edge = new Pen(ConsolePalette.Keyline, 1F);
                e.Graphics.DrawRectangle(edge, rect);
                if (i == 0)
                {
                    using var tick = new SolidBrush(ConsolePalette.Brass);
                    e.Graphics.FillRectangle(tick, x, y + h + 3, sw, 2);
                }
            }
            else
            {
                using var edge = new Pen(ConsolePalette.Keyline, 1F);
                e.Graphics.DrawRectangle(edge, rect);
            }
        }
    }

    private static bool TryCell(string token, out Color color)
    {
        color = Color.Empty;
        if (token.Length != 6) return false;
        try
        {
            var rgb = Convert.ToInt32(token, 16);
            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }
}

/// <summary>
/// NumericUpDown that does not steal the wheel from the page it lives in.
/// While unfocused the wheel is forwarded to the nearest scrollable ancestor
/// (the Advanced tab's AutoScroll panel); while focused the stock value-step
/// behaviour is kept so a user can still spin the number. This is a real-input
/// path only, never touched by the status timer.
/// </summary>
internal sealed class WheelSafeNumeric : NumericUpDown
{
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Focused || !ForwardToScrollableParent(e.Delta)) base.OnMouseWheel(e);
    }

    private bool ForwardToScrollableParent(int delta)
    {
        if (delta == 0) return false;
        for (var parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not ScrollableControl scroll || !scroll.AutoScroll) continue;
            var max = Math.Max(0, scroll.VerticalScroll.Maximum - scroll.ClientSize.Height);
            if (max <= 0) return true; // scrollable but nothing to scroll: swallow, don't step the value
            var lines = SystemInformation.MouseWheelScrollLines;
            var step = lines < 0 ? Math.Max(1, scroll.ClientSize.Height) : Math.Max(1, lines) * 16;
            var current = -scroll.AutoScrollPosition.Y;
            var target = Math.Clamp(current - (int)(delta / 120.0 * step), 0, max);
            scroll.AutoScrollPosition = new Point(Math.Max(0, -scroll.AutoScrollPosition.X), target);
            return true;
        }
        return false;
    }
}

/// <summary>Class/spec pill: rounded capsule with the detected class and spec.</summary>
internal sealed class ClassBadge : Control
{
    public ClassBadge()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        Font = DesignTokens.Type(8.25F, FontStyle.Bold);
        ForeColor = DesignTokens.TextPrimary;
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Class and spec";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = new GraphicsPath();
        var radius = Math.Min(13, bounds.Height / 2);
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(DesignTokens.Tint(DesignTokens.Info, DesignTokens.Surface));
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
