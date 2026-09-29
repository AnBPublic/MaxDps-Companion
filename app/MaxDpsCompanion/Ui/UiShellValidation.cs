namespace MaxDpsCompanion;

/// <summary>
/// A control that draws its own text and can report the width that text needs
/// (including its own padding). The smoke test uses this to prove owner-drawn
/// primitives never clip their labels — a bug class plain
/// <see cref="Label"/> checks cannot see (the v2.7.0 pills).
/// </summary>
internal interface IUiTextFit
{
    /// <summary>Minimum control width that shows the full text without clipping.</summary>
    int RequiredTextWidth { get; }

    /// <summary>Human-readable text being measured (for the finding message).</summary>
    string FittedText { get; }
}

/// <summary>
/// A control whose content panel must actually render its children inside the
/// panel bounds (the v2.7.0 empty-card guard).
/// </summary>
internal interface IUiContentHost
{
    /// <summary>The panel that owns the card content.</summary>
    Control ContentBody { get; }

    /// <summary>True when an empty body is intentional (none by default).</summary>
    bool AllowEmptyContent { get; }
}

/// <summary>
/// Headless structural checks for the UI smoke test (v2.7 §47, v2.8 hardened).
/// These assert layout invariants that a screenshot cannot:
///  * no visible zero-size control;
///  * no overlapping interactive siblings;
///  * non-autosize labels fit their measured text;
///  * owner-drawn primitives (pills, tiles, chips, cards, rail items) fit
///    their measured text — the class of bug that produced the clipped pills;
///  * every card body has rendered content inside its bounds — the class of
///    bug that produced the empty Home cards;
///  * interactive controls are Tab-reachable and carry accessible names.
/// Findings are printed and make the smoke test fail, so the check is
/// meaningful rather than a no-op.
/// </summary>
internal static class UiShellValidation
{
    public static List<string> Validate(Control root, string page)
    {
        var findings = new List<string>();
        Walk(root, page, findings);
        return findings;
    }

    /// <summary>
    /// Diagnostic bounds tree (ui-smoke-layout.txt): every visible control with
    /// its type, text, bounds and auto-size state. Used to diagnose layout
    /// defects that screenshots alone cannot localise.
    /// </summary>
    public static string Dump(Control root)
    {
        var sb = new System.Text.StringBuilder();
        DumpWalk(root, 0, sb);
        return sb.ToString();
    }

    private static void DumpWalk(Control control, int depth, System.Text.StringBuilder sb)
    {
        var pad = new string(' ', Math.Min(depth, 24) * 2);
        var text = string.IsNullOrEmpty(control.Text) ? "" : $" \"{Short(control.Text)}\"";
        var auto = control is IUiTextFit ? " fit=" + ((IUiTextFit)control).RequiredTextWidth : "";
        var vis = control.Visible ? "" : " hidden";
        sb.AppendLine($"{pad}{control.GetType().Name}{text} [{control.Left},{control.Top} {control.Width}x{control.Height}]" +
                      $"{(control.AutoSize ? " auto" : "")}{auto}{vis}");
        foreach (Control child in control.Controls) DumpWalk(child, depth + 1, sb);
    }

    private static void Walk(Control parent, string page, List<string> findings)
    {
        // NOTE: do NOT gate on Control.Visible — the smoke test builds the
        // form headlessly (never shown), so Visible is false for the whole
        // tree and a visibility gate made every check vacuous in v2.7.0.
        var children = new List<Control>();
        foreach (Control child in parent.Controls)
        {
            children.Add(child);
            Check(child, page, findings);
            if (child.HasChildren) Walk(child, page, findings);
        }
        CheckOverlap(children, page, findings);
    }

