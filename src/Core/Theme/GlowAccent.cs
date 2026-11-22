using System.Windows.Media;

namespace Core.Theme;

internal static class GlowAccent
{
    public const double MinGap = 24;
    public const double MovedGap = 36;
    public const double MaxShift = 40;
    public const double GreySaturation = 0.25;
    public const double MinContrast = 3;

    public static readonly (string Name, string Hex)[] Swatches =
    {
        ("Pink", "#DB156B"), ("Violet", "#8B4DF0"), ("Blue", "#3D7CFF"), ("Cyan", "#49C8F5"),
        ("Teal", "#27C1B1"), ("Mint", "#2EE2A0"), ("Lime", "#9BE22E"),
    };

    private static readonly string[] AccentKeys =
    {
        "Accent", "AccentHover", "AccentPressed", "AccentEdge", "AccentSoft", "AccentText", "Hover", "HoverRaised", "FocusEdge",
        "Meter1", "Meter2", "Meter3", "GraphText", "GraphSoft", "ScrollThumbBrushDrag", "BgGlow",
    };

    private static readonly string[] SlateKeys = { "Slate", "SlateSoft", "SlateDeep" };

    private sealed record Meaning(string Key, string[] Keys, bool Movable, string Label);

    private static readonly Meaning[] Meanings =
    {
        new("Sage", new[] { "Sage", "SageSoft", "SageDeep" }, true, "the green for gains and good results"),
        new("Slate", SlateKeys, true, "the blue for practice sessions"),
        new("Danger", new[] { "Danger", "DangerSoft", "DangerDeep", "Meter5" }, false, "the red for errors"),
        new("Coral", new[] { "Coral", "CoralSoft", "CoralDeep", "Plum", "PlumSoft", "PlumDeep" }, false, "the orange-red for losses"),
        new("Apricot", new[] { "Apricot", "ApricotSoft", "ApricotDeep" }, false, "the orange for warnings"),
        new("Warn", new[] { "Warn", "WarnSoft", "WarnDeep", "Gold", "GoldSoft", "GoldDeep", "GoldEdge", "Meter4" }, false, "the orange for warnings and podiums"),
    };

    public sealed record Result(IReadOnlyDictionary<string, Color>? Colors, string? Problem);

    private static string? Normal(string? hex) => CustomPalette.Parse(hex) is { } c ? Palettes.ToHex(c) : null;

    public static string? Stored(string? hex) => Normal(hex) is { } h && Swatches.Any(s => s.Hex == h) ? h : null;

    public static bool SameAs(string? a, string? b) => Normal(a) is { } x && string.Equals(x, Normal(b), StringComparison.OrdinalIgnoreCase);

    public static Result Apply(IReadOnlyDictionary<string, Color> basePalette, string? hex)
    {
        if (CustomPalette.Parse(hex) is not { } accent) return new(basePalette, null);
        var old = basePalette["Accent"];
        if (old == accent) return new(basePalette, null);
        if (ThemeManager.Contrast(accent, basePalette["Panel"]) < MinContrast)
            return new(null, "Too dark to see on the dark background. Pick a lighter color.");

        var (oldHue, oldSat, oldVal) = ToHsv(old);
        var (hue, sat, val) = ToHsv(accent);
        bool slateIsAccent = HueGap(ToHsv(basePalette["Slate"]).H, oldHue) < MinGap;
        var p = new Dictionary<string, Color>(basePalette);
        double turn = hue - oldHue, satScale = oldSat > 0 ? sat / oldSat : 1, valScale = oldVal > 0 ? val / oldVal : 1;
        foreach (var key in slateIsAccent ? AccentKeys.Concat(SlateKeys) : AccentKeys)
        {
            if (!p.TryGetValue(key, out var c)) continue;
            var (h, s, v) = ToHsv(c);
            p[key] = FromHsv(h + turn, s * satScale, v >= 0.5 ? v * valScale : v, c.A);
        }
        p["Accent"] = p["AccentEdge"] = accent;

        if (sat >= GreySaturation)
        {
            var placed = new List<(double Hue, Meaning Meaning)>();
            foreach (var meaning in Meanings)
            {
                if (meaning.Keys == SlateKeys && slateIsAccent) continue;
                if (!basePalette.TryGetValue(meaning.Key, out var color)) continue;
                double at = ToHsv(color).H, gap = HueGap(at, hue), wanted = meaning.Movable ? MovedGap : MinGap;
                if (gap >= wanted)
                {
                    placed.Add((at, meaning));
                    continue;
                }
                if (!meaning.Movable || wanted - gap > MaxShift) return new(null, $"Too close to {meaning.Label}. Pick a color further from it.");
                double away = Signed(at - hue) >= 0 ? 1 : -1, shift = (wanted - gap) * away;
                foreach (var key in meaning.Keys)
                    if (p.TryGetValue(key, out var c))
                    {
                        var (h, s, v) = ToHsv(c);
                        p[key] = FromHsv(h + shift, s, v, c.A);
                    }
                placed.Add((at + shift, meaning));
            }
            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                    if ((placed[i].Meaning.Movable || placed[j].Meaning.Movable) && HueGap(placed[i].Hue, placed[j].Hue) < MinGap)
                        return new(null, $"Too close to {(placed[i].Meaning.Movable ? placed[i] : placed[j]).Meaning.Label}. Pick a color further from it.");
        }

        var panel = p["Panel"];
        var grounds = p.TryGetValue("AccentSoft", out var soft) ? new[] { panel, soft } : new[] { panel };
        foreach (var key in new[] { "AccentText", "GraphText" })
            if (p.TryGetValue(key, out var text)) p[key] = Lift(text, grounds, 4.5);
        if (p.TryGetValue("FocusEdge", out var focus)) p["FocusEdge"] = Lift(focus, new[] { panel }, MinContrast);
        if (p.TryGetValue("OnAccent", out var on) && ThemeManager.Contrast(on, accent) < 4.5)
            p["OnAccent"] = ThemeManager.Contrast(Colors.White, accent) >= ThemeManager.Contrast(Colors.Black, accent)
                ? ThemeManager.Mix(accent, Colors.White, 0.96) : ThemeManager.Mix(accent, Colors.Black, 0.9);
        return new(p, null);
    }

    private static Color Lift(Color c, Color[] grounds, double target)
    {
        for (double t = 0; t <= 1.0001; t += 0.02)
        {
            var candidate = ThemeManager.Mix(c, Colors.White, t);
            if (grounds.All(g => ThemeManager.Contrast(candidate, g) >= target)) return candidate;
        }
        return Colors.White;
    }

    internal static double HueGap(double a, double b) => Math.Abs(Signed(a - b));

    private static double Signed(double degrees)
    {
        double d = ((degrees % 360) + 360) % 360;
        return d > 180 ? d - 360 : d;
    }

    internal static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d <= 0 ? 0 : max == r ? 60 * ((g - b) / d) : max == g ? 60 * (2 + (b - r) / d) : 60 * (4 + (r - g) / d);
        return ((h % 360 + 360) % 360, max <= 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v, byte alpha)
    {
        h = (h % 360 + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte B(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return Color.FromArgb(alpha, B(r + m), B(g + m), B(b + m));
    }
}
