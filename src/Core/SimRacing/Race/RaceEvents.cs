using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal enum RaceEventKind { Pass, OffTrack, Incident, PitStop, FastestLap, NotRecorded }

internal sealed record RaceMoment(int Id, RaceEventKind Kind, double Time, int Lap, int Car, int Other = -1, int Position = 0, double Pct = -1,
    int Points = 0, int Total = 0, double Seconds = 0, int PositionBefore = 0, double End = 0);

internal sealed record RaceLap(int Number, double Start, double? Time, bool PitLap);

internal sealed record RaceCar(int CarIdx, string Name, string Number, int NumberCode, int ClassId, string ClassName, int? Grid, int? IRating, long UserId)
{
    public string Key => UserId > 0 ? "id:" + UserId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "name:" + Name;
}

internal readonly record struct RaceCarNow(int Position, int ClassPosition, int LapsDone, double Progress, double Gap, bool OnPitRoad, bool InWorld);

internal sealed record RaceEventsSnapshot
{
    public static readonly RaceEventsSnapshot Empty = new();

    public long Version { get; init; }
    public bool Connected { get; init; }
    public SessionInfo Session { get; init; } = SessionInfo.Empty;
    public int SessionNum { get; init; }
    public SessionKind Kind { get; init; } = SessionKind.Other;
    public double Now { get; init; }
    public double? ReplayAt { get; init; }
    public bool Driving { get; init; }
    public int? LapsTotal { get; init; }
    public double? TimeTotal { get; init; }
    public double? RaceStart { get; init; }
    public int Player { get; init; } = -1;
    public int Camera { get; init; } = -1;
    public int CamGroup { get; init; }
    public int CamCamera { get; init; }
    public bool Multiclass { get; init; }
    public IReadOnlyDictionary<int, RaceCar> Cars { get; init; } = new Dictionary<int, RaceCar>();
    public IReadOnlyList<RaceMoment> Events { get; init; } = Array.Empty<RaceMoment>();
    public IReadOnlyDictionary<int, IReadOnlyList<RaceLap>> Laps { get; init; } = new Dictionary<int, IReadOnlyList<RaceLap>>();
    public IReadOnlyDictionary<int, RaceCarNow> Field { get; init; } = new Dictionary<int, RaceCarNow>();

    public bool HasRace => Cars.Count > 0;

    public double Behind => ReplayAt is { } at ? Math.Max(0, Now - at) : 0;

    public RaceCar? Car(int idx) => Cars.TryGetValue(idx, out var c) ? c : null;

    public IReadOnlyList<RaceLap> LapsOf(int idx) => Laps.TryGetValue(idx, out var l) ? l : Array.Empty<RaceLap>();

    public double? LapStart(int idx, int number) => LapsOf(idx).FirstOrDefault(l => l.Number == number)?.Start;
}

internal sealed class RaceEventRecorder
{
    public const double PassHold = 2.0;
    public const double OffTrackMerge = 3.0;
    public const double Step = 0.1;
    public const double LiveSlack = 2.0;
    public const double LapTimeWait = 5.0;
    private const int N = TelemetryFrame.MaxCars;

    private readonly object _gate = new();
    private readonly List<RaceMoment> _events = new();
    private readonly List<RaceLap>?[] _laps = new List<RaceLap>?[N];
    private readonly double[] _prog = new double[N], _stepStart = new double[N];
    private readonly bool[] _had = new bool[N];
    private readonly TrackSurface[] _surface = new TrackSurface[N];
    private readonly bool[] _pit = new bool[N], _pitThisLap = new bool[N];
    private readonly double[] _pitIn = new double[N];
    private readonly int[] _pitInPos = new int[N];
    private readonly double[] _offEnd = new double[N];
    private readonly bool[] _off = new bool[N];
    private readonly int[] _lapsDone = new int[N];
    private readonly double[] _lapStart = new double[N];
    private readonly double[] _lastLapSeen = new double[N];
    private readonly int[] _pendingLap = new int[N];
    private readonly double[] _pendingAt = new double[N], _pendingStart = new double[N];
    private readonly bool[] _pendingPit = new bool[N];
    private readonly int[] _class = new int[N];
    private readonly Dictionary<int, double> _classBest = new();
    private readonly List<(int A, int B, double At, double Pct)> _crossings = new();
    private readonly List<(int, int)> _undone = new();
    private readonly RaceCarNow[] _now = new RaceCarNow[N];

