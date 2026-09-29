namespace MaxDpsCompanion;

/// <summary>
/// Measured-layout primitives (v2.8). WinForms docking inside flow containers
/// is unsupported and silently collapses content to zero width/height (the
/// v2.7.0 empty-Home-card defect), so pages compose from two deterministic
/// containers instead:
///
///  * <see cref="VertStack"/> — top-down stack; every child gets the stack
///    width and its <see cref="UiMeasure.Height"/>.
///  * <see cref="WrapFlow"/> — left-to-right flow that wraps; children keep
///    their measured width.
///
/// Both implement <see cref="IUiMeasured"/> so they nest (a stack can contain
/// stacks and cards). Nothing in this file uses Dock for positioning.
/// </summary>
internal interface IUiMeasured
{
    /// <summary>Height this control needs at the given width (0 = use measured bounds).</summary>
    int MeasuredHeight(int width);
}

/// <summary>Shared measurement helpers.</summary>
internal static class UiMeasure
{
    /// <summary>The height a control needs when laid out at <paramref name="width"/>.</summary>
    public static int Height(Control control, int width)
    {
        if (control is IUiMeasured m) return Math.Max(m.MeasuredHeight(width), control.MinimumSize.Height);
        if (control is Label label)
        {
            var wrap = label.AutoSize || IsStackOwned(label);
            if (wrap)
            {
                // Re-measure on every layout: a container-owned label's stored
                // height is only valid for the width it was measured at (the
                // v2.8 stale-height gap between rows).
                var max = label.MaximumSize.Width > 0 ? Math.Min(label.MaximumSize.Width, width) : width;
                var textSize = TextRenderer.MeasureText(
                    label.Text ?? "", label.Font, new Size(Math.Max(10, max), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return Math.Max(textSize.Height, control.MinimumSize.Height);
            }
            return Math.Max(label.Height, control.MinimumSize.Height);
        }
        var preferred = control.GetPreferredSize(new Size(Math.Max(1, width), 0));
        var height = preferred.Height > 0 ? preferred.Height : control.Height;
        return Math.Max(height, control.MinimumSize.Height);
    }

    /// <summary>The width a flow child wants (its measured width, never stretched).</summary>
    public static int Width(Control control)
    {
        if (control.MinimumSize.Width > 0) return Math.Max(control.Width, control.MinimumSize.Width);
        if (control.AutoSize)
        {
            var preferred = control.GetPreferredSize(Size.Empty);
            if (preferred.Width > 0) return preferred.Width;
        }
        return control.Width;
    }

    /// <summary>
    /// Converts a wrapping AutoSize label into a container-owned (fixed) label
    /// on first layout. An AutoSize label resets its own bounds after the
    /// container assigns them, which produced the v2.8 "label overflows card
    /// body" race; the container now owns width+height while the measured
    /// height keeps the same wrapping rules.
    /// </summary>
    /// <summary>Tag marking a wrapping label as container-owned (see <see cref="Prepare"/>).</summary>
    public const string StackOwned = "ui-stack-owned";

    /// <summary>True when <see cref="Prepare"/> took ownership of the label's size.</summary>
    public static bool IsStackOwned(Control control) => control.Tag as string == StackOwned;

    public static void Prepare(Control control)
    {
        if (control is not Label label || !label.AutoSize || label.MaximumSize.Width <= 0) return;
        label.AutoSize = false;
        label.MaximumSize = Size.Empty;
        label.Tag = StackOwned;
    }
}

/// <summary>Top-down stack with measured children. No Dock, no absolute row heights.</summary>
internal sealed class VertStack : Panel, IUiMeasured
{
    public int Gap { get; set; } = DesignTokens.SpaceS;

    public VertStack()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(10, width - Padding.Horizontal);
        var height = Padding.Vertical;
        for (var i = 0; i < Controls.Count; i++)
        {
            if (height > Padding.Top) height += Gap;
            height += UiMeasure.Height(Controls[i], inner);
        }
        return height;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var inner = Math.Max(10, ClientSize.Width - Padding.Horizontal);
        var y = Padding.Top;
        var first = true;
        foreach (Control child in Controls)
        {
            if (!first) y += Gap;
            first = false;
            var h = UiMeasure.Height(child, inner);
            UiMeasure.Prepare(child);
            child.Bounds = new Rectangle(Padding.Left, y, inner, h);
            y += h;
        }
    }
}

/// <summary>Left-to-right flow with wrapping. Children keep their measured width.</summary>
internal sealed class WrapFlow : Panel, IUiMeasured
{
    public int Gap { get; set; } = DesignTokens.SpaceS;
    public int RowGap { get; set; } = DesignTokens.SpaceS;

