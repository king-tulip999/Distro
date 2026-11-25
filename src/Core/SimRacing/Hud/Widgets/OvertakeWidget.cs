using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class OvertakeWidget : HudWidget
{
    public const string Banner = "banner", Edge = "edge", Card = "card";
    public const string RightEdge = "right", LeftEdge = "left";
    public const double W = 380, H = 44;
    private const float BannerH = 44, EdgeW = 72, CardW = 352;
    private const double SampleGap = -0.6, SampleGainPerLap = 0.4;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Banner,
            new HudChoice(Banner, "Top banner", "A banner with who's catching and the gap"),
            new HudChoice(Edge, "Edge glow", "A glowing bar down the screen edge"),
            new HudChoice(Card, "Who and when", "Who's coming, how fast and the corner they'll reach you in")),
        TitleRow,
        HudOption.Number("within", "Show a car within", 1.0, 0.3, 3, 0.1, Units.S, "Shows while a car behind is this close and catching you"),
        HudOption.Bool("sameClass", "Only my class", true),
        HudOption.Choice("edge", "Screen edge", RightEdge,
            new HudChoice(RightEdge, "Right", "Place the widget on the right edge of the screen"),
            new HudChoice(LeftEdge, "Left", "Place the widget on the left edge of the screen")),
        HudOption.Int("length", "Bar length", 320, 120, 1200, "px"),
    };

    private long _seq = long.MinValue;
    private OvertakeThreat? _live;
    private int _held = -1;
    private readonly DuelSnapshot _duel = new();
    private long _duelSeq = long.MinValue;
    private int _duelCar = -1;
    private readonly PointF[] _spark = new PointF[DuelSnapshot.HistoryLaps + 1];
    private readonly PointF[] _chevron = new PointF[3];

    public override bool HasContent(HudView v) => Live(v) is not null;

    private OvertakeThreat? Live(HudView v)
    {
        var f = v.Frame;
        if (f.Sequence == _seq) return _live;
        _seq = f.Sequence;
        _live = f.Status == SimStatus.Connected && !f.IsReplayPlaying
            ? Overtake.Find(f, v.Race.Gaps, v.Race.Profile, v.Options.Bool("sameClass"), v.Options.Number("within"), _held)
            : null;
        _held = _live?.Car ?? -1;
        return _live;
    }

    private static OvertakeThreat? Sample(HudView v)
    {
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars) return null;
        int myClass = FieldOrder.ClassOf(f, me), best = -1;
        double bestGap = double.NegativeInfinity;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i] || FieldOrder.ClassOf(f, i) != myClass) continue;
            if (FieldOrder.GapSeconds(f, me, i, v.Race.Profile) is not { } g || g >= 0) continue;
            if (g > bestGap) { bestGap = g; best = i; }
        }
        if (best < 0) return null;
        double lap = FieldOrder.LapTimeOf(f, me) ?? 100;
        return new OvertakeThreat(best, SampleGap, -SampleGainPerLap / lap, false, OvertakeSide.Unknown);
    }

    public override void Draw(HudPainter p, HudView v)
    {
        string style = v.Options.Choice("style");
        if (style == Card) { if (DrawWaiting(p, v, "Catching you")) return; }
        else if (DrawWaiting(p, v)) return;
        var threat = Live(v);
        if (threat is null && (p.IsPreview || v.Frame.Simulated)) threat = Sample(v);
        switch (style)
        {
            case Edge: DrawEdge(p, v, threat); break;
            case Card: DrawCard(p, v, threat); break;
            default: DrawBanner(p, v, threat); break;
        }
    }

    private static string WhenText(HudView v) => PooledText.Number("Shows when a car closes within ", v.Options.Number("within"), "0.0", Inv, " " + Core.Services.Units.S);

    private static string GapText(OvertakeThreat t) => t.Alongside ? "0.0" : PooledText.Number("", t.Behind, "0.0", Inv);

    private static string CarNumber(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) && d.CarNumber.Length > 0 ? PooledText.Concat("#", d.CarNumber) : RaceFormat.Missing;

    private static string SideWord(OvertakeSide side) => side switch
    {
        OvertakeSide.Left => "Car left",
        OvertakeSide.Right => "Car right",
        OvertakeSide.Both => "Both sides",
        _ => "Closing",
    };

    private void Arrow(HudPainter p, float cx, float cy, float size, Color c, bool up)
    {
        float w = size * 0.5f, h = size * 0.5f;
        float stroke = Math.Max(1.5f, size * 0.18f);
        var g = p.G;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (up)
        {
            p.Line(cx, cy - h, cx, cy + h, c, stroke);
            p.Line(cx - w, cy - h + w, cx, cy - h, c, stroke);
            p.Line(cx + w, cy - h + w, cx, cy - h, c, stroke);
        }
        else
        {
            _chevron[0] = new PointF(cx - w, cy - h);
            _chevron[1] = new PointF(cx + w, cy);
            _chevron[2] = new PointF(cx - w, cy + h);
            g.DrawLines(p.Pen(c, stroke), _chevron);
        }
        g.SmoothingMode = old;
    }

    private void Chevron(HudPainter p, float cx, float cy, float size, Color c, int direction)
    {
        float w = size * 0.5f, h = size * 0.32f;
        switch (direction)
        {
            case 0:
                _chevron[0] = new PointF(cx - w, cy + h);
                _chevron[1] = new PointF(cx, cy - h);
                _chevron[2] = new PointF(cx + w, cy + h);
                break;
            case 1:
                _chevron[0] = new PointF(cx - h, cy - w);
                _chevron[1] = new PointF(cx + h, cy);
                _chevron[2] = new PointF(cx - h, cy + w);
                break;
            default:
                _chevron[0] = new PointF(cx + h, cy - w);
                _chevron[1] = new PointF(cx - h, cy);
                _chevron[2] = new PointF(cx + h, cy + w);
                break;
        }
        var g = p.G;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(p.Pen(c, Math.Max(2, size * 0.16f)), _chevron);
        g.SmoothingMode = old;
    }

    private static readonly string[] BannerParts = { "side", "driver", "gap" };

    private void DrawBanner(HudPainter p, HudView v, OvertakeThreat? threat)
    {
        var f = v.Frame;
        float d = p.Density, h = BannerH * d, pad = 4 * d, gap = 4 * d;
        p.Need(p.Bounds.Width, h);
        p.Panel(new RectangleF(p.Bounds.X, p.Bounds.Y, p.Bounds.Width, h));
        var inner = new RectangleF(p.Bounds.X + pad, p.Bounds.Y + pad, p.Bounds.Width - pad * 2, h - pad * 2);
        if (threat is not { } t)
        {
            p.Text(WhenText(v), p.Small, p.P.Faint, inner, HAlign.Center, VAlign.Center);
            return;
        }
        var font = p.Font(14f, true);
        var alarm = t.Alongside ? p.P.Danger : p.P.Warn;
        var ink = HudPalette.On(alarm);
        string word = p.LabelText(SideWord(t.Side));
        float icon = 14 * d;
        float chipW = MathF.Ceiling(10 * d + icon + 6 * d + p.Measure(word, font) + 10 * d);
        string gapNum = GapText(t);
        var gapFont = p.Font(16f, true);
        float gapW = MathF.Ceiling(p.Measure("0.0", gapFont) + p.Measure(Units.S, p.SmallBold) + 22 * d);
        float minName = 70 * d;
        bool showWord = inner.Width >= chipW + gap + gapW + gap + minName;
        if (!showWord) chipW = MathF.Ceiling(icon + 20 * d);
        bool showName = inner.Width >= chipW + gap + gapW + gap + minName;
        if (inner.Width < chipW + gap + gapW)
        {
            p.Fill(inner, p.Solid(alarm, 235), p.CornerRadius(4));
            p.Number(gapNum, Units.S, gapFont, ink, inner, HAlign.Center, VAlign.Center);
            return;
        }
        Span<RectangleF> parts = stackalloc RectangleF[3];
        float between = showName ? gap : Math.Min(gap, (inner.Width - chipW - gapW) / 2);
        p.Arrange(v, "overtake.banner", inner, true, between, BannerParts, stackalloc float[] { chipW, 0, gapW }, parts);
        var chip = new RectangleF(parts[0].X, inner.Y, chipW, inner.Height);
        p.Fill(chip, p.Solid(alarm, 235), p.CornerRadius(4));
        if (showWord)
        {
            Arrow(p, chip.X + 10 * d + icon / 2, chip.Y + chip.Height / 2, icon, ink, !t.Alongside);
            p.Text(word, font, ink, new RectangleF(chip.X + 10 * d + icon + 6 * d, chip.Y, chip.Width - 10 * d - icon - 6 * d, chip.Height), HAlign.Left, VAlign.Center, false);
        }
        else Arrow(p, chip.X + chip.Width / 2, chip.Y + chip.Height / 2, icon, ink, !t.Alongside);
        var gapBox = new RectangleF(parts[2].X, inner.Y, gapW, inner.Height);
        p.Tile(gapBox);
        p.Number(gapNum, Units.S, gapFont, t.Alongside ? p.P.DangerText : p.P.Defend, gapBox, HAlign.Center, VAlign.Center);
        if (!showName) return;
        var nameBox = parts[1];
        p.Tile(nameBox);
        DriverLine(p, f, t.Car, nameBox.Inflated(-8 * d, 0), font, false);
    }

    private static float DriverLine(HudPainter p, TelemetryFrame f, int car, RectangleF box, Font font, bool badges)
    {
        float d = p.Density, x = box.X;
        f.Session.ByCar.TryGetValue(car, out var driver);
        if (driver is not null && f.Session.NumCarClasses > 1 && driver.ClassLabel.Length > 0)
        {
            var pillFont = p.Font(11f, true);
            float pw = MathF.Ceiling(p.Measure(driver.ClassLabel, pillFont) + 10 * d), ph = MathF.Ceiling(p.LineHeight(pillFont) + 2 * d);
            if (pw < box.Width * 0.4f)
            {
                p.Pill(new RectangleF(x, box.Y + (box.Height - ph) / 2, pw, ph), driver.ClassLabel, p.ClassColor(f.Session, driver.ClassId), pillFont);
                x += pw + 7 * d;
            }
        }
        float right = box.Right;
        if (badges && driver is not null)
        {
            float bw = 96 * d;
            if (right - x > bw + 80 * d)
            {
                HudTable.Badge(p, new RectangleF(right - bw, box.Y, bw, box.Height), driver.License, driver.IRating, "both", false);
                right -= bw + 6 * d;
            }
        }
        string number = CarNumber(f, car);
        float nw = p.Measure(number, font);
        p.Text(number, font, p.P.Heading, new RectangleF(x, box.Y, nw + 2, box.Height), HAlign.Left, VAlign.Center, false);
        x += nw + 6 * d;
        if (driver is not null && driver.Name.Length > 0 && right - x > 20 * d)
            p.Text(driver.Name, font, p.P.Text, new RectangleF(x, box.Y, right - x, box.Height), HAlign.Left, VAlign.Center);
        return x;
    }

    private void DrawEdge(HudPainter p, HudView v, OvertakeThreat? threat)
    {
        var f = v.Frame;
        float d = p.Density, w = EdgeW * d, h = v.Options.Int("length") * d;
        p.Need(w, h);
        bool right = v.Options.Choice("edge") != LeftEdge;
        var r = new RectangleF(p.Bounds.X, p.Bounds.Y, Math.Min(w, p.Bounds.Width), Math.Min(h, p.Bounds.Height));
        float barW = 12 * d;
        var bar = right ? new RectangleF(r.Right - barW, r.Y, barW, r.Height) : new RectangleF(r.X, r.Y, barW, r.Height);
        if (threat is not { } t)
        {
            p.Fill(bar, HudPalette.Alpha(p.P.Faint, 50));
            var note = new RectangleF(r.X + 4 * d, r.Y, r.Width - barW - 8 * d, r.Height);
            if (!right) note.X = bar.Right + 4 * d;
            p.Text("Overtake alert", p.Tiny, p.P.Faint, note, HAlign.Center, VAlign.Center);
            return;
        }
        bool mySide = t.Alongside && (t.Side == OvertakeSide.Both || (t.Side == OvertakeSide.Right) == right);
        if (t.Alongside && !mySide) return;
        var color = mySide ? p.P.Danger : p.P.Defend;
        int glow = mySide ? 255 : 110;
        for (int i = 0; i < 4; i++)
        {
            float gw = (6 + i * 6) * d;
            var halo = right ? new RectangleF(bar.X - gw, bar.Y, gw, bar.Height) : new RectangleF(bar.Right, bar.Y, gw, bar.Height);
            p.Fill(halo, HudPalette.Alpha(color, (mySide ? 34 : 16) - i * (mySide ? 7 : 3)));
        }
        p.Fill(bar, HudPalette.Alpha(color, glow));
        float cx = right ? bar.X - 18 * d : bar.Right + 18 * d, size = 20 * d, step = 30 * d;
        float top = r.Y + 18 * d;
        for (int i = 0; i < 3; i++)
        {
            int alpha = mySide ? 255 - i * 75 : 200 - i * 60;
            float cy = mySide ? top + i * step : r.Bottom - 18 * d - (i + 1) * step - 60 * d;
            if (cy < r.Y + 4 * d || cy > r.Bottom - 4 * d) continue;
            Chevron(p, cx, cy + size / 2, size, HudPalette.Alpha(color, alpha), mySide ? right ? 1 : 2 : 0);
        }
        float tileW = r.Width - barW - 10 * d, numH = p.LineHeight(p.SmallBold), valH = p.LineHeight(p.Bold);
        float tileH = numH + valH + 8 * d;
        var tile = new RectangleF(right ? r.X + 2 * d : bar.Right + 8 * d, r.Bottom - tileH - 8 * d, tileW, tileH);
        if (tile.Y < r.Y + 3 * step) return;
        p.Fill(tile, p.Solid(mySide ? p.P.Danger : p.PanelColor, 235), p.CornerRadius(4));
        var ink = mySide ? HudPalette.On(p.P.Danger) : p.P.Heading;
        p.Text(CarNumber(f, t.Car), p.SmallBold, ink, new RectangleF(tile.X, tile.Y + 4 * d, tile.Width, numH), HAlign.Center, VAlign.Center, false);
        var valBox = new RectangleF(tile.X, tile.Y + 4 * d + numH, tile.Width, valH);
        if (mySide) p.Text(p.LabelText(t.Side == OvertakeSide.Both ? "Both" : right ? "Right" : "Left"), p.SmallBold, ink, valBox, HAlign.Center, VAlign.Center, false);
        else p.Number(GapText(t), Units.S, p.Bold, p.P.Defend, valBox, HAlign.Center, VAlign.Center);
    }

    private static readonly string[] Tiles = { "Behind", "Gaining per lap", "Likely at" };
    private static readonly string[] CardParts = { "driver", "stats", "trend", "sectors" };
    private static readonly string[] CardTileParts = { "behind", "gain", "at" };

    private void DrawCard(HudPainter p, HudView v, OvertakeThreat? threat)
    {
        var f = v.Frame;
        float d = p.Density;
        float nameH = 32 * d, tileH = p.BoxedStatHeight(p.Big), sparkH = 62 * d, footH = p.LineHeight(p.SmallBold) + 8 * d, gap = 6 * d;
        float body = nameH + gap + tileH + gap + sparkH + gap + footH;
        p.NeedWidth(260 * d);
        p.NeedBelow(p.HeaderBody(), body);
        string? stake = null;
        if (threat is { } st && Overtake.AtStake(f, st.Car) is var (from, to)) stake = PooledText.Number("P", from, " → P", to);
        var area = p.Header("Catching you", stake, p.P.Defend, body);
        if (threat is not { } t)
        {
            p.Text(WhenText(v), p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        int me = f.PlayerCarIdx;
        double? lap = FieldOrder.LapTimeOf(f, me);
        Span<RectangleF> parts = stackalloc RectangleF[4];
        p.Arrange(v, "overtake.card", new RectangleF(area.X, area.Y, area.Width, Math.Max(area.Height, body)), false, gap, CardParts, stackalloc float[] { nameH, tileH, sparkH, footH }, parts);
        var nameRow = new RectangleF(area.X, parts[0].Y, area.Width, nameH);
        p.Fill(nameRow, p.Solid(p.Raised(p.P.Dark ? 0.08 : 0.05)), p.CornerRadius(4));
        DriverLine(p, f, t.Car, nameRow.Inflated(-8 * d, 0), p.Font(15f, true), true);

        string behind = t.Alongside ? p.LabelText(SideWord(t.Side)) : PooledText.Number("", t.Behind, "0.0", Inv, " " + Units.S);
        string gain = t.GainPerLap(lap) is { } g ? PooledText.Number("", g, "0.0", Inv, " " + Units.S) : RaceFormat.Missing;
        string at = CatchAt(v, t, lap);
        Span<RectangleF> tiles = stackalloc RectangleF[3];
        p.Arrange(v, "overtake.card.tiles", new RectangleF(area.X, parts[1].Y, area.Width, tileH), true, gap, CardTileParts, stackalloc float[] { 0, 0, 0 }, tiles);
        p.BoxedStat(tiles[0], Tiles[0], behind, t.Alongside ? p.P.DangerText : p.P.Defend, p.Big);
        p.BoxedStat(tiles[1], Tiles[1], gain, p.P.Heading, p.Big);
        p.BoxedStat(tiles[2], Tiles[2], at, p.P.Heading, p.Big);

        FillDuel(v, t.Car);
        var spark = new RectangleF(area.X, parts[2].Y, area.Width, sparkH);
        Spark(p, spark);

        var foot = new RectangleF(area.X, parts[3].Y, area.Width, footH);
        if (foot.Y > area.Y) p.Line(foot.X, MathF.Round(foot.Y) + 0.5f, foot.Right, MathF.Round(foot.Y) + 0.5f, p.P.Line, 1);
        var text = new RectangleF(foot.X, foot.Y + 4 * d, foot.Width, footH - 4 * d);
        int best = -1;
        double most = 0;
        if (_duel.HasLastSectors)
            for (int i = 0; i < _duel.Sectors; i++)
                if (_duel.LastSector[i] > most) { most = _duel.LastSector[i]; best = i; }
        if (best >= 0) p.Text(PooledText.Concat(PooledText.Number("", most, "0.00", Inv, " s faster than you in S"), PooledText.Number("", best + 1)), p.SmallBold, p.P.WarnText, text, HAlign.Left, VAlign.Center);
        else p.Text("Not faster than you in any sector last lap", p.Small, p.P.Muted, text, HAlign.Left, VAlign.Center);
    }

    private static string CatchAt(HudView v, OvertakeThreat t, double? lap)
    {
        if (t.Alongside) return "Now";
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        if (t.TimeToCatch is not { } seconds || lap is not { } l || seconds > l) return RaceFormat.Missing;
        double est = f.HasCarEstTime ? f.CarEstTime[me] : 0;
        return Overtake.CatchPct(f.CarLapDistPct[me], est, l, seconds, v.Race.Profile) is { } pct ? v.Race.Turns.Where(pct, true) : RaceFormat.Missing;
    }

    private void FillDuel(HudView v, int car)
    {
        var f = v.Frame;
        if (f.Sequence == _duelSeq && car == _duelCar) return;
        _duelSeq = f.Sequence;
        _duelCar = car;
        v.Race.Duels.Fill(_duel, f.PlayerCarIdx, car, f.SessionKind == SessionKind.Race, f.SessionTime ?? 0, (uint)car < TelemetryFrame.MaxCars && f.CarOnPitRoad[car]);
    }

    private void Spark(HudPainter p, RectangleF r)
    {
        float d = p.Density;
        p.Tile(r);
        int n = _duel.HistoryCount;
        var labelBox = new RectangleF(r.X + 8 * d, r.Y + 3 * d, r.Width - 16 * d, p.LineHeight(p.Label));
        p.Text(p.LabelText(n >= 2 ? "Gap at the line" : "Gap at the line shows after a lap"), p.Label, p.LabelColor, labelBox, HAlign.Left, VAlign.Center);
        if (n < 2) return;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double gv = Math.Abs(_duel.HistoryGap[i]);
            lo = Math.Min(lo, gv);
            hi = Math.Max(hi, gv);
        }
        if (hi - lo < 0.2) { hi = lo + 0.2; }
        var tagFont = p.Tiny;
        string first = PooledText.Number("", Math.Abs(_duel.HistoryGap[0]), "0.0", Inv, " " + Units.S);
        float labelW = MathF.Ceiling(Math.Max(p.Measure(first, tagFont), p.Measure("00.0 S", tagFont)) + 6 * d);
        var plot = RectangleF.FromLTRB(r.X + labelW + 6 * d, labelBox.Bottom + 6 * d, r.Right - labelW - 6 * d, r.Bottom - 8 * d);
        if (plot.Width < 20 * d || plot.Height < 8 * d) return;
        double x0 = _duel.HistoryX[0], x1 = _duel.HistoryX[n - 1];
        double span = Math.Max(1e-6, x1 - x0);
        for (int i = 0; i < n; i++)
        {
            double gv = Math.Abs(_duel.HistoryGap[i]);
            _spark[i] = new PointF(plot.X + (float)((_duel.HistoryX[i] - x0) / span) * plot.Width, plot.Bottom - (float)((gv - lo) / (hi - lo)) * plot.Height);
        }
        var g = p.G;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pen = p.Pen(p.P.Defend, 2.5f * d);
        for (int i = 1; i < n; i++) g.DrawLine(pen, _spark[i - 1], _spark[i]);
        g.SmoothingMode = old;
        var last = _spark[n - 1];
        p.Dot(last.X, last.Y, 4 * d, p.P.Defend);
        float th = p.LineHeight(tagFont);
        p.Text(first, tagFont, p.P.Muted, new RectangleF(r.X + 4 * d, _spark[0].Y - th / 2, labelW, th), HAlign.Right, VAlign.Center, false);
        string now = PooledText.Number("", Math.Abs(_duel.HistoryGap[n - 1]), "0.0", Inv, " " + Units.S);
        p.Text(now, tagFont, p.P.Defend, new RectangleF(last.X + 6 * d, last.Y - th / 2, labelW, th), HAlign.Left, VAlign.Center, false);
    }
}
