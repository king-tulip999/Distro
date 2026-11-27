using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal enum DuelRole { Ahead, Behind }

internal enum DuelState { None, Ghost, Live, Done }

internal readonly record struct DuelValue(double Value, DuelState State)
{
    public static readonly DuelValue Empty = new(0, DuelState.None);
}

internal sealed class DuelSnapshot
{
    public const int MaxSectors = 16, HistoryLaps = 9;

    public int Car = -1;
    public bool HasGap;
    public double Gap;
    public int Sectors;
    public readonly DuelValue[] Sector = new DuelValue[MaxSectors];
    public readonly double[] LastSector = new double[MaxSectors];
    public bool HasLastSectors;
    public readonly DuelValue[] Piece = new DuelValue[DuelLog.Pieces];
    public readonly double[] LastPiece = new double[DuelLog.Pieces];
    public bool HasLastPieces;
    public double? LastLap;
    public double? Trend;
    public int TrendLaps;
    public int CurrentPiece;
    public double Progress;
    public readonly double[] HistoryX = new double[HistoryLaps + 1];
    public readonly double[] HistoryGap = new double[HistoryLaps + 1];
    public int HistoryCount;
    public bool OffTrack;
    public bool InPits;

    public double Closing(DuelRole role) => Trend is { } t ? role == DuelRole.Ahead ? -t : t : 0;

    public void Clear()
    {
        Car = -1;
        HasGap = false;
        Gap = 0;
        Sectors = 0;
        HasLastSectors = HasLastPieces = false;
        LastLap = null;
        Trend = null;
        TrendLaps = 0;
        CurrentPiece = 0;
        Progress = 0;
        HistoryCount = 0;
        OffTrack = InPits = false;
        Array.Fill(Sector, DuelValue.Empty);
        Array.Fill(Piece, DuelValue.Empty);
        Array.Fill(LastSector, double.NaN);
        Array.Fill(LastPiece, double.NaN);
    }
}

internal sealed class MySectors
{
    public int Count;
    public readonly DuelValue[] Sector = new DuelValue[DuelSnapshot.MaxSectors];
    public double? LastLap;
}

internal static class DuelSectors
{
    public static int[] Groups(IReadOnlyList<double> starts, int target)
    {
        int n = starts.Count;
        if (target <= 0 || n <= target) return Enumerable.Range(0, n).ToArray();
        var picks = new int[target];
        for (int j = 1; j < target; j++)
        {
            double want = j / (double)target;
            int best = picks[j - 1] + 1;
            for (int i = best + 1; i <= n - (target - j); i++)
                if (Math.Abs(starts[i] - want) < Math.Abs(starts[best] - want)) best = i;
            picks[j] = best;
        }
        return picks;
    }

    public static int Merge(DuelValue[] cells, int count, int[] groups)
    {
        if (groups.Length >= count) return count;
        for (int g = 0; g < groups.Length; g++)
        {
            int to = g + 1 < groups.Length ? groups[g + 1] : count;
            cells[g] = Combine(cells, groups[g], to);
        }
        for (int i = groups.Length; i < count; i++) cells[i] = DuelValue.Empty;
        return groups.Length;
    }

    public static void Merge(double[] values, int count, int[] groups)
    {
        if (groups.Length >= count) return;
        for (int g = 0; g < groups.Length; g++)
        {
            int to = g + 1 < groups.Length ? groups[g + 1] : count;
            double sum = 0;
            for (int i = groups[g]; i < to; i++) sum += values[i];
            values[g] = sum;
        }
        for (int i = groups.Length; i < count; i++) values[i] = double.NaN;
    }

    private static DuelValue Combine(DuelValue[] cells, int from, int to)
    {
        double driven = 0, ghost = 0;
        bool allDone = true, allGhost = true, started = false;
        for (int i = from; i < to; i++)
        {
            var c = cells[i];
            if (c.State != DuelState.Done) allDone = false;
            if (c.State != DuelState.Ghost) allGhost = false;
            if (c.State is DuelState.Done or DuelState.Live)
            {
                driven += c.Value;
                started = true;
            }
            else if (c.State == DuelState.Ghost) ghost += c.Value;
        }
        if (allDone) return new DuelValue(driven, DuelState.Done);
        if (started) return new DuelValue(driven, DuelState.Live);
        if (allGhost) return new DuelValue(ghost, DuelState.Ghost);
        return DuelValue.Empty;
    }
}

internal sealed class DuelLog
{
    public const int Pieces = 24, KeepLaps = 12;
    private const int MaxMarks = Pieces + DuelSnapshot.MaxSectors, Cars = TelemetryFrame.MaxCars;
    private const double OffTrackHold = 3;

