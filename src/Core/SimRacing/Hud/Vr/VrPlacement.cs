using System.Drawing;
using Core.Config;

namespace Core.SimRacing.Hud.Vr;

internal readonly record struct VrSpot(double X, double Y, double Distance, double Width);

internal readonly record struct VrPose(double X, double Y, double Distance, double Width, double Turn, double Tilt, double CurveRadius)
{
    public float[] Matrix() => VrPlacement.Matrix(X, Y, Distance, Turn, Tilt);

    public float Curvature => CurveRadius > 0 ? (float)Math.Min(1, Width / (2 * Math.PI * CurveRadius)) : 0;
}

internal enum VrAdjustSetting { Move, DistanceAndWidth, Angle }

internal static class VrAdjust
{
    public const double Step = 0.01, AngleStep = 2;

    public static string Label(VrAdjustSetting setting) => setting switch
    {
        VrAdjustSetting.DistanceAndWidth => "up / down: distance, left / right: width",
        VrAdjustSetting.Angle => "left / right: turn, up / down: tilt",
        _ => "move",
    };

    public static VrAdjustSetting Next(VrAdjustSetting setting, bool all) =>
        all ? setting == VrAdjustSetting.Move ? VrAdjustSetting.DistanceAndWidth : VrAdjustSetting.Move
            : (VrAdjustSetting)(((int)setting + 1) % 3);

    public static void Nudge(HudWidgetConfig? widget, RaceHudSettings settings, VrAdjustSetting setting, int right, int up)
    {
        if (widget is null)
        {
            if (setting == VrAdjustSetting.Move)
            {
                settings.VrShiftX += right * Step;
                settings.VrShiftY += up * Step;
            }
            else settings.VrShiftDistance += up * Step;
            settings.ClampVrShift();
            return;
        }
        switch (setting)
        {
            case VrAdjustSetting.Move:
                widget.VrX += right * Step;
                widget.VrY += up * Step;
                break;
            case VrAdjustSetting.DistanceAndWidth:
                widget.VrDistance += up * Step;
                widget.VrWidth += right * Step;
                break;
            default:
                widget.VrTurn += right * AngleStep;
                widget.VrTilt += up * AngleStep;
                break;
        }
        widget.Clamp();
    }
}

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

    // Velocita for SimHub has no monitor spots to copy: widgets start in a grid just below eye level, left to right, then lower rows.
    public static void PlaceInFront(HudWidgetConfig w, SizeF layout, int index)
    {
        const double ColumnStep = 0.42, RowStep = 0.3, Top = -0.12;
        int column = index % 3, row = index / 3 % 3;
        w.VrX = column == 0 ? 0 : column == 1 ? -ColumnStep : ColumnStep;
        w.VrY = Math.Clamp(Top - row * RowStep, -HudWidgetConfig.VrReach, HudWidgetConfig.VrReach);
        w.VrDistance = ScreenDistance;
        w.VrWidth = Math.Clamp(layout.Width / 1920.0 * ScreenWidth, HudWidgetConfig.MinVrWidth, HudWidgetConfig.MaxVrWidth);
        (w.VrTurn, w.VrTilt) = FacingHead(w.VrX, w.VrY, w.VrDistance);
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

    public static VrPose Pose(HudWidgetConfig c, RaceHudSettings? settings)
    {
        double x = c.VrX, y = c.VrY, distance = c.VrDistance;
        if (c.Vr == HudVrPlacement.Cockpit && settings is not null)
        {
            x += settings.VrShiftX;
            y += settings.VrShiftY;
            distance += settings.VrShiftDistance;
        }
        distance = Math.Clamp(distance, HudWidgetConfig.MinVrDistance, HudWidgetConfig.MaxVrDistance);
        double radius = c.VrCurve ? Math.Max(distance, c.VrWidth / Math.PI) : 0;
        return new VrPose(x, y, distance, c.VrWidth, c.VrTurn, c.VrTilt, radius);
    }

    public static float[] Matrix(double x, double y, double distance, double turn = 0, double tilt = 0)
    {
        double yaw = turn * Math.PI / 180, pitch = -tilt * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        return new[]
        {
            (float)cy, (float)(sy * sp), (float)(sy * cp), (float)x,
            0, (float)cp, (float)-sp, (float)y,
            (float)-sy, (float)(cy * sp), (float)(cy * cp), (float)-distance,
        };
    }

    public static (float X, float Y, float Z, float W) Rotation(double turn, double tilt)
    {
        double yaw = turn * Math.PI / 360, pitch = -tilt * Math.PI / 360;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        return ((float)(cy * sp), (float)(sy * cp), (float)(-sy * sp), (float)(cy * cp));
    }

    public static (double Turn, double Tilt) FacingHead(double x, double y, double distance)
    {
        double length = Math.Sqrt(x * x + y * y + distance * distance);
        if (length <= 0) return (0, 0);
        double turn = Math.Atan2(-x, distance) * 180 / Math.PI;
        double tilt = Math.Asin(Math.Clamp(-y / length, -1, 1)) * 180 / Math.PI;
        return (Math.Round(Math.Clamp(turn, -HudWidgetConfig.MaxVrAngle, HudWidgetConfig.MaxVrAngle)),
            Math.Round(Math.Clamp(tilt, -HudWidgetConfig.MaxVrAngle, HudWidgetConfig.MaxVrAngle)));
    }

    public static void Fade(Span<byte> premultiplied, int opacity)
    {
        if (opacity >= 100) return;
        int k = Math.Clamp(opacity, 0, 100) * 256 / 100;
        for (int i = 0; i < premultiplied.Length; i++) premultiplied[i] = (byte)(premultiplied[i] * k >> 8);
    }
}
