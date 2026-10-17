using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;
using XamlImage = Microsoft.UI.Xaml.Controls.Image;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-D virtualized ability row (WinUI port of the owner-drawn row in the old
/// Ui/AbilityExplorer.cs VirtualAbilityList :261-481): icon + name + subline,
/// tier/owner/verdict badges, ON/OFF toggle and the optional inline details the
/// explorer expands on click. Pooled by <see cref="AbilityListView"/>: Bind and
/// Unbind only write values, nothing is re-created per row.
/// </summary>
internal sealed class AbilityRowView : UserControl
{
    private const double IconDip = 36;

    private readonly Border _iconTile;
    private readonly TextBlock _iconLetter;
    private readonly XamlImage _icon;
    private readonly TextBlock _name;
    private readonly TextBlock _sub;
    private readonly Border _tierBadge;
    private readonly TextBlock _tierText;
    private readonly Border _ownerBadge;
    private readonly TextBlock _ownerText;
    private readonly Border _verdictPill;
    private readonly TextBlock _verdictText;
    private readonly ToggleSwitch _toggle;
    private readonly TextBlock _toggleLabel;
    private readonly StackPanel _details;
    private readonly TextBlock[] _detailLines;

    private AbilityDefinition? _ability;
    private Action<bool>? _toggleHandler;
    private Action? _clickHandler;
    private bool _suppressToggle;

    public AbilityRowView()
    {
        _iconTile = new Border
        {
            Width = IconDip,
            Height = IconDip,
            CornerRadius = new CornerRadius(7),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _iconLetter = new TextBlock
        {
            FontSize = ControlTheme.Dip(13f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var letterHost = new Grid();
        letterHost.Children.Add(_iconTile);
        letterHost.Children.Add(_iconLetter);

        _icon = new XamlImage
        {
            Width = IconDip,
            Height = IconDip,
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed,
        };
        letterHost.Children.Add(_icon);

        _name = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.LabelSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _sub = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var text = new VertStack { Gap = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_name);
        text.Children.Add(_sub);

        _tierText = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _tierBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _tierText,
        };

        _ownerText = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ownerBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _ownerText,
        };

        _verdictText = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.MicroSize),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _verdictPill = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            MaxWidth = 170,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _verdictText,
        };

        _toggleLabel = new TextBlock
        {
            FontSize = ControlTheme.Dip(7.5f),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _toggle = new ToggleSwitch { Width = 44, Height = 26, VerticalAlignment = VerticalAlignment.Center };
        _toggle.Toggled += (_, _) =>
        {
            if (_suppressToggle) return;
            _toggleHandler?.Invoke(_toggle.IsOn);
        };
        // The toggle owns its pointer input: no row-click/no list selection.
        _toggle.Tapped += (_, args) => args.Handled = true;
        var toggleCluster = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggleCluster.Children.Add(_toggleLabel);
        toggleCluster.Children.Add(_toggle);

        var line = new Grid
        {
            Height = 56,
            ColumnSpacing = 9,
            Padding = new Thickness(8, 0, 8, 0),
        };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(letterHost, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_tierBadge, 2);
        Grid.SetColumn(_ownerBadge, 3);
        Grid.SetColumn(_verdictPill, 4);
        Grid.SetColumn(toggleCluster, 5);
        line.Children.Add(letterHost);
        line.Children.Add(text);
        line.Children.Add(_tierBadge);
        line.Children.Add(_ownerBadge);
        line.Children.Add(_verdictPill);
        line.Children.Add(toggleCluster);

        // Explorer inline expansion (old DrawInlineDetails :456-481).
        _detailLines = [MakeDetailLine(), MakeDetailLine(), MakeDetailLine(), MakeDetailLine(), MakeDetailLine()];
        var hint = new TextBlock
        {
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = ControlTheme.Brush(ControlTheme.TextMuted),
            Text = "Open the inspector for relationships, evidence and audit findings.",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(18, 3, 0, 6),
        };
        _details = new StackPanel
        {
            Background = ControlTheme.Brush(WColor.FromArgb(22, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(0, 5, 0, 0),
        };
        foreach (var detailLine in _detailLines) _details.Children.Add(detailLine);
        _details.Children.Add(hint);

        var root = new StackPanel();
        root.Children.Add(line);
        root.Children.Add(_details);
        Content = root;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;

        Tapped += (_, _) => _clickHandler?.Invoke();
    }

    public AbilityDefinition? Ability => _ability;

    private static TextBlock MakeDetailLine() => new()
    {
        FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
        Foreground = ControlTheme.Brush(ControlTheme.TextSecondary),
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(18, 1, 0, 0),
    };

    public void Bind(
        AbilityDefinition ability,
        string? condition,
        bool enabled,
        bool automatable,
        string? verdict,
        bool expanded,
        Action<bool> toggleAction,
        Action clickAction)
    {
        _ability = ability;
        _toggleHandler = toggleAction;
        _clickHandler = clickAction;

        _name.Text = ability.Name;
        _sub.Text = AbilityViewPresets.SubLine(ability, condition);
        AutomationProperties.SetName(this, $"{ability.Name}, {ability.Category}");
        OwnedToolTip.SetToolTip(this, AbilityViewPresets.FullReason(ability));

        SetPlaceholder();
        SetToggleState(enabled, automatable);
        SetBadges(ability);
        SetVerdict(verdict);
        SetDetails(ability, expanded);
        RefreshIcon();
    }

    public void Unbind()
    {
        _ability = null;
        _toggleHandler = null;
        _clickHandler = null;
        _icon.Source = null;
        _icon.Visibility = Visibility.Collapsed;
        _details.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(this, "Ability row");
    }

    /// <summary>Value-only toggle write; never echoes back into the host.</summary>
    public void SetToggleState(bool enabled, bool automatable)
    {
        _suppressToggle = true;
        try
        {
            _toggle.IsOn = enabled;
        }
        finally
        {
            _suppressToggle = false;
        }
        _toggle.IsEnabled = automatable;
        _toggleLabel.Text = automatable ? (enabled ? "ON" : "OFF") : "N/A";
        if (_ability is not null)
        {
            var state = automatable ? "Automatic use toggle" : "Not automatable";
            AutomationProperties.SetName(_toggle, $"{_ability.Name} {state}");
            OwnedToolTip.SetToolTip(_toggle, $"{_ability.Name}: {state}");
        }
    }

    public void SetExpanded(bool expanded) =>
        _details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Value-only verdict write (S6 live verdict refresh).</summary>
    public void UpdateVerdict(string? verdict) => SetVerdict(verdict);

    /// <summary>Re-resolves the engine icon (called for realized rows on IconReady).</summary>
    public void RefreshIcon()
    {
        if (_ability is not { } ability) return;
        var pixels = IconPixels();
        var image = ScaledIconCache.Instance.Get(ability.SpellId, pixels);
        if (image is null) return;
        _icon.Source = image;
        _icon.Visibility = Visibility.Visible;
    }

    private int IconPixels()
    {
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        return Math.Max(16, (int)Math.Round(IconDip * scale));
    }

    private void SetBadges(AbilityDefinition ability)
    {
        if (ability.IsSurvival && ability.Tier != DefensiveTier.None)
        {
            var tier = ability.Tier switch
            {
                DefensiveTier.Minor => "Minor",
                DefensiveTier.Major => "Major",
                DefensiveTier.Immunity => "Immunity",
                _ => "",
            };
            if (tier.Length > 0)
            {
                var tierTone = ability.Tier switch
                {
                    DefensiveTier.Immunity => ControlTheme.Danger,
                    DefensiveTier.Major => ControlTheme.Warning,
                    _ => ControlTheme.Info,
                };
                _tierText.Text = tier;
                _tierText.Foreground = ControlTheme.Brush(tierTone);
                _tierBadge.Background = ControlTheme.Brush(ControlTheme.Tint(tierTone, ControlTheme.Surface));
                _tierBadge.BorderBrush = ControlTheme.Brush(ControlTheme.WithAlpha(tierTone, 130));
                _tierBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _tierBadge.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            _tierBadge.Visibility = Visibility.Collapsed;
        }

        var (owner, tone) = ability.Ownership switch
        {
            IntelligenceOwnership.Companion => ("Companion", ControlTheme.Success),
            IntelligenceOwnership.MaxDps => ("MaxDps", ControlTheme.Info),
            IntelligenceOwnership.Shared => ("Shared", ControlTheme.Accent),
            IntelligenceOwnership.Manual => ("Manual", ControlTheme.TextMuted),
            _ => ("Unavailable", ControlTheme.Warning),
        };
        _ownerText.Text = owner;
        _ownerText.Foreground = ControlTheme.Brush(tone);
        _ownerBadge.Background = ControlTheme.Brush(ControlTheme.Tint(tone, ControlTheme.Surface));
        _ownerBadge.BorderBrush = ControlTheme.Brush(ControlTheme.WithAlpha(tone, 130));
        _ownerBadge.Visibility = Visibility.Visible;
    }

    private void SetVerdict(string? verdict)
    {
        if (string.IsNullOrWhiteSpace(verdict))
        {
            _verdictPill.Visibility = Visibility.Collapsed;
            AutomationProperties.SetHelpText(this, "");
            return;
        }
        var tone = verdict.StartsWith("held", StringComparison.OrdinalIgnoreCase)
            ? ControlTheme.Warning
            : ControlTheme.Success;
        _verdictText.Text = verdict;
        _verdictText.Foreground = ControlTheme.Brush(tone);
        _verdictPill.Background = ControlTheme.Brush(ControlTheme.Tint(tone, ControlTheme.Surface));
        _verdictPill.Visibility = Visibility.Visible;
        AutomationProperties.SetHelpText(this, verdict);
    }

    private void SetDetails(AbilityDefinition ability, bool expanded)
    {
        _detailLines[0].Text = $"Why it can fire: {AbilityViewPresets.Why(ability)}";
        _detailLines[1].Text = $"When it holds: {AbilityViewPresets.Hold(ability)}";
        _detailLines[2].Text = $"Requirements: {ability.Requires}";
        _detailLines[3].Text = $"Patch: {ability.SourcePatch ?? "?"}  \u00B7  source {ability.SourceType} ({ability.SourceConfidence})";
        _detailLines[4].Text = $"Mode: {ability.Automation}  \u00B7  {(ability.LiveVerified ? "live-verified" : "live-unverified")}";
        SetExpanded(expanded);
    }

    /// <summary>Draws the hue tile + initial under the icon (old DrawIcon :316-328).</summary>
    private void SetPlaceholder()
    {
        if (_ability is not { } ability) return;
        var hue = (int)(Math.Abs((long)ability.SpellId * 2654435761u) % 360);
        var color = ColorFromHue(hue);
        _iconTile.Background = ControlTheme.Brush(ControlTheme.Tint(color, ControlTheme.Surface));
        _iconTile.BorderBrush = ControlTheme.Brush(color);
        _iconTile.BorderThickness = new Thickness(1);
        _iconLetter.Text = string.IsNullOrEmpty(ability.Name) ? "?" : ability.Name[..1].ToUpperInvariant();
        _iconLetter.Foreground = ControlTheme.Brush(color);
    }

    private static WColor ColorFromHue(int hue)
    {
        var (r, g, b) = HsvToRgb(hue, 0.5, 0.9);
        return WColor.FromArgb(255, (byte)r, (byte)g, (byte)b);
    }

    private static (int, int, int) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return ((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
