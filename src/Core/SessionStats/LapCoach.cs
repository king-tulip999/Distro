namespace Core.SessionStats;

internal sealed record CoachTurn(int Index, double Apex, double From, double To, double Prev, double Next)
{
    public int Number => Index + 1;
}

internal sealed record TurnMeasure(double LowSpeed, double LowAt, double? Brake, double Peak, double? Release, double? BrakeSeconds,
    double? ThrottleOn, double? FullThrottle, double Coast, double SpeedAfter, double Seconds, int Gear);

internal enum CoachPhase { Braking, Middle, Straights }

internal sealed record TurnPhases(double Braking, double Middle, double Straights, double BrakeFrom, double BrakeTo, double MiddleTo)
{
    public double Total => Braking + Middle + Straights;

    public double Of(CoachPhase phase) => phase switch { CoachPhase.Braking => Braking, CoachPhase.Middle => Middle, _ => Straights };
}

internal enum CoachKind { BrakePoint, Release, BrakeTime, Peak, LowSpeed, ThrottleOn, Coast, FullThrottle, SpeedAfter }

internal sealed record CoachDiff(CoachKind Kind, CoachPhase Phase, double Mine, double Theirs, double Cost);

internal sealed record CoachAdvice(CoachTurn Turn, CoachDiff? Change, CoachPhase Phase, double Worth, double Lost);

internal sealed record PedalShare(double Throttle, double Brake, double Neither);

internal sealed class LapCoach
{
    public const double BrakeOn = 0.15, BrakeOff = 0.05, BrakeQuietM = 30, PedalOn = 0.05, ThrottleOn = 0.2, FullOn = 0.98;
    public const double ApexWindowM = 60, BrakeSearchM = 400, ReleaseAfterM = 80, SpeedAfterM = 100, MergeM = 80;
    public const double CurveSpanM = 20, OnDegPerM = 0.3, JoinM = 16, MinTurnDeg = 20, LeastLoss = 0.02;
    public const double MetresApart = 5, SecondsApart = 0.12, CoastApart = 0.15, PeakApart = 0.08, SpeedApart = 2 / 3.6;

    public IReadOnlyList<CoachTurn> Turns { get; }
    public IReadOnlyList<TurnMeasure> Mine { get; }
    public IReadOnlyList<TurnMeasure> Theirs { get; }
    public IReadOnlyList<TurnPhases> Phases { get; }
    public double Length { get; }

    private LapCoach(IReadOnlyList<CoachTurn> turns, LapTrace mine, LapTrace reference, double length)
    {
        Turns = turns;
        Length = length;
        Mine = turns.Select(t => Measure(mine, t)).ToList();
        Theirs = turns.Select(t => Measure(reference, t)).ToList();
        Phases = turns.Select((t, k) => Split(mine, reference, t, Theirs[k])).ToList();
    }

    public static LapCoach? Build(LapTrace mine, LapTrace reference)
    {
        double length = Math.Min(mine.LengthM, reference.LengthM);
        var turns = TurnsOf(reference, length);
        return turns.Count == 0 ? null : new LapCoach(turns, mine, reference, length);
    }

    public TurnPhases Sum => new(Phases.Sum(p => p.Braking), Phases.Sum(p => p.Middle), Phases.Sum(p => p.Straights), 0, 0, 0);

    public List<CoachDiff> Diffs(int turn) => Compare(Mine[turn], Theirs[turn]);

    public CoachAdvice Advice(int turn)
    {
        var phases = Phases[turn];
        var main = Enum.GetValues<CoachPhase>().OrderByDescending(phases.Of).First();
        var pick = Diffs(turn).Where(d => d.Cost > 0).OrderByDescending(d => d.Phase == main).ThenByDescending(d => d.Cost).FirstOrDefault();
        var phase = pick?.Phase ?? main;
        return new CoachAdvice(Turns[turn], pick, phase, phases.Of(phase), phases.Total);
    }

    public List<CoachAdvice> Worst(int most = 3) =>
        Enumerable.Range(0, Turns.Count).Where(k => Phases[k].Total > LeastLoss).OrderByDescending(k => Phases[k].Total).Take(most).Select(Advice).ToList();

    public static List<CoachTurn> TurnsOf(LapTrace reference, double length)
    {
        var apexes = new List<double>();
        if (reference[TraceChannel.Speed] is not { } speed) return new List<CoachTurn>();
        if (reference[TraceChannel.X] is { } x && reference[TraceChannel.Y] is { } y)
        {
            foreach (var (a, b) in Bends(x, y, reference.StepM))
            {
                double apex = Lowest(reference, a, b).At;
                if (apex >= length - 20) continue;
                if (apexes.Count > 0 && apex - apexes[^1] < MergeM)
                {
                    if (reference.At(TraceChannel.Speed, apex) < reference.At(TraceChannel.Speed, apexes[^1])) apexes[^1] = apex;
                }
                else apexes.Add(apex);
            }
        }
        if (apexes.Count == 0) apexes = LapReplay.Corners(speed, reference.StepM).Where(a => a < length - 20).ToList();
        var turns = new List<CoachTurn>();
        for (int k = 0; k < apexes.Count; k++)
            turns.Add(new CoachTurn(k, apexes[k], k == 0 ? 0 : (apexes[k - 1] + apexes[k]) / 2, k + 1 < apexes.Count ? (apexes[k] + apexes[k + 1]) / 2 : length,
                k == 0 ? 0 : apexes[k - 1], k + 1 < apexes.Count ? apexes[k + 1] : length));
        return turns;
    }

