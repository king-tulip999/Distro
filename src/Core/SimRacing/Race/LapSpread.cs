namespace Core.SimRacing.Race;

internal static class LapSpread
{
    private static readonly double[] NiceSteps = { 0.1, 0.2, 0.25, 0.5, 1, 2, 2.5, 5, 10, 20, 30, 60 };

    public static double Median(ReadOnlySpan<double> sorted)
    {
        if (sorted.Length == 0) return double.NaN;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public static double Percentile(ReadOnlySpan<double> sorted, double fraction)
    {
        if (sorted.Length == 0) return double.NaN;
        double at = Math.Clamp(fraction, 0, 1) * (sorted.Length - 1);
        int lo = (int)Math.Floor(at), hi = Math.Min(sorted.Length - 1, lo + 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo);
    }

    public static int RankOf(ReadOnlySpan<double> sorted, double value)
    {
        int rank = 1;
        for (int i = 0; i < sorted.Length; i++)
            if (sorted[i] < value - 1e-9) rank++;
        return rank;
    }

    public static int TopPercent(int rank, int count) =>
        count <= 0 || rank <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(100.0 * rank / count), 1, 100);

    public static double NiceStep(double range, int maxSteps)
    {
        maxSteps = Math.Max(1, maxSteps);
        foreach (double step in NiceSteps)
            if (range / step <= maxSteps + 1e-9) return step;
        return Math.Ceiling(range / maxSteps / 60) * 60;
    }

    public static double AxisSpan(ReadOnlySpan<double> sortedDeltas)
    {
        if (sortedDeltas.Length == 0) return 1;
        double reach = sortedDeltas.Length >= 6 ? Percentile(sortedDeltas, 0.85) : sortedDeltas[^1];
        if (!double.IsFinite(reach) || reach <= 0) return 1;
        double step = NiceStep(reach, 4);
        return Math.Max(0.5, Math.Ceiling(reach / step - 1e-9) * step);
    }

    public static (double Start, double Width, int Count) BucketRange(double fastest, double slowest, int maxBuckets)
    {
        maxBuckets = Math.Max(1, maxBuckets);
        if (!double.IsFinite(fastest) || !double.IsFinite(slowest) || slowest < fastest) return (fastest, 1, 1);
        double width = NiceStep(Math.Max(slowest - fastest, 1e-6), maxBuckets - 1);
        double start = Math.Floor(fastest / width + 1e-9) * width;
        int count = Math.Max(1, (int)Math.Floor((slowest - start) / width + 1e-9) + 1);
        while (count > maxBuckets)
        {
            width = NiceStep(width * 1.01 * maxBuckets, maxBuckets);
            start = Math.Floor(fastest / width + 1e-9) * width;
            count = Math.Max(1, (int)Math.Floor((slowest - start) / width + 1e-9) + 1);
        }
        return (start, width, count);
    }

    public static int BucketOf(double value, double start, double width, int count) =>
        width <= 0 || count <= 0 ? 0 : Math.Clamp((int)Math.Floor((value - start) / width + 1e-9), 0, count - 1);

    public static void Fill(ReadOnlySpan<double> values, double start, double width, Span<int> counts)
    {
        counts.Clear();
        for (int i = 0; i < values.Length; i++) counts[BucketOf(values[i], start, width, counts.Length)]++;
    }

    public static int Lanes(ReadOnlySpan<float> sortedX, float spacing, int maxLanes, Span<int> lanes, Span<float> laneEnds)
    {
        maxLanes = Math.Clamp(maxLanes, 1, laneEnds.Length);
        for (int k = 0; k < maxLanes; k++) laneEnds[k] = float.NegativeInfinity;
        int dropped = 0;
        for (int i = 0; i < sortedX.Length; i++)
        {
            lanes[i] = -1;
            for (int k = 0; k < maxLanes; k++)
                if (sortedX[i] - laneEnds[k] >= spacing - 1e-3f)
                {
                    lanes[i] = k;
                    laneEnds[k] = sortedX[i];
                    break;
                }
            if (lanes[i] < 0) dropped++;
        }
        return dropped;
    }

    public static int LaneOffset(int lane) => lane == 0 ? 0 : lane % 2 == 1 ? -(lane + 1) / 2 : lane / 2;
}
