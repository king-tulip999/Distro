using System.Drawing;
using System.Globalization;
using Units = Core.Services.CapsUnits;
using Core.Config;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Coach;
using Core.SimRacing.Race;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class CoachWidget : HudWidget
{
    public const string Strip = "strip", RacingLine = "line", Compare = "compare", Debrief = "debrief";
    private const double BoardsM = 200, StraightM = 250, TrailM = 160, AheadM = 260, BehindM = 120, OnLineM = 0.5, LineRangeM = 4, BrakeRangeM = 30, SteerRangeDeg = 140;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Strip,
            new HudChoice(Strip, "Coach strip", "One call per turn from your own laps, with your pace, your pedals and the turn coming up"),
            new HudChoice(RacingLine, "Racing line", "A zoomed map that turns with the car: your best lap's line and yours"),
            new HudChoice(Compare, "Compare", "Your pedals, steering, line and brake point against your best lap at this spot"),
            new HudChoice(Debrief, "Lap debrief", "Your pace and the turns where this lap lost the most")),
        TitleRow,
        HudOption.Bool("pace", "Show pace", true, "Lap time, the gap to your best and how this lap compares with your usual"),
        HudOption.Bool("pedals", "Show pedals", true),
        HudOption.Bool("next", "Show the turn coming up", true, "A small map of the next turn with your best's brake points"),
        HudOption.Bool("boards", "Show brake boards", true, "150, 100 and 50 counting down to where your best brakes"),
        HudOption.Bool("result", "Show the result after each turn", true),
        HudOption.Choice("callSize", "Call size", "normal", new HudChoice("normal", "Normal"), new HudChoice("large", "Large")),
        HudOption.Choice("direction", "Map direction", "turn", new HudChoice("turn", "Turns with the car"), new HudChoice("north", "North up")),
        HudOption.Choice("zoom", "Zoom", "auto", new HudChoice("auto", "Auto", "Zooms out as you speed up"), new HudChoice("close", "Close"), new HudChoice("far", "Far")),
        HudOption.Bool("chips", "Show turn chips", true, "Each turn's slowest speed and gear on your best lap"),
        HudOption.Bool("grid", "Show grid lines", true),
        HudOption.Bool("speed", "Show speed and gear", true),
        HudOption.Bool("legend", "Show legend", true),
        HudOption.Color("brakeColor", "Braking color"),
        HudOption.Color("partColor", "Part throttle color"),
        HudOption.Color("fullColor", "Full throttle color"),
        HudOption.Set("rows", "Rows", "brake,throttle,steering,line,brakepoint,slowest", null,
            new HudChoice("brake", "Brake"), new HudChoice("throttle", "Throttle"), new HudChoice("steering", "Steering"),
            new HudChoice("line", "Line"), new HudChoice("brakepoint", "Brake point"), new HudChoice("slowest", "Slowest speed")),
        HudOption.Bool("map", "Show map", true),
        HudOption.Bool("lost", "Show most time lost", true),
        HudOption.Int("listed", "Turns listed", 3, 1, 5),
        HudOption.Bool("gained", "Show gained", true),
        HudOption.Bool("straights", "Only open on straights", false, "Shows just the top lines in braking zones and turns"),
        HudOption.Color("lostColor", "Time lost color"),
        HudOption.Color("gainedColor", "Time gained color"),
        HudOption.Color("you", "Your color", "Your bars, line and markers"),
        HudOption.Color("best", "Best color", "Your best's bars and markers"),
        HudOption.Bool("beep", "Brake beep", false, "A short tone where your best lap starts braking for each turn"),
        HudOption.Int("volume", "Beep volume", 60, 0, 100, "%") with { Slider = true },
    };

    public static SizeF Natural(HudOptions o)
    {
        switch (o.Choice("style"))
        {
            case RacingLine:
                return new SizeF(380, 300 + (o.Bool("speed") ? 32 : 0) + (o.Bool("legend") ? 82 : 0));
            case Compare:
                var rows = o.Columns("rows");
                float h = 30;
                for (int i = 0; i < CompareIds.Length; i++)
                    if (rows.Contains(CompareIds[i])) h += (i < 2 ? PairH : i == 5 ? SlowestH : SliderH) + RowGap;
                return new SizeF(330, h);
            case Debrief:
                return new SizeF(480, o.Bool("map") || o.Bool("lost") || o.Bool("gained") ? 268 : 110);
            default:
                float w = 400 + (o.Bool("pace") ? 226 : 0) + (o.Bool("pedals") ? 172 : 0) + (o.Bool("next") ? 140 : 0);
                return new SizeF(w, o.Bool("result") ? 200 : 168);
        }
    }

    private const double SampleShift = 31;
    private static CoachPlan? _samplePlan;
    private static CoachTurnResult?[]? _sampleResults;

    private CoachPlan? _plan;
    private LapTrace? _lap;
    private double _d, _filled, _lapTime, _speed, _throttle, _brake, _steer;
    private int _gear;
    private double[]? _xs, _ys;
    private bool _hasLine, _sample;
    private int _seenCrossings = -1;

    private CoachPlan? _verdictPlan;
    private int _verdictGroup = -2;
    private double _verdictAt = double.MaxValue;
    private string _verdict = "";
    private double _vsUsual;
    private int _groupsDone;
    private readonly List<(string Label, double Lost, string Why)> _worst = new();
    private double _gained;
    private int _gainedTurns;

    private readonly Dictionary<(int Group, HudUnits Units), (string Title, string Detail, string Cost)> _calls = new();
    private readonly Dictionary<(string, int, Font), (string, string)> _splits = new();
    private PointF[] _points = new PointF[64];
    private double _viewAngle = double.NaN, _viewScale = 2.5;
    private object? _mapKey;
    private readonly List<PointF[]> _mapPieces = new();
    private PointF[] _mapLine = Array.Empty<PointF>();
    private Func<double, PointF> _mapAt = _ => PointF.Empty;
    private object? _nextKey;
    private PointF[] _nextLine = Array.Empty<PointF>();
    private Func<double, PointF> _nextAt = _ => PointF.Empty;

    private Font Lb = null!, Tx = null!, TxB = null!, Vb = null!, Vr = null!, Bg = null!;

    public override void Draw(HudPainter p, HudView v)
    {
        Lb = p.Font(12f, true);
        Tx = p.Font(14f, false);
        TxB = p.Font(14f, true);
        Vb = p.Font(16f, true);
        Vr = p.Font(15f, false);
        Bg = p.Font(26f, true);
        string style = v.Options.Choice("style");
        if (DrawWaiting(p, v, style == Strip ? null : "Coach")) return;
        Snapshot(p, v);
        if (_plan is null || _lap is null)
        {
            DrawNotReady(p, style, null);
            return;
        }
        if (!_sample && !v.Frame.Driving)
        {
            DrawNotReady(p, style, "Starts when you're out on track");
            return;
        }
        if (!_sample && v.Options.Bool("beep"))
        {
            int crossings = CoachLive.Shared.BrakeCrossings;
            if (_seenCrossings >= 0 && crossings != _seenCrossings) Tone.Beep(v.Options.Int("volume"));
            _seenCrossings = crossings;
        }
        Verdicts(v);
        switch (style)
        {
            case RacingLine: DrawLine(p, v); break;
            case Compare: DrawCompare(p, v); break;
            case Debrief: DrawDebrief(p, v); break;
            default: DrawStrip(p, v); break;
        }
    }

    private void Snapshot(HudPainter p, HudView v)
    {
        var f = v.Frame;
        _sample = p.IsPreview || f.Simulated;
        if (_sample)
        {
            _samplePlan ??= CoachPlan.Build(CoachSample.Laps);
            _plan = _samplePlan;
            _lap = CoachSample.You;
            if (_plan is null) return;
            _sampleResults ??= _plan.Turns.Select(t => (CoachTurnResult?)_plan.Result(_lap, t)).ToArray();
            double t = (((v.Now + SampleShift) % _lap.LapTime) + _lap.LapTime) % _lap.LapTime;
            _d = Math.Min(_plan.Length, _lap.DistanceAtTime(t));
            _filled = _d;
            _lapTime = t;
            _speed = _lap.At(TraceChannel.Speed, _d) ?? 0;
            _throttle = _lap.At(TraceChannel.Throttle, _d) ?? 0;
            _brake = _lap.At(TraceChannel.Brake, _d) ?? 0;
            _steer = _lap.At(TraceChannel.Steering, _d) ?? 0;
            _gear = (int)(_lap.At(TraceChannel.Gear, _d) ?? 0);
            (_xs, _ys) = _plan.Line(_lap);
            _hasLine = true;
            return;
        }
        var live = CoachLive.Shared;
        live.Update(f);
        _plan = live.Plan;
        _lap = live.Lap;
        _d = f.LapDist ?? 0;
        _filled = live.Filled;
        _lapTime = f.LapCurrent ?? 0;
        _speed = f.Speed ?? 0;
        _throttle = f.Throttle ?? 0;
        _brake = f.Brake ?? 0;
        _steer = f.Steering ?? 0;
        _gear = f.Gear ?? 0;
        _xs = live.X;
        _ys = live.Y;
        _hasLine = live.HasLine && live.Filled >= 0;
    }

    private CoachTurnResult? Result(PlanTurn t)
    {
        if (_sample) return _d >= Math.Min(t.To, _plan!.Length - 1) ? _sampleResults![t.Index] : null;
        return CoachLive.Shared.Result(t.Index);
    }

    private void DrawNotReady(HudPainter p, string style, string? why)
    {
        string text = why ?? (_sample ? "No sample laps" : CoachLive.Shared.State switch
        {
            CoachState.NoStats => "Needs Session Stats to record your laps",
            CoachState.Reading => "Reading your laps for this car and track",
            _ => PooledText.Number("Needs 3 clean recorded laps in this car and track. So far: ", CoachLive.Shared.CleanLaps),
        });
        RectangleF area;
        if (style == Strip)
        {
            p.Panel();
            area = p.Bounds.Inflated(-12 * p.Density, 0);
        }
        else area = p.Header("Coach");
        p.Text(text, Vr, p.P.Muted, area, HAlign.Center, VAlign.Center);
    }

    private void Verdicts(HudView v)
    {
        var plan = _plan!;
        if (!ReferenceEquals(plan, _verdictPlan) || _d < _verdictAt - plan.Length / 2)
        {
            _verdictPlan = plan;
            _verdictGroup = -2;
            _calls.Clear();
        }
        _verdictAt = _d;
        int done = -1;
        foreach (var g in plan.Groups)
        {
            bool all = _d >= g.To;
            foreach (var t in g.Turns) all &= Result(t) is not null;
            if (all) done = g.Index;
        }
        if (done == _verdictGroup) return;
        _verdictGroup = done;
        _vsUsual = 0;
        _groupsDone = 0;
        _worst.Clear();
        _gained = 0;
        _gainedTurns = 0;
        var picked = new List<CoachGroupResult>();
        foreach (var g in plan.Groups)
        {
            if (g.Index > done) break;
            if (CoachPlan.Pick(g, Result) is not { } r) continue;
            picked.Add(r);
            _vsUsual += g.Usual - r.Lost;
            _groupsDone++;
            foreach (var t in g.Turns)
                if (Result(t) is { Lost: < -CoachPlan.EvenS } tr) { _gained += tr.Lost; _gainedTurns++; }
        }
        foreach (var r in picked.Where(r => r.Lost > CoachPlan.EvenS).OrderByDescending(r => r.Lost).Take(5))
            _worst.Add((r.Group.Label, r.Lost, r.Reason is { } why ? Short(why, v.Units) : ""));
        _verdict = done < 0 ? "No turn finished yet this lap." : picked.LastOrDefault(r => r.Group.Index == done) is { } last ? Sentence(last, v.Units) : "";
    }

    private static string Sentence(CoachGroupResult r, HudUnits u)
    {
        if (Math.Abs(r.Lost) < CoachPlan.EvenS) return $"{r.Group.Label}: level with your best.";
        string head = $"{r.Group.Label}: {(r.Lost > 0 ? "lost" : "gained")} {Math.Abs(r.Lost).ToString("0.00", CultureInfo.CurrentCulture)} {Units.S}.";
        if (r.Reason is not { } why) return r.Group.Several ? $"{head} Most in Turn {r.Most.Number}." : head;
        return r.Group.Several ? $"{head} Most in Turn {r.Most.Number}: you {Long(why, u)} than your best." : $"{head} You {Long(why, u)} than your best.";
    }

    private static string Long(CoachReason r, HudUnits u) => r.Kind switch
    {
        CoachReasonKind.Brake => $"braked {RaceFormat.Distance(Math.Abs(r.Amount), u)} {(r.Amount > 0 ? "earlier" : "later")}",
        CoachReasonKind.Throttle => $"got to full throttle {RaceFormat.Distance(Math.Abs(r.Amount), u)} {(r.Amount > 0 ? "later" : "earlier")}",
        _ => $"were {RaceFormat.Speed(Math.Abs(r.Amount), u)} {(r.Amount < 0 ? "slower" : "faster")} at the slowest point",
    };

    private static string Short(CoachReason r, HudUnits u) => r.Kind == CoachReasonKind.Speed
        ? $"{RaceFormat.Speed(Math.Abs(r.Amount), u)} {(r.Amount < 0 ? "slower" : "faster")} at the slowest point"
        : Long(r, u);

    private (string Title, string Detail, string Cost) CallText(CoachGroup g, HudPainter p, HudUnits u)
    {
        if (_calls.TryGetValue((g.Index, u), out var done)) return done;
        var c = g.Call;
        string detail = c.Kind switch
        {
            CoachCallKind.BrakeLater => $"Your best brakes {RaceFormat.Distance(c.Size, u)} later than you usually do.",
            CoachCallKind.CarrySpeed => $"Your best is {RaceFormat.Speed(c.Size, u)} faster at the slowest point than you usually are.",
            CoachCallKind.ThrottleSooner => $"Your best is on full throttle {RaceFormat.Distance(c.Size, u)} sooner than you usually are.",
            CoachCallKind.NoClearCall => "Your brake point, slowest speed and throttle here match your best on average.",
            _ => $"You're within {CoachPlan.LeastCostS.ToString("0.00", CultureInfo.CurrentCulture)} {Units.S} of your best here on average.",
        };
        string cost = c.Quiet ? "" : g.Several ? $"Losing {g.CallTurn.AverageLost.ToString("0.00", CultureInfo.CurrentCulture)} {Units.S} a lap in Turn {g.CallTurn.Number}" : $"Losing {g.CallTurn.AverageLost.ToString("0.00", CultureInfo.CurrentCulture)} {Units.S} a lap here";
        return _calls[(g.Index, u)] = (p.LabelText(c.Title), detail, p.LabelText(cost));
    }

    private (CoachGroup G, string Phase, double Start, double Goal, double D) Phase()
    {
        var plan = _plan!;
        var g = plan.GroupAhead(_d);
        double d = g.Index == 0 && _d >= plan.Groups[^1].End ? _d - plan.Length : _d;
        var f = g.First;
        var l = g.Last;
        if (f.Brake is { } bp && d < bp) return (g, "Approach", bp - BoardsM, bp, d);
        if (d < l.Apex + 10) return (g, "In", f.Brake ?? f.From, l.Apex, d);
        return (g, "Exit", l.Apex + 10, l.FullThrottle ?? l.To, d);
    }

    private double Gap() => _lapTime - _plan!.ReferenceTime(_d);

    private static Color Tone3(HudPainter p, double x) => x > CoachPlan.EvenS ? p.P.DangerText : x < -CoachPlan.EvenS ? p.P.GoodText : p.P.Muted;

    private static string Behind(double gap) => Math.Abs(gap) < 0.005 ? "level with your best"
        : PooledText.Number("", Math.Abs(gap), "0.00", CultureInfo.CurrentCulture, gap > 0 ? " " + Units.S + " behind your best" : " " + Units.S + " ahead of your best");

    private static string Usual(double better, int groups) => groups == 0 ? "Shows after the first turn"
        : Math.Abs(better) < 0.01 ? "same as usual"
        : PooledText.Number("", Math.Abs(better), "0.00", CultureInfo.CurrentCulture, better > 0 ? " " + Units.S + " better" : " " + Units.S + " worse");

    private (string, string) Lines(HudPainter p, string text, Font font, float width)
    {
        var key = (text, (int)width, font);
        if (_splits.TryGetValue(key, out var done)) return done;
        if (_splits.Count >= 64) _splits.Clear();
        int cut = -1;
        for (int i = text.IndexOf(' '); i >= 0; i = text.IndexOf(' ', i + 1))
        {
            if (p.Measure(text[..i], font) > width) break;
            cut = i;
        }
        return _splits[key] = p.Measure(text, font) <= width || cut <= 0 ? (text, "") : (text[..cut], text[(cut + 1)..]);
    }

    private static readonly string[] StripIds = { "pace", "pedals", "call", "next" };
    private static readonly string[] LineIds = { "map", "stats", "legend" };
    private static readonly string[] CompareIds = { "brake", "throttle", "steering", "line", "brakepoint", "slowest" };
    private static readonly string[] DebriefIds = { "map", "list" };
    private readonly float[] _sizes = new float[6];
    private readonly RectangleF[] _boxes = new RectangleF[6];

    private static Color Pick(HudView v, string key, Color fallback) => v.Options.ColorOf(key) ?? fallback;

    private void DrawStrip(HudPainter p, HudView v)
    {
        float s = p.Density;
        var o = v.Options;
        p.Panel();
        var b = p.Bounds;
        bool result = o.Bool("result");
        float verdictH = result ? MathF.Round(32 * s) : 0, pad = 12 * s;
        var area = new RectangleF(b.X, b.Y, b.Width, b.Height - verdictH);
        _sizes[0] = o.Bool("pace") ? 226 * s : -1;
        _sizes[1] = o.Bool("pedals") ? 172 * s : -1;
        _sizes[2] = 0;
        _sizes[3] = o.Bool("next") ? 140 * s : -1;
        p.Arrange(v, "coach.strip", area, true, 0, StripIds, _sizes.AsSpan(0, 4), _boxes.AsSpan(0, 4));
        float left = float.MaxValue;
        for (int i = 0; i < 4; i++) if (_boxes[i].Width > 0) left = Math.Min(left, _boxes[i].X);
        for (int i = 0; i < 4; i++)
            if (_boxes[i].Width > 0 && _boxes[i].X > left + 1) p.Divider(_boxes[i].X, area.Y + 6 * s, area.Bottom - 6 * s);
        if (result) p.Seam(b, area.Bottom);
        var you = Pick(v, "you", p.P.Accent);
        var best = Pick(v, "best", p.P.Muted);
        var (g, phase, start, goal, d) = Phase();
        if (_boxes[0].Width > 0) StripPace(p, _boxes[0].Inflated(-pad, -pad));
        if (_boxes[1].Width > 0) StripPedals(p, _boxes[1].Inflated(-pad, -pad), you, best);
        if (_boxes[2].Width > 0) StripCall(p, v, _boxes[2].Inflated(-pad, -pad), g, phase, start, goal, d);
        if (_boxes[3].Width > 0) DrawNext(p, _boxes[3].Inflated(-pad, -pad), g, you);
        if (!result) return;
        bool lit = phase == "Approach" && _verdictGroup >= 0 && _d >= _plan!.Groups[Math.Min(_verdictGroup, _plan.Groups.Count - 1)].To;
        p.Text(_verdict, Tx, lit ? p.P.Text : p.P.Faint, new RectangleF(b.X + pad, area.Bottom, b.Width - 2 * pad, verdictH));
    }

    private void StripPace(HudPainter p, RectangleF r)
    {
        float s = p.Density, lh = p.LineHeight(Lb), y = r.Y;
        p.Text(p.LabelText("This lap, on pace for"), Lb, p.P.Muted, new RectangleF(r.X, y, r.Width, lh));
        y += lh;
        double gap = Gap();
        float bigH = p.LineHeight(Bg);
        p.Text(RaceFormat.LapTime(_plan!.BestTurnsTogether + gap), Bg, p.P.Heading, new RectangleF(r.X, y, r.Width, bigH));
        y += bigH;
        float sh = p.LineHeight(Tx);
        p.Text(Behind(gap), TxB, Tone3(p, gap), new RectangleF(r.X, y, r.Width, sh));
        y += sh + 4 * s;
        p.Text(p.LabelText("Against your usual"), Lb, p.P.Muted, new RectangleF(r.X, y, r.Width, lh));
        y += lh;
        p.Text(Usual(_vsUsual, _groupsDone), TxB, _groupsDone == 0 ? p.P.Muted : Tone3(p, -_vsUsual), new RectangleF(r.X, y, r.Width, sh));
    }

    private void StripPedals(HudPainter p, RectangleF r, Color you, Color best)
    {
        float s = p.Density, lh = p.LineHeight(Lb);
        var refLap = _plan!.TurnAt(_d).Reference;
        float barH = r.Height - 2 * lh - 2 * s, barW = 20 * s, half = r.Width / 2;
        Pedal(p, new RectangleF(r.X + half / 2 - barW / 2, r.Y, barW, barH), _brake, refLap.At(TraceChannel.Brake, _d) ?? 0, you, best);
        Pedal(p, new RectangleF(r.X + half + half / 2 - barW / 2, r.Y, barW, barH), _throttle, refLap.At(TraceChannel.Throttle, _d) ?? 0, you, best);
        float ly = r.Y + barH + 2 * s;
        p.Text(p.LabelText("Brake"), Lb, p.P.Muted, new RectangleF(r.X, ly, half, lh), HAlign.Center);
        p.Text(p.LabelText("Throttle"), Lb, p.P.Muted, new RectangleF(r.X + half, ly, half, lh), HAlign.Center);
        p.Text(p.LabelText("Grey = your best"), Lb, p.P.Faint, new RectangleF(r.X, ly + lh, r.Width, lh), HAlign.Center);
    }

    private void StripCall(HudPainter p, HudView v, RectangleF r, CoachGroup g, string phase, double start, double goal, double d)
    {
        float s = p.Density, lh = p.LineHeight(Lb), y = r.Y;
        var (title, detail, cost) = CallText(g, p, v.Units);
        p.Text(p.LabelText(PooledText.Concat(phase == "Approach" ? "Next: " : "In ", g.Label)), Lb, p.P.Muted, new RectangleF(r.X, y, r.Width * 0.45f, lh));
        p.Text(cost, Lb, p.P.Muted, new RectangleF(r.X + r.Width * 0.45f, y, r.Width * 0.55f, lh), HAlign.Right);
        y += lh;
        var callFont = p.Font(v.Options.Choice("callSize") == "large" ? 38f : 30f, true);
        float ch = p.LineHeight(callFont);
        p.Text(title, callFont, g.Call.Quiet ? p.P.Muted : p.P.Heading, new RectangleF(r.X, y, r.Width, ch));
        y += ch;
        float bh = p.LineHeight(Tx);
        var (l1, l2) = Lines(p, detail, Tx, r.Width);
        p.Text(l1, Tx, p.P.Text, new RectangleF(r.X, y, r.Width, bh));
        if (l2.Length > 0) p.Text(l2, Tx, p.P.Text, new RectangleF(r.X, y + bh, r.Width, bh));
        if (!v.Options.Bool("boards")) return;
        var bar = new RectangleF(r.X, r.Bottom - lh - 12 * s, r.Width, 12 * s);
        p.Fill(bar, p.P.SurfaceAlt, 3 * s);
        double frac = Math.Clamp((d - start) / Math.Max(1, goal - start), 0, 1);
        p.Fill(new RectangleF(bar.X, bar.Y, (float)(bar.Width * frac), bar.Height), HudPalette.Alpha(p.P.Heading, 215), 3 * s);
        p.Fill(new RectangleF(bar.Right - 4 * s, bar.Y, 4 * s, bar.Height), p.P.Danger);
        float by = bar.Bottom + 2 * s;
        if (phase == "Approach")
        {
            for (int i = 1; i <= 3; i++) p.Fill(new RectangleF(bar.X + bar.Width * i / 4f - s, bar.Y, 2 * s, bar.Height), p.P.Surface);
            if (g.First.UsualBrake is { } usual)
            {
                float ux = bar.X + bar.Width * (float)Math.Clamp((usual - start) / (goal - start), 0, 1);
                p.Fill(new RectangleF(ux - 1.5f * s, bar.Y - 2 * s, 3 * s, bar.Height + 4 * s), p.P.Warn);
                p.Text(p.LabelText("Yellow = you usually brake"), Lb, p.P.WarnText, new RectangleF(bar.X, bar.Y - lh - 3 * s, bar.Width / 2, lh));
            }
            p.Text(p.LabelText("Red = your best brakes"), Lb, p.P.DangerText, new RectangleF(bar.X, bar.Y - lh - 3 * s, bar.Width, lh), HAlign.Right);
            for (int i = 1; i <= 3; i++)
                p.Text(Boards[i - 1], Lb, p.P.Muted, new RectangleF(bar.X + bar.Width * i / 4f - 20 * s, by, 40 * s, lh), HAlign.Center);
        }
        else
        {
            p.Text(p.LabelText(phase == "In" ? g.First.Brake is null ? "Turn in" : "Braking" : "Slowest point"), Lb, p.P.Muted, new RectangleF(bar.X, by, bar.Width / 2, lh));
            string end = phase == "In" ? g.Several ? "Slowest point of the last turn" : "Slowest point" : g.Last.FullThrottle is null ? "End of the turn" : "Your best on full throttle";
            p.Text(p.LabelText(end), Lb, p.P.DangerText, new RectangleF(bar.X + bar.Width / 2, by, bar.Width / 2, lh), HAlign.Right);
        }
    }

    private static readonly string[] LegendWords = { "braking", "part throttle", "full throttle" };

    private static readonly string[] Boards = { "150", "100", "50" };

    private static void Pedal(HudPainter p, RectangleF r, double you, double best, Color youColor, Color bestColor)
    {
        p.Fill(r, p.P.SurfaceAlt, 3 * p.Density);
        float h = (float)(r.Height * Math.Clamp(you, 0, 1));
        if (h > 0.5f) p.Fill(new RectangleF(r.X, r.Bottom - h, r.Width, h), youColor, 3 * p.Density);
        float by = r.Bottom - (float)(r.Height * Math.Clamp(best, 0, 1));
        p.Fill(new RectangleF(r.X - 2 * p.Density, by - 1.5f * p.Density, r.Width + 4 * p.Density, 3 * p.Density), bestColor);
    }

    private void DrawNext(HudPainter p, RectangleF r, CoachGroup g, Color you)
    {
        var plan = _plan!;
        float lh = p.LineHeight(Lb);
        p.Text(p.LabelText(g.Label), Lb, p.P.Muted, new RectangleF(r.X, r.Y, r.Width, lh));
        var box = new RectangleF(r.X, r.Y + lh + 4 * p.Density, r.Width, r.Height - lh - 4 * p.Density);
        double a = Math.Max(0, g.From - 60), bEnd = Math.Min(plan.Length, g.To + 20);
        var key = (plan, g.Index, box.X, box.Y, box.Width, box.Height);
        if (!Equals(_nextKey, key))
        {
            _nextKey = key;
            var (lx, ly) = plan.Line(plan.Best);
            (_nextLine, _nextAt) = Project(lx, ly, plan.Step, a, bEnd, box, 4);
        }
        if (_nextLine.Length > 1) p.G.DrawLines(p.Pen(p.P.LineStrong, 5 * p.Density), _nextLine);
        foreach (var t in g.Turns)
            if (t.Brake is { } br) { var q = _nextAt(br); p.Dot(q.X, q.Y, 3 * p.Density, p.P.Danger); }
        if (_d >= a && _d <= bEnd) { var c = _nextAt(_d); p.Dot(c.X, c.Y, 4 * p.Density, you); }
    }

    private static (PointF[] Line, Func<double, PointF> At) Project(double[] x, double[] y, double step, double from, double to, RectangleF box, double every)
    {
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
        for (double d = from; d <= to; d += step)
        {
            double px = LapReplay.At(x, step, d), py = LapReplay.At(y, step, d);
            x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
        }
        double k = Math.Min(box.Width / Math.Max(1, x1 - x0), box.Height / Math.Max(1, y1 - y0));
        double ox = box.X + (box.Width - (x1 - x0) * k) / 2, oy = box.Y + (box.Height - (y1 - y0) * k) / 2;
        PointF At(double d) => new((float)(ox + (LapReplay.At(x, step, d) - x0) * k), (float)(oy + (y1 - LapReplay.At(y, step, d)) * k));
        var points = new List<PointF>();
        for (double d = from; d < to; d += every) points.Add(At(d));
        points.Add(At(to));
        return (points.ToArray(), At);
    }

    private float LegendHeight(HudPainter p, float width)
    {
        float s = p.Density, sw = 18 * s, row = p.LineHeight(Tx) + 2 * s, lx = p.Measure("Your best, wide:", Tx) + 12 * s;
        int rows = 2;
        for (int i = 0; i < LegendWords.Length; i++)
        {
            float need = sw + 5 * s + p.Measure(LegendWords[i], Tx) + 12 * s;
            if (lx + need > width) { rows++; lx = 0; }
            lx += need;
        }
        return rows * row + 8 * s;
    }

    private void DrawLine(HudPainter p, HudView v)
    {
        float s = p.Density;
        var o = v.Options;
        var plan = _plan!;
        var refLap = plan.TurnAt(_d).Reference;
        double off = _hasLine && _xs is not null ? plan.Offset(_xs, _ys!, Math.Min(_d, _filled), refLap) : double.NaN;
        string right = double.IsNaN(off) ? "Your line isn't available" : Side(off);
        var body = p.Header("Racing line", right, double.IsNaN(off) ? p.P.Muted : Math.Abs(off) < OnLineM ? p.P.GoodText : p.P.Text);
        var full = new RectangleF(p.Bounds.X, body.Y, p.Bounds.Width, body.Bottom - body.Y);
        _sizes[0] = 0;
        _sizes[1] = o.Bool("speed") ? p.LineHeight(Vb) + 12 * s : -1;
        _sizes[2] = o.Bool("legend") ? LegendHeight(p, body.Width) : -1;
        p.Arrange(v, "coach.line", full, false, 0, LineIds, _sizes.AsSpan(0, 3), _boxes.AsSpan(0, 3));
        var you = Pick(v, "you", p.P.Accent);
        if (_boxes[0].Height > 0) LineMap(p, v, _boxes[0], you);
        if (_boxes[1].Height > 0)
        {
            var r = _boxes[1];
            p.Seam(p.Bounds, r.Y);
            float y = r.Y + 6 * s, sh = p.LineHeight(Vb), x = body.X, w = body.Width;
            Stat(p, new RectangleF(x, y, w * 0.38f, sh), "Speed", RaceFormat.Speed(_speed, v.SpeedUnits));
            Stat(p, new RectangleF(x + w * 0.38f, y, w * 0.44f, sh), "Your best", RaceFormat.Speed(refLap.At(TraceChannel.Speed, _d) ?? 0, v.SpeedUnits));
            Stat(p, new RectangleF(x + w * 0.82f, y, w * 0.18f, sh), "Gear", _gear > 0 ? PooledText.Number("", _gear) : RaceFormat.Missing);
        }
        if (_boxes[2].Height > 0)
        {
            var r = _boxes[2];
            if (_boxes[1].Height <= 0 || _boxes[1].Y > r.Y) p.Seam(p.Bounds, r.Y);
            float sw = 18 * s, x = body.X, edge = x + body.Width, row = p.LineHeight(Tx) + 2 * s, y = r.Y + 4 * s;
            float lx = Swatch(p, x, y, row, "Your best, wide:", null, 0);
            for (int i = 0; i < LegendWords.Length; i++)
            {
                string word = LegendWords[i];
                var col = PhaseColor(p, v, i);
                if (lx + sw + p.Measure(word, Tx) + 5 * s > edge) { lx = x; y += row; }
                lx = Swatch(p, lx, y, row, word, col, sw);
            }
            Swatch(p, x, y + row, row, "This lap, thin", you, sw);
        }
    }

    private static Color PhaseColor(HudPainter p, HudView v, int i) => i switch
    {
        0 => Pick(v, "brakeColor", p.P.Danger),
        1 => Pick(v, "partColor", p.P.Warn),
        _ => Pick(v, "fullColor", p.P.Good),
    };

    private void LineMap(HudPainter p, HudView v, RectangleF area, Color you)
    {
        float s = p.Density;
        var o = v.Options;
        var plan = _plan!;
        var g = p.G;
        var state = g.Save();
        g.SetClip(area);
        var (bx, by) = plan.Line(plan.Best);
        double h = Math.Atan2(LapReplay.At(by, plan.Step, Math.Min(plan.Length, _d + 12)) - LapReplay.At(by, plan.Step, Math.Max(0, _d - 12)),
            LapReplay.At(bx, plan.Step, Math.Min(plan.Length, _d + 12)) - LapReplay.At(bx, plan.Step, Math.Max(0, _d - 12)));
        bool north = o.Choice("direction") == "north";
        double want = north ? 0 : Math.PI / 2 - h;
        if (double.IsNaN(_viewAngle)) _viewAngle = want;
        _viewAngle += Math.IEEERemainder(want - _viewAngle, 2 * Math.PI) * (north ? 1 : 0.12);
        double target = o.Choice("zoom") switch
        {
            "close" => 4.2,
            "far" => 1.4,
            _ => Math.Clamp(4 - (_speed * 3.6 - 70) / 150 * 2.4, 1.6, 4),
        } * s;
        _viewScale += (target - _viewScale) * 0.05;
        float CX = area.X + area.Width / 2, CY = area.Y + area.Height * (north ? 0.5f : 0.7f);
        if (o.Bool("grid"))
        {
            float step25 = (float)(25 * _viewScale);
            if (step25 > 4)
                for (float gy = CY - step25 * MathF.Floor((CY - area.Y) / step25); gy <= area.Bottom; gy += step25)
                    p.Line(area.X, gy, area.Right, gy, p.P.Line, 1);
        }
        double cxw = _hasLine && _xs is not null && _filled >= 0 ? LapReplay.At(_xs, plan.Step, Math.Min(_d, _filled)) : LapReplay.At(bx, plan.Step, _d);
        double cyw = _hasLine && _ys is not null && _filled >= 0 ? LapReplay.At(_ys, plan.Step, Math.Min(_d, _filled)) : LapReplay.At(by, plan.Step, _d);
        if (!double.IsFinite(cxw) || !double.IsFinite(cyw)) { cxw = LapReplay.At(bx, plan.Step, _d); cyw = LapReplay.At(by, plan.Step, _d); }
        double ca = Math.Cos(_viewAngle), sa = Math.Sin(_viewAngle), scale = _viewScale;
        PointF P(double wx, double wy)
        {
            double dx = wx - cxw, dy = wy - cyw;
            return new PointF((float)(CX + (dx * ca - dy * sa) * scale), (float)(CY - (dx * sa + dy * ca) * scale));
        }
        double from = Math.Max(0, _d - BehindM), to = Math.Min(plan.Length - plan.Step, _d + AheadM);
        var braking = p.Pen(HudPalette.Alpha(PhaseColor(p, v, 0), 150), 9 * s);
        var part = p.Pen(HudPalette.Alpha(PhaseColor(p, v, 1), 150), 9 * s);
        var fullPen = p.Pen(HudPalette.Alpha(PhaseColor(p, v, 2), 150), 9 * s);
        double step = 2 * plan.Step;
        for (double d = from; d < to; d += step)
        {
            var r = plan.TurnAt(d).Reference;
            var (rx, ry) = plan.Line(r);
            double br = LapReplay.At(r[TraceChannel.Brake]!, r.StepM, d), th = LapReplay.At(r[TraceChannel.Throttle]!, r.StepM, d);
            g.DrawLine(br > 0.05 ? braking : th >= CoachPlan.FullOn ? fullPen : part,
                P(LapReplay.At(rx, plan.Step, d), LapReplay.At(ry, plan.Step, d)), P(LapReplay.At(rx, plan.Step, d + step), LapReplay.At(ry, plan.Step, d + step)));
        }
        if (_hasLine && _xs is not null && _ys is not null)
        {
            int n = 0;
            for (double d = Math.Max(0, _d - TrailM); d <= Math.Min(_d, _filled); d += step)
            {
                double px = LapReplay.At(_xs, plan.Step, d), py = LapReplay.At(_ys, plan.Step, d);
                if (!double.IsFinite(px) || !double.IsFinite(py)) continue;
                if (n == _points.Length) Array.Resize(ref _points, n * 2);
                _points[n++] = P(px, py);
            }
            if (n > 1)
            {
                if (_trail.Length != n) _trail = new PointF[n];
                Array.Copy(_points, _trail, n);
                g.DrawLines(p.Pen(p.P.Bg, 6 * s), _trail);
                g.DrawLines(p.Pen(you, 3 * s), _trail);
            }
        }
        foreach (var t in plan.Turns)
        {
            if (t.Apex < from || t.Apex > to) continue;
            var (rx, ry) = plan.Line(t.Reference);
            if (t.Brake is { } b && b > from && b < to) { var q = P(LapReplay.At(rx, plan.Step, b), LapReplay.At(ry, plan.Step, b)); p.Dot(q.X, q.Y, 4 * s, PhaseColor(p, v, 0)); }
            if (!o.Bool("chips")) continue;
            var low = CoachPlan.Lowest(t.Reference[TraceChannel.Speed]!, t.Reference.StepM, t.Apex);
            var at = P(LapReplay.At(rx, plan.Step, low.At), LapReplay.At(ry, plan.Step, low.At));
            string chip = TurnChip(t, v.Units);
            float tw = p.Measure(chip, TxB) + 16 * s, chH = 26 * s;
            var box = new RectangleF(Math.Clamp(at.X + 10 * s, area.X + 4 * s, area.Right - tw - 4 * s), Math.Clamp(at.Y - chH / 2, area.Y + 4 * s, area.Bottom - chH - 4 * s), tw, chH);
            p.Fill(box, p.P.Bg, chH / 2);
            p.Outline(box, p.P.LineStrong, 1, chH / 2);
            p.Text(chip, TxB, p.P.Heading, box, HAlign.Center);
        }
        var tip = P(cxw + Math.Cos(h), cyw + Math.Sin(h));
        float ux = tip.X - CX, uy = tip.Y - CY, len = MathF.Sqrt(ux * ux + uy * uy);
        if (len > 0) { ux /= len; uy /= len; } else { ux = 0; uy = -1; }
        float nx = -uy, ny = ux;
        _arrow[0] = new PointF(CX + ux * 12 * s, CY + uy * 12 * s);
        _arrow[1] = new PointF(CX - ux * 9 * s + nx * 8 * s, CY - uy * 9 * s + ny * 8 * s);
        _arrow[2] = new PointF(CX - ux * 5 * s, CY - uy * 5 * s);
        _arrow[3] = new PointF(CX - ux * 9 * s - nx * 8 * s, CY - uy * 9 * s - ny * 8 * s);
        g.FillPolygon(p.Brush(you), _arrow);
        g.DrawPolygon(p.Pen(p.P.Bg, 2 * s), _arrow);
        g.Restore(state);
    }

    private readonly PointF[] _arrow = new PointF[4];
    private PointF[] _trail = Array.Empty<PointF>();

    private float Swatch(HudPainter p, float x, float y, float h, string text, Color? c, float w)
    {
        if (c is { } col)
        {
            p.Fill(new RectangleF(x, y + h / 2 - 2.5f * p.Density, w, 5 * p.Density), col, 2 * p.Density);
            x += w + 5 * p.Density;
        }
        float tw = p.Measure(text, Tx);
        p.Text(text, Tx, p.P.Text, new RectangleF(x, y, tw + 2, h));
        return x + tw + 12 * p.Density;
    }

    private void Stat(HudPainter p, RectangleF r, string label, string value)
    {
        string caps = p.LabelText(label);
        float lw = p.Measure(caps, Lb) + 5 * p.Density;
        p.Text(caps, Lb, p.P.Muted, new RectangleF(r.X, r.Y, lw, r.Height));
        p.Text(value, Vb, p.P.Heading, new RectangleF(r.X + lw, r.Y, r.Width - lw, r.Height));
    }

    private readonly Dictionary<(int, HudUnits), string> _chips = new();

    private string TurnChip(PlanTurn t, HudUnits u)
    {
        if (_chips.TryGetValue((t.Number, u), out var c) && ReferenceEquals(_verdictPlan, _plan)) return c;
        if (_chips.Count > 64) _chips.Clear();
        return _chips[(t.Number, u)] = $"Turn {t.Number} · best {RaceFormat.Speed(t.MinSpeed, u)} · gear {t.Gear}";
    }

    private static string Side(double m) => Math.Abs(m) < OnLineM ? "On your best line"
        : PooledText.Number("", Math.Abs(m), "0.0", CultureInfo.CurrentCulture, m > 0 ? " " + Units.M + " left of your best line" : " " + Units.M + " right of your best line");

    private static string Steer(double rad)
    {
        double deg = rad * 180 / Math.PI;
        return Math.Abs(deg) < 2 ? "Centred" : PooledText.Number("", Math.Abs(deg), "0", CultureInfo.CurrentCulture, deg < 0 ? "° right" : "° left");
    }

    private static string OnLine(double m) => Math.Abs(m) < OnLineM ? "On best line"
        : PooledText.Number("", Math.Abs(m), "0.0", CultureInfo.CurrentCulture, m > 0 ? " " + Units.M + " left of best line" : " " + Units.M + " right of best line");

    public const float PairH = 70, SliderH = 82, SlowestH = 46, RowGap = 12;

    private float RowHeight(HudPainter p, int i)
    {
        float s = p.Density, lh = p.LineHeight(Lb), bh = p.LineHeight(Vb);
        return i switch
        {
            0 or 1 => lh + 2 * s + 2 * (bh + 2 * s),
            5 => lh + 2 * s + bh,
            _ => lh + 2 * s + 18 * s + lh + 2 * s + bh,
        } + RowGap * s;
    }

    private void DrawCompare(HudPainter p, HudView v)
    {
        float s = p.Density;
        var o = v.Options;
        var plan = _plan!;
        var r = plan.TurnAt(_d).Reference;
        var body = p.Header("Compare with your best");
        var rows = o.Columns("rows");
        for (int i = 0; i < CompareIds.Length; i++) _sizes[i] = rows.Contains(CompareIds[i]) ? RowHeight(p, i) : -1;
        p.Arrange(v, "coach.compare", new RectangleF(body.X, body.Y + 2 * s, body.Width, body.Height), false, 0, CompareIds, _sizes.AsSpan(0, 6), _boxes.AsSpan(0, 6));
        var you = Pick(v, "you", p.P.Accent);
        var best = Pick(v, "best", p.P.Muted);
        for (int i = 0; i < CompareIds.Length; i++)
        {
            var box = _boxes[i];
            if (box.Height <= 0) continue;
            float x = box.X, y = box.Y, w = box.Width;
            switch (i)
            {
                case 0: Pair(p, x, y, w, "Brake", _brake, r.At(TraceChannel.Brake, _d) ?? 0, you, best); break;
                case 1: Pair(p, x, y, w, "Throttle", _throttle, r.At(TraceChannel.Throttle, _d) ?? 0, you, best); break;
                case 2:
                    double bs = r.At(TraceChannel.Steering, _d) ?? 0;
                    y = Slider(p, x, y, w, "Steering", "Left", "Right", Math.Clamp(-_steer * 180 / Math.PI / SteerRangeDeg, -1, 1), Math.Clamp(-bs * 180 / Math.PI / SteerRangeDeg, -1, 1), you, best);
                    Values(p, x, y, w, Now, Steer(_steer), p.P.Heading, Steer(bs));
                    break;
                case 3:
                    double off = _hasLine && _xs is not null ? plan.Offset(_xs, _ys!, Math.Min(_d, _filled), r) : double.NaN;
                    y = Slider(p, x, y, w, "Line", "4 m left", "4 m right", double.IsNaN(off) ? double.NaN : Math.Clamp(-off / LineRangeM, -1, 1), 0, you, best);
                    Values(p, x, y, w, Now, double.IsNaN(off) ? "Not available" : OnLine(off), double.IsNaN(off) ? p.P.Muted : Math.Abs(off) < OnLineM ? p.P.GoodText : p.P.Heading, null);
                    break;
                case 4: BrakePoint(p, v, x, y, w, you, best); break;
                default: Slowest(p, v, x, y, w, plan); break;
            }
        }
    }

    private void BrakePoint(HudPainter p, HudView v, float x, float y, float w, Color you, Color best)
    {
        var (g, _, _, _, _) = Phase();
        var c = g.First;
        double? mine = null;
        if (_lap is not null && c.Brake is not null)
        {
            var on = LapReplay.Onsets(_lap[TraceChannel.Brake]!, _lap.StepM, CoachPlan.BrakeOn, CoachPlan.BrakeOff, CoachPlan.BrakeQuietM);
            foreach (double o in on) if (o > c.PrevApex && o <= c.Apex && o <= _filled) { mine = o; break; }
        }
        double? diff = mine is { } m2 && c.Brake is { } rb ? m2 - rb : null;
        string text = c.Brake is null ? "No braking here" : diff is not { } dv ? "Not braked yet"
            : Math.Abs(dv) < CoachPlan.SameBrakeM ? "Same as best"
            : PooledText.Concat(RaceFormat.Distance(Math.Abs(dv), v.Units), dv < 0 ? " early" : " late");
        var color = diff is { } df ? Math.Abs(df) < CoachPlan.SameBrakeM ? p.P.GoodText : df < 0 ? p.P.DangerText : p.P.Heading : p.P.Muted;
        y = Slider(p, x, y, w, PooledText.Number("Brake point · Turn ", c.Number), "30 m earlier", "30 m later", diff is { } dd ? Math.Clamp(dd / BrakeRangeM, -1, 1) : double.NaN, 0, you, best);
        Values(p, x, y, w, ThisLap, text, color, null);
    }

    private void Slowest(HudPainter p, HudView v, float x, float y, float w, CoachPlan plan)
    {
        PlanTurn? last = null;
        foreach (var t in plan.Turns) if (t.Apex + 30 <= _d) last = t;
        float s = p.Density, lh = p.LineHeight(Lb);
        p.Text(p.LabelText(last is null ? "Slowest in the last turn" : PooledText.Number("Slowest in Turn ", last.Number)), Lb, p.P.Muted, new RectangleF(x, y, w, lh));
        y += lh + 2 * s;
        if (last is null || _lap is null) { Values(p, x, y, w, ThisLap, "No turn yet", p.P.Muted, null); return; }
        double slowest = CoachPlan.Lowest(_lap[TraceChannel.Speed]!, _lap.StepM, last.Apex).Speed, gap = slowest - last.MinSpeed;
        Values(p, x, y, w, ThisLap, RaceFormat.Speed(slowest, v.SpeedUnits), Math.Abs(gap) < CoachPlan.SameSpeedMs ? p.P.Heading : gap < 0 ? p.P.DangerText : p.P.GoodText,
            RaceFormat.Speed(last.MinSpeed, v.SpeedUnits));
    }

    private const string Now = "Now", ThisLap = "This lap";

    private float Values(HudPainter p, float x, float y, float w, string who, string you, Color youColor, string? best)
    {
        float s = p.Density, bh = p.LineHeight(Vb);
        string youCaps = p.LabelText(who), bestCaps = p.LabelText("Best");
        float yw = p.Measure(youCaps, Lb) + 6 * s, half = best is null ? w : w * 0.52f;
        p.Text(youCaps, Lb, p.P.AccentText, new RectangleF(x, y, yw, bh));
        p.Text(you, Vb, youColor, new RectangleF(x + yw, y, half - yw, bh));
        if (best is not null)
        {
            float bw = p.Measure(bestCaps, Lb) + 6 * s;
            p.Text(bestCaps, Lb, p.P.Muted, new RectangleF(x + half, y, bw, bh));
            p.Text(best, Vr, p.P.Muted, new RectangleF(x + half + bw, y, w - half - bw, bh));
        }
        return y + bh;
    }

    private float Pair(HudPainter p, float x, float y, float w, string label, double you, double best, Color youColor, Color bestColor)
    {
        float s = p.Density, lh = p.LineHeight(Lb), sh = p.LineHeight(Vb), who = 58 * s, num = 52 * s, barH = 12 * s;
        p.Text(p.LabelText(label), Lb, p.P.Muted, new RectangleF(x, y, w, lh));
        y += lh + 2 * s;
        for (int i = 0; i < 2; i++)
        {
            double value = i == 0 ? you : best;
            var c = i == 0 ? youColor : bestColor;
            p.Text(p.LabelText(i == 0 ? Now : "Best"), Lb, i == 0 ? p.P.AccentText : p.P.Muted, new RectangleF(x, y, who, sh));
            var bar = new RectangleF(x + who, y + (sh - barH) / 2, w - who - num, barH);
            p.Fill(bar, p.P.SurfaceAlt, 3 * s);
            float fw = (float)(bar.Width * Math.Clamp(value, 0, 1));
            if (fw > 0.5f) p.Fill(new RectangleF(bar.X, bar.Y, fw, bar.Height), c, 3 * s);
            p.Text(PooledText.Number("", Math.Round(Math.Clamp(value, 0, 1) * 100), "0", Inv, "%"), i == 0 ? Vb : Vr, i == 0 ? p.P.Heading : p.P.Muted,
                new RectangleF(bar.Right, y, num, sh), HAlign.Right);
            y += sh + 2 * s;
        }
        return y;
    }

    private float Slider(HudPainter p, float x, float y, float w, string label, string left, string right, double you, double best, Color youColor, Color bestColor)
    {
        float s = p.Density, lh = p.LineHeight(Lb);
        p.Text(p.LabelText(label), Lb, p.P.Muted, new RectangleF(x, y, w, lh));
        y += lh + 2 * s;
        float mid = y + 8 * s;
        p.Fill(new RectangleF(x, mid - 1, w, 2 * s), p.P.SurfaceAlt);
        p.Fill(new RectangleF(x + w / 2, mid - 4 * s, 1, 8 * s), p.P.LineStrong);
        if (!double.IsNaN(best)) p.Fill(new RectangleF(x + w / 2 + (float)(best * w / 2) - 3 * s, mid - 7 * s, 6 * s, 14 * s), bestColor, 3 * s);
        if (!double.IsNaN(you))
        {
            var r = new RectangleF(x + w / 2 + (float)(you * w / 2) - 3 * s, mid - 8 * s, 6 * s, 16 * s);
            p.Outline(r.Inflated(1.5f * s, 1.5f * s), p.P.Bg, 2 * s, 4 * s);
            p.Fill(r, youColor, 3 * s);
        }
        y += 18 * s;
        p.Text(p.LabelText(left), Lb, p.P.Faint, new RectangleF(x, y, w / 2, lh));
        p.Text(p.LabelText(right), Lb, p.P.Faint, new RectangleF(x + w / 2, y, w / 2, lh), HAlign.Right);
        return y + lh + 2 * s;
    }

    private void DrawDebrief(HudPainter p, HudView v)
    {
        float s = p.Density;
        var o = v.Options;
        var plan = _plan!;
        float sh = p.LineHeight(Vb);
        double nextBrake = double.MaxValue;
        foreach (var t in plan.Turns) if (t.Brake is { } b && b > _d) { nextBrake = b - _d; break; }
        if (nextBrake == double.MaxValue && plan.Turns[0].Brake is { } first) nextBrake = plan.Length - _d + first;
        bool list = o.Bool("lost") || o.Bool("gained"), anyBody = o.Bool("map") || list;
        bool open = anyBody && (!o.Bool("straights") || _throttle >= CoachPlan.FullOn && nextBrake >= StraightM);
        float headH = 2 * (sh + 6 * s) + 4 * s;
        var body = p.Header("Lap debrief", null, null, open ? float.NaN : headH);
        float x = body.X, w = body.Width, y = body.Y;
        double gap = Gap();
        Stat(p, new RectangleF(x, y, w * 0.58f, sh), "This lap, on pace for", RaceFormat.LapTime(plan.BestTurnsTogether + gap));
        p.Text(Behind(gap), TxB, Tone3(p, gap), new RectangleF(x + w * 0.58f, y, w * 0.42f, sh), HAlign.Right);
        y += sh + 6 * s;
        Stat(p, new RectangleF(x, y, w, sh), "Your best turns together", RaceFormat.LapTime(plan.BestTurnsTogether));
        y += sh + 10 * s;
        if (!open) return;
        _sizes[0] = o.Bool("map") ? list ? w * 0.48f : 0 : -1;
        _sizes[1] = list ? 0 : -1;
        p.Arrange(v, "coach.debrief", new RectangleF(x, y, w, body.Bottom - y), true, 12 * s, DebriefIds, _sizes.AsSpan(0, 2), _boxes.AsSpan(0, 2));
        var lostColor = Pick(v, "lostColor", p.P.Danger);
        var gainedColor = Pick(v, "gainedColor", p.P.Good);
        if (_boxes[0].Width > 0) DebriefMap(p, _boxes[0], lostColor, gainedColor, Pick(v, "you", p.P.Accent));
        if (_boxes[1].Width > 0) DebriefList(p, v, _boxes[1], lostColor, gainedColor);
    }

    private void DebriefMap(HudPainter p, RectangleF mapBox, Color lostColor, Color gainedColor, Color you)
    {
        float s = p.Density;
        var plan = _plan!;
        var key = (plan, mapBox.X, mapBox.Y, mapBox.Width, mapBox.Height);
        if (!Equals(_mapKey, key))
        {
            _mapKey = key;
            var (lx, ly) = plan.Line(plan.Best);
            var (all, at) = Project(lx, ly, plan.Step, 0, plan.Length - plan.Step, mapBox, 8);
            _mapLine = all;
            _mapAt = at;
            _mapPieces.Clear();
            foreach (var t in plan.Turns)
            {
                var piece = new List<PointF>();
                for (double d = t.From; d < Math.Min(t.To, plan.Length - plan.Step); d += 8) piece.Add(at(d));
                piece.Add(at(Math.Min(t.To, plan.Length - plan.Step)));
                _mapPieces.Add(piece.ToArray());
            }
        }
        if (_mapLine.Length > 1) p.G.DrawLines(p.Pen(p.P.SurfaceAlt, 8 * s), _mapLine);
        foreach (var t in plan.Turns)
        {
            var piece = _mapPieces[t.Index];
            if (piece.Length < 2) continue;
            var res = Result(t);
            var col = res is null ? _d >= t.From ? p.P.Heading : p.P.LineStrong : res.Lost > CoachPlan.EvenS ? lostColor : res.Lost < -CoachPlan.EvenS ? gainedColor : p.P.Muted;
            p.G.DrawLines(p.Pen(col, 4.5f * s), piece);
        }
        var car = _mapAt(Math.Min(_d, plan.Length - plan.Step));
        p.Dot(car.X, car.Y, 4.5f * s, you);
    }

    private void DebriefList(HudPainter p, HudView v, RectangleF r, Color lostColor, Color gainedColor)
    {
        float s = p.Density, lh = p.LineHeight(Lb), sh = p.LineHeight(Tx), x = r.X, w = r.Width, y = r.Y;
        if (v.Options.Bool("lost"))
        {
            p.Text(p.LabelText("Most time lost this lap"), Lb, p.P.Muted, new RectangleF(x, y, w, lh));
            y += lh + 2 * s;
            if (_worst.Count == 0)
            {
                p.Text("Nothing lost yet", Tx, p.P.Muted, new RectangleF(x, y, w, sh));
                y += sh + 4 * s;
            }
            int most = v.Options.Int("listed");
            for (int i = 0; i < _worst.Count && i < most; i++)
            {
                var (label, lost, why) = _worst[i];
                string head = PooledText.Concat(label, ": lost ", PooledText.Number("", lost, "0.00", CultureInfo.CurrentCulture, " " + Units.S));
                p.Text(head, TxB, lostColor, new RectangleF(x, y, w, sh));
                y += sh;
                if (why.Length > 0) { p.Text(why, Tx, p.P.Muted, new RectangleF(x, y, w, sh)); y += sh; }
                y += 3 * s;
            }
        }
        if (!v.Options.Bool("gained")) return;
        p.Text(p.LabelText("Gained"), Lb, p.P.Muted, new RectangleF(x, y, w, lh));
        y += lh + 2 * s;
        string gained = _gainedTurns == 0 ? "Nothing gained yet"
            : PooledText.Concat("Gained ", PooledText.Number("", -_gained, "0.00", CultureInfo.CurrentCulture, " " + Units.S + " in "), RaceFormat.Count(_gainedTurns, "turn"));
        p.Text(gained, Tx, _gainedTurns == 0 ? p.P.Muted : gainedColor, new RectangleF(x, y, w, sh));
    }
}
