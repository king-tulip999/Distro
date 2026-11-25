using System.Drawing;
using System.Globalization;
using Core.SimRacing.Race;
using Units = Core.Services.CapsUnits;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class GForceWidget : HudWidget
{
    public const string Circle = "circle", Bars = "bars", Compare = "compare";
    public const double W = 340, H = 168;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] CircleParts = { "circle", "tiles" };
    private static readonly string[] CircleTiles = { "lateral", "long", "combined" };
    private static readonly string[] BarsParts = { "lateral", "long", "trace", "footer" };
    private static readonly string[] CompareParts = { "peaks", "footer" };
    private static readonly string[] ComparePeaks = { "circle", "table" };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Compare,
            new HudChoice(Circle, "Friction circle", "A dot with a fading trail, rings and this lap's peak"),
            new HudChoice(Bars, "Bars and trace", "Bars from a center line and a 10 s trace"),
            new HudChoice(Compare, "Compare", "A small circle with this lap's peaks against your best lap")),
        TitleRow,
    };

    private readonly double[] _lat = new double[GForceLog.TraceCapacity], _lon = new double[GForceLog.TraceCapacity], _age = new double[GForceLog.TraceCapacity];
    private readonly PointF[] _points = new PointF[GForceLog.TraceCapacity];
    private object? _whereTurns;
    private int _whereKey = -1;
    private string _where = "";

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "G-force")) return;
        switch (v.Options.Choice("style"))
        {
            case Circle: DrawCircle(p, v); break;
            case Bars: DrawBars(p, v); break;
            default: DrawCompare(p, v); break;
        }
    }

    private static bool Wide(HudPainter p) => p.Bounds.Width >= 220 * p.Density;

    private static double? LatG(HudView v) => v.Frame.LatAccel is { } a && double.IsFinite(a) ? a / GForceLog.Gravity : null;

    private static double? LongG(HudView v) => v.Frame.LongAccel is { } a && double.IsFinite(a) ? a / GForceLog.Gravity : null;

    private static double Scale(HudView v)
    {
        double most = Math.Max(v.Race.GForce.SessionPeak, Math.Max(Math.Abs(LatG(v) ?? 0), Math.Abs(LongG(v) ?? 0)));
        return Math.Max(2, Math.Ceiling(Math.Min(most, GForceLog.GlitchG)));
    }

    private static string G(double? g) => g is { } x && double.IsFinite(x) ? PooledText.Number("", Math.Abs(x), "0.00", Inv) : RaceFormat.Missing;

    private static string GUnit(double? g, string suffix = "") => g is { } x && double.IsFinite(x) ? PooledText.Number("", Math.Abs(x), "0.00", Inv, suffix) : RaceFormat.Missing;

    private string Where(HudView v, double pct)
    {
        var turns = v.Race.Turns;
        int key = (int)Math.Round((pct - Math.Floor(pct)) * 400);
        if (!ReferenceEquals(turns, _whereTurns) || key != _whereKey)
        {
            _whereTurns = turns;
            _whereKey = key;
            _where = turns.Where(pct);
        }
        return _where;
    }

    private void DrawCircle(HudPainter p, HudView v)
    {
        float d = p.Density;
        var log = v.Race.GForce;
        string right = log.Lap.Any ? GUnit(log.Lap.Combined, " g") : RaceFormat.Missing;
        var area = p.Header("G-force", Wide(p) ? PooledText.Concat("Peak ", right) : null, p.P.Heading);
        p.NeedBelow(area, 176 * d);
        float tileW = 96 * d, gap = 5 * d;
        float s = Math.Min(area.Height, area.Width);
        bool tiles = area.Width - s - gap >= tileW * 0.8f;
        if (tiles) s = Math.Min(area.Height, area.Width - gap - tileW);
        if (tiles) p.NeedWidth(area.X + 150 * d + gap + tileW + 8 * d);
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "gforce.circle", area, true, gap, CircleParts, stackalloc float[] { tiles ? s : 0, tiles ? 0 : -1 }, parts);
        var box = tiles ? new RectangleF(parts[0].X, parts[0].Y, s, s) : new RectangleF(area.X + (area.Width - s) / 2, area.Y, s, s);
        p.Tile(box);
        Friction(p, v, box.Inflated(-6 * d, -6 * d), true, true);
        if (!tiles) return;
        var col = new RectangleF(parts[1].X, parts[1].Y, parts[1].Width, s);
        float th = (col.Height - gap * 2) / 3;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "gforce.circle.tiles", col, false, gap, CircleTiles, stackalloc float[] { th, th, th }, cells);
        double? lat = LatG(v), lon = LongG(v);
        string latLabel = lat is > 0.02 ? "Lat · left" : lat is < -0.02 ? "Lat · right" : "Lateral";
        StatTile(p, new RectangleF(col.X, cells[0].Y, col.Width, th), latLabel, G(lat), p.P.Heading, null);
        bool braking = lon is < 0;
        StatTile(p, new RectangleF(col.X, cells[1].Y, col.Width, th), braking ? "Braking" : "Accel", G(lon),
            HudPalette.Mix(braking ? p.P.BrakeInput : p.P.ThrottleInput, p.P.Heading, 0.35), null);
        double? combined = lat is { } a && lon is { } b ? Math.Sqrt(a * a + b * b) : null;
        StatTile(p, new RectangleF(col.X, cells[2].Y, col.Width, th), "Combined", G(combined), p.P.Heading, combined / Scale(v));
    }

    private static void StatTile(HudPainter p, RectangleF r, string label, string value, Color color, double? bar)
    {
        float d = p.Density;
        p.Tile(r);
        var inner = r.Inflated(-6 * d, -2 * d);
        p.ValueStat(inner, label, value, color, p.Font(18f, true), HAlign.Center);
        if (bar is { } b) p.Fill(new RectangleF(r.X + 4 * d, r.Bottom - 3 * d, (float)Math.Clamp(b, 0, 1) * (r.Width - 8 * d), 2 * d), p.P.Accent);
    }

    private void Friction(HudPainter p, HudView v, RectangleF box, bool labels, bool trail)
    {
        float d = p.Density;
        double scale = Scale(v);
        float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2, r = Math.Min(box.Width, box.Height) / 2;
        if (r < 8 * d) return;
        var g = p.G;
        p.Dot(cx, cy, r, p.Solid(p.Sunken(0.3), 230));
        g.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.LineStrong, 200), 1.5f * d), cx - r, cy - r, r * 2, r * 2);
        for (int ring = 1; ring < scale; ring++)
        {
            float rr = (float)(r * ring / scale);
            g.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.LineStrong, 120), d), cx - rr, cy - rr, rr * 2, rr * 2);
        }
        p.Line(cx - r + 2 * d, cy, cx + r - 2 * d, cy, HudPalette.Alpha(p.P.LineStrong, 110));
        p.Line(cx, cy - r + 2 * d, cx, cy + r - 2 * d, HudPalette.Alpha(p.P.LineStrong, 110));
        var log = v.Race.GForce;
        if (log.Lap.Any && log.Lap.Combined > 0.05)
        {
            float pr = (float)(r * Math.Min(1, log.Lap.Combined / scale));
            g.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.Accent, 110), 1.5f * d), cx - pr, cy - pr, pr * 2, pr * 2);
        }
        if (labels && r >= 50 * d)
        {
            var tiny = p.Tiny;
            float lh = p.LineHeight(tiny);
            p.Text(p.LabelText("Brake"), tiny, HudPalette.Mix(p.P.BrakeInput, p.P.Muted, 0.3), new RectangleF(cx - 30 * d, cy - r + 4 * d, 60 * d, lh), HAlign.Center, VAlign.Top, false);
            p.Text(p.LabelText("Accel"), tiny, HudPalette.Mix(p.P.ThrottleInput, p.P.Muted, 0.3), new RectangleF(cx - 30 * d, cy + r - 4 * d - lh, 60 * d, lh), HAlign.Center, VAlign.Top, false);
            for (int ring = 1; ring <= Math.Min(2, (int)scale - 1); ring++)
            {
                float rx = cx + (float)(r * ring / scale);
                p.Text(PooledText.Number("", ring, "g"), tiny, p.P.Muted, new RectangleF(rx - 12 * d, cy + 2 * d, 24 * d, lh), HAlign.Center, VAlign.Top, false);
            }
        }
        PointF At(double lat, double lon)
        {
            double x = -lat / scale, y = lon / scale, len = Math.Sqrt(x * x + y * y);
            if (len > 1) { x /= len; y /= len; }
            return new PointF(cx + (float)(x * r), cy + (float)(y * r));
        }
        if (trail)
        {
            int n = log.Trail(v.Now, _lat, _lon, _age);
            for (int i = 1; i < n; i++)
            {
                var a = At(_lat[i - 1], _lon[i - 1]);
                var b = At(_lat[i], _lon[i]);
                double fresh = 1 - Math.Clamp(_age[i] / GForceLog.TrailSeconds, 0, 1);
                p.Line(a.X, a.Y, b.X, b.Y, HudPalette.Alpha(p.P.Accent, (int)(30 + 140 * fresh)), Math.Max(1.5f, 3 * d * (float)(0.5 + 0.5 * fresh)));
            }
        }
        if (LatG(v) is { } la && LongG(v) is { } lo)
        {
            var at = At(la, lo);
            float dr = Math.Max(3 * d, Math.Min(7.5f * d, r * 0.09f));
            p.Dot(at.X, at.Y, dr, p.P.Accent);
            g.DrawEllipse(p.Pen(p.P.Heading, Math.Max(1, 2 * d * dr / (7.5f * d))), at.X - dr, at.Y - dr, dr * 2, dr * 2);
        }
    }

    private void DrawBars(HudPainter p, HudView v)
    {
        float d = p.Density;
        var log = v.Race.GForce;
        var area = p.Header("G-force", Wide(p) ? "Last 10 " + Units.S : null);
        var labelFont = p.Label;
        var valueFont = p.Bold;
        float rowH = p.LineHeight(valueFont), barH = 12 * d, rowGap = 4 * d;
        float barsH = (rowH + 3 * d + barH) * 2 + rowGap;
        float footH = p.LineHeight(p.SmallBold) + 8 * d;
        p.NeedBelow(area, barsH + 8 * d + 60 * d + 6 * d + footH);
        double scale = Scale(v);
        double? lat = LatG(v), lon = LongG(v);
        float traceTop = area.Y + (rowH + 3 * d) + barH + rowGap + (rowH + 3 * d) + barH + 8 * d;
        float traceBottom = area.Bottom - footH - 4 * d;
        bool trace = traceBottom - traceTop >= 30 * d;
        if (!trace) traceBottom = traceTop - 4 * d;
        bool showFoot = area.Bottom - footH >= traceBottom;
        float rowBlock = rowH + 3 * d + barH;
        Span<RectangleF> parts = stackalloc RectangleF[4];
        p.Arrange(v, "gforce.bars", area, false, rowGap, BarsParts, stackalloc float[] { rowBlock, rowBlock, trace ? 0 : -1, showFoot ? trace ? footH : 0 : -1 }, parts);
        bool words = Wide(p);
        string latText = lat is not { } la ? RaceFormat.Missing : Math.Abs(la) < 0.02 || !words ? GUnit(la, " g") : GUnit(la, la > 0 ? " g left" : " g right");
        BarRow(p, parts[0], parts[0].Y, "Lateral", latText, p.P.Heading, lat is { } l1 ? -l1 / scale : 0, p.P.Accent);
        bool braking = lon is < 0;
        var lonColor = braking ? p.P.BrakeInput : p.P.ThrottleInput;
        string lonText = lon is not { } lo ? RaceFormat.Missing : Math.Abs(lo) < 0.02 || !words ? GUnit(lo, " g") : GUnit(lo, lo < 0 ? " g brake" : " g accel");
        BarRow(p, parts[1], parts[1].Y, words ? "Brake / accel" : "Long", lonText, HudPalette.Mix(lonColor, p.P.Heading, 0.35), lon is { } l2 ? l2 / scale : 0, lonColor);
        if (trace) Trace(p, v, new RectangleF(parts[2].X, parts[2].Y + 4 * d, parts[2].Width, parts[2].Height - 4 * d), scale);
        if (!showFoot) return;
        var foot = new RectangleF(parts[3].X, trace ? parts[3].Y : parts[3].Bottom - footH, parts[3].Width, footH);
        string peak = log.Lap.Any ? GUnit(log.Lap.Combined, " g") : RaceFormat.Missing;
        string where = log.Lap.Any && log.Lap.CombinedPct >= 0 ? Where(v, log.Lap.CombinedPct) : "";
        Footer(p, foot, "Peak this lap", peak, where.Length > 0 ? PooledText.Concat("at ", where) : "", p.P.Accent);
    }

    private static void BarRow(HudPainter p, RectangleF area, float y, string label, string value, Color valueColor, double signed, Color fill)
    {
        float d = p.Density, rowH = p.LineHeight(p.Bold), barH = 12 * d;
        p.Text(p.LabelText(label), p.Label, p.P.Muted, new RectangleF(area.X, y, area.Width * 0.5f, rowH), HAlign.Left, VAlign.Center);
        p.Text(value, p.Bold, valueColor, new RectangleF(area.X + area.Width * 0.4f, y, area.Width * 0.6f, rowH), HAlign.Right, VAlign.Center);
        y += rowH + 3 * d;
        var bar = new RectangleF(area.X, y, area.Width, barH);
        p.Fill(bar, p.Solid(p.Sunken(0.3), 230), p.CornerRadius(3));
        float mid = bar.X + bar.Width / 2, w = (float)Math.Clamp(signed, -1, 1) * bar.Width / 2;
        if (Math.Abs(w) > 0.5f) p.Fill(w > 0 ? new RectangleF(mid, bar.Y, w, barH) : new RectangleF(mid + w, bar.Y, -w, barH), fill);
        p.Fill(new RectangleF(mid - d, bar.Y, 2 * d, barH), p.P.Muted);
    }

    private void Trace(HudPainter p, HudView v, RectangleF r, double scale)
    {
        float d = p.Density;
        p.Fill(r, p.Solid(p.Sunken(0.35), 230), p.CornerRadius(4));
        float mid = r.Y + r.Height / 2;
        p.Line(r.X + 1, mid, r.Right - 1, mid, HudPalette.Alpha(p.P.LineStrong, 140));
        int n = v.Race.GForce.Trace(v.Now, _lat, _lon, _age);
        var inner = r.Inflated(-2 * d, -3 * d);
        var clip = p.G.Save();
        p.G.SetClip(r);
        var latColor = p.P.Accent;
        var lonColor = p.P.ClutchInput;
        double most = 0.5;
        for (int i = 0; i < n; i++) most = Math.Max(most, Math.Max(Math.Abs(_lat[i]), Math.Abs(_lon[i])));
        scale = Math.Min(scale, Math.Ceiling(most * 2) / 2);
        Polyline(p, inner, n, _lon, false, scale, lonColor, 1.5f * d);
        Polyline(p, inner, n, _lat, true, scale, latColor, 2 * d);
        p.G.Restore(clip);
        var tiny = p.Tiny;
        float lh = p.LineHeight(tiny), x = r.X + 6 * d;
        string latLabel = p.LabelText("Lat"), lonLabel = p.LabelText("Long");
        p.Text(latLabel, tiny, latColor, new RectangleF(x, r.Y + 3 * d, p.Measure(latLabel, tiny) + 2, lh), HAlign.Left, VAlign.Top, false);
        x += p.Measure(latLabel, tiny) + 8 * d;
        p.Text(lonLabel, tiny, lonColor, new RectangleF(x, r.Y + 3 * d, p.Measure(lonLabel, tiny) + 2, lh), HAlign.Left, VAlign.Top, false);
    }

    private void Polyline(HudPainter p, RectangleF r, int n, double[] values, bool flip, double scale, Color c, float width)
    {
        if (n < 2) return;
        float mid = r.Y + r.Height / 2;
        for (int i = 0; i < n; i++)
        {
            double y = Math.Clamp(values[i] / scale, -1, 1) * (flip ? -1 : 1);
            float x = r.Right - (float)(Math.Clamp(_age[i] / GForceLog.TraceSeconds, 0, 1) * r.Width);
            _points[i] = new PointF(x, mid + (float)(y * r.Height / 2));
        }
        var pen = p.Pen(c, width);
        for (int i = 1; i < n; i++) p.G.DrawLine(pen, _points[i - 1], _points[i]);
    }

    private static void Footer(HudPainter p, RectangleF r, string label, string value, string tail, Color stripe, Color? valueColor = null)
    {
        float d = p.Density;
        p.Fill(new RectangleF(r.X, r.Y + 4 * d, 2 * d, r.Height - 6 * d), stripe);
        var font = p.SmallBold;
        float tw = tail.Length > 0 ? p.Measure(tail, p.Small) + 2 : 0, vw = p.Measure(value, font) + 2;
        float right = r.Right;
        if (tw > 0)
        {
            p.Text(tail, p.Small, p.P.Muted, new RectangleF(right - tw, r.Y + 1 * d, tw, r.Height), HAlign.Right, VAlign.Center, false);
            right -= tw + 4 * d;
        }
        p.Text(value, font, valueColor ?? p.P.Heading, new RectangleF(right - vw, r.Y + 1 * d, vw, r.Height), HAlign.Right, VAlign.Center, false);
        right -= vw + 8 * d;
        p.Text(label, p.Small, p.P.Muted, new RectangleF(r.X + 8 * d, r.Y + 1 * d, Math.Max(0, right - r.X - 8 * d), r.Height), HAlign.Left, VAlign.Center);
    }

    private static readonly string[] CompareRows = { "Corner", "Brake", "Accel" };

    private void DrawCompare(HudPainter p, HudView v)
    {
        float d = p.Density;
        var log = v.Race.GForce;
        bool last = log.Lap.SeenCount < 3 && log.HasLast;
        var shown = last ? log.Last : log.Lap;
        var area = p.Header("G-force", Wide(p) ? last ? "Last lap vs best" : "Vs best lap" : null);
        var font = p.Font(13f, true);
        float rowH = p.LineHeight(font) + 6 * d, headH = p.LineHeight(p.Label) + 4 * d;
        float tableH = headH + rowH * 3;
        float footH = p.LineHeight(p.SmallBold) + 8 * d;
        p.NeedBelow(area, tableH + 6 * d + footH);
        float circle = Math.Min(tableH, 84 * d);
        bool showCircle = area.Width >= 230 * d;
        bool showFoot = area.Height >= tableH + 6 * d + footH;
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "gforce.compare", area, false, 6 * d, CompareParts, stackalloc float[] { 0, showFoot ? footH : -1 }, parts);
        var top = parts[0];
        Span<RectangleF> sides = stackalloc RectangleF[2];
        p.Arrange(v, "gforce.compare.peaks", top, true, 8 * d, ComparePeaks, stackalloc float[] { showCircle ? circle : -1, 0 }, sides);
        if (showCircle)
        {
            var box = new RectangleF(sides[0].X, top.Y + (tableH - circle) / 2, circle, circle);
            p.Tile(box);
            Friction(p, v, box.Inflated(-5 * d, -5 * d), false, true);
        }
        var table = new RectangleF(sides[1].X, top.Y, sides[1].Width, tableH);
        float colW = 48 * d, nameW = 54 * d;
        int cols = 3;
        while (cols > 1 && nameW + colW * cols > table.Width) cols--;
        float c0 = table.Right - colW * cols;
        p.Text(p.LabelText(last ? "Last" : "Lap"), p.Label, p.P.Muted, new RectangleF(c0, table.Y, colW, headH), HAlign.Right, VAlign.Center, false);
        if (cols > 1) p.Text(p.LabelText("Best"), p.Label, p.P.Muted, new RectangleF(c0 + colW, table.Y, colW, headH), HAlign.Right, VAlign.Center, false);
        if (cols > 2) p.Text("Δ", p.Label, p.P.Muted, new RectangleF(c0 + colW * 2, table.Y, colW, headH), HAlign.Right, VAlign.Center, false);
        float y = table.Y + headH;
        for (int i = 0; i < CompareRows.Length; i++)
        {
            if (y + rowH > top.Bottom + 1) break;
            double lap = i switch { 0 => shown.Corner, 1 => shown.Brake, _ => shown.Accel };
            double best = i switch { 0 => log.Best.Corner, 1 => log.Best.Brake, _ => log.Best.Accel };
            var row = new RectangleF(table.X, y, table.Width, rowH);
            p.Line(row.X, MathF.Round(row.Y) + 0.5f, row.Right, MathF.Round(row.Y) + 0.5f, p.P.Line);
            p.Text(CompareRows[i], font, p.P.Text, new RectangleF(row.X, row.Y, c0 - row.X, rowH), HAlign.Left, VAlign.Center);
            p.Text(shown.Any ? G(lap) : RaceFormat.Missing, font, p.P.Heading, new RectangleF(c0, row.Y, colW, rowH), HAlign.Right, VAlign.Center, false);
            if (cols > 1) p.Text(log.HasBest ? G(best) : RaceFormat.Missing, font, p.P.Muted, new RectangleF(c0 + colW, row.Y, colW, rowH), HAlign.Right, VAlign.Center, false);
            if (cols > 2)
            {
                bool both = log.HasBest && shown.Any;
                double delta = lap - best;
                var color = !both ? p.P.Muted : delta < -0.005 ? p.P.DangerText : delta > 0.005 ? p.P.GoodText : p.P.Muted;
                p.Text(both ? RaceFormat.Gap(delta, 2) : RaceFormat.Missing, font, color, new RectangleF(c0 + colW * 2, row.Y, colW, rowH), HAlign.Right, VAlign.Center, false);
            }
            y += rowH;
        }
        if (!showFoot) return;
        var foot = new RectangleF(parts[1].X, parts[1].Y, parts[1].Width, footH);
        if (!log.HasBest)
        {
            p.Text("Best lap peaks after a clean lap", p.Small, p.P.Faint, foot, HAlign.Left, VAlign.Center);
            return;
        }
        if (log.Softest(v.Frame.LapDistPct) is not { } soft)
        {
            p.Soft(foot, p.P.Good, 60);
            p.Text("Cornering as hard as your best lap", p.Small, p.P.GoodText, foot.Inflated(-8 * d, 0), HAlign.Left, VAlign.Center);
            return;
        }
        p.Soft(foot, p.P.Warn, 70);
        string where = Where(v, GLap.PieceMiddle(soft.Piece));
        var inner = foot.Inflated(-8 * d, 0);
        string tail = p.LabelText(soft.FromLastLap ? "Last lap" : "This lap");
        float tw = p.Measure(tail, p.Label) + 2;
        p.Text(tail, p.Label, p.P.WarnText, new RectangleF(inner.Right - tw, inner.Y, tw, inner.Height), HAlign.Right, VAlign.Center, false);
        p.Text(PooledText.Concat("Cornering ", PooledText.Number("", soft.Deficit, "0.00", Inv, " g softer at "), where), p.Small, p.P.Heading,
            new RectangleF(inner.X, inner.Y, Math.Max(0, inner.Width - tw - 8 * d), inner.Height), HAlign.Left, VAlign.Center);
    }
}
