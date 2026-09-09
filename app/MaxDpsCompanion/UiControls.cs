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
/// Renders the eight MaxDps pixel-bridge cells from the sampled hex string, so
/// the hero shows the actual strip the addon is drawing: magic, main, CD,
/// interrupt, defensive, consumable, state+heartbeat, ver+checksum. The magic
/// cell carries a brass tick underneath. Unparseable samples draw as empty
/// outlines.
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
        Size = new Size(272, 30);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        const int sw = 28, h = 18, gap = 6, y = 2;
        var tokens = _sample.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < 8; i++)
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

        titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 11.25F),
            ForeColor = Color.FromArgb(250, 250, 252),
            BackColor = Color.Transparent
        };
        subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 8.75F),
            ForeColor = Color.FromArgb(190, 207, 215),
            BackColor = Color.Transparent
        };
        toggle.Anchor = AnchorStyles.None;
        toggle.Margin = Padding.Empty;
        Controls.Add(titleLabel);
        Controls.Add(subtitleLabel);
        Controls.Add(toggle);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        const int left = 18;
        const int right = 18;
        const int toggleGap = 22;
        var textWidth = Math.Max(40, ClientSize.Width - left - right - toggle.Width - toggleGap);
        titleLabel.Bounds = new Rectangle(left, 13, textWidth, 29);
        subtitleLabel.Bounds = new Rectangle(left, 43, textWidth, 26);
        toggle.Location = new Point(ClientSize.Width - right - toggle.Width, Math.Max(0, (ClientSize.Height - toggle.Height) / 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = RoundedCardPath(bounds, 14);
        using var fill = new SolidBrush(Color.FromArgb(44, 19, 39, 50));
        using var border = new Pen(Color.FromArgb(34, 255, 255, 255));
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
        using var trackBrush = new SolidBrush(Checked ? Color.FromArgb(36, 132, 246) : Color.FromArgb(83, 98, 106));
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
