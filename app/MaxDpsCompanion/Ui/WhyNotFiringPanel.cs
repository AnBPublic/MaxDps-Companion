using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

/// <summary>
/// S8 diagnostics: a small owner-drawn panel that explains why a toggle/slot is
/// not firing. It renders <see cref="WhyNotFiring.Explain"/> — toggle state,
/// scheduler verdict and candidate staleness — for the last observed plan head.
/// No game read, no scheduler change; the caller pushes a
/// <see cref="WhyNotFiringFacts"/> snapshot once per UI tick.
/// </summary>
internal sealed class WhyNotFiringPanel : Control
{
    private string _title = "Why isn't this firing?";
    private IReadOnlyList<string> _lines = ["Waiting for the engine to report a verdict."];

    public WhyNotFiringPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        MinimumSize = new Size(0, 96);
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Why isn't this firing";
    }

    public void Update(in WhyNotFiringFacts facts)
    {
        _title = $"Why isn't {facts.Label} firing?";
        _lines = WhyNotFiring.Explain(facts);
        AccessibleName = WhyNotFiring.Summary(facts);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var y = 2;
        using var titleFont = new Font(MainForm.UiFontPublic, 10.5F, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, _title, titleFont,
            new Rectangle(0, y, Math.Max(10, Width), titleFont.Height + 2),
            DesignTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        y += titleFont.Height + 4;

        using var lineFont = new Font(MainForm.UiFontPublic, 9.25F, FontStyle.Regular);
        for (var i = 0; i < _lines.Count && y < Height; i++)
        {
            TextRenderer.DrawText(e.Graphics, "\u00B7 " + _lines[i], lineFont,
                new Rectangle(2, y, Math.Max(10, Width - 4), lineFont.Height + 2),
                DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += lineFont.Height + 1;
        }
    }
}
