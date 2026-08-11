using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SuperDucker.App;

/// <summary>
/// 一个水平方向自动换行，且每一行在可用宽度内水平居中的面板。
/// 行为与 WrapPanel 类似，但行尾剩余空间会平均分配到左右两侧，
/// 避免出现左侧留白很窄、右侧留白很宽的不对称视觉。
/// </summary>
public class CenteredWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(double.PositiveInfinity, double.PositiveInfinity);

        double x = 0;
        double y = 0;
        double rowHeight = 0;
        double desiredWidth = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childConstraint);
            var childSize = child.DesiredSize;

            if (x + childSize.Width > availableSize.Width && x > 0)
            {
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }

            x += childSize.Width;
            if (x > desiredWidth) desiredWidth = x;
            if (childSize.Height > rowHeight) rowHeight = childSize.Height;
        }

        y += rowHeight;
        return new Size(desiredWidth, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        double y = 0;
        double rowHeight = 0;
        var row = new List<UIElement>();

        foreach (UIElement child in InternalChildren)
        {
            var childSize = child.DesiredSize;

            if (x + childSize.Width > finalSize.Width && x > 0)
            {
                ArrangeRow(row, y, rowHeight, finalSize.Width);
                row.Clear();
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }

            row.Add(child);
            x += childSize.Width;
            if (childSize.Height > rowHeight) rowHeight = childSize.Height;
        }

        if (row.Count > 0)
            ArrangeRow(row, y, rowHeight, finalSize.Width);

        return finalSize;
    }

    private static void ArrangeRow(List<UIElement> row, double y, double rowHeight, double finalWidth)
    {
        double rowWidth = 0;
        foreach (var child in row)
            rowWidth += child.DesiredSize.Width;

        double startX = Math.Max(0, (finalWidth - rowWidth) / 2);
        double x = startX;

        foreach (var child in row)
        {
            var childSize = child.DesiredSize;
            child.Arrange(new Rect(x, y, childSize.Width, rowHeight));
            x += childSize.Width;
        }
    }
}