    public WrapFlow()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(10, width - Padding.Horizontal);
        var y = Padding.Top;
        var rowHeight = 0;
        var x = Padding.Left;
        var any = false;
        foreach (Control child in Controls)
        {
            var w = UiMeasure.Width(child);
            var h = UiMeasure.Height(child, Math.Max(10, w));
            if (any && x + w > Padding.Left + inner)
            {
                y += rowHeight + RowGap;
                x = Padding.Left;
                rowHeight = 0;
            }
            any = true;
            x += w + Gap;
            rowHeight = Math.Max(rowHeight, h);
        }
        if (any) y += rowHeight;
        return y + Padding.Bottom;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var inner = Math.Max(10, ClientSize.Width - Padding.Horizontal);
        var x = Padding.Left;
        var y = Padding.Top;
        var rowHeight = 0;
        var any = false;
        foreach (Control child in Controls)
        {
            var w = Math.Min(UiMeasure.Width(child), inner);
            var h = UiMeasure.Height(child, Math.Max(10, w));
            if (any && x + w > Padding.Left + inner)
            {
                y += rowHeight + RowGap;
                x = Padding.Left;
                rowHeight = 0;
            }
            any = true;
            UiMeasure.Prepare(child);
            child.Bounds = new Rectangle(x, y, w, h);
            x += w + Gap;
            rowHeight = Math.Max(rowHeight, h);
        }
    }
}

/// <summary>
/// Equal-width column grid with measured row heights (the MainForm TwoCol /
/// ButtonsRow replacement). Cells fill left-to-right; each row is as tall as
/// its tallest measured cell.
/// </summary>
internal sealed class GridPanel : Panel, IUiMeasured
{
    public int Gap { get; set; } = DesignTokens.SpaceS;
    public int RowGap { get; set; } = DesignTokens.SpaceS;

    private readonly int _columns;

    public GridPanel(int columns)
    {
        _columns = Math.Max(1, columns);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    private int Rows => (Controls.Count + _columns - 1) / _columns;

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(10, width - Padding.Horizontal);
        var cellWidth = Math.Max(10, (inner - Gap * (_columns - 1)) / _columns);
        var height = Padding.Vertical;
        for (var row = 0; row < Rows; row++)
        {
            var rowHeight = 0;
            for (var col = 0; col < _columns; col++)
            {
                var index = row * _columns + col;
                if (index >= Controls.Count) break;
                rowHeight = Math.Max(rowHeight, UiMeasure.Height(Controls[index], cellWidth));
            }
            if (row > 0) height += RowGap;
            height += rowHeight;
        }
        return height;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var inner = Math.Max(10, ClientSize.Width - Padding.Horizontal);
        var cellWidth = Math.Max(10, (inner - Gap * (_columns - 1)) / _columns);
        var y = Padding.Top;
        for (var row = 0; row < Rows; row++)
        {
            var rowHeight = 0;
            for (var col = 0; col < _columns; col++)
            {
                var index = row * _columns + col;
                if (index >= Controls.Count) break;
                rowHeight = Math.Max(rowHeight, UiMeasure.Height(Controls[index], cellWidth));
            }
            for (var col = 0; col < _columns; col++)
            {
                var index = row * _columns + col;
                if (index >= Controls.Count) break;
                Controls[index].Bounds = new Rectangle(Padding.Left + col * (cellWidth + Gap), y, cellWidth, rowHeight);
            }
            y += rowHeight + RowGap;
        }
    }
}

