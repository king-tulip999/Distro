using System.Drawing;
using Core.Config;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class WeatherWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        TitleRow,
        HudOption.Choice("look", "Look", PanelLook, new HudChoice(PanelLook, "Panel", "A compass with the wind, a row per value and the track's wetness"), new HudChoice(StripLook, "Strip", "The sky over one low row of temperatures, humidity and wind")),
        HudOption.Columns("rows", "Rows", "track,air,wind,humidity,rain,rubber",
            new HudChoice("track", "Track temperature"), new HudChoice("air", "Air temperature"), new HudChoice("wind", "Wind"), new HudChoice("humidity", "Humidity"),
            new HudChoice("rain", "Rain"), new HudChoice("fog", "Fog"), new HudChoice("rubber", "Rubber", "From the session information"), new HudChoice("trend", "Track temperature change (10 min)")),
        HudOption.Bool("wetBar", "Track wetness bar", true),
        HudOption.Choice("temp", "Temperatures in", "units", new HudChoice("units", "Overlay units"), new HudChoice("c", "°C"), new HudChoice("f", "°F"), new HudChoice("both", "°C and °F")),
        HudOption.Bool("wind", "Wind", true, "A pointer in a dotted dial showing where the wind blows, the speed under it: red for a headwind, amber across, green behind"),
        HudOption.Bool("turn", "Pointer turns with the car", true, "On: up = straight ahead. Off: up = north."),
    };

    private readonly (string Label, string Value)[] _cells = new (string, string)[3];
    private static readonly string[] StripParts = { "values", "wind" }, StripColumnParts = { "sky", "body" }, StripValueParts = { "air", "track", "humidity" };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Weather")) return;
        var f = v.Frame;
        var u = v.Units;
        float d = p.Density;
        string temp = v.Options.Choice("temp");
        bool fahrenheit = temp == "f" || temp == "units" && u == HudUnits.Imperial;
        string Temp(double? c) => temp == "both" && c is { } x && double.IsFinite(x)
            ? $"{x:0}° / {x * 9 / 5 + 32:0}°"
            : RaceFormat.Temp(c, fahrenheit ? HudUnits.Imperial : HudUnits.Metric);
        if (v.Options.Choice("look") != StripLook)
        {
            DrawPanel(p, v, Temp);
            return;
        }
        var bounds = p.Bounds;
        float pill = p.PillHeight + 4 * d;
        var body = bounds;
        p.Panel(bounds);
        bool sky = bounds.Height >= pill + p.PieceGap + 44 * d;
        {
            Span<RectangleF> parts = stackalloc RectangleF[2];
            Span<float> heights = stackalloc float[2];
            heights[0] = sky ? pill : -1;
            p.Arrange(v, "weather.strip.column", bounds, false, p.PieceGap, StripColumnParts, heights, parts);
            body = parts[1];
            if (sky)
            {
                var hr = parts[0];
                Sky(p, v, hr);
                p.Seam(bounds, hr.Y <= body.Y ? hr.Bottom : hr.Y);
            }
        }
        var area = body.Inflated(-10 * d, -6 * d);
        _cells[0] = ("AIR", Temp(f.AirTemp));
        _cells[1] = ("TRACK", Temp(f.TrackTemp));
        _cells[2] = ("HUMIDITY", f.Humidity is { } h ? PooledText.Number("", h * 100, "0", System.Globalization.CultureInfo.CurrentCulture, "%") : RaceFormat.Missing);
        var font = p.Big;
        float cellPad = 8 * d;
        float need = 0;
        int shown = 0;
        var order = v.Options.Arranged("weather.strip.values", StripValueParts);
        for (int n = 0; n < order.Count; n++)
        {
            int i = order[n];
            float cell = Math.Max(p.Measure(_cells[i].Value, font), p.Measure(_cells[i].Label, p.Label)) + cellPad * 2;
            float wider = Math.Max(need, cell);
            if (wider * (n + 1) > area.Width) break;
            need = wider;
            shown = n + 1;
        }
        if (shown == 0) return;
        Span<float> cellSizes = stackalloc float[3];
        for (int n = 0; n < order.Count; n++) cellSizes[order[n]] = n < shown ? 0 : -1;
        float valuesW = need * shown;
        if (v.Options.Bool("wind") && WindWidth(p, v, area.Height) is var windW && area.Width - valuesW >= windW)
        {
            Span<RectangleF> boxes = stackalloc RectangleF[2];
            Span<float> sizes = stackalloc float[2];
            sizes[1] = Math.Max(windW, Math.Min(area.Width * 0.47f, area.Width - valuesW));
            p.Arrange(v, "weather.strip", area, true, 0, StripParts, sizes, boxes);
            Wind(p, v, boxes[1]);
            area = boxes[0];
        }
        Span<RectangleF> cellBoxes = stackalloc RectangleF[3];
        p.Arrange(v, "weather.strip.values", area, true, 0, StripValueParts, cellSizes, cellBoxes);
        float vh = p.LineHeight(font), lh = p.LineHeight(p.Label);
        float top = area.Y + (area.Height - vh - lh) / 2;
        for (int i = 0; i < cellBoxes.Length; i++)
        {
            var box = cellBoxes[i];
            if (cellSizes[i] < 0 || box.Width <= 0) continue;
            float x = box.X;
            if (x > area.X + 0.5f) p.Line(x, top - 2 * d, x, top + lh + vh + 2 * d, HudPalette.Alpha(p.P.Muted, 110), 1);
            var cell = new RectangleF(x + cellPad / 2, top, box.Width - cellPad, lh + vh);
            p.Text(_cells[i].Label, p.Label, p.LabelColor, new RectangleF(cell.X, cell.Y, cell.Width, lh), HAlign.Center, VAlign.Top, false);
            p.Number(_cells[i].Value, font, p.P.Heading, new RectangleF(cell.X, cell.Y + lh, cell.Width, vh), HAlign.Center, VAlign.Center);
        }
    }

    private static readonly string[] WindWords = { "HEADWIND", "CROSSWIND", "TAILWIND", "CALM" };

    private static float DialSize(HudPainter p, float height) => Math.Min(height + 6 * p.Density, 74 * p.Density);

    private static float WindWidth(HudPainter p, HudView v, float height)
    {
        float d = p.Density;
        string line = RaceFormat.SpeedUnit(v.Units).ToUpperInvariant() + " · WNW";
        float text = Math.Max(p.Widest(WindWords, p.SmallBold), p.Measure(line, p.Label)) + 4;
        return 8 * d + DialSize(p, height) + 8 * d + text;
    }

    private static void Sky(HudPainter p, HudView v, RectangleF r)
    {
        var f = v.Frame;
        float d = p.Density;
        var inner = r.Inflated(-10 * d, 0);
        float s = inner.Height * 0.7f;
        var icon = new RectangleF(inner.X, inner.Y + (inner.Height - s) / 2, s * 1.2f, s);
        string sky = f.Session.Skies;
        Icon(p, icon, sky, f.Precipitation is > 0);
        bool wet = f.Precipitation is > 0 || f.TrackWetness is > 1 || f.DeclaredWet == true;
        string state = (f.DeclaredWet == true ? "DECLARED WET · " : "") + RaceFormat.Wetness(f.TrackWetness).ToUpperInvariant() + (f.Precipitation is { } rain ? $" · RAIN {rain * 100:0}%" : "");
        string skyText = sky.Length > 0 ? SentenceCase(sky) : RaceFormat.Missing;
        float skyX = icon.Right + 8 * d;
        float stateW = Math.Min(p.Measure(state, p.Label) + 4, inner.Right - skyX - p.Measure(skyText, p.Bold) - 12 * d);
        if (stateW < 40 * d) stateW = 0;
        else p.Text(state, p.Label, wet ? p.P.WarnText : p.P.Muted, new RectangleF(inner.Right - stateW, inner.Y, stateW, inner.Height), HAlign.Right, VAlign.Center);
        p.Text(skyText, p.Bold, p.P.Heading, new RectangleF(skyX, inner.Y, Math.Max(0, inner.Right - stateW - skyX), inner.Height), HAlign.Left, VAlign.Center);
    }

    private static (string From, string To) _skyCase = ("", "");

    private static string SentenceCase(string sky)
    {
        var known = _skyCase;
        if (ReferenceEquals(known.From, sky) || known.From == sky) return known.To;
        string shown = sky[..1] + sky[1..].ToLowerInvariant();
        _skyCase = (sky, shown);
        return shown;
    }

    private static void Icon(HudPainter p, RectangleF r, string sky, bool raining)
    {
        float s = r.Height;
        bool clear = sky.Contains("clear", StringComparison.OrdinalIgnoreCase);
        bool sun = clear || sky.Contains("partly", StringComparison.OrdinalIgnoreCase);
        if (sun) p.G.FillEllipse(p.Brush(p.P.WarnText), clear ? r.X + s * 0.15f : r.X + s * 0.55f, clear ? r.Y + s * 0.1f : r.Y, clear ? s * 0.8f : s * 0.6f, clear ? s * 0.8f : s * 0.6f);
        if (clear) return;
        var c = raining ? p.P.Muted : p.P.Heading;
        p.G.FillEllipse(p.Brush(c), r.X, r.Y + s * 0.42f, s * 0.55f, s * 0.55f);
        p.G.FillEllipse(p.Brush(c), r.X + s * 0.25f, r.Y + s * 0.22f, s * 0.62f, s * 0.62f);
        p.G.FillEllipse(p.Brush(c), r.X + s * 0.62f, r.Y + s * 0.45f, s * 0.52f, s * 0.52f);
        p.G.FillRectangle(p.Brush(c), r.X + s * 0.27f, r.Y + s * 0.6f, s * 0.62f, s * 0.37f);
        if (!raining) return;
        var pen = p.Pen(p.P.AccentText, Math.Max(1.2f, s * 0.08f));
        for (int k = 0; k < 3; k++)
        {
            float x = r.X + s * (0.3f + k * 0.25f);
            p.G.DrawLine(pen, x, r.Bottom + s * 0.02f, x - s * 0.08f, r.Bottom + s * 0.2f);
        }
    }

    private static void Wind(HudPainter p, HudView v, RectangleF r)
    {
        var f = v.Frame;
        float d = p.Density;
        p.Divider(r.X, r.Y + 4 * d, r.Bottom - 4 * d);
        float size = DialSize(p, r.Height);
        var ring = new RectangleF(r.X + 8 * d, r.Y + (r.Height - size) / 2, size, size);
        double? toward = f.WindDir is { } from ? from + Math.PI : null;
        double heading = v.Options.Bool("turn") && f.YawNorth is { } yaw ? yaw : 0;
        WindArrow.Draw(p, ring, f.WindSpeed, toward, heading, "headtail", v.Units, false);
        var text = new RectangleF(ring.Right + 8 * d, r.Y, r.Right - ring.Right - 8 * d, r.Height);
        var (word, color) = (toward is { } t ? WindArrow.Relation(f.WindSpeed, t, heading) : WindRelation.Calm) switch
        {
            WindRelation.Head => ("HEADWIND", p.P.DangerText),
            WindRelation.Cross => ("CROSSWIND", p.P.WarnText),
            WindRelation.Tail => ("TAILWIND", p.P.GoodText),
            _ => ("CALM", p.P.Muted),
        };
        float wh = p.LineHeight(p.SmallBold), lh = p.LineHeight(p.Label);
        float top = text.Y + (text.Height - wh - lh - 2) / 2;
        p.Text(word, p.SmallBold, color, new RectangleF(text.X, top, text.Width, wh), HAlign.Left, VAlign.Center);
        p.Text(RaceFormat.SpeedUnit(v.Units).ToUpperInvariant() + " · " + RaceFormat.Compass(toward), p.Label, p.LabelColor, new RectangleF(text.X, top + wh + 2, text.Width, lh), HAlign.Left, VAlign.Top, false);
    }
}

