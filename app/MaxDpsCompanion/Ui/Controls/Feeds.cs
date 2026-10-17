using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WColor = Windows.UI.Color;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the transient status toast (old UiPrimitives.cs ToastHost
/// :651-703): a bottom-docked strip that clears itself after 3.2 s. The frame
/// is a tinted capsule in the tone colour (never colour-alone: the message is
/// the signal). Uses a DispatcherQueueTimer like every other UI timer in the
/// migration (docs/plans/2026-10-11-winui3-v4-migration.md §4).
/// </summary>
internal sealed class ToastHost : UserControl
{
    private readonly Border _frame = new();
    private readonly TextBlock _text = new()
    {
        FontSize = ControlTheme.Dip(DesignTokens.BodySize),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;

    public ToastHost()
    {
        Height = 38;
        VerticalAlignment = VerticalAlignment.Bottom;
        _frame.CornerRadius = new CornerRadius(8);
        _frame.Margin = new Thickness(12, 5, 12, 5);
        _frame.Padding = new Thickness(14, 0, 14, 0);
        _frame.BorderThickness = new Thickness(1);
        _frame.Visibility = Visibility.Collapsed;
        _frame.Child = _text;
        Content = _frame;
        AutomationProperties.SetName(this, "Status message");

        if (DispatcherQueue is { } queue)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(3200);
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => Clear();
        }
    }

    public string CurrentMessage { get; private set; } = "";

    public void Show(string message, StatusTone tone = StatusTone.Info)
    {
        CurrentMessage = message;
        var color = ControlTheme.StatusColor(tone);
        _frame.Background = ControlTheme.Brush(ControlTheme.Blend(color, ControlTheme.Background, 0.12f));
        _frame.BorderBrush = ControlTheme.Brush(ControlTheme.WithAlpha(color, 150));
        _text.Foreground = ControlTheme.Brush(color);
        _text.Text = message;
        _frame.Visibility = Visibility.Visible;
        AutomationProperties.SetName(this, message);

        if (_timer is null) return;
        _timer.Stop();
        _timer.Start();
    }

    public void Clear()
    {
        _timer?.Stop();
        CurrentMessage = "";
        _text.Text = "";
        _frame.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(this, "Status message");
    }
}

/// <summary>
/// P2-B WinUI port of the multi-column legend (old UiFoundation.cs LegendGrid
/// :535-575): each entry is a brass/neutral marker square plus its word, laid
/// out in equal columns.
/// </summary>
public sealed class LegendGrid : UserControl
{
    private readonly Grid _grid = new();

    public LegendGrid(params (string Text, WColor Mark)[] items)
    {
        Content = _grid;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SetItems(items);
    }

    /// <summary>Number of equal columns; set before <see cref="SetItems"/> to re-lay out.</summary>
    public int Columns { get; set; } = 2;

    public void SetItems(params (string Text, WColor Mark)[] items)
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();

        var columns = Math.Max(1, Columns);
        var rows = Math.Max(1, (items.Length + columns - 1) / columns);
        for (var c = 0; c < columns; c++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows; r++)
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < items.Length; i++)
        {
            var marker = new ShapeRect
            {
                Width = 6,
                Height = 6,
                Fill = ControlTheme.Brush(items[i].Mark),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            };
            var label = new TextBlock
            {
                Text = items[i].Text,
                FontSize = ControlTheme.Dip(Math.Max(8f, DesignTokens.BodySize - 1f)),
                Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var cell = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 14, 1),
            };
            cell.Children.Add(marker);
            cell.Children.Add(label);
            Grid.SetRow(cell, i / columns);
            Grid.SetColumn(cell, i % columns);
            _grid.Children.Add(cell);
        }

        AutomationProperties.SetName(this, string.Join(". ", Array.ConvertAll(items, item => item.Text)));
    }
}
