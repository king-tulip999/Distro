using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal sealed class ErsLog
{
    public const int LapStarts = 6;
    public const string DrsVar = "DRS_Status", ManifoldVar = "ManifoldPress", DeployLapVar = "EnergyBatteryToMGU_KLap", BudgetLapVar = "EnergyBudgetBattToMGU_KLap";
    public const double BoostSeen = 1.2;
    private const double MaxStep = 0.5, Joules = 1e6;

    private readonly double[] _starts = new double[LapStarts];
    private int _startHead, _startCount;
    private int? _lap;
    private double _lastAt = double.NaN;
    private bool _wasActive;
    private double _activeSince;
    private double? _p2pFirst;
    private double _p2pFirstLaps;

    public double DeployMJ { get; private set; }
    public double RegenMJ { get; private set; }
    public double? LastDeployMJ { get; private set; }
    public double? LastRegenMJ { get; private set; }
    public double ManifoldMax { get; private set; }
    public double? Manifold { get; private set; }
    public int Uses { get; private set; }
    public double ActiveSeconds { get; private set; }
    public double? P2PRatePerLap { get; private set; }
    public bool P2PCountsDown { get; private set; } = true;
    public int MaxDeployMode { get; private set; }
    public double? P2PFirst => _p2pFirst;

    public bool HasBoost => ManifoldMax > BoostSeen && Manifold is not null;
    public bool HasP2P { get; private set; }
    public bool HasDrs { get; private set; }

    public int StartCount => _startCount;

    public double Start(int oldestFirst) => _starts[(_startHead - _startCount + oldestFirst + LapStarts * 2) % LapStarts];

    public double? SecondsPerUse => Uses > 0 ? ActiveSeconds / Uses : null;

    public double? NetPerLap
    {
        get
        {
            if (_startCount < 2) return null;
            return (Start(_startCount - 1) - Start(0)) / (_startCount - 1);
        }
    }

    public static double? Battery(TelemetryFrame f) => f.BatteryPct is { } b && double.IsFinite(b) ? Math.Clamp(b > 1.0001 ? b / 100 : b, 0, 1) : null;

    public static bool Reports(TelemetryFrame f, ErsLog log) =>
        f.BatteryPct is not null || f.MguKPower is not null || f.DeployMode is not null || log.HasP2P || log.HasDrs || log.HasBoost;

    public double? DeployThisLap(TelemetryFrame f) =>
        f.RawValue(DeployLapVar) is { } j && double.IsFinite(j) && j >= 0 ? j / Joules : f.MguKPower is not null ? DeployMJ : null;

    public static double? Budget(TelemetryFrame f) => f.RawValue(BudgetLapVar) is { } j && double.IsFinite(j) && j > 0 ? j / Joules : null;

    public double? P2PLapsLeft(TelemetryFrame f) =>
        HasP2P && f.PushToPassCount is { } n && P2PCountsDown && P2PRatePerLap is { } rate ? rate > 1e-6 ? Math.Max(0, n) / rate : double.PositiveInfinity : null;

    public void Clear()
    {
        _startHead = _startCount = 0;
        _lap = null;
        _lastAt = double.NaN;
        _wasActive = false;
        _p2pFirst = null;
        DeployMJ = RegenMJ = 0;
        LastDeployMJ = LastRegenMJ = null;
        ManifoldMax = 0;
        Manifold = null;
        Uses = 0;
        ActiveSeconds = 0;
        P2PRatePerLap = null;
        P2PCountsDown = true;
        MaxDeployMode = 0;
        HasP2P = HasDrs = false;
    }

    public void Update(TelemetryFrame f, double now)
    {
        double dt = double.IsNaN(_lastAt) ? 0 : now - _lastAt;
        _lastAt = now;
        bool step = dt > 0 && dt <= MaxStep;

        int? done = f.LapCompleted;
        if (done != _lap)
        {
            if (_lap is { } prev && done == prev + 1)
            {
                LastDeployMJ = f.MguKPower is not null ? DeployMJ : null;
                LastRegenMJ = f.MguKPower is not null ? RegenMJ : null;
            }
            DeployMJ = RegenMJ = 0;
            if (_lap is not null && Battery(f) is { } b)
            {
                _starts[_startHead] = b;
                _startHead = (_startHead + 1) % LapStarts;
                if (_startCount < LapStarts) _startCount++;
            }
            _lap = done;
        }

        if (f.MguKPower is { } w && double.IsFinite(w) && step)
        {
            double mj = w * dt / Joules;
            if (mj > 0) DeployMJ += mj; else RegenMJ -= mj;
        }

        Manifold = f.RawValue(ManifoldVar) is { } bar && double.IsFinite(bar) ? bar : null;
        if (Manifold is { } m && !f.OnPitRoad) ManifoldMax = Math.Max(ManifoldMax, m);

        if (f.DeployMode is { } mode && double.IsFinite(mode)) MaxDeployMode = Math.Max(MaxDeployMode, (int)Math.Round(mode));

        if (f.PushToPassCount is > 0 || f.PushToPassActive == true) HasP2P = true;
        if (f.DrsActive == true || f.RawValue(DrsVar) is > 0) HasDrs = true;
        bool active = f.PushToPassActive == true;
        if (active && !_wasActive) { Uses++; _activeSince = now; }
        if (active && step) ActiveSeconds += dt;
        _wasActive = active;

        if (HasP2P && f.PushToPassCount is { } count && double.IsFinite(count) && f.LapCompleted is { } laps && f.LapDistPct is { } pct)
        {
            double at = laps + Math.Clamp(pct, 0, 1);
            if (_p2pFirst is null || f.OnPitRoad && at - _p2pFirstLaps < 0.05)
            {
                _p2pFirst = count;
                _p2pFirstLaps = at;
            }
            double span = at - _p2pFirstLaps;
            double used = _p2pFirst.Value - count;
            P2PCountsDown = used >= 0;
            P2PRatePerLap = span >= 1 ? Math.Abs(used) / span : null;
        }
    }

    internal double ActiveFor(double now) => _wasActive ? Math.Max(0, now - _activeSince) : 0;
}
