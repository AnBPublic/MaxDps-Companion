using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WColor = Windows.UI.Color;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D read-only suggested-vs-cast audit grid (WinUI port of the old
/// Ui/CastAuditView.cs): renders the output of <see cref="CastAudit"/> — one
/// row per policy plan head, matched against the recorded send. It never edits
/// telemetry and never feeds the engine; a missing cast is informational (the
/// per-slot gates may have held it), not an error.
/// </summary>
internal sealed class CastAuditView : UserControl
{
    private const int MaxVisibleRows = 14;

    private readonly TextBlock _summary = new()
    {
        FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _empty = new()
    {
        Text = "Export a telemetry session with [Telemetry] Enabled=1 to audit suggestions.",
        FontSize = ControlTheme.Dip(DesignTokens.BodySize),
        Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private readonly StackPanel _rows = new();

    private CastAuditReport _report = CastAuditReport.Empty;

    public CastAuditView()
    {
        var header = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddHeader(header, "SLOT", 0);
        AddHeader(header, "SPELL", 1);
        AddHeader(header, "REASON", 2);
        AddHeader(header, "STATUS", 3);

        var stack = new VertStack { Gap = 2 };
        stack.Children.Add(_summary);
        stack.Children.Add(_empty);
        stack.Children.Add(header);
        stack.Children.Add(_rows);
        Content = stack;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(this, "Suggested vs cast audit");
        Refresh();
    }

    /// <summary>The loaded report (Empty until <see cref="Load"/>).</summary>
    public CastAuditReport Report => _report;

    /// <summary>Replaces the grid contents (old Load :33-38).</summary>
    public void Load(CastAuditReport report)
    {
        _report = report;
        Refresh();
    }

    private static void AddHeader(Grid grid, string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = ControlTheme.Dip(DesignTokens.MicroSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
        };
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private void Refresh()
    {
        _summary.Text = _report.Suggestions == 0
            ? "No plan-head suggestions recorded."
            : $"{_report.Casts}/{_report.Suggestions} suggestions cast ({_report.HitRate:P0}); {_report.Misses} held by gates.";
        _rows.Children.Clear();
        _empty.Visibility = _report.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var count = Math.Min(_report.Rows.Count, MaxVisibleRows);
        for (var i = 0; i < count; i++)
        {
            var row = _report.Rows[i];
            var tone = row.Cast ? StatusTone.Success : StatusTone.Muted;
            var color = row.Cast ? ControlTheme.TextPrimary : ControlTheme.TextMuted;
            var grid = new Grid
            {
                ColumnSpacing = 8,
                MinHeight = 24,
                Padding = new Thickness(6, 2, 6, 2),
                Background = i % 2 == 1
                    ? ControlTheme.Brush(ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.Background, 0.03f))
                    : null,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var slot = Cell(row.Slot.ToString(), color, bold: true);
            var spell = Cell(row.SpellId > 0 ? row.SpellId.ToString() : "-", ControlTheme.TextSecondary);
            var reason = Cell(row.Reason, ControlTheme.TextSecondary);
            reason.TextTrimming = TextTrimming.CharacterEllipsis;
            var status = Cell($"{ControlTheme.GlyphFor(tone)}  {row.Status}", ControlTheme.StatusColor(tone));
            AutomationProperties.SetName(grid, $"{row.Slot} {row.SpellId}: {row.Status}, {row.Reason}");
            Grid.SetColumn(slot, 0);
            Grid.SetColumn(spell, 1);
            Grid.SetColumn(reason, 2);
            Grid.SetColumn(status, 3);
            grid.Children.Add(slot);
            grid.Children.Add(spell);
            grid.Children.Add(reason);
            grid.Children.Add(status);
            _rows.Children.Add(grid);
        }
    }

    private static TextBlock Cell(string text, WColor color, bool bold = false) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(DesignTokens.BodySize),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
        Foreground = ControlTheme.Brush(color),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
}
