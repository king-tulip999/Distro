using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class InputsWidget : HudWidget
{
    public const string Dial = "dial", Bare = "bare", GearRight = "right", GearLeft = "left";
    private const double TraceSeconds = 6, LimiterHz = 10, AbsHz = 4;
    private const int Capacity = 640;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Dial,
            new HudChoice(Dial, "Gear dial", "A round gear dial with speed and steering, pedal bars with their numbers and a trace, an RPM bar across the top"),
            new HudChoice(Bare, "Trace only", "Just the input trace: no gear dial, pedal bars or RPM bar")),
        HudOption.Choice("gearSide", "Gear dial", GearRight, new HudChoice(GearLeft, "Left"), new HudChoice(GearRight, "Right")),
        HudOption.Set("show", "Shows", "throttle,brake,clutch,steering,gear,speed,rpm,trace,abs", null,
            new HudChoice("throttle", "Throttle"), new HudChoice("brake", "Brake"), new HudChoice("clutch", "Clutch", "Pedal travel: pressed shows full"),
            new HudChoice("steering", "Steering", "A mark on the gear dial that turns with the wheel"), new HudChoice("gear", "Gear"), new HudChoice("speed", "Speed"), new HudChoice("rpm", "RPM", "The RPM bar across the top"),
            new HudChoice("trace", "Input trace", "Trace only"),
            new HudChoice("abs", "ABS flash", "While ABS works the gear flashes amber and the brake turns amber")),
        HudOption.Columns("extras", "Extra fields", "",
            new HudChoice("speed", "Speed"), new HudChoice("rpm", "RPM"), new HudChoice("position", "Position"), new HudChoice("lapNumber", "Lap number"),
            new HudChoice("lapsLeft", "Laps remaining"), new HudChoice("timeLeft", "Session time left"), new HudChoice("last", "Last lap"), new HudChoice("best", "Best lap"),
            new HudChoice("deltaBest", "Delta to best"), new HudChoice("fuelPct", "Fuel %"), new HudChoice("incidents", "Incidents"), new HudChoice("bias", "Brake bias"),
            new HudChoice("abs", "ABS setting"), new HudChoice("tc", "Traction control"), new HudChoice("battery", "Battery level"), new HudChoice("steer", "Steering angle"),
            new HudChoice("air", "Air temperature"), new HudChoice("track", "Track temperature"), new HudChoice("wetness", "Track wetness"))
            with { Help = "Gear dial only: a row under the trace" },
    };

    internal readonly record struct Shows(bool Throttle, bool Brake, bool Clutch, bool Steering, bool Speed, bool Rpm, bool Trace, bool Abs, bool Gear)
    {
        public bool AnyPedal => Throttle || Brake || Clutch;
        public int Pedals => (Clutch ? 1 : 0) + (Brake ? 1 : 0) + (Throttle ? 1 : 0);
        public bool TraceGraph => Trace && (AnyPedal || Steering);
        public bool GearDial => Gear || Speed || Steering;
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
        return o.Choice("style") == Bare ? BareSize(on) : DialSize(on, o.Columns("extras").Count > 0);
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
        DrawDial(p, v, live, v.Options.Choice("style") == Bare);
    }

    private void ReadShows(HudOptions o)
    {
        _on = Parse(o);
        (_throttle, _brake, _clutch, _steering, _speed, _rpm, _abs, _gear) = (_on.Throttle, _on.Brake, _on.Clutch, _on.Steering, _on.Speed, _on.Rpm, _on.Abs, _on.Gear);
    }

    internal static double? Pedal(double? raw, double? cooked) => (raw ?? cooked) is { } x ? Math.Clamp(x, 0, 1) : null;

    internal static double? ClutchTravel(double? raw, double? cooked) => Pedal(raw, cooked) is { } engaged ? 1 - engaged : null;

    internal static double? SteerDegrees(double? radians) => radians is { } r ? -r * 180 / Math.PI : null;

    internal static double HalfLockDegrees(double? lockToLockRadians) => lockToLockRadians is > 0 and var m ? m * 90 / Math.PI : 450;

    internal static string Angle(double? degrees) => degrees is not { } d ? RaceFormat.Missing
        : Math.Abs(d) < 2 ? "0°"
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(d):0}° {(d > 0 ? "R" : "L")}");

    internal static string GearText(int? gear) => gear switch { null => RaceFormat.Missing, < 0 => "R", 0 => "N", _ => gear.Value.ToString(CultureInfo.InvariantCulture) };

    internal static (string? Before, string? After) NeighbourGears(int? gear, int? forwardGears) => gear switch
    {
        null => (null, null),
        < 0 => (null, "N"),
        _ => (GearText(gear - 1 < 0 ? -1 : gear - 1), forwardGears is { } top && gear < top ? GearText(gear + 1) : null),
    };

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
        return live.Limiter ? ShiftColor(p) : p.P.Heading;
    }

    private static Color ShiftColor(HudPainter p) => p.P.Night ? Color.White : p.P.Danger;

    private Color BrakeColor(HudPainter p, in Live live) => live.Abs ? p.P.AbsInput : p.P.BrakeInput;

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
        var on = shifting ? ShiftColor(p) : p.P.Text;
        var off = HudPalette.Alpha(p.P.Muted, 31);
        float w = (r.Width - gap * (count - 1)) / count;
        for (int i = 0; i < count; i++)
            p.Fill(new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height), i < lit && !dark ? on : off, Round(p, Math.Min(S(3), Math.Min(w, r.Height) / 2)));
    }
}
