using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal enum SpeedState { Under, Near, Over }

internal static class PitLane
{
    public const double NearLimitKph = 2;
    public const double MinSpan = 0.004, Margin = 0.15;

    public static double? MetresTo(double? fromPct, double? toPct, double? trackKm) =>
        fromPct is { } from && toPct is { } to && from >= 0 && to >= 0 && trackKm is > 0 and var km ? FieldOrder.Wrap(to, from) * km * 1000 : null;

    public static SpeedState State(double speedKph, double limitKph) =>
        speedKph > limitKph + 0.5 ? SpeedState.Over : speedKph >= limitKph - NearLimitKph ? SpeedState.Near : SpeedState.Under;

    public static (double From, double Length) Span(double box, double? entry, double? exit, ReadOnlySpan<double> others)
    {
        double lo = 0, hi = 0;
        foreach (double o in others)
        {
            if (o < 0) continue;
            double w = FieldOrder.Wrap(o, box);
            lo = Math.Min(lo, w);
            hi = Math.Max(hi, w);
        }
        double width = Math.Max(MinSpan * 4, hi - lo);
        lo -= width * Margin;
        hi += width * Margin;
        if (entry is { } e && FieldOrder.Wrap(e, box) is < 0 and var we) lo = we;
        if (exit is { } x && FieldOrder.Wrap(x, box) is > 0 and var wx) hi = wx;
        lo = Math.Min(lo, -MinSpan);
        hi = Math.Max(hi, MinSpan);
        double from = box + lo;
        return (from - Math.Floor(from), hi - lo);
    }

    public static double Place(double pct, double from, double length) =>
        length <= 0 ? 0.5 : FieldOrder.Wrap(pct, from) is var d && d < -0.25 ? (d + 1) / length : d / length;
}

internal sealed class PitLaneLearner
{
    public const int Kept = 12;
    private const double Teleport = 0.01;

    private readonly bool[] _was = new bool[TelemetryFrame.MaxCars];
    private readonly double[] _pct = new double[TelemetryFrame.MaxCars];
    private readonly bool[] _known = new bool[TelemetryFrame.MaxCars];
    private readonly double[] _entries = new double[Kept], _exits = new double[Kept], _scratch = new double[Kept];
    private int _entryCount, _exitCount, _entryNext, _exitNext;
    private long _sequence = -1;
    private (int? Track, string Config) _track = (null, "");

    public double? EntryPct { get; private set; }
    public double? ExitPct { get; private set; }

    public void Clear()
    {
        Array.Clear(_known);
        _entryCount = _exitCount = _entryNext = _exitNext = 0;
        EntryPct = ExitPct = null;
    }

    public void Observe(TelemetryFrame f)
    {
        if (f.Sequence == _sequence && f.Sequence != 0) return;
        _sequence = f.Sequence;
        var track = (f.Session.TrackId, f.Session.TrackConfig);
        if (track != _track)
        {
            _track = track;
            Clear();
        }
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            double pct = f.CarLapDistPct[i];
            bool live = pct >= 0 && f.CarSurface[i] != TrackSurface.NotInWorld;
            if (!live)
            {
                _known[i] = false;
                continue;
            }
            bool on = f.CarOnPitRoad[i];
            if (_known[i] && on != _was[i] && Math.Abs(FieldOrder.Wrap(pct, _pct[i])) < Teleport)
            {
                Learn(on, Mid(_pct[i], pct));
            }
            _was[i] = on;
            _pct[i] = pct;
            _known[i] = true;
        }
    }

    public void Learn(bool enteredPitRoad, double pct)
    {
        if (enteredPitRoad)
        {
            Add(_entries, ref _entryCount, ref _entryNext, pct);
            EntryPct = Median(_entries, _entryCount);
        }
        else
        {
            Add(_exits, ref _exitCount, ref _exitNext, pct);
            ExitPct = Median(_exits, _exitCount);
        }
    }

    private static double Mid(double a, double b)
    {
        double m = a + FieldOrder.Wrap(b, a) / 2;
        return m - Math.Floor(m);
    }

    private static void Add(double[] list, ref int count, ref int next, double pct)
    {
        list[next] = pct;
        next = (next + 1) % Kept;
        count = Math.Min(Kept, count + 1);
    }

    private double? Median(double[] list, int count)
    {
        if (count == 0) return null;
        double first = list[0];
        for (int i = 0; i < count; i++) _scratch[i] = FieldOrder.Wrap(list[i], first);
        Array.Sort(_scratch, 0, count);
        double mid = count % 2 == 1 ? _scratch[count / 2] : (_scratch[count / 2 - 1] + _scratch[count / 2]) / 2;
        double pct = first + mid;
        return pct - Math.Floor(pct);
    }
}

internal sealed class PitFill
{
    private const double Stalled = 1.5, MinRiseTime = 0.3, Rise = 1e-4;

    private bool _active;
    private double _startFuel, _request, _target, _lastFuel, _lastTime;
    private double? _riseFrom, _riseFuel, _lastRiseAt;
    private double _repairFrom, _optRepairFrom;

    public double? Rate { get; private set; }
    public double? LitresLeft { get; private set; }
    public double? Progress { get; private set; }
    public double? SecondsLeft { get; private set; }
    public double RepairTotal => _repairFrom;
    public double OptRepairTotal => _optRepairFrom;
    public bool Active => _active;
    public bool Rising => _lastRiseAt is { } at && _lastTime - at < Stalled;

    public void Reset()
    {
        _active = false;
        Rate = LitresLeft = Progress = SecondsLeft = null;
        _riseFrom = _riseFuel = _lastRiseAt = null;
        _repairFrom = _optRepairFrom = 0;
    }

    public void Update(bool stopActive, double? fuel, double? request, double? tankMax, double? repairLeft, double? optRepairLeft, double time)
    {
        if (!stopActive || fuel is not { } now)
        {
            if (!stopActive) _active = false;
            return;
        }
        if (!_active)
        {
            Reset();
            _active = true;
            _startFuel = _lastFuel = now;
            _lastTime = time;
            _request = Math.Max(0, request ?? 0);
            _target = _startFuel + _request;
            if (tankMax is > 0 and var max) _target = Math.Min(_target, Math.Max(_startFuel, max));
        }
        _repairFrom = Math.Max(_repairFrom, repairLeft ?? 0);
        _optRepairFrom = Math.Max(_optRepairFrom, optRepairLeft ?? 0);
        if (now > _lastFuel + Rise)
        {
            if (_riseFrom is null)
            {
                _riseFrom = _lastTime;
                _riseFuel = _lastFuel;
            }
            _lastRiseAt = time;
        }
        if (_riseFrom is { } from && _riseFuel is { } fromFuel && time - from >= MinRiseTime && now > fromFuel)
            Rate = (now - fromFuel) / (time - from);
        _lastFuel = now;
        _lastTime = time;

        double span = _target - _startFuel;
        bool stopped = _lastRiseAt is { } at && time - at >= Stalled;
        double left = stopped ? 0 : Math.Max(0, _target - now);
        LitresLeft = span > 0 ? left : 0;
        Progress = span > 0 ? Math.Clamp((now - _startFuel) / span, 0, 1) : null;
        if (stopped) Progress = span > 0 ? 1 : null;
        SecondsLeft = LitresLeft is 0 ? 0 : Rate is > 0 and var r ? left / r : null;
    }
}
