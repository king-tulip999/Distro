using System.Drawing;

namespace Core.SimRacing.Hud;

internal static class HudArrange
{
    public const string Prefix = "arrange.";

    public static bool IsKey(string key) => key.StartsWith(Prefix, StringComparison.Ordinal);

    public readonly record struct Link(string Option, string FirstId, string FirstValue, string OtherValue);

    private static readonly Dictionary<string, Link> Links = new(StringComparer.Ordinal)
    {
        [Prefix + "inputs.gear"] = new("gearSide", "dial", "left", "right"),
    };

    public static Link? LinkOf(string key) => Links.TryGetValue(key, out var link) ? link : null;

    public static string StoreKey(string key) => LinkOf(key)?.Option ?? key;

    public static string LinkedOrder(Link link, string value, IReadOnlyList<string> defaults)
    {
        bool first = value == link.FirstValue;
        return string.Join(",", defaults.Where(d => d == link.FirstId == first).Concat(defaults.Where(d => d == link.FirstId != first)));
    }

    public static List<string> Order(string? saved, IReadOnlyList<string> defaults)
    {
        var order = new List<string>(defaults.Count);
        if (!string.IsNullOrEmpty(saved))
            foreach (var raw in saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (defaults.Contains(raw) && !order.Contains(raw)) order.Add(raw);
        for (int i = 0; i < defaults.Count; i++)
        {
            if (order.Contains(defaults[i])) continue;
            int at = 0;
            for (int j = i - 1; j >= 0; j--)
                if (order.IndexOf(defaults[j]) is >= 0 and var previous) { at = previous + 1; break; }
            order.Insert(at, defaults[i]);
        }
        return order;
    }

    public static void Lay(RectangleF area, bool row, float gap, IReadOnlyList<int> order, ReadOnlySpan<float> sizes, Span<RectangleF> boxes)
    {
        int shown = 0, fills = 0;
        float fixedTotal = 0;
        foreach (int i in order)
        {
            if (sizes[i] < 0) continue;
            shown++;
            if (sizes[i] > 0) fixedTotal += sizes[i]; else fills++;
        }
        float room = (row ? area.Width : area.Height) - gap * Math.Max(0, shown - 1);
        float fill = fills > 0 ? Math.Max(0, room - fixedTotal) / fills : 0;
        float at = row ? area.X : area.Y;
        foreach (int i in order)
        {
            if (sizes[i] < 0)
            {
                boxes[i] = RectangleF.Empty;
                continue;
            }
            float size = sizes[i] > 0 ? sizes[i] : fill;
            float end = row ? area.Right : area.Bottom;
            size = Math.Max(0, Math.Min(size, end - at));
            boxes[i] = row ? new RectangleF(at, area.Y, size, area.Height) : new RectangleF(area.X, at, area.Width, size);
            at += size + gap;
        }
    }
}
