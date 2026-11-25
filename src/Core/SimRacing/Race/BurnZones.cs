using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal readonly record struct LiftTip(int FirstPiece, int BrakePiece, double LiftPct, double Saves, bool Enough);

internal sealed class BurnZones
{
    public const int Pieces = 24, KeepLaps = 5, MaxGears = 10;
    public const double JumpPct = 0.05, RefuelLiters = 0.05, MaxStep = 0.5;
    public const double StraightThrottle = 0.85, StraightBrake = 0.05, BrakingZone = 0.15;

    private readonly double[] _fuel = new double[Pieces], _throttle = new double[Pieces], _brake = new double[Pieces];
    private readonly int[] _samples = new int[Pieces];
    private readonly double[,] _lapFuel = new double[KeepLaps, Pieces], _lapThrottle = new double[KeepLaps, Pieces], _lapBrake = new double[KeepLaps, Pieces];
    private readonly double[] _avgFuel = new double[Pieces], _avgThrottle = new double[Pieces], _avgBrake = new double[Pieces];
    private readonly double[] _gearTime = new double[MaxGears + 1], _gearFuel = new double[MaxGears + 1], _shiftRpm = new double[MaxGears + 1];
    private readonly int[] _shifts = new int[MaxGears + 1];
    private int _stored, _head;
    private bool _started, _clean;
    private double _lastPct = -1, _lastFuel = double.NaN, _lastNow = double.NaN, _lastRpm;
    private int _lastGear;

    public int CleanLaps { get; private set; }
    public double LapFuel { get; private set; }
    public int Gear { get; private set; }
    public int TopGear { get; private set; }
    public double GearTimeTotal { get; private set; }
    public double GearFuelTotal { get; private set; }
    public IReadOnlyList<double> Fuel => _avgFuel;
    public IReadOnlyList<double> Throttle => _avgThrottle;
    public IReadOnlyList<double> Brake => _avgBrake;

    public double GearTime(int gear) => (uint)gear <= MaxGears ? _gearTime[gear] : 0;

    public double GearFuel(int gear) => (uint)gear <= MaxGears ? _gearFuel[gear] : 0;

    public double? UpshiftRpm(int gear) => (uint)gear <= MaxGears && _shifts[gear] > 0 ? _shiftRpm[gear] / _shifts[gear] : null;

    public void Clear()
    {
        Array.Clear(_lapFuel);
        Array.Clear(_lapThrottle);
        Array.Clear(_lapBrake);
        Array.Clear(_avgFuel);
        Array.Clear(_avgThrottle);
        Array.Clear(_avgBrake);
        Array.Clear(_gearTime);
        Array.Clear(_gearFuel);
        Array.Clear(_shiftRpm);
        Array.Clear(_shifts);
        _stored = _head = 0;
        CleanLaps = 0;
        LapFuel = 0;
        Gear = TopGear = 0;
        GearTimeTotal = GearFuelTotal = 0;
        Break();
    }

    private void Break()
    {
        _started = false;
        _clean = false;
        _lastPct = -1;
        _lastFuel = double.NaN;
        _lastNow = double.NaN;
        _lastGear = 0;
        _lastRpm = 0;
        StartLap();
    }

    private void StartLap()
    {
        Array.Clear(_fuel);
        Array.Clear(_throttle);
        Array.Clear(_brake);
        Array.Clear(_samples);
    }

    public static int PieceOf(double pct) => Math.Clamp((int)((pct - Math.Floor(pct)) * Pieces), 0, Pieces - 1);

    public void Update(TelemetryFrame f, double now)
    {
        Gear = f.Gear ?? 0;
        if (!f.Driving || f.LapDistPct is not { } pct || pct < 0 || f.Fuel is not { } fuel)
        {
            Break();
            return;
        }
        double dt = double.IsNaN(_lastNow) ? 0 : now - _lastNow;
        bool gap = dt <= 0 || dt > MaxStep;
        bool dirty = f.OnPitRoad || f.TowTime is > 0 || gap;
        double drop = 0;
        if (!double.IsNaN(_lastFuel))
        {
            if (fuel > _lastFuel + RefuelLiters) dirty = true;
            else drop = Math.Max(0, _lastFuel - fuel);
        }
        bool crossing = _lastPct >= 0 && _lastPct > 0.9 && pct < 0.1;
        if (_lastPct >= 0 && !crossing && Math.Abs(pct - _lastPct) > JumpPct) dirty = true;
        if (dirty) _clean = false;

        if (_lastPct >= 0 && !gap) _fuel[PieceOf(_lastPct)] += drop;
        if (!gap && !f.OnPitRoad && f.TowTime is not > 0) Gears(f, dt, drop);

        if (crossing)
        {
            if (_started && _clean && Covered()) Push();
            StartLap();
            _started = true;
            _clean = !dirty;
        }

        int piece = PieceOf(pct);
        _throttle[piece] += Math.Clamp(f.Throttle ?? 0, 0, 1);
        _brake[piece] += Math.Clamp(f.Brake ?? 0, 0, 1);
        _samples[piece]++;

        _lastPct = pct;
        _lastFuel = fuel;
        _lastNow = now;
        _lastGear = f.Gear ?? 0;
        _lastRpm = f.Rpm ?? 0;
    }