    public static List<(double From, double To)> Bends(double[] x, double[] y, double step)
    {
        int n = Math.Min(x.Length, y.Length), h = Math.Max(1, (int)Math.Round(CurveSpanM / step));
        var k = new double[n];
        for (int i = h; i < n - h; i++)
        {
            double d = Math.Atan2(y[i + h] - y[i], x[i + h] - x[i]) - Math.Atan2(y[i] - y[i - h], x[i] - x[i - h]);
            k[i] = Math.Atan2(Math.Sin(d), Math.Cos(d)) * 180 / Math.PI / (h * step);
        }
        var runs = new List<(int A, int B)>();
        int start = -1;
        for (int i = 0; i < n; i++)
        {
            bool on = Math.Abs(k[i]) > OnDegPerM;
            if (on && start < 0) start = i;
            if (!on && start >= 0) { runs.Add((start, i)); start = -1; }
        }
        var merged = new List<(int A, int B)>();
        foreach (var run in runs)
        {
            if (merged.Count > 0 && (run.A - merged[^1].B) * step <= JoinM && Math.Sign(k[merged[^1].A + 1]) == Math.Sign(k[run.A + 1])) merged[^1] = (merged[^1].A, run.B);
            else merged.Add(run);
        }
        return merged.Where(r => { double turn = 0; for (int j = r.A; j < r.B; j++) turn += k[j] * step; return Math.Abs(turn) >= MinTurnDeg; })
            .Select(r => (r.A * step, r.B * step)).ToList();
    }

    private static (double Speed, double At) Lowest(LapTrace lap, double from, double to)
    {
        double low = double.MaxValue, at = from;
        for (double d = Math.Max(0, from); d <= Math.Min(lap.LengthM - lap.StepM, to); d += lap.StepM)
        {
            double v = lap.At(TraceChannel.Speed, d) ?? double.MaxValue;
            if (v < low) { low = v; at = d; }
        }
        return (low == double.MaxValue ? 0 : low, at);
    }

    private static double Value(LapTrace lap, TraceChannel c, double d) => lap.At(c, d) ?? 0;

    public static TurnMeasure Measure(LapTrace lap, CoachTurn t)
    {
        double step = lap.StepM;
        var (low, lowAt) = Lowest(lap, t.Apex - ApexWindowM, t.Apex + ApexWindowM);
        double? brake = null;
        if (lap[TraceChannel.Brake] is { } brakes)
        {
            foreach (double d in LapReplay.Onsets(brakes, step, BrakeOn, BrakeOff, BrakeQuietM))
                if (d > lowAt - BrakeSearchM && d <= lowAt) brake = d;
            if (brake is { } b)
                for (double d = b; d < lowAt; d += step)
                    if (Value(lap, TraceChannel.Throttle, d) >= FullOn) { brake = null; break; }
        }
        double peak = 0, peakAt = 0;
        double? release = null;
        if (brake is { } from)
        {
            peakAt = from;
            for (double d = from; d <= lowAt + 40; d += step) { double v = Value(lap, TraceChannel.Brake, d); if (v > peak) { peak = v; peakAt = d; } }
            for (double d = peakAt; d <= lowAt + ReleaseAfterM; d += step) if (Value(lap, TraceChannel.Brake, d) < BrakeOff) { release = d; break; }
        }
        double start = release ?? lowAt - ApexWindowM;
        double? on = null;
        for (double d = start; d < t.Next; d += step)
            if (Value(lap, TraceChannel.Throttle, d) >= ThrottleOn && Value(lap, TraceChannel.Throttle, d + step) >= ThrottleOn && Value(lap, TraceChannel.Throttle, d + 2 * step) >= ThrottleOn) { on = d; break; }
        double? full = null;
        for (double d = lowAt; d < t.Next; d += step) if (Value(lap, TraceChannel.Throttle, d) >= FullOn) { full = d; break; }
        double coast = 0;
        for (double d = brake ?? t.From; d < (full ?? t.To); d += step)
            if (Value(lap, TraceChannel.Throttle, d) < PedalOn && Value(lap, TraceChannel.Brake, d) < PedalOn) coast += lap.TimeAt(d + step) - lap.TimeAt(d);
        double? brakeSeconds = brake is { } b2 && release is { } r ? lap.TimeAt(r) - lap.TimeAt(b2) : null;
        double after = Value(lap, TraceChannel.Speed, Math.Min(lap.LengthM - step, lowAt + SpeedAfterM));
        return new TurnMeasure(low, lowAt, brake, peak, release, brakeSeconds, on, full, coast, after, lap.TimeAt(t.To) - lap.TimeAt(t.From),
            (int)Math.Round(lap.At(TraceChannel.Gear, lowAt) ?? 0));
    }

