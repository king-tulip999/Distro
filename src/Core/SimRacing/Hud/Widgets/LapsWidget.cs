using System.Drawing;
using System.Globalization;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class LapsWidget : HudWidget
{
    public const string BarsLook = "bars", TrendLook = "trend", LastLook = "last", SectorsLook = "sectors";
    public const double BarRange = 1.5, TrendRange = 1.5, AverageRange = 1, MinSector = 0.01;
    private const float NaturalW = 400, NaturalH = 440, HeaderRoom = 27;
    private const double Settled = 1000;
    private static readonly string SecondsSuffix = " " + Units.S, BarEnd = "Bar end = +1.5" + SecondsSuffix;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly List<string> _marks = new(4);
    private readonly string[] _footLabels = new string[4], _footValues = new string[4];
    private readonly float[] _px = new float[64], _py = new float[64];
    private readonly PointF[] _triangle = new PointF[3];
    private Summary _summary;
    private LapRecord? _summaryLast;
    private int _summaryCount = -1, _summaryWindow, _summarySectors;
    private double _start = double.NaN, _now, _until;
    private int _seenCount = -1;
    private string _seenLook = "";
    private string _headRight = "";

    public override bool Animating => _now < _until;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("look", "Look", BarsLook,
            new HudChoice(BarsLook, "Delta bars", "One row per lap with a bar for how far it was off your best"),
            new HudChoice(TrendLook, "Trend chart", "Your laps as a line against your best, the latest laps under it"),
            new HudChoice(LastLook, "Last lap first", "The last lap large, your best and optimal, then earlier laps as tiles"),
            new HudChoice(SectorsLook, "Sector cells", "Each lap split into its sectors, colored against your best sectors")),
        TitleRow,
        HudOption.Int("count", "Laps listed", 10, 3, 30),
        HudOption.Int("window", "Laps in the statistics", 10, 3, 50),
        HudOption.Bool("fuel", "Fuel used", true),
        HudOption.Bool("stats", "Statistics", true),
    };

    public static SizeF Natural(HudOptions o) => new(NaturalW, NaturalH - (o.Bool(TitleRow.Key) ? 0 : HeaderRoom));

    private readonly record struct Summary(double? Best, int BestLap, double? Optimal, LapStats? Stats, double?[] BestSectors);

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Laps")) return;
        var laps = v.Race.Laps.Laps;
        var area = p.HeaderBody();
        if (laps.Count == 0)
        {
            float emptyH = p.LineHeight(p.Small) * 3;
            p.NeedBelow(area, emptyH);
            area = p.Header("Laps", "none yet", bodyHeight: emptyH);
            p.Text("Laps appear here as you complete them", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        _headRight = PooledText.Number("", laps.Count, " this session");
        int sectors = v.Race.Laps.SectorCount;
        var s = SummaryFor(laps, v.Options.Int("window"), sectors);
        string look = v.Options.Choice("look");
        Clock(v.Now, laps.Count, look, EntranceLength(look, Math.Min(v.Options.Int("count"), laps.Count), sectors));
        switch (look)
        {
            case TrendLook: DrawTrend(p, v, area, s); break;
            case LastLook: DrawLast(p, v, area, s); break;
            case SectorsLook:
                if (!DrawSectors(p, v, area, s)) DrawBars(p, v, area, s);
                break;
            default: DrawBars(p, v, area, s); break;
        }
    }

    private Summary SummaryFor(IReadOnlyList<LapRecord> laps, int window, int sectors)
    {
        if (laps.Count == _summaryCount && ReferenceEquals(laps[^1], _summaryLast) && window == _summaryWindow && sectors == _summarySectors) return _summary;
        var bestSectors = LapMath.BestSectors(laps, sectors);
        _summary = new Summary(BestClean(laps, out int bestLap), bestLap, LapMath.Theoretical(bestSectors), LapMath.Stats(laps, window), bestSectors);
        _summaryCount = laps.Count;
        _summaryLast = laps[^1];
        _summaryWindow = window;
        _summarySectors = sectors;
        return _summary;
    }

    public static double EntranceLength(string look, int shown, int sectors) => look switch
    {
        TrendLook => 2.3 + 0.08 * shown,
        LastLook => 1.2 + 0.07 * shown,
        SectorsLook => 0.5 + 0.06 * shown + (sectors <= 3 ? 0.08 : 0.04) * sectors,
        _ => 0.9 + 0.07 * shown,
    };

    private void Clock(double now, int count, string look, double length)
    {
        if (double.IsNaN(_start) || now < _start) _start = now - Settled;
        else if (count != _seenCount || look != _seenLook) _start = now;
        _seenCount = count;
        _seenLook = look;
        _now = now;
        _until = _start + length;
    }

    private float In(float delay, float length) => Ease(((float)(_now - _start) - delay) / length);

    public static float Ease(float x)
    {
        x = Math.Clamp(x, 0, 1);
        float u = 1 - x;
        return 1 - u * u * u;
    }

    private float Wave(double period) => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * (_now % period) / period));

    private float Cycle(double period) => (float)(_now % period / period);

    public static Color Fade(Color c, float a) => a >= 0.999f ? c : HudPalette.Alpha(c, (int)MathF.Round(c.A * Math.Clamp(a, 0, 1) / 8) * 8);

    private static RectangleF Pop(RectangleF r, float e, float from)
    {
        float k = from + (1 - from) * e;
        return new RectangleF(r.X + r.Width * (1 - k) / 2, r.Y + r.Height * (1 - k) / 2, r.Width * k, r.Height * k);
    }

    public static double BarShare(double off, bool best) => best ? 1 : Math.Clamp(off / BarRange, 0.04, 1);

    public static double AverageShare(double last, double average) => Math.Clamp(0.5 + (last - average) / (2 * AverageRange), 0.02, 1);

    public static double NearBest(int sectors) => 0.3 / Math.Max(1, sectors);

    public static int WorstSector(IReadOnlyList<double?> times, IReadOnlyList<double?> best)
    {
        int worst = -1;
        double most = double.NegativeInfinity;
        for (int k = 0; k < Math.Min(times.Count, best.Count); k++)
        {
            if (times[k] is not { } t || t <= MinSector || best[k] is not { } b) continue;
            if (t - b > most) { most = t - b; worst = k; }
        }
        return worst;
    }

    private static bool IsBest(LapRecord lap, double? best) => lap.Clean && best is { } b && Math.Abs(lap.Time - b) < 0.0005;

    private static bool Flagged(LapRecord lap, HudView v) => !lap.Clean || lap.Caution || v.Race.TrackLimits.OnLap(lap.Lap) > 0;

    private static Color TimeColor(HudPainter p, bool best, bool flagged) => best ? p.P.Fastest : flagged ? p.P.WarnText : p.P.Heading;

    private static Color DeltaColor(HudPainter p, double off, Color middle) => off <= 0.25 ? p.P.GoodText : off <= 0.75 ? middle : p.P.WarnText;

    private string Marks(LapRecord lap, HudView v)
    {
        _marks.Clear();
        if (lap.PitLap) _marks.Add("PIT");
        if (lap.IncidentsGained > 0) _marks.Add(PooledText.Number("", lap.IncidentsGained, "x"));
        if (lap.Caution) _marks.Add("YEL");
        if (v.Race.TrackLimits.OnLap(lap.Lap) is > 0 and var offs) _marks.Add(offs > 1 ? PooledText.Number("OFF ", offs) : "OFF");
        return _marks.Count == 0 ? "" : PooledText.Join(" ", _marks);
    }

    private static string FuelNumber(double? litres, HudView v) =>
        litres is { } l && double.IsFinite(l) ? PooledText.Number("", RaceFormat.FuelValue(l, v.Units), "0.00", Inv) : RaceFormat.Missing;

    private static Color Tile(HudPainter p) => p.Solid(p.Raised(0.035));

    private static float Pair(HudPainter p, float x, RectangleF row, string label, Font labelFont, Color labelColor, string value, Font valueFont, Color valueColor, float gap)
    {
        float lw = p.Measure(label, labelFont);
        p.Text(label, labelFont, labelColor, new RectangleF(x, row.Y, lw + 2, row.Height), HAlign.Left, VAlign.Center, false);
        if (value.Length == 0) return x + lw;
        x += lw + gap;
        float vw = p.Measure(value, valueFont);
        p.Text(value, valueFont, valueColor, new RectangleF(x, row.Y, vw + 2, row.Height), HAlign.Left, VAlign.Center, false);
        return x + vw;
    }

    private static float PairWidth(HudPainter p, string label, Font labelFont, string value, Font valueFont, float gap) =>
        p.Measure(label, labelFont) + (value.Length == 0 ? 0 : gap + p.Measure(value, valueFont));

    private static void PairRight(HudPainter p, float right, RectangleF row, string label, Font labelFont, Color labelColor, string value, Font valueFont, Color valueColor, float gap) =>
        Pair(p, right - PairWidth(p, label, labelFont, value, valueFont, gap), row, label, labelFont, labelColor, value, valueFont, valueColor, gap);

    private float FootHeight(HudPainter p) => p.LineHeight(p.SmallBold) + 8 * p.Density;

    private RectangleF Head(HudPainter p, float used) => p.Header("Laps", _headRight, bodyHeight: MathF.Ceiling(used + 1));

    private static int Shown(HudView v) => Math.Min(v.Options.Int("count"), v.Race.Laps.Laps.Count);

    private void Footer(HudPainter p, RectangleF foot, int n)
    {
        float d = p.Density, gap = 5 * d;
        p.Line(foot.X, foot.Y, foot.Right, foot.Y, p.P.Line, 1);
        var row = new RectangleF(foot.X, foot.Y + 1, foot.Width, foot.Height - 1);
        float total = 0;
        for (int i = 0; i < n; i++) total += PairWidth(p, _footLabels[i], p.Tiny, _footValues[i], p.SmallBold, gap);
        float between = n > 1 ? Math.Max(8 * d, (row.Width - total) / (n - 1)) : 0;
        float x = row.X;
        for (int i = 0; i < n; i++)
        {
            float w = PairWidth(p, _footLabels[i], p.Tiny, _footValues[i], p.SmallBold, gap);
            if (x + w > row.Right + 1) break;
            Pair(p, x, row, _footLabels[i], p.Tiny, p.P.Faint, _footValues[i], p.SmallBold, p.P.Text, gap);
            x += w + between;
        }
    }

    private void DrawBars(HudPainter p, HudView v, RectangleF area, in Summary s)
    {
        var laps = v.Race.Laps.Laps;
        float d = p.Density, gap = 6 * d, rowH = 28 * d, pitch = rowH + gap;
        bool fuel = v.Options.Bool("fuel"), stats = v.Options.Bool("stats") && s.Stats is not null;
        float topH = p.LineHeight(p.Big), headH = p.LineHeight(p.Tiny) + 2 * d, footH = FootHeight(p);
        p.NeedBelow(area, topH + gap + headH + gap + pitch * Math.Min(3, laps.Count) + (stats ? footH : 0));
        area = Head(p, topH + gap + headH + gap + Shown(v) * pitch - gap + (stats ? gap + footH : 0));

        var top = area.Top(topH, out var rest);
        float x = Pair(p, top.X, top, "BEST", p.Label, p.P.Faint, RaceFormat.LapTime(s.Best), p.Big, p.P.Fastest, 8 * d);
        if (s.Best is not null)
        {
            string at = PooledText.Number("lap ", s.BestLap);
            float aw = p.Measure(at, p.Small);
            p.Text(at, p.Small, p.P.Faint, new RectangleF(x + 8 * d, top.Y, aw + 2, top.Height), HAlign.Left, VAlign.Center, false);
            x += 8 * d + aw;
        }
        if (s.Optimal is { } opt)
        {
            string ot = RaceFormat.LapTime(opt);
            if (x + 16 * d + PairWidth(p, "OPTIMAL", p.Label, ot, p.Bold, 8 * d) <= top.Right)
                PairRight(p, top.Right, top, "OPTIMAL", p.Label, p.P.Faint, ot, p.Bold, p.P.Heading, 8 * d);
        }

        rest = RectangleF.FromLTRB(rest.X, rest.Y + gap, rest.Right, rest.Bottom);
        if (stats)
        {
            var foot = new RectangleF(rest.X, rest.Bottom - footH, rest.Width, footH);
            rest = RectangleF.FromLTRB(rest.X, rest.Y, rest.Right, foot.Y - gap);
            _footLabels[0] = "AVG"; _footValues[0] = RaceFormat.LapTime(s.Stats!.Average);
            _footLabels[1] = "SPREAD"; _footValues[1] = PooledText.Number("", s.Stats.Spread, "0.00", CultureInfo.CurrentCulture, SecondsSuffix);
            _footLabels[2] = "σ"; _footValues[2] = PooledText.Number("", s.Stats.StdDev, "0.000", CultureInfo.CurrentCulture);
            _footLabels[3] = BarEnd; _footValues[3] = "";
            Footer(p, foot, 4);
        }

        float lapW = 28 * d, timeW = 70 * d, fuelW = 46 * d;
        var heads = rest.Top(headH, out rest);
        var hr = heads;
        p.Text("LAP", p.Tiny, p.P.Faint, hr.Left(lapW, out hr), HAlign.Left);
        p.Text("TIME", p.Tiny, p.P.Faint, hr.Left(timeW, out hr), HAlign.Right);
        if (fuel) p.Text("FUEL", p.Tiny, p.P.Faint, hr.Right(fuelW, out hr), HAlign.Right);
        p.Text("OFF BEST", p.Tiny, p.P.Faint, RectangleF.FromLTRB(hr.X + 12 * d, hr.Y, hr.Right, hr.Bottom), HAlign.Left);

        float y = rest.Y + gap;
        int n = Math.Min(Math.Min(v.Options.Int("count"), laps.Count), Math.Max(1, (int)((rest.Bottom - y + gap) / pitch)));
        var tinyBold = p.Font(10f, true);
        var track = p.Solid(p.Raised(0.05));
        for (int i = laps.Count - 1, j = 0; i >= laps.Count - n; i--, j++, y += pitch)
        {
            var lap = laps[i];
            bool best = IsBest(lap, s.Best), flagged = Flagged(lap, v);
            float a = In(0.07f * j, 0.4f), grow = In(0.07f * j, 0.8f);
            var row = new RectangleF(rest.X, y, rest.Width, rowH);
            p.Text(PooledText.Number("", lap.Lap), p.SmallBold, Fade(p.P.Faint, a), row.Left(lapW, out var r), HAlign.Left);
            p.Text(RaceFormat.LapTime(lap.Time), p.Bold, Fade(TimeColor(p, best, flagged), a), r.Left(timeW, out r), HAlign.Right);
            if (fuel) p.Text(FuelNumber(lap.FuelUsed, v), p.Small, Fade(p.P.Muted, a), r.Right(fuelW, out r), HAlign.Right);
            r = RectangleF.FromLTRB(r.X + 12 * d, r.Y, r.Right, r.Bottom);
            string marks = Marks(lap, v);
            if (marks.Length > 0)
            {
                float cw = p.Measure(marks, tinyBold) + 10 * d;
                if (cw < r.Width - 40 * d)
                {
                    var chip = r.Right(cw, out r);
                    r = RectangleF.FromLTRB(r.X, r.Y, r.Right - 6 * d, r.Bottom);
                    var face = new RectangleF(chip.X, chip.Y + (chip.Height - 16 * d) / 2, chip.Width, 16 * d);
                    p.Fill(face, Fade(p.P.WarnSoft, a), p.CornerRadius(3));
                    p.Text(marks, tinyBold, Fade(p.P.WarnText, a), face, HAlign.Center, VAlign.Center, false);
                }
            }
            if (r.Width < 12 * d) continue;
            var bar = new RectangleF(r.X, r.Y + (r.Height - 14 * d) / 2, r.Width, 14 * d);
            float radius = p.CornerRadius(3);
            p.Fill(bar, Fade(track, a), radius);
            double off = s.Best is { } b ? lap.Time - b : 0;
            var fill = best ? HudPalette.Alpha(p.P.Fastest, 140) : flagged ? HudPalette.Alpha(p.P.Warn, 90) : off <= 0.25 ? HudPalette.Alpha(p.P.Good, 128) : HudPalette.Alpha(p.P.Text, 56);
            var filled = new RectangleF(bar.X, bar.Y, (float)(bar.Width * BarShare(off, best)) * grow, bar.Height);
            if ((s.Best is not null || best) && filled.Width > 0.5f)
            {
                p.Fill(filled, Fade(fill, a), radius);
                if (best) Sheen(p, filled, bar.Width);
            }
            string label = best ? "BEST" : s.Best is null ? RaceFormat.Missing : RaceFormat.Gap(off, 2);
            p.Text(label, tinyBold, Fade(p.P.Heading, a), RectangleF.FromLTRB(bar.X + 6 * d, bar.Y, bar.Right - 2, bar.Bottom), HAlign.Left, VAlign.Center, false);
        }
    }

    private void Sheen(HudPainter p, RectangleF filled, float width)
    {
        float band = width * 0.3f, x = filled.X - band + Cycle(3.2) * (width + band);
        var glow = HudPalette.Alpha(p.P.Heading, 46);
        var clear = HudPalette.Alpha(p.P.Heading, 0);
        var state = p.G.Save();
        p.G.IntersectClip(filled);
        p.Gradient(new RectangleF(x, filled.Y, band / 2, filled.Height), clear, glow);
        p.Gradient(new RectangleF(x + band / 2, filled.Y, band / 2, filled.Height), glow, clear);
        p.G.Restore(state);
    }

    private void DrawTrend(HudPainter p, HudView v, RectangleF area, in Summary s)
    {
        var laps = v.Race.Laps.Laps;
        float d = p.Density, gap = 6 * d;
        bool fuel = v.Options.Bool("fuel"), stats = v.Options.Bool("stats") && s.Stats is not null;
        float topH = p.LineHeight(p.Bold), headH = p.LineHeight(p.Tiny) + 2 * d, rowH = 22 * d;
        p.NeedBelow(area, topH + gap + 120 * d + gap + headH + rowH * 2);
        float chartH = Math.Min(168 * d, Math.Max(100 * d, (area.Height - topH - gap) * 0.45f));
        area = Head(p, topH + gap + chartH + gap + headH + Shown(v) * rowH);

        var top = area.Top(topH, out var rest);
        float x = Pair(p, top.X, top, "BEST", p.Tiny, p.P.Faint, RaceFormat.LapTime(s.Best), p.Bold, p.P.Fastest, 5 * d);
        float avgW = 0;
        string avg = stats ? RaceFormat.LapTime(s.Stats!.Average) : "";
        if (stats)
        {
            avgW = PairWidth(p, "AVG", p.Tiny, avg, p.SmallBold, 5 * d);
            PairRight(p, top.Right, top, "AVG", p.Tiny, p.P.Faint, avg, p.SmallBold, p.P.Text, 5 * d);
        }
        if (s.Optimal is { } opt)
        {
            string ot = RaceFormat.LapTime(opt);
            if (x + 14 * d + PairWidth(p, "OPTIMAL", p.Tiny, ot, p.Bold, 5 * d) + avgW + 12 * d <= top.Right)
                Pair(p, x + 14 * d, top, "OPTIMAL", p.Tiny, p.P.Faint, ot, p.Bold, p.P.Heading, 5 * d);
        }

        rest = RectangleF.FromLTRB(rest.X, rest.Y + gap, rest.Right, rest.Bottom);
        var chart = rest.Top(chartH, out rest);
        DrawChart(p, v, chart, s, stats);

        rest = RectangleF.FromLTRB(rest.X, rest.Y + gap, rest.Right, rest.Bottom);
        if (rest.Height < headH + rowH) return;
        float lapW = 30 * d, timeW = 74 * d, deltaW = 60 * d, fuelW = 52 * d;
        var hr = rest.Top(headH, out rest);
        p.Text("LAP", p.Tiny, p.P.Faint, hr.Left(lapW, out hr), HAlign.Left);
        p.Text("TIME", p.Tiny, p.P.Faint, hr.Left(timeW, out hr), HAlign.Right);
        p.Text("Δ BEST", p.Tiny, p.P.Faint, hr.Left(deltaW, out hr), HAlign.Right);
        if (fuel) p.Text("FUEL", p.Tiny, p.P.Faint, hr.Right(fuelW, out _), HAlign.Right);

        int n = Math.Min(Math.Min(v.Options.Int("count"), laps.Count), (int)(rest.Height / rowH));
        float y = rest.Y;
        var tinyBold = p.Font(10f, true);
        var rule = HudPalette.Alpha(p.P.Line, 150);
        for (int i = laps.Count - 1, j = 0; i >= laps.Count - n; i--, j++, y += rowH)
        {
            var lap = laps[i];
            bool best = IsBest(lap, s.Best), flagged = Flagged(lap, v);
            float a = In(1.6f + 0.08f * j, 0.45f);
            if (a <= 0) continue;
            var row = new RectangleF(rest.X, y - 6 * d * (1 - a), rest.Width, rowH - 1);
            p.Line(row.X, y + rowH - 1, row.Right, y + rowH - 1, Fade(rule, a), 1);
            p.Text(PooledText.Number("", lap.Lap), p.SmallBold, Fade(p.P.Faint, a), row.Left(lapW, out var r), HAlign.Left);
            p.Text(RaceFormat.LapTime(lap.Time), p.Bold, Fade(TimeColor(p, best, flagged), a), r.Left(timeW, out r), HAlign.Right);
            var delta = r.Left(deltaW, out r);
            if (s.Best is { } b && !best) p.Text(RaceFormat.Gap(lap.Time - b, 2), p.Small, Fade(DeltaColor(p, lap.Time - b, p.P.Text), a), delta, HAlign.Right);
            if (fuel) p.Text(RaceFormat.Fuel(lap.FuelUsed, v.Units), p.Small, Fade(p.P.Muted, a), r.Right(fuelW, out r), HAlign.Right);
            string marks = Marks(lap, v);
            if (marks.Length > 0) p.Text(marks, tinyBold, Fade(p.P.WarnText, a), RectangleF.FromLTRB(r.X + 12 * d, r.Y, r.Right - 4, r.Bottom), HAlign.Left);
        }
    }

    private void DrawChart(HudPainter p, HudView v, RectangleF chart, in Summary s, bool stats)
    {
        var laps = v.Race.Laps.Laps;
        float d = p.Density, sx = chart.Width / 380f, sy = chart.Height / 168f;
        p.Fill(chart, Tile(p), p.CornerRadius(4));
        float top = chart.Y + 20 * sy, bottom = chart.Y + 130 * sy, left = chart.X + 20 * sx, right = chart.Right - 24 * sx;
        float gl = chart.X + 8 * sx, gr = chart.Right - 8 * sx;
        var small = p.Font(9f, false);
        var markFont = p.Font(8.5f, true);
        float sh = p.LineHeight(small);
        for (int k = 0; k <= 3; k++)
        {
            float gy = top + (bottom - top) * k / 3f;
            p.Line(gl, gy, gr, gy, k == 0 || k == 3 ? p.P.Line : HudPalette.Alpha(p.P.Line, 150), 1);
        }
        var labelBox = new RectangleF(chart.X, 0, chart.Right - 10 * sx - chart.X, sh);
        p.Text("BEST", small, p.P.Faint, labelBox with { Y = top - sh - 1 }, HAlign.Right, VAlign.Center, false);
        p.Text("+1.5", small, p.P.Faint, labelBox with { Y = bottom - sh - 1 }, HAlign.Right, VAlign.Center, false);
        if (stats && s.Best is { } bb)
        {
            float ay = top + (float)(Math.Clamp((s.Stats!.Average - bb) / TrendRange, 0, 1)) * (bottom - top);
            var dash = HudPalette.Alpha(p.P.Text, 90);
            for (float dx = gl; dx < gr; dx += 7 * sx) p.Line(dx, ay, Math.Min(gr, dx + 3 * sx), ay, dash, 1);
            p.Text("AVG", small, p.P.Faint, labelBox with { Y = ay - sh - 1 }, HAlign.Right, VAlign.Center, false);
        }

        int m = Math.Min(Math.Min(v.Options.Int("count"), laps.Count), _px.Length);
        int first = laps.Count - m;
        float step = m > 1 ? (right - left) / (m - 1) : 0;
        for (int i = 0; i < m; i++)
        {
            var lap = laps[first + i];
            double off = s.Best is { } b ? lap.Time - b : 0;
            _px[i] = m > 1 ? left + i * step : (left + right) / 2;
            _py[i] = top + (float)(Math.Clamp(off, 0, TrendRange) / TrendRange) * (bottom - top);
        }
        float drawn = In(0, 1.6f) * Math.Max(1, m - 1);
        for (int i = 1; i < m && i - 1 < drawn; i++)
        {
            float f = Math.Min(1, drawn - (i - 1));
            p.Line(_px[i - 1], _py[i - 1], _px[i - 1] + (_px[i] - _px[i - 1]) * f, _py[i - 1] + (_py[i] - _py[i - 1]) * f, HudPalette.Alpha(p.P.Text, 140), 1.6f);
        }
        int every = step > 0 ? Math.Max(1, (int)Math.Ceiling(18 * d / step)) : 1;
        for (int i = 0; i < m; i++)
        {
            var lap = laps[first + i];
            bool best = IsBest(lap, s.Best), flagged = Flagged(lap, v);
            double off = s.Best is { } b ? lap.Time - b : 0;
            float px = _px[i], py = _py[i];
            bool clipped = off > TrendRange;
            float e = In(1.6f * i / Math.Max(1, m - 1), 0.35f);
            if (e <= 0) continue;
            if (best)
            {
                if (e >= 1)
                {
                    float c = Cycle(1.8), ring = (4 + 9 * c) * d;
                    p.G.DrawEllipse(p.Pen(Fade(p.P.Fastest, 0.9f * (1 - c)), 1.5f), px - ring, py - ring, ring * 2, ring * 2);
                }
                p.Dot(px, py, 4.5f * d * e, p.P.Fastest);
            }
            else if (clipped)
            {
                _triangle[0] = new PointF(px - 4 * d * e, bottom - 2 * d);
                _triangle[1] = new PointF(px + 4 * d * e, bottom - 2 * d);
                _triangle[2] = new PointF(px, bottom - 2 * d + 7 * d * e);
                p.G.FillPolygon(p.Brush(p.P.Warn), _triangle);
            }
            else if (flagged)
            {
                float rr = 4 * d * e;
                p.Dot(px, py, rr, p.Solid(p.PanelColor, 255));
                p.G.DrawEllipse(p.Pen(p.P.Warn, 1.6f), px - rr, py - rr, rr * 2, rr * 2);
            }
            else p.Dot(px, py, 3.6f * d * e, p.P.Heading);
            if (flagged)
            {
                string marks = Marks(lap, v);
                float my = clipped ? bottom + 8 * d : py + 6 * d;
                if (marks.Length > 0) p.Text(marks, markFont, Fade(p.P.WarnText, e), new RectangleF(px - 30 * d, my, 60 * d, p.LineHeight(markFont)), HAlign.Center, VAlign.Center, false);
            }
            bool last = i == m - 1;
            if (!last && !best && (m - 1 - i) % every != 0) continue;
            var numColor = Fade(best ? p.P.Fastest : last ? p.P.Heading : p.P.Faint, e);
            p.Text(PooledText.Number("", lap.Lap), small, numColor, new RectangleF(px - 15 * d, chart.Bottom - sh - 3 * sy, 30 * d, sh), HAlign.Center, VAlign.Center, false);
        }
    }

    private void DrawLast(HudPainter p, HudView v, RectangleF area, in Summary s)
    {
        var laps = v.Race.Laps.Laps;
        float d = p.Density, gap = 10 * d;
        bool fuel = v.Options.Bool("fuel"), stats = v.Options.Bool("stats") && s.Stats is not null;
        var last = laps[^1];
        bool lastBest = IsBest(last, s.Best), lastFlagged = Flagged(last, v);
        float labelH = p.LineHeight(p.Label), heroH = labelH + p.LineHeight(p.Huge) * 0.85f;
        float tileH = p.LineHeight(p.Bold) + 12 * d, smallTileH = p.LineHeight(p.Tiny) * 2 + p.LineHeight(p.Bold) + 10 * d, footH = FootHeight(p);
        p.NeedBelow(area, heroH + gap + tileH + gap + labelH + smallTileH);
        const int Columns = 3;
        float cg = 6 * d;
        int earlier = Math.Max(0, Shown(v) - 1), earlierRows = (earlier + Columns - 1) / Columns;
        float used = heroH + gap + (stats ? p.LineHeight(p.Tiny) + 9 * d + gap : 0) + tileH;
        if (earlier > 0) used += gap + labelH + 6 * d + earlierRows * smallTileH + (earlierRows - 1) * cg;
        if (stats) used += gap + footH;
        area = Head(p, used);

        var hero = area.Top(heroH, out var rest);
        float ha = In(0, 0.6f);
        hero.Y += 8 * d * (1 - ha);
        string marks = Marks(last, v);
        string head = marks.Length > 0 ? PooledText.Concat(PooledText.Number("LAST · LAP ", last.Lap), " · ", marks) : PooledText.Number("LAST · LAP ", last.Lap);
        p.Text(head, p.Label, Fade(marks.Length > 0 ? p.P.WarnText : p.P.Faint, ha), new RectangleF(hero.X, hero.Y, hero.Width, labelH), HAlign.Left, VAlign.Center, false);
        var big = new RectangleF(hero.X, hero.Y + labelH, hero.Width, hero.Height - labelH);
        string time = RaceFormat.LapTime(last.Time);
        float tw = p.Measure(time, p.Huge);
        p.Text(time, p.Huge, Fade(TimeColor(p, lastBest, lastFlagged), ha), new RectangleF(big.X, big.Y, tw + 2, big.Height), HAlign.Left, VAlign.Center, false);
        float side = big.X + tw + 12 * d, lineB = p.LineHeight(p.Bold), lineT = p.LineHeight(p.Tiny);
        float sideY = big.Bottom - lineB - lineT - 2 * d;
        if (s.Best is { } b)
        {
            string dt = lastBest ? "BEST" : RaceFormat.Gap(last.Time - b, 2);
            var dc = Fade(lastBest ? p.P.Fastest : DeltaColor(p, last.Time - b, p.P.Text), ha);
            p.Text(dt, p.Bold, dc, new RectangleF(side, sideY, 80 * d, lineB), HAlign.Left, VAlign.Center, false);
            if (!lastBest) p.Text("to best", p.Tiny, Fade(p.P.Faint, ha), new RectangleF(side, sideY + lineB + 2 * d, 80 * d, lineT), HAlign.Left, VAlign.Center, false);
        }
        if (fuel && last.FuelUsed is not null && big.Right - side > 150 * d)
        {
            var fr = new RectangleF(big.Right - 80 * d, sideY, 80 * d, lineB);
            p.Text(RaceFormat.Fuel(last.FuelUsed, v.Units), p.Bold, Fade(p.P.Muted, ha), fr, HAlign.Right, VAlign.Center, false);
            p.Text("fuel", p.Tiny, Fade(p.P.Faint, ha), fr with { Y = sideY + lineB + 2 * d, Height = lineT }, HAlign.Right, VAlign.Center, false);
        }

        rest = RectangleF.FromLTRB(rest.X, rest.Y + gap, rest.Right, rest.Bottom);
        if (stats)
        {
            var avgRow = rest.Top(lineT, out rest);
            p.Text(PooledText.Concat("vs average ", RaceFormat.LapTime(s.Stats!.Average)), p.Tiny, p.P.Faint, avgRow, HAlign.Left, VAlign.Center, false);
            p.Text(RaceFormat.Gap(last.Time - s.Stats.Average, 2), p.Tiny, p.P.Text, avgRow, HAlign.Right, VAlign.Center, false);
            var bar = new RectangleF(rest.X, rest.Y + 4 * d, rest.Width, 5 * d);
            p.Fill(bar, p.Solid(p.Raised(0.07)), p.CornerRadius(3));
            p.Fill(new RectangleF(bar.X, bar.Y, (float)(bar.Width * AverageShare(last.Time, s.Stats.Average)) * In(0, 1.1f), bar.Height), HudPalette.Alpha(p.P.Text, 140), p.CornerRadius(3));
            rest = RectangleF.FromLTRB(rest.X, bar.Bottom + gap, rest.Right, rest.Bottom);
        }

        var tiles = rest.Top(tileH, out rest);
        float half = (tiles.Width - 6 * d) / 2, radius = p.CornerRadius(4);
        float ea = In(0.15f, 0.4f), eb = In(0.2f, 0.4f);
        var bestTile = Pop(new RectangleF(tiles.X, tiles.Y, half, tileH), ea, 0.92f);
        var optTile = Pop(new RectangleF(tiles.X + half + 6 * d, tiles.Y, half, tileH), eb, 0.92f);
        p.Fill(bestTile, Fade(HudPalette.Alpha(p.P.Fastest, 41), ea), radius);
        p.Fill(optTile, Fade(Tile(p), eb), radius);
        var bi = bestTile.Inflated(-9 * d, 0);
        p.Text("BEST", p.Label, Fade(p.P.Heading, ea), bi, HAlign.Left, VAlign.Center, false);
        p.Text(RaceFormat.LapTime(s.Best), p.Bold, Fade(p.P.Fastest, ea), bi, HAlign.Right, VAlign.Center, false);
        var oi = optTile.Inflated(-9 * d, 0);
        p.Text("OPTIMAL", p.Label, Fade(p.P.Faint, eb), oi, HAlign.Left, VAlign.Center, false);
        p.Text(RaceFormat.LapTime(s.Optimal), p.Bold, Fade(p.P.Heading, eb), oi, HAlign.Right, VAlign.Center, false);

        rest = RectangleF.FromLTRB(rest.X, rest.Y + gap, rest.Right, rest.Bottom);
        if (stats)
        {
            var foot = new RectangleF(rest.X, rest.Bottom - footH, rest.Width, footH);
            rest = RectangleF.FromLTRB(rest.X, rest.Y, rest.Right, foot.Y - gap);
            int clean = 0;
            for (int i = 0; i < laps.Count; i++) if (laps[i].Clean && !laps[i].Caution) clean++;
            _footLabels[0] = "SPREAD"; _footValues[0] = PooledText.Number("", s.Stats!.Spread, "0.00", CultureInfo.CurrentCulture, SecondsSuffix);
            _footLabels[1] = "σ"; _footValues[1] = PooledText.Number("", s.Stats.StdDev, "0.000", CultureInfo.CurrentCulture);
            _footLabels[2] = "CLEAN"; _footValues[2] = PooledText.Number("", clean, " / ", laps.Count);
            Footer(p, foot, 3);
        }
        if (laps.Count < 2 || rest.Height < labelH + smallTileH) return;
        p.Text("EARLIER", p.Label, p.P.Faint, rest.Top(labelH, out rest), HAlign.Left, VAlign.Center, false);
        rest = RectangleF.FromLTRB(rest.X, rest.Y + 6 * d, rest.Right, rest.Bottom);
        float cw = (rest.Width - cg * (Columns - 1)) / Columns;
        int rows = Math.Max(1, (int)((rest.Height + cg) / (smallTileH + cg)));
        int n = Math.Min(Math.Min(v.Options.Int("count") - 1, laps.Count - 1), rows * Columns);
        var tinyBold = p.Font(10f, true);
        for (int k = 0; k < n; k++)
        {
            var lap = laps[laps.Count - 2 - k];
            bool best = IsBest(lap, s.Best), flagged = Flagged(lap, v);
            float e = In(0.3f + 0.07f * k, 0.4f);
            if (e <= 0) continue;
            var slot = new RectangleF(rest.X + k % Columns * (cw + cg), rest.Y + k / Columns * (smallTileH + cg), cw, smallTileH);
            var tile = Pop(slot, e, 0.92f);
            p.Fill(tile, Fade(best ? HudPalette.Alpha(p.P.Fastest, 31) : Tile(p), e), radius);
            if (best) p.Outline(tile, Fade(p.P.Fastest, e * (0.35f + 0.55f * Wave(2.6))), 1, radius);
            var inner = slot.Inflated(-8 * d, -5 * d);
            var line1 = inner.Top(lineT, out var r);
            p.Text(PooledText.Number("LAP ", lap.Lap), tinyBold, Fade(p.P.Faint, e), line1, HAlign.Left, VAlign.Center, false);
            string lm = Marks(lap, v);
            if (lm.Length > 0) p.Text(lm, tinyBold, Fade(p.P.WarnText, e), line1, HAlign.Right, VAlign.Center, false);
            var line2 = r.Top(lineB, out r);
            p.Text(RaceFormat.LapTime(lap.Time), p.Bold, Fade(TimeColor(p, best, flagged), e), line2, HAlign.Left, VAlign.Center, false);
            var line3 = r.Top(lineT, out _);
            if (best) p.Text("best", p.Tiny, Fade(p.P.Fastest, e), line3, HAlign.Left, VAlign.Center, false);
            else if (s.Best is { } sb) p.Text(RaceFormat.Gap(lap.Time - sb, 2), p.Tiny, Fade(DeltaColor(p, lap.Time - sb, p.P.Muted), e), line3, HAlign.Left, VAlign.Center, false);
        }
    }

    private bool DrawSectors(HudPainter p, HudView v, RectangleF area, in Summary s)
    {
        var laps = v.Race.Laps.Laps;
        var best = s.BestSectors;
        int count = best.Length;
        if (count < 2) return false;
        float d = p.Density;
        bool fuel = v.Options.Bool("fuel"), stats = v.Options.Bool("stats") && s.Stats is not null;
        bool times = count <= 3, deltas = count is > 3 and <= 8, colorOnly = count > 8;
        float lapW = 26 * d, timeW = 76 * d, gap = (times ? 4 : deltas ? 3 : 2) * d;
        float tailW = colorOnly ? 34 * d : fuel ? (times ? 40 : 36) * d : 0;
        float cellW = (area.Width - lapW - timeW - tailW - gap * (count + (tailW > 0 ? 2 : 1))) / count;
        if (cellW < 6 * d) return false;
        float headH = p.LineHeight(p.Tiny) + 6 * d, rowH = 28 * d, pitch = rowH + 4 * d, optH = 28 * d, legendH = p.LineHeight(p.SmallBold) + 2 * d;
        p.NeedBelow(area, headH + pitch * Math.Min(3, laps.Count) + optH + legendH);
        area = Head(p, headH + Shown(v) * pitch + 4 * d + optH + legendH);
        double near = NearBest(count);
        var cellFont = times ? p.SmallBold : p.Font(10.5f, true);
        var tinyBold = p.Font(9f, true);
        float radius = p.CornerRadius(3);

        var hr = area.Top(headH, out var rest);
        p.Text("LAP", p.Tiny, p.P.Faint, hr.Left(lapW, out hr), HAlign.Left);
        p.Text("TIME", p.Tiny, p.P.Faint, RectangleF.FromLTRB(hr.X, hr.Y, hr.X + timeW - 6 * d, hr.Bottom), HAlign.Right);
        float cx0 = hr.X + timeW + gap;
        for (int k = 0; k < count; k++)
            p.Text(colorOnly ? PooledText.Number("", k + 1) : PooledText.Number("S", k + 1), p.Tiny, p.P.Faint, new RectangleF(cx0 + k * (cellW + gap), hr.Y, cellW, hr.Height), HAlign.Center);
        if (tailW > 0 && !colorOnly) p.Text("FUEL", p.Tiny, p.P.Faint, new RectangleF(hr.Right - tailW, hr.Y, tailW, hr.Height), HAlign.Right);

        var legend = new RectangleF(rest.X, rest.Bottom - legendH, rest.Width, legendH);
        var opt = new RectangleF(rest.X, legend.Y - 4 * d - optH, rest.Width, optH);
        rest = RectangleF.FromLTRB(rest.X, rest.Y, rest.Right, opt.Y - 4 * d);

        int n = Math.Min(Math.Min(v.Options.Int("count"), laps.Count), Math.Max(1, (int)((rest.Height + 4 * d) / pitch)));
        float y = rest.Y;
        float cellStep = times ? 0.08f : 0.04f;
        for (int i = laps.Count - 1, j = 0; i >= laps.Count - n; i--, j++, y += pitch)
        {
            var lap = laps[i];
            bool isBest = IsBest(lap, s.Best), flagged = Flagged(lap, v);
            float ra = In(0.06f * j, 0.4f);
            var row = new RectangleF(rest.X, y, rest.Width, rowH);
            p.Text(PooledText.Number("", lap.Lap), p.SmallBold, Fade(p.P.Faint, ra), row.Left(lapW, out var r), HAlign.Left);
            p.Text(RaceFormat.LapTime(lap.Time), p.Bold, Fade(TimeColor(p, isBest, flagged), ra), RectangleF.FromLTRB(r.X, r.Y, r.X + timeW - 6 * d, r.Bottom), HAlign.Right);
            string marks = flagged ? Marks(lap, v) : "";
            int worst = flagged ? WorstSector(lap.Sectors, best) : -1;
            for (int k = 0; k < count; k++)
            {
                float e = In(0.06f * j + cellStep * k, 0.4f);
                if (e <= 0) continue;
                float ch = 24 * d * (0.4f + 0.6f * e);
                var cell = new RectangleF(cx0 + k * (cellW + gap), row.Y + (rowH - ch) / 2, cellW, ch);
                double? t = k < lap.Sectors.Count && lap.Sectors[k] is > MinSector ? lap.Sectors[k] : null;
                bool mark = k == worst;
                bool top = !mark && t is { } tt && best[k] is { } bk && Math.Abs(tt - bk) < 0.0005;
                bool close = !mark && !top && t is { } tc && best[k] is { } bc && tc - bc <= near;
                var bg = mark ? p.P.WarnSoft : top ? HudPalette.Mix(HudPalette.Alpha(p.P.Fastest, 115), HudPalette.Alpha(p.P.Heading, 115), 0.25 * Wave(2.4)) : close ? HudPalette.Alpha(p.P.Good, 71) : Tile(p);
                var fg = mark ? p.P.WarnText : top ? p.P.Heading : close ? p.P.GoodText : p.P.Muted;
                bg = Fade(bg, e);
                fg = Fade(fg, e);
                p.Fill(cell, bg, radius);
                if (colorOnly) continue;
                if (t is not { } st) { p.Text(RaceFormat.Missing, cellFont, Fade(p.P.Faint, e), cell, HAlign.Center, VAlign.Center, false); continue; }
                if (times)
                {
                    string text = SectorTime(st, 3);
                    if (mark && marks.Length > 0)
                    {
                        float a = p.Measure(text, cellFont), mw = p.Measure(marks, tinyBold), x0 = cell.X + Math.Max(0, (cell.Width - a - mw - 4 * d) / 2);
                        p.Text(text, cellFont, fg, new RectangleF(x0, cell.Y, a + 2, cell.Height), HAlign.Left, VAlign.Center, false);
                        p.Text(marks, tinyBold, fg, new RectangleF(x0 + a + 4 * d, cell.Y, mw + 2, cell.Height), HAlign.Left, VAlign.Center, false);
                    }
                    else p.Text(text, cellFont, fg, cell, HAlign.Center, VAlign.Center, false);
                }
                else
                {
                    string text = mark && marks.Length > 0 ? marks : top ? "BEST" : best[k] is { } bd ? SectorGap(st - bd) : RaceFormat.Missing;
                    p.Text(text, cellFont, fg, cell, HAlign.Center, VAlign.Center, false);
                }
            }
            if (tailW <= 0) continue;
            var tail = new RectangleF(row.Right - tailW, row.Y, tailW, row.Height);
            if (colorOnly) { if (marks.Length > 0) p.Text(marks, tinyBold, Fade(p.P.WarnText, ra), tail, HAlign.Right, VAlign.Center); }
            else p.Text(FuelNumber(lap.FuelUsed, v), p.Small, Fade(p.P.Muted, ra), tail, HAlign.Right);
        }

        p.Line(opt.X, opt.Y, opt.Right, opt.Y, p.P.Line, 1);
        var orow = new RectangleF(opt.X, opt.Y + 4 * d, opt.Width, opt.Height - 4 * d);
        p.Text("OPT", p.Tiny, p.P.Faint, orow.Left(lapW, out var or), HAlign.Left);
        p.Text(RaceFormat.LapTime(s.Optimal), p.Bold, p.P.Heading, RectangleF.FromLTRB(or.X, or.Y, or.X + timeW - 6 * d, or.Bottom), HAlign.Right);
        if (!colorOnly)
            for (int k = 0; k < count; k++)
                p.Text(best[k] is { } bv ? SectorTime(bv, times ? 3 : 2) : RaceFormat.Missing, cellFont, p.P.Fastest, new RectangleF(cx0 + k * (cellW + gap), orow.Y, cellW, orow.Height), HAlign.Center, VAlign.Center, false);

        float lx = legend.X, sw = 8 * d;
        lx = Swatch(p, lx, legend, HudPalette.Alpha(p.P.Fastest, 140), "Your best", sw);
        lx = Swatch(p, lx, legend, HudPalette.Alpha(p.P.Good, 102), PooledText.Number("Within ", near, near < 0.05 ? "0.000" : "0.00", CultureInfo.CurrentCulture), sw);
        lx = Swatch(p, lx, legend, p.P.WarnSoft, "Flagged", sw);
        if (stats)
        {
            string avg = RaceFormat.LapTime(s.Stats!.Average);
            if (lx + 12 * d + PairWidth(p, "AVG", p.Tiny, avg, p.SmallBold, 5 * d) <= legend.Right)
                PairRight(p, legend.Right, legend, "AVG", p.Tiny, p.P.Faint, avg, p.SmallBold, p.P.Text, 5 * d);
        }
        return true;
    }

    private static float Swatch(HudPainter p, float x, RectangleF row, Color c, string label, float size)
    {
        float lw = p.Measure(label, p.Tiny), d = p.Density;
        if (x + size + 4 * d + lw > row.Right) return x;
        p.Fill(new RectangleF(x, row.Y + (row.Height - size) / 2, size, size), c, p.CornerRadius(3));
        p.Text(label, p.Tiny, p.P.Faint, new RectangleF(x + size + 4 * d, row.Y, lw + 2, row.Height), HAlign.Left, VAlign.Center, false);
        return x + size + 4 * d + lw + 12 * d;
    }

    private static string SectorTime(double seconds, int decimals) =>
        seconds >= 60 ? RaceFormat.LapTime(seconds, decimals) : PooledText.Number("", seconds, decimals == 3 ? "0.000" : "0.00", Inv);

    public static string SectorGap(double off) =>
        off >= 1 ? PooledText.Number("+", off, "0.0", Inv) : PooledText.Number("+", Math.Max(0, off), "#.00", Inv);

    private static double? BestClean(IReadOnlyList<LapRecord> laps, out int bestLap)
    {
        double? best = null;
        bestLap = 0;
        foreach (var lap in laps)
        {
            if (!lap.Clean) continue;
            double t = lap.Time;
            if (double.IsNaN(t)) { bestLap = lap.Lap; return t; }
            if (best is not { } b || t < b) { best = t; bestLap = lap.Lap; }
        }
        return best;
    }
}