internal enum WindRelation { Calm, Head, Cross, Tail }

internal static class WindArrow
{
    public const double CalmBelow = 0.5;

    public static WindRelation Relation(double? speed, double towardRad, double headingRad)
    {
        if (speed is not > CalmBelow) return WindRelation.Calm;
        double a = Math.IEEERemainder(towardRad - headingRad, Math.PI * 2);
        double deg = Math.Abs(a) * 180 / Math.PI;
        return deg <= 60 ? WindRelation.Tail : deg >= 120 ? WindRelation.Head : WindRelation.Cross;
    }

    public static Color ColorOf(HudPainter p, WindRelation r, string colors) => colors == "accent"
        ? (r == WindRelation.Calm ? p.P.Muted : p.P.AccentText)
        : r switch { WindRelation.Head => p.P.DangerText, WindRelation.Cross => p.P.WarnText, WindRelation.Tail => p.P.GoodText, _ => p.P.Muted };

    private static readonly PointF[] Pointer = new PointF[4];
    private static readonly (float Along, float Across)[] PointerShape = { (0.55f, 0f), (-0.52f, 0.43f), (-0.29f, 0f), (-0.52f, -0.43f) };
    private static readonly (float X, float Y)[] ManyDots = Circle(36), FewDots = Circle(24);

