using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using ShapeEllipse = Microsoft.UI.Xaml.Shapes.Ellipse;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the owner-drawn clickable base (old UiPrimitives.cs
/// UiClickable :119-189). In WinUI the Button stack already owns the
/// press/release/click gesture, so this class only re-exposes the old
/// Hovered/Pressed hooks through virtual callbacks and strips the chrome via
/// the "UiClickableStyle" template (content-only, transparent).
/// </summary>
public class UiClickable : Button
{
    public UiClickable()
    {
        // NOTE: the "UiClickableStyle" template is NOT applied here. WinUI
        // resolves the element's type at this point as the Button base, so a
        // style whose TargetType is the concrete subclass is rejected (4.5.0:
        // the exception was swallowed and every owner-drawn control kept the
        // stock grey Button chrome). Each concrete subclass applies the style
        // as its first constructor statement instead.
        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) =>
        {
            SetHover(false);
            SetPressed(false);
        };
        PointerPressed += (_, _) => SetPressed(true);
        PointerReleased += (_, _) => SetPressed(false);
        PointerCaptureLost += (_, _) => SetPressed(false);
    }

    /// <summary>True while the pointer is over the control.</summary>
    protected bool Hovered { get; private set; }

    /// <summary>True while the left button is held down on this control.</summary>
    protected bool Pressed { get; private set; }

    protected virtual void OnHoverChanged() { }

    protected virtual void OnPressedChanged() { }

    private void SetHover(bool value)
    {
        if (Hovered == value) return;
        Hovered = value;
        OnHoverChanged();
    }

    private void SetPressed(bool value)
    {
        if (Pressed == value) return;
        Pressed = value;
        OnPressedChanged();
    }
}

/// <summary>
/// P2-B WinUI port of the console group header (old UiControls.cs GroupHeader
/// :430-476): brass marker + sentence-case near-white label + soft keyline
/// rule. Grouping is structural, never colour alone.
/// </summary>
public sealed class GroupHeader : UserControl
{
    private readonly TextBlock _label;

    public GroupHeader() : this("") { }

    public GroupHeader(string title)
    {
        _label = new TextBlock
        {
            Text = title,
            FontSize = ControlTheme.Dip(9.5f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.Bone),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var marker = new ShapeRect
        {
            Width = 5,
            Height = 5,
            Fill = ControlTheme.Brush(ControlTheme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        var rule = new ShapeRect
        {
            Height = 1,
            Fill = ControlTheme.Brush(ControlTheme.Ring),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };

        var grid = new Grid
        {
            Height = 24,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetColumn(marker, 0);
        Grid.SetColumn(_label, 1);
        Grid.SetColumn(rule, 2);
        grid.Children.Add(marker);
        grid.Children.Add(_label);
        grid.Children.Add(rule);
        Content = grid;

        AutomationProperties.SetName(this, title);
    }

    public string Title
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    /// <summary>Width-tier type step (D5).</summary>
    public void ApplyScale(UiScale scale)
    {
        _label.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont - 0.5f));
    }
}

/// <summary>
/// Round link lamp (old UiControls.cs LinkLamp :832-860). Always paired with a
/// text word, never colour-alone. <see cref="Pulse"/> is the one-shot 400 ms
/// bloom; <see cref="PulseLoop"/>/<see cref="StopPulse"/> are the v4.0.1
/// gentle 1.6 s opacity loop the Exp status card runs while the engine is
/// running. Shape classes are sealed in WinUI 3, so this wraps the ellipse.
/// </summary>
public sealed class LinkLamp : UserControl
{
    private readonly ShapeEllipse _lamp = new() { Width = 14, Height = 14 };
    private WColor _dot = ControlTheme.Keyline;
    private bool _looping;

    public WColor Dot
    {
        get => _dot;
        set
        {
            if (_dot == value) return;
            _dot = value;
            _lamp.Fill = ControlTheme.Brush(value);
        }
    }

    /// <summary>Lamp diameter; the Exp status row uses the 8 DIP dot.</summary>
    public double DotSize
    {
        get => _lamp.Width;
        set
        {
            Width = value;
            Height = value;
            _lamp.Width = value;
            _lamp.Height = value;
        }
    }

    public LinkLamp()
    {
        Width = 14;
        Height = 14;
        _lamp.Fill = ControlTheme.Brush(_dot);
        Content = _lamp;
        AutomationProperties.SetName(this, "Link lamp");
    }

    public void Pulse()
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_lamp);
            var compositor = visual.Compositor;
            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 0.35f);
            animation.InsertKeyFrame(1f, 1f);
            animation.Duration = TimeSpan.FromMilliseconds(400);
            visual.StartAnimation("Opacity", animation);
        }
        catch (Exception)
        {
            // Composition unavailable: the lamp keeps its last painted state.
        }
    }

    /// <summary>Gentle running pulse: opacity 1 -> 0.55 -> 1 on a 1.6 s loop.</summary>
    public void PulseLoop()
    {
        if (_looping) return;
        _looping = true;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_lamp);
            var compositor = visual.Compositor;
            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 1f);
            animation.InsertKeyFrame(0.5f, 0.55f);
            animation.InsertKeyFrame(1f, 1f);
            animation.Duration = TimeSpan.FromMilliseconds(1600);
            animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
            visual.StartAnimation("Opacity", animation);
        }
        catch (Exception)
        {
            // Composition unavailable: the lamp stays solid.
        }
    }

    /// <summary>Stops the running loop and restores full opacity.</summary>
    public void StopPulse()
    {
        if (!_looping) return;
        _looping = false;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_lamp);
            visual.StopAnimation("Opacity");
            visual.Opacity = 1f;
        }
        catch (Exception)
        {
            // Composition unavailable: nothing to stop.
        }
    }
}

/// <summary>
/// Class/spec pill (old UiControls.cs ClassBadge :964-996): rounded capsule,
/// info tint over the surface, centred bold label.
/// </summary>
public sealed class ClassBadge : ContentControl
{
    private readonly TextBlock _text = new()
    {
        FontSize = ControlTheme.Dip(8.25f),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public ClassBadge()
    {
        var frame = new Border
        {
            Background = ControlTheme.Brush(ControlTheme.Tint(ControlTheme.Info, ControlTheme.Surface)),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 26,
            Child = _text,
        };
        Content = frame;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(this, "Class and spec");
    }

    public string Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }
}
