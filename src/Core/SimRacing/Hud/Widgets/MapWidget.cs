using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;
using Core.SimRacing.TrackMaps;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class MapWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        TitleRow,
        HudOption.Number("zoom", "Zoom", 1, 1, 8, 0.5, null, "1 shows the whole track; above that the map closes in on your car"),
        HudOption.Bool("rotate", "Turn with my car", false, "Your direction of travel points up"),
        HudOption.Bool("turns", "Turn numbers", true, "Numbers that would overlap are left out"),
        HudOption.Number("trackWidth", "Track width", 100, 40, 400, 10, "%", "100 is the track's real width, drawn to scale"),
        HudOption.Number("edgeWidth", "Border width", 1, 0, 10, 0.5, "px", "0 for no border"),
        MapTrackLook.Look,
        MapTrackLook.TrackColor,
        MapTrackLook.EdgeColor,
        HudOption.Bool("pitLane", "Pit lane", true, "Draw the pit lane next to the track"),
        HudOption.Bool("startLine", "Start / finish line", true),
        HudOption.Choice("dotStyle", "Style", "dot", new HudChoice("dot", "Dot"), new HudChoice("pill", "Pill"), new HudChoice("circle", "Circle")),
        HudOption.Number("dotSize", "Dot size", 100, 50, 300, 10, "%"),
        HudOption.Number("myDotSize", "My dot size", 100, 50, 300, 10, "%", "Compared with the other dots"),
        HudOption.Color("myColor", "My color", "Until one is picked, the overlay's accent color"),
        HudOption.Color("carColor", "Other cars' color", "Until one is picked, grey with one class and iRacing's class colors with several. A picked color is used for every class"),
        HudOption.Bool("positions", "Text on the dots", true, "Shown once the dots are big enough"),
        HudOption.Choice("dotText", "Text", "position", new HudChoice("position", "Class position"), new HudChoice("number", "Car number")),
        HudOption.Bool("spread", "Spread packs along the track", true, "Cars in a pack slide a little along the track so their numbers fit"),
        HudOption.Int("maxMove", "Max move", 8, 0, 20, "%", "How far, as a share of a lap, a car may slide from where it really is"),
        HudOption.Bool("lapColors", "Lap colors", false, "Race only. Red edge: lapping you. Blue edge: a lap or more behind you"),
        HudOption.Bool("pit", "Cars on pit road", true, "Dimmed where iRacing places them along the track"),
    };


    private const double HeadingLookMetres = 25;
    private const double TurnSettleSeconds = 0.3;

    private readonly HudMapSource _maps = new();
    private TrackMap? _angleFor;
    private TrackMap? _builtFor;
    private RectangleF _extent;
    private GraphicsPath? _surface, _centre, _pit, _pitLines;
    private float _pitWidth;
    private float _angle;
    private bool _angleSet;
    private long _angleAt;
    private MapView _view;
    private readonly Matrix _toScreen = new();
    private readonly Matrix _back = new();
    private readonly List<RectangleF> _labels = new();
    private const float LabelReach = 0.95f;
    private readonly float[] _dotX = new float[TelemetryFrame.MaxCars], _dotY = new float[TelemetryFrame.MaxCars], _dotR = new float[TelemetryFrame.MaxCars];
    private readonly int[] _dotCar = new int[TelemetryFrame.MaxCars];
    private readonly Color[] _dotColor = new Color[TelemetryFrame.MaxCars];
    private readonly bool[] _dotCovered = new bool[TelemetryFrame.MaxCars];
    private const float BadgeHeight = 1.8f;
    private const double TextReturnSeconds = 0.2;
    private const double SlideSettleSeconds = 0.05;
    private const float SlidePixelsPerSecond = 240;
    private readonly Badge[] _badges = new Badge[TelemetryFrame.MaxCars];
    private readonly int[] _order = new int[TelemetryFrame.MaxCars], _chain = new int[TelemetryFrame.MaxCars], _blockCount = new int[TelemetryFrame.MaxCars];
    private readonly double[] _orderKey = new double[TelemetryFrame.MaxCars], _chainKey = new double[TelemetryFrame.MaxCars];
    private readonly double[] _real = new double[TelemetryFrame.MaxCars], _need = new double[TelemetryFrame.MaxCars], _placed = new double[TelemetryFrame.MaxCars], _blockSum = new double[TelemetryFrame.MaxCars];
    private readonly double[] _shown = new double[TelemetryFrame.MaxCars];
    private readonly bool[] _hasShown = new bool[TelemetryFrame.MaxCars];
    private readonly long[] _clearSince = new long[TelemetryFrame.MaxCars];
    private long _badgesAt;

    private struct Badge
    {
        public int Car;
        public double Rel, Shift;
        public float X, Y, HalfW, HalfH;
        public Color Color;
        public Color? Edge;
        public string? Label;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Track map")) return;
        var f = v.Frame;
        var area = p.Header("Track map", f.Session.TrackDisplayName);
        if (_maps.Get(p, f) is not { } map)
        {
            if (_maps.Loading)
            {
                p.Text("Loading the map", p.Body, p.P.Muted, area, HAlign.Center, VAlign.Center);
                return;
            }
            float line = p.LineHeight(p.Body);
            var upper = new RectangleF(area.X, area.Y + area.Height / 2 - line, area.Width, line);
            p.Text("No map yet", p.Body, p.P.Muted, upper, HAlign.Center, VAlign.Center);
            p.Text("Drive one clean lap", p.Body, p.P.Muted, upper with { Y = upper.Bottom }, HAlign.Center, VAlign.Center);
            return;
        }
        if (!ReferenceEquals(map, _angleFor))
        {
            _angleFor = map;
            _angleSet = false;
        }
        float d = p.Density;
        float side = Math.Min(area.Width, area.Height);
        float dot = Math.Clamp(side / 60, 4, 8) * d * (float)(v.Options.Number("dotSize") / 100);
        if (v.Options.Bool("positions") && v.Options.Value("dotStyle") is not ("pill" or "circle")) dot = Math.Max(dot, LabelDot(p, v));
        Build(map);
        float mine = (dot + 2 * d) * (float)(v.Options.Number("myDotSize") / 100);
        float widthScale = (float)(v.Options.Number("trackWidth") / 100);
        float edgePx = (float)v.Options.Number("edgeWidth") * d;
        float grow = Math.Max(0, widthScale - 1) * map.Width / 2;
        _view = View(p, v, map, area, Math.Max(dot, mine) + 2 * d, edgePx, grow);
        var (track, edge) = MapTrackLook.Colors(p, v.Options);
        var pitColor = p.P.MapPit ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.18);
        var g = p.G;
        g.SetClip(area);
        _toScreen.Reset();
        _toScreen.Translate(_view.Screen.X, _view.Screen.Y);
        _toScreen.Rotate(_view.Angle * 180 / MathF.PI);
        _toScreen.Scale(_view.Scale, _view.Scale);
        _toScreen.Translate(-_view.Centre.X, -_view.Centre.Y);
        DrawTrackDirect(p, map, new TrackLook(track, edge, pitColor, widthScale, edgePx, v.Options.Bool("pitLane")));
        if (v.Options.Bool("startLine") && map.StartLine is { } sf)
        {
            var (a, b) = StartLine(sf.A, sf.B, widthScale, edgePx / _view.Scale);
            a = _view.ToScreen(a);
            b = _view.ToScreen(b);
            p.Line(a.X, a.Y, b.X, b.Y, p.P.Heading, Math.Max(2, 3 * d));
        }
        if (v.Options.Bool("turns")) Turns(p, map, area);
        if (v.Options.Value("dotStyle") is "pill" or "circle") Badges(p, v, map, dot, mine);
        else Cars(p, v, map, dot, mine);
        g.ResetClip();
    }

    private readonly record struct TrackLook(Color Track, Color Edge, Color Pit, float Width, float EdgePx, bool PitLane);

    private void DrawTrackDirect(HudPainter p, TrackMap map, TrackLook look)
    {
        var g = p.G;
        _back.Reset();
        _back.Multiply(_toScreen);
        _back.Invert();
        g.MultiplyTransform(_toScreen);
        DrawTrack(g, p, map, look);
        g.MultiplyTransform(_back);
    }

    private void DrawTrack(Graphics g, HudPainter p, TrackMap map, TrackLook look)
    {
        float k = _view.Scale;
        float edge = look.EdgePx / k;
        if (look.PitLane)
        {
            if (_pit is not null) g.FillPath(p.Brush(look.Pit), _pit);
            if (_pitLines is not null) g.DrawPath(p.Pen(look.Pit, Math.Max(1, _pitWidth * k) / k), _pitLines);
        }
        if (_surface is not null && look.Width >= 1)
        {
            float grow = (look.Width - 1) * map.Width;
            if (edge > 0) g.DrawPath(p.Pen(look.Edge, grow + 2 * edge), _surface);
            g.FillPath(p.Brush(look.Track), _surface);
            if (grow > 0) g.DrawPath(p.Pen(look.Track, grow), _surface);
        }
        else if (_centre is not null)
        {
            float w = Math.Max(2 / k, map.Width * look.Width);
            if (edge > 0) g.DrawPath(p.Pen(look.Edge, w + 2 * edge), _centre);
            g.DrawPath(p.Pen(look.Track, w), _centre);
        }
    }

    internal static (PointF A, PointF B) StartLine(PointF a, PointF b, float widthScale, float edge)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0) return (a, b);
        float half = len * widthScale / 2 + edge;
        float mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, ux = dx / len, uy = dy / len;
        return (new PointF(mx - ux * half, my - uy * half), new PointF(mx + ux * half, my + uy * half));
    }

    public override void Dispose()
    {
        foreach (var path in new[] { _surface, _centre, _pit, _pitLines }) path?.Dispose();
        (_surface, _centre, _pit, _pitLines) = (null, null, null, null);
        _builtFor = null;
        _toScreen.Dispose();
        _back.Dispose();
    }

    private MapView View(HudPainter p, HudView v, TrackMap map, RectangleF area, float margin, float edgePx, float grow)
    {
        var extent = RectangleF.Inflate(_extent, grow, grow);
        margin += edgePx;
        var f = v.Frame;
        bool turning = v.Options.Bool("rotate");
        float zoom = (float)v.Options.Number("zoom");
        var room = new SizeF(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        float k = MapView.FitScale(extent, room, turning) * zoom;
        var middle = new PointF(extent.X + extent.Width / 2, extent.Y + extent.Height / 2);
        int me = f.PlayerCarIdx;
        double pct = me >= 0 && me < TelemetryFrame.MaxCars ? f.CarLapDistPct[me] : -1;
        var centre = middle;
        if (pct >= 0)
        {
            var car = map.At(pct);
            float follow = Math.Clamp(zoom - 1, 0, 1);
            centre = new PointF(middle.X + (car.X - middle.X) * follow, middle.Y + (car.Y - middle.Y) * follow);
        }
        float target = 0;
        if (turning && pct >= 0) target = MapView.UpAngle(Heading(map, pct, f.Session.TrackLengthKm));
        long now = Stopwatch.GetTimestamp();
        if (!_angleSet || p.IsPreview) _angle = MapView.Wrap(target);
        else _angle = MapView.Ease(_angle, target, Stopwatch.GetElapsedTime(_angleAt, now).TotalSeconds, TurnSettleSeconds);
        _angleSet = true;
        _angleAt = now;
        return new MapView(new PointF(area.X + area.Width / 2, area.Y + area.Height / 2), centre, k, _angle);
    }

    private static PointF Heading(TrackMap map, double pct, double? trackKm)
    {
        double look = trackKm is > 0 and var km ? HeadingLookMetres / (km * 1000) : 0.005;
        var a = map.At(pct - look);
        var b = map.At(pct + look);
        double len = TrackMap.Distance(a, b);
        return len <= 0 ? map.Heading(pct) : new PointF((float)((b.X - a.X) / len), (float)((b.Y - a.Y) / len));
    }

    private void Turns(HudPainter p, TrackMap map, RectangleF area)
    {
        _labels.Clear();
        var font = p.Tiny;
        float h = p.LineHeight(font);
        foreach (var t in map.Turns)
        {
            var at = _view.ToScreen(t.At);
            float w = p.Measure(t.Text, font) + 4;
            var box = new RectangleF(at.X - w / 2, at.Y - h / 2, w, h);
            if (!area.Contains(box)) continue;
            bool overlaps = false;
            foreach (var other in _labels)
                if (other.IntersectsWith(box)) { overlaps = true; break; }
            if (overlaps) continue;
            _labels.Add(box);
            p.Text(t.Text, font, p.P.Muted, box, HAlign.Center, VAlign.Center, false);
        }
    }

    private void Cars(HudPainter p, HudView v, TrackMap map, float dot, float myDot)
    {
        var f = v.Frame;
        var s = f.Session;
        float d = p.Density;
        bool multiclass = s.NumCarClasses > 1;
        bool text = v.Options.Bool("positions");
        bool carNumbers = v.Options.Value("dotText") == "number";
        bool pit = v.Options.Bool("pit");
        int me = f.PlayerCarIdx;
        bool lapColors = v.Options.Bool("lapColors") && f.SessionKind == SessionKind.Race && me >= 0 && me < TelemetryFrame.MaxCars && f.CarLapDistPct[me] >= 0;
        double myPct = lapColors ? f.CarLapDistPct[me] : 0;
        double myDist = lapColors ? FieldOrder.Distance(f, me) ?? 0 : 0;
        var others = v.Options.ColorOf("carColor") ?? p.P.MapCars ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.8);
        bool oneColor = v.Options.ColorOf("carColor") is not null;
        var myColor = v.Options.ColorOf("myColor") ?? p.P.Player;
        int n = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int car = 0; car < TelemetryFrame.MaxCars; car++)
            {
                bool mine = car == me;
                if (mine != (pass == 1)) continue;
                double pct = f.CarLapDistPct[car];
                if (pct < 0 || !s.ByCar.TryGetValue(car, out var driver) || !driver.Races || !v.ClassShown(car)) continue;
                bool onPit = f.CarOnPitRoad[car];
                if (onPit && !pit && !mine) continue;
                var at = _view.ToScreen(map.At(pct));
                float x = at.X, y = at.Y;
                var color = mine ? myColor : multiclass && !oneColor ? p.ClassColor(s, driver.ClassId) : others;
                if (onPit && !mine) color = HudPalette.Alpha(color, 110);
                float r = mine ? myDot : dot;
                int laps = lapColors && !mine ? LapDiff(f, car, pct, myPct, myDist) : 0;
                if (laps != 0) p.Dot(x, y, r + 2 * d, laps > 0 ? p.P.Lapping : p.P.Lapped);
                else p.Dot(x, y, r + (mine ? 2 : 1), mine ? p.P.Heading : p.P.Bg);
                p.Dot(x, y, r, color);
                _dotX[n] = x;
                _dotY[n] = y;
                _dotR[n] = r;
                _dotCar[n] = car;
                _dotColor[n] = color;
                n++;
            }
        if (!text) return;
        Covered(_dotX, _dotY, _dotR, n, _dotCovered);
        for (int i = 0; i < n; i++)
        {
            int car = _dotCar[i];
            if (car == me || _dotCovered[i] || !s.ByCar.TryGetValue(car, out var driver) || Label(f, driver, car, carNumbers) is not { Length: > 0 } label) continue;
            DotLabels.Draw(p, label, _dotX[i], _dotY[i], _dotR[i], _dotColor[i]);
        }
    }

    private static float LabelDot(HudPainter p, HudView v)
    {
        var f = v.Frame;
        var s = f.Session;
        bool carNumbers = v.Options.Value("dotText") == "number";
        bool pit = v.Options.Bool("pit");
        float need = 0;
        for (int car = 0; car < TelemetryFrame.MaxCars; car++)
        {
            if (car == f.PlayerCarIdx || f.CarLapDistPct[car] < 0 || !s.ByCar.TryGetValue(car, out var driver) || !driver.Races || !v.ClassShown(car)) continue;
            if (f.CarOnPitRoad[car] && !pit) continue;
            if (Label(f, driver, car, carNumbers) is { Length: > 0 } label) need = Math.Max(need, DotLabels.RadiusFor(p, label));
        }
        return need;
    }

    private void Badges(HudPainter p, HudView v, TrackMap map, float dot, float myDot)
    {
        var f = v.Frame;
        var s = f.Session;
        float d = p.Density;
        bool circle = v.Options.Value("dotStyle") == "circle";
        bool multiclass = s.NumCarClasses > 1;
        bool text = v.Options.Bool("positions");
        bool carNumbers = v.Options.Value("dotText") == "number";
        bool pit = v.Options.Bool("pit");
        int me = f.PlayerCarIdx;
        bool hasMe = me >= 0 && me < TelemetryFrame.MaxCars && f.CarLapDistPct[me] >= 0;
        double myPct = hasMe ? f.CarLapDistPct[me] : 0;
        bool lapColors = v.Options.Bool("lapColors") && f.SessionKind == SessionKind.Race && hasMe;
        double myDist = lapColors ? FieldOrder.Distance(f, me) ?? 0 : 0;
        var others = v.Options.ColorOf("carColor") ?? p.P.MapCars ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.8);
        bool oneColor = v.Options.ColorOf("carColor") is not null;
        var myColor = v.Options.ColorOf("myColor") ?? p.P.Player;
        var font = p.Font(Math.Max(8, dot * 1.15f / d), true);
        float widest = 0;
        int n = 0;
        for (int car = 0; car < TelemetryFrame.MaxCars; car++)
        {
            if (car == me) continue;
            double pct = f.CarLapDistPct[car];
            if (pct < 0 || !s.ByCar.TryGetValue(car, out var driver) || !driver.Races || !v.ClassShown(car)) continue;
            bool onPit = f.CarOnPitRoad[car];
            if (onPit && !pit) continue;
            var color = multiclass && !oneColor ? p.ClassColor(s, driver.ClassId) : others;
            if (onPit) color = HudPalette.Alpha(color, 110);
            int laps = lapColors ? LapDiff(f, car, pct, myPct, myDist) : 0;
            string? label = text ? Label(f, driver, car, carNumbers) : null;
            if (label is { Length: > 0 }) widest = Math.Max(widest, p.Measure(label, font));
            double rel = pct - myPct;
            rel -= Math.Round(rel);
            _badges[n] = new Badge { Car = car, Rel = rel, Color = color, Edge = laps > 0 ? p.P.Lapping : laps < 0 ? p.P.Lapped : null, Label = label };
            _orderKey[n] = FieldOrder.Distance(f, car) ?? pct;
            _order[n] = n;
            n++;
        }
        var size = BadgeSize(circle, Math.Max(dot * BadgeHeight, text && widest > 0 ? p.LineHeight(font) * 0.85f : 0), text ? widest : 0);
        float edge = 1.5f * d;
        for (int i = 0; i < n; i++)
        {
            _badges[i].HalfW = size.Width / 2 + edge;
            _badges[i].HalfH = size.Height / 2 + edge;
        }
        float myHalf = myDot + 2 * d;
        long now = Stopwatch.GetTimestamp();
        double dt = _badgesAt == 0 ? 0 : Math.Min(0.5, Stopwatch.GetElapsedTime(_badgesAt, now).TotalSeconds);
        _badgesAt = now;
        bool spread = text && hasMe && v.Options.Bool("spread");
        bool snap = p.IsPreview || dt <= 0;
        double trackPx = Math.Max(1, map.Length * _view.Scale);
        if (spread)
        {
            double maxShift = v.Options.Int("maxMove") / 100.0;
            Chain(map, n, myPct, myHalf, edge, trackPx, maxShift, 1);
            Chain(map, n, myPct, myHalf, edge, trackPx, maxShift, -1);
        }
        double most = SlidePixelsPerSecond * d * dt / trackPx;
        double ease = 1 - Math.Exp(-dt / SlideSettleSeconds);
        for (int i = 0; i < n; i++)
        {
            ref var b = ref _badges[i];
            double want = spread ? b.Shift : 0;
            double shown = snap || !_hasShown[b.Car] ? want : _shown[b.Car] + Math.Clamp((want - _shown[b.Car]) * ease, -most, most);
            _shown[b.Car] = shown;
            _hasShown[b.Car] = true;
            var at = _view.ToScreen(map.At(myPct + b.Rel + shown));
            b.X = at.X;
            b.Y = at.Y;
        }
        Array.Sort(_orderKey, _order, 0, n);
        var mineAt = hasMe ? _view.ToScreen(map.At(myPct)) : new PointF(float.NaN, float.NaN);
        for (int k = 0; k < n; k++)
        {
            ref var b = ref _badges[_order[k]];
            var box = new RectangleF(b.X - size.Width / 2, b.Y - size.Height / 2, size.Width, size.Height);
            if (circle)
            {
                p.Dot(b.X, b.Y, size.Width / 2 + edge, b.Edge ?? p.P.Bg);
                p.Dot(b.X, b.Y, size.Width / 2, b.Color);
            }
            else
            {
                p.Fill(RectangleF.Inflate(box, edge, edge), b.Edge ?? p.P.Bg, size.Height / 2 + edge);
                p.Fill(box, b.Color, size.Height / 2);
            }
            if (b.Label is not { Length: > 0 } label) continue;
            float tw = p.Measure(label, font) / 2 + d, th = p.LineHeight(font) * 0.45f;
            bool covered = hasMe && TextCovered(b.X, b.Y, tw, th, mineAt.X, mineAt.Y, myHalf * 2, myHalf * 2, d);
            for (int j = k + 1; j < n && !covered; j++)
            {
                ref var o = ref _badges[_order[j]];
                covered = TextCovered(b.X, b.Y, tw, th, o.X, o.Y, size.Width, size.Height, d);
            }
            if (covered) _clearSince[b.Car] = 0;
            else if (_clearSince[b.Car] == 0) _clearSince[b.Car] = now;
            bool show = !covered && (snap || Stopwatch.GetElapsedTime(_clearSince[b.Car], now).TotalSeconds >= TextReturnSeconds);
            if (show) p.Text(label, font, HudPalette.On(b.Color), box, HAlign.Center, VAlign.Center, false);
        }
        if (hasMe)
        {
            p.Dot(mineAt.X, mineAt.Y, myDot + 2, p.P.Heading);
            p.Dot(mineAt.X, mineAt.Y, myDot, myColor);
        }
    }

    internal static SizeF BadgeSize(bool circle, float height, float textWidth)
    {
        if (circle)
        {
            float across = Math.Max(height, textWidth > 0 ? textWidth + height * 0.35f : 0);
            return new SizeF(across, across);
        }
        return new SizeF(Math.Max(height, textWidth > 0 ? textWidth + height * 0.5f : 0), height);
    }

    internal static bool TextCovered(float x, float y, float halfW, float halfH, float ox, float oy, float w, float h, float density)
    {
        float r = h / 2 + 0.5f * density, half = Math.Max(0, (w - h) / 2);
        for (int i = 0; i < TextSamples.Length; i += 2)
        {
            float px = x + TextSamples[i] * halfW, py = y + TextSamples[i + 1] * halfH;
            float cx = Math.Clamp(px, ox - half, ox + half);
            if ((px - cx) * (px - cx) + (py - oy) * (py - oy) < r * r) return true;
        }
        return false;
    }

    private static readonly float[] TextSamples = { 0, 0, -0.8f, 0, 0.8f, 0, -0.5f, -0.7f, 0.5f, -0.7f, -0.5f, 0.7f, 0.5f, 0.7f };

    private void Chain(TrackMap map, int n, double myPct, float myHalf, float gap, double trackPx, double maxShift, int sign)
    {
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            double rel = _badges[i].Rel;
            if (sign > 0 ? rel < 0 : rel >= 0) continue;
            _chain[m] = i;
            _chainKey[m] = Math.Abs(rel);
            m++;
        }
        if (m == 0) return;
        Array.Sort(_chainKey, _chain, 0, m);
        double prevAbs = 0;
        float prevW = myHalf, prevH = myHalf;
        float cos = MathF.Cos(_view.Angle), sin = MathF.Sin(_view.Angle);
        for (int k = 0; k < m; k++)
        {
            ref var b = ref _badges[_chain[k]];
            double abs = Math.Abs(b.Rel);
            var t = map.Heading(myPct + sign * (prevAbs + abs) / 2);
            float ax = MathF.Abs(cos * t.X - sin * t.Y), ay = MathF.Abs(sin * t.X + cos * t.Y);
            _real[k] = abs;
            _need[k] = (ax * (prevW + b.HalfW) + ay * (prevH + b.HalfH) + gap) / trackPx;
            prevAbs = abs;
            prevW = b.HalfW;
            prevH = b.HalfH;
        }
        PlaceChain(_real, _need, m, maxShift, _placed, _blockSum, _blockCount);
        for (int k = 0; k < m; k++)
        {
            ref var b = ref _badges[_chain[k]];
            b.Shift = sign * _placed[k] - b.Rel;
        }
    }

    internal static void PlaceChain(double[] real, double[] need, int count, double maxShift, double[] placed, double[] blockSum, int[] blockCount)
    {
        int blocks = 0;
        double total = 0;
        for (int k = 0; k < count; k++)
        {
            total += need[k];
            placed[k] = total;
            blockSum[blocks] = real[k] - total;
            blockCount[blocks] = 1;
            blocks++;
            while (blocks > 1 && blockSum[blocks - 2] / blockCount[blocks - 2] > blockSum[blocks - 1] / blockCount[blocks - 1])
            {
                blockSum[blocks - 2] += blockSum[blocks - 1];
                blockCount[blocks - 2] += blockCount[blocks - 1];
                blocks--;
            }
        }
        int at = 0;
        for (int bl = 0; bl < blocks; bl++)
        {
            double mean = Math.Max(0, blockSum[bl] / blockCount[bl]);
            for (int c = 0; c < blockCount[bl]; c++, at++)
                placed[at] = Math.Clamp(mean + placed[at], real[at] - maxShift, real[at] + maxShift);
        }
    }

    internal static void Covered(float[] x, float[] y, float[] r, int count, bool[] covered)
    {
        for (int i = 0; i < count; i++)
        {
            covered[i] = false;
            for (int j = i + 1; j < count; j++)
            {
                float reach = r[i] * LabelReach + r[j];
                float dx = x[j] - x[i], dy = y[j] - y[i];
                if (dx * dx + dy * dy < reach * reach) { covered[i] = true; break; }
            }
        }
    }

    internal static string? Label(TelemetryFrame f, DriverEntry driver, int car, bool carNumbers) =>
        carNumbers ? driver.CarNumber
        : f.CarClassPosition[car] > 0 ? f.CarClassPosition[car].ToString(CultureInfo.InvariantCulture) : null;

    internal static int LapDiff(TelemetryFrame f, int car, double pct, double myPct, double myDist) =>
        FieldOrder.Distance(f, car) is { } dist ? (int)Math.Round(dist - myDist - FieldOrder.Wrap(pct, myPct)) : 0;

    private void Build(TrackMap map)
    {
        if (ReferenceEquals(map, _builtFor)) return;
        _builtFor = map;
        _extent = Extent(map);
        foreach (var old in new[] { _surface, _centre, _pit, _pitLines }) old?.Dispose();
        _surface = map.Surface.Count > 0 ? Path(map.Surface.Select(s => (s, true))) : null;
        _centre = Path(new[] { (map.Centerline, true) });
        var filled = map.Pit.Where(s => s.Filled).SelectMany(s => s.Figures).Select(fg => (fg.Points, fg.Closed)).ToList();
        var lines = map.Pit.Where(s => !s.Filled).ToList();
        _pit = filled.Count > 0 ? Path(filled.Select(x => (x.Points, true))) : null;
        _pitLines = lines.Count > 0 ? Path(lines.SelectMany(s => s.Figures).Select(fg => (fg.Points, fg.Closed))) : null;
        _pitWidth = lines.Count > 0 ? lines.Max(s => s.StrokeWidth) : 0;
    }

    private static GraphicsPath Path(IEnumerable<(PointF[] Points, bool Closed)> figures)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        foreach (var (pts, closed) in figures)
        {
            if (pts.Length < 2) continue;
            path.StartFigure();
            path.AddLines(pts);
            if (closed) path.CloseFigure();
        }
        return path;
    }

    internal static RectangleF Extent(TrackMap map)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Add(PointF pt, float r = 0)
        {
            x0 = Math.Min(x0, pt.X - r); y0 = Math.Min(y0, pt.Y - r);
            x1 = Math.Max(x1, pt.X + r); y1 = Math.Max(y1, pt.Y + r);
        }
        foreach (var fig in map.Surface) foreach (var pt in fig) Add(pt);
        foreach (var pt in map.Centerline) Add(pt, map.Width / 2);
        foreach (var s in map.Pit) foreach (var fig in s.Figures) foreach (var pt in fig.Points) Add(pt, s.Filled ? 0 : s.StrokeWidth / 2);
        if (map.StartLine is { } sf) { Add(sf.A); Add(sf.B); }
        foreach (var t in map.Turns) Add(t.At);
        return x0 > x1 ? RectangleF.Empty : RectangleF.FromLTRB(x0, y0, x1, y1);
    }
}
