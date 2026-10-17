using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WColor = Windows.UI.Color;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the measured caption/value row (old UiPrimitives.cs KvRow
/// :341-387): tracked uppercase caption above the value, tone colour on the
/// value, emphasis switching to the section size.
/// </summary>
internal sealed class KvRow : UserControl
{
    private readonly TextBlock _caption;
    private readonly TextBlock _value;
    private StatusTone _tone = StatusTone.Neutral;
    private bool _emphasize;

    public KvRow(string caption, string value = "-")
    {
        _caption = new TextBlock
        {
            Text = caption.ToUpperInvariant(),
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            CharacterSpacing = 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _value = new TextBlock
        {
            Text = value,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var stack = new VertStack { Gap = 4, Padding = new Thickness(0, 4, 0, 4) };
        stack.Children.Add(_caption);
        stack.Children.Add(_value);
        Content = stack;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, caption);
        RefreshValue();
    }

    public string Caption
    {
        get => _caption.Text;
        set => _caption.Text = value.ToUpperInvariant();
    }

    public string Value
    {
        get => _value.Text;
        set
        {
            if (_value.Text == value) return;
            _value.Text = value;
        }
    }

    public StatusTone Tone
    {
        get => _tone;
        set { _tone = value; RefreshValue(); }
    }

    public bool Emphasize
    {
        get => _emphasize;
        set { _emphasize = value; RefreshValue(); }
    }

    private void RefreshValue()
    {
        _value.FontSize = ControlTheme.Dip(_emphasize ? DesignTokens.SectionSize : DesignTokens.BodySize);
        _value.FontWeight = _emphasize
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;
        _value.Foreground = ControlTheme.Brush(
            _tone == StatusTone.Neutral ? ControlTheme.TextPrimary : ControlTheme.StatusColor(_tone));
    }
}

/// <summary>
/// P2-B WinUI port of the metric tile (old UiPrimitives.cs MetricTile :433-517):
/// tone strip + big value + label, optionally clickable to drill in. Sizing is
/// content-driven in WinUI; the old 68 DIP height and 150 DIP minimum width are
/// kept as the layout budget.
/// </summary>
internal sealed class MetricTile : UiClickable
{
    private readonly Border _frame = new();
    private readonly ShapeRect _toneStrip;
    private readonly TextBlock _value;
    private readonly TextBlock _label;
    private StatusTone _tone = StatusTone.Neutral;
    private bool _clickable;
    private bool _focused;

    public MetricTile(string label, string value, StatusTone tone = StatusTone.Neutral, bool clickable = false)
    {
        Height = 68;
        MinWidth = 150;
        _clickable = clickable;
        IsTabStop = clickable;
        if (clickable) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

        _frame.CornerRadius = new CornerRadius(DesignTokens.RadiusTile);
        _frame.BorderThickness = new Thickness(1);
        _frame.BorderBrush = ControlTheme.Brush(ControlTheme.Hairline);

        _toneStrip = new ShapeRect
        {
            Width = 3,
            Height = 22,
            Margin = new Thickness(12, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _value = new TextBlock
        {
            Text = value,
            FontSize = ControlTheme.Dip(20F),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            Margin = new Thickness(24, 10, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _label = new TextBlock
        {
            Text = label,
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
            Margin = new Thickness(24, 42, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var root = new Grid();
        root.Children.Add(_frame);
        root.Children.Add(_toneStrip);
        root.Children.Add(_value);
        root.Children.Add(_label);
        Content = root;

        Tone = tone;
        AutomationProperties.SetName(this, $"{label}: {value}");
        GotFocus += (_, _) => { _focused = true; RefreshFrame(); };
        LostFocus += (_, _) => { _focused = false; RefreshFrame(); };
        RefreshFrame();
    }

    public string Value
    {
        get => _value.Text;
        set
        {
            _value.Text = value;
            AutomationProperties.SetName(this, $"{_label.Text}: {value}");
        }
    }

    public string Label
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            AutomationProperties.SetName(this, $"{value}: {_value.Text}");
        }
    }

    public StatusTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            _toneStrip.Fill = ControlTheme.Brush(ControlTheme.StatusColor(value));
        }
    }

    public bool Clickable
    {
        get => _clickable;
        set
        {
            _clickable = value;
            IsTabStop = value;
            RefreshFrame();
        }
    }

    protected override void OnHoverChanged() => RefreshFrame();

    protected override void OnPressedChanged() => RefreshFrame();

    private void RefreshFrame()
    {
        // Old hover lift: 0.02 -> 0.06 blend of white over the surface (clickable only).
        var lift = 0.02f + (_clickable && Hovered ? 0.04f : 0f);
        _frame.Background = ControlTheme.Brush(
            ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.Surface, lift));
        var accent = _focused && _clickable;
        _frame.BorderBrush = ControlTheme.Brush(accent ? ControlTheme.Accent : ControlTheme.Hairline);
        _frame.BorderThickness = new Thickness(accent ? 2 : 1);
    }
}

/// <summary>
/// P2-B WinUI port of the status pill (old UiPrimitives.cs StatusPill :519-582):
/// tone glyph + word inside a tinted capsule. Colour plus text, always.
/// </summary>
internal sealed class StatusPill : UserControl
{
    private readonly Border _frame = new();
    private readonly TextBlock _text = new()
    {
        FontSize = ControlTheme.Dip(DesignTokens.BodySize),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private string _raw = "";
    private StatusTone _tone = StatusTone.Neutral;

    public StatusPill(string text, StatusTone tone = StatusTone.Neutral)
    {
        _frame.CornerRadius = new CornerRadius(14);
        _frame.Padding = new Thickness(13, 4, 13, 4);
        _frame.MinHeight = 28;
        _frame.BorderThickness = new Thickness(1);
        _frame.Child = _text;
        Content = _frame;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        _raw = text;
        _tone = tone;
        Refresh();
    }

    public string Text
    {
        get => _raw;
        set { _raw = value; Refresh(); }
    }

    public StatusTone Tone
    {
        get => _tone;
        set { _tone = value; Refresh(); }
    }

    private void Refresh()
    {
        var color = ControlTheme.StatusColor(_tone);
        _text.Text = $"{ControlTheme.GlyphFor(_tone)}  {_raw}";
        _text.Foreground = ControlTheme.Brush(color);
        _frame.Background = ControlTheme.Brush(ControlTheme.Tint(color, ControlTheme.Surface));
        _frame.BorderBrush = ControlTheme.Brush(ControlTheme.WithAlpha(color, 130));
        AutomationProperties.SetName(this, _raw);
    }
}

/// <summary>
/// P2-B WinUI port of the filter chip (old UiPrimitives.cs FilterChip :584-649):
/// selected state + label; activation stays on the inherited Click. The owning
/// view decides whether a click toggles <see cref="Selected"/>.
/// </summary>
public sealed class FilterChip : UiClickable
{
    private readonly Border _frame = new();
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _selected;
    private bool _focused;

    public FilterChip(string text, bool selected = false)
    {
        Height = 28;
        MinHeight = 28;
        Margin = new Thickness(0, 0, 6, 6);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

        _text.Text = text;
        _text.FontSize = ControlTheme.Dip(DesignTokens.BodySize);
        _frame.CornerRadius = new CornerRadius(14);
        _frame.Padding = new Thickness(12, 0, 12, 0);
        _frame.Child = _text;
        Content = _frame;

        _selected = selected;
        AutomationProperties.SetName(this, text);
        GotFocus += (_, _) => { _focused = true; Refresh(); };
        LostFocus += (_, _) => { _focused = false; Refresh(); };
        Refresh();
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

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Refresh(); }
    }

    protected override void OnHoverChanged() => Refresh();

    private void Refresh()
    {
        var fill = _selected
            ? ControlTheme.Tint(ControlTheme.Accent, ControlTheme.Surface)
            : Hovered ? ControlTheme.SurfaceElevated : ControlTheme.Surface;
        _frame.Background = ControlTheme.Brush(fill);
        _frame.BorderBrush = ControlTheme.Brush(_selected || _focused ? ControlTheme.Accent : ControlTheme.Hairline);
        _frame.BorderThickness = new Thickness(_focused && !_selected ? 2 : 1);
        _text.Foreground = ControlTheme.Brush(_selected ? ControlTheme.AccentSoft : ControlTheme.TextSecondary);
        _text.FontWeight = _selected
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;
    }
}