    private (long? Sub, int Num, string Track)? _sessionKey;
    private SessionInfo _session = SessionInfo.Empty;
    private int _sessionNum;
    private SessionKind _kind = SessionKind.Other;
    private double _highWater = double.NegativeInfinity, _lastStep = double.NegativeInfinity;
    private double? _pausedAt;
    private bool _baseline;
    private int? _incidents;
    private int _nextId;
    private long _version;
    private bool _connected, _driving;
    private double? _replayAt;
    private int _player = -1, _camera = -1, _camGroup, _camCamera;
    private int? _lapsTotal;
    private double? _timeTotal, _raceStart;
    private bool _wasDriving;

    public event Action? LeftCar;

    public RaceEventRecorder() => Array.Fill(_lapStart, double.NaN);

    public long Version { get { lock (_gate) return _version; } }

    public void Feed(TelemetryFrame f)
    {
        bool leftCar = false;
        lock (_gate)
        {
            if (f.Status != SimStatus.Connected || f.SessionTime is not { } time)
            {
                if (_connected) { _connected = false; _version++; }
                return;
            }
            var key = (f.Session.SubSessionId, f.SessionNum, f.Session.TrackName);
            if (f.Session.Drivers.Count > 0 && key != _sessionKey)
            {
                Reset();
                _sessionKey = key;
                _version++;
            }
            if (f.Session.Drivers.Count > 0) _session = f.Session;
            _sessionNum = f.SessionNum;
            _kind = f.SessionKind;
            _connected = true;
            _player = f.PlayerCarIdx;
            _camera = f.CamCarIdx;
            _camGroup = f.CamGroup ?? 0;
            _camCamera = f.CamCamera ?? 0;
            _lapsTotal = f.SessionLapsTotal;
            _timeTotal = f.SessionTimeTotal;
            _driving = f.Driving;
            bool behind = f.IsReplayPlaying && f.ReplayFramesToLive is > 120;
            _replayAt = f.IsReplayPlaying && f.ReplaySessionTime is { } at && f.ReplaySessionNum == f.SessionNum ? at : behind ? time : null;
            bool live = !behind && time >= _highWater - LiveSlack;
            if (time > _highWater) _highWater = time;
            if (_replayAt is null && behind) _replayAt = time;

            if (_wasDriving && !f.Driving && _kind == SessionKind.Race && f.Phase == SessionPhase.Racing) leftCar = true;
            _wasDriving = f.Driving;

            if (!live)
            {
                _pausedAt ??= _lastStep > double.NegativeInfinity ? _lastStep : time;
                _baseline = false;
                return;
            }
            if (_pausedAt is { } paused)
            {
                if (time - paused > Step * 5) Add(new RaceMoment(_nextId++, RaceEventKind.NotRecorded, paused, LapOf(_player), -1, End: time));
                _pausedAt = null;
                _baseline = false;
            }
            if (time - _lastStep < Step && _baseline) return;
            _lastStep = time;
            Read(f, time);
        }
        if (leftCar) LeftCar?.Invoke();
    }

    private void Read(TelemetryFrame f, double time)
    {
        bool racing = f.Phase is SessionPhase.Racing or SessionPhase.Checkered;
        if (racing && _raceStart is null)
        {
            _raceStart = time;
            for (int i = 0; i < N; i++)
                if (_had[i] && _lapsDone[i] == 0) _lapStart[i] = time;
        }
        for (int i = 0; i < N; i++)
        {
            bool inWorld = f.CarLapDistPct[i] >= 0 && f.CarSurface[i] != TrackSurface.NotInWorld && f.Session.ByCar.TryGetValue(i, out var d) && d.Races;
            _class[i] = f.Session.ByCar.TryGetValue(i, out var entry) ? entry.ClassId : 0;
            double prog = inWorld ? f.CarLap[i] + f.CarLapDistPct[i] : _prog[i];
            _now[i] = new RaceCarNow(f.CarPosition[i], f.CarClassPosition[i], f.CarLapCompleted[i], prog, f.CarF2Time[i], f.CarOnPitRoad[i], inWorld);
            if (!inWorld) { _had[i] = false; continue; }
            if (!_baseline || !_had[i])
            {
                _prog[i] = _stepStart[i] = prog;
                _surface[i] = f.CarSurface[i];
                _pit[i] = f.CarOnPitRoad[i];
                _lapsDone[i] = f.CarLapCompleted[i];
                if (_lapsDone[i] == 0 && _raceStart is { } green && double.IsNaN(_lapStart[i])) _lapStart[i] = green;
                if (_lastLapSeen[i] == 0) _lastLapSeen[i] = f.CarLastLap[i];
                _had[i] = true;
                continue;
            }
            _stepStart[i] = _prog[i];
            _prog[i] = prog;
            if (racing) Track(f, i, time);
        }
        if (_baseline && racing && _kind == SessionKind.Race) Passes(f, time);
        if (_baseline) Incidents(f, time);
        else _incidents = f.Incidents;
        _baseline = true;
        _version++;
    }

