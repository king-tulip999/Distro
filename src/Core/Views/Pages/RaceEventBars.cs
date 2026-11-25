using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Core.Views.Pages;

internal sealed class RaceEventBars : FrameworkElement
{
    private const int WaitingSlots = 18;
    private static readonly string[] KindBrushes = { "Sage", "Warn", "Danger", "Slate" };

    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(nameof(Phase), typeof(double), typeof(RaceEventBars),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private int[][] _bars = Array.Empty<int[]>();
    private int _most = 1;
    private int _current = -1;
    private int _filled;
    private bool _running;

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public void Show(int[][] bars, int filled, int current)
    {
        _bars = bars;
        _filled = filled;
        _current = current;
        _most = 1;
        foreach (var bar in bars)
        {
            int total = 0;
            foreach (int n in bar) total += n;
            _most = Math.Max(_most, total);
        }
        InvalidateVisual();
    }

    public void Run(bool on)
    {
        if (on == _running) return;
        _running = on;
        if (!on)
        {
            BeginAnimation(PhaseProperty, null);
            return;
        }
        var breathe = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(breathe, 20);
        BeginAnimation(PhaseProperty, breathe);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var track = TryFindResource("MeterTrack") as Brush;
        if (_bars.Length == 0)
        {
            dc.PushOpacity(0.5 + 0.5 * Phase);
            for (int i = 0; i < WaitingSlots; i++)
            {
                var (x, w) = Slot(i, WaitingSlots, 3, width);
                dc.DrawRectangle(track, null, new Rect(x, 0, w, height));
            }
            dc.Pop();
            return;
        }
        int gap = _bars.Length <= 24 ? 3 : 1;
        for (int i = 0; i < _bars.Length; i++)
        {
            var (x, w) = Slot(i, _bars.Length, gap, width);
            if (i >= _filled || i == _current)
            {
                dc.DrawRectangle(track, null, new Rect(x, 0, w, height));
                if (i >= _filled) continue;
            }
            double scale = i == _current ? 0.6 + 0.4 * Phase : 1;
            double bottom = height;
            var counts = _bars[i];
            for (int k = 0; k < counts.Length; k++)
            {
                if (counts[k] == 0) continue;
                double top = Math.Min(bottom - 1, Math.Round(bottom - counts[k] / (double)_most * height * scale));
                dc.DrawRectangle(TryFindResource(KindBrushes[k]) as Brush, null, new Rect(x, top, w, bottom - top));
                bottom = top;
            }
        }
    }

    private static (double X, double Width) Slot(int index, int count, int gap, double width)
    {
        double step = (width + gap) / count;
        double left = Math.Round(index * step), right = Math.Round((index + 1) * step) - gap;
        return (left, Math.Max(1, right - left));
    }
}
