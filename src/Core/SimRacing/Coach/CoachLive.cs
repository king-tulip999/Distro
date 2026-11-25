using Core.Plugins.BuiltIn;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Coach;

internal enum CoachState { NoStats, Reading, TooFewLaps, Ready }

internal sealed class CoachLive
{
    public const int KeptLaps = 40, ReadLaps = 200;
    private const double ReloadAfterS = 30;

    public static readonly CoachLive Shared = new();

    private string? _key;
    private int _generation = int.MinValue;
    private double _readAt = double.NegativeInfinity;
    private volatile bool _reading;
    private volatile CoachPlan? _plan;
    private volatile int _cleanLaps;
    private volatile bool _noStats;

    private CoachPlan? _built;
    private LapTrace? _lap;
    private double[] _time = Array.Empty<double>(), _speed = Array.Empty<double>(), _throttle = Array.Empty<double>(), _brake = Array.Empty<double>();
    private double[] _gear = Array.Empty<double>(), _steer = Array.Empty<double>(), _x = Array.Empty<double>(), _y = Array.Empty<double>();
    private CoachTurnResult?[] _results = Array.Empty<CoachTurnResult?>();
    private int _filled = -1;
    private int _lapNumber = int.MinValue;
    private double _lastD, _lastT, _lastSession;
    private double _px, _py;
    private bool _anchored, _usable;
    private long _seq = long.MinValue;

    public CoachPlan? Plan => _plan;
    public int CleanLaps => _cleanLaps;
    public LapTrace? Lap => _lap;
    public double Filled => _filled < 0 ? -1 : _filled * (_built?.Step ?? 1);
    public double[] X => _x;
    public double[] Y => _y;
    public bool HasLine { get; private set; }
    public int BrakeCrossings { get; private set; }

    public CoachState State => _noStats ? CoachState.NoStats : _plan is not null ? CoachState.Ready : _reading || _key is null ? CoachState.Reading : CoachState.TooFewLaps;

    public CoachTurnResult? Result(int turn) => (uint)turn < (uint)_results.Length ? _results[turn] : null;

    public void Update(TelemetryFrame f)
    {
        if (f.Sequence == _seq) return;
        _seq = f.Sequence;
        Read(f);
        var plan = _plan;
        if (!ReferenceEquals(plan, _built)) Rebuild(plan);
        if (plan is null || !f.Driving || f.LapDist is not { } d || f.LapCurrent is not { } t || !double.IsFinite(d) || !double.IsFinite(t)) return;
        int lap = f.Lap ?? 0;
        if (lap != _lapNumber || d < _lastD - plan.Length / 2)
        {
            Restart(lap);
            _usable = d < 50 && !f.OnPitRoad;
        }
        if (f.OnPitRoad) _usable = false;
        if (!_usable || d > plan.Length) { _lastD = d; return; }
        Position(f, plan, d);
        int upTo = Math.Min(_time.Length - 1, (int)Math.Floor(d / plan.Step));
        for (int i = _filled + 1; i <= upTo; i++)
        {
            double at = i * plan.Step, k = d > _lastD ? Math.Clamp((at - _lastD) / (d - _lastD), 0, 1) : 1;
            _time[i] = _filled < 0 && i == 0 ? Math.Max(0, t - d / Math.Max(1, f.Speed ?? 1)) : _lastT + (t - _lastT) * k;
            _speed[i] = f.Speed ?? 0;
            _throttle[i] = f.Throttle ?? 0;
            _brake[i] = f.Brake ?? 0;
            _gear[i] = f.Gear ?? 0;
            _steer[i] = f.Steering ?? 0;
            _x[i] = _px;
            _y[i] = _py;
        }
        if (upTo > _filled)
        {
            foreach (var turn in plan.Turns)
                if (turn.Brake is { } b && _lastD < b && d >= b) BrakeCrossings++;
            _filled = upTo;
            double done = _filled * plan.Step;
            foreach (var turn in plan.Turns)
                if (_results[turn.Index] is null && done >= Math.Min(turn.To, (_time.Length - 1) * plan.Step))
                    _results[turn.Index] = plan.Result(_lap!, turn);
        }
        _lastD = d;
        _lastT = t;
    }

