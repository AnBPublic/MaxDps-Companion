using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using WPoint = Windows.Foundation.Point;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the PRP gradient ground (old UiControls.cs
/// GradientCanvas :342-369). Two stacked LinearGradientBrushes reproduce the
/// GDI+ 38 deg base and 145 deg overlay; <see cref="FlatColor"/> keeps the Exp
/// flat-fill path. Children are added on top of both gradients.
/// </summary>
public sealed class GradientCanvas : ContentControl
{
    private readonly ShapeRect _base = new();
    private readonly ShapeRect _overlay = new();
    private readonly Grid _root = new();
    private WColor? _flat;

    public GradientCanvas()
    {
        _root.Children.Add(_base);
        _root.Children.Add(_overlay);
        Content = _root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        Rebuild();
    }

    /// <summary>Optional flat fill (Exp theme). Null keeps the classic gradient.</summary>
    public WColor? FlatColor
    {
        get => _flat;
        set
        {
            _flat = value;
            Rebuild();
        }
    }

    public void Add(UIElement child) => _root.Children.Add(child);

    private void Rebuild()
    {
        if (_flat is { } flat)
        {
            _base.Fill = ControlTheme.Brush(flat);
            _overlay.Fill = null;
            _overlay.Visibility = Visibility.Collapsed;
            return;
        }

        _base.Fill = Gradient(38, WColor.FromArgb(0xFF, 15, 56, 76), WColor.FromArgb(0xFF, 48, 47, 19));
        _overlay.Fill = Gradient(145, WColor.FromArgb(0x00, 8, 31, 42), WColor.FromArgb(0x96, 8, 35, 49));
        _overlay.Visibility = Visibility.Visible;
    }

    private static LinearGradientBrush Gradient(double angleDeg, WColor from, WColor to)
    {
        var radians = angleDeg * Math.PI / 180;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians); // screen space: y grows downward, GDI+ parity
        var brush = new LinearGradientBrush
        {
            StartPoint = new WPoint(0.5 - dx / 2, 0.5 - dy / 2),
            EndPoint = new WPoint(0.5 + dx / 2, 0.5 + dy / 2),
        };
        brush.GradientStops.Add(new GradientStop { Color = from, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = to, Offset = 1 });
        return brush;
    }
}

/// <summary>
/// P2-B WinUI port of the PRP rounded card (old UiControls.cs RoundedCard
/// :371-413); v4.0.1 double-bezel glass (design brief §1): outer shell at the
/// card radius with a white/4 wash + white/6 hairline ring and 5 DIP bezel
/// padding; inner core at the concentric radius (outer minus 5) with the card
/// fill and a vertical top-edge highlight border (#16FFFFFF -> #08FFFFFF).
/// The caller's Padding is applied INSIDE the core (the stock ContentControl
/// template ignores Padding, so this keeps the old call sites' copy inset).
/// </summary>
public sealed class RoundedCard : ContentControl
{
    private const double BezelPadding = 5;

    private readonly Border _shell = new();
    private readonly Border _core = new();
    private readonly Grid _content = new();
    private WColor _fillColor = WColor.FromArgb(78, 0xF4, 0xF7, 0xF9);
    private WColor _borderColor = ControlTheme.Ring;

    public RoundedCard()
    {
        _core.Child = _content;
        _shell.Child = _core;

        _shell.Background = ControlTheme.Brush(ControlTheme.BezelWash);
        _shell.BorderBrush = ControlTheme.Brush(ControlTheme.Ring);
        _shell.BorderThickness = new Thickness(1);
        _shell.CornerRadius = new CornerRadius(DesignTokens.RadiusOuter);
        _shell.Padding = new Thickness(BezelPadding);

        _core.Background = ControlTheme.Brush(_fillColor);
        _core.CornerRadius = new CornerRadius(DesignTokens.RadiusOuter - BezelPadding);
        _core.BorderBrush = TopEdgeHighlight();
        _core.BorderThickness = new Thickness(1);

        Content = _shell;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;

        RegisterPropertyChangedCallback(PaddingProperty, (_, _) => _content.Margin = Padding);
    }

