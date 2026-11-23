using Core.Config;

namespace Core.Fans;

internal sealed record CoolingTiming(int SampleMs, double IdleSeconds, double MinStep, double Window, double MaxSlopePerMinute, double MaxStep, double WarmMin, double WarmMax, double TypicalStep)
{
    public static readonly CoolingTiming Real = new(2000, 20, 60, 60, 0.3, 420, 60, 480, 95);
    public static readonly CoolingTiming Demo = new(400, 1.5, 3, 2.4, 0.9, 6, 3, 6, 4);
}

internal sealed record SettleFit(double Final, double Tau, double Rms);

internal sealed record CoolingResponse(string SensorId, IReadOnlyList<(int Percent, double Temp)> Points, double Spread, int? TooHotAt)
{
    public double? HeldAt { get; init; }
    public IReadOnlyList<int> Unfinished { get; init; } = Array.Empty<int>();
}

internal enum CoolingVerdict { None, Quieter, AboutRight, MoreSpeed, BarelyMatters }

internal sealed record CoolingSuggestion(string SensorId, string SensorName, List<CurvePoint> Points, int Knee, double KneeTemp, double Spread, bool BarelyMatters, CoolingResponse Response)
{
    public int? Current { get; init; }
    public CoolingVerdict Verdict { get; init; }
}

internal static class CoolingAnalysis
{
    public const double BarelyMattersC = 1.5;

    public static double SlopePerMinute(IReadOnlyList<(double Seconds, double Temp)> samples)
    {
        int n = samples.Count;
        if (n < 2) return double.PositiveInfinity;
        double mx = 0, my = 0;
        foreach (var (s, t) in samples) { mx += s; my += t; }
        mx /= n;
        my /= n;
        double sxy = 0, sxx = 0;
        foreach (var (s, t) in samples)
        {
            sxy += (s - mx) * (t - my);
            sxx += (s - mx) * (s - mx);
        }
        return sxx <= 0 ? double.PositiveInfinity : sxy / sxx * 60;
    }

    public static List<(double Seconds, double Temp)> Tail(IReadOnlyList<(double Seconds, double Temp)> samples, double seconds)
    {
        if (samples.Count == 0) return new();
        double from = samples[^1].Seconds - seconds;
        return samples.Where(s => s.Seconds >= from - 1e-9).ToList();
    }

    public static bool Settled(IReadOnlyList<(double Seconds, double Temp)> samples, double minSeconds, double windowSeconds, double maxSlopePerMinute)
    {
        if (samples.Count < 3 || samples[^1].Seconds - samples[0].Seconds < minSeconds) return false;
        var window = Tail(samples, windowSeconds);
        return window.Count >= 3 && Math.Abs(SlopePerMinute(window)) <= maxSlopePerMinute;
    }

    public static double Mean(IReadOnlyList<(double Seconds, double Temp)> samples, double seconds)
    {
        var tail = Tail(samples, seconds);
        return tail.Count == 0 ? double.NaN : tail.Average(s => s.Temp);
    }

    public static SettleFit? FitSettle(IReadOnlyList<(double Seconds, double Temp)> samples)
    {
        int n = samples.Count;
        if (n < 8) return null;
        double t0 = samples[0].Seconds, span = samples[^1].Seconds - t0;
        if (span <= 0) return null;
        SettleFit? best = null;
        double bestSse = double.PositiveInfinity;
        for (double tau = Math.Max(0.2, span / 200); tau <= span * 20; tau *= 1.08)
        {
            double mx = 0, my = 0;
            foreach (var (t, v) in samples) { mx += Math.Exp(-(t - t0) / tau); my += v; }
            mx /= n;
            my /= n;
            double sxx = 0, sxy = 0;
            foreach (var (t, v) in samples)
            {
                double dx = Math.Exp(-(t - t0) / tau) - mx;
                sxx += dx * dx;
                sxy += dx * (v - my);
            }
            if (sxx < 1e-12) continue;
            double b = sxy / sxx, a = my - b * mx, sse = 0;
            foreach (var (t, v) in samples)
            {
                double r = v - a - b * Math.Exp(-(t - t0) / tau);
                sse += r * r;
            }
            if (sse < bestSse)
            {
                bestSse = sse;
                best = new SettleFit(a, tau, Math.Sqrt(sse / n));
            }
        }
        return best;
    }

