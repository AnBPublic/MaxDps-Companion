using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;

namespace MaxDpsCompanion;

/// <summary>One bridge-strip cell model (bound by "StripCellTemplate" in Ui/Theme.xaml).</summary>
public sealed class StripCell : INotifyPropertyChanged
{
    private SolidColorBrush _fill = new(Microsoft.UI.Colors.Transparent);
    private bool _showTick;

    public SolidColorBrush Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Fill)));
        }
    }

    public bool ShowTick
    {
        get => _showTick;
        set
        {
            _showTick = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTick)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TickVisibility)));
        }
    }

    /// <summary>The first cell carries the brass magic tick under it.</summary>
    public Visibility TickVisibility => _showTick ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// P2-B WinUI port of the decoded bridge strip (old UiControls.cs StripView
/// :862-929). The plan's shape: ItemsRepeater + UniformGridLayout over 51
/// cells (PixelProtocol.CellCountExt4, the v5+Ext4 wire length) instead of one
/// owner-drawn row. Same cell-state update API: assign the raw hex
/// <see cref="Sample"/> (space-separated RRGGBB tokens); unparseable cells draw
/// as empty keyline outlines, cell 0 shows the brass tick when valid.
/// </summary>
public sealed class StripView : UserControl
{
    private const double CellWidth = 26;
    private const double CellHeight = 22; // 18 cell + 4 room for the magic tick

    private readonly StripCell[] _cells;
    private string _sample = "";

    public StripView()
    {
        _cells = new StripCell[PixelProtocol.CellCountExt4];
        for (var i = 0; i < _cells.Length; i++) _cells[i] = new StripCell();

        var repeater = new ItemsRepeater
        {
            ItemsSource = _cells,
            Layout = new UniformGridLayout
            {
                MinItemWidth = CellWidth,
                MinItemHeight = CellHeight,
                MinColumnSpacing = 4,
                MinRowSpacing = 4,
                ItemsStretch = UniformGridLayoutItemsStretch.None,
                Orientation = Orientation.Horizontal,
            },
            Margin = new Thickness(0, 2, 0, 2),
        };
        if (FindCellTemplate() is { } template) repeater.ItemTemplate = template;

        Content = repeater;
        AutomationProperties.SetName(this, "Bridge strip");
        ApplySample();
    }

    /// <summary>Number of rendered cells (51, the v5 Ext4 length).</summary>
    public int CellCount => _cells.Length;

    /// <summary>Raw hex sample: space-separated 6-char RRGGBB tokens.</summary>
    public string Sample
    {
        get => _sample;
        set
        {
            var next = value ?? "";
            if (string.Equals(_sample, next, StringComparison.Ordinal)) return;
            _sample = next;
            ApplySample();
        }
    }

    private static DataTemplate? FindCellTemplate()
    {
        try
        {
            return Application.Current?.Resources?["StripCellTemplate"] as DataTemplate;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ApplySample()
    {
        var tokens = _sample.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < _cells.Length; i++)
        {
            var color = default(WColor);
            var valid = i < tokens.Length && TryCell(tokens[i], out color);
            _cells[i].Fill = valid
                ? ControlTheme.Brush(color)
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _cells[i].ShowTick = valid && i == 0;
        }
    }

    private static bool TryCell(string token, out WColor color)
    {
        color = default;
        if (token.Length != 6) return false;
        try
        {
            var rgb = Convert.ToInt32(token, 16);
            color = WColor.FromArgb(0xFF, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
