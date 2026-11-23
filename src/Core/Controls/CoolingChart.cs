using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Core.Theme;

namespace Core.Controls;

internal sealed record CoolingSeries(string Name, IReadOnlyList<(int Percent, double Temp)> Points, bool Main);

internal sealed class CoolingChart : FrameworkElement
{
    private IReadOnlyList<CoolingSeries> _series = Array.Empty<CoolingSeries>();
    private int? _knee, _tooHot;
    private IReadOnlySet<int> _worked = new HashSet<int>();
    private string _unit = "°C";

    public CoolingChart()
    {
        MinHeight = 170;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public void Show(IReadOnlyList<CoolingSeries> series, int? knee, int? tooHot, IReadOnlySet<int>? worked = null, string unit = "°C")
    {
        _unit = unit;
        _worked = worked ?? new HashSet<int>();
        _series = series;
        _knee = knee;
        _tooHot = tooHot;
        InvalidateVisual();
    }

    private static FormattedText Text(string s, double size, Brush brush, double dpi, bool bold = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, ThemeManager.UiFace(bold), size, brush, dpi);

    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var plot = new Rect(40, 18, Math.Max(8, ActualWidth - 40 - 10), Math.Max(8, ActualHeight - 18 - 22));
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var all = _series.SelectMany(s => s.Points).ToList();
        if (all.Count == 0) return;

        int minPercent = Math.Min(all.Min(p => p.Percent), _tooHot ?? 100);
        minPercent = Math.Max(0, (minPercent - 5) / 10 * 10);
        double lo = Math.Floor(all.Min(p => p.Temp) - 1.5), hi = Math.Ceiling(all.Max(p => p.Temp) + 1.5);
        if (hi - lo < 8) { double c = (hi + lo) / 2; lo = Math.Floor(c - 4); hi = Math.Ceiling(c + 4); }
        if (_unit == "%" && hi > 100) { lo -= hi - 100; hi = 100; }
        Point At(double percent, double temp) => new(plot.Left + (percent - minPercent) / (100.0 - minPercent) * plot.Width, plot.Bottom - (temp - lo) / (hi - lo) * plot.Height);

        double px = Hairline.ScaleOf(this);
        var label = ThemeManager.Brush("TextFaint", Brushes.Gray);
        var gridPen = Hairline.Pen(ThemeManager.Brush("GridLine", Brushes.LightGray), px);
        var axisPen = Hairline.Pen(ThemeManager.Brush("LineStrong", Brushes.Gray), px);
        double stepT = (hi - lo) > 24 ? 10 : (hi - lo) > 12 ? 5 : 2;
        for (double t = Math.Ceiling(lo / stepT) * stepT; t <= hi; t += stepT)
        {
            double y = Hairline.Snap(At(minPercent, t).Y, px);
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var text = Text(_unit == "%" ? $"{t:0}%" : $"{t:0} °C", 10.5, label, dpi);
            dc.DrawText(text, new Point(plot.Left - text.Width - 6, y - text.Height / 2));
        }
        for (int p = (minPercent + 9) / 10 * 10; p <= 100; p += 10)
        {
            if ((p - minPercent) % 20 != 0 && p != 100) continue;
            double x = Hairline.Snap(At(p, lo).X, px);
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var text = Text($"{p}%", 10.5, label, dpi);
            dc.DrawText(text, new Point(Math.Min(plot.Right - text.Width, x - text.Width / 2), plot.Bottom + 5));
        }
        dc.DrawLine(axisPen, new Point(plot.Left, Hairline.Snap(plot.Bottom, px)), new Point(plot.Right, Hairline.Snap(plot.Bottom, px)));

        if (_tooHot is { } hot)
        {
            double x = At(hot, lo).X;
            var danger = ThemeManager.Brush("Danger", Brushes.Red);
            var area = new Rect(plot.Left, plot.Top, Math.Max(0, x - plot.Left), plot.Height);
            dc.PushOpacity(0.12);
            dc.DrawRectangle(danger, null, area);
            dc.Pop();
            var text = Text("Too hot", 10.5, ThemeManager.Brush("DangerDeep", Brushes.Red), dpi, bold: true);
            if (area.Width > text.Width + 8) dc.DrawText(text, new Point(plot.Left + 4, plot.Top + 2));
        }

        if (_knee is { } knee)
        {
            var mark = ThemeManager.Brush("ApricotDeep", Brushes.Orange);
            var pen = new Pen(mark, 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            pen.Freeze();
            double x = At(knee, lo).X;
            dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var text = Text($"{knee}%", 11, mark, dpi, bold: true);
            dc.DrawText(text, new Point(Math.Min(plot.Right - text.Width, Math.Max(plot.Left, x - text.Width / 2)), plot.Top - text.Height - 1));
        }

        var surface = ThemeManager.Brush("Surface", Brushes.White);
        foreach (var series in _series.OrderBy(s => s.Main))
        {
            var brush = ThemeManager.Brush(series.Main ? "Accent" : "TextMuted", Brushes.Gray);
            var sorted = series.Points.OrderBy(p => p.Percent).ToList();
            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                c.BeginFigure(At(sorted[0].Percent, sorted[0].Temp), false, false);
                foreach (var p in sorted.Skip(1)) c.LineTo(At(p.Percent, p.Temp), true, true);
            }
            line.Freeze();
            var pen = new Pen(brush, series.Main ? 2.4 : 1.6) { LineJoin = PenLineJoin.Round };
            if (!series.Main) pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
            var ring = new Pen(brush, 2);
            ring.Freeze();
            foreach (var p in sorted) dc.DrawEllipse(_worked.Contains(p.Percent) ? surface : brush, ring, At(p.Percent, p.Temp), 3.5, 3.5);
            if (series.Name.Length > 0)
            {
                var last = sorted[0];
                var name = Text(series.Name, 10.5, brush, dpi, bold: series.Main);
                var anchor = At(last.Percent, last.Temp);
                dc.DrawText(name, new Point(anchor.X + 7, anchor.Y - name.Height - 2));
            }
        }
    }
}
