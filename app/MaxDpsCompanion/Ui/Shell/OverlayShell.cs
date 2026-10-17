using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C in-window scrim overlay (docs/plans/2026-10-11-winui3-v4-migration.md
/// §2/§7-P2-C; WinForms BuildUnifiedPopup :1549-1645 parity): 60% black scrim +
/// centered rounded card with a title header ("Back" button) and a
/// <see cref="SegmentedTabs"/> host. Scrim click and Esc (wired by
/// <see cref="ShellWindow"/>) close it. Tab content is owned by the caller and
/// built lazily on first open (MainForm.ShowAdvanced :1659-1664).
/// </summary>
internal sealed class OverlayShell : UserControl
{
    /// <summary>M-route geometry: the card fills the scrim minus a 24 DIP edge, centered.</summary>
    private const double Edge = 24;

    /// <summary>60% black, per the migration plan's scrim contract (§7-P2-C).</summary>
    private static readonly WColor ScrimFill = WColor.FromArgb(0x99, 0x00, 0x00, 0x00);

    private readonly RoundedCard _card;
    private readonly double _maxWidth;

    /// <summary>Tab host: the caller adds pages through <see cref="AddTab"/>.</summary>
    public SegmentedTabs Tabs { get; } = new();

    public OverlayShell(string title, double maxWidth, Action onClose)
    {
        _maxWidth = maxWidth;
        Visibility = Visibility.Collapsed;
        IsTabStop = true;

        var scrim = new ShapeRect { Fill = new SolidColorBrush(ScrimFill) };
        scrim.Tapped += (_, _) => onClose();

        var header = new Grid { Height = 44, Padding = new Thickness(10, 0, 8, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleLabel = new TextBlock
        {
            Text = title,
            FontSize = ControlTheme.Dip(11.5f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var back = new ChamferButton
        {
            Content = "Back",
            Role = ButtonRole.Ghost,
            Height = 34,
            MinHeight = 32,
            Padding = new Thickness(14, 0, 14, 0),
            TrailingGlyph = null,
            FontSize = ControlTheme.Dip(9.5f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        };
        back.Click += (_, _) => onClose();
        Grid.SetColumn(titleLabel, 0);
        Grid.SetColumn(back, 1);
        header.Children.Add(titleLabel);
        header.Children.Add(back);
        AutomationProperties.SetName(back, "Back");
        AutomationProperties.SetName(this, title);

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(Tabs, 1);
        shell.Children.Add(header);
        shell.Children.Add(Tabs);

        _card = new RoundedCard
        {
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _card.Add(shell);

        var root = new Grid();
        root.Children.Add(scrim);
        root.Children.Add(_card);
        Content = root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        SizeChanged += (_, _) => Center();
    }

    /// <summary>Adds one tab page (title + content), same as MainForm.AddTab :1530-1540.</summary>
    public void AddTab(string title, UIElement content)
    {
        var page = new SegmentedTabPage(title) { Content = content };
        Tabs.TabPages.Add(page);
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open()
    {
        Visibility = Visibility.Visible;
        Center();
    }

    public void Close() => Visibility = Visibility.Collapsed;

    /// <summary>Scrim-24 x scrim-24, min 360x280, centered (old Center :1623-1641).</summary>
    private void Center()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _card.Width = Math.Min(_maxWidth, Math.Max(360, ActualWidth - Edge));
        _card.Height = Math.Max(280, ActualHeight - Edge);
    }
}
