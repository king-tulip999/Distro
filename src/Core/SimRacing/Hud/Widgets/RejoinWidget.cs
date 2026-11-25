using System.Drawing;
using System.Globalization;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class RejoinWidget : HudWidget
{
    public const string Go = "go", Line = "line", Countdown = "countdown";
    public const double W = 352, H = 172;
    private const int MaxRows = 12;
    private const double Lookahead = 30, Sample = 0.64;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string SecondsUnit = " " + Units.S;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Countdown,
            new HudChoice(Go, "Wait / go", "One big word, red or green"),
            new HudChoice(Line, "Traffic line", "The cars coming as a line with the next safe gap boxed"),
            new HudChoice(Countdown, "Countdown", "Counts down to the next safe gap with the next cars")),
        TitleRow,
        HudOption.Number("gap", "Gap needed", 3, 1, 10, 0.5, Units.S, "How much room to the next car counts as safe"),
        HudOption.Number("slow", "Shows below", 30, 5, 100, 5, Units.Kmh, "Also shows when you're on track but slower than this"),
    };

    private struct Row
    {
        public int Car;
        public string Number, Class;
        public Color Color;
        public double Time;
        public bool Faster;
    }

    private readonly List<Approaching> _cars = new(TelemetryFrame.MaxCars);
    private readonly Row[] _rows = new Row[MaxRows];
    private readonly float[] _labelRight = new float[2];
    private int _count;
    private RejoinPlan _plan;
    private double _ringMax;

    public override bool HasContent(HudView v) => Rejoin.Shows(v.Frame, v.Options.Number("slow"));

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Rejoin helper")) return;
        var f = v.Frame;
        double needed = v.Options.Number("gap");
        bool live = Rejoin.Shows(f, v.Options.Number("slow"));
        if (!live && !(p.IsPreview || f.Simulated))
        {
            _ringMax = 0;
            var quiet = p.Header("Rejoin");
            p.Text("Shows when you're off track or stopped", p.Small, p.P.Muted, quiet, HAlign.Center, VAlign.Center);
            return;
        }
        if (live) Gather(p, v);
        else FillSample(p, v);
        _plan = Rejoin.Plan(_cars, needed);
        double fraction;
        if (!live) fraction = _plan.Go ? 0 : Sample;
        else if (_plan.Go) { _ringMax = 0; fraction = 0; }
        else
        {
            _ringMax = Math.Max(_ringMax, _plan.Wait);
            fraction = _ringMax > 0 ? _plan.Wait / _ringMax : 0;
        }
        switch (v.Options.Choice("style"))
        {
            case Go: DrawGo(p, v, needed); break;
            case Line: DrawLine(p, v, needed); break;
            default: DrawCountdown(p, v, needed, fraction); break;
        }
    }

    private void Gather(HudPainter p, HudView v)
    {
        var f = v.Frame;
        Rejoin.Collect(f, v.Race.Profile, _cars);
        _count = 0;
        for (int i = 0; i < _cars.Count && _count < MaxRows; i++)
        {
            var c = _cars[i];
            if (c.TimeToYou > Lookahead) break;
            f.Session.ByCar.TryGetValue(c.Car, out var d);
            _rows[_count++] = new Row
            {
                Car = c.Car,
                Number = d is { CarNumber.Length: > 0 } ? PooledText.Concat("#", d.CarNumber) : RaceFormat.Missing,
                Class = d is { ClassLabel.Length: > 0 } ? d.ClassLabel : RaceFormat.Missing,
                Color = p.ClassColor(f.Session, d?.ClassId ?? 0),
                Time = c.TimeToYou,
                Faster = c.FasterClass,
            };
        }
    }

    private static readonly double[] SampleTimes = { 1.4, 3.1, 8.0 };

    private void FillSample(HudPainter p, HudView v)
    {
        var f = v.Frame;
        if (f.Session.ByCar.Count > 0)
        {
            Rejoin.Collect(f, v.Race.Profile, _cars);
            if (_cars.Count >= SampleTimes.Length)
            {
                int faster = -1;
                for (int i = SampleTimes.Length - 1; i < _cars.Count && faster < 0; i++) if (_cars[i].FasterClass) faster = i;
                if (faster > SampleTimes.Length - 1) _cars[SampleTimes.Length - 1] = _cars[faster];
                _cars.RemoveRange(SampleTimes.Length, _cars.Count - SampleTimes.Length);
                _count = SampleTimes.Length;
                for (int i = 0; i < _count; i++)
                {
                    var c = _cars[i] with { TimeToYou = SampleTimes[i] };
                    _cars[i] = c;
                    f.Session.ByCar.TryGetValue(c.Car, out var d);
                    _rows[i] = new Row
                    {
                        Car = c.Car,
                        Number = d is { CarNumber.Length: > 0 } ? PooledText.Concat("#", d.CarNumber) : RaceFormat.Missing,
                        Class = d is { ClassLabel.Length: > 0 } ? d.ClassLabel : RaceFormat.Missing,
                        Color = p.ClassColor(f.Session, d?.ClassId ?? 0),
                        Time = c.TimeToYou,
                        Faster = c.FasterClass,
                    };
                }
                return;
            }
        }
        _cars.Clear();
        _cars.Add(new Approaching(1, 1.4, false));
        _cars.Add(new Approaching(2, 3.1, false));
        _cars.Add(new Approaching(3, 8.0, true));
        _count = 3;
        _rows[0] = new Row { Car = 1, Number = "#65", Class = "GT3", Color = p.P.Attack, Time = 1.4 };
        _rows[1] = new Row { Car = 2, Number = "#72", Class = "GT3", Color = p.P.Attack, Time = 3.1 };
        _rows[2] = new Row { Car = 3, Number = "#6", Class = "GTP", Color = p.P.Fastest, Time = 8.0, Faster = true };
    }

    private static string Seconds(double s) => PooledText.Number("", s, "0.0", Inv, SecondsUnit);

    private static string Bare(double s) => PooledText.Number("", s, "0.0", Inv);

    private Color TimeColor(HudPainter p, int i, double needed) =>
        i == 0 && _rows[i].Time < needed ? p.P.DangerText : i < _plan.Before ? p.P.Heading : p.P.Muted;

    private static void Chip(HudPainter p, string text, Color bg, Color fg)
    {
        if (!p.TitleRowShown) return;
        float d = p.Density, h = p.PillHeight - 8 * d;
        string caps = p.LabelText(text);
        float w = p.Measure(caps, p.Label) + 14 * d;
        var r = new RectangleF(p.Bounds.Width - 8 * d - w, (p.PillHeight - h) / 2, w, h);
        p.Fill(r, bg, p.CornerRadius(4));
        p.Text(caps, p.Label, fg, r, HAlign.Center, VAlign.Center, false);
    }

    private void Header(HudPainter p, string title)
    {
        p.Header(title);
        if (_plan.Go) Chip(p, "Go", p.P.GoodSoft, p.P.GoodText);
        else Chip(p, "Hold", p.P.WarnSoft, p.P.WarnText);
    }

    private static readonly string[] CountdownParts = { "main", "clear" };
    private static readonly string[] CountdownMainParts = { "ring", "cars" };

    private void DrawCountdown(HudPainter p, HudView v, double needed, double fraction)
    {
        float d = p.Density;
        Header(p, "Rejoin");
        var area = p.HeaderBody();
        var footFont = p.Small;
        float footH = p.LineHeight(footFont) + 8 * d, gap = 6 * d;
        p.NeedBelow(area, 108 * d + gap + footH);
        bool footer = area.Height >= 70 * d + gap + footH;
        float ring = Math.Max(0, Math.Min(Math.Min(110 * d, area.Width * 0.4f), area.Height - (footer ? footH + gap : 0)));
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "rejoin.countdown", area, false, gap, CountdownParts, stackalloc float[] { Math.Max(ring, float.Epsilon), footer ? footH : -1 }, parts);
        Span<RectangleF> halves = stackalloc RectangleF[2];
        p.Arrange(v, "rejoin.countdown.main", new RectangleF(area.X, parts[0].Y, area.Width, ring), true, gap, CountdownMainParts, stackalloc float[] { Math.Max(ring, float.Epsilon), 0 }, halves);
        var ringTile = new RectangleF(halves[0].X, parts[0].Y, ring, ring);
        p.Tile(ringTile);
        var dial = ringTile.Inflated(-6 * d, -6 * d);
        if (_plan.Go) RingDial.Draw(p, dial, 1, p.P.Good, "GO", "Clear", p.P.GoodText);
        else RingDial.Draw(p, dial, fraction, p.P.Warn, Bare(_plan.Wait), "Safe in", p.P.Heading);

        DrawTable(p, halves[1], needed);

        if (!footer) return;
        var foot = new RectangleF(area.X, parts[1].Y, area.Width, footH);
        if (foot.Y > area.Y) p.Line(foot.X, MathF.Round(foot.Y) + 0.5f, foot.Right, MathF.Round(foot.Y) + 0.5f, p.P.Line, 1);
        var (left, right) = FootText();
        p.Text(left, footFont, p.P.Muted, foot, HAlign.Left, VAlign.Center);
        if (right.Length > 0) p.Number(right, p.Bold, p.P.Heading, foot, HAlign.Right, VAlign.Center);
    }

    private (string Left, string Right) FootText()
    {
        if (_count == 0) return ("Nothing coming", "");
        if (_plan.ClearFor is not { } clear) return ("Then clear", "");
        return (_plan.Go ? "Clear for" : "Then clear for", Seconds(clear));
    }

    private void DrawTable(HudPainter p, RectangleF r, double needed)
    {
        float d = p.Density;
        var rowFont = p.Font(12.5f, true);
        var head = p.Label;
        float headH = p.LineHeight(head) + 2 * d;
        float numW = Math.Max(p.Measure("#000", rowFont), 40 * d), gapW = Math.Max(p.Measure("00.0", rowFont), 40 * d), col = 8 * d;
        float classW = Math.Max(0, r.Width - numW - gapW - col * 2);
        p.Text(p.LabelText("Car"), head, p.P.Faint, new RectangleF(r.X, r.Y, numW, headH), HAlign.Left, VAlign.Center, false);
        if (classW > 24 * d) p.Text(p.LabelText("Class"), head, p.P.Faint, new RectangleF(r.X + numW + col, r.Y, classW, headH), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText("Gap"), head, p.P.Faint, new RectangleF(r.Right - gapW, r.Y, gapW, headH), HAlign.Right, VAlign.Center, false);
        float y = r.Y + headH;
        float rowH = Math.Max(p.LineHeight(rowFont), (r.Bottom - y) / 3);
        rowH = Math.Min(rowH, p.LineHeight(rowFont) + 10 * d);
        if (_count == 0)
        {
            p.Text("No cars coming", p.Small, p.P.Faint, RectangleF.FromLTRB(r.X, y, r.Right, r.Bottom), HAlign.Center, VAlign.Center);
            return;
        }
        for (int i = 0; i < _count; i++)
        {
            if (y + rowH > r.Bottom + 1) break;
            ref var row = ref _rows[i];
            p.Line(r.X, MathF.Round(y) + 0.5f, r.Right, MathF.Round(y) + 0.5f, p.P.Line, 1);
            p.Text(row.Number, rowFont, row.Faster ? p.P.WarnText : p.P.Heading, new RectangleF(r.X, y, numW, rowH), HAlign.Left, VAlign.Center);
            if (classW > 24 * d)
            {
                float pw = Math.Min(classW, p.Measure(row.Class, p.SmallBold) + 12 * d), ph = p.LineHeight(p.SmallBold) + 2 * d;
                p.Pill(new RectangleF(r.X + numW + col, y + (rowH - ph) / 2, pw, ph), row.Class, row.Color, p.SmallBold);
            }
            p.Text(Bare(row.Time), rowFont, TimeColor(p, i, needed), new RectangleF(r.Right - gapW, y, gapW, rowH), HAlign.Right, VAlign.Center, false);
            y += rowH;
        }
    }

    private static readonly string[] GoParts = { "word", "stats", "next" };
    private static readonly string[] GoTileParts = { "first", "within" };

    private void DrawGo(HudPainter p, HudView v, double needed)
    {
        float d = p.Density, gap = 6 * d;
        var area = p.Header("Rejoin");
        var footFont = p.SmallBold;
        float bigH = Math.Min(58 * d, area.Height), tileH = p.BoxedStatHeight(p.Big) + 4 * d, footH = p.LineHeight(footFont) + 8 * d;
        p.NeedBelow(area, bigH + gap + tileH + gap + footH);
        bool go = _plan.Go;
        Span<float> stack = stackalloc float[] { Math.Max(bigH, float.Epsilon), tileH, footH };
        var order = v.Options.Arranged("rejoin.go", GoParts);
        float at = area.Y;
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            if (i != 0 && at + stack[i] > area.Bottom + 1) stack[i] = -1;
            else at += stack[i] + gap;
        }
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "rejoin.go", area, false, gap, GoParts, stack, parts);
        var big = new RectangleF(area.X, parts[0].Y, area.Width, bigH);
        var bg = go ? p.P.Good : p.P.Danger;
        p.Fill(big, p.Solid(bg, 200), p.CornerRadius(4));
        p.Display(go ? "GO" : "WAIT", Math.Clamp(bigH / d * 0.58f, 12f, 34f), HudPalette.On(bg), big, HAlign.Center, VAlign.Center);

        if (stack[1] >= 0)
        {
            Span<RectangleF> pair = stackalloc RectangleF[2];
            p.Arrange(v, "rejoin.go.stats", new RectangleF(area.X, parts[1].Y, area.Width, tileH), true, gap, GoTileParts, stackalloc float[] { 0, 0 }, pair);
            var t1 = pair[0];
            var t2 = pair[1];
            if (go)
            {
                p.Tile(t1);
                p.ValueStat(t1.Inflated(-4 * d, -3 * d), "Clear for", _plan.ClearFor is { } c ? Seconds(c) : RaceFormat.Missing, p.P.GoodText, p.Big, HAlign.Center);
            }
            else
            {
                p.Tile(t1);
                p.ValueStat(t1.Inflated(-4 * d, -3 * d), "Next car", _count > 0 ? Seconds(_rows[0].Time) : RaceFormat.Missing, TimeColor(p, 0, needed), p.Big, HAlign.Center);
            }
            p.Tile(t2);
            int within = 0;
            while (within < _count && _rows[within].Time < needed) within++;
            p.ValueStat(t2.Inflated(-4 * d, -3 * d), PooledText.Concat("Within ", Seconds(needed)), PooledText.Number("", within), p.P.Heading, p.Big, HAlign.Center);
        }
        if (stack[2] >= 0)
        {
            var foot = new RectangleF(area.X, parts[2].Y, area.Width, footH);
            string text;
            Color color;
            int show = go ? _plan.Before : 0;
            if (_count == 0 || show >= _count) { text = "Track clear"; color = p.P.GoodText; }
            else
            {
                ref var row = ref _rows[show];
                text = go ? PooledText.Concat("Then ", row.Number, PooledText.Concat(" ", row.Class)) : PooledText.Concat(row.Number, PooledText.Concat(" ", row.Class), " coming");
                color = go ? p.P.GoodText : p.P.DangerText;
            }
            p.Fill(foot, HudPalette.Alpha(go ? p.P.Good : p.P.Danger, 40), p.CornerRadius(4));
            p.Text(text, footFont, color, foot, HAlign.Center, VAlign.Center);
        }
    }

    private static readonly string[] LineParts = { "line", "stats" };
    private static readonly string[] LineTileParts = { "next", "safe", "faster" };

    private void DrawLine(HudPainter p, HudView v, double needed)
    {
        float d = p.Density, gap = 6 * d;
        string right = _plan.Go ? "Clear now" : PooledText.Concat("Gap in ", Seconds(_plan.Wait));
        var area = p.Header("Coming up behind", right, _plan.Go ? p.P.GoodText : p.P.WarnText);
        var small = p.Small;
        float lineH = 26 * d + p.LineHeight(p.Label) + 2 * p.LineHeight(small) + 14 * d, tileH = p.BoxedStatHeight(p.Bold) + 6 * d;
        p.NeedBelow(area, lineH + gap + tileH);
        bool tiles = area.Height >= lineH + gap + tileH;
        if (!tiles) lineH = Math.Max(0, area.Height);
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "rejoin.line", area, false, gap, LineParts, stackalloc float[] { Math.Max(lineH, float.Epsilon), tiles ? tileH : -1 }, parts);
        var box = new RectangleF(area.X, parts[0].Y, area.Width, lineH);
        p.Tile(box);
        float labelTop = box.Y + 4 * d;
        float barY = labelTop + p.LineHeight(p.Label) + 14 * d;
        float youR = 8 * d, carR = 6 * d;
        float youX = box.Right - 14 * d - youR, leftX = box.X + 12 * d;
        double span = Math.Clamp(Math.Max(Math.Max(8, needed * 2.5), _plan.Wait + (_plan.ClearFor ?? 0) + 1), 8, Lookahead);
        if (_count > 0) span = Math.Clamp(Math.Max(span, _rows[Math.Min(_count, 4) - 1].Time + 1), 8, Lookahead);
        float X(double t) => youX - (float)(Math.Min(t, span) / span) * (youX - leftX);
        p.Fill(new RectangleF(leftX - 6 * d, barY - 5 * d, youX - leftX + 6 * d, 10 * d), p.Solid(p.Sunken(0.25)), 5 * d);

        float gx1 = X(_plan.Wait), gx2 = _plan.ClearFor is { } cf ? X(_plan.Wait + cf) : leftX - 6 * d;
        if (_count == 0) gx2 = leftX - 6 * d;
        var safe = RectangleF.FromLTRB(Math.Min(gx1, gx2) + (_plan.Before > 0 ? carR + 2 * d : 0), barY - 13 * d, Math.Max(gx1, gx2) - (_plan.Go ? youR + 2 * d : carR + 2 * d), barY + 13 * d);
        if (safe.Width > 6 * d)
        {
            p.Fill(safe, HudPalette.Alpha(p.P.Good, 50), p.CornerRadius(4));
            p.Outline(safe, p.P.Good, 1.5f * d, p.CornerRadius(4));
            string label = _plan.ClearFor is { } c2 ? PooledText.Concat("Safe gap ", Seconds(c2)) : "Safe gap";
            string caps = p.LabelText(label);
            float lw = p.Measure(caps, p.Label) + 2;
            float lx = Math.Clamp(safe.X + safe.Width / 2 - lw / 2, box.X + 4 * d, box.Right - 4 * d - lw);
            p.Text(caps, p.Label, p.P.GoodText, new RectangleF(lx, labelTop, lw, p.LineHeight(p.Label)), HAlign.Left, VAlign.Center, false);
        }

        _labelRight[0] = _labelRight[1] = float.PositiveInfinity;
        float rowTop = barY + 13 * d + 2 * d, rowH = p.LineHeight(small);
        p.Dot(youX, barY, youR + 2 * d, p.P.Heading);
        p.Dot(youX, barY, youR, p.P.Player);
        string you = p.LabelText("You");
        float yw = p.Measure(you, p.Label) + 2;
        if (rowTop + rowH <= box.Bottom + 1) p.Text(you, p.Label, p.P.PlayerText, new RectangleF(Math.Min(youX - yw / 2, box.Right - 2 * d - yw), rowTop, yw, rowH), HAlign.Left, VAlign.Center, false);
        _labelRight[0] = Math.Min(youX - yw / 2, box.Right - 2 * d - yw);
        for (int i = 0; i < _count; i++)
        {
            ref var row = ref _rows[i];
            if (row.Time > span) break;
            float cx = X(row.Time);
            bool close = i == 0 && row.Time < needed;
            if (close) p.Dot(cx, barY, carR + 2.5f * d, p.P.Danger);
            else if (row.Faster) p.Dot(cx, barY, carR + 2 * d, p.P.Warn);
            p.Dot(cx, barY, carR, row.Color);
            string text = PooledText.Concat(row.Number, " · ", Bare(row.Time));
            float tw = p.Measure(text, small) + 2;
            float tx = Math.Max(box.X + 2 * d, cx - tw / 2);
            for (int k = 0; k < 2; k++)
            {
                if (tx + tw > _labelRight[k] - 4 * d || rowTop + (k + 1) * rowH > box.Bottom + 1) continue;
                var color = close ? p.P.DangerText : row.Faster ? p.P.WarnText : p.P.Text;
                p.Text(text, small, color, new RectangleF(tx, rowTop + k * rowH, tw, rowH), HAlign.Left, VAlign.Center, false);
                _labelRight[k] = tx;
                break;
            }
        }

        if (!tiles) return;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "rejoin.line.stats", new RectangleF(area.X, parts[1].Y, area.Width, tileH), true, gap, LineTileParts, stackalloc float[] { 0, 0, 0 }, cells);
        p.BoxedStat(cells[0], "Next car", _count > 0 ? Seconds(_rows[0].Time) : RaceFormat.Missing, _count > 0 ? TimeColor(p, 0, needed) : p.P.Faint);
        p.BoxedStat(cells[1], "Safe in", _plan.Go ? "Now" : Seconds(_plan.Wait), _plan.Go ? p.P.GoodText : p.P.WarnText);
        int faster = -1;
        for (int i = 0; i < _count && faster < 0; i++) if (_rows[i].Faster) faster = i;
        if (faster >= 0) p.BoxedStat(cells[2], PooledText.Concat("In ", Seconds(_rows[faster].Time)), _rows[faster].Class, _rows[faster].Color);
        else p.BoxedStat(cells[2], "Faster class", RaceFormat.Missing, p.P.Faint);
    }
}