    public static TurnPhases Split(LapTrace mine, LapTrace reference, CoachTurn t, TurnMeasure r)
    {
        double Clamp(double d) => Math.Clamp(d, t.From, t.To);
        double bA, bB, mB;
        if (r.Brake is { } brake) { bA = Clamp(brake); bB = Clamp(r.Release ?? r.LowAt); }
        else bA = bB = Clamp(r.LowAt - 50);
        mB = Clamp(Math.Max(bB, r.FullThrottle ?? r.LowAt + 50));
        double Loss(double a, double b) => b <= a ? 0 : (mine.TimeAt(b) - mine.TimeAt(a)) - (reference.TimeAt(b) - reference.TimeAt(a));
        return new TurnPhases(Loss(bA, bB), Loss(bB, mB), Loss(t.From, bA) + Loss(mB, t.To), bA, bB, mB);
    }

    public static List<CoachDiff> Compare(TurnMeasure y, TurnMeasure r)
    {
        var list = new List<CoachDiff>();
        if (y.Brake is { } yb && r.Brake is { } rb && Math.Abs(yb - rb) >= MetresApart) list.Add(new CoachDiff(CoachKind.BrakePoint, CoachPhase.Braking, yb, rb, rb - yb));
        if (y.Release is { } yr && r.Release is { } rr && Math.Abs(yr - rr) >= MetresApart) list.Add(new CoachDiff(CoachKind.Release, CoachPhase.Braking, yr, rr, rr - yr));
        if (y.BrakeSeconds is { } ys && r.BrakeSeconds is { } rs && Math.Abs(ys - rs) >= SecondsApart) list.Add(new CoachDiff(CoachKind.BrakeTime, CoachPhase.Braking, ys, rs, ys - rs));
        if (y.Brake is not null && r.Brake is not null && Math.Abs(y.Peak - r.Peak) >= PeakApart) list.Add(new CoachDiff(CoachKind.Peak, CoachPhase.Braking, y.Peak, r.Peak, r.Peak - y.Peak));
        if (Math.Abs(y.LowSpeed - r.LowSpeed) >= SpeedApart) list.Add(new CoachDiff(CoachKind.LowSpeed, CoachPhase.Middle, y.LowSpeed, r.LowSpeed, r.LowSpeed - y.LowSpeed));
        if (y.ThrottleOn is { } yo && r.ThrottleOn is { } ro && Math.Abs(yo - ro) >= MetresApart) list.Add(new CoachDiff(CoachKind.ThrottleOn, CoachPhase.Middle, yo, ro, yo - ro));
        if (Math.Abs(y.Coast - r.Coast) >= CoastApart) list.Add(new CoachDiff(CoachKind.Coast, CoachPhase.Middle, y.Coast, r.Coast, y.Coast - r.Coast));
        if (y.FullThrottle is { } yf && r.FullThrottle is { } rf && Math.Abs(yf - rf) >= MetresApart) list.Add(new CoachDiff(CoachKind.FullThrottle, CoachPhase.Straights, yf, rf, yf - rf));
        if (Math.Abs(y.SpeedAfter - r.SpeedAfter) >= SpeedApart) list.Add(new CoachDiff(CoachKind.SpeedAfter, CoachPhase.Straights, y.SpeedAfter, r.SpeedAfter, r.SpeedAfter - y.SpeedAfter));
        return list;
    }

    public static PedalShare Pedals(LapTrace lap)
    {
        if (lap[TraceChannel.Throttle] is not { } th || lap[TraceChannel.Brake] is not { } br) return new PedalShare(0, 0, 0);
        var time = lap.Time;
        double t = 0, b = 0, c = 0;
        for (int i = 0; i + 1 < lap.Points; i++)
        {
            double dt = time[i + 1] - time[i];
            if (br[i] >= PedalOn) b += dt;
            else if (th[i] >= PedalOn) t += dt;
            else c += dt;
        }
        double sum = t + b + c;
        return sum <= 0 ? new PedalShare(0, 0, 0) : new PedalShare(t / sum, b / sum, c / sum);
    }

    public static double Seconds(LapTrace lap, CoachTurn t) => lap.TimeAt(t.To) - lap.TimeAt(t.From);

    public static double Top(LapTrace lap) => lap[TraceChannel.Speed] is { Length: > 0 } v ? v.Max() : 0;
}
