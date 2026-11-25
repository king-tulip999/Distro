using System.Drawing;
using System.Globalization;
using Core.SimRacing.Hud.Editors;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;
using Core.SimRacing.TrackMaps;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class FlatMapWidget : HudWidget
{
    public const string Strip = "strip", Around = "around", Lanes = "lanes", Profile = "profile", Bar = "bar";

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Strip,
            new HudChoice(Strip, "Lap strip", "The whole lap as one line, start / finish at the left"),
            new HudChoice(Around, "Around me", "Your car stays in the middle"),
            new HudChoice(Lanes, "Class lanes", "One line per class"),
            new HudChoice(Profile, "Corner profile", "The line bends up for left turns and down for right turns"),
            new HudChoice(Bar, "Thin bar", "No title row")),
        HudOption.Bool("upright", "Upright", false, "Start / finish at the bottom, the lap running upwards"),
        HudOption.Choice("range", "Range", "half",
            new HudChoice("half", "½ lap"), new HudChoice("quarter", "¼ lap"), new HudChoice("eighth", "⅛ lap")),
        HudOption.Bool("gaps", "Gap labels", true, "Seconds to the nearest car ahead and behind"),
        HudOption.Bool("lapColors", "Lap colors", true, "Race only. Red edge: lapping you. Blue edge: a lap or more behind you"),
        HudOption.Number("bend", "Bend strength", 100, 50, 200, 10, "%"),
        HudOption.Bool("turns", "Turn numbers", true),
        HudOption.Bool("sectors", "Sector marks", true),
        HudOption.Bool("pit", "Cars on pit road", true, "On the pit lane under the track"),
        HudOption.Bool("positions", "Text on the dots", true),
        HudOption.Choice("dotText", "Text", "position", new HudChoice("position", "Class position"), new HudChoice("number", "Car number")),
        HudOption.Number("dotSize", "Dot size", 100, 50, 300, 10, "%"),
        HudOption.Color("myColor", "My color", "Until one is picked, the overlay's accent color"),
        HudOption.Color("carColor", "Other cars' color", "Until one is picked, grey or iRacing's class colors. A picked color is used for every class"),
        HudOption.Number("trackWidth", "Track width", 100, 50, 250, 10, "%"),
        HudOption.Number("edgeWidth", "Border width", 1, 0, 6, 0.5, "px", "0 for no border"),
        MapTrackLook.Look,
        MapTrackLook.TrackColor,
        MapTrackLook.EdgeColor,
        HudOption.Bool("startLine", "Start / finish line", true),
        HudClassFilter.Scope,
        HudClassFilter.List,
    };


    private const float StripDot = 7, AroundDot = 6.5f, LaneDot = 6.5f, ProfileDot = 6, BarDot = 5;
    private const float TrackWidth = 6, PitWidth = 3;
    private const float StripTrackY = 36, StripPitY = 22, StripHeight = 79, LabelAbove = 17;
    private const float AroundTrackY = 38, AroundGapY = 20, AroundHeight = 79;
    private const float LaneLabel = 40, LaneGap = 6, LanePad = 4, LaneHeight = 107f / 3;
    private const float ProfileHeight = 101, ProfileAmp = 0.3f, ProfileDrop = 4, ProfileLabel = 15;
    private const float BarHeight = 34, UprightWidth = 64, UprightEnd = 16;
    private static readonly string[] LaneParts = { "labels", "lanes" }, UprightParts = { "labels", "track" };
    private const double PitIn = 0.04, PitInBend = 0.02, PitOut = 0.035, PitOutBend = 0.02;

    private readonly HudMapSource _maps = new();
    private TrackMap? _builtFor;
    private bool _built;
    private double? _builtKm;
    private float[] _bends = Array.Empty<float>();
    private (double Start, double End)? _pitSpan;
    private double[] _turnPct = Array.Empty<double>();
    private string[] _turnText = Array.Empty<string>();

    private SessionInfo? _laneSession;
    private string? _laneScope, _laneList;
    private readonly Dictionary<int, int> _laneOf = new();
    private readonly List<string> _laneName = new();
    private readonly List<int> _laneClass = new();
    private float[] _laneY = Array.Empty<float>();

    private PointF[] _profile = Array.Empty<PointF>();
    private (float X0, float X1, float Mid, float Amp, float[]? Bends) _profileFor;

    private Pen? _dash;
    private Color _dashColor;
    private readonly PointF[] _marker = new PointF[3];
    private int? _lapFor;
    private string _lapText = "";

    private readonly float[] _dotX = new float[TelemetryFrame.MaxCars], _dotY = new float[TelemetryFrame.MaxCars], _dotR = new float[TelemetryFrame.MaxCars];
    private readonly int[] _dotCar = new int[TelemetryFrame.MaxCars];
    private readonly Color[] _dotColor = new Color[TelemetryFrame.MaxCars];
    private readonly bool[] _dotCovered = new bool[TelemetryFrame.MaxCars];

    private string _style = Strip;
    private bool _upright;
    private float _x0, _x1, _y, _pitY, _cx, _halfW, _mid, _amp, _top, _bottom;
    private double _myPct, _range;
    private float _trackW, _edgeW;
    private Color _trackColor, _edgeColor;
    private bool _startLine;

    public override void Dispose()
    {
        _dash?.Dispose();
        _dash = null;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        _style = v.Options.Choice("style");
        bool bar = _style == Bar;
        if (DrawWaiting(p, v, bar ? null : _style == Around ? "Around me" : "Track map")) return;
        var f = v.Frame;
        Build(_maps.Get(p, f), f.Session.TrackLengthKm);
        float d = p.Density;
        float size = (float)(v.Options.Number("dotSize") / 100);
        float dot = d * size * _style switch { Around => AroundDot, Lanes => LaneDot, Profile => ProfileDot, Bar => BarDot, _ => StripDot };
        int me = f.PlayerCarIdx;
        _myPct = me >= 0 && me < TelemetryFrame.MaxCars && f.CarLapDistPct[me] >= 0 ? f.CarLapDistPct[me] : 0;
        _upright = bar && v.Options.Bool("upright");
        _trackW = TrackWidth * d * (float)(v.Options.Number("trackWidth") / 100);
        _edgeW = (float)v.Options.Number("edgeWidth") * d;
        (_trackColor, _edgeColor) = MapTrackLook.Colors(p, v.Options);
        _startLine = v.Options.Bool("startLine");
        _labels.Clear();
        RectangleF area;
        if (bar)
        {
            p.Panel();
            area = p.Bounds;
        }
        else if (_style == Around) area = p.Header("Around me", LapText(f));
        else area = p.Header("Track map", f.Session.TrackDisplayName);

        switch (_style)
        {
            case Around: LayoutAround(p, v, area); break;
            case Lanes: LayoutLanes(p, v, area); break;
            case Profile: LayoutProfile(p, v, area); break;
            case Bar: LayoutBar(p, v, area); break;
            default: LayoutStrip(p, v, area); break;
        }
        Cars(p, v, dot);
        if (_style == Around) AroundMarks(p, v, dot);
        if (_style == Lanes) MyLaneLine(p, area);
    }

    private string LapText(TelemetryFrame f)
    {
        if (f.Lap != _lapFor)
        {
            _lapFor = f.Lap;
            _lapText = f.Lap is { } lap and > 0 ? "Lap " + lap.ToString(CultureInfo.InvariantCulture) : "";
        }
        return _lapText;
    }

    private void Build(TrackMap? map, double? km)
    {
        if (_built && ReferenceEquals(map, _builtFor) && km == _builtKm) return;
        _built = true;
        _builtFor = map;
        _builtKm = km;
        if (map is null)
        {
            _bends = Array.Empty<float>();
            _pitSpan = null;
            _turnPct = Array.Empty<double>();
            _turnText = Array.Empty<string>();
            return;
        }
        _bends = FlatMapShape.Bends(map, km);
        _pitSpan = FlatMapShape.PitSpan(map);
        var turns = map.Turns.Select(t => (Pct: map.PctOf(t.At), t.Text)).OrderBy(t => t.Pct).ToArray();
        _turnPct = turns.Select(t => t.Pct).ToArray();
        _turnText = turns.Select(t => t.Text).ToArray();
    }

    private float X(double pct) => _x0 + (_x1 - _x0) * (float)(pct - Math.Floor(pct));

    private static Color PitColor(HudPainter p) => p.P.MapPit ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.18);

    private void Track(HudPainter p, float x1, float y1, float x2, float y2)
    {
        if (_edgeW > 0) p.Line(x1, y1, x2, y2, _edgeColor, _trackW + 2 * _edgeW);
        p.Line(x1, y1, x2, y2, _trackColor, _trackW);
    }

    private float Reach(HudPainter p, float half, float beyond) => Math.Max(half * p.Density, _trackW / 2 + _edgeW + beyond * p.Density);

    private void StartLine(HudPainter p, float x, float y, float half)
    {
        if (!_startLine) return;
        float r = Reach(p, half, 3);
        p.Line(x, y - r, x, y + r, p.P.Heading, 3 * p.Density);
    }

    private void Tick(HudPainter p, float x, float y, float half)
    {
        float r = Reach(p, half, 2);
        p.Line(x, y - r, x, y + r, p.P.Faint, 1);
    }

    private readonly List<RectangleF> _labels = new();

    private void TurnText(HudPainter p, int i, float cx, float cy)
    {
        var font = p.Tiny;
        float h = p.LineHeight(font), w = p.Measure(_turnText[i], font) + 4;
        var box = new RectangleF(cx - w / 2, cy - h / 2, w, h);
        foreach (var other in _labels)
            if (other.IntersectsWith(box)) return;
        _labels.Add(box);
        p.Text(_turnText[i], font, p.P.Muted, box, HAlign.Center, VAlign.Center, false);
    }

    private void LayoutStrip(HudPainter p, HudView v, RectangleF area)
    {
        float d = p.Density;
        p.NeedBelow(area, StripHeight * d);
        _x0 = area.X + 14 * d;
        _x1 = area.Right - 14 * d;
        _y = area.Y + StripTrackY * d;
        _pitY = _y + StripPitY * d;
        if (v.Options.Bool("turns"))
            for (int i = 0; i < _turnPct.Length; i++) TurnText(p, i, X(_turnPct[i]), _y - LabelAbove * d);
        if (v.Options.Bool("pit") && _pitSpan is { } span) PitLane(p, span);
        Track(p, _x0, _y, _x1, _y);
        var sectors = v.Frame.Session.Sectors;
        bool marks = v.Options.Bool("sectors");
        if (marks)
            foreach (var s in sectors)
                if (s.StartPct is > 0 and < 1) Tick(p, MathF.Round(X(s.StartPct)) + 0.5f, _y, 8);
        StartLine(p, _x0, _y, 9);
        if (marks && sectors.Count > 1)
        {
            var font = p.Tiny;
            float h = p.LineHeight(font);
            for (int i = 0; i < sectors.Count; i++)
            {
                string label = SectorLabel(i);
                float x = X(Math.Clamp(sectors[i].StartPct, 0, 0.999999)) + 6 * d;
                p.Text(label, font, p.P.Faint, new RectangleF(x, _pitY + d - h / 2, p.Measure(label, font) + 2, h), HAlign.Left, VAlign.Center, false);
            }
        }
    }

    private static readonly string[] SectorNames = Enumerable.Range(1, 12).Select(i => "S" + i.ToString(CultureInfo.InvariantCulture)).ToArray();

    private static string SectorLabel(int i) => i < SectorNames.Length ? SectorNames[i] : "S" + (i + 1).ToString(CultureInfo.InvariantCulture);

    private void PitLane(HudPainter p, (double Start, double End) span)
    {
        float d = p.Density, w = _x1 - _x0;
        var pen = p.Pen(PitColor(p), PitWidth * d);
        var g = p.G;
        float xs = X(span.Start), xe = X(span.End), join = _y + 4 * d;
        float inEnd = xs + (float)PitIn * w, outStart = xe - (float)PitOut * w;
        Curve(g, pen, new PointF(xs, join), new PointF(xs + (float)PitInBend * w, _pitY), new PointF(inEnd, _pitY));
        Curve(g, pen, new PointF(outStart, _pitY), new PointF(outStart + (float)PitOutBend * w, _pitY), new PointF(xe, join));
        if (span.Start <= span.End)
        {
            if (outStart > inEnd) g.DrawLine(pen, inEnd, _pitY, outStart, _pitY);
        }
        else
        {
            if (_x1 > inEnd) g.DrawLine(pen, inEnd, _pitY, _x1, _pitY);
            if (outStart > _x0) g.DrawLine(pen, _x0, _pitY, outStart, _pitY);
        }
    }

    private static void Curve(Graphics g, Pen pen, PointF from, PointF control, PointF to)
    {
        var c1 = new PointF(from.X + (control.X - from.X) * 2 / 3, from.Y + (control.Y - from.Y) * 2 / 3);
        var c2 = new PointF(to.X + (control.X - to.X) * 2 / 3, to.Y + (control.Y - to.Y) * 2 / 3);
        g.DrawBezier(pen, from, c1, c2, to);
    }

    private void LayoutAround(HudPainter p, HudView v, RectangleF area)
    {
        float d = p.Density;
        _range = v.Options.Choice("range") switch { "quarter" => 0.25, "eighth" => 0.125, _ => 0.5 };
        p.NeedBelow(area, AroundHeight * d);
        _cx = area.X + area.Width / 2;
        _halfW = Math.Max(1, area.Width / 2 - 14 * d);
        _y = area.Y + AroundTrackY * d;
        Track(p, _cx - _halfW, _y, _cx + _halfW, _y);
        if (v.Options.Bool("turns"))
            for (int i = 0; i < _turnPct.Length; i++)
            {
                double dd = FieldOrder.Wrap(_turnPct[i], _myPct);
                if (Math.Abs(dd) < _range * 0.96) TurnText(p, i, AroundX(dd), _y - LabelAbove * d);
            }
        double sf = FieldOrder.Wrap(0, _myPct);
        if (Math.Abs(sf) < _range * 0.98) StartLine(p, AroundX(sf), _y, 9);
    }

    private float AroundX(double dd) => _cx + (float)(dd / _range) * _halfW;

    private void AroundMarks(HudPainter p, HudView v, float dot)
    {
        var f = v.Frame;
        float d = p.Density;
        int me = f.PlayerCarIdx;
        if (v.Options.Bool("gaps") && me >= 0 && me < TelemetryFrame.MaxCars)
        {
            int ahead = -1, behind = -1;
            double aheadD = double.MaxValue, behindD = double.MaxValue;
            for (int car = 0; car < TelemetryFrame.MaxCars; car++)
            {
                if (car == me || f.CarLapDistPct[car] < 0 || !f.Session.ByCar.TryGetValue(car, out var driver) || !driver.Races || !v.ClassShown(car)) continue;
                double dd = FieldOrder.Wrap(f.CarLapDistPct[car], _myPct);
                if (Math.Abs(dd) > _range) continue;
                if (dd > 0 && dd < aheadD) { aheadD = dd; ahead = car; }
                if (dd < 0 && -dd < behindD) { behindD = -dd; behind = car; }
            }
            var font = p.SmallBold;
            string? front = Gap(v, ahead), back = Gap(v, behind);
            float fw = front is null ? 0 : p.Measure(front, font) + 4, bw = back is null ? 0 : p.Measure(back, font) + 4;
            float fx = AroundX(aheadD) - fw / 2, bx = AroundX(-behindD) - bw / 2;
            float clash = front is not null && back is not null ? bx + bw + 4 * d - fx : 0;
            if (clash > 0)
            {
                fx += clash / 2;
                bx -= clash / 2;
            }
            float h = p.LineHeight(font), gy = _y + AroundGapY * d - h / 2;
            if (front is not null) p.Text(front, font, p.P.Text, new RectangleF(fx, gy, fw, h), HAlign.Center, VAlign.Center, false);
            if (back is not null) p.Text(back, font, p.P.Muted, new RectangleF(bx, gy, bw, h), HAlign.Center, VAlign.Center, false);
        }
        float tip = _y + 28 * d;
        _marker[0] = new PointF(_cx - 5 * d, tip + 6 * d);
        _marker[1] = new PointF(_cx + 5 * d, tip + 6 * d);
        _marker[2] = new PointF(_cx, tip);
        p.G.FillPolygon(p.Brush(p.P.Accent), _marker);
    }

    private static string? Gap(HudView v, int car)
    {
        var f = v.Frame;
        return car >= 0 && FieldOrder.GapSeconds(f, f.PlayerCarIdx, car, v.Race.Profile) is { } gap ? RaceFormat.Gap(gap, 1) : null;
    }

    private void LayoutLanes(HudPainter p, HudView v, RectangleF area)
    {
        float d = p.Density;
        var s = v.Frame.Session;
        BuildLanes(v, s);
        int count = Math.Max(1, _laneName.Count);
        float laneH = LaneHeight * d;
        p.NeedBelow(area, laneH * count + LanePad * d);
        var font = p.Font(10.5f, true);
        Span<RectangleF> cols = stackalloc RectangleF[2];
        p.Arrange(v, "flatmap.lanes", RectangleF.FromLTRB(area.X + 4 * d, area.Y, area.Right - 12 * d, area.Bottom), true, LaneGap * d, LaneParts, stackalloc float[] { LaneLabel * d, 0 }, cols);
        float labelX = cols[0].X;
        _x0 = cols[1].X;
        _x1 = cols[1].Right;
        for (int i = 0; i < count; i++)
        {
            float y = area.Y + laneH * i + laneH / 2;
            _laneY[i] = y;
            if (i < _laneName.Count)
                p.Text(_laneName[i], font, p.ClassColor(s, _laneClass[i]), new RectangleF(labelX, y - laneH / 2, LaneLabel * d, laneH));
            Track(p, _x0, y, _x1, y);
            StartLine(p, _x0, y, 7);
        }
    }

    private void MyLaneLine(HudPainter p, RectangleF area)
    {
        var color = HudPalette.Alpha(p.P.Accent, 115);
        if (_dash is null || _dashColor != color)
        {
            _dash?.Dispose();
            _dash = new Pen(color, 1) { DashPattern = new[] { 2f, 3f } };
            _dashColor = color;
        }
        float x = X(_myPct);
        float bottom = area.Y + LaneHeight * p.Density * Math.Max(1, _laneName.Count) + LanePad * p.Density;
        p.G.DrawLine(_dash, x, area.Y, x, bottom);
    }

    private void BuildLanes(HudView v, SessionInfo s)
    {
        string scope = v.Options.Raw(HudClassFilter.ScopeKey), list = v.Options.Raw(HudClassFilter.ListKey);
        if (ReferenceEquals(s, _laneSession) && scope == _laneScope && list == _laneList) return;
        _laneSession = s;
        _laneScope = scope;
        _laneList = list;
        _laneOf.Clear();
        _laneName.Clear();
        _laneClass.Clear();
        var classes = s.Drivers.Where(d => d.Races && v.ClassShown(d.CarIdx)).GroupBy(d => d.ClassId)
            .OrderBy(g => g.First().ClassEstLapTime ?? double.MaxValue).ToList();
        for (int i = 0; i < classes.Count; i++)
        {
            _laneOf[classes[i].Key] = i;
            _laneName.Add(classes[i].First().ClassLabel);
            _laneClass.Add(classes[i].Key);
        }
        _laneY = new float[Math.Max(1, classes.Count)];
    }

    private void LayoutProfile(HudPainter p, HudView v, RectangleF area)
    {
        float d = p.Density;
        float strength = (float)(v.Options.Number("bend") / 100);
        _amp = ProfileHeight * ProfileAmp * d * strength;
        float height = ProfileHeight * d + Math.Max(0, _amp - ProfileHeight * ProfileAmp * d) * 2;
        p.NeedBelow(area, height);
        _mid = area.Y + height / 2 + ProfileDrop * d;
        _x0 = area.X + 14 * d;
        _x1 = area.Right - 14 * d;
        var key = (_x0, _x1, _mid, _amp, (float[]?)_bends);
        if (key != _profileFor)
        {
            _profileFor = key;
            int n = Math.Max(2, (int)((_x1 - _x0) / 1.5f));
            _profile = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double pct = (double)i / n;
                _profile[i] = new PointF(_x0 + (_x1 - _x0) * (float)pct, ProfileY(i == n ? 0.999999 : pct));
            }
        }
        if (_edgeW > 0) p.G.DrawLines(p.Pen(_edgeColor, _trackW + 2 * _edgeW), _profile);
        p.G.DrawLines(p.Pen(_trackColor, _trackW), _profile);
        StartLine(p, _x0, ProfileY(0), 9);
        if (v.Options.Bool("turns"))
            for (int i = 0; i < _turnPct.Length; i++)
            {
                double pct = _turnPct[i];
                float side = FlatMapShape.At(_bends, pct) >= 0 ? -1 : 1;
                TurnText(p, i, X(pct), ProfileY(pct) + side * ProfileLabel * d);
            }
    }

    private float ProfileY(double pct) => _mid - FlatMapShape.At(_bends, pct) * _amp;

    private void LayoutBar(HudPainter p, HudView v, RectangleF area)
    {
        float d = p.Density;
        if (!_upright)
        {
            p.NeedBelow(area, BarHeight * d);
            _y = area.Y + Math.Min(area.Height, BarHeight * d) / 2;
            _x0 = area.X + 16 * d;
            _x1 = area.Right - 16 * d;
            Track(p, _x0, _y, _x1, _y);
            foreach (var s in v.Frame.Session.Sectors)
                if (s.StartPct is > 0 and < 1) Tick(p, MathF.Round(X(s.StartPct)) + 0.5f, _y, 6);
            StartLine(p, _x0, _y, 7);
            return;
        }
        p.Need(UprightWidth * d, (float)v.Config.H);
        float width = Math.Min(area.Width, UprightWidth * d);
        Span<RectangleF> cols = stackalloc RectangleF[2];
        p.Arrange(v, "flatmap.upright", new RectangleF(area.X, area.Y, width, area.Height), true, 0, UprightParts, stackalloc float[] { Math.Max(1, width / 2 - 7 * d), 26 * d }, cols);
        var labels = cols[0];
        _cx = cols[1].X + 13 * d;
        _bottom = area.Bottom - UprightEnd * d;
        _top = area.Y + UprightEnd * d;
        Track(p, _cx, _bottom, _cx, _top);
        if (_startLine)
        {
            float r = Reach(p, 9, 3);
            p.Line(_cx - r, _bottom, _cx + r, _bottom, p.P.Heading, 3 * d);
        }
        var font = p.Tiny;
        float h = p.LineHeight(font), last = float.MaxValue;
        for (int i = 0; i < _turnPct.Length; i++)
        {
            float y = UprightY(_turnPct[i]);
            float room = labels.Width;
            if (last - y < 12 * d || p.Measure(_turnText[i], font) > room - 2 * d) continue;
            p.Text(_turnText[i], font, p.P.Muted, new RectangleF(labels.X, y - h / 2, room, h), labels.X < cols[1].X ? HAlign.Right : HAlign.Left, VAlign.Center, false);
            last = y;
        }
    }

    private float UprightY(double pct) => _bottom - (_bottom - _top) * (float)(pct - Math.Floor(pct));

    private bool Place(TelemetryFrame f, int car, double pct, bool onPit, bool pitLane, out float x, out float y)
    {
        x = y = 0;
        switch (_style)
        {
            case Around:
                double dd = FieldOrder.Wrap(pct, _myPct);
                if (Math.Abs(dd) > _range * 0.98) return false;
                x = AroundX(dd);
                y = _y;
                return true;
            case Lanes:
                if (!f.Session.ByCar.TryGetValue(car, out var driver) || !_laneOf.TryGetValue(driver.ClassId, out int lane) || lane >= _laneY.Length) return false;
                x = X(pct);
                y = _laneY[lane];
                return true;
            case Profile:
                x = X(pct);
                y = ProfileY(pct);
                return true;
            case Bar when _upright:
                x = _cx;
                y = UprightY(pct);
                return true;
            case Bar:
                x = X(pct);
                y = _y;
                return true;
            default:
                x = X(pct);
                y = onPit && pitLane ? _pitY : _y;
                return true;
        }
    }

    private void Cars(HudPainter p, HudView v, float dot)
    {
        var f = v.Frame;
        var s = f.Session;
        float d = p.Density;
        bool multiclass = s.NumCarClasses > 1;
        bool around = _style == Around;
        bool text = (_style == Strip || _style == Lanes) && v.Options.Bool("positions");
        bool carNumbers = _style == Strip && v.Options.Value("dotText") == "number";
        bool pitLane = _style == Strip && v.Options.Bool("pit");
        int me = f.PlayerCarIdx;
        bool lapColors = around && v.Options.Bool("lapColors") && f.SessionKind == SessionKind.Race && me >= 0 && me < TelemetryFrame.MaxCars && f.CarLapDistPct[me] >= 0;
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
                if (onPit && !mine && _style == Strip && !pitLane) continue;
                if (!Place(f, car, pct, onPit, pitLane, out float x, out float y)) continue;
                var color = mine ? myColor : around || !multiclass || oneColor ? others : p.ClassColor(s, driver.ClassId);
                if (onPit && !mine) color = HudPalette.Alpha(color, 110);
                float r = mine ? dot + 2 * d : dot;
                int laps = lapColors && !mine ? MapWidget.LapDiff(f, car, pct, _myPct, myDist) : 0;
                if (laps != 0) p.Dot(x, y, r + 2 * d, laps > 0 ? p.P.Lapping : p.P.Lapped);
                else p.Dot(x, y, r + (mine ? 2 : 1) * d, mine ? p.P.Heading : p.P.Bg);
                p.Dot(x, y, r, color);
                _dotX[n] = x;
                _dotY[n] = y;
                _dotR[n] = r;
                _dotCar[n] = car;
                _dotColor[n] = color;
                n++;
            }
        if (!text) return;
        MapWidget.Covered(_dotX, _dotY, _dotR, n, _dotCovered);
        for (int i = 0; i < n; i++)
        {
            int car = _dotCar[i];
            if (car == me || _dotCovered[i] || !s.ByCar.TryGetValue(car, out var driver) || MapWidget.Label(f, driver, car, carNumbers) is not { Length: > 0 } label) continue;
            DotLabels.Draw(p, label, _dotX[i], _dotY[i], _dotR[i], _dotColor[i]);
        }
    }
}
