using Core.SimRacing.TrackMaps;

namespace Core.SimRacing.Hud;

internal static class FlatMapShape
{
    public const double BendReachMetres = 75;
    public const double StepMetres = 5;
    public const int FewestSteps = 360, MostSteps = 4000;
    public const double LongestPit = 0.4;
    public const int MostPitPoints = 400;

    public static int Steps(double? trackKm) =>
        trackKm is > 0 and var km ? (int)Math.Clamp(Math.Round(km * 1000 / StepMetres), FewestSteps, MostSteps) : 1000;

    public static float[] Bends(TrackMap map, double? trackKm)
    {
        int n = Steps(trackKm);
        var heading = new double[n];
        for (int i = 0; i < n; i++)
        {
            var a = map.At((double)i / n);
            var b = map.At((double)(i + 1) / n);
            heading[i] = Math.Atan2(b.Y - a.Y, b.X - a.X);
        }
        var turn = new double[n];
        for (int i = 0; i < n; i++) turn[i] = Angle(heading[(i + 1) % n] - heading[i]);
        int reach = trackKm is > 0 and var km ? Math.Max(1, (int)Math.Round(BendReachMetres / (km * 1000) * n)) : Math.Max(1, n / 100);
        reach = Math.Min(reach, n / 4);
        var sum = new double[n + 1];
        for (int i = 0; i < n; i++) sum[i + 1] = sum[i] + turn[i];
        double total = sum[n];
        double Between(int from, int count)
        {
            from = ((from % n) + n) % n;
            int to = from + count;
            return to <= n ? sum[to] - sum[from] : total - sum[from] + sum[to - n];
        }
        var bends = new float[n];
        for (int i = 0; i < n; i++)
        {
            double swing = -Between(i - reach, reach * 2);
            bends[i] = (float)Math.Clamp(swing / Math.PI, -1, 1);
        }
        return bends;
    }

    public static float At(float[] bends, double pct)
    {
        int n = bends.Length;
        if (n == 0) return 0;
        double f = (pct - Math.Floor(pct)) * n;
        int i = (int)f;
        double t = f - i;
        return (float)(bends[i % n] * (1 - t) + bends[(i + 1) % n] * t);
    }

    public static double Angle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a <= -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static (double Start, double End)? PitSpan(IReadOnlyList<double> pcts)
    {
        int n = pcts.Count;
        if (n < 2) return null;
        var sorted = new double[n];
        for (int i = 0; i < n; i++) sorted[i] = pcts[i] - Math.Floor(pcts[i]);
        Array.Sort(sorted);
        double widest = sorted[0] + 1 - sorted[n - 1];
        int after = 0;
        for (int i = 1; i < n; i++)
            if (sorted[i] - sorted[i - 1] > widest) { widest = sorted[i] - sorted[i - 1]; after = i; }
        double start = sorted[after], end = sorted[(after - 1 + n) % n];
        double length = end - start;
        length -= Math.Floor(length);
        return length > 0 && length <= LongestPit ? (start, end) : null;
    }

    public static bool Inside(double pct, double start, double end)
    {
        pct -= Math.Floor(pct);
        return start <= end ? pct >= start && pct <= end : pct >= start || pct <= end;
    }

    public static (double Start, double End)? PitSpan(TrackMap map)
    {
        var points = map.Pit.SelectMany(s => s.Figures).SelectMany(fg => fg.Points).ToList();
        int stride = Math.Max(1, points.Count / MostPitPoints);
        var pcts = new List<double>();
        for (int i = 0; i < points.Count; i += stride) pcts.Add(map.PctOf(points[i]));
        return PitSpan(pcts);
    }
}
