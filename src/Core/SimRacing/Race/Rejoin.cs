using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal readonly record struct Approaching(int Car, double TimeToYou, bool FasterClass);

internal readonly record struct RejoinPlan(bool Go, double Wait, double? ClearFor, int Before, int? NextCar)
{
    public static readonly RejoinPlan Clear = new(true, 0, null, 0, null);
}

internal static class Rejoin
{
    public const double FasterBy = 0.98;

    private static readonly Comparison<Approaching> Soonest = (a, b) =>
        a.TimeToYou.CompareTo(b.TimeToYou) is var c && c != 0 ? c : a.Car.CompareTo(b.Car);

    public static bool Shows(TelemetryFrame f, double slowKph)
    {
        if (f.Status != SimStatus.Connected || !f.IsOnTrack || f.IsReplayPlaying || f.OnPitRoad || f.InPitStall) return false;
        if (f.Phase is not (SessionPhase.Racing or SessionPhase.Checkered)) return false;
        if (f.PlayerSurface == TrackSurface.OffTrack) return true;
        if (f.PlayerSurface is TrackSurface.InPitStall or TrackSurface.NotInWorld) return false;
        return f.Speed is { } s && s * 3.6 < slowKph;
    }

    public static bool FasterClass(TelemetryFrame f, int car)
    {
        int me = f.PlayerCarIdx;
        if (FieldOrder.ClassOf(f, car) == FieldOrder.ClassOf(f, me)) return false;
        return f.Session.ByCar.TryGetValue(car, out var d) && d.ClassEstLapTime is > 0 and var lap
            && f.Session.ByCar.TryGetValue(me, out var mine) && mine.ClassEstLapTime is > 0 and var own && lap < own * FasterBy;
    }

    public static void Collect(TelemetryFrame f, EstProfile? profile, List<Approaching> into)
    {
        into.Clear();
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars || f.CarLapDistPct[me] < 0) return;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i] || f.CarLapDistPct[i] < 0) continue;
            if (FieldOrder.GapSeconds(f, me, i, profile) is not { } gap || gap >= 0) continue;
            into.Add(new Approaching(i, -gap, FasterClass(f, i)));
        }
        Order(into);
    }

    public static void Order(List<Approaching> cars) => cars.Sort(Soonest);

    public static RejoinPlan Plan(IReadOnlyList<Approaching> sorted, double needed)
    {
        if (sorted.Count == 0) return RejoinPlan.Clear;
        double prev = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            double gap = sorted[i].TimeToYou - prev;
            if (gap >= needed) return new RejoinPlan(i == 0, prev, gap, i, sorted[i].Car);
            prev = sorted[i].TimeToYou;
        }
        return new RejoinPlan(false, prev, null, sorted.Count, null);
    }

    public static int Within(IReadOnlyList<Approaching> sorted, double seconds)
    {
        int n = 0;
        while (n < sorted.Count && sorted[n].TimeToYou < seconds) n++;
        return n;
    }
}
