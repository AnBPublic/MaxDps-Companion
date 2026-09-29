using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

/// <summary>
/// Full-width semantic banner (Stream 3 design pass): a tinted rounded row with
/// a tone stripe, an uppercase title and one wrapped detail line. Used for the
/// bridge skew / stale-addon warnings (Warning/Info) and inline errors
/// (Danger) — status is never carried by colour alone (a glyph precedes the
/// title). Measured so it never clips at high DPI.
/// </summary>
internal sealed class SemanticBanner : Control, IUiMeasured
{
    private string _detail = "";

    public StatusTone Tone { get; set; } = StatusTone.Info;

    public string Detail
    {
        get => _detail;
        set
        {
            _detail = value;
            Parent?.PerformLayout();
            Invalidate();
        }
    }

    public SemanticBanner(string title, StatusTone tone = StatusTone.Info, string detail = "")
    {
        Text = title;
        Tone = tone;
        _detail = detail;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = title;
    }

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(40, width - 2 * DesignTokens.SpaceM);
        if (_detail.Length == 0) return 44;
        using var font = DesignTokens.Type(DesignTokens.BodySize);
        var measured = TextRenderer.MeasureText(_detail, font, new Size(inner, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return 24 + measured.Height + DesignTokens.SpaceS;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = DesignTokens.StatusColor(Tone);
        var bounds = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 3));
        using (var path = Ui.Rounded(bounds, DesignTokens.RadiusControl))
        {
            using var fill = new SolidBrush(DesignTokens.Tint(color, DesignTokens.Surface));
            e.Graphics.FillPath(fill, path);
            using var border = new Pen(Color.FromArgb(120, color), 1F);
            e.Graphics.DrawPath(border, path);
            // Tone stripe on the leading edge.
            using var stripe = new SolidBrush(color);
            e.Graphics.FillRectangle(stripe, bounds.X + 1, bounds.Y + 6, 3, Math.Max(1, bounds.Height - 12));
        }

        var glyph = Tone switch
        {
            StatusTone.Success => "\u2713",
            StatusTone.Warning => "!",
            StatusTone.Danger => "\u00D7",
            StatusTone.Info => "i",
            _ => "\u2022",
        };
        var textLeft = DesignTokens.SpaceM;
        TextRenderer.DrawText(e.Graphics, $"{glyph}  {Text}", DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold),
            new Rectangle(textLeft, 8, Math.Max(10, Width - textLeft - DesignTokens.SpaceM), 20), color,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (_detail.Length > 0)
            TextRenderer.DrawText(e.Graphics, _detail, DesignTokens.Type(DesignTokens.BodySize),
                new Rectangle(textLeft, 28, Math.Max(10, Width - textLeft - DesignTokens.SpaceM), Height - 32),
                DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Empty-state block (Stream 3 design pass): a muted centered title + optional
/// hint, used where a surface has no verified content yet (e.g. "no verified
/// DH self-heal"). Deliberately plain so it reads as informational, never as an
/// error.
/// </summary>
internal sealed class EmptyState : Control, IUiMeasured
{
    private string _hint = "";

    public string Hint
    {
        get => _hint;
        set { _hint = value; Parent?.PerformLayout(); Invalidate(); }
    }

    public EmptyState(string title, string hint = "")
    {
        Text = title;
        _hint = hint;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = title;
    }

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(40, width - 2 * DesignTokens.SpaceL);
        if (_hint.Length == 0) return 56;
        using var font = DesignTokens.Type(DesignTokens.BodySize);
        var measured = TextRenderer.MeasureText(_hint, font, new Size(inner, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return 28 + measured.Height + DesignTokens.SpaceL;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 3));
        using (var path = Ui.Rounded(bounds, DesignTokens.RadiusControl))
        {
            using var fill = new SolidBrush(DesignTokens.Blend(DesignTokens.TextMuted, DesignTokens.Surface, 0.08f));
            e.Graphics.FillPath(fill, path);
            using var dashed = new Pen(Color.FromArgb(90, DesignTokens.TextMuted), 1F) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawPath(dashed, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold),
            new Rectangle(DesignTokens.SpaceL, 10, Math.Max(10, Width - 2 * DesignTokens.SpaceL), 20),
            DesignTokens.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (_hint.Length > 0)
            TextRenderer.DrawText(e.Graphics, _hint, DesignTokens.Type(DesignTokens.MetaSize),
                new Rectangle(DesignTokens.SpaceL, 30, Math.Max(10, Width - 2 * DesignTokens.SpaceL), Height - 34),
                DesignTokens.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