    public static double Noise(IReadOnlyList<(double Seconds, double Temp)> samples)
    {
        if (samples.Count < 3) return 0;
        var d = new List<double>(samples.Count - 1);
        for (int i = 1; i < samples.Count; i++) d.Add(samples[i].Temp - samples[i - 1].Temp);
        double mean = d.Average();
        return Math.Sqrt(d.Sum(x => (x - mean) * (x - mean)) / d.Count / 2);
    }

    public static double? PredictFinal(IReadOnlyList<(double Seconds, double Temp)> samples, double minSeconds)
    {
        if (samples.Count < 8) return null;
        double span = samples[^1].Seconds - samples[0].Seconds;
        if (span < minSeconds) return null;
        double noise = Noise(samples);
        var half = Tail(samples, span / 2);
        if (Math.Abs(SlopePerMinute(half)) < 0.25 + noise * 0.5) return Mean(samples, span / 2);
        var fit = FitSettle(samples);
        if (fit is null) return null;
        if (fit.Rms > 0.2 + 1.5 * noise) return null;
        if (span < fit.Tau * 1.5) return null;
        double cut = samples[^1].Seconds - span * 0.25;
        var earlier = FitSettle(samples.Where(s => s.Seconds <= cut).ToList());
        if (earlier is null || Math.Abs(earlier.Final - fit.Final) > 0.4 + noise) return null;
        double recent = Mean(samples, Math.Min(span / 4, 20));
        if (Math.Abs(fit.Final - recent) > 3) return null;
        return fit.Final;
    }

    public static double Threshold(CoolingGoal goal) => goal switch
    {
        CoolingGoal.Quiet => 4,
        CoolingGoal.Cool => 0.75,
        _ => 2,
    };

    public const double HeldSpreadC = 2.5, HeldFloorC = 80;

    public static bool IsCpu(CoolingSensor sensor) => sensor.Cpu || (!sensor.Gpu && sensor.Name == "CPU");

    public static bool TooHot(CoolingTestResult result, CoolingStep step) =>
        step.OverLimit && step.Temps.Any(kv => result.Sensors.FirstOrDefault(s => s.Id == kv.Key) is { } sensor && !IsCpu(sensor) && kv.Value >= result.LimitC - 1);

    public static bool Unfinished(CoolingStep step) => step.OverLimit && !step.Settled && !step.Predicted;

    public static double? HeldAt(CoolingTestResult result, string sensorId)
    {
        var temps = result.Groups.SelectMany(g => g.Steps).Where(s => !Unfinished(s) && s.Temps.ContainsKey(sensorId)).Select(s => (double)s.Temps[sensorId]).ToList();
        if (temps.Count < 4 || temps.Average() < HeldFloorC || temps.Max() - temps.Min() > HeldSpreadC) return null;
        return Math.Round(temps.Max());
    }

    private static float? SidePower(CoolingSensor sensor, CoolingStep step) => sensor.Gpu ? step.GpuPower : step.CpuPower;

    public static CoolingResponse? ResponseOf(CoolingTestResult result, CoolingGroupResult group, string sensorId)
    {
        var sensor = result.Sensors.FirstOrDefault(s => s.Id == sensorId);
        if (sensor is null) return null;
        var steps = group.Steps
            .Where(s => s.Temps.ContainsKey(sensorId) && !Unfinished(s) && !TooHot(result, s))
            .GroupBy(s => s.Percent)
            .Select(g => g.Last())
            .OrderByDescending(s => s.Percent)
            .ToList();
        if (steps.Count < 2) return null;
        int? tooHot = group.Steps.Where(s => TooHot(result, s)).Select(s => (int?)s.Percent).DefaultIfEmpty(null).Max();
        var unfinished = group.Steps.Where(s => Unfinished(s) && !TooHot(result, s)).Select(s => s.Percent).ToList();

        if (HeldAt(result, sensorId) is { } ceiling)
        {
            if (steps.Any(s => SidePower(sensor, s) is null)) return null;
            double best = steps.Max(s => SidePower(sensor, s)!.Value);
            if (best <= 0) return null;
            var lost = steps.Select(s => (s.Percent, Temp: Math.Round(Math.Max(0, (best - SidePower(sensor, s)!.Value) / best * 100), 1))).ToList();
            return new CoolingResponse(sensorId, lost, lost.Max(p => p.Temp), tooHot) { HeldAt = ceiling, Unfinished = unfinished };
        }

        var loads = steps.Select(s => SidePower(sensor, s)).OfType<float>().OrderBy(p => p).ToList();
        double? reference = loads.Count == steps.Count ? loads[loads.Count / 2] : null;
        var points = steps.Select(s => (s.Percent, Temp: Corrected(sensor, s, reference))).ToList();
        return new CoolingResponse(sensorId, points, points.Max(p => p.Temp) - points.Min(p => p.Temp), tooHot) { Unfinished = unfinished };
    }