    private void Gears(TelemetryFrame f, double dt, double drop)
    {
        int gear = f.Gear ?? 0;
        if (_lastGear is >= 1 and <= MaxGears && f.Speed is > 1)
        {
            _gearTime[_lastGear] += dt;
            _gearFuel[_lastGear] += drop;
            GearTimeTotal += dt;
            GearFuelTotal += drop;
            TopGear = Math.Max(TopGear, _lastGear);
        }
        if (_lastGear is >= 1 and < MaxGears && gear == _lastGear + 1 && _lastRpm > 0)
        {
            _shiftRpm[_lastGear] += _lastRpm;
            _shifts[_lastGear]++;
        }
    }

    private bool Covered()
    {
        for (int i = 0; i < Pieces; i++) if (_samples[i] == 0) return false;
        return true;
    }

    private void Push()
    {
        for (int i = 0; i < Pieces; i++)
        {
            _lapFuel[_head, i] = _fuel[i];
            _lapThrottle[_head, i] = _throttle[i] / _samples[i];
            _lapBrake[_head, i] = _brake[i] / _samples[i];
        }
        _head = (_head + 1) % KeepLaps;
        _stored = Math.Min(KeepLaps, _stored + 1);
        CleanLaps++;
        double total = 0;
        for (int i = 0; i < Pieces; i++)
        {
            double fuel = 0, throttle = 0, brake = 0;
            for (int k = 0; k < _stored; k++)
            {
                fuel += _lapFuel[k, i];
                throttle += _lapThrottle[k, i];
                brake += _lapBrake[k, i];
            }
            _avgFuel[i] = fuel / _stored;
            _avgThrottle[i] = throttle / _stored;
            _avgBrake[i] = brake / _stored;
            total += _avgFuel[i];
        }
        LapFuel = total;
    }

    public static double Between(IReadOnlyList<double> pieces, double from, double to)
    {
        int n = pieces.Count;
        if (n == 0 || !(to > from)) return 0;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double a = i / (double)n, b = (i + 1) / (double)n;
            double overlap = Math.Min(b, to) - Math.Max(a, from);
            if (overlap > 0) sum += pieces[i] * overlap * n;
        }
        return sum;
    }

    public const double HotShare = 0.75, EvenSpread = 0.02;

    public static bool Even(IReadOnlyList<double> pieces)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity, sum = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            lo = Math.Min(lo, pieces[i]);
            hi = Math.Max(hi, pieces[i]);
            sum += pieces[i];
        }
        return pieces.Count == 0 || !(sum > 0) || hi - lo <= EvenSpread * sum / pieces.Count;
    }

    public static (int First, int Length, double Fuel)? Hottest(IReadOnlyList<double> pieces)
    {
        int n = pieces.Count;
        if (n == 0 || Even(pieces)) return null;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            lo = Math.Min(lo, pieces[i]);
            hi = Math.Max(hi, pieces[i]);
        }
        double cut = lo + HotShare * (hi - lo);
        int start = -1;
        for (int i = 0; i < n; i++)
            if (pieces[i] < cut) { start = i; break; }
        if (start < 0) return null;
        int bestFirst = -1, bestLen = 0;
        double best = 0;
        int first = -1, len = 0;
        double sum = 0;
        for (int k = 1; k <= n; k++)
        {
            int i = (start + k) % n;
            if (pieces[i] >= cut)
            {
                if (len == 0) first = i;
                len++;
                sum += pieces[i];
                continue;
            }
            if (len > 0 && sum > best) { best = sum; bestFirst = first; bestLen = len; }
            len = 0;
            sum = 0;
        }
        return bestFirst < 0 ? null : (bestFirst, bestLen, best);
    }

    public static bool Straight(double throttle, double brake) => throttle >= StraightThrottle && brake < StraightBrake;

    public static LiftTip? Lift(IReadOnlyList<double> fuel, IReadOnlyList<double> throttle, IReadOnlyList<double> brake, double save)
    {
        int n = fuel.Count;
        if (!(save > 0) || n == 0 || throttle.Count != n || brake.Count != n) return null;
        double least = double.PositiveInfinity;
        for (int i = 0; i < n; i++) least = Math.Min(least, fuel[i]);
        int bestFirst = -1, bestBrake = -1, bestLen = 0;
        double bestFuel = 0;
        for (int b = 0; b < n; b++)
        {
            if (brake[b] < BrakingZone) continue;
            int len = 0;
            double runFuel = 0;
            for (int k = 1; k < n; k++)
            {
                int i = (b - k + n) % n;
                if (!Straight(throttle[i], brake[i])) break;
                len++;
                runFuel += fuel[i];
            }
            if (len == 0 || runFuel <= bestFuel) continue;
            bestFuel = runFuel;
            bestLen = len;
            bestBrake = b;
            bestFirst = (b - len + n) % n;
        }
        if (bestBrake < 0) return null;
        double perPct = (bestFuel / bestLen - least) * n;
        if (!(perPct > 0)) return null;
        double runPct = bestLen / (double)n;
        double lift = save / perPct;
        bool enough = lift <= runPct;
        if (!enough) lift = runPct;
        return new LiftTip(bestFirst, bestBrake, lift, lift * perPct, enough);
    }
}
