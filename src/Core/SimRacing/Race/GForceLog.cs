using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal sealed class GLap
{
    public const int Pieces = 24;

    public double Brake, Accel, Left, Right, Combined;
    public double CombinedPct = -1;
    public bool Any;
    public readonly double[] Lateral = new double[Pieces];
    public readonly bool[] Seen = new bool[Pieces];

    public double Corner => Math.Max(Left, Right);

    public int SeenCount
    {
        get
        {
            int n = 0;
            foreach (bool s in Seen) if (s) n++;
            return n;
        }
    }

    public void Reset()
    {
        Brake = Accel = Left = Right = Combined = 0;
        CombinedPct = -1;
        Any = false;
        Array.Clear(Lateral);
        Array.Clear(Seen);
    }

    public void CopyFrom(GLap o)
    {
        Brake = o.Brake; Accel = o.Accel; Left = o.Left; Right = o.Right; Combined = o.Combined;
        CombinedPct = o.CombinedPct;
        Any = o.Any;
        Array.Copy(o.Lateral, Lateral, Pieces);
        Array.Copy(o.Seen, Seen, Pieces);
    }

    public void Add(double lat, double lon, double? pct)
    {
        Any = true;
        Brake = Math.Max(Brake, -lon);
        Accel = Math.Max(Accel, lon);
        Left = Math.Max(Left, lat);
        Right = Math.Max(Right, -lat);
        double c = Math.Sqrt(lat * lat + lon * lon);
        if (c > Combined)
        {
            Combined = c;
            CombinedPct = pct ?? -1;
        }
        if (pct is not { } at || !double.IsFinite(at)) return;
        int piece = Piece(at);
        Seen[piece] = true;
        Lateral[piece] = Math.Max(Lateral[piece], Math.Abs(lat));
    }

    public static int Piece(double pct) => Math.Clamp((int)((pct - Math.Floor(pct)) * Pieces), 0, Pieces - 1);

    public static double PieceMiddle(int piece) => (piece + 0.5) / Pieces;
}

internal readonly record struct GSoftest(int Piece, double Deficit, bool FromLastLap);

internal sealed class GForceLog
{
    public const double Gravity = 9.80665, GlitchG = 6, TrailSeconds = 1.5, TraceSeconds = 10, TraceStep = 0.05, MovingSpeed = 3;
    public const int TrailCapacity = 256, TraceCapacity = 256;
    private const double LapTimeMatch = 0.0005, LapTimeWait = 5, CornerG = 0.5, SoftestMin = 0.02;

    private readonly double[] _trailT = new double[TrailCapacity], _trailLat = new double[TrailCapacity], _trailLong = new double[TrailCapacity];
    private readonly double[] _traceT = new double[TraceCapacity], _traceLat = new double[TraceCapacity], _traceLong = new double[TraceCapacity];
    private int _trailHead, _trailCount, _traceHead, _traceCount;
    private double _lastTraceAt = double.NegativeInfinity;

    public GLap Lap { get; } = new();
    public GLap Last { get; } = new();
    public GLap Best { get; } = new();
    public bool HasLast { get; private set; }
    public bool HasBest { get; private set; }
    public double? LatG { get; private set; }
    public double? LongG { get; private set; }
    public double SessionPeak { get; private set; }

    private int? _lap;
    private bool _pitThisLap, _fullLap, _pending;
    private double _pendingAt;
    private double? _lapLastSeen, _pendingLapLastBefore;

    public void Clear()
    {
        _trailHead = _trailCount = _traceHead = _traceCount = 0;
        _lastTraceAt = double.NegativeInfinity;
        Lap.Reset();
        Last.Reset();
        Best.Reset();
        HasLast = HasBest = false;
        LatG = LongG = null;
        SessionPeak = 0;
        _lap = null;
        _pitThisLap = _fullLap = _pending = false;
        _lapLastSeen = _pendingLapLastBefore = null;
    }

