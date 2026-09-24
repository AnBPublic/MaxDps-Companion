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
    public static readonly Color Brass = Color.FromArgb(0xC6, 0x9A, 0x3F);
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
            ForeColor = value == ButtonRole.Primary ? ConsolePalette.Iron : ConsolePalette.Bone;
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
        Font = new Font(MainForm.UiFontPublic, 10F, FontStyle.Bold);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        BackColor = ConsolePalette.Field;
        ForeColor = ConsolePalette.Bone;
        Height = 40;
        // AutoSize lets FlowLayoutPanel measure the text instead of
        // clipping it at a fixed width — the narrow-window clip source.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18, 0, 18, 0);
    }

    private Color BaseColor() => AccentColor ?? _role switch
    {
        ButtonRole.Primary => ConsolePalette.Brass,
        ButtonRole.Danger => ConsolePalette.Ember,
        _ => ConsolePalette.Field,
    };

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

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0) return;
        using var path = ConsolePalette.Chamfer(new Rectangle(0, 0, Width, Height), 6);
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = BaseColor();
        if (!Enabled) color = ConsolePalette.Keyline;
        else if (_pressed) color = ControlPaint.Dark(color, 0.15F);
        else if (_hover) color = ControlPaint.Light(color, 0.12F);

        // Transform-only press scale: shrink the painted shape toward center.
        var w = (int)(Width * _pressScale);
        var h = (int)(Height * _pressScale);
        var bounds = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        using var path = ConsolePalette.Chamfer(bounds, 6);
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(fill, path);

        if (_role == ButtonRole.Ghost && Enabled)
        {
            using var border = new Pen(ConsolePalette.Keyline, 1F);
            e.Graphics.DrawPath(border, path);
        }

        var textColor = Enabled ? ForeColor : ConsolePalette.Tidewash;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && Enabled)
        {
            var focusColor = _role == ButtonRole.Primary ? ConsolePalette.Iron : ConsolePalette.Brass;
            var focusBounds = new Rectangle(3, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 6));
            using var focusPath = ConsolePalette.Chamfer(focusBounds, 4);
            using var focusPen = new Pen(focusColor, 2F);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }
}

/// <summary>
/// Double-bezel section: an outer hairline shell with concentric inner content
/// (outer 12px radius, inner 8px), eyebrow-tag title, hairline rule.
/// Sections read as machined plates, never flat boxes on the background.
/// </summary>
internal sealed class RuleSection : Panel
{
    private string _title = "";

    public string SectionTitle
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    public RuleSection()
    {
        Padding = new Padding(10, 34, 10, 10);
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    private static GraphicsPath Squircle(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Outer shell: faint fill + hairline, 12px concentric radius.
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var outerPath = Squircle(outer, 12);
        using var shell = new SolidBrush(Color.FromArgb(14, 255, 255, 255));
        e.Graphics.FillPath(shell, outerPath);
        using var hairline = new Pen(Color.FromArgb(28, 255, 255, 255), 1F);
        e.Graphics.DrawPath(hairline, outerPath);

        // Eyebrow tag: microscopic wide-tracked label with brass tick.
        using var tick = new SolidBrush(ConsolePalette.Brass);
        e.Graphics.FillRectangle(tick, 12, 10, 6, 6);
        using var font = new Font(MainForm.UiFontPublic, 9F, FontStyle.Bold);
        using var text = new SolidBrush(ConsolePalette.Bone);
        // Wide tracking for the eyebrow feel: draw with extra spacing via format.
        e.Graphics.DrawString(SectionTitle.ToUpperInvariant(), font, text, 24, 6);
        var size = e.Graphics.MeasureString(SectionTitle.ToUpperInvariant(), font);
        var y = 17;
        var x1 = 24 + (int)Math.Ceiling(size.Width) + 12;
        if (x1 < Width - 12)
        {
            using var pen = new Pen(ConsolePalette.Keyline, 1F);
            e.Graphics.DrawLine(pen, x1, y, Width - 12, y);
        }

        base.OnPaint(e);
    }
}

/// <summary>Round link lamp. Always paired with a text word, never colour-alone.</summary>
internal sealed class LinkLamp : Control
{
    private Color _dot = ConsolePalette.Keyline;

    public Color Dot
    {
        get => _dot;
        set { _dot = value; Invalidate(); }
    }