    private void Track(TelemetryFrame f, int i, double time)
    {
        var surface = f.CarSurface[i];
        bool off = surface == TrackSurface.OffTrack;
        if (off && !_off[i])
        {
            bool merge = time - _offEnd[i] <= OffTrackMerge && _offEnd[i] > 0;
            if (!merge) Add(new RaceMoment(_nextId++, RaceEventKind.OffTrack, time, LapOf(i), i, Pct: f.CarLapDistPct[i]));
        }
        if (!off && _off[i]) _offEnd[i] = time;
        _off[i] = off;
        _surface[i] = surface;

        bool pit = f.CarOnPitRoad[i];
        if (pit) _pitThisLap[i] = true;
        if (pit && !_pit[i])
        {
            _pitIn[i] = time;
            _pitInPos[i] = Rank(i);
        }
        if (!pit && _pit[i] && _pitIn[i] > 0)
        {
            Add(new RaceMoment(_nextId++, RaceEventKind.PitStop, _pitIn[i], LapOf(i), i, Position: Rank(i), PositionBefore: _pitInPos[i], Seconds: time - _pitIn[i]));
            _pitIn[i] = 0;
        }
        _pit[i] = pit;

        int done = f.CarLapCompleted[i];
        if (done > _lapsDone[i] && done > 0)
        {
            if (_pendingLap[i] > 0) CommitLap(i, null);
            _pendingLap[i] = done;
            _pendingAt[i] = time;
            _pendingStart[i] = _lapStart[i];
            _pendingPit[i] = _pitThisLap[i];
            _lapStart[i] = time;
            _pitThisLap[i] = pit;
        }
        _lapsDone[i] = done;
        double last = f.CarLastLap[i];
        if (_pendingLap[i] > 0)
        {
            if (last > 0 && last != _lastLapSeen[i]) CommitLap(i, last);
            else if (time - _pendingAt[i] > LapTimeWait) CommitLap(i, null);
        }
        if (last > 0) _lastLapSeen[i] = last;
    }

    private void CommitLap(int i, double? lapTime)
    {
        int number = _pendingLap[i];
        double start = _pendingStart[i];
        double end = _pendingAt[i];
        _pendingLap[i] = 0;
        if (double.IsNaN(start)) return;
        var laps = _laps[i] ??= new List<RaceLap>();
        double? time = lapTime ?? end - start;
        laps.Add(new RaceLap(number, start, time, _pendingPit[i]));
        if (number <= 1 || time is not { } t || _kind != SessionKind.Race) return;
        int cls = _class[i];
        if (_classBest.TryGetValue(cls, out var best) && t >= best) return;
        _classBest[cls] = t;
        Add(new RaceMoment(_nextId++, RaceEventKind.FastestLap, end, number, i, Seconds: t));
    }