    private static (float X, float Y)[] Circle(int count) => Enumerable.Range(0, count)
        .Select(i => ((float)Math.Sin(i * Math.PI * 2 / count), (float)-Math.Cos(i * Math.PI * 2 / count))).ToArray();

    public static void Draw(HudPainter p, RectangleF box, double? speed, double? towardRad, double headingRad, string colors, HudUnits units, bool showUnit, string speedUnit = "units")
    {
        var relation = towardRad is { } t ? Relation(speed, t, headingRad) : WindRelation.Calm;
        var color = ColorOf(p, relation, colors);
        double? shown = speed is not { } s ? null : speedUnit switch
        {
            "kmh" => s * 3.6, "mph" => s * 2.23694, "ms" => s,
            _ => RaceFormat.SpeedValue(s, units),
        };
        string unit = speedUnit switch { "kmh" => Units.Kmh, "mph" => Units.Mph, "ms" => Units.MetresPerSecond, _ => RaceFormat.SpeedUnit(units) };
        var font = p.Font(MathF.Max(10, Math.Min(box.Width, box.Height) * 0.2f), false);
        string text = shown is { } x ? x.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : RaceFormat.Missing;
        float lh = p.LineHeight(font), uh = showUnit ? p.LineHeight(p.Tiny) : 0;
        float size = Math.Max(8, Math.Min(box.Width, box.Height - lh - uh));
        float top = box.Y + (box.Height - size - lh - uh) / 2;
        float cx = box.X + box.Width / 2, cy = top + size / 2;
        float dot = Math.Max(1f, size * 0.017f), r = size / 2 - dot;
        var dialColor = HudPalette.Alpha(p.P.Muted, 170);
        var dial = p.Brush(dialColor);
        foreach (var (dx, dy) in size < 70 ? FewDots : ManyDots)
            p.G.FillEllipse(dial, cx + dx * r - dot, cy + dy * r - dot, dot * 2, dot * 2);
        var tick = p.Pen(dialColor, Math.Max(1.5f, r * 0.05f));
        p.G.DrawLine(tick, cx, cy - r, cx, cy - r * 0.8f);
        p.G.DrawLine(tick, cx, cy + r, cx, cy + r * 0.8f);
        p.G.DrawLine(tick, cx - r, cy, cx - r * 0.8f, cy);
        p.G.DrawLine(tick, cx + r, cy, cx + r * 0.8f, cy);
        if (towardRad is { } toward)
        {
            double bearing = toward - headingRad;
            float ux = (float)Math.Sin(bearing), uy = (float)-Math.Cos(bearing);
            for (int i = 0; i < Pointer.Length; i++)
            {
                var (along, across) = PointerShape[i];
                Pointer[i] = new PointF(cx + (ux * along - uy * across) * r, cy + (uy * along + ux * across) * r);
            }
            p.G.FillPolygon(p.Brush(color), Pointer);
        }
        var numberBox = new RectangleF(box.X, top + size, box.Width, lh);
        p.Text(text, font, color, numberBox, HAlign.Center, VAlign.Center, false);
        if (showUnit) p.Text(unit.ToUpperInvariant(), p.Tiny, p.LabelColor, new RectangleF(box.X, numberBox.Bottom - 2, box.Width, uh), HAlign.Center, VAlign.Center, false);
    }
}