/// <summary>
/// Responsive asymmetric split (the bento adaptation for Home): two measured
/// columns while wide, a single stacked column when narrow. Children are
/// distributed by the owner; no Dock anywhere.
/// </summary>
internal sealed class BentoSplit : Panel, IUiMeasured
{
    /// <summary>Below this client width the split collapses to one column.</summary>
    public int CollapseWidth { get; set; } = 980;

    public int ColumnGap { get; set; } = DesignTokens.SpaceL;
    public int RowGap { get; set; } = DesignTokens.SpaceL;

    public VertStack Left { get; } = new();
    public VertStack Right { get; } = new();
    public VertStack Full { get; } = new();

    public BentoSplit()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Controls.Add(Left);
        Controls.Add(Right);
        Controls.Add(Full);
    }

    public int MeasuredHeight(int width)
    {
        var inner = Math.Max(10, width - Padding.Horizontal);
        var gap = ColumnGap;
        var stacked = inner < CollapseWidth;
        int leftH, rightH, fullH;
        if (stacked)
        {
            leftH = Left.MeasuredHeight(inner);
            rightH = Right.MeasuredHeight(inner);
            fullH = Full.MeasuredHeight(inner);
            var total = leftH + rightH + fullH;
            if (leftH > 0 && rightH > 0) total += RowGap;
            if (fullH > 0 && (leftH > 0 || rightH > 0)) total += RowGap;
            return Padding.Vertical + total;
        }
        var leftW = Math.Max(200, (int)((inner - gap) * 0.58));
        var rightW = Math.Max(200, inner - gap - leftW);
        leftH = Left.MeasuredHeight(leftW);
        rightH = Right.MeasuredHeight(rightW);
        fullH = Full.MeasuredHeight(inner);
        var max = Math.Max(leftH, rightH);
        var total2 = max + fullH;
        if (fullH > 0 && max > 0) total2 += RowGap;
        return Padding.Vertical + total2;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var inner = Math.Max(10, ClientSize.Width - Padding.Horizontal);
        var stacked = inner < CollapseWidth;
        var leftW = stacked ? inner : Math.Max(200, (int)((inner - ColumnGap) * 0.58));
        var rightW = stacked ? inner : Math.Max(200, inner - ColumnGap - leftW);

        var leftH = Left.MeasuredHeight(leftW);
        var rightH = Right.MeasuredHeight(rightW);
        if (stacked)
        {
            var y = Padding.Top;
            Left.Bounds = new Rectangle(Padding.Left, y, leftW, leftH);
            y += leftH;
            if (rightH > 0 && leftH > 0) y += RowGap;
            Right.Bounds = new Rectangle(Padding.Left, y, rightW, rightH);
            y += rightH;
            var fullH = Full.MeasuredHeight(inner);
            if (fullH > 0 && (leftH > 0 || rightH > 0)) y += RowGap;
            Full.Bounds = new Rectangle(Padding.Left, y, inner, fullH);
        }
        else
        {
            var max = Math.Max(leftH, rightH);
            Left.Bounds = new Rectangle(Padding.Left, Padding.Top, leftW, max);
            Right.Bounds = new Rectangle(Padding.Left + leftW + ColumnGap, Padding.Top, rightW, max);
            var fullH = Full.MeasuredHeight(inner);
            var y = Padding.Top + max;
            if (fullH > 0 && max > 0) y += RowGap;
            Full.Bounds = new Rectangle(Padding.Left, y, inner, fullH);
        }
    }
}
