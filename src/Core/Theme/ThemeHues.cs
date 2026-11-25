using System.Windows.Media;

namespace Core.Theme;

internal static class ThemeHues
{
    public static Color Of(System.Drawing.Color c) => Color.FromRgb(c.R, c.G, c.B);

    public static string Words(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d < 0.12) return max > 0.8 ? "white grey gray" : "grey gray black";
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (h * 60 + 360) % 360;
        return h switch
        {
            < 15 or >= 345 => "red",
            < 40 => "orange",
            < 65 => "yellow gold",
            < 165 => "green",
            < 195 => "teal cyan",
            < 255 => "blue",
            < 290 => "purple violet",
            _ => "pink magenta",
        };
    }

    public static Brush Glow(Color bg, Color glow)
    {
        var brush = new RadialGradientBrush(glow, bg) { Center = new System.Windows.Point(0, 0), GradientOrigin = new System.Windows.Point(0, 0), RadiusX = 0.9, RadiusY = 0.9 };
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Solid(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
