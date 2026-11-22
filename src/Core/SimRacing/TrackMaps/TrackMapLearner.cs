using System.Drawing;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.TrackMaps;

internal sealed class TrackMapLearner
{
    public const double MaxStepSeconds = 0.5;
    public const double MinLapSeconds = 10;
    public const double MaxDrift = 0.005;
    public const int MinSamples = 200;

    private readonly List<(double Pct, double X, double Y)> _lap = new();
    private (int? Id, string Name)? _track;
    private bool _recording, _clean;
    private double _x, _y, _prevPct = -1, _prevTime, _lapStart;
    private int? _incidents;

    public TrackMap? Feed(TelemetryFrame f)
    {
        if (!f.Driving || f.Simulated || f.LapDistPct is not { } raw || raw < -0.01 || f.SessionTime is not { } t
            || f.YawNorth is not { } heading || f.VelocityX is not { } forward || f.VelocityY is not { } left)
        {
            Stop();
            _prevPct = -1;
            return null;
        }
        double pct = Math.Max(0, raw);
        var track = (f.Session.TrackId, f.Session.TrackName);
        if (track != _track)
        {
            _track = track;
            Stop();
            _prevPct = -1;
        }
        double dt = t - _prevTime;
        if (_prevPct >= 0 && dt == 0) return null;
        TrackMap? learned = null;
        if (_prevPct >= 0 && dt > 0 && dt <= MaxStepSeconds)
        {
            _x += (forward * Math.Sin(heading) - left * Math.Cos(heading)) * dt;
            _y += (forward * Math.Cos(heading) + left * Math.Sin(heading)) * dt;
            bool crossed = _prevPct > 0.75 && pct < 0.25;
            if (_recording && (!crossed && pct < _prevPct - 0.001 || f.OnPitRoad || f.PlayerSurface == TrackSurface.OffTrack
                || f.Incidents is { } now && _incidents is { } before && now != before))
                _clean = false;
            if (crossed)
            {
                if (_recording && _clean && t - _lapStart > MinLapSeconds) learned = Build(f.Session.TrackId, f.Session.TrackName, _lap, out _);
                Start(t, f);
            }
            if (_recording) _lap.Add((pct, _x, _y));
        }
        else Stop();
        _prevPct = pct;
        _prevTime = t;
        return learned;
    }

    private void Start(double t, TelemetryFrame f)
    {
        _lap.Clear();
        _recording = true;
        _clean = !f.OnPitRoad;
        _lapStart = t;
        _incidents = f.Incidents;
    }

    private void Stop()
    {
        _recording = false;
        _lap.Clear();
    }

    internal static TrackMap? Build(int? trackId, string trackName, IReadOnlyList<(double Pct, double X, double Y)> lap, out string? problem)
    {
        problem = null;
        if (lap.Count < MinSamples)
        {
            problem = $"only {lap.Count} samples";
            return null;
        }
        var first = lap[0];
        var last = lap[^1];
        double span = last.Pct - first.Pct;
        if (first.Pct > 0.02 || last.Pct < 0.98 || span <= 0)
        {
            problem = "the lap isn't complete";
            return null;
        }
        double length = 0;
        for (int i = 1; i < lap.Count; i++) length += Math.Sqrt(Sq(lap[i].X - lap[i - 1].X) + Sq(lap[i].Y - lap[i - 1].Y));
        double ex = last.X - first.X, ey = last.Y - first.Y;
        double drift = Math.Max(0, Math.Sqrt(ex * ex + ey * ey) - length * (1 - span) / span);
        if (drift > length * MaxDrift)
        {
            problem = $"the lap doesn't close ({drift:0} m over {length:0} m)";
            return null;
        }
        var points = new List<PointF>(lap.Count);
        foreach (var s in lap)
        {
            double k = (s.Pct - first.Pct) / span;
            points.Add(new PointF((float)(s.X - ex * k), (float)-(s.Y - ey * k)));
        }
        var result = TrackMapImport.FromPath(TrackMapLibrary.LearnedKey(trackId, trackName), trackId, trackName, TrackMapSource.Learned, points, null);
        problem = result.Problem;
        return result.Map;
    }

    private static double Sq(double v) => v * v;
}
