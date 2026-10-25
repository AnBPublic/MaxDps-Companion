using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WSize = Windows.Foundation.Size;
using WRect = Windows.Foundation.Rect;

namespace MaxDpsCompanion;

/// <summary>
/// P2-B WinUI port of the measured-layout primitives (old Ui/Layout.cs).
/// WinForms docking inside flow containers collapsed content, so pages compose
/// from deterministic containers. In WinUI the stock StackPanel/Grid cover most
/// of this; the ports keep the old class names and geometry so P2-C/D keep the
/// same composition language.
/// </summary>
public sealed class VertStack : StackPanel
{
    /// <summary>Old VertStack.Gap; maps onto the stock Spacing.</summary>
    public double Gap
    {
        get => Spacing;
        set => Spacing = value;
    }

    public VertStack()
    {
        Spacing = DesignTokens.SpaceS; // old default gap 8
    }
}

/// <summary>
/// Left-to-right flow that wraps (old Ui/Layout.cs WrapFlow). Children keep
/// their desired width; the row height is the tallest child in the row.
/// </summary>
public sealed class WrapFlow : Panel
{
    private double _gap = DesignTokens.SpaceS;
    private double _rowGap = DesignTokens.SpaceS;

    public double Gap
    {
        get => _gap;
        set { _gap = value; InvalidateMeasure(); }
    }

    public double RowGap
    {
        get => _rowGap;
        set { _rowGap = value; InvalidateMeasure(); }
    }

