using System.Drawing;
using System.Globalization;
using Core.SimRacing.Race;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class InputsWidget
{
    private const float DialRow = 96, DialStrip = 26, DialGap = 6, DialTraceW = 270, DialBarW = 14, DialBarGap = 6, DialNumbers = 14, DialRpmH = 10, DialRpmGap = 6, DialRpmMinW = 160;
    private const float SteerMarkSweep = 30;
    private const int DialLights = 15;

    private readonly List<(string Label, string Value, Color? Color)> _extras = new();
    private readonly List<string> _extraIds = new();

    internal static float DialPedalsW(Shows on) => on.AnyPedal ? on.Pedals * (DialBarW + DialBarGap) - DialBarGap : 0;

    internal static SizeF DialSize(Shows on, bool extras)
    {
        float h = DialRow + (extras ? DialGap + DialStrip : 0);
        float pedals = DialPedalsW(on), trace = on.TraceGraph ? DialTraceW : 0;
        float content = pedals + trace + (pedals > 0 && trace > 0 ? 8 : 0);
        if (on.Rpm) content = Math.Max(content, DialRpmMinW);
        if (extras) content = Math.Max(content, 240);
        float dial = on.GearDial ? h + (content > 0 ? 8 : 0) : 0;
        return new SizeF(Math.Max(60, dial + content), h);
    }

    private void DrawDial(HudPainter p, HudView v, in Live live, bool traceOnly)
    {
        var extras = traceOnly ? Array.Empty<string>() : v.Options.Columns("extras");
        var size = traceOnly ? BareSize(_on) : DialSize(_on, extras.Count > 0);
        Fit(p, size.Width, size.Height);
        float contentW = _on.GearDial ? Math.Max(0, size.Width - size.Height - 8) : size.Width;
        if (_on.GearDial) GearDial(p, v, live, At(size.Width - size.Height, 0, size.Height, size.Height));
        bool rpmBar = !traceOnly && _rpm;
        float top = rpmBar ? DialRpmH + DialRpmGap : 0, rowH = DialRow - top;
        if (rpmBar)
        {
            var bar = At(0, 0, contentW, DialRpmH);
            p.Panel(bar);
            ShiftLights(p, live, bar.Inflated(-S(2), -S(2)), DialLights, S(3));
        }
        float x = 0;
        if (_on.TraceGraph)
        {
            var trace = At(x, top, DialTraceW, rowH);
            p.Panel(trace);
            var inner = trace.Inflated(-S(1), -S(1));
            for (int i = 1; i < 4; i++)
            {
                float gy = MathF.Round(inner.Y + inner.Height * i / 4) + 0.5f;
                p.Line(inner.X, gy, inner.Right, gy, HudPalette.Alpha(p.P.InputTicks, 120), 1);
            }
            DialGraph(p, inner, live.Now);
            x += DialTraceW + 8;
        }
        float pedalsW = traceOnly ? 0 : DialPedalsW(_on);
        if (pedalsW > 0) DialPedals(p, live, At(x, top, pedalsW, rowH));
        if (extras.Count == 0) return;
        var strip = At(0, DialRow + DialGap, contentW, DialStrip);
        p.Panel(strip);
        _extras.Clear();
        _extraIds.Clear();
        foreach (var id in extras)
        {
            if (HudChannels.Find(id) is not { } channel) continue;
            _extras.Add((ExtraLabel(id, channel.Label), HudChannels.Format(channel, v, withUnit: true), null));
            _extraIds.Add(id);
        }
        p.Strip(strip, _extras, "extras", _extraIds);
    }

    private static string ExtraLabel(string id, string label) => id switch
    {
        "air" => "Air", "track" => "Track", "best" => "Best", "last" => "Last", "bias" => "Bias", "tc" => "TC", "abs" => "ABS",
        "fuelPct" => "Fuel", "incidents" => "Inc", "lapNumber" => "Lap", "lapsLeft" => "Left", "position" => "Pos", "rpm" => "RPM",
        "timeLeft" => "Time", "speed" => "Speed", "steer" => "Steer", "wetness" => "Wet", "battery" => "Batt", "deltaBest" => "Delta",
        _ => label,
    };

    private void DialGraph(HudPainter p, RectangleF r, double now)
    {
        if (_throttle) Area(p, r, now, TraceSeconds, 0, HudPalette.Alpha(p.P.ThrottleInput, 55));
        if (_brake) Area(p, r, now, TraceSeconds, 1, HudPalette.Alpha(p.P.BrakeInput, 80));
        if (_steering) Lines(p, r, now, TraceSeconds, 3, HudPalette.Alpha(p.P.SteerInput, 170), null, S(1.2f));
        if (_clutch) Lines(p, r, now, TraceSeconds, 2, p.P.ClutchInput, null, S(1.5f));
        if (_brake) Lines(p, r, now, TraceSeconds, 1, p.P.BrakeInput, _abs ? p.P.AbsInput : null, S(1.8f));
        if (_throttle) Lines(p, r, now, TraceSeconds, 0, p.P.ThrottleInput, null, S(1.8f));
    }

    private void GearDial(HudPainter p, HudView v, in Live live, RectangleF r)
    {
        p.G.FillEllipse(p.Brush(HudPalette.Alpha(p.PanelColor, Math.Max(150, p.PanelAlpha))), r);
        p.G.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.LineStrong, 140), 1), r.Inflated(-0.5f, -0.5f));
        var face = r.Inflated(-S(9), -S(9));
        p.G.FillEllipse(p.Brush(p.P.TraceBack), face);
        if (_steering && live.SteerDeg is { } steer)
            p.G.DrawArc(p.Pen(p.P.Text, S(3)), r.Inflated(-S(4.5f), -S(4.5f)), 270 + (float)steer - SteerMarkSweep / 2, SteerMarkSweep);
        float d = face.Height;
        if (_gear)
        {
            float px = S(_speed ? 40 : 48);
            float gh = p.DisplaySize("8", px).Height;
            float gy = _speed ? face.Y + d * 0.44f - gh / 2 : face.Y + (d - gh) / 2;
            p.Display(GearText(v.Frame.Gear), px, GearColor(p, live), new RectangleF(face.X, gy, face.Width, gh), HAlign.Center, VAlign.Center);
            var (before, after) = NeighbourGears(v.Frame.Gear, v.Frame.Session.ForwardGears);
            float sidePx = px * 0.4f, sideH = p.DisplaySize("8", sidePx).Height, sideW = d * 0.24f, sideY = gy + (gh - sideH) / 2;
            var side = HudPalette.Alpha(p.P.Muted, 170);
            if (before is not null) p.Display(before, sidePx, side, new RectangleF(face.X + d * 0.2f - sideW / 2, sideY, sideW, sideH), HAlign.Center, VAlign.Center);
            if (after is not null) p.Display(after, sidePx, side, new RectangleF(face.X + d * 0.8f - sideW / 2, sideY, sideW, sideH), HAlign.Center, VAlign.Center);
        }
        if (_speed)
        {
            var font = p.Font(S(_gear ? 11 : 18), true);
            float lh = p.LineHeight(font);
            float sy = _gear ? face.Y + d * 0.70f : face.Y + (d - lh) / 2;
            string value = v.Frame.Speed is { } ms ? PooledText.Number("", RaceFormat.SpeedValue(ms, v.Units), "0", CultureInfo.InvariantCulture) : RaceFormat.Missing;
            p.Text(value, font, p.P.Muted, new RectangleF(face.X, sy, face.Width, lh), HAlign.Center, VAlign.Center, false);
        }
    }

    private void DialPedals(HudPainter p, in Live live, RectangleF r)
    {
        var font = p.Font(S(11), true);
        float numbers = S(DialNumbers);
        float x = r.X;
        if (_clutch) One(live.Clutch, p.P.ClutchInput);
        if (_brake) One(live.Brake, BrakeColor(p, live));
        if (_throttle) One(live.Throttle, p.P.ThrottleInput);

        void One(double? value, Color color)
        {
            var label = new RectangleF(x - S(4), r.Y, S(DialBarW + 8), numbers);
            p.Text(value is { } pv ? PooledText.Number("", (long)Math.Round(pv * 100)) : RaceFormat.Missing, font, p.P.Heading, label, HAlign.Center, VAlign.Center, false);
            var bar = new RectangleF(x, r.Y + numbers + S(2), S(DialBarW), r.Height - numbers - S(2));
            p.Panel(bar);
            if (value is { } fv && fv > 0)
            {
                var inner = bar.Inflated(-S(2), -S(2));
                p.Fill(RectangleF.FromLTRB(inner.Left, inner.Bottom - (float)fv * inner.Height, inner.Right, inner.Bottom), color, Round(p, S(3)));
            }
            x += S(DialBarW + DialBarGap);
        }
    }
}
