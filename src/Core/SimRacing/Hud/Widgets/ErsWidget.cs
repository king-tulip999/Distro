using System.Drawing;
using System.Globalization;
using Core.SimRacing.Race;
using Units = Core.Services.CapsUnits;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class ErsWidget : HudWidget
{
    public const string Battery = "battery", PushToPass = "p2p", Ring = "ring";
    public const double W = 320, H = 210;
    private const double Idle = 2000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Battery,
            new HudChoice(Battery, "Battery", "Charge, deploy and regen this lap, mode and recent laps"),
            new HudChoice(PushToPass, "Push to pass", "What's left, ready / active and whether it lasts"),
            new HudChoice(Ring, "Ring", "A compact ring with deploy or regen, boost and mode")),
        TitleRow,
    };

    private struct Energy
    {
        public double? Battery, Power, Mode, Deploy, Regen, Budget, Count, First, PerUse, LapsLeft, LapsToGo, Manifold, ManifoldMax, Net;
        public bool? Active, Drs;
        public int Uses, Starts;
        public bool CountsDown;
        public int? Lap;

        public readonly bool Hybrid => Battery is not null || Power is not null;
        public readonly bool P2P => Count is not null || Active is not null;
        public readonly bool Boost => Manifold is not null && ManifoldMax is > ErsLog.BoostSeen;
    }

    private readonly double[] _starts = new double[ErsLog.LapStarts];

    public override bool HasContent(HudView v) => ErsLog.Reports(v.Frame, v.Race.Ers);

    private Energy Read(HudPainter p, HudView v, out bool sample)
    {
        var f = v.Frame;
        var log = v.Race.Ers;
        sample = !ErsLog.Reports(f, log) && (p.IsPreview || f.Simulated);
        if (sample) return Sample(f.Lap);
        var e = new Energy
        {
            Battery = ErsLog.Battery(f),
            Power = f.MguKPower is { } w && double.IsFinite(w) ? w : null,
            Mode = f.DeployMode,
            Deploy = log.DeployThisLap(f),
            Regen = f.MguKPower is not null ? log.RegenMJ : null,
            Budget = ErsLog.Budget(f),
            Count = log.HasP2P ? f.PushToPassCount : null,
            First = log.P2PFirst,
            Active = log.HasP2P ? f.PushToPassActive : null,
            Uses = log.Uses,
            PerUse = log.SecondsPerUse,
            LapsLeft = log.P2PLapsLeft(f),
            LapsToGo = log.HasP2P ? v.Race.LapsToGo(f) : null,
            CountsDown = log.P2PCountsDown,
            Drs = log.HasDrs ? f.DrsActive ?? false : null,
            Manifold = log.Manifold,
            ManifoldMax = log.ManifoldMax,
            Net = log.NetPerLap,
            Starts = log.StartCount,
            Lap = f.Lap,
        };
        for (int i = 0; i < log.StartCount; i++) _starts[i] = log.Start(i);
        return e;
    }

    private Energy Sample(int? lap)
    {
        ReadOnlySpan<double> starts = stackalloc double[] { 0.86, 0.74, 0.79, 0.66, 0.6, 0.55 };
        starts.CopyTo(_starts);
        return new Energy
        {
            Battery = 0.64, Power = 48000, Mode = 3, Deploy = 2.1, Regen = 0.9, Count = 112, First = 150, Active = false, Uses = 6, PerUse = 11,
            LapsLeft = 8.2, LapsToGo = 12, CountsDown = true, Manifold = 1.42, ManifoldMax = 2, Net = -0.062, Starts = 6, Lap = lap ?? 14,
        };
    }

    public override void Draw(HudPainter p, HudView v)
    {
        string style = v.Options.Choice("style");
        string title = style switch { PushToPass => "Push to pass", Ring => "Energy", _ => "Hybrid" };
        if (DrawWaiting(p, v, title)) return;
        var e = Read(p, v, out _);
        if (!e.Hybrid && !e.P2P && !e.Boost && e.Drs is null && e.Mode is null)
        {
            var area = p.Header(title);
            p.Text("Shows for cars with hybrid, push to pass or boost", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        if (style == PushToPass && !e.P2P) style = e.Hybrid ? Battery : Ring;
        if (style == Battery && !e.Hybrid) style = e.P2P ? PushToPass : Ring;
        if (p.Bounds.Height < 110 * p.Density)
        {
            Compact(p, e, style);
            return;
        }
        switch (style)
        {
            case PushToPass: DrawP2P(p, v, e); break;
            case Ring: DrawRing(p, v, e); break;
            default: DrawBattery(p, v, e); break;
        }
    }

    private static void Fit(HudView v, string group, string[] ids, float start, float end, float gap, Span<float> sizes, int always, bool stop)
    {
        var order = v.Options.Arranged(group, ids);
        float at = start;
        bool cut = false;
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            if (sizes[i] < 0) continue;
            if (i != always && (cut || at + sizes[i] > end + 1))
            {
                sizes[i] = -1;
                cut |= stop;
                continue;
            }
            at += sizes[i] + gap;
        }
    }

    private static void Compact(HudPainter p, in Energy e, string style)
    {
        float d = p.Density;
        p.Panel();
        var r = p.Bounds.Inflated(-10 * d, -4 * d);
        string value, label;
        Color color = p.P.Heading;
        if ((style == PushToPass || e.Battery is null) && e.P2P)
        {
            value = e.Count is { } c ? PooledText.Number("", (long)Math.Round(c)) : RaceFormat.Missing;
            label = e.Active == true ? "P2P active" : e.CountsDown ? "P2P left" : "P2P used";
            if (e.Active == true) color = p.P.Attack;
        }
        else if (e.Battery is { } b)
        {
            value = Pct(b);
            label = State(p, e)?.Text ?? "Battery";
            color = State(p, e)?.Color ?? p.P.Heading;
        }
        else if (e.Boost)
        {
            value = PooledText.Number("", e.Manifold!.Value, "0.00", Inv);
            label = "bar";
        }
        else
        {
            value = e.Mode is { } m ? ControlLog.Format(m) : e.Drs == true ? "Open" : RaceFormat.Missing;
            label = e.Mode is not null ? "Deploy mode" : "DRS";
        }
        float px = Math.Min(30f, r.Height * 0.62f / d);
        var size = p.DisplaySize(value, px);
        float lw = p.Measure(label, p.Small) + 2, total = size.Width + 8 * d + lw;
        float x = r.X + Math.Max(0, (r.Width - total) / 2);
        p.Display(value, px, p.P.Heading, new RectangleF(x, r.Y, size.Width + 2, r.Height), HAlign.Left, VAlign.Center);
        x += size.Width + 8 * d;
        p.Text(label, p.Small, color == p.P.Heading ? p.P.Muted : color, new RectangleF(x, r.Y, Math.Max(0, r.Right - x), r.Height), HAlign.Left, VAlign.Center);
    }

    private static (string Text, Color Color)? State(HudPainter p, in Energy e) => e.Power switch
    {
        > Idle => ("Deploying", p.P.Attack),
        < -Idle => ("Regen", p.P.GoodText),
        not null => ("Idle", p.P.Muted),
        _ => null,
    };

    private static string Pct(double? x) => x is { } b ? PooledText.Number("", (long)Math.Round(b * 100), "%") : RaceFormat.Missing;

    private static string MJ(double? x, string format = "0.0") => x is { } m && double.IsFinite(m) ? PooledText.Number("", m, format, Inv) : RaceFormat.Missing;

    private static Color ChargeColor(HudPainter p, double b) => b < 0.15 ? p.P.Danger : b < 0.3 ? p.P.Warn : p.P.Good;

    private static readonly string[] BatteryParts = { "charge", "stats" };
    private static readonly string[] BatteryStatParts = { "deploy", "pair", "chart" };
    private static readonly string[] BatteryPairParts = { "regen", "mode" };

    private void DrawBattery(HudPainter p, HudView v, in Energy e)
    {
        float d = p.Density;
        var state = State(p, e);
        var area = p.Header("Hybrid", state?.Text, state?.Color);
        float gap = 5 * d, cellW = 78 * d;
        var stat = p.Font(17f, true);
        float deployH = 8 * d + p.LineHeight(p.Label) + p.LineHeight(stat) + 10 * d;
        float pairH = p.BoxedStatHeight(stat) + 2 * d;
        float chartH = 8 * d + p.LineHeight(p.Label) + 34 * d;
        bool hasPair = e.Power is not null || e.Mode is not null;
        bool hasChart = e.Battery is not null && e.Starts > 0;
        float colH = deployH + (hasPair ? gap + pairH : 0) + (hasChart ? gap + chartH : 0);
        p.NeedBelow(area, Math.Max(colH, e.Battery is not null ? 150 * d : 0));
        Span<RectangleF> sides = stackalloc RectangleF[2];
        p.Arrange(v, "ers.battery", area, true, gap, BatteryParts, stackalloc float[] { e.Battery is not null ? cellW : -1, 0 }, sides);
        var col = sides[1];
        if (e.Battery is { } b)
        {
            var cell = new RectangleF(sides[0].X, area.Y, cellW, Math.Max(colH, 150 * d));
            cell.Height = Math.Min(cell.Height, area.Height);
            p.Tile(cell);
            BatteryIcon(p, cell.Inflated(-6 * d, -6 * d), b);
            p.NeedWidth(area.X + cellW + gap + 150 * d);
        }
        Span<float> stack = stackalloc float[] { e.Power is not null || e.Deploy is not null ? deployH : -1, hasPair ? pairH : -1, hasChart ? chartH : -1 };
        Fit(v, "ers.battery.stats", BatteryStatParts, col.Y, col.Bottom, gap, stack, -1, false);
        Span<RectangleF> rows = stackalloc RectangleF[3];
        p.Arrange(v, "ers.battery.stats", col, false, gap, BatteryStatParts, stack, rows);
        if (stack[0] >= 0)
        {
            var r = new RectangleF(col.X, rows[0].Y, col.Width, deployH);
            p.Tile(r);
            var inner = r.Inflated(-8 * d, -5 * d);
            float lh = p.LineHeight(p.Label);
            p.Text(p.LabelText("Deploy this lap"), p.Label, p.P.Muted, new RectangleF(inner.X, inner.Y, inner.Width, lh), HAlign.Left, VAlign.Top);
            float vy = inner.Y + lh;
            string value = MJ(e.Deploy);
            p.Text(value, stat, p.P.Heading, new RectangleF(inner.X, vy, inner.Width * 0.5f, p.LineHeight(stat)), HAlign.Left, VAlign.Center, false);
            string of = e.Budget is { } budget ? PooledText.Number("of ", budget, "0.0", Inv, " MJ") : "MJ";
            p.Text(of, p.Small, p.P.Muted, new RectangleF(inner.X + inner.Width * 0.4f, vy, inner.Width * 0.6f, p.LineHeight(stat)), HAlign.Right, VAlign.Center);
            if (e.Budget is { } all && e.Deploy is { } used) p.Bar(new RectangleF(inner.X, r.Bottom - 7 * d, inner.Width, 4 * d), used / all, p.P.Attack);
        }
        if (stack[1] >= 0)
        {
            Span<RectangleF> pair = stackalloc RectangleF[2];
            p.Arrange(v, "ers.battery.pair", new RectangleF(col.X, rows[1].Y, col.Width, pairH), true, gap, BatteryPairParts, stackalloc float[] { e.Power is not null ? 0 : -1, e.Mode is not null ? 0 : -1 }, pair);
            if (e.Power is not null) p.BoxedStat(pair[0], "Regen MJ", e.Regen is { } rg ? PooledText.Concat("+", MJ(rg)) : RaceFormat.Missing, p.P.GoodText, stat);
            if (e.Mode is { } mode) p.BoxedStat(pair[1], "Deploy mode", ControlLog.Format(mode), p.P.Heading, stat);
        }
        if (stack[2] >= 0) StartChart(p, new RectangleF(col.X, rows[2].Y, col.Width, chartH), e);
    }

    private static void BatteryIcon(HudPainter p, RectangleF r, double charge)
    {
        float d = p.Density;
        var pctFont = p.Font(18f, true);
        float textH = p.LineHeight(pctFont);
        float bodyW = Math.Min(50 * d, r.Width), capH = 5 * d;
        float bodyH = r.Height - textH - capH - 4 * d;
        if (bodyH < 30 * d) { p.Text(Pct(charge), pctFont, p.P.Heading, r, HAlign.Center, VAlign.Center, false); return; }
        float bx = r.X + (r.Width - bodyW) / 2;
        p.Fill(new RectangleF(bx + bodyW / 2 - 8 * d, r.Y, 16 * d, capH), p.P.LineStrong, p.CornerRadius(3));
        var body = new RectangleF(bx, r.Y + capH, bodyW, bodyH);
        p.Outline(body, p.P.LineStrong, 2 * d, p.Look.Corner > 0 ? 5 * d : 0);
        var inside = body.Inflated(-4 * d, -4 * d);
        const int cells = 5;
        float cg = 2 * d, ch = (inside.Height - cg * (cells - 1)) / cells;
        var color = ChargeColor(p, charge);
        for (int i = 0; i < cells; i++)
        {
            double fill = Math.Clamp(charge * cells - i, 0, 1);
            if (fill <= 0.02) break;
            float cy = inside.Bottom - (i + 1) * ch - i * cg;
            p.Fill(new RectangleF(inside.X, cy, inside.Width, ch), fill >= 0.98 ? color : HudPalette.Alpha(color, (int)(90 + 120 * fill)), p.CornerRadius(3));
        }
        p.Text(Pct(charge), pctFont, p.P.Heading, new RectangleF(r.X, body.Bottom + 4 * d, r.Width, textH), HAlign.Center, VAlign.Top, false);
    }

    private void StartChart(HudPainter p, RectangleF r, in Energy e)
    {
        float d = p.Density;
        p.Tile(r);
        var inner = r.Inflated(-8 * d, -5 * d);
        float lh = p.LineHeight(p.Label);
        p.Text(p.LabelText("Charge at lap starts"), p.Label, p.P.Muted, new RectangleF(inner.X, inner.Y, inner.Width, lh), HAlign.Left, VAlign.Top);
        var bars = RectangleF.FromLTRB(inner.X, inner.Y + lh + 3 * d, inner.Right, inner.Bottom);
        int n = e.Starts + 1;
        float gap = 3 * d, bw = (bars.Width - gap * (n - 1)) / n;
        double lo = 1, hi = 0;
        for (int i = 0; i < n; i++)
        {
            double value = Math.Clamp(i < e.Starts ? _starts[i] : e.Battery ?? 0, 0, 1);
            lo = Math.Min(lo, value);
            hi = Math.Max(hi, value);
        }
        lo = Math.Max(0, lo - 0.1);
        hi = Math.Max(hi, lo + 0.2);
        for (int i = 0; i < n; i++)
        {
            double value = i < e.Starts ? _starts[i] : e.Battery ?? 0;
            float h = Math.Max(2 * d, (float)((Math.Clamp(value, 0, 1) - lo) / (hi - lo)) * bars.Height);
            bool now = i == n - 1;
            p.Fill(new RectangleF(bars.X + i * (bw + gap), bars.Bottom - h, bw, h), now ? ChargeColor(p, value) : HudPalette.Alpha(p.P.Muted, 150), p.CornerRadius(3));
        }
    }

    private static readonly string[] P2PParts = { "count", "stats", "lasting" };
    private static readonly string[] P2PCountParts = { "number", "cells" };
    private static readonly string[] P2PStatParts = { "uses", "peruse", "lapsleft" };

    private static void DrawP2P(HudPainter p, HudView v, in Energy e)
    {
        float d = p.Density;
        bool empty = e.Count is <= 0 && e.CountsDown;
        var (chip, chipColor) = e.Active == true ? ("Active", p.P.Attack) : empty ? ("Empty", p.P.DangerText) : ("Ready", p.P.GoodText);
        var area = p.Header("Push to pass", chip, chipColor);
        const float px = 46f;
        var bigH = p.DisplaySize("0", px).Height;
        var stat = p.Font(17f, true);
        float tileH = p.BoxedStatHeight(stat), footH = p.LineHeight(p.SmallBold) + 8 * d, gap = 5 * d;
        float topH = 8 * d + bigH + 10 * d + 8 * d + 8 * d;
        bool lasting = e.LapsToGo is not null && e.CountsDown;
        p.NeedBelow(area, topH + gap + tileH + (lasting ? gap + footH : 0));
        Span<float> stack = stackalloc float[] { topH, tileH, lasting ? footH : -1 };
        Fit(v, "ers.p2p", P2PParts, area.Y, area.Bottom, gap, stack, 0, true);
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "ers.p2p", area, false, gap, P2PParts, stack, parts);
        var top = new RectangleF(area.X, parts[0].Y, area.Width, topH);
        p.Tile(top);
        var inner = top.Inflated(-10 * d, -8 * d);
        Span<RectangleF> rows = stackalloc RectangleF[2];
        p.Arrange(v, "ers.p2p.count", inner, false, 10 * d, P2PCountParts, stackalloc float[] { bigH, 8 * d }, rows);
        float headY = rows[0].Y;
        string count = e.Count is { } c ? PooledText.Number("", (long)Math.Round(c)) : RaceFormat.Missing;
        var size = p.DisplaySize(count, px);
        p.Display(count, px, empty ? p.P.DangerText : p.P.Heading, new RectangleF(inner.X, headY, size.Width + 2, bigH), HAlign.Left, VAlign.Top);
        var unit = p.Font(16f, false);
        p.Text(e.CountsDown ? "left" : "used", unit, p.P.Muted, new RectangleF(inner.X + size.Width + 8 * d, headY + bigH - p.LineHeight(unit), 80 * d, p.LineHeight(unit)), HAlign.Left, VAlign.Bottom, false);
        if (e.Drs is { } drs)
        {
            string text = drs ? "DRS open" : "DRS";
            float w = p.Measure(text, p.SmallBold) + 14 * d;
            p.Pill(new RectangleF(inner.Right - w, headY + (bigH - 20 * d) / 2, w, 20 * d), text, drs ? p.P.Attack : p.Solid(p.Raised(0.12)), p.SmallBold, drs ? null : p.P.Muted);
        }
        var cellsBox = new RectangleF(inner.X, rows[1].Y, inner.Width, 8 * d);
        const int cells = 10;
        float cg = 3 * d, cw = (cellsBox.Width - cg * (cells - 1)) / cells;
        double share = e.Count is { } now && e.First is > 0 and var first && e.CountsDown ? Math.Clamp(now / first, 0, 1) : e.Count is not null ? 1 : 0;
        for (int i = 0; i < cells; i++)
        {
            bool on = i < Math.Round(share * cells);
            p.Fill(new RectangleF(cellsBox.X + i * (cw + cg), cellsBox.Y, cw, cellsBox.Height), on ? p.P.Attack : p.Solid(p.Sunken(0.35), 230), p.CornerRadius(2));
        }
        if (stack[1] >= 0)
        {
            Span<RectangleF> tiles = stackalloc RectangleF[3];
            p.Arrange(v, "ers.p2p.stats", new RectangleF(area.X, parts[1].Y, area.Width, tileH), true, gap, P2PStatParts, stackalloc float[] { 0, 0, 0 }, tiles);
            p.BoxedStat(tiles[0], "Uses", PooledText.Number("", e.Uses), p.P.Heading, stat);
            p.BoxedStat(tiles[1], "Per use", e.PerUse is { } s ? PooledText.Number("", s, "0", Inv, " " + Units.S) : RaceFormat.Missing, p.P.Heading, stat);
            string left = e.LapsLeft is { } l && double.IsFinite(l) ? RaceFormat.About(PooledText.Number("", l, "0", Inv)) : RaceFormat.Missing;
            p.BoxedStat(tiles[2], "Laps left", left, e.LapsLeft is { } ll && e.LapsToGo is { } go && ll < go ? p.P.WarnText : p.P.Heading, stat);
        }
        if (stack[2] < 0) return;
        var foot = new RectangleF(area.X, parts[2].Y, area.Width, footH);
        string toGo = PooledText.Number("", e.LapsToGo!.Value, "0", Inv, " to go");
        if (e.LapsLeft is { } at)
        {
            bool short_ = double.IsFinite(at) && at < e.LapsToGo!.Value;
            p.Soft(foot, short_ ? p.P.Warn : p.P.Good, 70);
            Line(p, foot, short_ ? PooledText.Number("Lasts ", at, "0", Inv, " laps at this rate") : "Lasts to the flag at this rate", toGo, short_ ? p.P.WarnText : p.P.GoodText);
        }
        else Line(p, foot, "Use rate after a lap", toGo, p.P.Faint);
    }

    private static void Line(HudPainter p, RectangleF r, string left, string right, Color leftColor)
    {
        float d = p.Density;
        var inner = r.Inflated(-8 * d, 0);
        float rw = p.Measure(right, p.SmallBold) + 2;
        p.Text(right, p.SmallBold, p.P.Heading, new RectangleF(inner.Right - rw, inner.Y, rw, inner.Height), HAlign.Right, VAlign.Center, false);
        p.Text(left, p.Small, leftColor, new RectangleF(inner.X, inner.Y, Math.Max(0, inner.Width - rw - 8 * d), inner.Height), HAlign.Left, VAlign.Center);
    }

    private static readonly string[] RingParts = { "body", "trend" };
    private static readonly string[] RingBodyParts = { "ring", "tiles" };
    private static readonly string[] RingTileParts = { "boost", "mode", "p2p" };

    private static void DrawRing(HudPainter p, HudView v, in Energy e)
    {
        float d = p.Density;
        var area = p.Header("Energy", e.Lap is { } lap ? PooledText.Number("Lap ", lap) : null);
        var stat = p.Font(17f, true);
        float gap = 5 * d, ring = 120 * d, footH = p.LineHeight(p.SmallBold) + 8 * d;
        float tileH = 8 * d + p.LineHeight(p.Label) + p.LineHeight(stat) + 10 * d;
        int tiles = (e.Boost ? 1 : 0) + (e.Mode is not null ? 1 : 0) + (e.P2P ? 1 : 0);
        bool foot = e.Battery is not null && e.Net is { } net && Math.Abs(net) >= 0.005 && area.Height >= 80 * d + gap + footH;
        float colH = Math.Max(tiles * tileH + Math.Max(0, tiles - 1) * gap, ring);
        p.NeedBelow(area, colH + (foot ? gap + footH : 0));
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "ers.ring", area, false, gap, RingParts, stackalloc float[] { 0, foot ? footH : -1 }, parts);
        var body = parts[0];
        ring = Math.Min(ring, body.Height);
        bool side = tiles > 0 && area.Width - ring - gap >= 110 * d;
        Span<RectangleF> halves = stackalloc RectangleF[2];
        if (side) p.Arrange(v, "ers.ring.body", body, true, gap, RingBodyParts, stackalloc float[] { Math.Max(ring, float.Epsilon), 0 }, halves);
        var box = side ? new RectangleF(halves[0].X, body.Y, ring, ring) : new RectangleF(area.X + (area.Width - ring) / 2, body.Y, ring, ring);
        p.Tile(box);
        RingFace(p, box.Inflated(-6 * d, -6 * d), e);
        if (side)
        {
            var col = halves[1];
            tiles = Math.Min(tiles, (int)((col.Height + gap) / (tileH * 0.8f + gap)));
            float th = tiles > 0 ? Math.Min(tileH, (col.Height - gap * (tiles - 1)) / tiles) : 0;
            Span<float> sizes = stackalloc float[] { e.Boost ? th : -1, e.Mode is not null ? th : -1, e.P2P ? th : -1 };
            var order = v.Options.Arranged("ers.ring.tiles", RingTileParts);
            for (int k = 0; k < order.Count; k++)
                if (sizes[order[k]] >= 0 && tiles-- <= 0) sizes[order[k]] = -1;
            Span<RectangleF> cells = stackalloc RectangleF[3];
            p.Arrange(v, "ers.ring.tiles", col, false, gap, RingTileParts, sizes, cells);
            if (sizes[0] >= 0)
            {
                var r = new RectangleF(col.X, cells[0].Y, col.Width, th);
                Labeled(p, r, "Manifold pressure", PooledText.Number("", e.Manifold!.Value, "0.00", Inv, " bar"), stat);
                p.Bar(new RectangleF(r.X + 8 * d, r.Bottom - 7 * d, r.Width - 16 * d, 4 * d), e.Manifold.Value / e.ManifoldMax!.Value, p.P.Warn);
            }
            if (sizes[1] >= 0 && e.Mode is { } mode)
                Labeled(p, new RectangleF(col.X, cells[1].Y, col.Width, th), "Deploy mode", ControlLog.Format(mode), stat);
            if (sizes[2] >= 0)
            {
                string count = e.Count is { } c ? PooledText.Number("", (long)Math.Round(c), e.CountsDown ? " left" : " used") : RaceFormat.Missing;
                Labeled(p, new RectangleF(col.X, cells[2].Y, col.Width, th), e.Active == true ? "Push to pass · active" : "Push to pass", count, stat, e.Active == true ? p.P.Attack : null);
            }
        }
        if (!foot) return;
        var fr = new RectangleF(area.X, parts[1].Y, area.Width, footH);
        double n = e.Net!.Value;
        if (n < 0)
        {
            double laps = e.Battery!.Value / -n;
            Line(p, fr, "Empty in", PooledText.Concat(RaceFormat.About(PooledText.Number("", laps, "0.0", Inv, " laps")), " at this rate"), p.P.Muted);
        }
        else Line(p, fr, "Charging each lap", PooledText.Number("+", n * 100, "0", Inv, "%"), p.P.Muted);
    }

    private static void Labeled(HudPainter p, RectangleF r, string label, string value, Font font, Color? color = null)
    {
        float d = p.Density;
        p.Tile(r);
        var inner = r.Inflated(-8 * d, -4 * d);
        float lh = p.LineHeight(p.Label);
        p.Text(p.LabelText(label), p.Label, p.P.Muted, new RectangleF(inner.X, inner.Y, inner.Width, lh), HAlign.Left, VAlign.Top);
        p.Text(value, font, color ?? p.P.Heading, new RectangleF(inner.X, inner.Y + lh, inner.Width, Math.Min(p.LineHeight(font), inner.Height - lh)), HAlign.Left, VAlign.Top);
    }

    private static void RingFace(HudPainter p, RectangleF box, in Energy e)
    {
        float d = p.Density;
        float size = Math.Min(box.Width, box.Height), thick = Math.Max(4 * d, size * 0.09f);
        if (size < 24 * d) return;
        var r = new RectangleF(box.X + (box.Width - size) / 2 + thick / 2, box.Y + (box.Height - size) / 2 + thick / 2, size - thick, size - thick);
        p.G.DrawEllipse(p.Pen(p.Solid(p.Sunken(0.35), 230), thick), r);
        string big;
        if (e.Battery is { } b)
        {
            if (b > 0.002) p.G.DrawArc(p.Pen(ChargeColor(p, b), thick), r, -90, (float)(360 * b));
            big = Pct(b);
        }
        else if (e.Boost)
        {
            p.G.DrawArc(p.Pen(p.P.Warn, thick), r, -90, (float)(360 * Math.Clamp(e.Manifold!.Value / e.ManifoldMax!.Value, 0, 1)));
            big = PooledText.Number("", e.Manifold.Value, "0.00", Inv);
        }
        else big = e.Count is { } c ? PooledText.Number("", (long)Math.Round(c)) : RaceFormat.Missing;
        float px = Math.Min(24f, size * 0.2f / d);
        var bs = p.DisplaySize(big, px);
        var state = State(p, e);
        var tiny = p.Font(10f, true);
        float th = state is not null ? p.LineHeight(tiny) + 2 * d : 0;
        float top = r.Y + (r.Height - bs.Height - th) / 2;
        p.Display(big, px, p.P.Heading, new RectangleF(r.X, top, r.Width, bs.Height), HAlign.Center, VAlign.Top);
        if (state is { } s && size >= 70 * d)
            p.Text(p.LabelText(s.Text), tiny, s.Color, new RectangleF(r.X, top + bs.Height + 4 * d, r.Width, p.LineHeight(tiny)), HAlign.Center, VAlign.Top, false);
    }
}
