using Core.SessionStats;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal sealed record FinishCard(string Driver, string Car, string CarClass, string Track, string Config, int Finish, int? Start, int? Incidents,
    bool Official, int? IRating, double? IRatingChange, IReadOnlyList<(double Time, bool Clean)> Laps, int PitStops, IReadOnlyList<Award> Awards, int? Overall = null, int? LapsDone = null, double? RaceSeconds = null, double? BestLap = null)
{
    public int? Gained => Start is > 0 and var start ? start - Finish : null;
    public double? Best => BestLap ?? Laps.Where(l => l.Clean).Select(l => (double?)l.Time).Min();
    public double? AverageClean => RaceFinish.AverageClean(Laps);
    public double? Consistency => RaceResult.Consistency(Laps);
    public int LapCount => LapsDone ?? Laps.Count;
    public double? RaceTime => RaceSeconds ?? (LapsDone is null || LapsDone == Laps.Count ? Laps.Sum(l => l.Time) : null);
    public string Place => Config.Length > 0 ? $"{Track} · {Config}" : Track;

    public static FinishCard Sample { get; } = new("Sam Driver", "Dallara IR18", "IR18", "WeatherTech Raceway at Laguna Seca", "Full Course", 3, 9, 2, true, 2286, 41,
        new (double, bool)[]
        {
            (71.42, true), (69.31, true), (69.05, true), (68.96, true), (69.88, false), (68.91, true), (68.80, true), (69.12, true), (68.95, true), (73.60, false),
            (69.20, true), (68.99, true), (69.07, true), (68.86, true), (69.01, true), (68.94, true), (69.25, true), (68.90, true),
        }, 1, new[] { Award.Podium, Award.Charger, Award.PersonalBest }, 5);
}

internal sealed class RaceFinish
{
    public const double HistoryGapSeconds = 60;
    public const double SettleSeconds = 5;

    private (long? SubSession, int SessionNum, int? TrackId, string? CarPath) _key = (null, -1, null, null);
    private int? _lapsAtCheckered;
    private int _lapsAtLine;
    private bool _beforeGreen, _official;
    private double? _greenAt;
    private DateTime _startUtc;
    private RaceFacts? _facts;
    private IReadOnlyList<RaceFacts> _history = Array.Empty<RaceFacts>();

    public FinishCard? Card { get; private set; }
    public double FinishedAt { get; private set; }

    public static bool Crossed(SessionPhase phase, int? lapsAtCheckered, int? lapCompleted) =>
        phase == SessionPhase.CoolDown || lapsAtCheckered is { } at && lapCompleted is { } done && done > at;

    public static double? AverageClean(IReadOnlyList<(double Time, bool Clean)> laps)
    {
        if (laps.Count == 0) return null;
        var sorted = laps.Select(l => l.Time).OrderBy(t => t).ToList();
        double median = sorted[sorted.Count / 2];
        var pace = laps.Where(l => l.Clean && l.Time <= median * RaceResult.PaceWindow).Select(l => l.Time).ToList();
        return pace.Count > 0 ? pace.Average() : null;
    }

    public static List<Award> AwardsFor(RaceFacts race, IReadOnlyList<RaceFacts> history) =>
        RaceResult.Awards(race, history.Where(h => h.StartUtc < race.StartUtc.AddSeconds(-HistoryGapSeconds)).ToList());

    private static (int Finish, int? Overall, bool Official) Placing(TelemetryFrame f, int lapsDone)
    {
        int car = f.PlayerCarIdx;
        bool live = (uint)car < TelemetryFrame.MaxCars;
        var result = f.Session.ResultOf(f.SessionNum, car);
        bool official = result is { ClassPosition: > 0 } && result.LapsComplete >= lapsDone;
        int finish = official ? result!.ClassPosition : live && f.CarClassPosition[car] > 0 ? f.CarClassPosition[car] : f.PlayerClassPosition ?? 0;
        int? overall = result is { Position: > 0 } && result.LapsComplete >= lapsDone ? result.Position : live && f.CarPosition[car] > 0 ? f.CarPosition[car] : null;
        return (finish, overall, official);
    }

