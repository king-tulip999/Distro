using System.Drawing;
using Core.Config;

namespace Core.SimRacing.Hud.Vr;

internal readonly record struct VrSpot(double X, double Y, double Distance, double Width);

internal static class VrPlacement
{
    public const double ScreenWidth = 1.2, ScreenDistance = 1.0;
    public const float MaxScale = 2;

    public static VrSpot FromScreen(RectangleF widget, RectangleF monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0 || widget.Width <= 0) return new VrSpot(0, 0, ScreenDistance, 0.3);
        double screenHeight = ScreenWidth * monitor.Height / monitor.Width;
        double cx = (widget.X + widget.Width / 2 - monitor.X) / monitor.Width;
        double cy = (widget.Y + widget.Height / 2 - monitor.Y) / monitor.Height;
        double width = widget.Width / monitor.Width * ScreenWidth;
        return new VrSpot(
            Math.Clamp((cx - 0.5) * ScreenWidth, -HudWidgetConfig.VrReach, HudWidgetConfig.VrReach),
            Math.Clamp((0.5 - cy) * screenHeight, -HudWidgetConfig.VrReach, HudWidgetConfig.VrReach),
            ScreenDistance,
            Math.Clamp(width, HudWidgetConfig.MinVrWidth, HudWidgetConfig.MaxVrWidth));
    }

    public static void PlaceFromScreen(HudWidgetConfig w, RectangleF widget, RectangleF monitor)
    {
        var spot = FromScreen(widget, monitor);
        w.VrX = spot.X;
        w.VrY = spot.Y;
        w.VrDistance = spot.Distance;
        w.VrWidth = spot.Width;
    }

    public static Size Pixels(SizeF layout, int maxPixels)
    {
        if (layout.Width <= 0 || layout.Height <= 0) return new Size(1, 1);
        float scale = Math.Min(MaxScale, MathF.Sqrt(maxPixels / (layout.Width * layout.Height)));
        int w = Math.Max(1, (int)MathF.Floor(layout.Width * scale));
        int h = Math.Max(1, (int)MathF.Floor(layout.Height * scale));
        while ((long)w * h > maxPixels)
        {
            w = Math.Max(1, w - 1);
            h = Math.Max(1, (int)MathF.Floor(w * layout.Height / layout.Width));
        }
        return new Size(w, h);
    }

    public static float[] Matrix(double x, double y, double distance) =>
        new[] { 1f, 0, 0, (float)x, 0, 1f, 0, (float)y, 0, 0, 1f, (float)-distance };

    public static void Fade(Span<byte> premultiplied, int opacity)
    {
        if (opacity >= 100) return;
        int k = Math.Clamp(opacity, 0, 100) * 256 / 100;
        for (int i = 0; i < premultiplied.Length; i++) premultiplied[i] = (byte)(premultiplied[i] * k >> 8);
    }
}
