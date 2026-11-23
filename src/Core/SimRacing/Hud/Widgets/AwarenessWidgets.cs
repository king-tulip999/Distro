using System.Drawing;
using System.Globalization;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class TrafficWidget : HudWidget
{
    public const string Ladder = "ladder", Timeline = "timeline";

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Ladder,
            new HudChoice(Ladder, "Ladder", "You in the middle row; a rail places each car by its gap"),
            new HudChoice(Timeline, "Timeline", "Cars on a line from behind to ahead; the most urgent car spelled out underneath")),
        TitleRow,
        HudOption.Number("window", "Within", 6, 1, 30, 0.5, Units.S),
        HudOption.Int("max", "Cars at most", 5, 1, 12),
        HudOption.Bool("sameClass", "Include my class", false),
    };

    private const double TextEvery = 0.3, MeetWithin = 120;
    private const string BlueFlag = "BLUE FLAG";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string Seconds = " " + Core.Services.Units.S, Behind = Seconds + " behind", Ahead = Seconds + " ahead", BothWays = Seconds + " both ways";

    private readonly List<(int Car, double Gap)> _cars = new(), _shown = new();
    private readonly List<int> _order = new();
    private readonly List<bool> _threats = new(), _closings = new();
    private readonly List<RectangleF> _placed = new();
    private readonly TrafficMotion _motion = new();
    private readonly double[] _gap = new double[TelemetryFrame.MaxCars], _gapText = new double[TelemetryFrame.MaxCars], _textAt = new double[TelemetryFrame.MaxCars];
    private readonly double?[] _meet = new double?[TelemetryFrame.MaxCars];
    private readonly bool[] _threat = new bool[TelemetryFrame.MaxCars], _closing = new bool[TelemetryFrame.MaxCars];
    private int? _urgent;
    private float _blue;

    public override bool HasContent(HudView v) => Gather(v) > 0;

    private int Gather(HudView v)
    {
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        _cars.Clear();
        if ((uint)me >= TelemetryFrame.MaxCars) return 0;
        double window = v.Options.Number("window");
        bool same = v.Options.Bool("sameClass");
        int myClass = FieldOrder.ClassOf(f, me);
        double? myLap = FieldOrder.LapTimeOf(f, me);
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i] || !same && FieldOrder.ClassOf(f, i) == myClass) continue;
            if (FieldOrder.GapSeconds(f, me, i, v.Race.Profile) is not { } g || Math.Abs(g) > window) continue;
            if (PullingAway(g, v.Race.Gaps.Trend(i), Faster(FieldOrder.LapTimeOf(f, i), myLap))) continue;
            _cars.Add((i, g));
        }
        _cars.Sort((a, b) => Math.Abs(a.Gap).CompareTo(Math.Abs(b.Gap)));
        return _cars.Count;
    }

    internal static bool Faster(double? lap, double? mine) => lap is { } l && mine is { } m && l < m * 0.98;

    internal static bool PullingAway(double gap, double? trend, bool faster) => faster && gap > 0 && trend is > 0.01;

    internal static void LadderOrder(IReadOnlyList<(int Car, double Gap)> cars, List<int> order)
    {
        order.Clear();
        for (int i = 0; i < cars.Count; i++) if (cars[i].Gap > 0) Insert(cars, order, 0, i);
        int you = order.Count;
        order.Add(-1);
        for (int i = 0; i < cars.Count; i++) if (cars[i].Gap <= 0) Insert(cars, order, you + 1, i);
    }

    private static void Insert(IReadOnlyList<(int Car, double Gap)> cars, List<int> order, int from, int index)
    {
        int at = from;
        while (at < order.Count && cars[order[at]].Gap >= cars[index].Gap) at++;
        order.Insert(at, index);
    }

    internal static int? PickUrgent(int? current, IReadOnlyList<(int Car, double Gap)> cars, IReadOnlyList<bool> threat, IReadOnlyList<bool> closing)
    {
        int best = -1, held = -1;
        for (int i = 0; i < cars.Count && best < 0; i++) if (threat[i]) best = i;
        for (int i = 0; i < cars.Count && best < 0; i++) if (closing[i]) best = i;
        for (int i = 0; i < cars.Count; i++) if (cars[i].Car == current) held = i;
        if (held < 0 || !closing[held] || best >= 0 && threat[best] && !threat[held]) return best < 0 ? null : cars[best].Car;
        return current;
    }

    internal static float TimelineX(double gap, double window, float left, float width) =>
        left + (float)((Math.Clamp(gap, -window, window) + window) / (2 * window)) * width;

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Traffic")) return;
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        bool timeline = v.Options.Choice("style") == Timeline;
        double window = v.Options.Number("window");
        float d = p.Density;
        _motion.Begin(v.Now, p.ReducedMotion);
        var area = p.Header("Traffic");
        float blue = f.Flags.HasFlag(RaceFlags.Blue) ? 1 : 0;
        _blue = _motion.Fresh ? blue : _motion.Toward(_blue, blue);
        if (p.TitleRowShown && _blue > 0.02f) BlueBadge(p);
        if (timeline) p.NeedBelow(area, 128 * d);
        else p.NeedBelow(area, RowHeight(p) * (v.Options.Int("max") + 1) + 4 * d);
        _shown.Clear();
        _threats.Clear();
        _closings.Clear();
        if ((uint)me < TelemetryFrame.MaxCars)
        {
            Gather(v);
            double? myLap = FieldOrder.LapTimeOf(f, me);
            int max = v.Options.Int("max");
            for (int i = 0; i < _cars.Count && i < max; i++)
            {
                var (car, gap) = _cars[i];
                bool closing = v.Race.Gaps.Trend(car) is < -0.01;
                bool threat = Faster(FieldOrder.LapTimeOf(f, car), myLap) && gap < 0 && closing;
                bool seen = _motion.Visible(car, out _, out _);
                _gap[car] = gap;
                _closing[car] = closing;
                _threat[car] = threat;
                if (_motion.Fresh || !seen || v.Now - _textAt[car] >= TextEvery || v.Now < _textAt[car])
                {
                    _textAt[car] = v.Now;
                    _gapText[car] = gap;
                    _meet[car] = closing ? v.Race.Gaps.TimeToCatch(car, gap) : null;
                }
                _shown.Add((car, gap));
                _threats.Add(threat);
                _closings.Add(closing);
            }
        }
        double pulse = p.ReducedMotion ? 0.5 : 0.5 + 0.5 * Math.Sin(v.Now * 4);
        if (timeline) DrawTimeline(p, f, area, window, pulse);
        else DrawLadder(p, f, area, window, pulse);
    }

    private void DrawLadder(HudPainter p, TelemetryFrame f, RectangleF area, double window, double pulse)
    {
        float d = p.Density, rowH = RowHeight(p), h = rowH - 2 * d;
        int fits = Math.Max(1, (int)((area.Height + 2 * d) / rowH));
        bool clear = _shown.Count == 0, withYou = fits > 1 || clear;
        int room = withYou ? fits - 1 : 1;
        if (_shown.Count > room) _shown.RemoveRange(room, _shown.Count - room);
        LadderOrder(_shown, _order);
        if (!withYou) _order.Remove(-1);
        float top = area.Y + (area.Height - _order.Count * rowH) / 2;
        for (int i = 0; i < _order.Count; i++) _motion.Show(_order[i] < 0 ? TrafficMotion.You : _shown[_order[i]].Car, top + i * rowH);
        _motion.End();

        float rx = area.X + 8 * d, railTop = area.Y + 3 * d, railBottom = area.Bottom - 4 * d, mid = (railTop + railBottom) / 2, half = (railBottom - railTop) / 2;
        p.Fill(new RectangleF(rx - 1, railTop, 2, railBottom - railTop), p.P.SurfaceAlt, Round(p, 1));
        p.Dot(rx, mid, 5 * d, p.P.Player);
        for (int car = 0; car < TelemetryFrame.MaxCars; car++)
        {
            if (!_motion.Visible(car, out _, out float a)) continue;
            p.Dot(rx, mid - (float)Math.Clamp(_gap[car] / window, -1, 1) * half, 4.5f * d, Faded(ClassColor(p, f, car), a));
        }

        float left = area.X + 24 * d, right = area.Right - 2 * d;
        if (_motion.Live(TrafficMotion.You) && _motion.Visible(TrafficMotion.You, out float youY, out _))
        {
            p.Fill(new RectangleF(left, youY, right - left, h), p.P.SurfaceAlt, Round(p, 4));
            p.Fill(new RectangleF(left, youY, 3 * d, h), p.P.Player);
            var mine = f.Session.Player;
            p.Pill(PillBox(left + 8 * d, youY, h, d), mine?.ClassLabel ?? "", p.ClassColor(f.Session, mine?.ClassId ?? 0), p.Font(11, true));
            p.Text("You", p.Bold, p.P.Heading, RectangleF.FromLTRB(left + 56 * d, youY, right - 120 * d, youY + h));
            if (_shown.Count == 0) p.Text(PooledText.Number("Clear for ", window, "0.#", Inv, Seconds), p.Small, p.P.Muted, RectangleF.FromLTRB(left, youY, right - 8 * d, youY + h), HAlign.Right);
        }
        for (int car = 0; car < TelemetryFrame.MaxCars; car++)
        {
            if (!_motion.Visible(car, out float y, out float a)) continue;
            bool threat = _threat[car];
            if (threat) p.Fill(new RectangleF(left, y, right - left, h), Faded(HudPalette.Alpha(p.P.Warn, (int)Math.Round((0.1 + 0.12 * pulse) * 255)), a), Round(p, 4));
            ClassPill(p, f, PillBox(left + 8 * d, y, h, d), car, a);
            p.Text(NameOf(f, car), p.Body, Faded(p.P.Text, a), RectangleF.FromLTRB(left + 56 * d, y, right - 120 * d, y + h));
            string status = threat ? _meet[car] is { } t && t < MeetWithin ? PooledText.Number("in ", t, "0", Inv, Seconds) : "" : _closing[car] ? "catching" : "";
            p.Text(status, p.Small, Faded(threat ? p.P.WarnText : p.P.Muted, a), RectangleF.FromLTRB(right - 150 * d, y, right - 70 * d, y + h), HAlign.Right);
            p.Text(PooledText.Number(_gapText[car] > 0 ? "+" : "−", Math.Abs(_gapText[car]), "0.0", Inv), p.Bold, Faded(threat ? p.P.WarnText : p.P.Text, a), RectangleF.FromLTRB(right - 70 * d, y, right - 8 * d, y + h), HAlign.Right);
        }
    }

    private void DrawTimeline(HudPainter p, TelemetryFrame f, RectangleF area, double window, double pulse)
    {
        float d = p.Density;
        foreach (var (car, _) in _shown) _motion.Show(car, 0);
        _motion.End();

        bool pills = area.Height >= 76 * d, scale = area.Height >= 97 * d, callout = area.Height >= 128 * d;
        float ay = pills ? area.Y + 43 * d : area.Y + area.Height / 2, left = area.X + 18 * d, width = area.Width - 36 * d, labelY = ay + 48 * d;
        var tiny = p.Tiny;
        float lh = p.LineHeight(tiny);
        p.Fill(new RectangleF(left, ay - 1, width, 2), p.P.SurfaceAlt, Round(p, 1));
        string behind = PooledText.Number("", window, "0.#", Inv, Behind), ahead = PooledText.Number("", window, "0.#", Inv, Ahead);
        float behindEnd = left + p.Measure(behind, tiny) + 4 * d, aheadStart = left + width - p.Measure(ahead, tiny) - 4 * d;
        if (scale)
        {
            p.Text(behind, tiny, p.P.Faint, new RectangleF(left, labelY - lh / 2, width / 2, lh));
            p.Text(ahead, tiny, p.P.Faint, new RectangleF(left + width / 2, labelY - lh / 2, width / 2, lh), HAlign.Right);
        }
        double step = window / 3;
        for (int k = -3; k <= 3; k++)
        {
            float x = TimelineX(k * step, window, left, width);
            p.Fill(new RectangleF(x, ay - 5 * d, 1, 10 * d), p.P.LineStrong);
            if (!scale || k == 0 || Math.Abs(k) == 3) continue;
            string label = PooledText.Number("", Math.Abs(k) * step, "0.#", Inv);
            float lw = p.Measure(label, tiny);
            if (x - lw / 2 < behindEnd || x + lw / 2 > aheadStart) continue;
            p.Text(label, tiny, p.P.Faint, new RectangleF(x - 20 * d, labelY - lh / 2, 40 * d, lh), HAlign.Center);
        }
        float x0 = TimelineX(0, window, left, width);
        p.Fill(new RectangleF(x0 - 3 * d, ay - 10 * d, 6 * d, 20 * d), p.P.Player, Round(p, 3));

        _placed.Clear();
        foreach (var (car, _) in _shown) TimelineCar(p, f, car, ay, left, width, window, pulse, pills);
        for (int car = 0; car < TelemetryFrame.MaxCars; car++)
            if (!_motion.Live(car)) TimelineCar(p, f, car, ay, left, width, window, pulse, pills);

        if (!callout) return;
        _urgent = PickUrgent(_urgent, _shown, _threats, _closings);
        var box = new RectangleF(area.X + 4 * d, area.Y + 101 * d, area.Width - 8 * d, 26 * d);
        if (_shown.Count == 0)
        {
            p.Text(PooledText.Number("Clear for ", window, "0.#", Inv, BothWays), p.Small, p.P.Faint, box, HAlign.Center);
            return;
        }
        if (_urgent is not { } u)
        {
            p.Text("Nobody closing", p.Small, p.P.Faint, box, HAlign.Center);
            return;
        }
        bool urgentThreat = _threat[u];
        bool behindMe = _gapText[u] < 0;
        p.Fill(box, urgentThreat ? HudPalette.Alpha(p.P.Warn, 46) : p.P.SurfaceAlt, Round(p, 5));
        ClassPill(p, f, new RectangleF(box.X + 6 * d, box.Y + 4 * d, 40 * d, 18 * d), u, 1);
        string message = _meet[u] is { } t && t < MeetWithin
            ? PooledText.Number(behindMe ? "catching you in " : "you catch them in ", t, "0", Inv, Seconds)
            : behindMe ? "catching you" : "you catch them";
        float mw = p.Measure(message, p.Bold);
        p.Text(message, p.Bold, urgentThreat ? p.P.WarnText : p.P.Text, RectangleF.FromLTRB(box.Right - 8 * d - mw - 2, box.Y, box.Right - 8 * d, box.Bottom), HAlign.Right, VAlign.Center, false);
        p.Text(NameOf(f, u), p.Body, p.P.Text, RectangleF.FromLTRB(box.X + 54 * d, box.Y, box.Right - 16 * d - mw, box.Bottom));
    }

    private void TimelineCar(HudPainter p, TelemetryFrame f, int car, float ay, float left, float width, double window, double pulse, bool pills)
    {
        if (!_motion.Visible(car, out _, out float a)) return;
        float d = p.Density, x = TimelineX(_gap[car], window, left, width);
        var color = ClassColor(p, f, car);
        if (_threat[car]) p.Dot(x, ay, (float)(8 + 3 * pulse) * d, Faded(HudPalette.Alpha(p.P.Warn, (int)Math.Round((0.25 + 0.3 * pulse) * 255)), a));
        p.Dot(x, ay, 6 * d, Faded(HudPalette.Alpha(p.PanelColor, 255), a));
        p.Dot(x, ay, 4.5f * d, Faded(color, a));
        if (!pills) return;
        bool up = car % 2 == 0;
        var pillFont = p.Font(10, true);
        string label = f.Session.ByCar.GetValueOrDefault(car)?.ClassLabel ?? "";
        float pw = p.Measure(label, pillFont) + 10 * d;
        var box = new RectangleF(x - pw / 2, up ? ay - 32 * d : ay + 16 * d, pw, 16 * d);
        foreach (var taken in _placed) if (taken.IntersectsWith(box.Inflated(2 * d, 0))) return;
        _placed.Add(box);
        p.Fill(new RectangleF(x - 0.5f, up ? ay - 16 * d : ay + 7 * d, 1, 9 * d), Faded(HudPalette.Alpha(color, 153), a));
        p.Pill(box, label, Faded(color, a), pillFont, Faded(HudPalette.On(color), a));
    }

    private static float RowHeight(HudPainter p) => p.LineHeight(p.Body) * 1.4f * p.Density;

    private static float Round(HudPainter p, float r) => p.Look.Corner > 0 ? r * p.Density : 0;

    private static RectangleF PillBox(float x, float rowY, float rowH, float d) => new(x, rowY + (rowH - 18 * d) / 2, 40 * d, 18 * d);

    private static Color Faded(Color c, float alpha) => HudPalette.Alpha(c, (int)Math.Round(c.A * Math.Clamp(alpha, 0, 1)));

    private static Color ClassColor(HudPainter p, TelemetryFrame f, int car) => p.ClassColor(f.Session, f.Session.ByCar.GetValueOrDefault(car)?.ClassId ?? 0);

    private static string NameOf(TelemetryFrame f, int car) => f.Session.ByCar.GetValueOrDefault(car)?.Name ?? PooledText.Number("Car ", car);

    private static void ClassPill(HudPainter p, TelemetryFrame f, RectangleF r, int car, float alpha)
    {
        var bg = ClassColor(p, f, car);
        p.Pill(r, f.Session.ByCar.GetValueOrDefault(car)?.ClassLabel ?? "", Faded(bg, alpha), p.Font(11, true), Faded(HudPalette.On(bg), alpha));
    }

    private void BlueBadge(HudPainter p)
    {
        float d = p.Density, h = 18 * d;
        var font = p.Font(11, true);
        float w = p.Measure(BlueFlag, font) + 16 * d;
        var r = new RectangleF(p.Bounds.Width - 8 * d - w, (p.PillHeight - h) / 2, w, h);
        p.Pill(r, BlueFlag, Faded(HudPalette.FlagBlue, _blue), font, Faded(Color.White, _blue));
    }
}