internal sealed class WindWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Bool("turn", "Turns with the car", true, "On: the pointer shows where the wind blows as you drive (down = a headwind). Off: up = north."),
        HudOption.Choice("colors", "Colors", "headtail", new HudChoice("headtail", "By headwind / crosswind / tailwind", "Red at the nose, amber across, green behind"), new HudChoice("accent", "The accent color")),
        HudOption.Choice("speedUnit", "Speed in", "units", new HudChoice("units", "Overlay units"), new HudChoice("kmh", Units.Kmh), new HudChoice("mph", Units.Mph), new HudChoice("ms", Units.MetresPerSecond)),
        HudOption.Bool("unit", "Unit label", true),
        HudOption.Bool("title", "Title", false, "“Wind” over the dial"),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        var f = v.Frame;
        var o = v.Options;
        if (!o.Bool("title")) p.Panel();
        var area = o.Bool("title") ? p.Header("Wind") : p.Bounds.Inflated(-6 * p.Density, -6 * p.Density);
        bool turn = o.Bool("turn");
        double? toward = f.WindDir is { } from ? from + Math.PI : null;
        if (turn && f.YawNorth is null) toward = null;
        WindArrow.Draw(p, area, f.WindSpeed, toward, turn ? f.YawNorth ?? 0 : 0, o.Choice("colors"), v.Units, o.Bool("unit"), o.Choice("speedUnit"));
    }
}

