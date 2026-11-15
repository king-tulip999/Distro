using System.Drawing;
using System.Globalization;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class LedBoardWidget : HudWidget
{
    private static readonly Color Board = Color.FromArgb(0x11, 0x12, 0x14), Unlit = Color.FromArgb(0x1B, 0x1D, 0x20), Dimmed = Color.FromArgb(0x3A, 0x3A, 0x3A);

    private readonly LedDot[] _dots = new LedDot[LedBoard.Size * LedBoard.Size];

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("show", "Shows", "gear",
            new HudChoice("gear", "Gear"), new HudChoice("flag", "Flag", "Blank until a flag is out"), new HudChoice("lap", "Lap"), new HudChoice("pos", "Position"), new HudChoice("left", "Laps left")),
        HudOption.Choice("frame", "Frame color", "yellow",
            new HudChoice("yellow", "Yellow"), new HudChoice("white", "White"), new HudChoice("custom", "Custom"),
            new HudChoice("off", "Off", "The number fills the board")),
        HudOption.Color("frameColor", "Frame color", "Until one is picked, the overlay's accent color"),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        var f = v.Frame;
        string show = v.Options.Choice("show");
        var flag = show == "flag" ? LedBoard.FlagOf(HudFlags.BannerKind(f.Flags)) : LedFlag.None;
        string frame = v.Options.Choice("frame");
        bool blinkOn = (long)Math.Floor(v.Now / 0.4) % 2 == 0;
        LedBoard.Build(_dots, Text(v, show), flag, frame != "off", blinkOn);
        Paint(p, frame switch { "white" => HudPalette.FlagWhite, "custom" => v.Options.ColorOf("frameColor") ?? p.P.Accent, _ => HudPalette.FlagYellow });
    }

    internal static string Text(HudView v, string show)
    {
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        int? n = show switch
        {
            "lap" => f.Lap is > 0 and var lap ? lap : null,
            "pos" => (uint)me < TelemetryFrame.MaxCars && f.CarPosition[me] > 0 ? f.CarPosition[me] : null,
            "left" => v.Race.LapsToGo(f) is { } togo ? (int)Math.Ceiling(togo) : null,
            _ => null,
        };
        if (show == "gear") return f.Gear is { } g ? InputsWidget.GearText(g) : "";
        return n is >= 0 and < 1000 ? n.Value.ToString(CultureInfo.InvariantCulture) : "";
    }

    private void Paint(HudPainter p, Color frame)
    {
        var b = p.Bounds;
        float side = Math.Min(b.Width, b.Height);
        var box = new RectangleF(b.X + (b.Width - side) / 2, b.Y + (b.Height - side) / 2, side, side);
        p.G.FillRectangle(p.Brush(Board), box);
        float pitch = side / LedBoard.Size, r = pitch * 0.36f;
        for (int y = 0; y < LedBoard.Size; y++)
            for (int x = 0; x < LedBoard.Size; x++)
            {
                var c = _dots[y * LedBoard.Size + x] switch
                {
                    LedDot.White => HudPalette.FlagWhite,
                    LedDot.Yellow => HudPalette.FlagYellow,
                    LedDot.Green => HudPalette.FlagGreen,
                    LedDot.Blue => HudPalette.FlagBlue,
                    LedDot.Red => HudPalette.FlagRed,
                    LedDot.Dim => Dimmed,
                    LedDot.Frame => frame,
                    _ => Unlit,
                };
                float cx = box.X + (x + 0.5f) * pitch, cy = box.Y + (y + 0.5f) * pitch;
                if (c != Unlit && c != Dimmed) p.Dot(cx, cy, r * 1.45f, HudPalette.Alpha(c, 70));
                p.Dot(cx, cy, r, c);
            }
    }
}
