using Core.SimRacing.Race;

namespace Core.SimRacing.Hud;

internal enum LedDot : byte
{
    Off,
    White,
    Yellow,
    Green,
    Blue,
    Red,
    Orange,
    Frame,
}

internal enum LedFlag
{
    None,
    SafetyCar,
    Green,
    Yellow,
    Blue,
    White,
    Checkered,
    Black,
    Repair,
    Red,
}

internal static class LedBoard
{
    public const int Size = 30;
    private const int Inner = 26;

    private static readonly Dictionary<char, string> Segments = new()
    {
        ['0'] = "abcdef", ['2'] = "abged", ['3'] = "abgcd", ['4'] = "fgbc", ['5'] = "afgcd", ['6'] = "afgedc",
        ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg", ['S'] = "afgcd", ['C'] = "afed", ['N'] = "febc", ['R'] = "afbge",
    };

    public static bool CanShow(string text)
    {
        if (text.Length is < 1 or > 3) return false;
        foreach (char c in text)
            if (c != '1' && !Segments.ContainsKey(c)) return false;
        return true;
    }

    public static LedFlag FlagOf(FlagKind kind) => kind switch
    {
        FlagKind.Caution => LedFlag.SafetyCar,
        FlagKind.Yellow or FlagKind.Debris => LedFlag.Yellow,
        FlagKind.Blue => LedFlag.Blue,
        FlagKind.White => LedFlag.White,
        FlagKind.Checkered => LedFlag.Checkered,
        FlagKind.Meatball => LedFlag.Repair,
        FlagKind.Black or FlagKind.Disqualified => LedFlag.Black,
        FlagKind.Red => LedFlag.Red,
        FlagKind.Green => LedFlag.Green,
        _ => LedFlag.None,
    };

    public static void Build(LedDot[] dots, string text, bool frame)
    {
        Array.Clear(dots);
        if (!frame)
        {
            Text(new Area(dots, Size, 0, Size), text, false);
            return;
        }
        var g = new LedDot[Inner * Inner];
        Ring(g, LedDot.Frame);
        Text(new Area(g, Inner, 2, Inner - 2), text, true);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                dots[y * Size + x] = g[Clamp(y) * Inner + Clamp(x)];
    }

    public static void Digits(LedDot[] dots, string text, bool framed)
    {
        Build(dots, text, framed);
        for (int i = 0; i < dots.Length; i++)
            if (dots[i] == LedDot.Frame) dots[i] = LedDot.Off;
    }

    public const int FrameWidth = 3;

    public static bool IsFrame(int x, int y) => x < FrameWidth || y < FrameWidth || x >= Size - FrameWidth || y >= Size - FrameWidth;

    public static double BothSides(int x, int y)
    {
        double c = (Size - 1) / 2.0;
        double a = Math.Atan2(y - c, x - c) - Math.PI / 2;
        double turn = (a % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) / (2 * Math.PI);
        return turn <= 0.5 ? turn * 2 : (1 - turn) * 2;
    }

    public static double RevFill(double? rpm, double first, double shift) =>
        rpm is { } r && shift > first ? Math.Clamp((r - first) / (shift - first), 0, 1) : 0;

    public static bool OnLimiter(double? rpm, double limit) => limit > 0 && rpm is { } r && r >= limit;

    private static int Clamp(int v) => Math.Clamp(v - 2, 0, Inner - 1);

    private static void Ring(LedDot[] g, LedDot c)
    {
        for (int i = 0; i < Inner; i++)
        {
            g[i] = g[(Inner - 1) * Inner + i] = c;
            g[i * Inner] = g[i * Inner + Inner - 1] = c;
        }
    }

    private readonly record struct Area(LedDot[] Dots, int Stride, int Lo, int Hi)
    {
        public void Rect(int x, int y, int w, int h)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                    if (i >= Lo && i < Hi && j >= Lo && j < Hi) Dots[j * Stride + i] = LedDot.White;
        }
    }

    private static void Text(Area a, string text, bool framed)
    {
        if (!CanShow(text)) return;
        if (framed)
        {
            switch (text.Length)
            {
                case 1 when text == "N": Glyph(a, 'N', 4, 2, 18, 22, 4); break;
                case 1: Glyph(a, text[0], 6, 2, 14, 22, 4); break;
                case 2: Glyph(a, text[0], 2, 2, 10, 22, 3); Glyph(a, text[1], 14, 2, 10, 22, 3); break;
                default: for (int i = 0; i < 3; i++) Glyph(a, text[i], 2 + i * 8, 2, 6, 22, 2); break;
            }
            return;
        }
        switch (text.Length)
        {
            case 1 when text == "N": Glyph(a, 'N', 4, 1, 22, 28, 5); break;
            case 1: Glyph(a, text[0], 6, 1, 18, 28, 5); break;
            case 2: Glyph(a, text[0], 1, 1, 13, 28, 4); Glyph(a, text[1], 16, 1, 13, 28, 4); break;
            default: for (int i = 0; i < 3; i++) Glyph(a, text[i], 1 + i * 10, 1, 8, 28, 3); break;
        }
    }

    private static void Glyph(Area a, char d, int x, int y, int w, int h, int t)
    {
        if (d == '1')
        {
            int sw = t * 2 - 2, sx = x + (w - sw + 1) / 2;
            a.Rect(sx, y, sw, h);
            a.Rect(sx - t, y, t, t);
            a.Rect(x, y + h - t, w, t);
            return;
        }
        int m = y + (h - t) / 2;
        string s = Segments[d];
        if (s.Contains('a')) a.Rect(x, y, w, t);
        if (s.Contains('d')) a.Rect(x, y + h - t, w, t);
        if (s.Contains('g')) a.Rect(x, m, w, t);
        if (s.Contains('f')) a.Rect(x, y, t, m - y + t);
        if (s.Contains('b')) a.Rect(x + w - t, y, t, m - y + t);
        if (s.Contains('e')) a.Rect(x, m, t, y + h - m);
        if (s.Contains('c')) a.Rect(x + w - t, m, t, y + h - m);
        if (d == 'N') Slant(a, x + t - 1, y, x + w - 2 * t + 1, y + h, t);
        if (d == 'R') Slant(a, x + (w - t) / 2, m + t, x + w - t, y + h, t);
    }

    private static void Slant(Area a, int x0, int y0, int x1, int y1, int t)
    {
        int n = y1 - y0;
        for (int r = 0; r < n; r++)
        {
            int xx = (int)Math.Floor(x0 + (x1 - x0) * r / (double)Math.Max(1, n - 1) + 0.5);
            a.Rect(xx, y0 + r, t, 1);
        }
    }
}
