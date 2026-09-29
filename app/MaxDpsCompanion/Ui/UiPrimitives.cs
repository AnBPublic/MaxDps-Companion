using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

/// <summary>
/// Owner-drawn UI primitives for the v2.8 shell ("Ethereal Glass × brass").
/// Every primitive is measured (no fixed literals that clip at high DPI), every
/// interactive primitive is TabStop with an AccessibleName and a visible focus
/// ring, and status is never carried by colour alone — a glyph or word
/// accompanies every tone.
/// </summary>
internal static class Ui
{
    /// <summary>Rounded rectangle path used by cards, pills and tiles.</summary>
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var rad = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        var d = rad * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Label(string text, float size = DesignTokens.BodySize, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = DesignTokens.Type(size, style),
            ForeColor = DesignTokens.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, DesignTokens.SpaceXs),
        };
    }

    public static Label Caption(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = DesignTokens.Type(DesignTokens.BodySize),
            ForeColor = DesignTokens.TextSecondary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, DesignTokens.SpaceXs),
        };
    }

    /// <summary>Caption + control row (caption left, control fills). Measured height.</summary>
    public static Control FieldRow(string caption, Control control, int labelWidth = 190)
        => new FieldRowPanel(caption, control, labelWidth);

    public static Control Divider()
    {
        return new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = DesignTokens.Border,
            Margin = new Padding(0, DesignTokens.SpaceS, 0, DesignTokens.SpaceS),
        };
    }

    /// <summary>Measure a single line and return whether it fits in <paramref name="width"/>.</summary>
    public static bool Fits(string? text, Font font, int width)
        => TextRenderer.MeasureText(text ?? "", font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width <= width;

    /// <summary>
    /// Draw uppercase "eyebrow" text with letter tracking (GDI+ has no letter
    /// spacing; draw char by char). Tracking is 0.18em of the font size.
    /// </summary>
    public static void DrawTracked(Graphics g, string text, Font font, Point origin, Color color)
    {
        var tracking = Math.Max(1f, font.Size * 0.18f);
        var x = (float)origin.X;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            TextRenderer.DrawText(g, s, font, new Point((int)x, origin.Y), color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            x += TextRenderer.MeasureText(g, s, font, Size.Empty, TextFormatFlags.NoPrefix).Width + tracking;
        }
    }

    /// <summary>Width the tracked string occupies (matches <see cref="DrawTracked"/>).</summary>
    public static int TrackedWidth(Graphics g, string text, Font font)
    {
        var tracking = Math.Max(1f, font.Size * 0.18f);
        var width = 0f;
        foreach (var ch in text)
            width += TextRenderer.MeasureText(g, ch.ToString(), font, Size.Empty, TextFormatFlags.NoPrefix).Width + tracking;
        return (int)Math.Ceiling(width);
    }

    /// <summary>Stable hover/press easing: cubic-bezier(0.32, 0.72, 0, 1) evaluated at t.</summary>
    public static float EaseOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        // Newton-free approximation of the CSS cubic-bezier(0.32,0.72,0,1):
        // fast start, soft landing — used for indicator slides and hovers.
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }
}

/// <summary>
/// Base for owner-drawn interactive controls. v2.8.1: the base
/// <see cref="Control"/> Click path (StandardClick + WmMouseUp) proved
/// unreliable — the validation-cancelled path suppressed Click entirely on
/// some controls (the mouse-dead rail/toggle class) — so these controls own
/// the press/release gesture explicitly. Enter/Space is raised by each
/// subclass through <see cref="Control.OnClick"/> as before.
/// </summary>
internal abstract class UiClickable : Control
{
    private bool _pressed;
    private bool _hover;

    /// <summary>True while the left button is held down on this control.</summary>
    protected bool Pressed
    {
        get => _pressed;
        private set
        {
            if (_pressed == value) return;
            _pressed = value;
            OnPressedChanged();
            Invalidate();
        }
    }

