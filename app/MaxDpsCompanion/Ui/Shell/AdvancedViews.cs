using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using ShapeRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-C shared composition helpers for the Advanced overlay tabs (WinUI port of
/// the MainForm card/row builders :2088-2192). Types stay stock
/// (TextBlock/CheckBox/TextBox/WheelSafeNumeric/Grid) so every tab keeps the
/// existing control-map contract.
/// </summary>
internal static class AdvancedUi
{
    public static TextBlock Text(string text, float points, WColor color, bool bold = false) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(points),
        Foreground = ControlTheme.Brush(color),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Short visible line (old Hint :2100-2111): wraps inside the card.</summary>
    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(DesignTokens.BodySize),
        Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 800,
    };

    /// <summary>Short visible line + full sentence in the owned tooltip (old HintTip :2118-2124).</summary>
    public static TextBlock HintTip(string shortText, string fullDetail)
    {
        var label = Hint(shortText);
        OwnedToolTip.SetToolTip(label, fullDetail);
        AutomationProperties.SetHelpText(label, fullDetail);
        return label;
    }

    public static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
        Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Check row (old CheckRow :2126-2137): long labels wrap, name = text.</summary>
    public static CheckBox Check(string text, string? hint = null)
    {
        var box = new CheckBox
        {
            Content = text,
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            MaxWidth = 820,
            MinHeight = 30,
            Margin = new Thickness(0, 2, 0, 2),
        };
        AutomationProperties.SetName(box, text);
        if (!string.IsNullOrEmpty(hint))
        {
            OwnedToolTip.SetToolTip(box, hint);
            AutomationProperties.SetHelpText(box, hint);
        }
        return box;
    }

    /// <summary>Single-line text field (old styled TextBox).</summary>
    public static TextBox Field(string value, string name)
    {
        var box = new TextBox
        {
            Text = value ?? "",
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            MinHeight = 34,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(box, name);
        return box;
    }

    /// <summary>
    /// Numeric field with an accessible name (old Spin :2305-2306). Plain
    /// NumberBox: the P2-B WheelSafeNumeric subclass was deleted in P3 — a
    /// C#-only Control subclass is not in XamlTypeInfo in WinUI 3 and its style
    /// lookup crashed at measure (COMException 0x800F1000, found in the P2-C
    /// smoke). The stock NumberBox does not step on the pointer wheel, so the
    /// page keeps its scroll behaviour regardless.
    /// </summary>
    public static NumberBox Number(double value, double min, double max, string name)
    {
        var spin = new NumberBox
        {
            Value = value,
            Minimum = min,
            Maximum = max,
            Width = 96,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            MinHeight = 34,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        AutomationProperties.SetName(spin, name);
        return spin;
    }

    /// <summary>Caption above/first + control in a two-column measured row (old FieldRow usage).</summary>
    public static Grid Row(string caption, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = Text(caption, DesignTokens.BodySize, ControlTheme.TextSecondary);
        label.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(label);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>SettingRow wrapper with the old RowFor semantics (:2187-2192).</summary>
    public static SettingRow ToggleRow(string title, string subtitle, ToggleSwitch toggle)
    {
        AutomationProperties.SetName(toggle, title);
        AutomationProperties.SetHelpText(toggle, subtitle);
        var row = new SettingRow(title, subtitle, toggle);
        row.Margin = new Thickness(0, 0, 0, 6);
        return row;
    }

    public static ChamferButton Button(string text, ButtonRole role, Action click)
    {
        var button = new ChamferButton
        {
            Content = text,
            Role = role,
            TrailingGlyph = role == ButtonRole.Primary ? "\u2197" : null,
            FontSize = ControlTheme.Dip(9.5f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Height = 40,
            MinHeight = 38,
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Equal-width button row (old ButtonsRow :2173-2185).</summary>
    public static Grid ButtonRow(params ChamferButton[] buttons)
    {
        var grid = new Grid { ColumnSpacing = DesignTokens.SpaceS, Margin = new Thickness(0, 2, 0, 2) };
        for (var i = 0; i < buttons.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(buttons[i], i);
            grid.Children.Add(buttons[i]);
        }
        return grid;
    }

    /// <summary>Card filled with the given children in a spaced vertical stack.</summary>
    public static GlassCard Card(string title, string eyebrow, params UIElement[] children)
    {
        var card = new GlassCard(title, eyebrow);
        foreach (var child in children) card.Add(child);
        return card;
    }

    public static void Scroll(UIElement content, ThemedScrollHost host, double padding = 14)
    {
        host.Items.Padding = new Thickness(padding);
        host.Add(content);
    }
}

/// <summary>
/// P2-C WinUI port of the semantic banner (old Ui/SemanticBanner.cs :12-85):
/// tinted rounded row + tone stripe + glyph + title and a wrapped detail line.
/// Status is never carried by colour alone.
/// </summary>
internal sealed class BannerView : UserControl
{
    private readonly Border _frame = new();
    private readonly ShapeRect _stripe;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private StatusTone _tone = StatusTone.Info;

    public BannerView(string title, StatusTone tone = StatusTone.Info)
    {
        _title = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _detail = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 3, 0, 0),
        };
        _stripe = new ShapeRect
        {
            Width = 3,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(1, 6, 0, 6),
        };
        var stack = new StackPanel { Margin = new Thickness(12, 6, 12, 6) };
        stack.Children.Add(_title);
        stack.Children.Add(_detail);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_stripe, 0);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(_stripe);
        grid.Children.Add(stack);

        _frame.CornerRadius = new CornerRadius(ControlTheme.Dip(DesignTokens.RadiusControl));
        _frame.BorderThickness = new Thickness(1);
        _frame.Child = grid;
        Content = _frame;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, title);
        Title = title;
        Tone = tone;
        Refresh();
    }

    public string Title
    {
        get => _title.Text;
        set
        {
            _title.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    public string Detail
    {
        get => _detail.Text;
        set
        {
            if (_detail.Text == value) return;
            _detail.Text = value;
            _detail.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public StatusTone Tone
    {
        get => _tone;
        set { _tone = value; Refresh(); }
    }

    private void Refresh()
    {
        var color = ControlTheme.StatusColor(_tone);
        _frame.Background = ControlTheme.Brush(ControlTheme.Tint(color, ControlTheme.Surface));
        _frame.BorderBrush = ControlTheme.Brush(ControlTheme.WithAlpha(color, 120));
        _stripe.Fill = ControlTheme.Brush(color);
        _title.Text = $"{ControlTheme.GlyphFor(_tone)}  {Title}";
        _title.Foreground = ControlTheme.Brush(color);
    }
}

/// <summary>
/// P2-C WinUI port of the single stacked proportion bar (old Ui/Pages.cs
/// ProportionBar :69-102): only non-zero segments, last segment absorbs the
/// rounding remainder via star sizing.
/// </summary>
internal sealed class ProportionBarView : UserControl
{
    private readonly Grid _grid = new() { ColumnSpacing = 1 };

    public ProportionBarView()
    {
        Height = 22;
        Content = _grid;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, "Coverage proportion");
    }

    public (int Value, WColor Color, string Label)[] Segments { get; private set; } = [];

    public void SetSegments(params (int Value, WColor Color, string Label)[] segments)
    {
        Segments = segments;
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        foreach (var (value, color, label) in segments)
        {
            if (value <= 0) continue;
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            var cell = new ShapeRect
            {
                Fill = ControlTheme.Brush(color),
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(cell, label);
            Grid.SetColumn(cell, _grid.ColumnDefinitions.Count - 1);
            _grid.Children.Add(cell);
        }
        AutomationProperties.SetName(this, "Coverage proportion");
    }
}

/// <summary>
/// Read-only ADDON-WINS witness: one mask pill per toggle bit (old ConsoleHome
/// role in the migration: MainForm's Advanced mirrors :193-206, but read-only
/// and driven by <see cref="ToggleSync.EffectiveMask"/> — fail-closed, so no
/// echo means no bit is assumed ON; nothing is ever pushed).
/// </summary>
internal sealed class MaskPillRow : UserControl
{
    private readonly StatusPill _pill;
    private readonly int _bit;

    public MaskPillRow(string shortName, int bit, string fullName)
    {
        _bit = bit;
        _pill = new StatusPill(shortName, StatusTone.Muted);
        OwnedToolTip.SetToolTip(_pill, fullName);
        AutomationProperties.SetName(_pill, $"Effective mask: {fullName}");
        Content = _pill;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(this, $"Effective mask: {fullName}");
    }

    public void Update(bool valid, int mask) =>
        _pill.Tone = valid && (mask & (1 << _bit)) != 0 ? StatusTone.Success : StatusTone.Muted;
}