    private static double Corrected(CoolingSensor sensor, CoolingStep step, double? reference)
    {
        double t = step.Temps[sensor.Id];
        if (reference is not { } pref || sensor.Idle is not { } idle || sensor.IdlePower is not { } pidle || SidePower(sensor, step) is not { } p) return t;
        if (p - pidle < 20 || pref - pidle < 20) return t;
        double factor = Math.Clamp((pref - pidle) / (p - pidle), 0.8, 1.25);
        return Math.Round(idle + (t - idle) * factor, 1);
    }

    public static string? PickSensor(CoolingGroupResult group, CoolingTestResult result, FanCurve? curve)
    {
        if (curve is { Type: FanCurveType.Graph, SensorId: { } own } && ResponseOf(result, group, own) is not null) return own;
        return result.Sensors
            .Select(s => (s.Id, Response: ResponseOf(result, group, s.Id)))
            .Where(r => r.Response is { HeldAt: null })
            .OrderByDescending(r => r.Response!.Spread)
            .Select(r => r.Id)
            .FirstOrDefault();
    }

    public static int Knee(IReadOnlyList<(int Percent, double Temp)> points, double threshold)
    {
        double best = points.Min(p => p.Temp);
        foreach (var p in points.OrderBy(p => p.Percent))
        {
            bool holds = points.Where(q => q.Percent >= p.Percent).All(q => q.Temp - best <= threshold);
            if (holds) return p.Percent;
        }
        return points.Max(p => p.Percent);
    }

    public static double TempAt(IReadOnlyList<(int Percent, double Temp)> points, int percent)
    {
        var sorted = points.OrderBy(p => p.Percent).ToList();
        if (percent <= sorted[0].Percent) return sorted[0].Temp;
        for (int i = 1; i < sorted.Count; i++)
        {
            if (percent <= sorted[i].Percent)
            {
                var (a, b) = (sorted[i - 1], sorted[i]);
                double f = (percent - a.Percent) / (double)(b.Percent - a.Percent);
                return a.Temp + (b.Temp - a.Temp) * f;
            }
        }
        return sorted[^1].Temp;
    }

    public static List<CurvePoint> Curve(double idle, IReadOnlyList<(int Percent, double Temp)> points, int floor, int limit, CoolingGoal goal, double shift = 0)
    {
        double spread = points.Max(p => p.Temp) - points.Min(p => p.Temp);
        int knee = Knee(points, Threshold(goal));
        double kneeTemp = TempAt(points, knee) + shift;
        floor = Math.Clamp(floor, 0, 100);
        knee = Math.Max(knee, floor);

        if (spread < BarelyMattersC)
        {
            double start = Math.Max(25, Math.Min(Math.Round(idle + shift), limit - 20));
            double rampFrom = Math.Max(start + 4, limit - 12);
            return Build(new[] { (start, (double)floor), (rampFrom, floor), (Math.Max(rampFrom + 3, limit - 4), 100d) });
        }

        double hot = goal switch
        {
            CoolingGoal.Quiet => limit - 4,
            CoolingGoal.Cool => Math.Min(limit - 6, kneeTemp + 5),
            _ => Math.Min(limit - 4, kneeTemp + 8),
        };
        hot = Math.Max(hot, kneeTemp + 3);
        double low = Math.Round(idle + shift);
        if (low > kneeTemp - 6) low = kneeTemp - 10;
        low = Math.Max(25, low);
        double mid = low + (kneeTemp - low) * 0.55;
        double midPercent = floor + (knee - floor) * 0.35;
        return Build(new[] { (low, (double)floor), (mid, midPercent), (kneeTemp, knee), (hot, 100d) });
    }

    private static List<CurvePoint> Build(IEnumerable<(double Temp, double Percent)> raw)
    {
        var result = new List<CurvePoint>();
        foreach (var (t, p) in raw)
        {
            float temp = (float)Math.Clamp(Math.Round(t), 20, 105);
            float percent = (float)Math.Clamp(Math.Round(p), 0, 100);
            if (result.Count > 0)
            {
                temp = Math.Max(temp, result[^1].Temp + 1);
                percent = Math.Max(percent, result[^1].Percent);
            }
            if (temp > 105) break;
            result.Add(new CurvePoint { Temp = temp, Percent = percent });
        }
        return result;
    }

