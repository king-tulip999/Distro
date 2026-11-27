using System.Windows;
using System.Windows.Controls;

namespace Core.Views;

internal sealed class TabBarRow : Panel
{
    private const double StackedTop = 8, StackedGap = 2;
    private bool _stacked;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count < 2) return new Size(0, 0);
        UIElement tabs = InternalChildren[0], tools = InternalChildren[1];
        double height = available.Height;
        tools.Measure(new Size(double.PositiveInfinity, height));
        double toolsWidth = tools.DesiredSize.Width;
        if (double.IsInfinity(available.Width))
        {
            _stacked = false;
            tabs.Measure(available);
            return new Size(tabs.DesiredSize.Width + toolsWidth, Math.Max(tabs.DesiredSize.Height, tools.DesiredSize.Height));
        }
        double room = available.Width - toolsWidth;
        _stacked = !Fits(tabs, room, height);
        if (!_stacked)
        {
            tabs.Measure(new Size(room, height));
            return new Size(available.Width, Math.Max(tabs.DesiredSize.Height, tools.DesiredSize.Height));
        }
        tools.Measure(new Size(available.Width, height));
        tabs.Measure(new Size(available.Width, height));
        return new Size(available.Width, StackedTop + tools.DesiredSize.Height + StackedGap + tabs.DesiredSize.Height);
    }

    private static bool Fits(UIElement tabs, double room, double height)
    {
        if (room <= 0) return false;
        tabs.Measure(new Size(double.PositiveInfinity, height));
        UIElement inner = tabs;
        while (inner is Decorator { Child: { } child }) inner = child;
        if (inner is not TabRow row) return tabs.DesiredSize.Width <= room + 0.5;
        double chrome = tabs.DesiredSize.Width - row.DesiredSize.Width;
        return row.TightestWidth + chrome <= room + 0.5;
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count < 2) return final;
        UIElement tabs = InternalChildren[0], tools = InternalChildren[1];
        double toolsWidth = Math.Min(tools.DesiredSize.Width, final.Width);
        if (!_stacked)
        {
            tabs.Arrange(new Rect(0, 0, tabs.DesiredSize.Width, final.Height));
            tools.Arrange(new Rect(final.Width - toolsWidth, 0, toolsWidth, final.Height));
            return final;
        }
        tools.Arrange(new Rect(final.Width - toolsWidth, StackedTop, toolsWidth, tools.DesiredSize.Height));
        double tabsTop = StackedTop + tools.DesiredSize.Height + StackedGap;
        tabs.Arrange(new Rect(0, tabsTop, tabs.DesiredSize.Width, Math.Max(0, final.Height - tabsTop)));
        return final;
    }
}
