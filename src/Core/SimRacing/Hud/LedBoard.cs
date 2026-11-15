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
    Dim,
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
        FlagKind.Black or FlagKind.Meatball or FlagKind.Disqualified => LedFlag.Black,
        FlagKind.Red => LedFlag.Red,
        FlagKind.Green => LedFlag.Green,
        _ => LedFlag.None,
    };

    public static void Build(LedDot[] dots, string text, LedFlag flag, bool frame, bool blinkOn)
    {
        Array.Clear(dots);
        if (flag == LedFlag.Checkered)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    dots[y * Size + x] = (x / 5 + y / 5) % 2 == 0 ? LedDot.White : LedDot.Off;
            return;
        }
        if (flag == LedFlag.None && !frame)
        {
            var full = new Area(dots, Size, 0, Size);
            Text(full, text, false);
            return;
        }
        var g = new LedDot[Inner * Inner];
        var inner = new Area(g, Inner, 2, Inner - 2);
        switch (flag)
        {
            case LedFlag.None:
                Ring(g, LedDot.Frame);
                Text(inner, text, true);
                break;
            case LedFlag.SafetyCar:
                if (blinkOn) Ring(g, LedDot.Yellow);
                Text(inner, "SC", true);
                break;
            case LedFlag.Blue:
                for (int y = 0; y < Inner; y++)
                    for (int x = 0; x < Inner; x++)
                        g[y * Inner + x] = Math.Abs(x - (Inner - 1 - y)) <= 2 ? LedDot.Yellow : LedDot.Blue;
                break;
            case LedFlag.Black:
                Ring(g, LedDot.White);
                if (blinkOn) Fill(g, 2, Inner - 2, LedDot.Dim);
                break;
            default:
                bool steady = flag is LedFlag.Yellow or LedFlag.Red;
                if (steady || blinkOn) Fill(g, 0, Inner, Solid(flag));
                break;
        }
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                dots[y * Size + x] = g[Clamp(y) * Inner + Clamp(x)];
    }

    private static int Clamp(int v) => Math.Clamp(v - 2, 0, Inner - 1);

    private static LedDot Solid(LedFlag flag) => flag switch
    {
        LedFlag.Green => LedDot.Green,
        LedFlag.Yellow => LedDot.Yellow,
        LedFlag.White => LedDot.White,
        _ => LedDot.Red,
    };

    private static void Ring(LedDot[] g, LedDot c)
    {
        for (int i = 0; i < Inner; i++)
        {
            g[i] = g[(Inner - 1) * Inner + i] = c;
            g[i * Inner] = g[i * Inner + Inner - 1] = c;
        }
    }

    private static void Fill(LedDot[] g, int from, int to, LedDot c)
    {
        for (int y = from; y < to; y++)
            for (int x = from; x < to; x++)
                g[y * Inner + x] = c;
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
