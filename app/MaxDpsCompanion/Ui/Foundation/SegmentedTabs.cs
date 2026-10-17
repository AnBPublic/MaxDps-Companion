using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the S5 owner-drawn segmented tab strip (old UiFoundation.cs
/// SegmentedTabs). Stock WinUI TabView is document-style chrome; this keeps the
/// classic underline-strip geometry: a 40 DIP header with measured segments,
/// selected segment underlined in brass, pages swapped below.
/// API parity with the old callsites: TabPages.Add(page), SelectedIndex and
/// SelectedIndexChanged.
/// </summary>
public sealed class SegmentedTabs : UserControl
{
    private const double HeaderHeight = 40;

    private readonly SegmentedTabPageCollection _pages;
    private readonly StackPanel _header = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _content = new();
    private readonly List<Segment> _segments = new();
    private int _selectedIndex = -1;

    public event EventHandler? SelectedIndexChanged;

    public SegmentedTabs()
    {
        _pages = new SegmentedTabPageCollection(this);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerHost = new Grid { Background = ControlTheme.Brush(ControlTheme.Background) };
        headerHost.Children.Add(_header);
        headerHost.Children.Add(new ShapeRect
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = ControlTheme.Brush(ControlTheme.Hairline),
        });
        Grid.SetRow(headerHost, 0);
        root.Children.Add(headerHost);

        Grid.SetRow(_content, 1);
        root.Children.Add(_content);
        Content = root;

        AutomationProperties.SetName(this, "Tabs");
    }

    public SegmentedTabPageCollection TabPages => _pages;

    public SegmentedTabPage? SelectedTab =>
        _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_pages.Count == 0)
            {
                _selectedIndex = -1;
                return;
            }
            var index = Math.Clamp(value, 0, _pages.Count - 1);
            if (index == _selectedIndex) return;
            _selectedIndex = index;
            ApplySelection();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void AddPage(SegmentedTabPage page)
    {
        _pages.Items.Add(page);
        page.Visibility = Visibility.Collapsed;
        _content.Children.Add(page);
        _segments.Add(new Segment(page.Title, _segments.Count, this));
        _header.Children.Add(_segments[^1].Root);
        if (_selectedIndex < 0)
        {
            _selectedIndex = 0;
            ApplySelection();
        }
        else
        {
            RefreshSegmentVisuals();
        }
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == _selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        RefreshSegmentVisuals();
    }

    private void RefreshSegmentVisuals()
    {
        for (var i = 0; i < _segments.Count; i++)
            _segments[i].SetSelected(i == _selectedIndex);
    }

    private void SelectFromSegment(int index)
    {
        if (index >= 0 && index < _pages.Count) SelectedIndex = index;
    }

    /// <summary>One header segment: transparent button over a background/underline pair.</summary>
    private sealed class Segment
    {
        private readonly Border _background = new();
        private readonly TextBlock _text;
        private readonly ShapeRect _underline = new()
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 8, 1),
            Visibility = Visibility.Collapsed,
        };

        public Button Root { get; }

        public Segment(string title, int index, SegmentedTabs owner)
        {
            _text = new TextBlock
            {
                Text = title,
                FontSize = ControlTheme.Dip(DesignTokens.BodySize),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(17, 0, 17, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var grid = new Grid();
            grid.Children.Add(_background);
            grid.Children.Add(_text);
            grid.Children.Add(_underline);

            Root = new Button
            {
                Content = grid,
                Height = HeaderHeight,
                MinWidth = 84,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            ControlTheme.ApplyAppStyle(Root, "UiClickableStyle");
            Root.Click += (_, _) => owner.SelectFromSegment(index);
            AutomationProperties.SetName(Root, title);
        }

        public void SetSelected(bool selected)
        {
            _background.Background = selected
                ? ControlTheme.Brush(ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.Background, 0.05f))
                : null;
            _text.Foreground = ControlTheme.Brush(selected ? ControlTheme.TextPrimary : ControlTheme.TextMuted);
            _underline.Fill = ControlTheme.Brush(ControlTheme.Accent);
            _underline.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>One page hosted by <see cref="SegmentedTabs"/>.</summary>
public sealed class SegmentedTabPage : ContentControl
{
    public string Title { get; }

    public SegmentedTabPage(string title)
    {
        Title = title;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(this, title);
    }
}

/// <summary>Collection facade kept shape-compatible with TabControl.TabPages usage.</summary>
public sealed class SegmentedTabPageCollection : IEnumerable<SegmentedTabPage>
{
    private readonly SegmentedTabs _owner;
    internal readonly List<SegmentedTabPage> Items = new();

    internal SegmentedTabPageCollection(SegmentedTabs owner) => _owner = owner;

    public void Add(SegmentedTabPage page) => _owner.AddPage(page);

    public int Count => Items.Count;

    public SegmentedTabPage this[int index] => Items[index];

    public IEnumerator<SegmentedTabPage> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
}