internal sealed class EventsWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[] { TitleRow, HudOption.Int("count", "Events listed", 8, 3, 30) };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Race events")) return;
        var events = v.Race.Events;
        var area = p.Header("Race events");
        float rowH = p.LineHeight(p.Small) * 1.55f * p.Density, y = area.Y;
        p.NeedBelow(area, rowH * v.Options.Int("count"));
        if (events.Count == 0) { p.Text("Nothing yet", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center); return; }
        float timeW = p.Measure("00:00:00", p.Small) + 8 * p.Density;
        for (int i = events.Count - 1, n = 0; i >= 0 && n < v.Options.Int("count"); i--, n++)
        {
            if (y + rowH > area.Bottom + 1) break;
            if (n > 0) p.AccentRule(area.X + 2, area.Right - 2, MathF.Round(y) + 0.5f);
            var e = events[i];
            var row = new RectangleF(area.X, y, area.Width, rowH);
            var time = row.Left(timeW, out var rest);
            p.Text(RaceFormat.Clock(e.SessionTime), p.Small, p.P.Muted, time.Inflated(-2, 0), HAlign.Left);
            var c = e.Level switch { AlertLevel.Critical => p.P.Danger, AlertLevel.Warning => p.P.Warn, AlertLevel.Good => p.P.Good, _ => p.P.Accent };
            p.Dot(rest.X + 4, rest.Y + rowH / 2, 3.5f, c);
            p.Text(e.Text, p.Small, p.P.Text, new RectangleF(rest.X + 13, rest.Y, rest.Width - 13, rowH), HAlign.Left);
            y += rowH;
        }
    }
}

