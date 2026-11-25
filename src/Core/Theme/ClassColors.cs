using System.Windows.Media;

namespace Core.Theme;

internal static class ClassColors
{
    private static readonly Dictionary<int, SolidColorBrush> Cache = new();

    public static Brush? For(int rgb)
    {
        if (rgb <= 0 || rgb > 0xFFFFFF) return null;
        if (Cache.TryGetValue(rgb, out var known)) return known;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return Cache[rgb] = brush;
    }
}
