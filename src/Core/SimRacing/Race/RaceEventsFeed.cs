using System.Diagnostics;
using Core.SimRacing.Replay;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal interface IRaceEventsFeed
{
    RaceEventsSnapshot Snapshot();

    bool JumpTo(double time, int? car);

    bool Watch(int car);

    bool ToLive();
}

internal sealed class LiveRaceEvents : IRaceEventsFeed
{
    private readonly RaceEventRecorder _recorder;
    private readonly Func<string, bool> _allowed;

    public LiveRaceEvents(RaceEventRecorder recorder, Func<string, bool> allowed)
    {
        _recorder = recorder;
        _allowed = allowed;
    }

    public RaceEventsSnapshot Snapshot() => _recorder.Snapshot();

    public bool JumpTo(double time, int? car)
    {
        var s = _recorder.Snapshot();
        if (!_allowed("iRacing replay")) return false;
        bool jumped = IracingReplay.JumpTo(s.SessionNum, time);
        if (car is { } c) Watch(s, c);
        return jumped;
    }

    public bool Watch(int car)
    {
        var s = _recorder.Snapshot();
        return _allowed("iRacing replay") && Watch(s, car);
    }

    private static bool Watch(RaceEventsSnapshot s, int car) =>
        s.Car(car) is { } c && IracingReplay.Watch(c.NumberCode, s.CamGroup, s.CamCamera);

    public bool ToLive() => _allowed("iRacing replay") && IracingReplay.ToLive();
}

internal sealed class DemoRaceEvents : IRaceEventsFeed
{
    private const double FrameStep = RaceEventRecorder.Step;
    private const double HistoryFrom = -14 * 60;
    private static readonly int[] OffTrackCars = { 3, 7, 12, 16, 19, 24 };

    private readonly RaceEventRecorder _recorder = new();
    private readonly DemoTelemetry _demo = new();
    private readonly TelemetryFrame _frame = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private double _fed = HistoryFrom;
    private double? _replayAt;
    private double _replayClock;
    private int? _camera;

    public Task Warm() => Task.Run(() => FeedTo(0));

    private void FeedTo(double since)
    {
        lock (_gate)
        {
            while (_fed + FrameStep <= since)
            {
                _fed += FrameStep;
                _demo.Fill(_frame, _fed);
                AddOffTracks(_frame);
                _recorder.Feed(_frame);
            }
        }
    }

    private static void AddOffTracks(TelemetryFrame f)
    {
        if (f.SessionTime is not { } t) return;
        for (int k = 0; k < OffTrackCars.Length; k++)
        {
            int car = OffTrackCars[k];
            double phase = (t + k * 151) % 520;
            if (phase is > 0 and < 1.6 && f.CarSurface[car] == TrackSurface.OnTrack) f.CarSurface[car] = TrackSurface.OffTrack;
        }
    }

    public RaceEventsSnapshot Snapshot()
    {
        FeedTo(_clock.Elapsed.TotalSeconds);
        var s = _recorder.Snapshot();
        double? replay = null;
        lock (_gate)
            if (_replayAt is { } at)
            {
                replay = Math.Min(s.Now, at + (_clock.Elapsed.TotalSeconds - _replayClock));
                if (replay >= s.Now - 0.5) _replayAt = replay = null;
            }
        return s with { ReplayAt = replay, Camera = _camera ?? s.Player, Driving = false };
    }

    public bool JumpTo(double time, int? car)
    {
        lock (_gate)
        {
            _replayAt = Math.Max(0, time);
            _replayClock = _clock.Elapsed.TotalSeconds;
            if (car is { } c) _camera = c;
        }
        return true;
    }

    public bool Watch(int car)
    {
        lock (_gate) _camera = car;
        return true;
    }

    public bool ToLive()
    {
        lock (_gate) _replayAt = null;
        return true;
    }
}