    private void Passes(TelemetryFrame f, double time)
    {
        _undone.Clear();
        for (int c = _crossings.Count - 1; c >= 0; c--)
        {
            var (a, b, at, pct) = _crossings[c];
            if (!_had[a] || !_had[b] || _prog[a] <= _prog[b] || _pit[a] || _pit[b])
            {
                _crossings.RemoveAt(c);
                _undone.Add((b, a));
                continue;
            }
            if (time - at < PassHold) continue;
            _crossings.RemoveAt(c);
            Add(new RaceMoment(_nextId++, RaceEventKind.Pass, at, LapOf(a), a, b, Rank(a), pct));
        }
        for (int a = 0; a < N; a++)
        {
            if (!_had[a] || _surface[a] != TrackSurface.OnTrack || _pit[a]) continue;
            for (int b = 0; b < N; b++)
            {
                if (a == b || !_had[b] || _class[a] != _class[b] || _surface[b] != TrackSurface.OnTrack || _pit[b]) continue;
                double before = _stepStart[a] - _stepStart[b], after = _prog[a] - _prog[b];
                if (before >= 0 || after <= 0 || after - before > 0.5) continue;
                bool known = _undone.Contains((a, b));
                foreach (var x in _crossings) if (x.A == a && x.B == b) { known = true; break; }
                if (!known) _crossings.Add((a, b, time, f.CarLapDistPct[a]));
            }
        }
    }

    private void Incidents(TelemetryFrame f, double time)
    {
        if (f.Incidents is not { } now) return;
        if (_incidents is { } was && now > was && (uint)_player < N)
            Add(new RaceMoment(_nextId++, RaceEventKind.Incident, time, LapOf(_player), _player, Points: now - was, Total: now, Pct: f.LapDistPct ?? -1));
        _incidents = now;
    }

    private int Rank(int i)
    {
        int rank = 1;
        for (int j = 0; j < N; j++)
            if (j != i && _class[j] == _class[i] && _prog[j] > 0 && _prog[j] > _prog[i]) rank++;
        return rank;
    }

    private int LapOf(int i) => (uint)i < N ? Math.Max(1, _lapsDone[i] + 1) : 1;

    private void Add(RaceMoment e)
    {
        _events.Add(e);
        _version++;
    }

    private void Reset()
    {
        _events.Clear();
        Array.Clear(_laps);
        Array.Clear(_prog);
        Array.Clear(_stepStart);
        Array.Clear(_had);
        Array.Clear(_pit);
        Array.Clear(_pitThisLap);
        Array.Clear(_pitIn);
        Array.Clear(_offEnd);
        Array.Clear(_off);
        Array.Clear(_lapsDone);
        Array.Fill(_lapStart, double.NaN);
        Array.Clear(_lastLapSeen);
        Array.Clear(_pendingLap);
        _classBest.Clear();
        _crossings.Clear();
        _highWater = _lastStep = double.NegativeInfinity;
        _raceStart = null;
        _pausedAt = null;
        _baseline = false;
        _incidents = null;
        _nextId = 0;
    }

    public RaceEventsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var session = _session;
            var cars = new Dictionary<int, RaceCar>();
            foreach (var d in session.Drivers)
            {
                if (!d.Races || d.CarIdx < 0) continue;
                int code = d.CarNumberRaw ?? Replay.IracingReplay.NumberCode(d.CarNumber);
                session.GridPositions.TryGetValue(d.CarIdx, out int grid);
                session.GridClassPositions.TryGetValue(d.CarIdx, out int classGrid);
                cars[d.CarIdx] = new RaceCar(d.CarIdx, d.Name, d.CarNumber, code, d.ClassId, d.ClassLabel, session.NumCarClasses > 1 ? (classGrid > 0 ? classGrid : null) : (grid > 0 ? grid : null), d.IRating, d.UserId);
            }
            var laps = new Dictionary<int, IReadOnlyList<RaceLap>>();
            var field = new Dictionary<int, RaceCarNow>();
            for (int i = 0; i < N; i++)
            {
                if (_laps[i] is { Count: > 0 } l) laps[i] = l.ToArray();
                if (cars.ContainsKey(i)) field[i] = _now[i];
            }
            return new RaceEventsSnapshot
            {
                Version = _version,
                Connected = _connected,
                Session = session,
                SessionNum = _sessionNum,
                Kind = _kind,
                Now = double.IsFinite(_highWater) ? _highWater : 0,
                ReplayAt = _replayAt,
                Driving = _driving,
                LapsTotal = _lapsTotal,
                TimeTotal = _timeTotal,
                RaceStart = _raceStart,
                Player = _player,
                Camera = _camera,
                CamGroup = _camGroup,
                CamCamera = _camCamera,
                Multiclass = session.NumCarClasses > 1,
                Cars = cars,
                Events = _events.OrderBy(e => e.Time).ThenBy(e => e.Id).ToArray(),
                Laps = laps,
                Field = field,
            };
        }
    }
}