    /// <summary>Card fill on the inner core; Exp sets the opaque card token.</summary>
    public WColor FillColor
    {
        get => _fillColor;
        set { _fillColor = value; _core.Background = ControlTheme.Brush(value); }
    }

    /// <summary>Bezel ring colour (default white 6%); never a mid-gray keyline.</summary>
    public WColor BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; _shell.BorderBrush = ControlTheme.Brush(value); }
    }

    /// <summary>Outer corner radius; the inner core stays concentric (minus the 5 DIP bezel).</summary>
    public new double CornerRadius
    {
        get => _shell.CornerRadius.TopLeft;
        set
        {
            _shell.CornerRadius = new CornerRadius(value);
            _core.CornerRadius = new CornerRadius(Math.Max(4, value - BezelPadding));
        }
    }

    public void Add(UIElement child) => _content.Children.Add(child);

    /// <summary>Vertical top-edge highlight: #16FFFFFF at the top fading to #08FFFFFF.</summary>
    private static LinearGradientBrush TopEdgeHighlight()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new WPoint(0.5, 0),
            EndPoint = new WPoint(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = ControlTheme.TopEdgeHighlight, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = ControlTheme.TopEdgeFade, Offset = 1 });
        return brush;
    }
}

/// <summary>
/// P2-B WinUI port of the double-bezel glass card (old UiPrimitives.cs
/// GlassCard :236-334): outer shell (blended lighter surface + hairline) holds
/// the darker core with a concentric radius, tracked brass eyebrow, title,
/// hairline rule and the measured body stack. The header block keeps the old
/// 88 DIP reserve so a wrapped title cannot collide with the first row.
/// </summary>
public sealed class GlassCard : UserControl
{
    public string Title { get; }
    public string Eyebrow { get; }

    /// <summary>Content stack: add rows/primitives here.</summary>
    public VertStack Body { get; } = new() { Gap = DesignTokens.SpaceM };

    public GlassCard(string title, string eyebrow = "")
    {
        Title = title;
        Eyebrow = eyebrow;

        var shellFill = ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.Background, 0.045f);
        var shell = new Border
        {
            Background = ControlTheme.Brush(shellFill),
            CornerRadius = new CornerRadius(DesignTokens.RadiusOuter),
            BorderBrush = ControlTheme.Brush(ControlTheme.Hairline),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
        };

        var core = new Grid();
        core.Background = ControlTheme.Brush(ControlTheme.Surface);
        core.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        core.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        core.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var eyebrowText = new TextBlock
        {
            Text = (string.IsNullOrEmpty(eyebrow) ? title : eyebrow).ToUpperInvariant(),
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.Accent),
            CharacterSpacing = 180, // 0.18 em tracking, old Ui.DrawTracked parity
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = ControlTheme.Dip(DesignTokens.SectionSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        };
        var header = new VertStack { Gap = 4, Padding = new Thickness(13, 17, 13, 8) };
        header.Children.Add(eyebrowText);
        header.Children.Add(titleText);
        Grid.SetRow(header, 0);
        core.Children.Add(header);

        var rule = new Border
        {
            Height = 1,
            Margin = new Thickness(13, 0, 13, 0),
            Background = ControlTheme.Brush(ControlTheme.Hairline),
        };
        Grid.SetRow(rule, 1);
        core.Children.Add(rule);

        Body.Margin = new Thickness(13, 12, 13, 13);
        Grid.SetRow(Body, 2);
        core.Children.Add(Body);

        var coreBorder = new Border
        {
            Background = ControlTheme.Brush(ControlTheme.Surface),
            CornerRadius = new CornerRadius(DesignTokens.RadiusInner),
            Child = core,
        };
        shell.Child = coreBorder;
        Content = shell;

        AutomationProperties.SetName(this, title);
    }

    /// <summary>Adds a content control to the card body.</summary>
    public T Add<T>(T child) where T : UIElement
    {
        Body.Children.Add(child);
        return child;
    }
}