    public void Update(TelemetryFrame f, double now)
    {
        TrackLap(f, now);
        _lapLastSeen = f.LapLast;
        if (f.LatAccel is not { } latRaw || f.LongAccel is not { } longRaw || !double.IsFinite(latRaw) || !double.IsFinite(longRaw))
        {
            LatG = LongG = null;
            return;
        }
        double lat = latRaw / Gravity, lon = longRaw / Gravity;
        if (Math.Abs(lat) > GlitchG || Math.Abs(lon) > GlitchG) return;
        LatG = lat;
        LongG = lon;
        if (_trailCount > 0 && now < _trailT[(_trailHead - 1 + TrailCapacity) % TrailCapacity]) _trailHead = _trailCount = _traceHead = _traceCount = 0;
        Push(_trailT, _trailLat, _trailLong, ref _trailHead, ref _trailCount, TrailCapacity, now, lat, lon);
        if (now < _lastTraceAt) _lastTraceAt = double.NegativeInfinity;
        if (now - _lastTraceAt >= TraceStep)
        {
            _lastTraceAt = now;
            Push(_traceT, _traceLat, _traceLong, ref _traceHead, ref _traceCount, TraceCapacity, now, lat, lon);
        }
        if (f.OnPitRoad || f.IsReplayPlaying || (f.Speed ?? 0) < MovingSpeed) return;
        Lap.Add(lat, lon, f.LapDistPct);
        SessionPeak = Math.Max(SessionPeak, Math.Sqrt(lat * lat + lon * lon));
    }

    private void TrackLap(TelemetryFrame f, double now)
    {
        int? done = f.LapCompleted;
        if (done != _lap)
        {
            if (_lap is { } prev && done == prev + 1) FinishLap(now);
            else _pending = false;
            _lap = done;
            Lap.Reset();
            _pitThisLap = f.OnPitRoad;
            _fullLap = f.LapDistPct is { } at && (at < 0.1 || at > 0.95);
        }
        if (f.OnPitRoad) _pitThisLap = true;
        if (!_pending) return;
        if (!Nullable.Equals(f.LapLast, _pendingLapLastBefore) || now - _pendingAt >= LapTimeWait)
        {
            _pending = false;
            if (f.LapLast is > 0 and var last && f.LapBest is > 0 and var best && Math.Abs(last - best) < LapTimeMatch)
            {
                Best.CopyFrom(Last);
                HasBest = true;
            }
        }
    }

    private void FinishLap(double now)
    {
        Last.CopyFrom(Lap);
        HasLast = Lap.Any;
        _pending = HasLast && _fullLap && !_pitThisLap;
        _pendingAt = now;
        _pendingLapLastBefore = _lapLastSeen;
    }

    private static void Push(double[] t, double[] lat, double[] lon, ref int head, ref int count, int capacity, double now, double la, double lo)
    {
        t[head] = now;
        lat[head] = la;
        lon[head] = lo;
        head = (head + 1) % capacity;
        if (count < capacity) count++;
    }

    public int Trail(double now, double[] lat, double[] lon, double[] age) =>
        Copy(_trailT, _trailLat, _trailLong, _trailHead, _trailCount, TrailCapacity, now, TrailSeconds, lat, lon, age);

    public int Trace(double now, double[] lat, double[] lon, double[] age) =>
        Copy(_traceT, _traceLat, _traceLong, _traceHead, _traceCount, TraceCapacity, now, TraceSeconds, lat, lon, age);

    private static int Copy(double[] t, double[] la, double[] lo, int head, int count, int capacity, double now, double window, double[] lat, double[] lon, double[] age)
    {
        int n = 0, room = Math.Min(lat.Length, Math.Min(lon.Length, age.Length));
        int first = count;
        for (int k = 0; k < count; k++)
        {
            int i = (head - count + k + capacity) % capacity;
            if (now - t[i] <= window + 1e-9) { first = k; break; }
        }
        for (int k = Math.Max(first, count - room); k < count; k++)
        {
            int i = (head - count + k + capacity) % capacity;
            lat[n] = la[i];
            lon[n] = lo[i];
            age[n] = Math.Max(0, now - t[i]);
            n++;
        }
        return n;
    }

    public GSoftest? Softest(double? pct)
    {
        bool fromLast = Lap.SeenCount < 3 && HasLast;
        int skip = !fromLast && pct is { } at && double.IsFinite(at) ? GLap.Piece(at) : -1;
        return HasBest ? Softest(fromLast ? Last : Lap, Best, fromLast, skip) : null;
    }

    internal static GSoftest? Softest(GLap lap, GLap best, bool fromLast, int skip = -1)
    {
        int at = -1;
        double most = SoftestMin;
        for (int i = 0; i < GLap.Pieces; i++)
        {
            if (i == skip || !lap.Seen[i] || !best.Seen[i] || best.Lateral[i] < CornerG) continue;
            double deficit = best.Lateral[i] - lap.Lateral[i];
            if (deficit > most) { most = deficit; at = i; }
        }
        return at < 0 ? null : new GSoftest(at, most, fromLast);
    }
}