    public LinkLamp()
    {
        Size = new Size(14, 14);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
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
/// Renders the nine MaxDps pixel-bridge cells from the sampled hex string, so
/// the hero shows the actual strip the addon is drawing: magic, main,
/// offensive, interrupt, defensive, consumable, trinket, state+heartbeat,
/// ver+checksum. The magic cell carries a brass tick underneath.
/// Unparseable samples draw as empty outlines.
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
        // 9 cells × (28px + 6px gap) = 306px wide.
        Size = new Size(306, 30);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        const int sw = 28, h = 18, gap = 6, y = 2;
        var tokens = _sample.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < PixelProtocol.CellCount; i++)
        {
            var x = i * (sw + gap);
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

internal sealed class TitleBarButton : Control
{
    private bool hovered;

    public Color HoverColor { get; set; } = Color.FromArgb(54, 66, 73);

    public TitleBarButton()
    {
        Size = new Size(48, 48);
        Font = new Font(MainForm.UiFontPublic, 14F, FontStyle.Regular);
        ForeColor = Color.FromArgb(215, 224, 229);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (hovered)
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
/// Eyebrow group header: brass tick + small-caps label + hairline rule, the
/// same visual language as <see cref="RuleSection"/>. Used to segment the
/// hero card into labelled groups (spell slots / behaviour / interrupt) so
/// the groups read as segments instead of one undifferentiated block.
/// </summary>
internal sealed class GroupHeader : Control
{
    public GroupHeader()
    {
        Height = 22;
        Margin = new Padding(4, 0, 4, 0);
        Font = new Font(MainForm.UiFontPublic, 9F, FontStyle.Bold);
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        ForeColor = ConsolePalette.Tidewash;
    }

    public GroupHeader(string title) : this()
    {
        Text = title;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var tick = new SolidBrush(ConsolePalette.Brass);
        e.Graphics.FillRectangle(tick, 14, 8, 6, 6);
        var label = Text.ToUpperInvariant();
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

internal sealed class SettingRow : Panel
{
    private readonly Label titleLabel;
    private readonly Label subtitleLabel;
    private readonly ToggleSwitch toggle;

    public SettingRow(string title, string subtitle, ToggleSwitch toggle)
    {
        this.toggle = toggle;
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        // Epoch-4: fixed 76px row, labels measured to fit. The table row
        // reserves the same 76px, so the row is never squeezed (the old
        // percent-row bug) and never mis-measured (the AutoSize flow bug).
        // Subtitles wrap to two lines via MeasureHeights; see OnLayout.

        titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 11.5F),
            // Epoch-2 type scale: near-white title on the smoke fill clears
            // WCAG AA large-text contrast with margin.
            ForeColor = Color.FromArgb(252, 253, 254),
            BackColor = Color.Transparent
        };
        subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = false,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 9F),
            // Epoch-2: subtitle lifted two tonal steps so secondary text
            // still passes AA against the smoke fill in daylight conditions.
            ForeColor = Color.FromArgb(226, 235, 240),
            BackColor = Color.Transparent
        };
        toggle.Anchor = AnchorStyles.None;
        toggle.Margin = Padding.Empty;
        Controls.Add(titleLabel);
        Controls.Add(subtitleLabel);
        Controls.Add(toggle);
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
        // Cap the subtitle at two lines: every hero subtitle fits in two
        // lines even at the minimum window width, so the row stays compact.
        var lineH = Math.Max(12, subtitleLabel.Font.Height);
        subH = Math.Min(subH, lineH * 2 + 4);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // Epoch-5 compact row (64px): title at top-8, subtitle wraps to at
        // most two lines, toggle vertically centred. Title 11.5pt ≈ 22px,
        // subtitle 9pt ≈ 2x15px: 8+22+2+30+2 = 64 exactly.
        const int left = 18;
        const int right = 18;
        var textWidth = TextWidthFor(ClientSize.Width);
        MeasureHeights(textWidth, out var titleH, out var subH);
        titleLabel.Bounds = new Rectangle(left, 8, textWidth, titleH);
        subtitleLabel.Bounds = new Rectangle(left, 8 + titleH + 2, textWidth, subH);
        toggle.Location = new Point(ClientSize.Width - right - toggle.Width, Math.Max(0, (ClientSize.Height - toggle.Height) / 2));
    }

    private static readonly Color RowFill = Color.FromArgb(84, 107, 121);
    private static readonly Color RowFillAlt = Color.FromArgb(78, 100, 114);

    /// <summary>Alternating row depth for group scanning (zebra): even rows
    /// use the base smoke fill, odd rows the half-step darker tone. The
    /// difference is deliberately subtle (±6) — a scan aid, not a stripe.
    /// Set by the parent after construction; defaults to the base fill.
    /// </summary>
    internal bool AlternateFill { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = RoundedCardPath(bounds, 14);
        // Design epoch-6: layered tonal cards (+ zebra). The filled body
        // sits on the card's frosted surface, so a flat smoke fill with a
        // single 1px lowlight reads depth; the old translucent dark wash
        // muddied low-contrast subtitles (WCAG body-text contrast).
        using var fill = new SolidBrush(AlternateFill ? RowFillAlt : RowFill);
        using var border = new Pen(Color.FromArgb(120, 255, 255, 255), 1F);
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

internal sealed class ClassBadge : Control
{
    public ClassBadge()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        Font = new Font("Segoe UI Semibold", 8.25F);
        ForeColor = Color.FromArgb(224, 239, 248);
        BackColor = Color.Transparent;
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
        using var fill = new SolidBrush(Color.FromArgb(35, 126, 246));
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool isChecked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            Invalidate();
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

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(1, 3, Width - 2, Height - 6);
        using var trackPath = Capsule(track);
        // Epoch-2 switch semantics: ON keeps the product blue, OFF drops to
        // a desaturated slate so state is luminance + hue, never hue alone
        // (colour-vision safe); thumb carries a soft drop shadow for depth.
        using var trackBrush = new SolidBrush(Checked ? Color.FromArgb(36, 132, 246) : Color.FromArgb(96, 110, 119));
        e.Graphics.FillPath(trackBrush, trackPath);
        var diameter = Height - 10;
        var x = Checked ? Width - diameter - 5 : 5;
        using var thumb = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(thumb, x, 5, diameter, diameter);
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

internal sealed class StatusDot : Control
{
    private Color _dot = Color.FromArgb(71, 230, 148);

    /// <summary>Dot colour. Default is the PRP running-green.</summary>
    public Color Dot
    {
        get => _dot;
        set { _dot = value; Invalidate(); }
    }

    public StatusDot()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(55, _dot.R, _dot.G, _dot.B));
        using var dot = new SolidBrush(_dot);
        e.Graphics.FillEllipse(glow, 1, Height / 2 - 8, 16, 16);
        e.Graphics.FillEllipse(dot, 5, Height / 2 - 4, 8, 8);
    }
}