internal static class RingDial
{
    public static void Draw(HudPainter p, RectangleF box, double fraction, Color color, string value, string label, Color valueColor)
    {
        float d = p.Density, size = Math.Min(box.Width, box.Height), stroke = Math.Max(4 * d, size * 0.09f);
        var ring = new RectangleF(box.X + (box.Width - size) / 2 + stroke / 2, box.Y + (box.Height - size) / 2 + stroke / 2, size - stroke, size - stroke);
        p.G.DrawEllipse(p.Pen(p.Solid(p.Sunken(0.3)), stroke), ring);
        double f = Math.Clamp(fraction, 0, 1);
        if (f > 0.002) p.G.DrawArc(p.Pen(color, stroke), ring, -90, (float)(360 * f));
        float px = Math.Clamp(size * 0.24f / d, 14f, 28f);
        var numH = p.DisplaySize("0", px).Height;
        string caps = p.LabelText(label);
        float lh = p.LineHeight(p.Label);
        float stack = numH + 5 * d + lh, top = ring.Y + ring.Height / 2 - stack / 2;
        var inner = ring.Inflated(-stroke, 0);
        float vw = p.DisplaySize(value, px).Width;
        if (vw > inner.Width) px *= inner.Width / vw;
        p.Display(value, px, valueColor, new RectangleF(inner.X, top, inner.Width, numH), HAlign.Center, VAlign.Bottom);
        p.Text(caps, p.Label, p.P.Muted, new RectangleF(inner.X, top + numH + 5 * d, inner.Width, lh), HAlign.Center, VAlign.Center, false);
    }
}
