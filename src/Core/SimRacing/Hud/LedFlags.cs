namespace Core.SimRacing.Hud;

internal sealed record LedFlagKind(LedFlag Flag, string Key, string Name, IReadOnlyList<HudChoice> Variants);

internal static class LedFlags
{
    public static readonly IReadOnlyList<LedFlagKind> All = new LedFlagKind[]
    {
        new(LedFlag.Green, "flagGreen", "Green", new HudChoice[]
        {
            new("arrows", "Go arrows", "Chevrons run upwards through the green"),
            new("sparkle", "Sparkle", "Dots twinkle across the green"),
            new("waving", "Waving", "Light and shade roll across, like a flag being waved"),
        }),
        new(LedFlag.Yellow, "flagYellow", "Yellow / debris", new HudChoice[]
        {
            new("sign", "Hazard sign", "A dark warning triangle with “!” flashes on the yellow"),
            new("stripes", "Hazard stripes", "Diagonal stripes slide across"),
            new("lamps", "Marshal lamps", "Two round lamps flash in turn"),
        }),
        new(LedFlag.SafetyCar, "flagSafetyCar", "Safety car", new HudChoice[]
        {
            new("wigwag", "Wig-wag", "White “SC”; the yellow behind flashes left and right in turn"),
            new("flash", "SC flash", "White “SC”; the yellow behind flashes"),
            new("chase", "SC and chase", "White “SC”, lights running round the edge"),
        }),
        new(LedFlag.Blue, "flagBlue", "Blue", new HudChoice[]
        {
            new("headlights", "Headlights", "The board flashes white twice, like headlights behind you"),
            new("stripe", "Stripe flash", "The yellow stripe flashes; the blue stays lit"),
            new("chase", "Edge chase", "Steady flag, yellow lights running round the edge"),
        }),
        new(LedFlag.White, "flagWhite", "White", new HudChoice[]
        {
            new("sparkle", "Sparkle", "Dots twinkle across the board"),
            new("waving", "Waving", "Light and shade roll across, like a flag being waved"),
            new("breathe", "Breathe", "Slowly brightens and dims"),
        }),
        new(LedFlag.Checkered, "flagCheckered", "Checkered", new HudChoice[]
        {
            new("marching", "Marching", "The squares swap every beat"),
            new("wave", "Wave", "The flag ripples as if it's being waved"),
            new("fireworks", "Fireworks", "Bursts go off over a dimmed checkered flag"),
        }),
        new(LedFlag.Black, "flagBlack", "Black", new HudChoice[]
        {
            new("cross", "Cross", "A steady white frame with a flashing X"),
            new("double", "Double frame", "Two white frames flash in turn"),
            new("stripes", "Striped border", "Black and white stripes run round the border"),
        }),
        new(LedFlag.Repair, "flagRepair", "Repair", new HudChoice[]
        {
            new("flash", "Disc and frame flash", "An orange disc and an orange frame flash in turn"),
            new("sparks", "Sparks", "Sparks fly round a steady orange disc"),
            new("ring", "Disc and ring", "The orange disc swaps with a ring around it"),
        }),
        new(LedFlag.Red, "flagRed", "Red", new HudChoice[]
        {
            new("strobe", "Strobe", "Hard flash, twice a second"),
            new("cross", "Cross", "A red frame with a flashing X"),
            new("wigwag", "Wig-wag", "Left and right halves flash fast in turn"),
        }),
    };

