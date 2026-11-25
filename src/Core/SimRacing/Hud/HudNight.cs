using System.Drawing;
using System.Runtime.InteropServices;
using Core.Config;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud;

internal static class HudNight
{
    public static bool IsNight(HudNightMode mode, double? sunAltitudeRadians) => mode switch
    {
        HudNightMode.On => true,
        HudNightMode.Auto => sunAltitudeRadians is { } sun && double.IsFinite(sun) && sun < 0,
        _ => false,
    };

    public static bool IsNight(RaceHudSettings settings, TelemetryFrame frame) =>
        IsNight(settings.NightMode, frame.Status == SimStatus.Connected ? frame.RawValue("SolarAltitude") : null);

    public static Color Tint(HudNightColor color) => color switch
    {
        HudNightColor.Blue => Color.FromArgb(0x22, 0xB8, 0xF0),
        HudNightColor.Purple => Color.FromArgb(0xA8, 0x5C, 0xFF),
        HudNightColor.Orange => Color.FromArgb(0xFF, 0x9A, 0x1F),
        _ => Color.FromArgb(0xFF, 0x2E, 0x2E),
    };

    private static readonly Dictionary<HudNightColor, HudPalette> Palettes = new();
    private static readonly object Gate = new();

    public static HudPalette Palette(HudNightColor color)
    {
        lock (Gate)
        {
            if (Palettes.TryGetValue(color, out var known)) return known;
            var built = Build(Tint(color));
            if (color == HudNightColor.Red) SetApartFromRed(built);
            return Palettes[color] = built;
        }
    }

    private static void SetApartFromRed(HudPalette p)
    {
        var d = HudPalette.Default;
        p.Lapping = HudPalette.Mix(d.Lapping, Color.White, 0.55);
        p.Danger = HudPalette.Mix(d.Danger, Color.White, 0.5);
        p.DangerText = HudPalette.Mix(d.DangerText, Color.White, 0.55);
    }

    public static HudAppearance Look(HudAppearance look) => new()
    {
        Colors = look.Colors, Custom = look.Custom, Background = Math.Max(look.Background, 90), FontScale = look.FontScale,
        Density = look.Density, Corner = look.Corner, ReducedMotion = look.ReducedMotion, Outline = true,
    };

    internal static HudPalette Build(Color tint)
    {
        var d = HudPalette.Default;
        var black = Color.FromArgb(255, 0, 0, 0);
        Color Shade(double t) => HudPalette.Mix(black, tint, t);
        var bright = HudPalette.Mix(tint, Color.White, 0.25);
        return new HudPalette
        {
            Dark = true, Night = true,
            Bg = black, Surface = black, SurfaceAlt = Shade(0.14), Text = tint, Heading = bright,
            Muted = Shade(0.75), Faint = Shade(0.58), Line = Shade(0.28), LineStrong = Shade(0.5),
            Accent = tint, AccentText = bright, AccentSoft = Shade(0.18), OnAccent = black,
            Good = d.Good, GoodText = d.GoodText, GoodSoft = d.GoodSoft, Warn = d.Warn, WarnText = d.WarnText, WarnSoft = d.WarnSoft,
            Danger = d.Danger, DangerText = d.DangerText, DangerSoft = d.DangerSoft, Slate = Shade(0.58), Plum = d.Plum,
            Player = tint, PlayerText = bright, Highlight = Shade(0.32), HighlightEdge = tint, BadgeInk = Shade(0.88), ClassRowDepth = 0.45f, SoftBands = true,
            InputTrack = Shade(0.12), TraceBack = black, InputTicks = Shade(0.4), RedZone = d.RedZone,
        };
    }

    public static void Dim(IntPtr bits, int pixels, int brightness, ref int[] buffer)
    {
        if (brightness >= 100 || bits == IntPtr.Zero || pixels <= 0) return;
        if (buffer.Length < pixels) buffer = new int[pixels];
        Marshal.Copy(bits, buffer, 0, pixels);
        Dim(buffer.AsSpan(0, pixels), brightness);
        Marshal.Copy(buffer, 0, bits, pixels);
    }

    public static void Dim(Span<int> pixels, int brightness)
    {
        uint keep = (uint)(Math.Clamp(brightness, 0, 100) * 256 / 100);
        for (int i = 0; i < pixels.Length; i++)
        {
            uint px = (uint)pixels[i];
            if ((px & 0x00FFFFFF) == 0) continue;
            uint rb = ((px & 0x00FF00FF) * keep >> 8) & 0x00FF00FF;
            uint g = ((px & 0x0000FF00) * keep >> 8) & 0x0000FF00;
            pixels[i] = (int)((px & 0xFF000000) | rb | g);
        }
    }
}
