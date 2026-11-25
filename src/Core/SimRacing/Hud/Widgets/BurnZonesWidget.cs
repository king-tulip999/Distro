using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Core.SimRacing.Race;
using Core.SimRacing.TrackMaps;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class BurnZonesWidget : HudWidget
{
    public const string Map = "map", Bar = "bar", Gears = "gears";
    public const double W = 340, H = 250;
    private const int Pieces = BurnZones.Pieces, MapSamples = 240, PerPiece = MapSamples / Pieces, MaxSectorTiles = 6;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] MapParts = { "map", "legend", "tiles" }, MapTiles = { "perlap", "target", "save" };
    private static readonly string[] BarParts = { "strip", "sectors", "tip" }, GearParts = { "head", "list" };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Gears,
            new HudChoice(Map, "Heat map", "The track colored by how much fuel each stretch burns"),
            new HudChoice(Bar, "Lap bar and tip", "The lap as a colored bar with a lift-and-coast tip"),
            new HudChoice(Gears, "Gear economy", "Time and fuel per gear")),
        TitleRow,
    };

    private readonly double[] _fuel = new double[Pieces];
    private double _lo, _hi, _lap;
    private bool _even;
    private readonly HudMapSource _maps = new();
    private TrackMap? _builtFor;
    private RectangleF _builtIn;
    private float _builtDensity;
    private readonly PointF[] _centre = new PointF[MapSamples + 1], _normal = new PointF[MapSamples + 1];
    private readonly PointF[][] _piece = PieceLines();
    private readonly List<RectangleF> _labels = new();
    private readonly double[] _sectorFuel = new double[MaxSectorTiles];
    private readonly int[] _gearRows = new int[BurnZones.MaxGears];

    private static PointF[][] PieceLines()
    {
        var lines = new PointF[Pieces][];
        for (int i = 0; i < Pieces; i++) lines[i] = new PointF[PerPiece + 1];
        return lines;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Fuel burn zones")) return;
        Load(v);
        switch (v.Options.Choice("style"))
        {
            case Map: DrawMap(p, v); break;
            case Bar: DrawBar(p, v); break;
            default: DrawGears(p, v); break;
        }
    }

    private void Load(HudView v)
    {
        var burn = v.Race.Burn;
        _lap = burn.LapFuel;
        if (v.Frame.Simulated && burn.CleanLaps > 0 && BurnZones.Even(burn.Fuel))
        {
            double weight = 0;
            for (int i = 0; i < Pieces; i++) weight += 0.3 + burn.Throttle[i] * burn.Throttle[i];
            for (int i = 0; i < Pieces; i++) _fuel[i] = _lap * (0.3 + burn.Throttle[i] * burn.Throttle[i]) / weight;
        }
        else for (int i = 0; i < Pieces; i++) _fuel[i] = burn.Fuel[i];
        _lo = double.PositiveInfinity;
        _hi = double.NegativeInfinity;
        for (int i = 0; i < Pieces; i++)
        {
            _lo = Math.Min(_lo, _fuel[i]);
            _hi = Math.Max(_hi, _fuel[i]);
        }
        _even = BurnZones.Even(_fuel);
    }

    private double Heat(int piece) => _even ? 0.5 : (_fuel[piece] - _lo) / (_hi - _lo);

    private static Color Ramp(HudPainter p, double t)
    {
        const double knee = 0.62;
        t = Math.Clamp(t, 0, 1);
        if (t < knee) return HudPalette.Mix(HudPalette.Mix(p.P.Surface, p.P.Warn, 0.3), p.P.Warn, t / knee);
        return HudPalette.Mix(p.P.Warn, p.P.Danger, (t - knee) / (1 - knee));
    }

    private static string LapsText(HudView v) => v.Race.Burn.CleanLaps > 0 ? RaceFormat.Count(v.Race.Burn.CleanLaps, "clean lap") : "Learning";

    private static void Learning(HudPainter p, RectangleF r) =>
        p.Text("Shows after your first clean lap", p.Small, p.P.Faint, r, HAlign.Center, VAlign.Center);

    private static FuelSaveTarget? Target(HudView v)
    {
        double? least = null;
        var laps = v.Race.FuelData.Laps;
        for (int i = 0; i < laps.Count; i++)
            if (laps[i] is var l && l.IsEligible && l.FuelUsedLiters > 0 && (least is null || l.FuelUsedLiters < least)) least = l.FuelUsedLiters;
        return FuelSave.Target(v.Race.Fuel, v.Frame.Fuel ?? 0, v.Race.Tank, least);
    }

    private static string FuelText(double litres, HudView v) => RaceFormat.Fuel(litres, v.Units);

    private void DrawMap(HudPainter p, HudView v)
    {
        float d = p.Density;
        float mapH = 160 * d, legendH = 14 * d, tileH = p.BoxedStatHeight(p.Bold), gap = 6 * d;
        float body = mapH + legendH + gap + tileH;
        p.NeedWidth(220 * d);
        p.NeedBelow(p.HeaderBody(), body);
        var area = p.Header("Fuel burn", LapsText(v), p.P.Muted, body);
        bool tiles = area.Height >= 80 * d + legendH + gap + tileH;
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "burn.map", area, false, 0, MapParts, stackalloc float[] { 0, tiles ? legendH : -1, tiles ? gap + tileH : -1 }, boxes);
        var room = boxes[0];
        bool learned = v.Race.Burn.CleanLaps > 0;
        var map = _maps.Get(p, v.Frame);
        if (!learned) Learning(p, room);
        else if (map is null) Strip(p, v, new RectangleF(room.X, room.Y + room.Height / 2 - 14 * d, room.Width, 18 * d), true);
        else HeatMap(p, v, map, room);
        if (!tiles) return;
        Legend(p, boxes[1]);
        var row = boxes[2];
        float y = row.Y <= area.Y ? row.Y : row.Bottom - tileH;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "burn.map.tiles", new RectangleF(row.X, y, row.Width, tileH), true, gap, MapTiles, stackalloc float[] { 0, 0, 0 }, cells);
        var t = Target(v);
        double? perLap = learned ? _lap : v.Race.Fuel?.PerLap;
        p.BoxedStat(cells[0], "Per lap", perLap is { } pl ? FuelText(pl, v) : RaceFormat.Missing, p.P.Heading, p.Bold);
        p.BoxedStat(cells[1], "Target", t is { } a ? FuelText(a.Target, v) : RaceFormat.Missing, t is { } b ? b.Spare || b.Proven ? p.P.GoodText : p.P.WarnText : p.P.Faint, p.Bold);
        string saveLabel = t is { Spare: true } ? "Spare" : "To save";
        p.BoxedStat(cells[2], saveLabel, t is { } c ? FuelText(Math.Abs(c.Save), v) : RaceFormat.Missing, t is { } e ? e.Spare ? p.P.GoodText : p.P.WarnText : p.P.Faint, p.Bold);
    }

    private void Legend(HudPainter p, RectangleF r)
    {
        float d = p.Density;
        float lw = p.Measure("less", p.Tiny) + 4 * d, mw = p.Measure("more", p.Tiny) + 4 * d;
        p.Text("less", p.Tiny, p.P.Faint, new RectangleF(r.X, r.Y, lw, r.Height), HAlign.Left, VAlign.Center, false);
        p.Text("more", p.Tiny, p.P.Faint, new RectangleF(r.Right - mw, r.Y, mw, r.Height), HAlign.Right, VAlign.Center, false);
        var bar = RectangleF.FromLTRB(r.X + lw + 4 * d, r.Y + r.Height / 2 - 2.5f * d, r.Right - mw - 4 * d, r.Y + r.Height / 2 + 2.5f * d);
        if (bar.Width <= 0) return;
        float half = MathF.Floor(bar.Width * 0.62f);
        p.Gradient(new RectangleF(bar.X, bar.Y, half, bar.Height), Ramp(p, 0), Ramp(p, 0.62));
        p.Gradient(new RectangleF(bar.X + half, bar.Y, bar.Width - half, bar.Height), Ramp(p, 0.62), Ramp(p, 1));
    }

    private void BuildMap(HudPainter p, TrackMap map, RectangleF room)
    {
        if (ReferenceEquals(map, _builtFor) && room == _builtIn && p.Density == _builtDensity) return;
        _builtFor = map;
        _builtIn = room;
        _builtDensity = p.Density;
        float margin = 20 * p.Density;
        var extent = map.Bounds;
        float scale = MapView.FitScale(extent, new SizeF(Math.Max(10, room.Width - margin * 2), Math.Max(10, room.Height - margin * 2)), false);
        var view = new MapView(new PointF(room.X + room.Width / 2, room.Y + room.Height / 2), new PointF(extent.X + extent.Width / 2, extent.Y + extent.Height / 2), scale, 0);
        for (int i = 0; i <= MapSamples; i++) _centre[i] = view.ToScreen(map.At(i / (double)MapSamples));
        double area = 0;
        for (int i = 0; i < MapSamples; i++) area += _centre[i].X * _centre[i + 1].Y - _centre[i + 1].X * _centre[i].Y;
        float outside = area > 0 ? -1 : 1;
        for (int i = 0; i <= MapSamples; i++)
        {
            var a = i == 0 ? _centre[MapSamples - 1] : _centre[i - 1];
            var b = i == MapSamples ? _centre[1] : _centre[i + 1];
            float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            _normal[i] = new PointF(-dy / len * outside, dx / len * outside);
        }
        for (int piece = 0; piece < Pieces; piece++)
            for (int j = 0; j <= PerPiece; j++) _piece[piece][j] = _centre[piece * PerPiece + j];
    }

    private PointF MapPoint(double pct, out PointF normal)
    {
        double x = (pct - Math.Floor(pct)) * MapSamples;
        int i = Math.Clamp((int)x, 0, MapSamples - 1);
        float u = (float)(x - i);
        normal = _normal[i];
        return new PointF(_centre[i].X + (_centre[i + 1].X - _centre[i].X) * u, _centre[i].Y + (_centre[i + 1].Y - _centre[i].Y) * u);
    }

    private void HeatMap(HudPainter p, HudView v, TrackMap map, RectangleF room)
    {
        float d = p.Density;
        BuildMap(p, map, room);
        var g = p.G;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(p.Pen(p.P.MapEdge ?? HudPalette.Mix(p.P.Surface, p.P.Bg, 0.6), 13 * d), _centre);
        for (int i = 0; i < Pieces; i++) g.DrawLines(p.Pen(Ramp(p, Heat(i)), 9 * d), _piece[i]);
        var start = MapPoint(0, out var sn);
        p.Line(start.X - sn.X * 9 * d, start.Y - sn.Y * 9 * d, start.X + sn.X * 9 * d, start.Y + sn.Y * 9 * d, p.P.Heading, 2.5f * d);
        _labels.Clear();
        if (BurnZones.Hottest(_fuel) is var (first, len, fuel) && _lap > 0)
        {
            var at = MapPoint((first + len / 2.0) / Pieces, out var nrm);
            string share = PooledText.Number("", fuel / _lap * 100, "0", Inv, "%");
            var font = p.Font(13f, true);
            float tw = p.Measure(share, font) + 2, th = p.LineHeight(font), reach = 20 * d;
            var label = new RectangleF(at.X - nrm.X * reach - tw / 2, at.Y - nrm.Y * reach - th / 2, tw, th);
            label.X = Math.Clamp(label.X, room.X, room.Right - tw);
            label.Y = Math.Clamp(label.Y, room.Y, room.Bottom - th);
            _labels.Add(label);
            p.Text(share, font, p.P.DangerText, label, HAlign.Center, VAlign.Center, false);
        }
        var turns = v.Race.Turns.All;
        int named = room.Height >= 110 * d ? turns.Count : 0;
        for (int i = 0; i < named; i++)
        {
            var (pct, name) = turns[i];
            var at = MapPoint(pct, out var nrm);
            float tw = p.Measure(name, p.Tiny) + 2, th = p.LineHeight(p.Tiny), reach = 15 * d;
            var label = new RectangleF(at.X + nrm.X * reach - tw / 2, at.Y + nrm.Y * reach - th / 2, tw, th);
            label.X = Math.Clamp(label.X, room.X, room.Right - tw);
            label.Y = Math.Clamp(label.Y, room.Y, room.Bottom - th);
            bool clash = false;
            foreach (var l in _labels) if (l.IntersectsWith(label)) { clash = true; break; }
            if (clash) continue;
            _labels.Add(label);
            p.Text(name, p.Tiny, p.P.Faint, label, HAlign.Center, VAlign.Center, false);
        }
        int me = v.Frame.PlayerCarIdx;
        if ((uint)me < Telemetry.TelemetryFrame.MaxCars && v.Frame.CarLapDistPct[me] >= 0)
        {
            var at = MapPoint(v.Frame.CarLapDistPct[me], out _);
            p.Dot(at.X, at.Y, 6 * d, p.P.Heading);
            p.Dot(at.X, at.Y, 4.5f * d, p.P.Accent);
        }
        g.SmoothingMode = old;
    }

    private void Strip(HudPainter p, HudView v, RectangleF r, bool turns)
    {
        float d = p.Density, gap = Math.Max(1, d);
        float w = (r.Width - gap * (Pieces - 1)) / Pieces;
        for (int i = 0; i < Pieces; i++) p.Fill(new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height), Ramp(p, Heat(i)), p.CornerRadius(2));
        float me = v.Frame.LapDistPct is { } pct ? r.X + (float)(pct - Math.Floor(pct)) * r.Width : float.NaN;
        if (float.IsFinite(me)) p.Fill(new RectangleF(me - d, r.Y - 3 * d, 2 * d, r.Height + 6 * d), p.P.Heading, d);
        if (!turns) return;
        float th = p.LineHeight(p.Tiny), right = float.NegativeInfinity;
        var all = v.Race.Turns.All;
        for (int i = 0; i < all.Count; i++)
        {
            var (at, name) = all[i];
            float tw = p.Measure(name, p.Tiny) + 2;
            float x = Math.Clamp(r.X + (float)at * r.Width - tw / 2, r.X, r.Right - tw);
            if (x < right + 4 * d) continue;
            right = x + tw;
            p.Text(name, p.Tiny, p.P.Faint, new RectangleF(x, r.Bottom + 3 * d, tw, th), HAlign.Center, VAlign.Top, false);
        }
    }

    private void DrawBar(HudPainter p, HudView v)
    {
        float d = p.Density, gap = 6 * d;
        float barH = 22 * d, turnsH = p.LineHeight(p.Tiny) + 4 * d, tileH = p.BoxedStatHeight(p.Bold);
        float footH = p.LineHeight(p.Bold) + p.LineHeight(p.Small) + 10 * d;
        float body = 4 * d + barH + turnsH + gap + tileH + gap + footH;
        p.NeedWidth(260 * d);
        p.NeedBelow(p.HeaderBody(), body);
        var t = Target(v);
        bool learned = v.Race.Burn.CleanLaps > 0;
        string? right = t is { Spare: false } s ? PooledText.Concat("Need ", PooledText.Concat(RaceFormat.Signed(-RaceFormat.FuelValue(s.Save, v.Units), "0.00", Inv), PooledText.Concat(" ", PooledText.Concat(RaceFormat.FuelUnit(v.Units), "/lap"))))
            : t is { Spare: true } ? "On target" : LapsText(v);
        var area = p.Header("Fuel by stretch", right, t is null ? p.P.Muted : t.Value.Spare ? p.P.GoodText : p.P.WarnText, body);
        float y = area.Y + 4 * d;
        if (!learned)
        {
            Learning(p, new RectangleF(area.X, y, area.Width, body - 4 * d));
            return;
        }
        float stripH = 4 * d + barH + turnsH;
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "burn.bar", area, false, gap, BarParts, stackalloc float[] { stripH, tileH, footH }, boxes);
        if (boxes[0].Y <= area.Y || boxes[0].Y + stripH <= area.Bottom + 1) Strip(p, v, new RectangleF(area.X, boxes[0].Y + 4 * d, area.Width, barH), true);
        if (boxes[1].Y + tileH <= area.Bottom + 1) Sectors(p, v, new RectangleF(area.X, boxes[1].Y, area.Width, tileH));
        if (boxes[2].Y + footH <= area.Bottom + 1) Tip(p, v, t, new RectangleF(area.X, boxes[2].Y, area.Width, footH));
    }

    private void Sectors(HudPainter p, HudView v, RectangleF r)
    {
        float d = p.Density, gap = 6 * d;
        var sectors = v.Frame.Session.Sectors;
        int n = sectors.Count is > 0 and <= MaxSectorTiles ? sectors.Count : 3;
        float tw = (r.Width - gap * (n - 1)) / n;
        if (tw < 52 * d) return;
        int hot = 0, cool = 0;
        for (int i = 0; i < n; i++)
        {
            double from = sectors.Count == n ? sectors[i].StartPct : i / (double)n;
            double to = i + 1 < n ? sectors.Count == n ? sectors[i + 1].StartPct : (i + 1) / (double)n : 1;
            _sectorFuel[i] = BurnZones.Between(_fuel, from, to);
            if (_sectorFuel[i] > _sectorFuel[hot]) hot = i;
            if (_sectorFuel[i] < _sectorFuel[cool]) cool = i;
        }
        for (int i = 0; i < n; i++)
        {
            var box = new RectangleF(r.X + i * (tw + gap), r.Y, tw, r.Height);
            p.BoxedStat(box, PooledText.Number("S", i + 1), FuelText(_sectorFuel[i], v), p.P.Heading, p.Bold);
            if (n < 2 || _even) continue;
            var mark = i == hot ? p.P.Danger : i == cool ? p.P.Good : p.P.Warn;
            p.Fill(new RectangleF(box.X + 6 * d, box.Bottom - 3 * d, box.Width - 12 * d, 2 * d), mark, d);
        }
    }

    private void Tip(HudPainter p, HudView v, FuelSaveTarget? target, RectangleF r)
    {
        float d = p.Density;
        p.Line(r.X, MathF.Round(r.Y) + 0.5f, r.Right, MathF.Round(r.Y) + 0.5f, p.P.Line, 1);
        var top = new RectangleF(r.X, r.Y + 5 * d, r.Width, p.LineHeight(p.Bold));
        var under = new RectangleF(r.X, top.Bottom + 1, r.Width, p.LineHeight(p.Small));
        if (target is not { } t)
        {
            p.Text("A lift-and-coast tip shows when the fuel plan comes up short", p.Small, p.P.Faint, new RectangleF(r.X, r.Y, r.Width, r.Height), HAlign.Left, VAlign.Center);
            return;
        }
        if (t.Spare || t.Save <= 0)
        {
            p.Text("No lift needed", p.Bold, p.P.GoodText, top, HAlign.Left, VAlign.Center);
            p.Text("The fuel plan reaches the flag at your pace", p.Small, p.P.Muted, under, HAlign.Left, VAlign.Center);
            return;
        }
        var burn = v.Race.Burn;
        if (BurnZones.Lift(_fuel, burn.Throttle, burn.Brake, t.Save) is not { } tip)
        {
            p.Text("No full-throttle straight into a braking zone found", p.Small, p.P.Muted, new RectangleF(r.X, r.Y, r.Width, r.Height), HAlign.Left, VAlign.Center);
            return;
        }
        string turn = v.Race.Turns.Where((tip.BrakePiece + 0.5) / Pieces);
        string distance = v.Frame.Session.TrackLengthKm is { } km
            ? RaceFormat.Distance(Math.Round(tip.LiftPct * km * 1000 / 10) * 10, v.Units)
            : PooledText.Number("", tip.LiftPct * 100, "0", Inv, "% of the lap");
        string head = tip.Enough ? PooledText.Concat("Lift ", PooledText.Concat(distance, PooledText.Concat(" earlier into ", turn)))
            : PooledText.Concat("Coast the whole straight into ", turn);
        p.Text(head, p.Bold, p.P.GoodText, top, HAlign.Left, VAlign.Center);
        string saves = PooledText.Concat(RaceFormat.About(RaceFormat.Fuel(tip.Saves, v.Units)), PooledText.Concat(" a lap of ", PooledText.Concat(RaceFormat.Fuel(t.Save, v.Units), " needed")));
        p.Text(saves, p.Small, p.P.Muted, under, HAlign.Left, VAlign.Center);
    }

    private static readonly string[] GearHeads = { "Gear", "Time", "Fuel / min", "Share", "Upshift" };

    private void DrawGears(HudPainter p, HudView v)
    {
        float d = p.Density;
        var burn = v.Race.Burn;
        int gears = Math.Clamp(Math.Max(burn.TopGear, v.Frame.Session.ForwardGears ?? 0), 1, BurnZones.MaxGears);
        float headH = p.LineHeight(p.Label) + 6 * d, rowH = p.LineHeight(p.Bold) + 7 * d;
        float body = headH + rowH * gears + 2 * d;
        p.NeedWidth(250 * d);
        p.NeedBelow(p.HeaderBody(), body);
        var area = p.Header("Gear economy", "This session", p.P.Muted, body);
        if (burn.GearTimeTotal <= 0)
        {
            p.Text("Shows once you're driving", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        float gearW = 40 * d, timeW = 46 * d, shareW = 50 * d, shiftW = 66 * d, gap = 8 * d;
        float barX = area.X + gearW + timeW + gap, barRight = area.Right - shareW - shiftW - gap;
        bool shift = barRight - barX >= 60 * d;
        if (!shift) barRight = area.Right - shareW - gap;
        Span<RectangleF> boxes = stackalloc RectangleF[2];
        p.Arrange(v, "burn.gears", area, false, 2 * d, GearParts, stackalloc float[] { headH, 0 }, boxes);
        float y = boxes[0].Y;
        var head = new RectangleF(area.X, y, area.Width, headH);
        p.Text(p.LabelText(GearHeads[0]), p.Label, p.LabelColor, new RectangleF(area.X + 4 * d, y, gearW, headH), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText(GearHeads[1]), p.Label, p.LabelColor, new RectangleF(area.X + gearW, y, timeW, headH), HAlign.Right, VAlign.Center, false);
        p.Text(p.LabelText(GearHeads[2]), p.Label, p.LabelColor, new RectangleF(barX, y, barRight - barX - 4 * d, headH), HAlign.Left, VAlign.Center);
        float shareX = barRight + gap;
        p.Text(p.LabelText(GearHeads[3]), p.Label, p.LabelColor, new RectangleF(shareX, y, shareW, headH), HAlign.Right, VAlign.Center, false);
        if (shift) p.Text(p.LabelText(GearHeads[4]), p.Label, p.LabelColor, new RectangleF(shareX + shareW, y, shiftW, headH), HAlign.Right, VAlign.Center, false);
        p.Line(head.X, MathF.Round(head.Bottom) - 0.5f, head.Right, MathF.Round(head.Bottom) - 0.5f, p.P.Line, 1);
        y = boxes[1].Y;

        int fit = Math.Clamp((int)((area.Height - headH - 2 * d + 1) / rowH), 0, gears);
        int shown = Rows(burn, gears, fit);
        double most = 0;
        for (int g = 1; g <= gears; g++)
            if (burn.GearTime(g) > 0) most = Math.Max(most, burn.GearFuel(g) / burn.GearTime(g));
        for (int k = 0; k < shown; k++)
        {
            int g = _gearRows[k];
            var row = new RectangleF(area.X, y, area.Width, rowH);
            bool now = burn.Gear == g;
            if (now) p.Fill(row, p.Solid(HudPalette.Alpha(p.P.Player, 90)), p.CornerRadius(3));
            double time = burn.GearTime(g), fuel = burn.GearFuel(g);
            p.Text(RaceFormat.Gear(g), p.Bold, now ? p.P.PlayerText : p.P.Heading, new RectangleF(row.X + 8 * d, row.Y, gearW - 8 * d, rowH), HAlign.Left, VAlign.Center, false);
            p.Text(time > 0 ? PooledText.Number("", time / burn.GearTimeTotal * 100, "0", Inv, "%") : RaceFormat.Missing, p.Body, now ? p.P.Text : p.P.Muted, new RectangleF(row.X + gearW, row.Y, timeW, rowH), HAlign.Right, VAlign.Center, false);
            double rate = time > 0 ? fuel / time * 60 : 0;
            string rateText = time > 0 ? PooledText.Number("", RaceFormat.FuelValue(rate, v.Units), "0.00", Inv) : RaceFormat.Missing;
            float rw = p.Measure("0.00", p.SmallBold) + 4 * d;
            var track = RectangleF.FromLTRB(barX, row.Y + rowH / 2 - 3 * d, barRight - rw - 4 * d, row.Y + rowH / 2 + 3 * d);
            if (track.Width > 8 * d) p.Bar(track, most > 0 ? rate / most : 0, now ? p.P.Accent : Ramp(p, most > 0 ? rate / most : 0), HudPalette.Alpha(p.P.LineStrong, 70));
            p.Text(rateText, p.SmallBold, now ? p.P.Text : p.P.Muted, new RectangleF(barRight - rw, row.Y, rw, rowH), HAlign.Right, VAlign.Center, false);
            string share = burn.GearFuelTotal > 0 && time > 0 ? PooledText.Number("", fuel / burn.GearFuelTotal * 100, "0", Inv, "%") : RaceFormat.Missing;
            p.Text(share, p.Bold, now ? p.P.PlayerText : p.P.Heading, new RectangleF(shareX, row.Y, shareW, rowH), HAlign.Right, VAlign.Center, false);
            if (shift)
            {
                string rpm = burn.UpshiftRpm(g) is { } r ? PooledText.Number("", r, "#,0", Inv) : RaceFormat.Missing;
                p.Text(rpm, p.Body, now ? p.P.Text : p.P.Muted, new RectangleF(shareX + shareW, row.Y, shiftW, rowH), HAlign.Right, VAlign.Center, false);
            }
            y += rowH;
        }
    }

    private int Rows(BurnZones burn, int gears, int fit)
    {
        int n = 0;
        for (int g = 1; g <= gears; g++) _gearRows[n++] = g;
        while (n > fit)
        {
            int drop = 0;
            for (int k = 1; k < n; k++) if (burn.GearTime(_gearRows[k]) < burn.GearTime(_gearRows[drop])) drop = k;
            for (int k = drop; k < n - 1; k++) _gearRows[k] = _gearRows[k + 1];
            n--;
        }
        return n;
    }
}