    public static List<CurvePoint> HeldCurve(double idle, double ceiling, int knee, int floor, int limit)
    {
        floor = Math.Clamp(floor, 0, 100);
        knee = Math.Max(knee, floor);
        double low = Math.Max(25, Math.Min(Math.Round(idle), ceiling - 12));
        double mid = low + (ceiling - low) * 0.55;
        return Build(new[] { (low, (double)floor), (mid, floor + (knee - floor) * 0.35), (ceiling, knee), (Math.Max(ceiling + 3, limit + 4), 100d) });
    }

    public static int? Equilibrium(IReadOnlyList<CurvePoint> curve, CoolingResponse response)
    {
        if (curve.Count == 0) return null;
        var sorted = curve.ToList();
        if (response.HeldAt is { } ceiling) return (int)Math.Round(FanController.Interpolate(sorted, (float)ceiling));
        int? best = null;
        double bestGap = double.PositiveInfinity;
        for (int p = 0; p <= 100; p++)
        {
            double gap = Math.Abs(FanController.Interpolate(sorted, (float)TempAt(response.Points, p)) - p);
            if (gap < bestGap) { bestGap = gap; best = p; }
        }
        return best;
    }

    public static CoolingVerdict VerdictOf(int knee, int? current, bool barely)
    {
        if (barely) return current is { } c && c > knee + 8 ? CoolingVerdict.Quieter : CoolingVerdict.BarelyMatters;
        if (current is not { } now) return CoolingVerdict.None;
        if (now > knee + 8) return CoolingVerdict.Quieter;
        if (now < knee - 8) return CoolingVerdict.MoreSpeed;
        return CoolingVerdict.AboutRight;
    }

    public static CoolingSuggestion? Suggest(CoolingTestResult result, CoolingGroupResult group, FanCurve? curve, CoolingGoal goal, double shift = 0)
    {
        if (PickSensor(group, result, curve) is not { } sensorId || ResponseOf(result, group, sensorId) is not { } response) return null;
        var sensor = result.Sensors.FirstOrDefault(s => s.Id == sensorId);
        int floor = Math.Max(group.Floor, response.TooHotAt is { } hot ? Math.Min(100, hot + 1) : 0);
        int knee = Math.Max(Knee(response.Points, Threshold(goal)), floor);
        bool barely = response.HeldAt is null ? response.Spread < BarelyMattersC : response.Spread < Threshold(CoolingGoal.Cool);
        List<CurvePoint> points;
        double kneeTemp;
        if (response.HeldAt is { } ceiling)
        {
            double idle = sensor?.Idle ?? ceiling - 25;
            if (barely) knee = Math.Max(floor, response.Points.Min(p => p.Percent));
            points = HeldCurve(idle + shift, ceiling + shift, knee, floor, result.LimitC);
            kneeTemp = ceiling + shift;
        }
        else
        {
            double idle = sensor?.Idle ?? response.Points.Min(p => p.Temp) - 15;
            points = Curve(idle, response.Points, floor, result.LimitC, goal, shift);
            kneeTemp = TempAt(response.Points, knee) + shift;
        }
        int? current = curve is { Type: FanCurveType.Graph, SensorId: { } id } && id == sensorId ? Equilibrium(curve.Points, response) : null;
        return new CoolingSuggestion(sensorId, sensor?.Name ?? sensorId, points, knee, kneeTemp, response.Spread, barely, response)
        {
            Current = current,
            Verdict = VerdictOf(knee, current, barely),
        };
    }

    public static double? Predict(CoolingTestResult result, CoolingGroupResult group, string sensorId, double percent, double power)
    {
        if (ResponseOf(result, group, sensorId) is not { HeldAt: null } response) return null;
        var sensor = result.Sensors.FirstOrDefault(s => s.Id == sensorId);
        if (sensor?.Idle is not { } idle || sensor.IdlePower is not { } idlePower) return null;
        var loads = group.Steps.Where(s => !s.OverLimit).Select(s => sensor.Gpu ? s.GpuPower : s.CpuPower).OfType<float>().ToList();
        if (loads.Count == 0) return null;
        double loadPower = loads.Average();
        if (loadPower - idlePower < 5) return null;
        double atLoad = TempAt(response.Points, (int)Math.Round(percent));
        double f = Math.Clamp((power - idlePower) / (loadPower - idlePower), 0, 1.3);
        return idle + (atLoad - idle) * f;
    }
}
