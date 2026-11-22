using System.Windows;
using System.Windows.Media;
using Core.Theme;

namespace Core.Views;

internal enum PanelSpread { Race, Best, Banner, Row }

internal sealed class SoftPanels : FrameworkElement, IFrameStep
{
    public static readonly DependencyProperty FillProperty = Ink(nameof(Fill)), GroundProperty = Ink(nameof(Ground));

    private static DependencyProperty Ink(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(SoftPanels),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((SoftPanels)d).Rebuild()));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Ground { get => (Brush?)GetValue(GroundProperty); set => SetValue(GroundProperty, value); }

    private const double Faintest = 0.02, Brightest = 0.16, FeatherPerBlur = 3, FrameMs = 33;
    private const double SweepMs = 10000, SweepMoves = 0.55, BandWidth = 0.84, BandAngleDeg = 15;
    private const byte BandEdge = 15, BandMiddle = 33;

    private readonly int _count;
    private readonly double _slope, _blur, _phase;
    private readonly DrawingVisual _band = new();
    private readonly TranslateTransform _bandMove = new();
    private Brush? _panels;
    private Size _builtFor;
    private double _last = double.NegativeInfinity;
    private bool _running;

    public SoftPanels(PanelSpread spread, string place, double phase = 0)
    {
        (_count, _slope, _blur) = spread switch
        {
            PanelSpread.Race => (4, 0.14, 12.0),
            PanelSpread.Best => (6, 0.05, 14.0),
            PanelSpread.Banner => (8, 0.05, 14.0),
            _ => (5, 0.02, 6.0),
        };
        _phase = phase - Math.Floor(phase);
        IsHitTestVisible = false;
        ClipToBounds = true;
        _band.Transform = _bandMove;
        AddVisualChild(_band);
        SetResourceReference(FillProperty, RaceLook.Backdrop(place));
        SetResourceReference(GroundProperty, "Surface");
        Loaded += (_, _) =>
        {
            Motion.WindowActiveChanged -= Update;
            Motion.WindowActiveChanged += Update;
            Update();
        };
        Unloaded += (_, _) =>
        {
            Motion.WindowActiveChanged -= Update;
            _running = false;
            FrameDriver.Stop(this);
        };
        IsVisibleChanged += (_, _) => Update();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _band;

    private bool CanRun => Motion.Flourish && Motion.WindowActive && IsLoaded && IsVisible;

    private void Update()
    {
        if (CanRun == _running) return;
        _running = !_running;
        if (_running) FrameDriver.Run(this);
        else
        {
            FrameDriver.Stop(this);
            Place(double.NaN);
        }
    }

    public bool Step(double now)
    {
        if (!_running) return false;
        if (now - _last < FrameMs) return true;
        _last = now;
        Place(now);
        return true;
    }

    private void Place(double now)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        if (double.IsNaN(now))
        {
            _bandMove.X = -2 * w;
            return;
        }
        double cycle = now / SweepMs + _phase;
        double t = Math.Min(1, (cycle - Math.Floor(cycle)) / SweepMoves);
        double eased = (1 - Math.Cos(Math.PI * t)) / 2;
        _bandMove.X = -w + 2 * w * eased;
    }

    private void Rebuild()
    {
        _builtFor = Size.Empty;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || Fill is not SolidColorBrush { Color: var fill } || Ground is not SolidColorBrush { Color: var ground }) return;
        if (_builtFor != RenderSize)
        {
            _builtFor = RenderSize;
            _panels = PanelBrush(w, h, fill, ground);
            DrawBand(w, h, fill);
            if (!_running) Place(double.NaN);
        }
        dc.DrawRectangle(_panels, null, new Rect(0, 0, w, h));
    }

    private Brush PanelBrush(double w, double h, Color fill, Color ground)
    {
        double lean = _slope * w, length = Math.Sqrt(h * h + lean * lean);
        var normal = new Vector(h / length, lean / length);
        double far = (w * h + lean * h) / length;
        double step = (1 + _slope) / _count * w * h / length, feather = Math.Min(step * 0.45, _blur * FeatherPerBlur / 2);
        var stops = new GradientStopCollection();
        Color Shade(int i) => ThemeManager.Mix(ground, fill, Faintest + (Brightest - Faintest) * (_count == 1 ? 1 : (double)i / (_count - 1)));
        stops.Add(new GradientStop(Shade(0), 0));
        for (int i = 1; i < _count; i++)
        {
            double at = i * step / far, half = feather / far;
            stops.Add(new GradientStop(Shade(i - 1), at - half));
            stops.Add(new GradientStop(ThemeManager.Mix(Shade(i - 1), Shade(i), 0.5), at));
            stops.Add(new GradientStop(Shade(i), at + half));
        }
        stops.Add(new GradientStop(Shade(_count - 1), 1));
        var brush = new LinearGradientBrush(stops) { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = (Point)(normal * far) };
        brush.Freeze();
        return brush;
    }

    private void DrawBand(double w, double h, Color fill)
    {
        double angle = BandAngleDeg * Math.PI / 180, half = BandWidth * w / 2;
        var along = new Vector(Math.Cos(angle), Math.Sin(angle));
        var middle = new Point(w / 2, h / 2);
        var clear = fill;
        clear.A = 0;
        var edge = fill;
        edge.A = BandEdge;
        var core = fill;
        core.A = BandMiddle;
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new(clear, 0), new(edge, 0.25), new(core, 0.5), new(edge, 0.75), new(clear, 1),
        }) { MappingMode = BrushMappingMode.Absolute, StartPoint = middle - along * half, EndPoint = middle + along * half };
        brush.Freeze();
        using var dc = _band.RenderOpen();
        dc.DrawRectangle(brush, null, new Rect(-2 * w, 0, 5 * w, h));
    }
}