    private double[] _marks = Array.Empty<double>();
    private int[] _sectorMarks = Array.Empty<int>();
    private readonly int[] _pieceMarks = new int[Pieces];
    private double[] _sectorStarts = Array.Empty<double>();
    private int _m = 1, _cap = 1;

    private readonly double[] _time = new double[Cars * KeepLaps * MaxMarks];
    private readonly long[] _stamp = new long[Cars * KeepLaps * MaxMarks];
    private readonly bool[] _lapPit = new bool[Cars * KeepLaps];
    private readonly long[] _lapPitStamp = new long[Cars * KeepLaps];
    private readonly double[] _progress = new double[Cars], _prevT = new double[Cars], _offAt = new double[Cars];
    private readonly long[] _last = new long[Cars];
    private readonly bool[] _seen = new bool[Cars], _pitNow = new bool[Cars];
    private double _lastTime = double.NaN;

    public DuelLog() => SetSectors(new[] { 0.0 });

    public int MarkCount => _m;

    public IReadOnlyList<double> SectorStarts => _sectorStarts;

    public void Clear()
    {
        Array.Fill(_stamp, -1);
        Array.Fill(_lapPitStamp, -1);
        Array.Clear(_seen);
        Array.Clear(_pitNow);
        Array.Fill(_offAt, double.NegativeInfinity);
        _lastTime = double.NaN;
    }

    public void SetSectors(IReadOnlyList<double> starts)
    {
        var s = starts.Where(x => x >= 0 && x < 1 && double.IsFinite(x)).Distinct().OrderBy(x => x).Take(DuelSnapshot.MaxSectors).ToList();
        if (s.Count == 0 || s[0] > 0.001) s.Insert(0, 0);
        if (s.Count > DuelSnapshot.MaxSectors) s.RemoveAt(s.Count - 1);
        if (s.SequenceEqual(_sectorStarts)) return;
        _sectorStarts = s.ToArray();
        var marks = new SortedSet<double>();
        for (int i = 0; i < Pieces; i++) marks.Add(Math.Round(i / (double)Pieces, 6));
        foreach (var x in s) marks.Add(Math.Round(x, 6));
        _marks = marks.ToArray();
        _m = _marks.Length;
        _cap = KeepLaps * _m;
        for (int i = 0; i < Pieces; i++) _pieceMarks[i] = Array.IndexOf(_marks, Math.Round(i / (double)Pieces, 6));
        _sectorMarks = s.Select(x => Array.IndexOf(_marks, Math.Round(x, 6))).ToArray();
        Clear();
    }

    private double At(long g) => Math.Floor((double)g / _m) + _marks[(int)(((g % _m) + _m) % _m)];

    private long LastAtOrBefore(double progress)
    {
        long lap = (long)Math.Floor(progress);
        double frac = progress - lap;
        int idx = 0;
        for (int i = 0; i < _m; i++) if (_marks[i] <= frac + 1e-9) idx = i;
        return lap * _m + idx;
    }

    private int Slot(int car, long g) => car * KeepLaps * MaxMarks + (int)(g % _cap);

    public double Time(int car, long g)
    {
        if ((uint)car >= Cars || g < 0) return double.NaN;
        int slot = Slot(car, g);
        return _stamp[slot] == g ? _time[slot] : double.NaN;
    }

    public long LastMark(int car) => (uint)car < Cars && _seen[car] ? _last[car] : -1;

    public bool OffTrackRecently(int car, double now) => (uint)car < Cars && now - _offAt[car] <= OffTrackHold;

    private bool PitLap(int car, long lap)
    {
        if (lap < 0) return false;
        int slot = car * KeepLaps + (int)(lap % KeepLaps);
        return _lapPitStamp[slot] == lap && _lapPit[slot];
    }