    private static void Check(Control control, string page, List<string> findings)
    {
        if (control is Form) return;
        if (control.Width <= 0 || control.Height <= 0)
        {
            findings.Add($"{page}: '{Describe(control)}' has zero size ({control.Width}x{control.Height})");
            return; // deeper checks are meaningless on a collapsed control
        }
        if (control.Left < -8 || control.Top < -8)
            findings.Add($"{page}: '{Describe(control)}' has negative layout ({control.Left},{control.Top})");

        CheckTextFit(control, page, findings);
        CheckContentHost(control, page, findings);

        if (control is Label label && !label.AutoSize)
        {
            var text = label.Text ?? "";
            if (text.Length > 0 && !text.Contains('\n'))
            {
                if (UiMeasure.IsStackOwned(label))
                {
                    // Container-owned wrapping label: a single-line overflow is
                    // expected; the real clip test is whether the assigned
                    // height covers the wrapped text at the assigned width.
                    var wrapped = TextRenderer.MeasureText(text, label.Font,
                        new Size(Math.Max(10, label.Width), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    if (wrapped.Height > label.Height + 2)
                        findings.Add($"{page}: label '{Short(text)}' clips wrapped text " +
                                     $"({wrapped.Height}px needed > {label.Height}px)");
                }
                else
                {
                    var measured = TextRenderer.MeasureText(text, label.Font);
                    if (measured.Width > label.Width + 2 && !label.AutoEllipsis)
                        findings.Add($"{page}: label '{Short(text)}' overflows ({measured.Width}px > {label.Width}px)");
                }
            }
        }

        if (IsInteractive(control) && control.Enabled)
        {
            if (!control.TabStop)
                findings.Add($"{page}: interactive '{Describe(control)}' is not reachable by Tab");
            if (string.IsNullOrWhiteSpace(control.AccessibleName) && string.IsNullOrWhiteSpace(control.Text))
                findings.Add($"{page}: interactive '{Describe(control)}' has no accessible name");
        }
    }

    /// <summary>Owner-drawn primitives must report and honour their required text width.</summary>
    private static void CheckTextFit(Control control, string page, List<string> findings)
    {
        if (control is not IUiTextFit fit) return;
        if (string.IsNullOrEmpty(fit.FittedText)) return;
        if (control.Width + 2 < fit.RequiredTextWidth)
        {
            findings.Add(
                $"{page}: '{Describe(control)}' clips its text ('{Short(fit.FittedText)}': " +
                $"{fit.RequiredTextWidth}px needed > {control.Width}px)");
        }
    }

    /// <summary>
    /// A content card must render its children inside the body panel. The
    /// empty-Home-card bug was a zero-height subtree that all other checks
    /// missed because the parent panels still had size.
    /// </summary>
    private static void CheckContentHost(Control control, string page, List<string> findings)
    {
        if (control is not IUiContentHost host || host.AllowEmptyContent) return;
        var body = host.ContentBody;
        var rendered = 0;
        CheckBodyRecursive(body, body, page, findings, ref rendered);
        if (rendered == 0)
            findings.Add($"{page}: card '{Describe(control)}' has no rendered content in its body");
    }

    private static void CheckBodyRecursive(Control root, Control current, string page, List<string> findings, ref int rendered)
    {
        foreach (Control child in current.Controls)
        {
            if (child.Width <= 0 || child.Height <= 0)
            {
                if (!string.IsNullOrEmpty(child.Text))
                    findings.Add($"{page}: collapsed content '{Describe(child)}' in card '{Describe(root)}'");
                continue;
            }
            rendered++;
            if (current == root)
            {
                // Direct content must sit inside the body's client area.
                var bounds = child.Bounds;
                var client = root.ClientRectangle;
                if (bounds.Right > client.Right + 1 || bounds.Bottom > client.Bottom + 1)
                {
                    findings.Add(
                        $"{page}: content '{Describe(child)}' overflows card body " +
                        $"({bounds.Right}x{bounds.Bottom} > {client.Right}x{client.Bottom})");
                }
            }
            if (child.HasChildren) CheckBodyRecursive(root, child, page, findings, ref rendered);
        }
    }

    private static bool IsInteractive(Control control)
        => control is IButtonControl or ToggleSwitch or TextBox or NumericUpDown or ComboBox or ListBox
           || control is NavRailItem or FilterChip
           || control is MetricTile { Clickable: true };

    private static void CheckOverlap(List<Control> siblings, string page, List<string> findings)
    {
        for (var i = 0; i < siblings.Count; i++)
        {
            for (var j = i + 1; j < siblings.Count; j++)
            {
                var a = siblings[i];
                var b = siblings[j];
                if (a is VScrollBar or HScrollBar || b is VScrollBar or HScrollBar) continue;
                if (a.Width == 0 || a.Height == 0 || b.Width == 0 || b.Height == 0) continue;
                if (a.Dock == DockStyle.Fill && b.Dock == DockStyle.Fill) continue;
                if (a is ToastHost || b is ToastHost) continue;
                var overlap = Rectangle.Intersect(a.Bounds, b.Bounds);
                if (overlap.Width > 4 && overlap.Height > 4)
                    findings.Add($"{page}: '{Describe(a)}' overlaps '{Describe(b)}' ({overlap.Width}x{overlap.Height})");
            }
        }
    }

    private static string Describe(Control control)
    {
        var type = control.GetType().Name;
        var text = control.Text;
        return string.IsNullOrWhiteSpace(text) ? type : $"{type}(\"{Short(text)}\")";
    }

    private static string Short(string text) => text.Length <= 30 ? text : text[..30] + "\u2026";
}
