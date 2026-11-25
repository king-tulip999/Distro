using Core.SessionStats;

namespace Core.SimRacing.Coach;

internal enum CoachCallKind { BrakeLater, CarrySpeed, ThrottleSooner, NoClearCall, KeepItTheSame }

internal sealed record CoachCall(CoachCallKind Kind, double Size)
{
    public bool Quiet => Kind is CoachCallKind.NoClearCall or CoachCallKind.KeepItTheSame;

    public string Title => Kind switch
    {
        CoachCallKind.BrakeLater => "Brake later",
        CoachCallKind.CarrySpeed => "Carry more speed",
        CoachCallKind.ThrottleSooner => "Full throttle sooner",
        CoachCallKind.NoClearCall => "No clear call",
        _ => "Keep it the same",
    };
}

internal sealed class PlanTurn
{
    public required int Index { get; init; }
    public required int Number { get; init; }
    public required double Apex { get; init; }
    public required double From { get; init; }
    public required double To { get; init; }
    public required double PrevApex { get; init; }
    public required double NextApex { get; init; }
    public LapTrace Reference { get; internal set; } = null!;
    public double? Brake { get; internal set; }
    public double MinSpeed { get; internal set; }
    public int Gear { get; internal set; }
    public double? FullThrottle { get; internal set; }
    public double BestTime { get; internal set; }
    public double WorstTime { get; internal set; }
    public double AverageLost { get; internal set; }
    public double? UsualBrake { get; internal set; }
    public CoachGroup Group { get; internal set; } = null!;
}

internal sealed class CoachGroup
{
    public required int Index { get; init; }
    public required IReadOnlyList<PlanTurn> Turns { get; init; }
    public PlanTurn First => Turns[0];
    public PlanTurn Last => Turns[^1];
    public bool Several => Turns.Count > 1;
    public double From => First.From;
    public double To => Last.To;
    public double End => Last.FullThrottle is { } f ? Math.Min(Last.To, f + CoachPlan.ExitHoldM) : Last.To;
    public string Label => Several ? $"Turns {First.Number}–{Last.Number}" : $"Turn {First.Number}";
    public CoachCall Call { get; internal set; } = new(CoachCallKind.KeepItTheSame, 0);
    public PlanTurn CallTurn { get; internal set; } = null!;
    public double Usual { get; internal set; }
}

internal sealed record CoachTurnResult(double Lost, double Entry, double? Brake, double MinSpeed, double? FullThrottle)
{
    public double Exit => Lost - Entry;
}

internal sealed class CoachPlan
{
    public const int LeastLaps = 3;
    public const double BrakeOn = 0.15, BrakeOff = 0.05, BrakeQuietM = 30, FullOn = 0.98, ApexWindowM = 60;
    public const double BendDegPerM = 0.3, BendLeastDeg = 20, BendJoinM = 16, HeadingSpanM = 20;
    public const double GroupGapS = 5, ExitHoldM = 40;
    public const double SameBrakeM = 3, SameSpeedMs = 1 / 3.6, SameThrottleM = 4, LeastCostS = 0.05, EvenS = 0.02;

    public required IReadOnlyList<LapTrace> Laps { get; init; }
    public required IReadOnlyList<PlanTurn> Turns { get; init; }
    public IReadOnlyList<CoachGroup> Groups { get; private set; } = Array.Empty<CoachGroup>();
    public required double Length { get; init; }
    public required double Step { get; init; }
    public required LapTrace Best { get; init; }
    public required double BestTurnsTogether { get; init; }
    private double[] _refTime = Array.Empty<double>();
    private readonly Dictionary<LapTrace, (double[] X, double[] Y)> _lines = new();

    public (double[] X, double[] Y) Line(LapTrace lap) => _lines[lap];

    public double Offset(double[] x, double[] y, double d, LapTrace reference) => Offset(x, y, d, _lines[reference], Step);