    public void Update(TelemetryFrame f)
    {
        if (f.SessionTime is not { } t || !double.IsFinite(t)) return;
        if (double.IsFinite(_lastTime) && t < _lastTime - 1) Clear();
        _lastTime = t;
        if (f.Session.Sectors.Count > 0)
        {
            Span<double> starts = stackalloc double[Math.Min(f.Session.Sectors.Count, DuelSnapshot.MaxSectors)];
            bool same = starts.Length == _sectorStarts.Length;
            for (int i = 0; i < starts.Length; i++)
            {
                starts[i] = f.Session.Sectors[i].StartPct;
                if (same && Math.Abs(starts[i] - _sectorStarts[i]) > 1e-9) same = false;
            }
            if (!same) SetSectors(starts.ToArray());
        }
        for (int car = 0; car < Cars; car++)
        {
            double pct = f.CarLapDistPct[car];
            if (pct < 0 || !double.IsFinite(pct) || f.CarSurface[car] == TrackSurface.NotInWorld)
            {
                _seen[car] = false;
                continue;
            }
            if (f.CarSurface[car] == TrackSurface.OffTrack) _offAt[car] = t;
            if (f.CarOnPitRoad[car]) _pitNow[car] = true;
            double raw = Math.Max(0, f.CarLapCompleted[car]) + Math.Min(pct, 0.999999);
            if (!_seen[car])
            {
                Start(car, raw, t);
                continue;
            }
            double prev = _progress[car];
            if (raw < prev - 0.5) raw += 1;
            else if (raw > prev + 0.5) raw -= 1;
            if (Math.Abs(raw - prev) > 0.5)
            {
                Start(car, raw, t);
                continue;
            }
            if (raw <= prev)
            {
                if (raw < prev - 0.02) Start(car, raw, t);
                continue;
            }
            double pt = _prevT[car];
            long g = _last[car] + 1;
            for (double q = At(g); q <= raw; q = At(++g))
            {
                double cross = t > pt ? pt + (t - pt) * (q - prev) / (raw - prev) : t;
                int slot = Slot(car, g);
                _time[slot] = cross;
                _stamp[slot] = g;
                if (g % _m == 0)
                {
                    long lap = g / _m - 1;
                    if (lap >= 0)
                    {
                        int ps = car * KeepLaps + (int)(lap % KeepLaps);
                        _lapPit[ps] = _pitNow[car];
                        _lapPitStamp[ps] = lap;
                    }
                    _pitNow[car] = f.CarOnPitRoad[car];
                }
                _last[car] = g;
            }
            _progress[car] = raw;
            _prevT[car] = t;
        }
    }

    private void Start(int car, double progress, double t)
    {
        _seen[car] = true;
        _progress[car] = progress;
        _prevT[car] = t;
        _last[car] = LastAtOrBefore(progress);
        _pitNow[car] = false;
    }

    public void Fill(DuelSnapshot s, int me, int car, bool sameLapCount, double now, bool onPitRoad)
    {
        s.Clear();
        s.Car = car;
        if ((uint)me >= Cars || (uint)car >= Cars) return;
        s.OffTrack = OffTrackRecently(car, now);
        s.InPits = onPitRoad;
        long lastMe = LastMark(me), lastThem = LastMark(car);
        if (lastMe < 0 || lastThem < 0) return;
        long shift = sameLapCount ? 0 : Shift(me, car, lastMe, lastThem);
        long kk = Math.Min(lastMe, lastThem - shift);
        long floor = Math.Max(0, kk - _m);
        while (kk >= floor && !double.IsFinite(Rel(me, car, shift, kk))) kk--;
        if (kk < floor) return;
        s.HasGap = true;
        s.Gap = Rel(me, car, shift, kk);
        long lap = kk / _m;
        int within = (int)(kk % _m);
        s.Progress = lap + _marks[within];
        int n = _sectorMarks.Length;
        s.Sectors = n;
        s.HasLastSectors = lap > 0;
        for (int i = 0; i < n; i++)
        {
            long a = lap * _m + _sectorMarks[i], b = i + 1 < n ? lap * _m + _sectorMarks[i + 1] : (lap + 1) * _m;
            s.Sector[i] = Cell(me, car, shift, kk, a, b, lap);
            double last = lap > 0 ? Rel(me, car, shift, b - _m) - Rel(me, car, shift, a - _m) : double.NaN;
            s.LastSector[i] = last;
            if (!double.IsFinite(last)) s.HasLastSectors = false;
        }
        s.HasLastPieces = lap > 0;
        for (int i = 0; i < Pieces; i++)
        {
            long a = lap * _m + _pieceMarks[i], b = i + 1 < Pieces ? lap * _m + _pieceMarks[i + 1] : (lap + 1) * _m;
            s.Piece[i] = Cell(me, car, shift, kk, a, b, lap);
            if (kk >= a) s.CurrentPiece = i;
            double last = lap > 0 ? Rel(me, car, shift, b - _m) - Rel(me, car, shift, a - _m) : double.NaN;
            s.LastPiece[i] = last;
            if (!double.IsFinite(last)) s.HasLastPieces = false;
        }
        double sum = 0;
        int used = 0;
        for (long l = lap; l >= 1 && l > lap - KeepLaps + 1; l--)
        {
            double d = Rel(me, car, shift, l * _m) - Rel(me, car, shift, (l - 1) * _m);
            if (!double.IsFinite(d)) break;
            s.LastLap ??= d;
            if (used < 2 && !PitLap(me, l - 1) && !PitLap(car, l - 1 + shift / _m))
            {
                sum += d;
                used++;
            }
        }
        if (used > 0)
        {
            s.Trend = sum / used;
            s.TrendLaps = used;
        }
        int h = 0;
        for (long l = Math.Max(0, lap - DuelSnapshot.HistoryLaps + 1); l <= lap && h < DuelSnapshot.HistoryLaps; l++)
        {
            double gap = Rel(me, car, shift, l * _m);
            if (!double.IsFinite(gap) || l * _m > kk) continue;
            s.HistoryX[h] = l;
            s.HistoryGap[h] = gap;
            h++;
        }
        if (s.Progress > (h > 0 ? s.HistoryX[h - 1] : -1) + 1e-6)
        {
            s.HistoryX[h] = s.Progress;
            s.HistoryGap[h] = s.Gap;
            h++;
        }
        s.HistoryCount = h;
    }

