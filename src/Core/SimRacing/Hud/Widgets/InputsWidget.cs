using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class InputsWidget : HudWidget
{
    public const string Trace = "trace", Lights = "lights", Bare = "bare";
    private const double TraceSeconds = 6, LimiterHz = 10, AbsHz = 4;
    private const int TraceLights = 20, BarLights = 15;
    private const float TraceH = 84, RightW = 90, BarW = 470, BarH = 66, SpeedW = 80;
    private const int Capacity = 640;
    private const float GearTileW = 54;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Trace,
            new HudChoice(Trace, "Trace", "A pedal and steering trace beside the pedal bars, gear, speed and RPM, shift lights across the top"),
            new HudChoice(Lights, "Shift-light bar", "One low row: gear, a wide row of shift lights, pedal and steering bars, speed and RPM"),
            new HudChoice(Bare, "Trace only", "Trace, gear and speed straight on the game, no panel")),
        HudOption.Set("show", "Shows", "throttle,brake,clutch,steering,gear,speed,rpm,trace,abs", null,
            new HudChoice("throttle", "Throttle"), new HudChoice("brake", "Brake"), new HudChoice("clutch", "Clutch", "Pedal travel: pressed shows full"),
            new HudChoice("steering", "Steering"), new HudChoice("gear", "Gear"), new HudChoice("speed", "Speed"), new HudChoice("rpm", "RPM", "Shift lights and the RPM number"),
            new HudChoice("trace", "Input trace", "Trace only"),
            new HudChoice("abs", "ABS flash", "While ABS works the gear flashes amber and the brake turns amber")),
    };

    internal readonly record struct Shows(bool Throttle, bool Brake, bool Clutch, bool Steering, bool Speed, bool Rpm, bool Trace, bool Abs, bool Gear)
    {
        public bool AnyPedal => Throttle || Brake || Clutch;
        public int Pedals => (Clutch ? 1 : 0) + (Brake ? 1 : 0) + (Throttle ? 1 : 0);
        public bool TraceGraph => Trace && (AnyPedal || Steering);
        public bool SteerBar => Steering && !TraceGraph;
        public float PedalsW => Math.Max(AnyPedal ? Pedals * 17 - 5 : 0, SteerBar ? 46 : 0);
        public float TraceW => 8 + (TraceGraph ? 250 + 10 : 0) + (PedalsW > 0 ? PedalsW + 8 : 0) + (Gear || Speed || Rpm ? RightW + 8 : 0);
        public float TraceHeight => (Rpm ? 18 : 8) + TraceH + 8;
    }

    internal static Shows Parse(HudOptions o)
    {
        bool t = false, b = false, c = false, st = false, sp = false, r = false, tr = false, a = false, g = false;
        var on = o.Columns("show");
        for (int i = 0; i < on.Count; i++)
            switch (on[i])
            {
                case "throttle": t = true; break;
                case "brake": b = true; break;
                case "clutch": c = true; break;
                case "steering": st = true; break;
                case "speed": sp = true; break;
                case "rpm": r = true; break;
                case "trace": tr = true; break;
                case "abs": a = true; break;
                case "gear": g = true; break;
            }
        return new Shows(t, b, c, st, sp, r, tr, a, g);
    }

    public static SizeF Natural(HudOptions o)
    {
        var on = Parse(o);
        return o.Choice("style") switch
        {
            Lights => new SizeF(BarW - (on.Speed || on.Rpm ? 0 : SpeedW - 12) - (on.Gear ? 0 : GearTileW), BarH),
            Bare => BareSize(on),
            _ => new SizeF(on.TraceW, on.TraceHeight),
        };
    }

    private struct Sample
    {
        public double At;
        public float Throttle, Brake, Clutch, Steer;
        public bool Abs;
    }

    private readonly Sample[] _ring = new Sample[Capacity];
    private int _head, _count;
    private long _seq = long.MinValue;
    private double _peakRpm;
    private Shows _on;
    private bool _throttle, _brake, _clutch, _steering, _speed, _rpm, _abs, _gear;
    private readonly GraphicsPath _path = new(), _absPath = new();

    public override void Dispose()
    {
        _path.Dispose();
        _absPath.Dispose();
    }
    private float _k = 1;

    private readonly record struct RpmScale(double Min, double Max, double First, double Shift, double Blink, double ZoneFrom);

    private readonly record struct Live(double? Throttle, double? Brake, double? Clutch, double? SteerDeg, double SteerFrac,
        double? Rpm, RpmScale Scale, bool Limiter, bool Abs, double Now);

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        ReadShows(v.Options);
        var live = Read(v);
        Record(v, live);
        switch (v.Options.Choice("style"))
        {
            case Bare: DrawBare(p, v, live); return;
        }
        p.Panel();
        if (v.Options.Choice("style") == Lights) DrawLights(p, v, live);
        else DrawTrace(p, v, live);
    }

    private void ReadShows(HudOptions o)
    {
        _on = Parse(o);
        (_throttle, _brake, _clutch, _steering, _speed, _rpm, _abs, _gear) = (_on.Throttle, _on.Brake, _on.Clutch, _on.Steering, _on.Speed, _on.Rpm, _on.Abs, _on.Gear);
    }

    private int PedalCount => _on.Pedals;

    internal static double? Pedal(double? raw, double? cooked) => (raw ?? cooked) is { } x ? Math.Clamp(x, 0, 1) : null;

    internal static double? ClutchTravel(double? raw, double? cooked) => Pedal(raw, cooked) is { } engaged ? 1 - engaged : null;

    internal static double? SteerDegrees(double? radians) => radians is { } r ? -r * 180 / Math.PI : null;

    internal static double HalfLockDegrees(double? lockToLockRadians) => lockToLockRadians is > 0 and var m ? m * 90 / Math.PI : 450;

    internal static string Angle(double? degrees) => degrees is not { } d ? RaceFormat.Missing
        : Math.Abs(d) < 2 ? "0°"
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(d):0}° {(d > 0 ? "R" : "L")}");

    internal static string GearText(int? gear) => gear switch { null => RaceFormat.Missing, < 0 => "R", 0 => "N", _ => gear.Value.ToString(CultureInfo.InvariantCulture) };

    private Live Read(HudView v)
    {
        var f = v.Frame;
        double? steer = SteerDegrees(f.Steering);
        double half = HalfLockDegrees(f.SteeringMax);
        var scale = Scale(f);
        bool limiter = f.Rpm is { } rpm && rpm >= scale.Blink;
        double? brake = Pedal(f.BrakeRaw, f.Brake);
        bool abs = _abs && f.AbsActive == true && brake is > 0.01;
        return new Live(Pedal(f.ThrottleRaw, f.Throttle), brake, ClutchTravel(f.ClutchRaw, f.Clutch), steer,
            steer is { } d ? Math.Clamp(d / half, -1, 1) : 0, f.Rpm, scale, limiter, abs, v.Now);
    }

    private RpmScale Scale(TelemetryFrame f)
    {
        var s = f.Session;
        if (f.Rpm is { } now && now > _peakRpm) _peakRpm = now;
        double shift = s.ShiftRpm ?? s.ShiftLightFirstRpm ?? 0, blink = s.BlinkRpm ?? 0, redline = s.RedlineRpm ?? 0;
        double max = Math.Max(redline, Math.Max(blink, shift));
        if (max <= 0) max = Math.Max(6000, Math.Ceiling(_peakRpm / 1000) * 1000);
        double min = s.IdleRpm is { } idle && idle < max * 0.6 ? idle : 0;
        double b = blink > 0 ? blink : redline > 0 ? redline : max;
        double sh = shift > 0 ? Math.Min(shift, max) : b;
        double zone = redline > 0 ? Math.Min(b, redline) : b;
        double first = s.ShiftLightFirstRpm is { } fl && fl < sh ? fl : min;
        return new RpmScale(min, max, first, sh, Math.Min(b, max), Math.Min(zone, max));
    }

    private void Record(HudView v, in Live live)
    {
        var f = v.Frame;
        if (f.Sequence == _seq) return;
        _seq = f.Sequence;
        if (f.Simulated && _count == 0) Backfill(v);
        Push(live.Now, live.Throttle ?? 0, live.Brake ?? 0, live.Clutch ?? 0, live.SteerFrac, live.Abs);
    }

    private void Push(double at, double throttle, double brake, double clutch, double steer, bool abs)
    {
        _ring[_head] = new Sample { At = at, Throttle = (float)throttle, Brake = (float)brake, Clutch = (float)clutch, Steer = (float)steer, Abs = abs };
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    private void Backfill(HudView v)
    {
        if (v.Frame.SessionTime is not { } now) return;
        var demo = new DemoTelemetry();
        var past = new TelemetryFrame();
        double step = 1.0 / 30;
        for (double back = TraceSeconds; back > step / 2; back -= step)
        {
            demo.FillAt(past, now - back);
            double? brake = Pedal(past.BrakeRaw, past.Brake);
            Push(v.Now - back, Pedal(past.ThrottleRaw, past.Throttle) ?? 0, brake ?? 0, ClutchTravel(past.ClutchRaw, past.Clutch) ?? 0,
                SteerDegrees(past.Steering) is { } d ? Math.Clamp(d / HalfLockDegrees(past.SteeringMax), -1, 1) : 0,
                _abs && past.AbsActive == true && brake is > 0.01);
        }
    }

    private float S(float px) => px * _k;

    private static bool FlashOn(HudPainter p, double now, double hz) => p.ReducedMotion || ((long)Math.Floor(now * hz * 2) & 1) == 0;

    private Color GearColor(HudPainter p, in Live live)
    {
        if (live.Abs) return FlashOn(p, live.Now, AbsHz) ? p.P.AbsInput : p.P.Heading;
        return live.Limiter ? p.P.Danger : p.P.Heading;
    }

    private Color BrakeColor(HudPainter p, in Live live) => live.Abs ? p.P.AbsInput : p.P.BrakeInput;

    private void Graph(HudPainter p, RectangleF r, double now, double window, bool steering)
    {
        p.Fill(r, p.P.TraceBack, Round(p, S(6)));
        if (steering) p.Fill(new RectangleF(r.X, r.Y + r.Height / 2 - 0.5f, r.Width, 1), p.P.InputTicks);
        float thick = steering ? 2 : 1.5f;
        if (_throttle) Area(p, r, now, window, 0, HudPalette.Alpha(p.P.ThrottleInput, 70));
        if (_brake) Area(p, r, now, window, 1, HudPalette.Alpha(p.P.BrakeInput, 95));
        if (steering && _steering) Lines(p, r, now, window, 3, HudPalette.Alpha(p.P.SteerInput, 180), null, S(1.2f));
        if (_clutch) Lines(p, r, now, window, 2, p.P.ClutchInput, null, S(1.5f));
        if (_brake) Lines(p, r, now, window, 1, p.P.BrakeInput, _abs ? p.P.AbsInput : null, S(thick));
        if (_throttle) Lines(p, r, now, window, 0, p.P.ThrottleInput, null, S(thick));
    }

    private void Area(HudPainter p, RectangleF r, double now, double window, int channel, Color color) => Area(p, r, now, window, channel, p.Brush(color));

    private void Area(HudPainter p, RectangleF r, double now, double window, int channel, Brush brush)
    {
        _path.Reset();
        float from = r.Bottom, span = r.Height - 1;
        int start = (_head - _count + Capacity) % Capacity;
        bool open = false;
        PointF last = default;
        for (int n = 0; n < _count; n++)
        {
            ref readonly var s = ref _ring[(start + n) % Capacity];
            double age = now - s.At;
            if (age > window || age < -0.5)
            {
                if (open) { _path.AddLine(last, new PointF(last.X, from)); _path.CloseFigure(); open = false; }
                continue;
            }
            var pt = new PointF(r.Right - (float)(age / window) * r.Width, from - Value(s, channel) * span);
            if (!open)
            {
                _path.StartFigure();
                _path.AddLine(new PointF(pt.X, from), pt);
                open = true;
            }
            else _path.AddLine(last, pt);
            last = pt;
        }
        if (open) { _path.AddLine(last, new PointF(last.X, from)); _path.CloseFigure(); }
        if (_path.PointCount > 2) p.G.FillPath(brush, _path);
    }

    private static float Value(in Sample s, int channel) => channel switch { 0 => s.Throttle, 1 => s.Brake, 2 => s.Clutch, _ => s.Steer };

    private void Lines(HudPainter p, RectangleF r, double now, double window, int channel, Color color, Color? absColor, float width)
    {
        _path.Reset();
        _absPath.Reset();
        float inset = Math.Max(1, width), from = r.Bottom - inset, span = r.Height - inset * 2, mid = r.Y + r.Height / 2;
        int start = (_head - _count + Capacity) % Capacity;
        PointF last = default;
        bool have = false, lastAbs = false, normalOpen = false, absOpen = false;
        for (int n = 0; n < _count; n++)
        {
            ref readonly var s = ref _ring[(start + n) % Capacity];
            double age = now - s.At;
            if (age > window || age < -0.5) { have = false; continue; }
            float x = r.Right - (float)(age / window) * r.Width;
            float val = Value(s, channel);
            float y = channel == 3 ? mid - val * (r.Height / 2 - inset) : from - val * span;
            var pt = new PointF(x, y);
            if (have)
            {
                bool abs = absColor is not null && (s.Abs || lastAbs);
                var path = abs ? _absPath : _path;
                ref bool open = ref abs ? ref absOpen : ref normalOpen;
                ref bool other = ref abs ? ref normalOpen : ref absOpen;
                if (!open) path.StartFigure();
                path.AddLine(last, pt);
                open = true;
                other = false;
            }
            else
            {
                normalOpen = absOpen = false;
            }
            last = pt;
            lastAbs = s.Abs;
            have = true;
        }
        if (_path.PointCount > 1) p.G.DrawPath(p.Pen(color, width), _path);
        if (absColor is { } ac && _absPath.PointCount > 1) p.G.DrawPath(p.Pen(ac, width), _absPath);
    }

    private void VerticalBar(HudPainter p, RectangleF r, double? value, Color fill)
    {
        float radius = Round(p, r.Width / 2);
        p.Fill(r, p.P.InputTrack, radius);
        if (value is { } x && x > 0) p.Fill(RectangleF.FromLTRB(r.Left, r.Bottom - (float)x * r.Height, r.Right, r.Bottom), fill, Math.Min(radius, (float)x * r.Height / 2));
    }

    private void HorizontalBar(HudPainter p, RectangleF r, double? value, Color fill)
    {
        float radius = Round(p, r.Height / 2);
        p.Fill(r, p.P.InputTrack, radius);
        if (value is { } x && x > 0) p.Fill(new RectangleF(r.X, r.Y, (float)x * r.Width, r.Height), fill, Math.Min(radius, (float)x * r.Width / 2));
    }

    private void SteerBar(HudPainter p, RectangleF r, in Live live, bool overhang = true)
    {
        p.Fill(r, p.P.InputTrack, Round(p, r.Height / 2));
        float cx = r.X + r.Width / 2, end = cx + (float)live.SteerFrac * r.Width / 2;
        if (live.SteerDeg is not null && Math.Abs(end - cx) > 0.25f) p.Fill(RectangleF.FromLTRB(Math.Min(cx, end), r.Top, Math.Max(cx, end), r.Bottom), p.P.SteerInput, Round(p, r.Height / 2));
        float over = overhang ? S(2) : 0;
        p.Fill(new RectangleF(cx - S(1), r.Y - over, S(2), r.Height + over * 2), p.P.Accent);
    }

    private static string RpmText(double? rpm) => rpm is { } x ? PooledText.Number("", x, "0", CultureInfo.InvariantCulture, " RPM") : RaceFormat.Missing + " RPM";

    private static readonly string[] SpeedSamples = { "888" };

    private void SpeedLine(HudPainter p, HudView v, RectangleF r, Font number, Font unitFont)
    {
        string value = v.Frame.Speed is { } ms ? PooledText.Number("", RaceFormat.SpeedValue(ms, v.Units), "0", CultureInfo.InvariantCulture) : RaceFormat.Missing;
        string unit = RaceFormat.SpeedUnit(v.Units);
        float nw = p.Widest(SpeedSamples, number), uw = p.Measure(unit, unitFont), gap = S(3);
        float x = r.X + (r.Width - nw - gap - uw) / 2;
        p.Text(value, number, p.P.Text, new RectangleF(x, r.Y, nw, r.Height), HAlign.Right, VAlign.Center, false);
        float drop = (p.LineHeight(number) - p.LineHeight(unitFont)) * 0.3f;
        p.Text(unit, unitFont, p.P.Muted, new RectangleF(x + nw + gap, r.Y + drop, uw + 2, r.Height), HAlign.Left, VAlign.Center, false);
    }

    internal static int LitLights(double? rpm, double first, double shift, int count)
    {
        if (rpm is not { } r || count <= 0) return 0;
        if (r >= shift) return count;
        if (shift <= first) return 0;
        return Math.Clamp((int)Math.Floor((r - first) * count / (shift - first)), 0, count);
    }

    private void ShiftLights(HudPainter p, in Live live, RectangleF r, int count, float gap)
    {
        var sc = live.Scale;
        int lit = LitLights(live.Rpm, sc.First, sc.Shift, count);
        bool shifting = live.Rpm is { } rpm && rpm >= sc.Shift;
        bool dark = live.Limiter && !FlashOn(p, live.Now, LimiterHz);
        var on = shifting ? p.P.Danger : p.P.Text;
        var off = HudPalette.Alpha(p.P.Muted, 31);
        float w = (r.Width - gap * (count - 1)) / count;
        for (int i = 0; i < count; i++)
            p.Fill(new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height), i < lit && !dark ? on : off, Round(p, Math.Min(S(3), Math.Min(w, r.Height) / 2)));
    }

    private void DrawTrace(HudPainter p, HudView v, in Live live)
    {
        var b = p.Bounds;
        bool graph = _on.TraceGraph;
        float pedalsW = _on.PedalsW;
        _k = Math.Max(0.1f, Math.Min(b.Height / _on.TraceHeight, b.Width / _on.TraceW));
        float top = (b.Height - S(_on.TraceHeight)) / 2;
        float pad = S(8), x = pad;
        if (_rpm) ShiftLights(p, live, new RectangleF(pad, top + pad, b.Width - pad * 2, S(4)), TraceLights, S(2));
        float y = top + S(_rpm ? 18 : 8), h = S(TraceH);
        if (graph)
        {
            Graph(p, new RectangleF(x, y, S(250), h), live.Now, TraceSeconds, true);
            x += S(260);
        }
        if (pedalsW > 0)
        {
            PedalBars(p, new RectangleF(x, y, S(pedalsW), h), live);
            x += S(pedalsW + 8);
        }
        if (_gear || _speed || _rpm) GearBlock(p, v, new RectangleF(x, y, Math.Max(S(40), b.Right - pad - x), h), live);
    }

    private void PedalBars(HudPainter p, RectangleF r, in Live live)
    {
        float steerH = _on.SteerBar ? S(11) : 0;
        float barH = r.Height - steerH;
        float x = r.X;
        if (_clutch) One(live.Clutch, p.P.ClutchInput);
        if (_brake) One(live.Brake, BrakeColor(p, live));
        if (_throttle) One(live.Throttle, p.P.ThrottleInput);
        if (_on.SteerBar) SteerBar(p, new RectangleF(r.X, r.Bottom - S(5), r.Width, S(5)), live);

        void One(double? value, Color color)
        {
            VerticalBar(p, new RectangleF(x, r.Y, S(12), barH), value, color);
            x += S(17);
        }
    }

    private void GearBlock(HudPainter p, HudView v, RectangleF r, in Live live)
    {
        float gearPx = S(52);
        float gearH = p.DisplaySize("8", gearPx).Height;
        if (_gear) p.Display(GearText(v.Frame.Gear), gearPx, GearColor(p, live), new RectangleF(r.X, r.Y + S(26) - gearH / 2, r.Width, gearH), HAlign.Center, VAlign.Center);
        var small = p.Font(S(10.5f), true);
        float rpmH = p.LineHeight(small);
        float speedY = _gear ? r.Y + S(50) : r.Y + (r.Height - S(24) - (_rpm ? rpmH : 0)) / 2;
        if (_speed) SpeedLine(p, v, new RectangleF(r.X, speedY, r.Width, S(24)), p.Font(S(18), true), small);
        if (_rpm)
        {
            float rpmY = _gear ? r.Bottom - rpmH : _speed ? speedY + S(24) : r.Y + (r.Height - rpmH) / 2;
            p.Text(RpmText(live.Rpm), small, p.P.Muted, new RectangleF(r.X, rpmY, r.Width, rpmH), HAlign.Center, VAlign.Bottom, false);
        }
    }

    private void ReadoutColumn(HudPainter p, HudView v, in Live live, RectangleF r)
    {
        var unitFont = p.Font(S(9), true);
        float px = S(_speed && _rpm ? 24 : 30);
        float numberBottom = r.Y + S(_speed && _rpm ? 28 : 36);
        string number = _speed
            ? v.Frame.Speed is { } ms ? PooledText.Number("", RaceFormat.SpeedValue(ms, v.Units), "0", CultureInfo.InvariantCulture) : RaceFormat.Missing
            : live.Rpm is { } rpm ? PooledText.Number("", rpm, "0", CultureInfo.InvariantCulture) : RaceFormat.Missing;
        float numberH = p.DisplaySize("888", px).Height;
        p.Display(number, px, p.P.Heading, new RectangleF(r.X, numberBottom - numberH, r.Width, numberH), HAlign.Center, VAlign.Bottom);
        float unitTop = numberBottom + S(3);
        p.Text(p.LabelText(_speed ? RaceFormat.SpeedUnit(v.Units) : "RPM"), unitFont, p.P.Muted, new RectangleF(r.X, unitTop, r.Width, p.LineHeight(unitFont)), HAlign.Center, VAlign.Top, false);
        if (!(_speed && _rpm)) return;
        var rpmFont = p.Font(S(10.5f), true);
        p.Text(RpmText(live.Rpm), rpmFont, p.P.Text, new RectangleF(r.X, r.Bottom - S(6) - p.LineHeight(rpmFont), r.Width, p.LineHeight(rpmFont)), HAlign.Center, VAlign.Bottom, false);
    }

    private void DrawLights(HudPainter p, HudView v, in Live live)
    {
        var b = p.Bounds;
        float naturalW = BarW - (_speed || _rpm ? 0 : SpeedW - 12) - (_gear ? 0 : GearTileW);
        _k = Math.Max(0.1f, Math.Min(b.Height / BarH, b.Width / naturalW));
        float oy = (b.Height - S(BarH)) / 2, right = b.Width - S(12);
        var sc = live.Scale;
        bool shifting = live.Rpm is { } rpm && rpm >= sc.Shift;
        float x = S(12);
        if (_gear)
        {
            var tile = new RectangleF(0, oy, S(56), S(BarH));
            var tileColor = live.Limiter ? FlashOn(p, live.Now, LimiterHz) ? p.P.Danger : p.P.SurfaceAlt : shifting ? p.P.Danger : p.P.SurfaceAlt;
            p.Fill(tile, tileColor, Round(p, S(BarH) / 2));
            p.Display(GearText(v.Frame.Gear), S(48), live.Abs ? GearColor(p, live) : p.P.Heading, tile, HAlign.Center, VAlign.Center);
            x = tile.Right + S(10);
        }
        bool column = _speed || _rpm;
        float speedLeft = column ? right - S(SpeedW - 12) : right + S(12);
        if (column) ReadoutColumn(p, v, live, new RectangleF(speedLeft, oy, right - speedLeft, S(BarH)));
        float width = speedLeft - S(10) - x;
        ShiftLights(p, live, new RectangleF(x, oy + S(8), width, S(18)), BarLights, S(4));
        int bars = PedalCount + (_steering ? 1 : 0);
        if (bars == 0) return;
        float barTop = p.Snap(oy + S(32)), room = S(BarH - 8 - 32), gap = Math.Max(1, p.Snap(S(3)));
        float barH = Math.Max(1, p.Snap(Math.Min(S(6), (room - gap * (bars - 1)) / bars)));
        if (_throttle) Bar(live.Throttle, p.P.ThrottleInput);
        if (_brake) Bar(live.Brake, BrakeColor(p, live));
        if (_clutch) Bar(live.Clutch, p.P.ClutchInput);
        if (_steering) SteerBar(p, new RectangleF(x, barTop, width, barH), live, false);

        void Bar(double? value, Color color)
        {
            HorizontalBar(p, new RectangleF(x, barTop, width, barH), value, color);
            barTop += barH + gap;
        }
    }
}