internal sealed class RadarWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Number("range", "Range", 20, 8, 60, 1, Units.M, "How far ahead and behind cars show"),
        HudOption.Bool("distances", "Distances", true, "To the nearest car ahead and behind"),
    };

    private const float ClosingRate = 0.2f;

    private readonly RadarSlide _slide = new();
    private readonly List<(double Offset, double Lateral)> _placed = new();
    private readonly double[] _shift = new double[TelemetryFrame.MaxCars], _width = new double[TelemetryFrame.MaxCars];
    private readonly Dictionary<int, (double Offset, double At, double Rate)> _rates = new();
    private readonly PointF[] _marker = new PointF[3];

    public override bool HasContent(HudView v) =>
        v.Frame.Driving && (RadarModel.Alongside(v.Frame.Sides) > 0 || v.Race.Radar.Gather(v.Frame, v.Options.Number("range")).Count > 0);

    private double Rate(int car, double offset, double now)
    {
        if (!_rates.TryGetValue(car, out var last) || now < last.At || now - last.At > 1) { _rates[car] = (offset, now, 0); return 0; }
        if (now - last.At < 0.01) return last.Rate;
        double closing = (Math.Abs(last.Offset) - Math.Abs(offset)) / (now - last.At);
        double rate = last.Rate + (closing - last.Rate) * 0.25;
        _rates[car] = (offset, now, rate);
        return rate;
    }

    private static Color CarColor(HudPainter p, double offset, double lateral, double range)
    {
        double k = Math.Min(1, Math.Abs(lateral));
        double inline = Math.Clamp(1 - Math.Abs(offset) / range + 0.3, 0.3, 1);
        var hue = HudPalette.Mix(p.P.Warn, p.P.Danger, k);
        return HudPalette.Mix(p.PanelColor, hue, inline + (1 - inline) * k);
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        var f = v.Frame;
        double range = v.Options.Number("range"), now = v.Now;
        var cars = v.Race.Radar.Gather(f, range);
        double? length = v.Race.Radar.CarLength;
        p.Panel();
        float pad = 8 * p.Density;
        var area = p.Bounds.Inflated(-pad, -pad);
        float laneW = Math.Min(area.Width / 3, area.Height * 0.24f), carW = laneW * 0.62f;
        float cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        float scale = (float)(area.Height / 2 / range);
        float meH = length is { } l ? (float)(l * scale) : carW * 1.8f;

        _placed.Clear();
        double leftK = 0, rightK = 0;
        for (int i = 0; i < cars.Count; i++)
        {
            double lateral = _slide.Lateral(cars[i].CarIdx, cars[i].Lane, now, p.ReducedMotion);
            _placed.Add((cars[i].Offset, lateral));
            if (lateral < 0) leftK = Math.Max(leftK, -lateral); else rightK = Math.Max(rightK, lateral);
        }
        _slide.Prune(now);
        if (length is { } carLength) RadarModel.Split(_placed, carLength, _shift, _width);
        else for (int i = 0; i < _placed.Count; i++) { _shift[i] = 0; _width[i] = 1; }

        SideWash(p, cx - laneW, laneW, area, leftK);
        SideWash(p, cx + laneW, laneW, area, rightK);
        var guide = HudPalette.Alpha(p.P.LineStrong, 230);
        p.Line(cx - laneW / 2, area.Y, cx - laneW / 2, area.Bottom, guide);
        p.Line(cx + laneW / 2, area.Y, cx + laneW / 2, area.Bottom, guide);
        double tick = range <= 30 ? 5 : 10;
        for (double m = tick; m <= range; m += tick)
        {
            Tick(p, cx, laneW, cy - (float)(m * scale));
            Tick(p, cx, laneW, cy + (float)(m * scale));
        }

        RadarCar? ahead = null, behind = null;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            var (offset, lateral) = _placed[i];
            var color = CarColor(p, offset, lateral, range);
            float w = carW * (float)_width[i], x = cx + (float)lateral * laneW + (float)_shift[i] * carW * 0.27f;
            if (c.Lane == RadarLane.Inline)
            {
                if (c.Offset > 0 && ahead is null) ahead = c;
                if (c.Offset < 0 && behind is null) behind = c;
            }
            if (length is null && Math.Abs(lateral) < 0.5)
            {
                float y = cy - (float)(offset * scale);
                p.Fill(new RectangleF(x - w / 2, y - 1.5f * p.Density, w, 3 * p.Density), color);
                continue;
            }
            float top = length is null ? cy - meH / 2 : cy - (float)(offset * scale) - meH / 2;
            p.Fill(new RectangleF(x - w / 2, top + 1, w, Math.Max(2, meH - 2)), color, p.CornerRadius(3));
        }
        p.Fill(new RectangleF(cx - carW / 2, cy - meH / 2, carW, meH), p.P.Heading, p.CornerRadius(3));

        if (v.Options.Bool("distances"))
        {
            var font = p.Font(12, true);
            float lh = p.LineHeight(font), marker = 6 * p.Density, tx = cx + laneW / 2 + 5;
            var textBox = new RectangleF(tx + marker + 2, 0, area.Right - tx - marker - 2, lh);
            if (textBox.Width >= p.Measure("00 M", font))
            {
                Distance(p, v, ahead, length, now, font, textBox, tx, marker, cy, scale, area);
                Distance(p, v, behind, length, now, font, textBox, tx, marker, cy, scale, area);
            }
        }
    }

    private static void SideWash(HudPainter p, float center, float laneW, RectangleF area, double k)
    {
        if (k > 0.01) p.Fill(new RectangleF(center - laneW / 2 + 2, area.Y, laneW - 4, area.Height), HudPalette.Alpha(p.P.Danger, (int)(41 * k)));
    }

    private static void Tick(HudPainter p, float cx, float laneW, float y)
    {
        p.Line(cx - laneW / 2 - 3, y, cx - laneW / 2, y, p.P.Faint);
        p.Line(cx + laneW / 2, y, cx + laneW / 2 + 3, y, p.P.Faint);
    }

    private void Distance(HudPainter p, HudView v, RadarCar? car, double? length, double now, Font font, RectangleF textBox, float tx, float marker, float cy, float scale, RectangleF area)
    {
        if (car is not { } c) return;
        float lh = textBox.Height;
        float y = Math.Clamp(cy - (float)(c.Offset * scale), area.Y + lh / 2, area.Bottom - lh / 2);
        double gap = Math.Max(0, Math.Abs(c.Offset) - (length ?? 0));
        p.Text(RaceFormat.Distance(gap, v.Units), font, p.P.WarnText, textBox with { Y = y - lh / 2 }, HAlign.Left, VAlign.Center, false);
        if (Rate(c.CarIdx, c.Offset, now) <= ClosingRate) return;
        float d = c.Offset > 0 ? 1 : -1, h = marker / 2;
        _marker[0] = new PointF(tx, y - h * d);
        _marker[1] = new PointF(tx + marker, y - h * d);
        _marker[2] = new PointF(tx + marker / 2, y + h * d);
        p.G.FillPolygon(p.Brush(p.P.WarnText), _marker);
    }
}