    public static double Offset(double[] x, double[] y, double d, (double[] X, double[] Y) line, double step)
    {
        double px = LapReplay.At(x, step, d), py = LapReplay.At(y, step, d);
        var (rx, ry) = line;
        int i0 = (int)Math.Round(d / step), best = i0;
        double nearest = double.MaxValue;
        for (int j = Math.Max(1, i0 - 40); j <= Math.Min(rx.Length - 2, i0 + 40); j++)
        {
            double q = (rx[j] - px) * (rx[j] - px) + (ry[j] - py) * (ry[j] - py);
            if (q < nearest) { nearest = q; best = j; }
        }
        best = Math.Clamp(best, 1, rx.Length - 2);
        double hx = rx[best + 1] - rx[best - 1], hy = ry[best + 1] - ry[best - 1], n = Math.Sqrt(hx * hx + hy * hy);
        return n > 0 ? (hx * (py - ry[best]) - hy * (px - rx[best])) / n : 0;
    }

    private static (double[] X, double[] Y) Aligned(LapTrace lap, LapTrace best)
    {
        var (x, y) = lap.LineIn(best)!.Value;
        var bx = best[TraceChannel.X]!;
        var by = best[TraceChannel.Y]!;
        int span = (int)Math.Round(60 / best.StepM);
        var dx = new List<double>();
        var dy = new List<double>();
        for (int i = 0; i < x.Length; i += 5)
        {
            int lo = Math.Max(0, i - span), hi = Math.Min(bx.Length - 1, i + span), at = lo;
            double nearest = double.MaxValue;
            for (int j = lo; j <= hi; j++)
            {
                double q = (bx[j] - x[i]) * (bx[j] - x[i]) + (by[j] - y[i]) * (by[j] - y[i]);
                if (q < nearest) { nearest = q; at = j; }
            }
            dx.Add(bx[at] - x[i]);
            dy.Add(by[at] - y[i]);
        }
        double sx = Median(dx), sy = Median(dy);
        return (x.Select(v => v + sx).ToArray(), y.Select(v => v + sy).ToArray());
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    public double ReferenceTime(double d)
    {
        d = Math.Clamp(d, 0, Length);
        double last = (_refTime.Length - 1) * Step;
        if (d <= last) return LapReplay.At(_refTime, Step, d);
        var r = Turns[^1].Reference;
        return _refTime[^1] + r.TimeAt(d) - r.TimeAt(last);
    }

    public double ReferenceDistance(double time)
    {
        var t = _refTime;
        if (time <= 0) return 0;
        if (time >= t[^1]) return Length;
        int lo = 0, hi = t.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (t[mid] <= time) lo = mid; else hi = mid; }
        double f = t[hi] > t[lo] ? (time - t[lo]) / (t[hi] - t[lo]) : 0;
        return (lo + f) * Step;
    }

    public PlanTurn TurnAt(double d)
    {
        foreach (var t in Turns) if (d < t.To) return t;
        return Turns[^1];
    }

    public CoachGroup GroupAhead(double d)
    {
        foreach (var g in Groups)
        {
            if (g.First.Brake is { } b && d < b) return g;
            if (d < g.End) return g;
        }
        return Groups[0];
    }