    private void Position(TelemetryFrame f, CoachPlan plan, double d)
    {
        double now = f.SessionTime ?? 0;
        if (!_anchored)
        {
            var (bx, by) = plan.Line(plan.Best);
            _px = LapReplay.At(bx, plan.Step, d);
            _py = LapReplay.At(by, plan.Step, d);
            _anchored = true;
            _lastSession = now;
            HasLine = f.YawNorth is not null && f.VelocityX is not null && f.VelocityY is not null;
            return;
        }
        double dt = now - _lastSession;
        _lastSession = now;
        if (f.YawNorth is not { } h || f.VelocityX is not { } vx || f.VelocityY is not { } vy) { HasLine = false; return; }
        if (dt is <= 0 or > 0.5) return;
        _px += (vx * Math.Sin(h) - vy * Math.Cos(h)) * dt;
        _py += (vx * Math.Cos(h) + vy * Math.Sin(h)) * dt;
    }

    private void Restart(int lap)
    {
        _lapNumber = lap;
        _filled = -1;
        _lastD = 0;
        _lastT = 0;
        _anchored = false;
        Array.Clear(_results);
        Array.Fill(_speed, double.NaN);
        Array.Fill(_x, double.NaN);
        Array.Fill(_y, double.NaN);
    }

    private void Rebuild(CoachPlan? plan)
    {
        _built = plan;
        _lapNumber = int.MinValue;
        _usable = false;
        if (plan is null) { _lap = null; return; }
        int n = (int)Math.Floor(plan.Length / plan.Step) + 1;
        _time = new double[n]; _speed = new double[n]; _throttle = new double[n]; _brake = new double[n];
        _gear = new double[n]; _steer = new double[n]; _x = new double[n]; _y = new double[n];
        _lap = new LapTrace(plan.Step, (n - 1) * plan.Step, plan.Best.LapTime, 0, 0, n)
        {
            [TraceChannel.Time] = _time, [TraceChannel.Speed] = _speed, [TraceChannel.Throttle] = _throttle, [TraceChannel.Brake] = _brake,
            [TraceChannel.Gear] = _gear, [TraceChannel.Steering] = _steer, [TraceChannel.X] = _x, [TraceChannel.Y] = _y,
        };
        _results = new CoachTurnResult?[plan.Turns.Count];
        Restart(int.MinValue);
    }

    private void Read(TelemetryFrame f)
    {
        var me = f.Session.Player;
        if (me is null || f.Session.TrackDisplayName.Length == 0) return;
        string car = me.CarName.Length > 0 ? me.CarName : me.CarShortName, track = f.Session.TrackDisplayName, config = f.Session.TrackConfig;
        string key = string.Concat(car, "\n", track, "\n", config);
        var app = AppServices.Current;
        var stats = app is null ? null : SessionStatsPlugin.Of(app);
        int generation = stats?.Generation ?? 0;
        double now = f.SessionTime ?? 0;
        bool changed = key != _key;
        if (!changed && (generation == _generation || now - _readAt < ReloadAfterS || _reading)) return;
        if (changed) { _plan = null; _cleanLaps = 0; }
        _key = key;
        _generation = generation;
        _readAt = now;
        if (stats is null) { _noStats = true; return; }
        _noStats = false;
        _reading = true;
        Task.Run(() =>
        {
            try
            {
                var traces = stats.Traces;
                if (traces is null) { _noStats = true; return; }
                var clean = traces.Recent(car, track, config, ReadLaps, KeptLaps, null).Clean;
                var plan = CoachPlan.Build(clean);
                if (_key != key) return;
                _cleanLaps = clean.Count;
                _plan = plan;
            }
            catch (Exception ex) { App.LogError(ex); }
            finally { _reading = false; }
        });
    }
}
