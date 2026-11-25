using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Core.Theme;

namespace Core.Views.RaceEvents;

internal readonly record struct TimelineTick(double Time, string Brush, int Id, string Tip);

internal sealed class RaceTimeline : FrameworkElement
{
    private const double TrackHeight = 22, LabelGap = 4, LabelSize = 11, TickWidth = 3, HitSlop = 4;

    private readonly Popup _tip;
    private readonly TextBlock _tipText = new() { FontSize = 12 };
    private readonly Dictionary<string, FormattedText> _labelCache = new();
    private IReadOnlyList<TimelineTick> _ticks = Array.Empty<TimelineTick>();
    private IReadOnlyList<double> _laps = Array.Empty<double>();
    private IReadOnlyList<(double At, string Text)> _labels = Array.Empty<(double, string)>();
    private double _start, _end = 1, _now;
    private double? _replay;
    private int? _selected;
    private double? _hoverX;
    private bool _off = true;
    private Func<double, string>? _describe;
    private double _dpi = 1;

    public RaceTimeline()
    {
        Height = TrackHeight + LabelGap + 15;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        var tipBorder = Hairline.Frame(new Border { Padding = new Thickness(9, 3, 9, 4), CornerRadius = ThemeManager.Round(6), Child = _tipText });
        tipBorder.SetResourceReference(Border.BackgroundProperty, "TooltipBg");
        tipBorder.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        _tipText.SetResourceReference(TextBlock.ForegroundProperty, "TooltipText");
        _tip = new Popup { Child = tipBorder, AllowsTransparency = true, PlacementTarget = this, Placement = PlacementMode.Relative, IsHitTestVisible = false };
        Loaded += (_, _) => _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Unloaded += (_, _) => _tip.IsOpen = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "Race timeline");
    }

    public event Action<int>? EventClicked;

    public event Action<double>? TimeClicked;

    public void Set(double start, double end, double now, double? replay, IReadOnlyList<TimelineTick> ticks, IReadOnlyList<double> laps,
        IReadOnlyList<(double At, string Text)> labels, int? selected, bool off, Func<double, string> describe)
    {
        _start = start;
        _end = Math.Max(end, start + 1);
        _now = now;
        _replay = replay;
        _ticks = ticks;
        _laps = laps;
        _labels = labels;
        _selected = selected;
        _off = off;
        _describe = describe;
        Cursor = off ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual();
    }

    public void SetHeads(double now, double? replay)
    {
        if (Math.Abs(now - _now) < 0.05 && Nullable.Equals(replay, _replay)) return;
        _now = now;
        _replay = replay;
        InvalidateVisual();
    }

    private double X(double time) => Math.Clamp((time - _start) / (_end - _start), 0, 1) * ActualWidth;

    private double TimeAt(double x) => _start + Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * (_end - _start);

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;

    private readonly Dictionary<(Brush, double), Pen> _pens = new();

    private Pen PenOf(Brush brush, double width)
    {
        if (_pens.TryGetValue((brush, width), out var pen)) return pen;
        if (_pens.Count > 32) _pens.Clear();
        pen = new Pen(brush, width);
        pen.Freeze();
        return _pens[(brush, width)] = pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        var track = new Rect(0.5, 0.5, w - 1, TrackHeight - 1);
        double r = 6;
        dc.DrawRoundedRectangle(Res("SurfaceAlt"), PenOf(Res("Line"), 1), track, r, r);
        if (_off) return;
        double done = X(_now);
        if (done > 1)
        {
            dc.PushClip(new RectangleGeometry(track, r, r));
            dc.DrawRectangle(Res("TileRaised"), null, new Rect(0.5, 0.5, Math.Min(done, w - 1), TrackHeight - 1));
            dc.Pop();
        }
        var lapPen = PenOf(Res("GridLine"), 1);
        foreach (double lap in _laps)
        {
            double x = Math.Round(X(lap)) + 0.5;
            if (x > 2 && x < w - 2) dc.DrawLine(lapPen, new Point(x, 1), new Point(x, TrackHeight - 1));
        }
        foreach (var tick in _ticks)
        {
            double x = Math.Round(X(tick.Time) - TickWidth / 2);
            var rect = new Rect(x, 4, TickWidth, TrackHeight - 8);
            dc.DrawRoundedRectangle(Res(tick.Brush), null, rect, 1.5, 1.5);
            if (_selected == tick.Id) dc.DrawRoundedRectangle(null, PenOf(Res("Heading"), 1), new Rect(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), 2.5, 2.5);
        }
        if (_hoverX is { } hx && hx <= done) dc.DrawLine(PenOf(Res("InputEdgeHover"), 1), new Point(Math.Round(hx) + 0.5, 1), new Point(Math.Round(hx) + 0.5, TrackHeight - 1));
        dc.DrawRoundedRectangle(Res("Danger"), null, new Rect(Math.Round(done) - 1, -3, 2, TrackHeight + 6), 1, 1);
        if (_replay is { } rep) dc.DrawRoundedRectangle(Res("Warn"), null, new Rect(Math.Round(X(rep)) - 1, -3, 2, TrackHeight + 6), 1, 1);
        var faint = Res("TextFaint");
        double lastRight = double.NegativeInfinity;
        for (int i = 0; i < _labels.Count; i++)
        {
            var text = Label(_labels[i].Text, faint);
            double x = X(_labels[i].At);
            x = i == 0 ? 0 : i == _labels.Count - 1 && _labels.Count > 1 ? Math.Min(x, w - text.Width) : x - text.Width / 2;
            x = Math.Clamp(x, 0, Math.Max(0, w - text.Width));
            if (x < lastRight + 12) continue;
            dc.DrawText(text, new Point(Math.Round(x), TrackHeight + LabelGap));
            lastRight = x + text.Width;
        }
    }

    private FormattedText Label(string text, Brush brush)
    {
        if (!ReferenceEquals(_labelBrush, brush)) { _labelCache.Clear(); _labelBrush = brush; }
        if (_labelCache.TryGetValue(text, out var known)) return known;
        if (_labelCache.Count > 64) _labelCache.Clear();
        var family = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), LabelSize, brush, _dpi);
        return _labelCache[text] = formatted;
    }

    private Brush? _labelBrush;

    private int? TickAt(double x)
    {
        int? best = null;
        double bestDistance = HitSlop + TickWidth;
        foreach (var tick in _ticks)
        {
            double d = Math.Abs(X(tick.Time) - x);
            if (d <= bestDistance) { bestDistance = d; best = tick.Id; }
        }
        return best;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_off) return;
        var p = e.GetPosition(this);
        if (p.Y > TrackHeight + 2) { Hide(); return; }
        int? tick = TickAt(p.X);
        double t = TimeAt(p.X);
        _tipText.Text = tick is { } id ? _ticks.First(k => k.Id == id).Tip : t > _now ? "Not raced yet" : _describe?.Invoke(t) ?? "";
        _hoverX = tick is null ? p.X : null;
        _tip.HorizontalOffset = Math.Clamp(p.X - 80, 0, Math.Max(0, ActualWidth - 160));
        _tip.VerticalOffset = TrackHeight + 6;
        _tip.IsOpen = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Hide();
    }

    private void Hide()
    {
        _tip.IsOpen = false;
        if (_hoverX is null) return;
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_off) return;
        var p = e.GetPosition(this);
        if (p.Y > TrackHeight + 2) return;
        e.Handled = true;
        if (TickAt(p.X) is { } id) { EventClicked?.Invoke(id); return; }
        double t = TimeAt(p.X);
        if (t <= _now) TimeClicked?.Invoke(t);
    }
}
