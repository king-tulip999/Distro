namespace Core.SimRacing.Race;

internal static class RaceEventLaps
{
    public static readonly RaceEventKind[] Kinds = { RaceEventKind.Pass, RaceEventKind.OffTrack, RaceEventKind.Incident, RaceEventKind.PitStop };
    public const int MaxBars = 44;

    public static int Current(RaceEventsSnapshot s)
    {
        int lap = 0;
        foreach (var now in s.Field.Values) lap = Math.Max(lap, now.LapsDone + 1);
        foreach (var e in s.Events) lap = Math.Max(lap, e.Lap);
        return Total(s) is int total ? Math.Min(lap, total) : lap;
    }

    public static int Slots(RaceEventsSnapshot s, int current) => Math.Max(current, Total(s) ?? 0);

    private static int? Total(RaceEventsSnapshot s) => s.LapsTotal is int t and > 0 and < 1000 ? t : null;

    public static (int[][] Bars, int Group) Count(IReadOnlyList<RaceMoment> events, int laps)
    {
        int group = laps <= 0 ? 1 : (laps + MaxBars - 1) / MaxBars;
        int count = laps <= 0 ? 0 : (laps + group - 1) / group;
        var bars = new int[count][];
        for (int i = 0; i < count; i++) bars[i] = new int[Kinds.Length];
        if (count == 0) return (bars, group);
        foreach (var e in events)
        {
            int kind = Array.IndexOf(Kinds, e.Kind);
            if (kind < 0) continue;
            int lap = Math.Clamp(e.Lap, 1, laps);
            bars[(lap - 1) / group][kind]++;
        }
        return (bars, group);
    }
}
