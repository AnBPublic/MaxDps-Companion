using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WColor = Windows.UI.Color;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the hero-card setting row (old UiControls.cs SettingRow
/// :478-641): title + up-to-three-line subtitle on the left, the toggle
/// vertically centred on the right, one rounded hairline row behind them
/// (zebra depth via <see cref="AlternateFill"/>). The full-length hint goes to
/// the themed owned tooltip.
/// </summary>
public sealed class SettingRow : UserControl
{
    private static readonly WColor RowFill = ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.Surface, 0.035f);
    private static readonly WColor RowFillAlt = ControlTheme.Blend(Microsoft.UI.Colors.White, ControlTheme.Surface, 0.060f);

    private readonly TextBlock _titleLabel;
    private readonly TextBlock _subtitleLabel;
    private readonly ToggleSwitch _toggle;
    private readonly Border _fill;
    private bool _alternate;
    private string? _hint;

    public SettingRow(string title, string subtitle, ToggleSwitch toggle)
    {
        _toggle = toggle;
        _titleLabel = new TextBlock
        {
            Text = title,
            FontSize = ControlTheme.Dip(DesignTokens.LabelSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        _subtitleLabel = new TextBlock
        {
            Text = subtitle,
            FontSize = ControlTheme.Dip(DesignTokens.BodySize),
            Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var textColumn = new VertStack { Gap = 2 };
        textColumn.Children.Add(_titleLabel);
        textColumn.Children.Add(_subtitleLabel);

        _fill = new Border
        {
            Background = ControlTheme.Brush(RowFill),
            CornerRadius = new CornerRadius(DesignTokens.RadiusControl),
            BorderBrush = ControlTheme.Brush(ControlTheme.Hairline),
            BorderThickness = new Thickness(1),
        };

        var layout = new Grid { Padding = new Thickness(18, 9, 14, 12) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textColumn, 0);
        layout.Children.Add(textColumn);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(22, 0, 0, 0);
        Grid.SetColumn(toggle, 1);
        layout.Children.Add(toggle);

        var root = new Grid();
        root.Children.Add(_fill);
        root.Children.Add(layout);
        Content = root;

        AutomationProperties.SetName(this, title);
        OwnedToolTip.SetToolTip(_titleLabel, title);
        OwnedToolTip.SetToolTip(_subtitleLabel, subtitle);
    }

    public ToggleSwitch Toggle => _toggle;

    public string Title
    {
        get => _titleLabel.Text;
        set
        {
            _titleLabel.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    public string Subtitle
    {
        get => _subtitleLabel.Text;
        set => _subtitleLabel.Text = value;
    }

    /// <summary>Alternating row depth for group scanning (zebra). Set by the parent.</summary>
    public bool AlternateFill
    {
        get => _alternate;
        set
        {
            _alternate = value;
            _fill.Background = ControlTheme.Brush(value ? RowFillAlt : RowFill);
        }
    }

    /// <summary>
    /// Longer description shown on hover (the visible subtitle stays short).
    /// </summary>
    public string? Hint
    {
        get => _hint;
        set
        {
            _hint = value;
            if (string.IsNullOrWhiteSpace(value)) return;
            OwnedToolTip.SetToolTip(this, value);
            OwnedToolTip.SetToolTip(_titleLabel, value);
            OwnedToolTip.SetToolTip(_subtitleLabel, value);
            OwnedToolTip.SetToolTip(_toggle, value);
        }
    }

    /// <summary>Re-applies a width tier (D5): type scale and toggle size.</summary>
    public void ApplyScale(UiScale scale)
    {
        _titleLabel.FontSize = ControlTheme.Dip(Math.Max(8f, scale.BaseFont + 1f));
        _subtitleLabel.FontSize = ControlTheme.Dip(scale.BaseFont);
        _toggle.Width = scale.ToggleWidth;
        _toggle.Height = scale.ToggleHeight;
    }
}

/// <summary>
/// One "Rotation"/"Automation" row: two <see cref="SettingRow"/>s side by side
/// (old UiControls.cs ToggleRowPanel :648-681). 4.5.1: below
/// <see cref="ResponsiveColumns.StackWidth"/> the pair stacks into one column so
/// a narrow window never squeezes the row copy.
/// </summary>
public sealed class ToggleRowPanel : UserControl
{
    private readonly ResponsiveColumns _grid = new() { Gap = DesignTokens.SpaceS, StackWidth = 500 };

    public ToggleRowPanel(SettingRow left, SettingRow right)
    {
        _grid.Children.Add(left);
        _grid.Children.Add(right);
        Content = _grid;
    }

    public double Gap
    {
        get => _grid.Gap;
        set => _grid.Gap = value;
    }
}
