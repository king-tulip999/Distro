using System.Windows;
using System.Windows.Controls;

namespace Core.Views;

internal sealed class TabRow : Panel
{
    private const double TightestPad = 6;
    private double _widestPad = -1;

    protected override Size MeasureOverride(Size available)
    {
        var tabs = InternalChildren.OfType<Control>().ToList();
        if (tabs.Count == 0) return new Size(0, 0);
        if (_widestPad < 0) _widestPad = tabs[0].Padding.Left;
        var free = new Size(double.PositiveInfinity, available.Height);
        foreach (var tab in tabs) tab.Measure(free);
        if (!double.IsInfinity(available.Width))
        {
            double fixedWidth = tabs.Sum(t => t.DesiredSize.Width - t.Padding.Left - t.Padding.Right);
            double pad = Math.Floor(Math.Clamp((available.Width - fixedWidth) / (2 * tabs.Count), TightestPad, _widestPad));
            foreach (var tab in tabs)
            {
                if (tab.Padding.Left == pad) continue;
                tab.Padding = new Thickness(pad, tab.Padding.Top, pad, tab.Padding.Bottom);
                tab.Measure(free);
            }
        }
        double width = tabs.Sum(t => t.DesiredSize.Width), height = tabs.Max(t => t.DesiredSize.Height);
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, final.Height));
            x += child.DesiredSize.Width;
        }
        return final;
    }
}