    public static LedFlagKind? Of(LedFlag flag)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Flag == flag) return All[i];
        return null;
    }

    private const int S = LedBoard.Size;
    private const double N = 8, C = (N - 1) / 2, E0 = 0.35, E1 = N - 1 - E0;

    private static readonly bool[] ScLetters = BuildSc();

    private static bool[] BuildSc()
    {
        var dots = new LedDot[S * S];
        LedBoard.Digits(dots, "SC", false);
        var lit = new bool[S * S];
        for (int i = 0; i < lit.Length; i++) lit[i] = dots[i] == LedDot.White;
        return lit;
    }

    public static void Draw(LedDot[] dots, float[] levels, LedFlag flag, string variant, double t)
    {
        for (int fy = 0; fy < S; fy++)
            for (int fx = 0; fx < S; fx++)
            {
                double x = (fx + 0.5) * N / S - 0.5, y = (fy + 0.5) * N / S - 0.5;
                var (c, k) = Dot(flag, variant, fx, fy, x, y, t);
                int i = fy * S + fx;
                dots[i] = c;
                levels[i] = (float)Math.Clamp(k, 0, 1);
            }
    }

    private static (LedDot, double) Dot(LedFlag flag, string v, int fx, int fy, double x, double y, double t) => flag switch
    {
        LedFlag.Green => v switch
        {
            "sparkle" => (LedDot.Green, Sparkle(x, y, t)),
            "waving" => (LedDot.Green, Waving(x, y, t)),
            _ => (LedDot.Green, Mod((N - 1 - y) + Math.Abs(x - C) * 0.9 - t * 6, 4) < 1.3 ? 1 : 0.3),
        },
        LedFlag.Yellow => v switch
        {
            "stripes" => (LedDot.Yellow, (x + y + Math.Floor(t * 8)) % 4 < 2 ? 1 : 0.18),
            "lamps" => Lamps(x, y, t),
            _ => Warning(x, y) && Even(t, 0.4) ? (LedDot.Off, 0) : (LedDot.Yellow, 1),
        },
        LedFlag.SafetyCar => Sc(fx, fy) ? (LedDot.White, 1) : v switch
        {
            "flash" => (LedDot.Yellow, Even(t, 0.25) ? 1 : 0.15),
            "chase" => Edge(x, y) ? (LedDot.Yellow, RunLights(x, y, t) ? 1 : 0.15) : (LedDot.Yellow, 0.55),
            _ => (LedDot.Yellow, (x < C) == Even(t, 0.3) ? 1 : 0.15),
        },
        LedFlag.Blue => v switch
        {
            "stripe" => Stripe(x, y) ? (LedDot.Yellow, Even(t, 0.3) ? 1 : 0.12) : (LedDot.Blue, 1),
            "chase" => Edge(x, y) ? (LedDot.Yellow, RunLights(x, y, t) ? 1 : 0.15) : BlueFlag(x, y),
            _ => Headlights(t) ? (LedDot.White, 1) : BlueFlag(x, y),
        },
        LedFlag.White => v switch
        {
            "waving" => (LedDot.White, Waving(x, y, t)),
            "breathe" => (LedDot.White, 0.3 + 0.7 * Ease(Tri(t / 1.6))),
            _ => (LedDot.White, Sparkle(x, y, t)),
        },
        LedFlag.Checkered => v switch
        {
            "wave" => (LedDot.White, (Cell(fx) + Cell(fy, (int)Math.Floor(Math.Sin(x * 0.8 - t * 6) * 3 + 0.5))) % 2 == 0 ? 1 : 0.08),
            "fireworks" => (LedDot.White, Firework(fx, fy, x, y, t) ? 1 : Checks(fx, fy) ? 0.45 : 0),
            _ => (LedDot.White, (Cell(fx) + Cell(fy) + (long)Math.Floor(t / 0.3)) % 2 == 0 ? 1 : 0),
        },
        LedFlag.Black => v switch
        {
            "double" => DoubleFrame(x, y, t),
            "stripes" => Edge(x, y) && Mod(x + y - t * 6, 2) < 1 ? (LedDot.White, 1) : (LedDot.Off, 0),
            _ => FrameAndCross(x, y, t, LedDot.White, 0.4),
        },
        LedFlag.Repair => v switch
        {
            "sparks" => Disc(x, y) ? (LedDot.Orange, 1) : Math.Sqrt((x - C) * (x - C) + (y - C) * (y - C)) < 4.2 && Hash(fx, fy, Math.Floor(t * 12)) > 0.93 ? (LedDot.Orange, 1) : (LedDot.Off, 0),
            "ring" => DiscAndRing(x, y, t),
            _ => Even(t, 0.4) ? (Disc(x, y) ? (LedDot.Orange, 1) : (LedDot.Off, 0)) : (Edge(x, y) ? (LedDot.Orange, 1) : (LedDot.Off, 0)),
        },
        LedFlag.Red => v switch
        {
            "cross" => FrameAndCross(x, y, t, LedDot.Red, 0.3),
            "wigwag" => (LedDot.Red, (x < C) == Even(t, 0.2) ? 1 : 0.12),
            _ => (LedDot.Red, t % 0.5 < 0.25 ? 1 : 0.1),
        },
        _ => (LedDot.Off, 0),
    };

    private static (LedDot, double) BlueFlag(double x, double y) => (Stripe(x, y) ? LedDot.Yellow : LedDot.Blue, 1);

    private static bool Headlights(double t)
    {
        double p = t % 1.4;
        return p < 0.1 || (p > 0.2 && p < 0.3);
    }

    private static (LedDot, double) Lamps(double x, double y, double t)
    {
        bool left = Distance(x - 1.9, y - C) < 1.7, right = Distance(x - 5.1, y - C) < 1.7, on = Even(t, 0.35);
        if ((left && on) || (right && !on)) return (LedDot.Yellow, 1);
        return left || right ? (LedDot.Yellow, 0.15) : (LedDot.Off, 0);
    }

    private static (LedDot, double) FrameAndCross(double x, double y, double t, LedDot c, double period)
    {
        if (Edge(x, y)) return (c, 1);
        if (OnLine(x, y) || OnLine(x, N - 1 - y)) return (c, Even(t, period) ? 1 : 0.1);
        return (LedDot.Off, 0);
    }

    private static (LedDot, double) DoubleFrame(double x, double y, double t)
    {
        double r = Math.Max(Math.Abs(x - C), Math.Abs(y - C)) + 0.5;
        bool outer = Edge(x, y), inner = r > 2.3 && r < 3.0, on = Even(t, 0.35);
        return (outer && on) || (inner && !on) ? (LedDot.White, 1) : (LedDot.Off, 0);
    }

    private static (LedDot, double) DiscAndRing(double x, double y, double t)
    {
        double d = Distance(x - C, y - C);
        bool lit = Even(t, 0.4) ? d < 2.6 : Math.Abs(d - 2.9) < 0.55;
        return lit ? (LedDot.Orange, 1) : (LedDot.Off, 0);
    }

    private static bool Firework(int fx, int fy, double x, double y, double t)
    {
        double k = Math.Floor(t * 2.5), phase = t * 2.5 - k;
        double bx = 1 + Hash(k, 1, 2) * 5, by = 1 + Hash(k, 3, 4) * 5;
        return Math.Abs(Distance(x - bx, y - by) - phase * 3.5) < 0.45 && Hash(fx, fy, k) > 0.35;
    }

    private static bool Warning(double x, double y) =>
        (InTriangle(x, y, 0) && !InTriangle(x, y, 0.95)) || (Math.Abs(x - C) < 0.38 && y > 2.6 && y < 4.7) || Distance(x - C, y - 5.6) < 0.42;

    private static bool InTriangle(double x, double y, double inset) =>
        y >= 0.4 + inset * 1.8 && y <= 6.7 - inset && Math.Abs(x - C) <= (y - 0.4) / 6.3 * 3.4 - inset;

    private static bool Sc(int fx, int fy) => ScLetters[fy * S + fx];

    private static double Sparkle(double x, double y, double t) => Hash(x, y, Math.Floor(t * 10)) > 0.82 ? 1 : 0.55;

    private static double Waving(double x, double y, double t) => 0.5 + 0.5 * (0.5 + 0.5 * Math.Sin(x * 0.9 + y * 0.35 - t * 5));

    private static bool Edge(double x, double y) => x < E0 || y < E0 || x > E1 || y > E1;

    private static double Around(double x, double y)
    {
        if (y < E0) return x;
        if (x > E1) return N - 1 + y;
        if (y > E1) return 2 * (N - 1) + (N - 1 - x);
        return 3 * (N - 1) + (N - 1 - y);
    }

    private static bool RunLights(double x, double y, double t) => Mod(Around(x, y) - Math.Floor(t * 18), 7) < 3;

    private static bool OnLine(double a, double b) => Math.Abs(a - b) < 0.6;

    private static bool Stripe(double x, double y) => OnLine(x, N - 1 - y);

    private static bool Disc(double x, double y) => Distance(x - C, y - C) < 2.6;

    private static int Cell(int fine, int shift = 0) => (int)Math.Floor((fine + shift) / 5.0);

    private static bool Checks(int fx, int fy) => (Cell(fx) + Cell(fy)) % 2 == 0;

    private static double Distance(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static bool Even(double t, double period) => (long)Math.Floor(t / period) % 2 == 0;

    private static double Mod(double a, double m) => (a % m + m) % m;

    internal static double Tri(double v) => 1 - Math.Abs(Mod(v, 1) * 2 - 1);

    internal static double Ease(double v) => v * v * (3 - 2 * v);

    internal static double Hash(double x, double y, double k)
    {
        double v = Math.Sin(x * 127.1 + y * 311.7 + k * 74.7) * 43758.5453;
        return v - Math.Floor(v);
    }
}