    public static CoachPlan? Build(IReadOnlyList<LapTrace> clean)
    {
        var laps = clean.Where(Usable).ToList();
        if (laps.Count < LeastLaps) return null;
        var best = laps.MinBy(l => l.LapTime)!;
        double step = best.StepM;
        if (laps.Any(l => Math.Abs(l.StepM - step) > 1e-9)) laps = laps.Where(l => Math.Abs(l.StepM - step) <= 1e-9).ToList();
        if (laps.Count < LeastLaps) return null;
        double length = laps.Min(l => l.LengthM);
        var bends = Bends(best[TraceChannel.X]!, best[TraceChannel.Y]!, step);
        var speed = best[TraceChannel.Speed]!;
        var apexes = bends.Select(b => Slowest(speed, step, b.From, b.To)).Where(a => a < length).ToList();
        if (apexes.Count == 0) return null;
        var turns = new List<PlanTurn>();
        for (int k = 0; k < apexes.Count; k++)
            turns.Add(new PlanTurn
            {
                Index = k, Number = k + 1, Apex = apexes[k],
                From = k == 0 ? 0 : (apexes[k - 1] + apexes[k]) / 2, To = k + 1 < apexes.Count ? (apexes[k] + apexes[k + 1]) / 2 : length,
                PrevApex = k == 0 ? 0 : apexes[k - 1], NextApex = k + 1 < apexes.Count ? apexes[k + 1] : length,
            });
        var onsets = laps.ToDictionary(l => l, l => LapReplay.Onsets(l[TraceChannel.Brake]!, l.StepM, BrakeOn, BrakeOff, BrakeQuietM));
        foreach (var t in turns)
        {
            var times = laps.Select(l => PieceTime(l, t.From, t.To)).ToList();
            t.BestTime = times.Min();
            t.WorstTime = times.Max();
            var r = laps[times.IndexOf(t.BestTime)];
            t.Reference = r;
            t.Brake = FirstOnset(onsets[r], t.PrevApex, t.Apex);
            var low = Lowest(r[TraceChannel.Speed]!, r.StepM, t.Apex);
            t.MinSpeed = low.Speed;
            t.Gear = r[TraceChannel.Gear] is { } g ? (int)Math.Round(LapReplay.At(g, r.StepM, low.At)) : 0;
            t.FullThrottle = Full(r, t.Apex, t.NextApex);
        }
        var plan = new CoachPlan
        {
            Laps = laps, Turns = turns, Length = length, Step = step, Best = best,
            BestTurnsTogether = turns.Sum(t => t.BestTime),
        };
        foreach (var l in laps) plan._lines[l] = Aligned(l, best);
        plan._refTime = plan.BuildReferenceTime();
        foreach (var t in turns)
        {
            var mine = laps.Select(l => plan.Result(l, t, onsets[l])).ToList();
            t.AverageLost = mine.Average(m => m.Lost);
            var usual = laps.Where(l => l != t.Reference).Select(l => FirstOnset(onsets[l], t.PrevApex, t.Apex)).OfType<double>().ToList();
            t.UsualBrake = usual.Count > 0 ? usual.Average() : null;
        }
        var groups = new List<List<PlanTurn>>();
        foreach (var t in turns)
        {
            if (groups.Count > 0 && plan.ReferenceTime(t.Apex) - plan.ReferenceTime(groups[^1][^1].Apex) < GroupGapS) groups[^1].Add(t);
            else groups.Add(new List<PlanTurn> { t });
        }
        var built = groups.Select((g, i) => new CoachGroup { Index = i, Turns = g }).ToList();
        foreach (var g in built)
        {
            foreach (var t in g.Turns) t.Group = g;
            var w = g.Turns.MaxBy(t => t.AverageLost)!;
            g.CallTurn = w;
            g.Usual = g.Turns.Sum(t => t.AverageLost);
            g.Call = plan.CallFor(w, laps.Where(l => l != w.Reference).Select(l => plan.Result(l, w, onsets[l])).ToList());
        }
        plan.Groups = built;
        return plan;
    }

    internal CoachCall CallFor(PlanTurn t, IReadOnlyList<CoachTurnResult> others)
    {
        if (t.AverageLost < LeastCostS) return new CoachCall(CoachCallKind.KeepItTheSame, 0);
        if (others.Count == 0) return new CoachCall(CoachCallKind.NoClearCall, 0);
        double entry = others.Average(o => o.Entry), exit = others.Average(o => o.Exit);
        var brakes = others.Select(o => o.Brake).OfType<double>().ToList();
        var fulls = others.Select(o => o.FullThrottle).OfType<double>().ToList();
        double? brake = brakes.Count > 0 ? brakes.Average() : null, full = fulls.Count > 0 ? fulls.Average() : null;
        double speed = others.Average(o => o.MinSpeed);
        if (entry >= exit && brake is >= SameBrakeM) return new CoachCall(CoachCallKind.BrakeLater, brake.Value);
        if (entry < exit && full is >= SameThrottleM) return new CoachCall(CoachCallKind.ThrottleSooner, full.Value);
        if (speed <= -SameSpeedMs) return new CoachCall(CoachCallKind.CarrySpeed, -speed);
        return new CoachCall(CoachCallKind.NoClearCall, 0);
    }