    private double Rel(int me, int car, long shift, long g) => Time(me, g) - Time(car, g + shift);

    private DuelValue Cell(int me, int car, long shift, long kk, long a, long b, long lap)
    {
        double ra = Rel(me, car, shift, a);
        if (kk >= b && Rel(me, car, shift, b) is var rb && double.IsFinite(rb) && double.IsFinite(ra)) return new DuelValue(rb - ra, DuelState.Done);
        if (kk > a && Rel(me, car, shift, kk) is var rk && double.IsFinite(rk) && double.IsFinite(ra)) return new DuelValue(rk - ra, DuelState.Live);
        if (lap > 0 && Rel(me, car, shift, b - _m) - Rel(me, car, shift, a - _m) is var last && double.IsFinite(last)) return new DuelValue(last, DuelState.Ghost);
        return DuelValue.Empty;
    }

    private long Shift(int me, int car, long lastMe, long lastThem)
    {
        long best = 0;
        double bestGap = double.PositiveInfinity;
        for (long laps = -2; laps <= 2; laps++)
        {
            long shift = laps * _m;
            long k = Math.Min(lastMe, lastThem - shift);
            double gap = Time(me, k) - Time(car, k + shift);
            if (double.IsFinite(gap) && Math.Abs(gap) < bestGap)
            {
                bestGap = Math.Abs(gap);
                best = shift;
            }
        }
        return best;
    }

    private static bool Racer(TelemetryFrame f, int car) => f.Session.ByCar.TryGetValue(car, out var d) && d.Races;

    public static (int Ahead, int Behind) Rivals(TelemetryFrame f)
    {
        int me = f.PlayerCarIdx;
        if ((uint)me >= Cars) return (-1, -1);
        int myClass = FieldOrder.ClassOf(f, me);
        int ahead = -1, behind = -1;
        if (f.SessionKind == SessionKind.Race && f.CarClassPosition[me] > 0)
        {
            int pos = f.CarClassPosition[me];
            for (int car = 0; car < Cars; car++)
            {
                if (car == me || !Racer(f, car) || FieldOrder.ClassOf(f, car) != myClass) continue;
                if (f.CarClassPosition[car] == pos - 1) ahead = car;
                else if (f.CarClassPosition[car] == pos + 1) behind = car;
            }
            return (ahead, behind);
        }
        double mine = f.CarLapDistPct[me];
        if (mine < 0) return (-1, -1);
        double nearestAhead = 1, nearestBehind = -1;
        for (int car = 0; car < Cars; car++)
        {
            if (car == me || !Racer(f, car) || FieldOrder.ClassOf(f, car) != myClass) continue;
            if (f.CarLapDistPct[car] < 0 || f.CarOnPitRoad[car] || f.CarSurface[car] == TrackSurface.NotInWorld) continue;
            double w = FieldOrder.Wrap(f.CarLapDistPct[car], mine);
            if (w > 0 && w < nearestAhead) { nearestAhead = w; ahead = car; }
            else if (w < 0 && w > nearestBehind) { nearestBehind = w; behind = car; }
        }
        return (ahead, behind);
    }

    public void FillMine(MySectors s, int me, double now)
    {
        s.Count = _sectorMarks.Length;
        s.LastLap = null;
        long last = LastMark(me);
        for (int i = 0; i < s.Count; i++) s.Sector[i] = DuelValue.Empty;
        if (last < 0) return;
        long lap = last / _m;
        int n = _sectorMarks.Length;
        for (int i = 0; i < n; i++)
        {
            long a = lap * _m + _sectorMarks[i], b = i + 1 < n ? lap * _m + _sectorMarks[i + 1] : (lap + 1) * _m;
            double ta = Time(me, a), tb = Time(me, b);
            if (last >= b && double.IsFinite(ta) && double.IsFinite(tb)) s.Sector[i] = new DuelValue(tb - ta, DuelState.Done);
            else if (last >= a && double.IsFinite(ta)) s.Sector[i] = new DuelValue(Math.Max(0, now - ta), DuelState.Live);
            else if (lap > 0 && Time(me, b - _m) - Time(me, a - _m) is var g && double.IsFinite(g)) s.Sector[i] = new DuelValue(g, DuelState.Ghost);
        }
        if (lap > 0 && Time(me, lap * _m) - Time(me, (lap - 1) * _m) is var l && double.IsFinite(l)) s.LastLap = l;
    }
}