    /// <summary>True while the pointer is over the control.</summary>
    protected bool Hovered
    {
        get => _hover;
        private set
        {
            if (_hover == value) return;
            _hover = value;
            OnHoverChanged();
            Invalidate();
        }
    }

    protected UiClickable()
    {
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    protected virtual void OnPressedChanged() { }
    protected virtual void OnHoverChanged() { }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left) Pressed = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var wasPressed = Pressed;
        Pressed = false;
        if (wasPressed && Enabled && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            OnClick(EventArgs.Empty);
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Hovered = true;
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        Hovered = false;
        Pressed = false;
        base.OnMouseLeave(e);
    }
}

/// <summary>Page heading: display title + optional muted subtitle. Non-interactive.</summary>
internal sealed class PageHeader : Control
{
    private string _subtitle = "";

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Invalidate(); }
    }

    public PageHeader(string title, string subtitle = "")
    {
        Text = title;
        _subtitle = subtitle;
        Dock = DockStyle.Top;
        Height = 72;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = title;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        TextRenderer.DrawText(e.Graphics, Text, DesignTokens.Display,
            new Rectangle(0, 10, Width, 30), DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (_subtitle.Length > 0)
            TextRenderer.DrawText(e.Graphics, _subtitle, DesignTokens.Type(DesignTokens.BodySize),
                new Rectangle(0, 42, Width, 22), DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Double-bezel glass card (Doppelrand): an outer shell (blended lighter
/// surface + hairline ring) holds an inner core (darker, concentric radius,
/// inner top highlight). The body is a measured <see cref="VertStack"/>; the
/// card height is always derived from its content — never a literal.
/// </summary>
internal sealed class GlassCard : Panel, IUiContentHost, IUiMeasured
{
    private const int ShellInset = 5;
    private const int CoreInset = 13;
    private const int HeaderHeight = 68;

    public string Title { get; }
    public string Eyebrow { get; }

    /// <summary>Measured content stack: add rows/primitives here.</summary>
    public VertStack Body { get; } = new() { Gap = DesignTokens.SpaceM };

    Control IUiContentHost.ContentBody => Body;
    bool IUiContentHost.AllowEmptyContent => false;

    public GlassCard(string title, string eyebrow = "")
    {
        Title = title;
        Eyebrow = eyebrow;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = title;
        Controls.Add(Body);
    }

    /// <summary>Adds a content control to the card body.</summary>
    public T Add<T>(T control) where T : Control
    {
        control.Margin = Padding.Empty;
        Body.Controls.Add(control);
        return control;
    }

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(60, width - 2 * (ShellInset + CoreInset));
        var bodyHeight = Body.MeasuredHeight(inner);
        return ShellInset * 2 + HeaderHeight + bodyHeight + CoreInset;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var inner = Math.Max(60, ClientSize.Width - 2 * (ShellInset + CoreInset));
        var bodyHeight = Body.MeasuredHeight(inner);
        Body.Bounds = new Rectangle(ShellInset + CoreInset, ShellInset + HeaderHeight, inner, bodyHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var w = Math.Max(1, Width - 1);
        var h = Math.Max(1, Height - 1);

        // Outer shell: lighter blended surface with a hairline ring.
        var shell = new Rectangle(0, 0, w, h);
        using (var shellPath = Ui.Rounded(shell, DesignTokens.RadiusOuter))
        {
            using var shellFill = new SolidBrush(DesignTokens.Blend(Color.White, DesignTokens.Background, 0.045f));
            e.Graphics.FillPath(shellFill, shellPath);
            using var ring = new Pen(DesignTokens.Hairline, 1F);
            e.Graphics.DrawPath(ring, shellPath);
        }

        // Inner core: darker surface, concentric radius, top highlight.
        var core = new Rectangle(ShellInset, ShellInset, Math.Max(1, w - ShellInset * 2), Math.Max(1, h - ShellInset * 2));
        using (var corePath = Ui.Rounded(core, DesignTokens.RadiusInner))
        {
            using var coreFill = new SolidBrush(DesignTokens.Surface);
            e.Graphics.FillPath(coreFill, corePath);
            var highlight = new Rectangle(core.X + DesignTokens.RadiusInner / 2, core.Y + 1,
                Math.Max(1, core.Width - DesignTokens.RadiusInner), 1);
            e.Graphics.SetClip(corePath);
            using var hl = new SolidBrush(DesignTokens.InnerHighlight);
            e.Graphics.FillRectangle(hl, highlight);
            e.Graphics.ResetClip();
        }

        // Header: eyebrow (tracked) + title + hairline rule, comfortably above
        // the body so the title can never collide with the first row.
        var textLeft = ShellInset + CoreInset;
        var textRight = Math.Max(textLeft + 20, Width - textLeft);
        using var eyebrowFont = DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold);
        var eyebrow = string.IsNullOrEmpty(Eyebrow) ? Title.ToUpperInvariant() : Eyebrow.ToUpperInvariant();
        if (!string.IsNullOrEmpty(eyebrow))
            Ui.DrawTracked(e.Graphics, eyebrow, eyebrowFont, new Point(textLeft, ShellInset + CoreInset + 4), DesignTokens.Accent);
        TextRenderer.DrawText(e.Graphics, Title, DesignTokens.Section,
            new Rectangle(textLeft, ShellInset + CoreInset + 20, Math.Max(10, textRight - textLeft), 24), DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var ruleY = ShellInset + HeaderHeight - 1;
        using var rule = new Pen(DesignTokens.Hairline, 1F);
        e.Graphics.DrawLine(rule, textLeft, ruleY, Width - textLeft, ruleY);
    }
}

/// <summary>
/// Caption + value row drawn as one measured primitive (no nested panels).
/// The v2.7.0 Home cards nested four levels of transparent panels per row and
/// collapsed; this has none.
/// </summary>
internal sealed class KvRow : Control, IUiMeasured
{
    private string _value = "-";

    public string Caption { get; }
    public StatusTone Tone { get; set; } = StatusTone.Neutral;
    public bool Emphasize { get; set; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
            Parent?.PerformLayout();
        }
    }

    public KvRow(string caption, string value = "-")
    {
        Caption = caption;
        _value = value;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = caption;
    }

    /// <summary>Row height: caption + value with a breathable gap (v2.8.1 spacing).</summary>
    public int MeasuredHeight(int width) => 56;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var captionFont = DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold);
        Ui.DrawTracked(e.Graphics, Caption.ToUpperInvariant(), captionFont,
            new Point(0, 4), DesignTokens.TextMuted);
        var valueFont = DesignTokens.Type(Emphasize ? DesignTokens.SectionSize : DesignTokens.BodySize,
            Emphasize ? FontStyle.Bold : FontStyle.Regular);
        var color = Tone == StatusTone.Neutral ? DesignTokens.TextPrimary : DesignTokens.StatusColor(Tone);
        TextRenderer.DrawText(e.Graphics, _value, valueFont,
            new Rectangle(0, 24, Math.Max(10, Width), Math.Max(18, Height - 24)), color,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Caption + interactive control on one measured row.</summary>
internal sealed class FieldRowPanel : Panel, IUiMeasured
{
    public string Caption { get; }
    public Control Field { get; }
    public int LabelWidth { get; set; } = 190;

    public FieldRowPanel(string caption, Control field, int labelWidth = 190)
    {
        Caption = caption;
        Field = field;
        LabelWidth = labelWidth;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Controls.Add(field);
    }

    public int MeasuredHeight(int width)
    {
        var fieldHeight = UiMeasure.Height(Field, Math.Max(60, width - LabelWidth - DesignTokens.SpaceM));
        using var captionFont = DesignTokens.Type(DesignTokens.BodySize);
        var captionHeight = TextRenderer.MeasureText(Caption, captionFont, new Size(Math.Max(20, LabelWidth), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        return Math.Max(fieldHeight, captionHeight) + DesignTokens.SpaceXs * 2;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var fieldWidth = Math.Max(60, ClientSize.Width - LabelWidth - DesignTokens.SpaceM);
        var fieldHeight = UiMeasure.Height(Field, fieldWidth);
        Field.Bounds = new Rectangle(LabelWidth + DesignTokens.SpaceM,
            Math.Max(0, (ClientSize.Height - fieldHeight) / 2), fieldWidth, fieldHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var font = DesignTokens.Type(DesignTokens.BodySize);
        TextRenderer.DrawText(e.Graphics, Caption, font,
            new Rectangle(0, 0, LabelWidth, Height), DesignTokens.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Metric tile: big value + label + tone; optionally clickable to drill in.</summary>
internal sealed class MetricTile : UiClickable, IUiTextFit, IUiMeasured
{
    private float _hoverT;

    public string Value { get; set; } = "0";
    public string Label { get; set; } = "";
    public StatusTone Tone { get; set; } = StatusTone.Neutral;
    public bool Clickable { get; set; }

    /// <summary>Smoke check (§47): the tile must fit both its value and its label.</summary>
    public int RequiredTextWidth
    {
        get
        {
            var value = TextRenderer.MeasureText(Value, DesignTokens.Type(20F, FontStyle.Bold)).Width;
            var label = TextRenderer.MeasureText(Label, DesignTokens.Type(DesignTokens.MetaSize)).Width;
            return Math.Max(value, label) + 34;
        }
    }

    public string FittedText => Label;

    public MetricTile(string label, string value, StatusTone tone = StatusTone.Neutral, bool clickable = false)
    {
        Label = label;
        Value = value;
        Tone = tone;
        Clickable = clickable;
        AutoSize = true;
        Height = 68;
        Width = 150;
        Margin = new Padding(0, 0, DesignTokens.SpaceS, DesignTokens.SpaceS);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        if (clickable) Cursor = Cursors.Hand;
        AccessibleRole = clickable ? AccessibleRole.PushButton : AccessibleRole.StaticText;
        AccessibleName = $"{label}: {value}";
        TabStop = clickable;
    }

    /// <summary>Auto-sized so a long label can never clip (the smoke test enforces this).</summary>
    public override Size GetPreferredSize(Size proposedSize)
        => new(Math.Max(150, RequiredTextWidth), 68);

    public int MeasuredHeight(int width) => 68;

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Clickable && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _hoverT = Math.Clamp(_hoverT + (Hovered && Clickable ? 0.2f : -0.2f), 0f, 1f);
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var path = Ui.Rounded(bounds, DesignTokens.RadiusTile);
        var surface = DesignTokens.Blend(Color.White, DesignTokens.Surface, 0.02f + 0.04f * _hoverT);
        using var fill = new SolidBrush(surface);
        using var border = new Pen(Focused && Clickable ? DesignTokens.Accent : DesignTokens.Hairline, Focused ? 2F : 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        // Tone is a glyph strip, never the only signal (AccessibleName carries it too).
        using var tone = new SolidBrush(DesignTokens.StatusColor(Tone));
        e.Graphics.FillRectangle(tone, 12, 14, 3, 22);

        TextRenderer.DrawText(e.Graphics, Value, DesignTokens.Type(20F, FontStyle.Bold),
            new Rectangle(24, 10, Width - 34, 30), DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, Label, DesignTokens.Type(DesignTokens.MetaSize),
            new Rectangle(24, 42, Width - 34, 18), DesignTokens.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Status pill: tone glyph + word. Colour plus text, always. Auto-sized.</summary>
internal sealed class StatusPill : Control, IUiTextFit
{
    public StatusTone Tone { get; set; } = StatusTone.Neutral;

    /// <summary>Smoke check (§47): the pill must fit its glyph + word.</summary>
    public int RequiredTextWidth => GetPreferredSize(Size.Empty).Width;

    public string FittedText => Text ?? "";

    public StatusPill(string text, StatusTone tone = StatusTone.Neutral)
    {
        Text = text;
        Tone = tone;
        AutoSize = true;
        Height = 28;
        Margin = new Padding(0, 0, DesignTokens.SpaceS, DesignTokens.SpaceXs);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = text;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        using var font = DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold);
        var measured = TextRenderer.MeasureText($"{GlyphFor(Tone)}  {Text}", font);
        return new Size(measured.Width + 26, Height);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        PerformLayout();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = DesignTokens.StatusColor(Tone);
        var glyph = GlyphFor(Tone);
        var text = $"{glyph}  {Text}";
        var bounds = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 2));
        using var path = Ui.Rounded(bounds, (Height - 2) / 2);
        using var fill = new SolidBrush(DesignTokens.Tint(color, DesignTokens.Surface));
        using var border = new Pen(Color.FromArgb(130, color), 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, text, DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold),
            bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private static string GlyphFor(StatusTone tone) => tone switch
    {
        StatusTone.Success => "\u2713", // check
        StatusTone.Warning => "!",
        StatusTone.Danger => "\u00D7",  // cross
        StatusTone.Info => "i",
        StatusTone.Muted => "-",
        _ => "\u2022",
    };
}

/// <summary>Filter chip: toggled state + label. Keyboard Space/Enter activates.</summary>
internal sealed class FilterChip : UiClickable, IUiTextFit
{
    private bool _selected;

    /// <summary>Smoke check (§47): the chip must fit its label.</summary>
    public int RequiredTextWidth => TextRenderer.MeasureText(Text, Font).Width + 26;

    public string FittedText => Text ?? "";

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public FilterChip(string text, bool selected = false)
    {
        Text = text;
        _selected = selected;
        AutoSize = true;
        Height = 28;
        Padding = new Padding(12, 0, 12, 0);
        Margin = new Padding(0, 0, 6, 6);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = text;
        TabStop = true;
        Font = DesignTokens.Type(DesignTokens.BodySize, selected ? FontStyle.Bold : FontStyle.Regular);
    }

    public override Size GetPreferredSize(Size proposedSize)
        => new(TextRenderer.MeasureText(Text, Font).Width + 26, 28);

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 2));
        using var path = Ui.Rounded(bounds, (Height - 2) / 2);
        var fillColor = _selected ? DesignTokens.Tint(DesignTokens.Accent, DesignTokens.Surface)
            : Hovered ? DesignTokens.SurfaceElevated : DesignTokens.Surface;
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(Focused || _selected ? DesignTokens.Accent : DesignTokens.Hairline, Focused ? 2F : 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var textColor = _selected ? DesignTokens.AccentSoft : DesignTokens.TextSecondary;
        TextRenderer.DrawText(e.Graphics, Text, DesignTokens.Type(DesignTokens.BodySize, _selected ? FontStyle.Bold : FontStyle.Regular),
            bounds, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Transient non-blocking status toast for the shell.</summary>
internal sealed class ToastHost : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3200 };
    private string _message = "";
    private StatusTone _tone = StatusTone.Info;

    public ToastHost()
    {
        Height = 38;
        Dock = DockStyle.Bottom;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Status message";
        _timer.Tick += (_, _) => { _timer.Stop(); _message = ""; Invalidate(); };
    }

    public void Show(string message, StatusTone tone = StatusTone.Info)
    {
        _message = message;
        _tone = tone;
        AccessibleName = message;
        _timer.Stop();
        _timer.Start();
        Invalidate();
    }

    public string CurrentMessage => _message;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_message.Length == 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = DesignTokens.StatusColor(_tone);
        var bounds = new Rectangle(12, 5, Math.Max(10, Width - 24), Height - 10);
        using var path = Ui.Rounded(bounds, 8);
        using var fill = new SolidBrush(DesignTokens.Blend(color, DesignTokens.Background, 0.12f));
        using var border = new Pen(Color.FromArgb(150, color), 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, _message, DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold),
            new Rectangle(26, 5, Math.Max(10, Width - 50), Height - 10), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