    public void Update(TelemetryFrame f, RaceState race, double now, DateTime utcNow)
    {
        var key = (f.Session.SubSessionId, f.SessionNum, f.Session.TrackId, f.Session.Player?.CarPath);
        if (key != _key)
        {
            _key = key;
            _lapsAtCheckered = null;
            _beforeGreen = false;
            _greenAt = null;
            _official = false;
            _facts = null;
            _history = Array.Empty<RaceFacts>();
            Card = null;
            _startUtc = utcNow.AddSeconds(-(f.SessionTime ?? 0));
        }
        if (f.SessionKind != SessionKind.Race || f.Session.Player is not { } me) return;
        if (Card is not null)
        {
            Settle(f, now);
            return;
        }
        if (f.Phase < SessionPhase.Racing) _beforeGreen = true;
        else if (f.Phase == SessionPhase.Racing && _beforeGreen) _greenAt ??= f.SessionTime;
        if (f.Phase == SessionPhase.Checkered) _lapsAtCheckered ??= f.LapCompleted;
        if (!Crossed(f.Phase, _lapsAtCheckered, f.LapCompleted)) return;
        int car = f.PlayerCarIdx;
        _lapsAtLine = f.LapCompleted ?? 0;
        var (finish, overall, official) = Placing(f, _lapsAtLine);
        if (finish <= 0) return;
        _official = official;
        int? start = f.Session.GridClassPositions.TryGetValue(car, out var grid) && grid > 0 ? grid : null;
        var laps = race.Laps.Laps.Select(l => (l.Time, l.Clean)).ToList();
        double? raceSeconds = _greenAt is { } green && f.SessionTime is { } line && line > green ? line - green : null;
        string carName = me.CarName.Length > 0 ? me.CarName : me.CarShortName;
        var card = new FinishCard(me.Name, carName, me.ClassName.Length > 0 ? me.ClassName : me.CarShortName, f.Session.TrackDisplayName, f.Session.TrackConfig,
            finish, start, f.Incidents, f.Session.Official, RaceResult.KnownRating(me.IRating) ? me.IRating : null, f.Session.Official ? race.IRatingChangeOf(car) : null,
            laps, race.StopsOf(car), Array.Empty<Award>(), overall, f.LapCompleted is > 0 ? _lapsAtLine : null, raceSeconds, f.LapBest);
        _facts = new RaceFacts(0, _startUtc, "Race", carName, f.Session.TrackDisplayName, f.Session.TrackConfig, start, finish, true, f.Incidents,
            card.RaceTime ?? laps.Sum(l => l.Time), laps.Where(l => l.Clean).Select(l => (double?)l.Time).Min(), RaceResult.Consistency(laps));
        Card = card with { Awards = AwardsFor(_facts, _history) };
        FinishedAt = now;
        if (!RaceHistory.Available) return;
        var at = _key;
        RaceHistory.LoadAsync().ContinueWith(t =>
        {
            if (_key != at || Card is not { } shown || _facts is not { } facts) return;
            _history = t.Result;
            Card = shown with { Awards = AwardsFor(facts, t.Result) };
        }, TaskScheduler.Default);
    }

    private void Settle(TelemetryFrame f, double now)
    {
        if (_official || Card is not { } shown || _facts is not { } facts) return;
        var (finish, overall, official) = Placing(f, _lapsAtLine);
        if (!official && now - FinishedAt > SettleSeconds) return;
        _official = official;
        double? best = f.LapBest ?? shown.BestLap;
        if (finish <= 0 || finish == shown.Finish && overall == shown.Overall && best == shown.BestLap) return;
        _facts = facts with { FinishPos = finish };
        Card = shown with { Finish = finish, Overall = overall, BestLap = best, Awards = AwardsFor(_facts, _history) };
    }
}
