using System.Drawing.Drawing2D;

namespace MaxDpsCompanion;

/// <summary>
/// Read-only suggested-vs-cast audit grid (Stream 3 §3.4). Renders the output of
/// <see cref="CastAudit"/> — one row per policy plan head, matched against the
/// recorded send. It never edits telemetry and never feeds the engine; it is a
/// review surface over an exported JSONL file. A missing cast is informational
/// (the per-slot gates may have held it), not an error.
/// </summary>
internal sealed class CastAuditView : Control, IUiMeasured
{
    private const int HeaderHeight = 30;
    private const int RowHeight = 26;
    private const int MaxVisibleRows = 14;

    private CastAuditReport _report = CastAuditReport.Empty;

    public CastAuditView()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Table;
        AccessibleName = "Suggested vs cast audit";
    }

    /// <summary>The loaded report (Empty until <see cref="Load"/>).</summary>
    public CastAuditReport Report => _report;

    /// <summary>Replaces the grid contents and re-measures.</summary>
    public void Load(CastAuditReport report)
    {
        _report = report;
        Parent?.PerformLayout();
        Invalidate();
    }

    public int MeasuredHeight(int width) =>
        HeaderHeight + RowHeight * Math.Min(_report.Rows.Count, MaxVisibleRows) + DesignTokens.SpaceS;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var summary = _report.Suggestions == 0
            ? "No plan-head suggestions recorded."
            : $"{_report.Casts}/{_report.Suggestions} suggestions cast ({_report.HitRate:P0}); {_report.Misses} held by gates.";
        TextRenderer.DrawText(e.Graphics, summary, DesignTokens.Type(DesignTokens.MetaSize, FontStyle.Bold),
            new Rectangle(0, 0, Math.Max(10, Width), 18), DesignTokens.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        if (_report.Rows.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "Export a telemetry session with [Telemetry] Enabled=1 to audit suggestions.",
                DesignTokens.Type(DesignTokens.BodySize),
                new Rectangle(0, HeaderHeight, Math.Max(10, Width), 24), DesignTokens.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            return;
        }

        var columnSlot = DesignTokens.SpaceS;
        var columnSpell = DesignTokens.SpaceS + 70;
        var columnStatus = Width - 220;
        var y = HeaderHeight - RowHeight + 4;
        for (var i = 0; i < _report.Rows.Count && i < MaxVisibleRows; i++)
        {
            var row = _report.Rows[i];
            if (i % 2 == 1)
            {
                using var zebra = new SolidBrush(DesignTokens.Blend(Color.White, DesignTokens.Background, 0.03f));
                e.Graphics.FillRectangle(zebra, 0, y, Math.Max(1, Width), RowHeight - 2);
            }
            var tone = row.Cast ? StatusTone.Success : StatusTone.Muted;
            var color = row.Cast ? DesignTokens.TextPrimary : DesignTokens.TextMuted;
            TextRenderer.DrawText(e.Graphics, row.Slot.ToString(), DesignTokens.Type(DesignTokens.BodySize, FontStyle.Bold),
                new Rectangle(columnSlot, y, 68, RowHeight - 2), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, row.SpellId > 0 ? row.SpellId.ToString() : "-", DesignTokens.Type(DesignTokens.BodySize),
                new Rectangle(columnSpell, y, 70, RowHeight - 2), DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, row.Reason, DesignTokens.Type(DesignTokens.BodySize),
                new Rectangle(columnSpell + 70, y, Math.Max(10, columnStatus - columnSpell - 70), RowHeight - 2), DesignTokens.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, $"{Glyph(tone)}  {row.Status}", DesignTokens.Type(DesignTokens.BodySize),
                new Rectangle(columnStatus, y, Math.Max(10, Width - columnStatus), RowHeight - 2), DesignTokens.StatusColor(tone),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            y += RowHeight;
        }
    }

    private static string Glyph(StatusTone tone) => tone switch
    {
        StatusTone.Success => "\u2713",
        StatusTone.Danger => "\u00D7",
        _ => "\u2022",
    };
}
