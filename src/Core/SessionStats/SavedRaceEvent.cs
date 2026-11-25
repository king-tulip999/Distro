using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SessionStats;

internal sealed record SavedRaceEvent(int Seq, int Lap, string Kind, double Time, double? Pct, int? Position, int? PositionBefore, string OtherNumber, string OtherName, int? Points, double? Seconds);

internal static class SavedRaceEvents
{
    public const string Pass = "pass", Lost = "lost", Off = "off", Incident = "incident", Pit = "pit", Fastest = "fastest";

    public static bool Matches(RaceEventsSnapshot s, SessionRow row) =>
        row.SessionType == nameof(SessionKind.Race) && s.Kind == SessionKind.Race && s.HasRace && s.Player >= 0
        && s.SessionNum == row.SessionNum && (s.Session.SubSessionId ?? 0) == row.SubSession && s.Session.TrackName == row.TrackName;

    public static List<SavedRaceEvent> Of(RaceEventsSnapshot s)
    {
        var list = new List<SavedRaceEvent>();
        int me = s.Player;
        if (me < 0) return list;
        foreach (var e in s.Events)
        {
            var saved = e.Kind switch
            {
                RaceEventKind.Pass when e.Car == me => Make(s, e, Pass, e.Other, Place(e.Position), Place(e.Position) + 1, null, null),
                RaceEventKind.Pass when e.Other == me => Make(s, e, Lost, e.Car, Place(e.Position) + 1, Place(e.Position), null, null),
                RaceEventKind.OffTrack when e.Car == me => Make(s, e, Off, -1, null, null, null, null),
                RaceEventKind.Incident when e.Car == me => Make(s, e, Incident, -1, null, null, e.Points > 0 ? e.Points : null, null),
                RaceEventKind.PitStop when e.Car == me => Make(s, e, Pit, -1, Place(e.Position), Place(e.PositionBefore), null, e.Seconds > 0 ? e.Seconds : null),
                RaceEventKind.FastestLap when e.Car == me => Make(s, e, Fastest, -1, null, null, null, e.Seconds > 0 ? e.Seconds : null),
                _ => null,
            };
            if (saved is not null) list.Add(saved with { Seq = list.Count + 1 });
        }
        return list;
    }

    private static int? Place(int position) => position > 0 ? position : null;

    private static SavedRaceEvent Make(RaceEventsSnapshot s, RaceMoment e, string kind, int other, int? position, int? before, int? points, double? seconds)
    {
        var car = other >= 0 ? s.Car(other) : null;
        return new SavedRaceEvent(0, Math.Max(1, e.Lap), kind, e.Time, e.Pct is >= 0 and <= 1 ? e.Pct : null, position, before,
            car?.Number ?? "", car?.Name ?? "", points, seconds);
    }
}
