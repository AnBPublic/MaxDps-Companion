using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B port of the S5 owner-drawn combo box (old UiFoundation.cs
/// OwnedComboBox). WinUI's ComboBox is already dark-themed and owner-rendered,
/// so this subclass only carries the width-tier scale hook
/// (<see cref="ApplyScale"/>) so P2-C code ports 1:1.
/// </summary>
public sealed class OwnedComboBox : ComboBox
{
    public OwnedComboBox()
    {
        FontSize = ControlTheme.Dip(DesignTokens.BodySize);
        MinHeight = 34;
        AutomationProperties.SetName(this, "Dropdown");
    }

    /// <summary>Applies the width tier: type step and a matching control height.</summary>
    public void ApplyScale(UiScale scale)
    {
        FontSize = ControlTheme.Dip(scale.BaseFont);
        MinHeight = Math.Max(30, scale.ButtonRow2Height - 6);
    }
}

/// <summary>
/// P2-B port of the S5 owned tooltip (old UiFoundation.cs OwnedToolTip). The
/// full, unwrapped sentence is handed to the tooltip host; the themed bubble
/// wraps at 380 DIP and paints the console surface with a brass keyline.
/// Accessibility gets the full text through AutomationProperties.HelpText.
/// </summary>
internal static class OwnedToolTip
{
    /// <summary>Cap the text column so a very long hint stays readable.</summary>
    public const double MaxTextWidth = 380;

    public static void SetToolTip(FrameworkElement element, string? text)
    {
        if (element is null) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            ToolTipService.SetToolTip(element, null);
            return;
        }

        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = MaxTextWidth,
            FontSize = ControlTheme.Dip(DesignTokens.MetaSize),
            Foreground = ControlTheme.Brush(ControlTheme.TextPrimary),
        };
        var tip = new ToolTip
        {
            Content = block,
            Background = ControlTheme.Brush(ControlTheme.SurfaceElevated),
            BorderBrush = ControlTheme.Brush(ControlTheme.Accent),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7, 9, 7),
            CornerRadius = new CornerRadius(4),
        };
        ToolTipService.SetToolTip(element, tip);
        AutomationProperties.SetHelpText(element, text);
    }
}
