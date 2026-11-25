using Core.SessionStats;

namespace Core.SimRacing.Coach;

internal static class CoachSample
{
    private const double Top = 64, Grip = 17, Brake = 13, Push = 7;
    private static readonly (double Length, double Turn, double Radius)[] Track =
    {
        (520, 0, 0), (0, 90, 45), (240, 0, 0), (0, 90, 30), (180, 0, 0), (0, 30, 80), (30, 0, 0), (0, -30, 80), (200, 0, 0),
        (0, 90, 60), (260, 0, 0), (0, 45, 40), (25, 0, 0), (0, 45, 40), (300, 0, 0),
    };
    private static readonly (double Early, double Slow)[][] Habits =
    {
        new[] { (0.0, 0.0), (0.0, 0.0), (0.0, 0.0), (0.0, 0.0), (0.0, 0.0), (0.0, 0.0) },
        new[] { (1.5, 0.3), (0.0, 1.2), (0.5, 0.0), (0.0, 0.0), (1.0, 0.0), (0.0, 0.8) },
        new[] { (2.2, 0.0), (0.4, 1.6), (0.0, 0.6), (0.6, 0.0), (1.4, 0.4), (0.0, 1.1) },
        new[] { (1.8, 0.5), (0.0, 1.4), (0.8, 0.2), (0.3, 0.3), (1.2, 0.0), (0.2, 1.4) },
        new[] { (2.6, 0.2), (0.2, 0.9), (0.2, 0.4), (0.0, 0.6), (0.8, 0.5), (0.0, 0.9) },
        new[] { (1.0, 0.0), (0.6, 1.8), (0.4, 0.0), (0.4, 0.2), (1.6, 0.2), (0.1, 1.2) },
    };

    private static List<LapTrace>? _laps;

    public static IReadOnlyList<LapTrace> Laps => _laps ??= Habits.Select(Lap).OfType<LapTrace>().ToList();

    public static LapTrace You => Laps[3];

    private static LapTrace? Lap((double Early, double Slow)[] habit)
    {
        var path = new List<(double X, double Y, double Radius, int Corner)>();
        double x = 0, y = 0, heading = 0;
        int corner = -1;
        foreach (var (length, turn, radius) in Track)
        {
            if (turn == 0)
            {
                for (int i = 0; i < (int)length; i++) { path.Add((x, y, 0, -1)); x += Math.Cos(heading); y += Math.Sin(heading); }
                continue;
            }
            corner++;
            double arc = Math.Abs(turn) * Math.PI / 180 * radius;
            for (int i = 0; i < (int)arc; i++)
            {
                path.Add((x, y, radius, corner));
                heading += Math.Sign(turn) / radius;
                x += Math.Cos(heading);
                y += Math.Sin(heading);
            }
        }
        int n = path.Count;
        var cap = new double[n];
        for (int i = 0; i < n; i++)
            cap[i] = path[i].Radius > 0 ? Math.Sqrt(Grip * path[i].Radius) - habit[path[i].Corner % habit.Length].Slow : Top;
        var speed = (double[])cap.Clone();
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < n; i++) speed[i] = Math.Min(speed[i], Math.Sqrt(speed[i - 1] * speed[i - 1] + 2 * Push * (1 - speed[i - 1] / 80)));
            speed[0] = Math.Min(speed[0], Math.Sqrt(speed[n - 1] * speed[n - 1] + 2 * Push * (1 - speed[n - 1] / 80)));
            for (int i = n - 2; i >= 0; i--)
            {
                int next = NextCorner(path, i);
                double early = next >= 0 ? habit[next % habit.Length].Early : 0;
                speed[i] = Math.Min(speed[i], Math.Sqrt(speed[i + 1] * speed[i + 1] + 2 * (Brake - early)));
            }
        }
        var samples = new List<TraceBuilder.Sample>(n);
        double t = 0;
        for (int i = 0; i < n; i++)
        {
            double v = speed[i], ahead = speed[(i + 1) % n];
            bool braking = ahead < v - 0.05;
            bool full = !braking && (ahead > v + 0.01 || v >= Top - 0.1);
            double throttle = braking ? 0 : full ? 1 : 0.55, brake = braking ? Math.Clamp((v - ahead) * 4, 0.2, 0.95) : 0;
            double steer = path[i].Radius > 0 ? Math.Sign(Turn(path, i)) * 18 / path[i].Radius : 0;
            int gear = Math.Clamp((int)(v / 11) + 1, 1, 6);
            samples.Add(new TraceBuilder.Sample(i, (double)i / n, t, v, throttle, brake, null, steer, gear, 6000, 0, 0, 0, 0, path[i].X, path[i].Y));
            t += 1 / Math.Max(1, v);
        }
        samples.Add(samples[^1] with { Dist = n, Pct = 1, Time = t });
        return TraceBuilder.Build(samples, 0, t, 1);
    }

    private static double Turn(List<(double X, double Y, double Radius, int Corner)> path, int i)
    {
        int a = Math.Max(0, i - 1), b = Math.Min(path.Count - 1, i + 1);
        double h1 = Math.Atan2(path[i].Y - path[a].Y, path[i].X - path[a].X), h2 = Math.Atan2(path[b].Y - path[i].Y, path[b].X - path[i].X);
        return Math.IEEERemainder(h2 - h1, 2 * Math.PI);
    }

    private static int NextCorner(List<(double X, double Y, double Radius, int Corner)> path, int i)
    {
        for (int j = i; j < Math.Min(path.Count, i + 400); j++) if (path[j].Corner >= 0) return path[j].Corner;
        return -1;
    }
}
