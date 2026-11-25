using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal enum OvertakeSide { Unknown, Left, Right, Both }

internal readonly record struct OvertakeThreat(int Car, double Gap, double? Trend, bool Alongside, OvertakeSide Side)
{
    public double Behind => Math.Max(0, -Gap);

    public double? GainPerLap(double? lap) => Trend is { } t && t < 0 && lap is > 0 and var l ? -t * l : null;

    public double? TimeToCatch => Trend is { } t && t < Overtake.Closing && Gap < 0 ? -Gap / -t : null;
}

internal static class Overtake
{
    public const double AlongsideWithin = 0.25, Hysteresis = 0.2, Closing = -0.005;

    public static OvertakeSide SideOf(CarSides sides) => sides switch
    {
        CarSides.Left or CarSides.TwoLeft => OvertakeSide.Left,
        CarSides.Right or CarSides.TwoRight => OvertakeSide.Right,
        CarSides.LeftRight => OvertakeSide.Both,
        _ => OvertakeSide.Unknown,
    };

    public static OvertakeThreat? Find(TelemetryFrame f, GapHistory gaps, EstProfile? profile, bool sameClass, double within, int held)
    {
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars || f.OnPitRoad || f.CarLapDistPct[me] < 0) return null;
        int myClass = FieldOrder.ClassOf(f, me);
        var side = SideOf(f.Sides);
        OvertakeThreat? beside = null, behind = null;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i]) continue;
            if (sameClass && FieldOrder.ClassOf(f, i) != myClass) continue;
            if (FieldOrder.GapSeconds(f, me, i, profile) is not { } gap) continue;
            double? trend = gaps.Trend(i);
            if (side != OvertakeSide.Unknown && Math.Abs(gap) <= AlongsideWithin)
            {
                if (beside is not { } b || Math.Abs(gap) < Math.Abs(b.Gap)) beside = new OvertakeThreat(i, gap, trend, true, side);
                continue;
            }
            if (gap >= 0) continue;
            bool kept = i == held;
            if (-gap > within + (kept ? Hysteresis : 0)) continue;
            bool closing = trend is { } t ? t < Closing || kept && t <= 0 : kept;
            if (!closing) continue;
            if (behind is not { } c || gap > c.Gap) behind = new OvertakeThreat(i, gap, trend, false, OvertakeSide.Unknown);
        }
        return beside ?? behind;
    }

    public static double? CatchPct(double myPct, double myEst, double lap, double seconds, EstProfile? profile)
    {
        if (!(lap > 0) || !(seconds >= 0) || myPct < 0) return null;
        double linear = myPct + seconds / lap;
        linear -= Math.Floor(linear);
        if (profile is null || !(myEst > 0)) return linear;
        double target = (myEst + seconds) % lap;
        double lo = 0, hi = 1 - 1e-9;
        if (profile.At(lo, lap) is not { } tlo || profile.At(hi, lap) is not { } thi) return linear;
        if (target <= tlo) return 0;
        if (target >= thi) return hi;
        for (int k = 0; k < 40; k++)
        {
            double mid = (lo + hi) / 2;
            if (profile.At(mid, lap) is not { } t) return linear;
            if (t < target) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2;
    }

    public static (int From, int To)? AtStake(TelemetryFrame f, int car)
    {
        int me = f.PlayerCarIdx;
        if (f.SessionKind != SessionKind.Race || (uint)me >= TelemetryFrame.MaxCars || (uint)car >= TelemetryFrame.MaxCars) return null;
        if (FieldOrder.ClassOf(f, me) != FieldOrder.ClassOf(f, car)) return null;
        int mine = f.CarClassPosition[me], theirs = f.CarClassPosition[car];
        if (mine <= 0 || theirs <= mine) return null;
        return (mine, mine + 1);
    }
}
