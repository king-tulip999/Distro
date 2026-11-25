using System.Drawing;
using System.Globalization;
using Core.Config;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class TyresWidget : HudWidget
{
    public const string RingsLook = "rings", BarsLook = "bars", StripLook = "strip";
    private const float HeaderRoom = 27, RingsW = 280, RingsBody = 141, BarsW = 280, BarsBody = 175, StripW = 344, StripBody = 59;
    private static readonly string[] Names = { "LF", "RF", "LR", "RR" };
    private static readonly string[] WearSample = { "100%" }, PressureSample = { "888.8" };
    private static readonly string[] RingParts = { "ring", "side" }, RingSideParts = { "head", "temps" }, BarParts = { "meta", "bars" }, StripParts = { "top", "stripe", "line" };
    private readonly double?[] _temps = new double?[3], _tread = new double?[3];

    private static RectangleF Moved(RectangleF r, float dx, float dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("look", "Look", RingsLook,
            new HudChoice(RingsLook, "Rings", "Tread left as a ring around the pressure, temperatures as three chips"),
            new HudChoice(BarsLook, "Tread bars", "One bar per tread zone: its height is the tread left, its color the temperature"),
            new HudChoice(StripLook, "Strip", "One low row: tread, a heat stripe, pressure and middle temperature for each tire")),
        TitleRow,
        HudOption.Bool("pressure", "Pressures", true),
        HudOption.Bool("temps", "Temperatures", true),
        HudOption.Bool("wear", "Wear", true),
        HudOption.Int("low", "Cold below", 70, 20, 120, "°C"),
        HudOption.Int("high", "Hot above", 105, 60, 160, "°C"),
    };

    public static SizeF Natural(HudOptions o)
    {
        float head = o.Bool(TitleRow.Key) ? HeaderRoom : 0;
        return o.Choice("look") switch
        {
            BarsLook => new SizeF(BarsW, BarsBody + head),
            StripLook => new SizeF(StripW, StripBody + head),
            _ => new SizeF(RingsW, RingsBody + head),
        };
    }

    internal static Color Heat(HudPalette p, double? celsius, int low, int high)
    {
        if (celsius is not { } t || !double.IsFinite(t)) return HudPalette.Alpha(p.LineStrong, 200);
        var cold = HudPalette.Mix(p.Slate, p.Lapped, 0.5);
        if (t < low - 8) return cold;
        if (t < low) return HudPalette.Mix(cold, p.Good, (t - (low - 8)) / 8);
        if (t < high - 10) return p.Good;
        if (t < high) return HudPalette.Mix(p.Good, p.Warn, (t - (high - 10)) / 10);
        if (t < high + 5) return HudPalette.Mix(p.Warn, p.Danger, (t - high) / 5);
        return p.Danger;
    }

    internal static double? Lowest(double? a, double? b, double? c)
    {
        double? low = null;
        if (a is { } x && double.IsFinite(x)) low = x;
        if (b is { } y && double.IsFinite(y) && (low is null || y < low)) low = y;
        if (c is { } z && double.IsFinite(z) && (low is null || z < low)) low = z;
        return low;
    }

    private static Color WearColor(HudPainter p, double left) => left < 0.5 ? p.P.DangerText : left < 0.75 ? p.P.WarnText : p.P.GoodText;

    private static string WearText(double? left) => left is { } w ? PooledText.Number("", w * 100, "0", CultureInfo.InvariantCulture, "%") : RaceFormat.Missing;

    private static string TempNumber(double? celsius, HudUnits u, string suffix = "") =>
        celsius is { } c && double.IsFinite(c) ? PooledText.Number("", u == HudUnits.Imperial ? c * 9 / 5 + 32 : c, "0", CultureInfo.CurrentCulture, suffix) : RaceFormat.Missing;

    private static string PressureNumber(double? kpa, HudUnits u) =>
        kpa is { } k && double.IsFinite(k) && k > 0
            ? PooledText.Number("", u == HudUnits.Imperial ? k * 0.145038 : k, u == HudUnits.Imperial ? "0.0" : "0", CultureInfo.CurrentCulture)
            : RaceFormat.Missing;

    private static string PressureUnit(HudUnits u) => u == HudUnits.Imperial ? Units.Psi : Units.KPa;

    private void Fill(TelemetryFrame f, int i)
    {
        bool left = i % 2 == 0;
        _temps[0] = left ? f.TyreTempOuter[i] : f.TyreTempInner[i];
        _temps[1] = f.TyreTempMiddle[i];
        _temps[2] = left ? f.TyreTempInner[i] : f.TyreTempOuter[i];
        _tread[0] = left ? f.TyreWearOuter[i] : f.TyreWearInner[i];
        _tread[1] = f.TyreWearMiddle[i];
        _tread[2] = left ? f.TyreWearInner[i] : f.TyreWearOuter[i];
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Tires")) return;
        var f = v.Frame;
        var area = p.Header("Tires", "from the pit box");
        if (f.TyreColdPressure.All(x => x is null) && f.TyreTempMiddle.All(x => x is null) && f.TyreWearMiddle.All(x => x is null))
        {
            p.Text("This car doesn't report tire data", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        switch (v.Options.Choice("look"))
        {
            case BarsLook: DrawBars(p, v, f, area); break;
            case StripLook: DrawStrip(p, v, f, area); break;
            default: DrawRings(p, v, f, area); break;
        }
    }

    private void DrawRings(HudPainter p, HudView v, TelemetryFrame f, RectangleF area)
    {
        float d = p.Density, gapX = 6 * d, gapY = 5 * d;
        float tw = (area.Width - gapX) / 2, th = (area.Height - gapY) / 2;
        int low = v.Options.Int("low"), high = v.Options.Int("high");
        bool pressure = v.Options.Bool("pressure"), temps = v.Options.Bool("temps"), wearOn = v.Options.Bool("wear");
        var unitFont = p.Font(8f, false);
        var firstInner = new RectangleF(area.X, area.Y, tw, th).Inflated(-5 * d, -5 * d);
        float s = Math.Min(Math.Min(firstInner.Height, 52 * d), firstInner.Width * 0.42f);
        float headH = p.LineHeight(p.SmallBold), pillH = p.LineHeight(p.Label) + 4 * d, between = 5 * d;
        float used = headH + (temps ? between + pillH : 0);
        Span<RectangleF> rowBoxes = stackalloc RectangleF[2];
        Span<RectangleF> sideBoxes = stackalloc RectangleF[2];
        Span<float> rowSizes = stackalloc float[2];
        Span<float> sideSizes = stackalloc float[2];
        rowSizes[0] = s;
        p.Arrange(v, "tyres.rings", firstInner, true, 7 * d, RingParts, rowSizes, rowBoxes);
        var firstSide = rowBoxes[1];
        sideSizes[0] = headH;
        sideSizes[1] = temps ? pillH : -1;
        p.Arrange(v, "tyres.rings.side", new RectangleF(firstSide.X, firstSide.Y + (firstSide.Height - used) / 2, firstSide.Width, used), false, between, RingSideParts, sideSizes, sideBoxes);
        for (int i = 0; i < 4; i++)
        {
            float dx = i % 2 * (tw + gapX), dy = i / 2 * (th + gapY);
            var box = new RectangleF(area.X + dx, area.Y + dy, tw, th);
            p.Tile(box);
            Fill(f, i);
            double? wear = wearOn ? Lowest(_tread[0], _tread[1], _tread[2]) : null;

            var ringBox = Moved(rowBoxes[0], dx, dy);
            var ring = new RectangleF(ringBox.X, ringBox.Y + (ringBox.Height - s) / 2, s, s);
            float stroke = Math.Max(3, s * 0.08f);
            var arc = ring.Inflated(-stroke / 2 - 1, -stroke / 2 - 1);
            bool drawRing = s >= 24 * d;
            if (drawRing) p.G.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.LineStrong, 200), stroke), arc);
            if (drawRing && wear is { } w && w > 0.005)
            {
                var pen = p.Pen(WearColor(p, w), stroke);
                if (w >= 0.995) p.G.DrawEllipse(pen, arc);
                else p.G.DrawArc(pen, arc, -90, (float)Math.Max(2, 360 * w));
            }
            if (pressure && drawRing)
            {
                float nh = p.LineHeight(p.Bold), uh = p.LineHeight(unitFont), total = nh + uh - 3;
                float y0 = ring.Y + (ring.Height - total) / 2;
                p.Text(PressureNumber(f.TyreColdPressure[i], v.Units), p.Bold, p.P.Heading, new RectangleF(ring.X - 4, y0, ring.Width + 8, nh), HAlign.Center, VAlign.Center, false);
                p.Text(PressureUnit(v.Units), unitFont, p.P.Faint, new RectangleF(ring.X, y0 + nh - 3, ring.Width, uh), HAlign.Center, VAlign.Center, false);
            }

            var head = Moved(sideBoxes[0], dx, dy);
            p.Text(Names[i], p.SmallBold, p.P.AccentText, head, HAlign.Left, VAlign.Center, false);
            if (wearOn) p.Number(WearText(wear), p.SmallBold, wear is { } ww ? WearColor(p, ww) : p.P.Faint, head, HAlign.Right, VAlign.Center);
            if (!temps) continue;
            var row = Moved(sideBoxes[1], dx, dy);
            float gap = 3 * d, cw = (row.Width - gap * 2) / 3;
            for (int k = 0; k < 3; k++)
            {
                var cell = new RectangleF(row.X + k * (cw + gap), row.Y, cw, row.Height);
                p.Chip(cell, Heat(p.P, _temps[k], low, high));
                p.Text(TempNumber(_temps[k], v.Units), p.Label, p.P.Heading, cell, HAlign.Center, VAlign.Center, false);
            }
        }
    }

    private void DrawBars(HudPainter p, HudView v, TelemetryFrame f, RectangleF area)
    {
        float d = p.Density, gapX = 6 * d, gapY = 5 * d;
        float tw = (area.Width - gapX) / 2, th = (area.Height - gapY) / 2;
        int low = v.Options.Int("low"), high = v.Options.Int("high");
        bool pressure = v.Options.Bool("pressure"), temps = v.Options.Bool("temps"), wearOn = v.Options.Bool("wear");
        float metaW = Math.Max(p.Measure(WearSample[0], p.Bold), p.Measure(PressureSample[0], p.Small)) + 2;
        float headH = p.LineHeight(p.SmallBold), wearH = p.LineHeight(p.Bold), lineH = p.LineHeight(p.Small);
        var track = HudPalette.Alpha(p.P.LineStrong, 120);
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "tyres.bars", new RectangleF(area.X, area.Y, tw, th).Inflated(-6 * d, -5 * d), true, 7 * d, BarParts, stackalloc float[] { metaW, 0 }, parts);
        for (int i = 0; i < 4; i++)
        {
            float dx = i % 2 * (tw + gapX), dy = i / 2 * (th + gapY);
            var box = new RectangleF(area.X + dx, area.Y + dy, tw, th);
            p.Tile(box);
            Fill(f, i);
            double? wear = wearOn ? Lowest(_tread[0], _tread[1], _tread[2]) : null;

            var meta = Moved(parts[0], dx, dy);
            p.Text(Names[i], p.SmallBold, p.P.AccentText, new RectangleF(meta.X, meta.Y, meta.Width, headH), HAlign.Left, VAlign.Center, false);
            if (wearOn) p.Number(WearText(wear), p.Bold, wear is { } ww ? WearColor(p, ww) : p.P.Faint, new RectangleF(meta.X, meta.Y + headH, meta.Width, wearH), HAlign.Left, VAlign.Center);
            float unitH = p.LineHeight(p.Label), room = meta.Height - headH - wearH;
            if (pressure && room >= lineH * 0.8f)
            {
                bool unit = room >= (lineH + unitH) * 0.8f;
                float numberY = unit ? meta.Bottom - (lineH + unitH) * 0.8f : meta.Bottom - lineH * 0.8f;
                p.Text(PressureNumber(f.TyreColdPressure[i], v.Units), p.Small, p.P.Muted, new RectangleF(meta.X, numberY - lineH * 0.1f, meta.Width, lineH), HAlign.Left, VAlign.Center, false);
                if (unit) p.Text(PressureUnit(v.Units), p.Label, p.P.Faint, new RectangleF(meta.X, meta.Bottom - unitH * 0.9f, meta.Width, unitH), HAlign.Left, VAlign.Center, false);
            }

            var bars = Moved(parts[1], dx, dy);
            float gap = 3 * d, cw = (bars.Width - gap * 2) / 3, radius = p.CornerRadius(3);
            float textH = p.LineHeight(p.Label) + 4 * d;
            for (int k = 0; k < 3; k++)
            {
                var col = new RectangleF(bars.X + k * (cw + gap), bars.Y, cw, bars.Height);
                p.Fill(col, track, radius);
                var heat = temps ? Heat(p.P, _temps[k], low, high) : p.P.Slate;
                double left = !wearOn ? 1 : _tread[k] is { } t && double.IsFinite(t) ? Math.Clamp(t, 0, 1) : 0;
                float h = (float)(col.Height * left);
                if (h > 0.5f) p.Fill(new RectangleF(col.X, col.Bottom - h, col.Width, h), heat, radius);
                if (!temps) continue;
                var label = new RectangleF(col.X, col.Y + 2 * d, col.Width, textH - 2 * d);
                var ink = col.Bottom - h < label.Bottom ? HudPalette.On(heat) : p.P.Heading;
                p.Text(TempNumber(_temps[k], v.Units), p.Label, ink, label, HAlign.Center, VAlign.Center, false);
            }
        }
    }

    private void DrawStrip(HudPainter p, HudView v, TelemetryFrame f, RectangleF area)
    {
        float d = p.Density, cw = area.Width / 4;
        int low = v.Options.Int("low"), high = v.Options.Int("high");
        bool pressure = v.Options.Bool("pressure"), temps = v.Options.Bool("temps"), wearOn = v.Options.Bool("wear");
        float topH = p.LineHeight(p.Bold), stripeH = 6 * d, lineH = p.LineHeight(p.Small), between = 4 * d;
        float used = topH + (temps ? between + stripeH : 0) + (pressure || temps ? between + lineH : 0);
        float y = area.Y + Math.Max(0, (area.Height - used) / 2);
        var firstInner = new RectangleF(area.X, area.Y, cw, area.Height).Inflated(-7 * d, 0);
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "tyres.strip", new RectangleF(firstInner.X, y, firstInner.Width, used), false, between, StripParts,
            stackalloc float[] { topH, temps ? stripeH : -1, pressure || temps ? lineH : -1 }, parts);
        for (int i = 0; i < 4; i++)
        {
            float dx = i * cw;
            var col = new RectangleF(area.X + dx, area.Y, cw, area.Height);
            if (i > 0) p.Divider(col.X, area.Y + 2 * d, area.Bottom - 2 * d);
            Fill(f, i);
            double? wear = wearOn ? Lowest(_tread[0], _tread[1], _tread[2]) : null;

            var top = Moved(parts[0], dx, 0);
            p.Text(Names[i], p.SmallBold, p.P.AccentText, top, HAlign.Left, VAlign.Center, false);
            if (wearOn) p.Number(WearText(wear), p.Bold, wear is { } ww ? WearColor(p, ww) : p.P.Faint, top, HAlign.Right, VAlign.Center);
            if (temps)
            {
                var stripe = Moved(parts[1], dx, 0);
                float gap = 2 * d, sw = (stripe.Width - gap * 2) / 3;
                for (int k = 0; k < 3; k++)
                    p.Fill(new RectangleF(stripe.X + k * (sw + gap), stripe.Y, sw, stripe.Height), Heat(p.P, _temps[k], low, high), p.CornerRadius(2));
            }
            if (!pressure && !temps) continue;
            var line = Moved(parts[2], dx, 0);
            if (pressure) p.Text(PressureNumber(f.TyreColdPressure[i], v.Units), p.Small, p.P.Muted, line, HAlign.Left, VAlign.Center, false);
            if (temps) p.Text(TempNumber(_temps[1], v.Units, "°"), p.Small, p.P.Muted, line, HAlign.Right, VAlign.Center, false);
        }
    }
}