    protected override WSize MeasureOverride(WSize availableSize)
    {
        var maxWidth = availableSize.Width;
        double x = 0, y = 0, rowHeight = 0;
        var any = false;
        foreach (var child in Children)
        {
            child.Measure(new WSize(double.PositiveInfinity, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (any && x + size.Width > maxWidth)
            {
                y += rowHeight + RowGap;
                x = 0;
                rowHeight = 0;
            }
            any = true;
            x += size.Width + Gap;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        var width = double.IsInfinity(maxWidth) ? x : maxWidth;
        return new WSize(Math.Max(0, width), y + rowHeight);
    }

    protected override WSize ArrangeOverride(WSize finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        var any = false;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (any && x + size.Width > finalSize.Width)
            {
                y += rowHeight + RowGap;
                x = 0;
                rowHeight = 0;
            }
            any = true;
            child.Arrange(new WRect(x, y, size.Width, size.Height));
            x += size.Width + Gap;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return finalSize;
    }
}

/// <summary>
/// Equal-width column grid with measured row heights (old Ui/Layout.cs
/// GridPanel; the MainForm TwoCol / ButtonsRow replacement).
/// </summary>
public sealed class GridPanel : Panel
{
    private readonly int _columns;

    public double Gap { get; set; } = DesignTokens.SpaceS;
    public double RowGap { get; set; } = DesignTokens.SpaceS;

    public GridPanel(int columns)
    {
        _columns = Math.Max(1, columns);
    }

    public int Columns => _columns;

    private int RowCount => (_columns <= 0 ? 0 : (Children.Count + _columns - 1) / _columns);

    protected override WSize MeasureOverride(WSize availableSize)
    {
        var inner = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var cellWidth = Math.Max(10, (inner - Gap * (_columns - 1)) / _columns);
        double height = 0;
        for (var row = 0; row < RowCount; row++)
        {
            double rowHeight = 0;
            for (var col = 0; col < _columns; col++)
            {
                var index = row * _columns + col;
                if (index >= Children.Count) break;
                Children[index].Measure(new WSize(cellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
            }
            if (row > 0) height += RowGap;
            height += rowHeight;
        }
        return new WSize(inner, height);
    }

    protected override WSize ArrangeOverride(WSize finalSize)
    {
        var cellWidth = Math.Max(10, (finalSize.Width - Gap * (_columns - 1)) / _columns);
        double y = 0;
        for (var row = 0; row < RowCount; row++)
        {
            double rowHeight = 0;
            for (var col = 0; col < _columns; col++)
            {
                var index = row * _columns + col;
                if (index >= Children.Count) break;
                rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
            }
            for (var col = 0; col < _columns; col++)
            {
                var index = row * _columns + col;
                if (index >= Children.Count) break;
                Children[index].Arrange(new WRect(col * (cellWidth + Gap), y, cellWidth, rowHeight));
            }
            y += rowHeight + RowGap;
        }
        return finalSize;
    }
}

/// <summary>
/// Responsive side-by-side pair (4.5.1): equal columns while the container is
/// wide, a single stacked column below <see cref="StackWidth"/>. Used by the
/// classic toggle rows and the console cards so a narrow window never squeezes
/// two segments into a column that cannot hold them.
/// </summary>
public sealed class ResponsiveColumns : Panel
{
    public double Gap { get; set; } = DesignTokens.SpaceS;

    /// <summary>Below this width the children stack vertically.</summary>
    public double StackWidth { get; set; } = 620;

    public bool IsStacked { get; private set; }

    protected override WSize MeasureOverride(WSize availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? StackWidth : availableSize.Width;
        var stacked = width < StackWidth;
        IsStacked = stacked;
        var count = Math.Max(1, Children.Count);

        if (stacked)
        {
            double height = 0;
            foreach (var child in Children)
            {
                child.Measure(new WSize(width, double.PositiveInfinity));
                height += child.DesiredSize.Height;
            }
            return new WSize(width, height + Gap * (Children.Count - 1));
        }

        var cell = Math.Max(10, (width - Gap * (count - 1)) / count);
        double max = 0;
        foreach (var child in Children)
        {
            child.Measure(new WSize(cell, double.PositiveInfinity));
            max = Math.Max(max, child.DesiredSize.Height);
        }
        return new WSize(width, max);
    }

    protected override WSize ArrangeOverride(WSize finalSize)
    {
        var stacked = finalSize.Width < StackWidth;
        var count = Math.Max(1, Children.Count);
        var cell = Math.Max(10, (finalSize.Width - Gap * (count - 1)) / count);

        if (!stacked)
        {
            for (var i = 0; i < Children.Count; i++)
                Children[i].Arrange(new WRect(i * (cell + Gap), 0, cell, finalSize.Height));
            return finalSize;
        }

        double y = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var height = Children[i].DesiredSize.Height;
            Children[i].Arrange(new WRect(0, y, finalSize.Width, height));
            y += height + Gap;
        }
        return finalSize;
    }
}
/// <summary>
/// Responsive asymmetric split (old Ui/Layout.cs BentoSplit): two measured
/// columns while wide, a single stacked column when narrow. Children go into
/// <see cref="Left"/>, <see cref="Right"/>, <see cref="Full"/>.
/// </summary>
public sealed class BentoSplit : Panel
{
    /// <summary>Below this client width the split collapses to one column.</summary>
    public int CollapseWidth { get; set; } = 980;

    public double ColumnGap { get; set; } = DesignTokens.SpaceL;
    public double RowGap { get; set; } = DesignTokens.SpaceL;

    public VertStack Left { get; } = new();
    public VertStack Right { get; } = new();
    public VertStack Full { get; } = new();

    public BentoSplit()
    {
        Children.Add(Left);
        Children.Add(Right);
        Children.Add(Full);
    }

    protected override WSize MeasureOverride(WSize availableSize)
    {
        var inner = double.IsInfinity(availableSize.Width) ? CollapseWidth : availableSize.Width;
        var stacked = inner < CollapseWidth;
        double leftWidth, rightWidth;
        if (stacked)
        {
            leftWidth = rightWidth = inner;
        }
        else
        {
            leftWidth = Math.Max(200, (inner - ColumnGap) * 0.58);
            rightWidth = Math.Max(200, inner - ColumnGap - leftWidth);
        }

        Left.Measure(new WSize(leftWidth, double.PositiveInfinity));
        Right.Measure(new WSize(rightWidth, double.PositiveInfinity));
        Full.Measure(new WSize(inner, double.PositiveInfinity));

        var leftHeight = Left.DesiredSize.Height;
        var rightHeight = Right.DesiredSize.Height;
        var fullHeight = Full.DesiredSize.Height;
        double total;
        if (stacked)
        {
            total = leftHeight + rightHeight + fullHeight;
            if (leftHeight > 0 && rightHeight > 0) total += RowGap;
            if (fullHeight > 0 && (leftHeight > 0 || rightHeight > 0)) total += RowGap;
        }
        else
        {
            var max = Math.Max(leftHeight, rightHeight);
            total = max + fullHeight;
            if (fullHeight > 0 && max > 0) total += RowGap;
        }
        return new WSize(inner, total);
    }

    protected override WSize ArrangeOverride(WSize finalSize)
    {
        var stacked = finalSize.Width < CollapseWidth;
        var leftWidth = stacked ? finalSize.Width : Math.Max(200, (finalSize.Width - ColumnGap) * 0.58);
        var rightWidth = stacked ? finalSize.Width : Math.Max(200, finalSize.Width - ColumnGap - leftWidth);
        var leftHeight = Left.DesiredSize.Height;
        var rightHeight = Right.DesiredSize.Height;
        var fullHeight = Full.DesiredSize.Height;

        if (stacked)
        {
            double y = 0;
            Left.Arrange(new WRect(0, y, leftWidth, leftHeight));
            y += leftHeight;
            if (rightHeight > 0 && leftHeight > 0) y += RowGap;
            Right.Arrange(new WRect(0, y, rightWidth, rightHeight));
            y += rightHeight;
            if (fullHeight > 0 && (leftHeight > 0 || rightHeight > 0)) y += RowGap;
            Full.Arrange(new WRect(0, y, finalSize.Width, fullHeight));
        }
        else
        {
            var max = Math.Max(leftHeight, rightHeight);
            Left.Arrange(new WRect(0, 0, leftWidth, max));
            Right.Arrange(new WRect(leftWidth + ColumnGap, 0, rightWidth, max));
            var y = max;
            if (fullHeight > 0 && max > 0) y += RowGap;
            Full.Arrange(new WRect(0, y, finalSize.Width, fullHeight));
        }
        return finalSize;
    }
}