    public CoachTurnResult Result(LapTrace lap, PlanTurn t) =>
        Result(lap, t, LapReplay.Onsets(lap[TraceChannel.Brake]!, lap.StepM, BrakeOn, BrakeOff, BrakeQuietM));

    private CoachTurnResult Result(LapTrace lap, PlanTurn t, IReadOnlyList<double> onsets)
    {
        var r = t.Reference;
        double lost = PieceTime(lap, t.From, t.To) - PieceTime(r, t.From, t.To);
        double entry = PieceTime(lap, t.From, t.Apex) - PieceTime(r, t.From, t.Apex);
        double? mine = FirstOnset(onsets, t.PrevApex, t.Apex);
        double? mineFull = Full(lap, t.Apex, t.NextApex);
        return new CoachTurnResult(lost, entry,
            mine is { } mb && t.Brake is { } rb ? rb - mb : null,
            Lowest(lap[TraceChannel.Speed]!, lap.StepM, t.Apex).Speed - t.MinSpeed,
            mineFull is { } mf && t.FullThrottle is { } rf ? mf - rf : null);
    }

    public static CoachTurnResult Live(PlanTurn t, double lost, double entry, double? brakeAt, double slowest, double? fullAt) =>
        new(lost, entry, brakeAt is { } mb && t.Brake is { } rb ? rb - mb : null, slowest - t.MinSpeed, fullAt is { } mf && t.FullThrottle is { } rf ? mf - rf : null);

    public static CoachGroupResult? Pick(CoachGroup g, Func<PlanTurn, CoachTurnResult?> results)
    {
        double lost = 0;
        PlanTurn? pick = null;
        CoachTurnResult? picked = null;
        int found = 0;
        foreach (var t in g.Turns)
            if (results(t) is { } r) { lost += r.Lost; found++; }
        if (found == 0) return null;
        foreach (var t in g.Turns)
            if (results(t) is { } r && (picked is null || (lost >= 0 ? r.Lost > picked.Lost : r.Lost < picked.Lost))) { pick = t; picked = r; }
        return new CoachGroupResult(g, lost, pick!, Reason(picked!));
    }

    public static CoachReason? Reason(CoachTurnResult r)
    {
        int sign = r.Lost >= 0 ? 1 : -1;
        bool entry = sign > 0 ? r.Entry >= r.Exit : r.Entry <= r.Exit;
        CoachReason? slow = Math.Abs(r.MinSpeed) >= SameSpeedMs ? new CoachReason(CoachReasonKind.Speed, r.MinSpeed) : null;
        if (entry)
        {
            if (r.Brake is { } b && Math.Abs(b) >= SameBrakeM && Math.Sign(b) == sign) return new CoachReason(CoachReasonKind.Brake, b);
        }
        else if (r.FullThrottle is { } f && Math.Abs(f) >= SameThrottleM && Math.Sign(f) == sign) return new CoachReason(CoachReasonKind.Throttle, f);
        return slow is { } s && Math.Sign(-s.Amount) == sign ? s : null;
    }

