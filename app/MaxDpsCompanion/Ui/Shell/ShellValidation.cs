using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// P2-E WinUI port of the v2.7/p2-C structural smoke checks
/// (docs/plans/2026-10-11-winui3-v4-migration.md §5/§6). The old WinForms
/// <c>UiShellValidation</c> checked zero-size controls, overlapping siblings,
/// text fit and Tab reachability on the control tree; XAML owns layout and
/// wrapping now, so this port keeps the checks XAML cannot guarantee and that
/// catch the historical defect classes:
/// <list type="bullet">
///   <item>the window title is the single-sourced display title;</item>
///   <item>no VISIBLE interactive element renders at zero size (the "empty
///     card / collapsed row" class);</item>
///   <item>buttons and toggles carry an accessible name (Tab/automation);</item>
///   <item>the Exp contract: the in-game-config shell mounts ZERO toggles;</item>
///   <item>the classic hero card mounts exactly the 14 hero toggles.</item>
/// </list>
/// Findings are printed and make <c>--ui-smoke-test</c> fail (exit 3), so the
/// check is meaningful rather than a no-op.
/// </summary>
internal static class ShellValidation
{
    /// <summary>Hero toggle count in the classic shell (7 rows x 2).</summary>
    private const int HeroToggleCount = 14;

    public static List<string> Validate(FrameworkElement root, string page, string title, bool expMode)
    {
        var findings = new List<string>();
        if (string.IsNullOrWhiteSpace(title))
            findings.Add($"{page}: window title is empty");
        else if (page == "shell" && !title.StartsWith("MaxDPS Companion ", StringComparison.Ordinal))
            findings.Add($"{page}: window title '{title}' does not start with 'MaxDPS Companion '");

        var toggles = 0;
        Walk(root, page, findings, ref toggles);

        if (expMode)
        {
            if (toggles > 0)
                findings.Add($"{page}: Exp layout contains {toggles} ToggleSwitch control(s); the contract is zero");
        }
        else if (page == "shell" && toggles != HeroToggleCount)
        {
            // The classic hero card holds exactly the 14 toggles; overlays are
            // not built yet at this point, so any other count is a layout bug.
            findings.Add($"{page}: expected the {HeroToggleCount} hero toggles, found {toggles}");
        }
        return findings;
    }

    /// <summary>Diagnostic bounds tree (ui-smoke-layout.txt): the visible visual tree with type/size/name.</summary>
    public static string Dump(FrameworkElement root)
    {
        var sb = new System.Text.StringBuilder();
        DumpWalk(root, 0, sb);
        return sb.ToString();
    }

    private static void Walk(DependencyObject node, string page, List<string> findings, ref int toggles)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement fe)
            {
                // Visibility.Collapsed subtrees are not rendered: nothing to check.
                if (fe.Visibility != Visibility.Visible) continue;

                if (fe is ToggleSwitch) toggles++;

                if (IsInteractive(fe) && fe.ActualWidth <= 0 && fe.ActualHeight <= 0)
                    findings.Add($"{page}: interactive {Describe(fe)} renders at zero size");

                if (IsNamedInteractive(fe) && string.IsNullOrWhiteSpace(AccessibleName(fe)))
                    findings.Add($"{page}: interactive {Describe(fe)} has no accessible name");
            }
            Walk(child, page, findings, ref toggles);
        }
    }

    private static void DumpWalk(DependencyObject node, int depth, System.Text.StringBuilder sb)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is not FrameworkElement fe) continue;
            if (fe.Visibility != Visibility.Visible) continue;
            var pad = new string(' ', Math.Min(depth, 24) * 2);
            var name = AccessibleName(fe);
            var namePart = string.IsNullOrEmpty(name) ? "" : $" \"{Short(name)}\"";
            sb.AppendLine($"{pad}{fe.GetType().Name}{namePart} [{fe.ActualWidth:0}x{fe.ActualHeight:0}]");
            DumpWalk(child, depth + 1, sb);
        }
    }

    private static bool IsInteractive(FrameworkElement fe) =>
        fe is ButtonBase or ToggleSwitch or CheckBox or ComboBox or NumberBox or TextBox;

    /// <summary>Buttons/toggles/checkboxes must carry an accessible name (Tab reachability + screen readers).</summary>
    private static bool IsNamedInteractive(FrameworkElement fe) =>
        fe is ButtonBase or ToggleSwitch or CheckBox;

    private static string AccessibleName(FrameworkElement fe)
    {
        var name = AutomationProperties.GetName(fe);
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return fe switch
        {
            Button b when b.Content is string text => text,
            ToggleSwitch t when t.Content is string text => text,
            CheckBox c when c.Content is string text => text,
            _ => "",
        };
    }

    private static string Describe(FrameworkElement fe)
    {
        var name = AccessibleName(fe);
        var type = fe.GetType().Name;
        return string.IsNullOrWhiteSpace(name) ? type : $"{type}(\"{Short(name)}\")";
    }

    private static string Short(string text) => text.Length <= 30 ? text : text[..30] + "\u2026";
}