internal sealed class BlockWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        new HudOption("source", "Value", HudOptionKind.Choice, "fuelLaps") { Choices = HudChannels.All.Select(c => new HudChoice(c.Id, c.Label, HudChannels.KindText(c.Kind))).ToArray() },
        HudOption.Int("decimals", "Decimals", -1, -1, 4, help: "−1 = the value's own."),
        HudOption.Choice("visual", "Show as", "number", new HudChoice("number", "Number"), new HudChoice("bar", "Number + bar"), new HudChoice("graph", "Number + graph")),
        HudOption.Number("min", "Bar / graph from", 0, -100000, 100000, 1),
        HudOption.Number("max", "Bar / graph to", 100, -100000, 100000, 1),
        HudOption.Number("warn", "Warning at", 0, -100000, 100000, 0.1, help: "0 with critical 0 = no coloring."),
        HudOption.Number("crit", "Critical at", 0, -100000, 100000, 0.1),
        HudOption.Bool("above", "Warn when above (else below)", true),
    };

    private readonly float[] _history = new float[90];
    private int _head, _count;
    private double _lastSample;
    private PointF[] _points = Array.Empty<PointF>();
    private static readonly string[] BlockParts = { "label", "value", "visual" };

    public override void Draw(HudPainter p, HudView v)
    {
        var o = v.Options;
        var channel = HudChannels.Find(o.Choice("source")) ?? HudChannels.All[0];
        p.Panel();
        var b = p.Bounds;
        float pad = 6 * p.Density;
        var area = new RectangleF(pad, pad * 0.6f, b.Width - pad * 2, b.Height - pad * 1.2f);
        string label = channel.Label;
        double? raw = v.Frame.Status == SimStatus.Connected ? channel.Read(v) : null;
        double? value = raw is { } r ? HudChannels.Convert(r, channel.Unit, v.Units) : null;
        string unit = HudChannels.UnitText(channel.Unit, v.Units);
        int decimals = o.Int("decimals") is >= 0 and var d ? d : channel.Decimals;
        string text = value is not { } x ? RaceFormat.Missing
            : channel.Text is { } named ? named(x)
            : channel.Unit == ChannelUnit.LapTime ? (channel.Id is "timeLeft" or "stint" ? RaceFormat.Clock(x) : RaceFormat.LapTime(x))
            : x.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);
        double warn = o.Number("warn"), crit = o.Number("crit");
        bool above = o.Bool("above");
        bool Past(double t) => t != 0 && value is { } vv && (above ? vv >= t : vv <= t);
        var color = Past(crit) ? p.P.DangerText : Past(warn) ? p.P.WarnText : p.P.Heading;
        string labelText = p.LabelText(p.Shortened(label, p.Label, area.Width));
        string visual = o.Choice("visual");
        float headH = p.LineHeight(p.Label) + 3, bodyH = Math.Max(0, area.Height - headH);
        Span<RectangleF> parts = stackalloc RectangleF[3];
        Span<float> heights = stackalloc float[3];
        heights[0] = headH;
        heights[1] = visual == "number" ? 0 : Math.Max(bodyH * 0.62f, Math.Min(bodyH, p.LineHeight(p.Big)));
        heights[2] = visual == "number" ? -1 : 0;
        p.Arrange(v, "block", area, false, 0, BlockParts, heights, parts);
        var head = parts[0];
        var main = parts[1];
        var lower = parts[2];
        p.Text(labelText, p.Label, p.LabelColor, head, HAlign.Center, VAlign.Center);
        float minV = (float)o.Number("min"), maxV = (float)o.Number("max");
        double frac = value is { } fv && maxV > minV ? (fv - minV) / (maxV - minV) : 0;
        string shownUnit = unit.Length > 0 && channel.Unit != ChannelUnit.LapTime && channel.Text is null ? unit : "";
        float unitW = shownUnit.Length > 0 ? p.Measure(shownUnit, p.SmallBold) + 4 : 0;
        var font = p.Fitting(text, main.Width - unitW, main.Height + 4, p.Huge, p.Font(26f, true), p.Big, p.Bold);
        p.Number(text, shownUnit, font, color, main, HAlign.Center, VAlign.Center);
        if (visual == "bar") p.Bar(new RectangleF(lower.X + 4, lower.Y + lower.Height / 2 - 3, lower.Width - 8, 6), frac, Past(crit) ? p.P.Danger : Past(warn) ? p.P.Warn : p.P.Accent);
        if (visual == "graph")
        {
            if (v.Now - _lastSample >= 0.25 && value is { } gv)
            {
                _lastSample = v.Now;
                _history[_head] = (float)gv;
                _head = (_head + 1) % _history.Length;
                _count = Math.Min(_history.Length, _count + 1);
            }
            if (_count > 1)
            {
                if (_points.Length != _count) _points = new PointF[_count];
                for (int i = 0; i < _count; i++)
                {
                    float val = _history[(_head - _count + i + _history.Length) % _history.Length];
                    float t = maxV > minV ? Math.Clamp((val - minV) / (maxV - minV), 0, 1) : 0.5f;
                    _points[i] = new PointF(lower.X + lower.Width * i / (_history.Length - 1), lower.Bottom - t * lower.Height);
                }
                p.G.DrawLines(p.Pen(p.P.Accent, 1.5f), _points);
            }
        }
    }
}
