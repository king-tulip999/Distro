namespace Core.SimRacing.Race;

internal sealed class DeltaTrace
{
    public const int Bins = 400;
    public const int RecentCap = 256;
    public const double RecentStep = 0.05;

    private float[] _current = new float[Bins], _previous = new float[Bins];
    private int _filled = -1, _previousFilled = -1, _lap = int.MinValue;
    private readonly double[] _recentT = new double[RecentCap];
    private readonly float[] _recentV = new float[RecentCap];
    private int _recentHead, _recentCount;

    public DeltaTrace()
    {
        Array.Fill(_current, float.NaN);
        Array.Fill(_previous, float.NaN);
    }

    public int Filled => _filled;

    public int PreviousFilled => _previousFilled;

    public float Current(int bin) => _current[bin];

    public float Previous(int bin) => _previous[bin];

    public int RecentCount => _recentCount;

    public static int BinOf(double pct) => Math.Clamp((int)(pct * Bins), 0, Bins - 1);

    public double? At(double pct)
    {
        int bin = BinOf(pct);
        for (int i = Math.Min(bin, _filled); i >= 0 && i >= bin - 3; i--)
            if (float.IsFinite(_current[i])) return _current[i];
        return null;
    }

    public (double Time, float Value) Recent(int i)
    {
        int at = (_recentHead - _recentCount + i + RecentCap * 2) % RecentCap;
        return (_recentT[at], _recentV[at]);
    }

    public void Clear()
    {
        Array.Fill(_current, float.NaN);
        Array.Fill(_previous, float.NaN);
        _filled = _previousFilled = -1;
        _lap = int.MinValue;
        _recentCount = 0;
    }

    public void Add(double now, int lap, double pct, double? delta)
    {
        if (!double.IsFinite(pct)) return;
        float value = delta is { } d && double.IsFinite(d) ? (float)d : float.NaN;
        if (lap != _lap)
        {
            if (lap == _lap + 1 && _filled >= Bins * 9 / 10)
            {
                (_previous, _current) = (_current, _previous);
                _previousFilled = _filled;
            }
            else if (_lap != int.MinValue)
            {
                Array.Fill(_previous, float.NaN);
                _previousFilled = -1;
            }
            Array.Fill(_current, float.NaN);
            _filled = -1;
            _lap = lap;
        }
        int bin = BinOf(pct);
        if (bin > _filled)
        {
            float from = _filled >= 0 ? _current[_filled] : float.NaN;
            int start = _filled + 1, span = bin - _filled;
            for (int i = start; i <= bin; i++)
                _current[i] = float.IsFinite(from) && float.IsFinite(value) && span <= Bins / 20
                    ? from + (value - from) * (i - _filled) / span
                    : value;
            _filled = bin;
        }
        else if (bin == _filled) _current[bin] = value;
        if (float.IsFinite(value) && (_recentCount == 0 || now - _recentT[(_recentHead - 1 + RecentCap) % RecentCap] >= RecentStep || now < _recentT[(_recentHead - 1 + RecentCap) % RecentCap]))
        {
            _recentT[_recentHead] = now;
            _recentV[_recentHead] = value;
            _recentHead = (_recentHead + 1) % RecentCap;
            if (_recentCount < RecentCap) _recentCount++;
        }
    }
}

internal sealed class DeltaHistory
{
    public DeltaTrace Best { get; } = new();
    public DeltaTrace Session { get; } = new();
    public DeltaTrace Optimal { get; } = new();
    public DeltaTrace LastLap { get; } = new();
    public DeltaTrace SessionOptimal { get; } = new();

    public DeltaTrace For(string reference) => reference switch
    {
        "session" => Session,
        "optimal" => Optimal,
        "lastlap" => LastLap,
        "sessionOptimal" => SessionOptimal,
        _ => Best,
    };

    public void Clear()
    {
        Best.Clear();
        Session.Clear();
        Optimal.Clear();
        LastLap.Clear();
        SessionOptimal.Clear();
    }

    public void Update(double now, Telemetry.TelemetryFrame f)
    {
        if (f.LapCompleted is not { } lap || f.LapDistPct is not { } pct || f.OnPitRoad) return;
        Best.Add(now, lap, pct, f.DeltaBestOk ? f.DeltaBest : null);
        Session.Add(now, lap, pct, f.DeltaSessionBestOk ? f.DeltaSessionBest : null);
        Optimal.Add(now, lap, pct, f.DeltaOptimalOk ? f.DeltaOptimal : null);
        LastLap.Add(now, lap, pct, f.RawValue("LapDeltaToSessionLastlLap_OK") is > 0 ? f.RawValue("LapDeltaToSessionLastlLap") : null);
        SessionOptimal.Add(now, lap, pct, f.RawValue("LapDeltaToSessionOptimalLap_OK") is > 0 ? f.RawValue("LapDeltaToSessionOptimalLap") : null);
    }
}
