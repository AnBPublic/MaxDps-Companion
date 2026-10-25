using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using MediaFontFamily = Microsoft.UI.Xaml.Media.FontFamily;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C WinUI port of the owner-drawn rolling log (old Ui/ConsoleHome.cs
/// RollingLog :247-346): append-only, newest line at the bottom, capacity
/// capped, optional leading "HH:mm:ss" stamp in its own tone, optional mono
/// body and an empty-state line.
///
/// 4.5.1: lines WRAP instead of being clipped with an ellipsis. A stamped line
/// is a two-column grid (fixed stamp column, wrapping body) so continuation
/// lines stay aligned under the message and the reader can see the whole entry
/// at any window width.
///
/// Timer-safety parity: <see cref="Append"/> is called only on state
/// transitions (status message change, spec change, preset apply, one-shot
/// bridge hint), never unconditionally on the 250 ms tick — the same contract
/// the WinForms log kept ("no Add/Remove on the timer path").
/// </summary>
internal sealed class RollingLogView : UserControl
{
    private const double StampGap = 8;

    private readonly ScrollViewer _viewer = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Auto,
        ZoomMode = ZoomMode.Disabled,
    };
    private readonly StackPanel _lines = new() { Spacing = 1 };
    private readonly List<LogLine> _all = new();
    private readonly TextBlock _empty;
    private double _fontSize = ControlTheme.Dip(DesignTokens.MetaSize);
    private bool _emptyVisible = true;

    /// <summary>One rendered entry: the root element plus the blocks that need a font-size pass.</summary>
    private sealed record LogLine(FrameworkElement Root, TextBlock Body, TextBlock? Stamp);

    public RollingLogView()
    {
        _empty = new TextBlock
        {
            FontSize = _fontSize,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
        };
        _lines.Children.Add(_empty);
        _viewer.Content = _lines;
        Content = _viewer;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Log");
    }

    /// <summary>Maximum retained lines (old RollingLog.Capacity: 200).</summary>
    public int Capacity { get; set; } = 200;

    /// <summary>Render the body in the mono family (Exp terminal). Set before appending.</summary>
    public bool UseMono { get; set; }

    /// <summary>Leading stamp tone; null draws the whole line in the line tone.</summary>
    public WColor? StampTone { get; set; }

    /// <summary>Copy drawn in place of the lines while the log is empty.</summary>
    public string? EmptyText
    {
        get => _empty.Text;
        set => _empty.Text = value ?? "";
    }

    public WColor EmptyTone { get; set; } = ControlTheme.TextMuted;

    /// <summary>Retained line count (excludes the empty-state block).</summary>
    public int LineCount => _all.Count;

    /// <summary>Last appended line text, or "" — a light UIA/smoke seam.</summary>
    public string LastLine { get; private set; } = "";

    public void Append(string text, WColor tone)
    {
        TextBlock Body(string value, WColor color)
        {
            var block = new TextBlock
            {
                Text = value,
                FontSize = _fontSize,
                Foreground = ControlTheme.Brush(color),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                VerticalAlignment = VerticalAlignment.Top,
            };
            if (UseMono) block.FontFamily = MonoFamily();
            return block;
        }

        FrameworkElement root;
        TextBlock body;
        TextBlock? stampBlock = null;

        if (StampTone is { } stampTone && TrySplitStamp(text, out var stamp, out var rest))
        {
            stampBlock = new TextBlock
            {
                Text = stamp,
                FontSize = _fontSize,
                Foreground = ControlTheme.Brush(stampTone),
                FontFamily = MonoFamily(),
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, StampGap, 0),
            };
            body = Body(rest, tone);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(stampBlock, 0);
            Grid.SetColumn(body, 1);
            grid.Children.Add(stampBlock);
            grid.Children.Add(body);
            root = grid;
        }
        else
        {
            body = Body(text, tone);
            root = body;
        }

        LastLine = text;
        var line = new LogLine(root, body, stampBlock);
        _all.Add(line);
        _lines.Children.Add(root);
        // Newest line fades in (~150 ms, opacity only) once the tree has it.
        root.Loaded += (_, _) => Motion.Reveal(root);
        while (_all.Count > Math.Max(1, Capacity))
        {
            _lines.Children.RemoveAt(1); // index 0 is the empty-state block
            _all.RemoveAt(0);
        }
        RefreshEmpty();
        ScrollToEnd();
    }

    public void Clear()
    {
        if (_all.Count == 0) return;
        _all.Clear();
        while (_lines.Children.Count > 1) _lines.Children.RemoveAt(1);
        LastLine = "";
        RefreshEmpty();
    }

    /// <summary>Width-tier type step (old RollingLog.ApplyScale :290-292).</summary>
    public void ApplyScale(UiScale scale)
    {
        _fontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 2f));
        _empty.FontSize = _fontSize;
        foreach (var line in _all)
        {
            line.Body.FontSize = _fontSize;
            if (line.Stamp is not null) line.Stamp.FontSize = _fontSize;
        }
    }

    private static MediaFontFamily MonoFamily() => new("Cascadia Mono, Consolas");

    private void RefreshEmpty()
    {
        var show = _all.Count == 0 && !string.IsNullOrEmpty(_empty.Text);
        if (show == _emptyVisible) return;
        _emptyVisible = show;
        _empty.Foreground = ControlTheme.Brush(EmptyTone);
        _empty.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollToEnd()
    {
        try
        {
            _viewer.UpdateLayout();
            _viewer.ChangeView(null, _viewer.ScrollableHeight, null, true);
        }
        catch (Exception)
        {
            // Not laid out yet: the next append retries; the log still holds the lines.
        }
    }

    /// <summary>True when the line starts with a leading "HH:mm:ss " stamp (old :337-345).</summary>
    private static bool TrySplitStamp(string text, out string stamp, out string rest)
    {
        stamp = "";
        rest = text;
        if (text.Length <= 10 || text[2] != ':' || text[5] != ':' || text[8] != ' ') return false;
        stamp = text[..10];
        rest = text[10..];
        return true;
    }
}