    private double[] BuildReferenceTime()
    {
        int n = (int)Math.Floor(Length / Step) + 1;
        var t = new double[n];
        double baseTime = 0;
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            double d = i * Step;
            while (k + 1 < Turns.Count && d >= Turns[k].To) { baseTime += Turns[k].BestTime; k++; }
            var r = Turns[k].Reference;
            t[i] = baseTime + r.TimeAt(d) - r.TimeAt(Turns[k].From);
        }
        return t;
    }

    private static bool Usable(LapTrace l) =>
        l[TraceChannel.Time] is not null && l[TraceChannel.Speed] is not null && l[TraceChannel.Throttle] is not null && l[TraceChannel.Brake] is not null
        && l[TraceChannel.X] is not null && l[TraceChannel.Y] is not null && l.Points > 10;

    public static double PieceTime(LapTrace lap, double from, double to) => lap.TimeAt(to) - lap.TimeAt(from);

    private static double? FirstOnset(IReadOnlyList<double> onsets, double after, double upTo)
    {
        foreach (double d in onsets) if (d > after && d <= upTo) return d;
        return null;
    }

    private static double? Full(LapTrace t, double apex, double to)
    {
        var th = t[TraceChannel.Throttle]!;
        int a = Math.Max(0, (int)(apex / t.StepM)), b = Math.Min(th.Length - 1, (int)(to / t.StepM));
        for (int i = a; i <= b; i++) if (th[i] >= FullOn) return i * t.StepM;
        return null;
    }

    public static (double Speed, double At) Lowest(double[] speed, double step, double apex)
    {
        int a = Math.Max(0, (int)((apex - ApexWindowM) / step)), b = Math.Min(speed.Length - 1, (int)((apex + ApexWindowM) / step));
        double low = double.MaxValue;
        int at = a;
        for (int i = a; i <= b; i++) if (speed[i] < low) { low = speed[i]; at = i; }
        return low == double.MaxValue ? (0, apex) : (low, at * step);
    }

    private static double Slowest(double[] speed, double step, double from, double to)
    {
        int a = Math.Max(0, (int)(from / step)), b = Math.Min(speed.Length - 1, (int)(to / step));
        double low = double.MaxValue;
        int at = a;
        for (int i = a; i <= b; i++) if (speed[i] < low) { low = speed[i]; at = i; }
        return at * step;
    }

    public static List<(double From, double To)> Bends(double[] x, double[] y, double step)
    {
        int n = Math.Min(x.Length, y.Length), h = Math.Max(1, (int)Math.Round(HeadingSpanM / step));
        var k = new double[n];
        for (int i = h; i < n - h; i++)
        {
            double d = Math.Atan2(y[i + h] - y[i], x[i + h] - x[i]) - Math.Atan2(y[i] - y[i - h], x[i] - x[i - h]);
            k[i] = Math.Atan2(Math.Sin(d), Math.Cos(d)) * 180 / Math.PI / (h * step);
        }
        var segs = new List<(int A, int B)>();
        int s = -1;
        for (int i = 0; i < n; i++)
        {
            bool on = Math.Abs(k[i]) > BendDegPerM;
            if (on && s < 0) s = i;
            if (!on && s >= 0) { segs.Add((s, i)); s = -1; }
        }
        var merged = new List<(int A, int B)>();
        foreach (var g in segs)
        {
            if (merged.Count > 0 && (g.A - merged[^1].B) * step <= BendJoinM && Math.Sign(k[merged[^1].A + 1]) == Math.Sign(k[g.A + 1])) merged[^1] = (merged[^1].A, g.B);
            else merged.Add(g);
        }
        var found = new List<(double, double)>();
        foreach (var (a, b) in merged)
        {
            double turn = 0;
            for (int j = a; j < b; j++) turn += k[j] * step;
            if (Math.Abs(turn) >= BendLeastDeg) found.Add((a * step, b * step));
        }
        return found;
    }
}

internal enum CoachReasonKind { Brake, Speed, Throttle }

internal sealed record CoachReason(CoachReasonKind Kind, double Amount);

internal sealed record CoachGroupResult(CoachGroup Group, double Lost, PlanTurn Most, CoachReason? Reason);
