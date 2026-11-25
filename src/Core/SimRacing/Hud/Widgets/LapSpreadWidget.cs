using System.Drawing;
using System.Globalization;
using Core.SimRacing.Hud.Editors;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class LapSpreadWidget : HudWidget
{
    public const string Dots = "dots", Ranking = "ranking", Histogram = "histogram";
    public const string Pace = "pace", Best = "best";
    public const double W = 440, H = 240;
    private const int MaxClasses = 8, MaxLanes = 5, MaxBuckets = 12;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] DotsParts = { "rows", "axis", "tiles" }, RankingParts = { "head", "list", "foot" }, HistogramParts = { "chart", "axis", "tiles" };
    private static readonly string[] DotsTiles = { "rank", "fastest", "median" }, HistogramTiles = { "pace", "median", "cars" };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Dots,
            new HudChoice(Dots, "Dot strip", "One dot per car on a time axis, a row per class"),
            new HudChoice(Ranking, "Pace ranking", "Cars ranked by recent pace with their road position"),
            new HudChoice(Histogram, "Histogram", "How your class's lap times are spread")),
        TitleRow,
        HudOption.Choice("basis", "Lap time used", Pace,
            new HudChoice(Pace, "Recent average", "The average of each car's last clean laps; laps on pit road or under caution are left out"),
            new HudChoice(Best, "Best lap", "Each car's best lap this session")),
        HudOption.Int("laps", "Laps averaged", 5, 2, RaceState.CarLapHistory),
        HudClassFilter.Scope,
        HudClassFilter.List,
    };

    private readonly int[] _car = new int[TelemetryFrame.MaxCars], _cls = new int[TelemetryFrame.MaxCars];
    private readonly double[] _pace = new double[TelemetryFrame.MaxCars];
    private int _count;

    private readonly int[] _classIds = new int[MaxClasses];
    private readonly double[] _classFastest = new double[MaxClasses];
    private int _classCount;

    private readonly double[] _sorted = new double[TelemetryFrame.MaxCars];
    private readonly int[] _sortedCar = new int[TelemetryFrame.MaxCars];
    private readonly float[] _xs = new float[TelemetryFrame.MaxCars], _laneEnds = new float[MaxLanes];
    private readonly int[] _lanes = new int[TelemetryFrame.MaxCars];
    private readonly int[] _buckets = new int[MaxBuckets];

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Lap time spread")) return;
        Gather(v);
        switch (v.Options.Choice("style"))
        {
            case Ranking: DrawRanking(p, v); break;
            case Histogram: DrawHistogram(p, v); break;
            default: DrawDots(p, v); break;
        }
    }

    private static bool UsesBest(HudView v) => v.Options.Choice("basis") == Best;

    private static string BasisText(HudView v) => UsesBest(v) ? "Best laps" : PooledText.Number("Avg of last ", v.Options.Int("laps"), " laps");

    private void Gather(HudView v)
    {
        var f = v.Frame;
        bool best = UsesBest(v);
        int laps = v.Options.Int("laps");
        _count = 0;
        for (int car = 0; car < TelemetryFrame.MaxCars; car++)
        {
            if (!f.Session.ByCar.TryGetValue(car, out var d) || !d.Races) continue;
            double? pace = best ? f.CarBestLap[car] > 0 ? f.CarBestLap[car] : null : v.Race.PaceOf(car, laps);
            if (pace is not { } t || t <= 0 || !double.IsFinite(t)) continue;
            _car[_count] = car;
            _cls[_count] = d.ClassId;
            _pace[_count] = t;
            _count++;
        }
    }

    private int MyClass(HudView v)
    {
        if (v.Frame.Session.Player is { } me) return me.ClassId;
        int fastest = -1;
        for (int i = 0; i < _count; i++)
            if (fastest < 0 || _pace[i] < _pace[fastest]) fastest = i;
        return fastest >= 0 ? _cls[fastest] : 0;
    }

    private int SortClass(int classId, bool filtered, HudView v)
    {
        int n = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_cls[i] != classId) continue;
            if (filtered && !v.ClassShown(_car[i])) continue;
            _sorted[n] = _pace[i];
            _sortedCar[n] = _car[i];
            n++;
        }
        Array.Sort(_sorted, _sortedCar, 0, n);
        return n;
    }

    private double? PaceOfCar(int car)
    {
        for (int i = 0; i < _count; i++)
            if (_car[i] == car) return _pace[i];
        return null;
    }

    private void Classes(HudView v)
    {
        _classCount = 0;
        for (int i = 0; i < _count; i++)
        {
            if (!v.ClassShown(_car[i])) continue;
            int at = Array.IndexOf(_classIds, _cls[i], 0, _classCount);
            if (at < 0)
            {
                if (_classCount >= MaxClasses) continue;
                at = _classCount++;
                _classIds[at] = _cls[i];
                _classFastest[at] = double.MaxValue;
            }
            if (_pace[i] < _classFastest[at]) _classFastest[at] = _pace[i];
        }
        Array.Sort(_classFastest, _classIds, 0, _classCount);
    }

    private static string ClassName(HudView v, int classId)
    {
        var drivers = v.Frame.Session.Drivers;
        for (int i = 0; i < drivers.Count; i++)
            if (drivers[i].ClassId == classId && drivers[i].Races) return drivers[i].ClassLabel;
        return "";
    }

    private static void Empty(HudPainter p, HudView v, RectangleF r) =>
        p.Text(UsesBest(v) ? "No lap times yet" : "Pace shows after each car's first clean lap", p.Small, p.P.Faint, r, HAlign.Center, VAlign.Center);

    private float ClassPill(HudPainter p, HudView v, int classId, float right, float cy)
    {
        string name = ClassName(v, classId);
        if (name.Length == 0 || v.Frame.Session.NumCarClasses <= 1 && _classCount <= 1) return right;
        var font = p.Font(10.5f, true);
        float w = p.Measure(name, font) + 10 * p.Density, h = p.LineHeight(font) + 2 * p.Density;
        p.Pill(new RectangleF(right - w, cy - h / 2, w, h), name, p.ClassColor(v.Frame.Session, classId), font);
        return right - w - 6 * p.Density;
    }

    private RectangleF Head(HudPainter p, HudView v, string title, int? pillClass)
    {
        if (!p.TitleRowShown) return p.Header(title);
        float d = p.Density;
        string basis = BasisText(v);
        float titleW = 22 * d + p.Measure(p.LabelText(title), p.Label) * 1.06f + 12 * d;
        float basisW = p.Measure(basis, p.Small) + 8 * d;
        string name = pillClass is { } pc && v.Frame.Session.NumCarClasses > 1 ? ClassName(v, pc) : "";
        float pillW = name.Length > 0 ? p.Measure(name, p.Font(10.5f, true)) + 16 * d : 0;
        float room = p.Bounds.Width;
        bool showPill = pillW > 0 && titleW + pillW <= room;
        bool showBasis = titleW + basisW + (showPill ? pillW : 0) <= room;
        var area = p.Header(title, showBasis ? basis : null);
        if (showPill && pillClass is { } cls) ClassPill(p, v, cls, room - 8 * d - (showBasis ? basisW : 0), p.PillHeight / 2);
        return area;
    }

    private static void Tiles(HudPainter p, HudView v, string group, string[] ids, RectangleF r, string a, string aLabel, Color aColor, string b, string bLabel, string c, string cLabel)
    {
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, group, r, true, 4 * p.Density, ids, stackalloc float[] { 0, 0, 0 }, boxes);
        p.BoxedStat(boxes[0], aLabel, a, aColor, p.Bold);
        p.BoxedStat(boxes[1], bLabel, b, null, p.Bold);
        p.BoxedStat(boxes[2], cLabel, c, null, p.Bold);
    }

    private static RectangleF TilesIn(RectangleF box, RectangleF area, float tileH) =>
        new(box.X, box.Y <= area.Y ? box.Y : box.Bottom - tileH, box.Width, tileH);

    private static string Signed(double? s) => s is { } x ? RaceFormat.Gap(x, 2) : RaceFormat.Missing;

    private void DrawDots(HudPainter p, HudView v)
    {
        float d = p.Density;
        var area = Head(p, v, "Lap time spread", null);
        var f = v.Frame;
        Classes(v);
        float tileH = p.BoxedStatHeight(p.Bold), axisH = p.LineHeight(p.Tiny) + 4 * d, rowMin = 22 * d;
        p.NeedBelow(area, 30 * d * Math.Max(1, _classCount) + axisH + 6 * d + tileH);
        if (_classCount == 0) { Empty(p, v, area); return; }

        bool showTiles = area.Height >= rowMin * Math.Min(_classCount, 2) + axisH + 6 * d + tileH;
        bool showAxis = area.Height - (showTiles ? tileH + 6 * d : 0) >= rowMin + axisH;
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "spread.dots", area, false, 0, DotsParts, stackalloc float[] { 0, showAxis ? axisH : -1, showTiles ? tileH + 6 * d : -1 }, boxes);
        var rowsArea = boxes[0];
        var axis = boxes[1];
        var tiles = TilesIn(boxes[2], area, tileH);
        int me = f.PlayerCarIdx, myClass = MyClass(v);
        int rows = Math.Clamp((int)(rowsArea.Height / rowMin), 1, _classCount);
        if (rows < _classCount)
        {
            int mine = Array.IndexOf(_classIds, myClass, 0, _classCount);
            if (mine >= rows) { _classIds[rows - 1] = _classIds[mine]; _classFastest[rows - 1] = _classFastest[mine]; }
            _classCount = rows;
        }
        float rowH = rowsArea.Height / _classCount;

        var pillFont = p.Font(10.5f, true);
        float pillW = 0;
        for (int k = 0; k < _classCount; k++) pillW = Math.Max(pillW, p.Measure(ClassName(v, _classIds[k]), pillFont));
        bool pills = _classCount > 1 || f.Session.NumCarClasses > 1;
        pillW = pills ? pillW + 10 * d : 0;
        float moreW = p.Measure("+00", p.Tiny) + 4 * d;
        float x0 = area.X + pillW + (pills ? 8 * d : 0) + 6 * d, x1 = area.Right - moreW - 6 * d;
        if (x1 - x0 < 40 * d) { Empty(p, v, area); return; }

        int deltas = 0;
        for (int k = 0; k < _classCount; k++)
            for (int i = 0; i < _count; i++)
                if (_cls[i] == _classIds[k] && v.ClassShown(_car[i])) _sorted[deltas++] = _pace[i] - _classFastest[k];
        Array.Sort(_sorted, 0, deltas);
        double span = LapSpread.AxisSpan(_sorted.AsSpan(1, Math.Max(0, deltas - 1)));
        float X(double delta) => x0 + (float)(Math.Min(delta, span) / span) * (x1 - x0);

        float r = 4 * d, meR = 6 * d;
        var dotColor = HudPalette.Mix(p.P.Muted, p.PanelColor, 0.25);
        double? myPace = null, myFastest = null, myMedian = null;
        int myRank = 0, myCount = 0;
        for (int k = 0; k < _classCount; k++)
        {
            int classId = _classIds[k];
            float cy = rowsArea.Y + rowH * k + rowH / 2;
            float pillH = Math.Min(rowH - 4 * d, p.LineHeight(pillFont) + 2 * d);
            if (pills) p.Pill(new RectangleF(area.X, cy - pillH / 2, pillW, pillH), ClassName(v, classId), p.ClassColor(f.Session, classId), pillFont);
            p.Line(x0 - 4 * d, cy, x1 + 4 * d, cy, p.P.LineStrong, 1);
            int n = SortClass(classId, true, v);
            double fastest = _sorted[0], median = LapSpread.Median(_sorted.AsSpan(0, n));
            float mx = X(median - fastest), half = Math.Min(rowH / 2 - 1, 14 * d);
            for (float y = cy - half; y < cy + half; y += 4 * d) p.Line(mx, y, mx, Math.Min(y + 2 * d, cy + half), p.P.Faint, 1);
            for (int i = 0; i < n; i++) _xs[i] = X(_sorted[i] - fastest);
            int lanes = Math.Clamp((int)((rowH - 2 * d) / (r * 2 + d)), 1, MaxLanes);
            LapSpread.Lanes(_xs.AsSpan(0, n), r * 2, lanes, _lanes, _laneEnds);
            int hidden = 0, mine = -1;
            for (int i = 0; i < n; i++)
            {
                if (_sortedCar[i] == me) { mine = i; continue; }
                if (_lanes[i] < 0 || _sorted[i] - fastest > span + 1e-6) { hidden++; continue; }
                float y = cy + LapSpread.LaneOffset(_lanes[i]) * (r * 2 + d);
                if (i == 0) p.Dot(_xs[i], y, r + d, p.P.Fastest);
                else p.Dot(_xs[i], y, r, dotColor);
            }
            if (mine >= 0)
            {
                float x = _xs[mine], y = cy + LapSpread.LaneOffset(Math.Max(0, _lanes[mine])) * (r * 2 + d);
                p.Dot(x, y, meR + 1.5f * d, p.P.Heading);
                p.Dot(x, y, meR, mine == 0 ? p.P.Fastest : p.P.Player);
                myPace = _sorted[mine];
                myFastest = fastest;
                myMedian = median;
                myRank = LapSpread.RankOf(_sorted.AsSpan(0, n), _sorted[mine]);
                myCount = n;
            }
            if (hidden > 0) p.Text(PooledText.Number("+", hidden), p.Tiny, p.P.Muted, new RectangleF(x1 + 6 * d, cy - rowH / 2, moreW, rowH), HAlign.Left, VAlign.Center, false);
        }

        if (showAxis) DrawAxis(p, area, axis, x0, x1, span);
        if (!showTiles) return;
        string rank = myRank > 0 ? PooledText.Number("P", myRank) : RaceFormat.Missing;
        Tiles(p, v, "spread.dots.tiles", DotsTiles, tiles, rank, "Your pace rank", myRank > 0 ? p.P.AccentText : p.P.Muted,
            myPace is { } mp && myFastest is { } mf ? RaceFormat.Plus(mp - mf, 2) : RaceFormat.Missing, "To fastest",
            myPace is { } mp2 && myMedian is { } mm ? Signed(mp2 - mm) : RaceFormat.Missing, "To median");
    }

    private static void DrawAxis(HudPainter p, RectangleF area, RectangleF axis, float x0, float x1, double span)
    {
        float d = p.Density, axisH = axis.Height;
        float X(double delta) => x0 + (float)(Math.Min(delta, span) / span) * (x1 - x0);
        p.Line(x0, axis.Y, x1, axis.Y, HudPalette.Alpha(p.P.LineStrong, 200), 1);
        double step = LapSpread.NiceStep(span, Math.Max(1, (int)((x1 - x0) / (48 * d))));
        for (double t = 0; t <= span + 1e-6; t += step)
        {
            float x = X(t);
            p.Line(x, axis.Y, x, axis.Y + 3 * d, p.P.LineStrong, 1);
            string label = t < 1e-6 ? "0" : PooledText.Number("+", t, step < 1 ? "0.0#" : "0", Inv);
            float lw = p.Measure(label, p.Tiny) + 2;
            float lx = Math.Clamp(x - lw / 2, area.X, area.Right - lw);
            p.Text(label, p.Tiny, p.P.Faint, new RectangleF(lx, axis.Y + 3 * d, lw, axisH - 3 * d), HAlign.Center, VAlign.Center, false);
        }
    }

    private void DrawRanking(HudPainter p, HudView v)
    {
        float d = p.Density;
        var f = v.Frame;
        int myClass = MyClass(v);
        _classCount = 0;
        var area = Head(p, v, "Pace ranking", myClass);
        int n = SortClass(myClass, false, v);
        var font = p.Font(13f, false);
        float rowH = p.LineHeight(font) + 7 * d, headH = p.LineHeight(p.Label) + 4 * d, footH = p.LineHeight(p.Small) + 8 * d;
        p.NeedBelow(area, headH + rowH * 6);
        if (n == 0) { Empty(p, v, area); return; }

        int me = f.PlayerCarIdx, mine = -1;
        for (int i = 0; i < n; i++) if (_sortedCar[i] == me) mine = i;
        double myPace = mine >= 0 ? _sorted[mine] : double.NaN;
        int myRoad = mine >= 0 ? RoadPosition(f, me) : 0;

        int threat = -1;
        for (int i = 0; i < n && mine >= 0; i++)
        {
            int road = RoadPosition(f, _sortedCar[i]);
            if (i < mine && road > myRoad && myRoad > 0 && (threat < 0 || road < RoadPosition(f, _sortedCar[threat]))) threat = i;
        }
        bool foot = threat >= 0 && area.Height >= headH + rowH * 4 + footH;
        float bodyBottom = foot ? area.Bottom - footH : area.Bottom;
        int fit = Math.Max(1, (int)((bodyBottom - area.Y - headH) / rowH));
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "spread.ranking", area, false, 0, RankingParts, stackalloc float[] { headH, 0, foot ? footH : -1 }, boxes);
        int shown = Math.Min(n, fit);
        bool swapMe = mine >= shown;

        float rankW = p.Measure("00", font) + 6 * d, numW = p.Measure("#000", font) + 6 * d;
        float avgW = p.Measure("0:00.00", font) + 6 * d, gapW = p.Measure("+00.00", font) + 6 * d, roadW = p.Measure("P00", p.Tiny) + 10 * d;
        float xRank = area.X, xNum = xRank + rankW, xName = xNum + numW, xGap = area.Right - gapW, xAvg = xGap - avgW, xRoad = xAvg - roadW - 4 * d;
        bool roadCol = xRoad - xName > 60 * d;
        if (!roadCol) xRoad = xAvg;
        float y = boxes[0].Y;
        p.Text(p.LabelText("#"), p.Label, p.LabelColor, new RectangleF(xRank, y, rankW, headH), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText("Car"), p.Label, p.LabelColor, new RectangleF(xNum, y, numW, headH), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText("Driver"), p.Label, p.LabelColor, new RectangleF(xName, y, xRoad - xName, headH), HAlign.Left, VAlign.Center);
        if (roadCol) p.Text(p.LabelText("Road"), p.Label, p.LabelColor, new RectangleF(xRoad, y, roadW, headH), HAlign.Center, VAlign.Center, false);
        p.Text(p.LabelText(UsesBest(v) ? "Best" : "Avg"), p.Label, p.LabelColor, new RectangleF(xAvg, y, avgW, headH), HAlign.Right, VAlign.Center, false);
        p.Text(p.LabelText("Gap"), p.Label, p.LabelColor, new RectangleF(xGap, y, gapW, headH), HAlign.Right, VAlign.Center, false);
        y = boxes[1].Y;

        for (int row = 0; row < shown; row++)
        {
            int i = swapMe && row == shown - 1 ? mine : row;
            int car = _sortedCar[i];
            bool isMe = car == me;
            var r = new RectangleF(area.X - 8 * d, y, area.Width + 16 * d, rowH);
            if (isMe) p.Fill(r, p.Solid(p.P.Player, 200));
            else if (row > 0) p.Line(area.X, y, area.Right, y, p.P.Line, 1);
            var ink = isMe ? p.P.PlayerText : p.P.Text;
            var dim = isMe ? p.P.PlayerText : p.P.Muted;
            int rank = LapSpread.RankOf(_sorted.AsSpan(0, n), _sorted[i]);
            p.Text(PooledText.Number("", rank), font, dim, new RectangleF(xRank, y, rankW, rowH), HAlign.Left, VAlign.Center, false);
            p.Text(Number(f, car), font, ink, new RectangleF(xNum, y, numW, rowH), HAlign.Left, VAlign.Center, false);
            p.Text(isMe ? "You" : NameOf(f, car), isMe ? p.Font(13f, true) : font, ink, new RectangleF(xName, y, xRoad - xName - 4 * d, rowH), HAlign.Left, VAlign.Center);
            if (roadCol && RoadPosition(f, car) is > 0 and var road)
            {
                bool stuck = road - rank >= 3;
                float ch = p.LineHeight(p.Tiny) + 3 * d;
                var chip = new RectangleF(xRoad + 2 * d, y + (rowH - ch) / 2, roadW - 4 * d, ch);
                var bg = isMe ? HudPalette.Alpha(p.P.PlayerText, 40) : stuck ? p.P.WarnSoft : HudPalette.Mix(p.PanelColor, p.P.Muted, 0.22);
                p.Pill(chip, PooledText.Number("P", road), bg, p.Tiny, isMe ? p.P.PlayerText : stuck ? p.P.WarnText : p.P.Text);
            }
            p.Text(RaceFormat.LapTime(_sorted[i], 2), font, isMe ? ink : i == 0 ? p.P.Fastest : ink, new RectangleF(xAvg, y, avgW, rowH), HAlign.Right, VAlign.Center, false);
            p.Text(i == 0 ? RaceFormat.Missing : RaceFormat.Plus(_sorted[i] - _sorted[0], 2), font, dim, new RectangleF(xGap, y, gapW, rowH), HAlign.Right, VAlign.Center, false);
            y += rowH;
        }

        if (foot)
        {
            var fr = new RectangleF(area.X - 8 * d, boxes[2].Y <= area.Y ? boxes[2].Y : boxes[2].Y + 4 * d, area.Width + 16 * d, footH - 4 * d);
            p.Fill(fr, p.Solid(p.P.WarnSoft, 200));
            int car = _sortedCar[threat];
            string lead = PooledText.Concat(Number(f, car), PooledText.Number(" is P", RoadPosition(f, car), " on the road but "));
            string text = PooledText.Concat(lead, PooledText.Number("", myPace - _sorted[threat], "0.00", Inv, " a lap faster"));
            p.Text(text, p.Small, p.P.WarnText, fr.Inflated(-8 * d, 0), HAlign.Left, VAlign.Center);
        }
    }

    private static int RoadPosition(TelemetryFrame f, int car) =>
        (uint)car >= TelemetryFrame.MaxCars ? 0 : f.CarClassPosition[car] > 0 ? f.CarClassPosition[car] : f.CarPosition[car];

    private static string Number(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) && d.CarNumber.Length > 0 ? PooledText.Concat("#", d.CarNumber) : RaceFormat.Missing;

    private static string NameOf(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) ? RaceFormat.Name(d.Name.Length > 0 ? d.Name : d.TeamName, "full") : RaceFormat.Missing;

    private void DrawHistogram(HudPainter p, HudView v)
    {
        float d = p.Density;
        var f = v.Frame;
        int myClass = MyClass(v);
        _classCount = 0;
        var area = Head(p, v, "Field pace", myClass);
        float tileH = p.BoxedStatHeight(p.Bold), axisH = p.LineHeight(p.Tiny) + 4 * d;
        p.NeedBelow(area, 90 * d + axisH + 6 * d + tileH);
        int n = SortClass(myClass, false, v);
        if (n == 0) { Empty(p, v, area); return; }

        bool showTiles = area.Height >= 40 * d + axisH + 6 * d + tileH;
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "spread.histogram", area, false, 0, HistogramParts, stackalloc float[] { 0, axisH, showTiles ? tileH + 6 * d : -1 }, boxes);
        var chart = RectangleF.FromLTRB(boxes[0].X, boxes[0].Y + 4 * d, boxes[0].Right, boxes[0].Bottom);
        var axis = boxes[1];
        var tiles = TilesIn(boxes[2], area, tileH);

        double fastest = _sorted[0], median = LapSpread.Median(_sorted.AsSpan(0, n));
        Span<double> deltas = stackalloc double[n];
        for (int i = 0; i < n; i++) deltas[i] = _sorted[i] - fastest;
        double span = LapSpread.AxisSpan(deltas.Length > 1 ? deltas[1..] : deltas);
        int maxBuckets = Math.Clamp((int)(chart.Width / (22 * d)), 3, MaxBuckets);
        var (start, width, count) = LapSpread.BucketRange(fastest, Math.Min(_sorted[n - 1], fastest + span), maxBuckets);
        var counts = _buckets.AsSpan(0, count);
        LapSpread.Fill(_sorted.AsSpan(0, n), start, width, counts);
        int most = 1;
        foreach (int c in counts) most = Math.Max(most, c);

        int me = f.PlayerCarIdx, mine = -1;
        for (int i = 0; i < n; i++) if (_sortedCar[i] == me) mine = i;
        int myBucket = mine >= 0 ? LapSpread.BucketOf(_sorted[mine], start, width, count) : -1;

        float total = count * width <= 0 ? 1 : (float)(count * width);
        float mx = chart.X + (float)((median - start) / total) * chart.Width;
        mx = Math.Clamp(mx, chart.X + 1, chart.Right - 1);
        float gap = 4 * d, bw = (chart.Width - gap * (count - 1)) / count;
        int medianBucket = LapSpread.BucketOf(median, start, width, count);
        float medianTop = chart.Bottom - 8 * d;
        var barColor = HudPalette.Mix(p.PanelColor, p.P.Muted, 0.38);
        for (int b = 0; b < count; b++)
        {
            float x = chart.X + b * (bw + gap);
            p.Fill(new RectangleF(x, chart.Bottom - 1, bw, 1), p.P.Line);
            if (counts[b] == 0) continue;
            float h = Math.Max(2 * d, (chart.Height - p.LineHeight(p.Tiny)) * counts[b] / most);
            var bar = new RectangleF(x, chart.Bottom - h, bw, h);
            p.Fill(bar, b == myBucket ? p.P.Player : barColor, p.CornerRadius(3));
            if (b == medianBucket) medianTop = Math.Min(medianTop, bar.Y);
            if (bw >= p.Measure("00", p.Tiny) + 2)
                p.Text(PooledText.Number("", counts[b]), p.Tiny, b == myBucket ? p.P.Heading : p.P.Faint, new RectangleF(x, bar.Y - p.LineHeight(p.Tiny), bw, p.LineHeight(p.Tiny)), HAlign.Center, VAlign.Bottom, false);
        }
        p.Fill(new RectangleF(mx - d, medianTop, 2 * d, chart.Bottom + 3 * d - medianTop), p.P.Heading);

        string lo = RaceFormat.LapTime(start, 1), hi = RaceFormat.LapTime(start + count * width, 1);
        float low = p.Measure(lo, p.Tiny) + 2, hiw = p.Measure(hi, p.Tiny) + 2, medW = p.Measure("median", p.Tiny) + 2;
        p.Text(lo, p.Tiny, p.P.Faint, new RectangleF(chart.X, axis.Y, low, axisH), HAlign.Left, VAlign.Center, false);
        p.Text(hi, p.Tiny, p.P.Faint, new RectangleF(chart.Right - hiw, axis.Y, hiw, axisH), HAlign.Right, VAlign.Center, false);
        float medX = Math.Clamp(mx - medW / 2, chart.X + low + 4 * d, chart.Right - hiw - 4 * d - medW);
        if (medX >= chart.X + low + 4 * d - 0.5f) p.Text("median", p.Tiny, p.P.Muted, new RectangleF(medX, axis.Y, medW, axisH), HAlign.Center, VAlign.Center, false);

        if (!showTiles) return;
        string top = mine >= 0 ? PooledText.Number("Top ", LapSpread.TopPercent(LapSpread.RankOf(_sorted.AsSpan(0, n), _sorted[mine]), n), "%") : RaceFormat.Missing;
        Tiles(p, v, "spread.histogram.tiles", HistogramTiles, tiles, top, "Your pace", mine >= 0 ? p.P.AccentText : p.P.Muted, RaceFormat.LapTime(median, 2), "Median", PooledText.Number("", n), "Cars");
    }
}
