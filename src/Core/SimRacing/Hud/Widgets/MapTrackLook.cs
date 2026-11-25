using System.Drawing;

namespace Core.SimRacing.Hud.Widgets;

internal static class MapTrackLook
{
    public const string Overlay = "overlay", BlackWhite = "blackWhite", WhiteBlack = "whiteBlack", Custom = "custom";

    public static HudOption Look { get; } = HudOption.Choice("trackLook", "Colors", Overlay,
        new HudChoice(Overlay, "Overlay's own"),
        new HudChoice(BlackWhite, "Black, white border"),
        new HudChoice(WhiteBlack, "White, black border"),
        new HudChoice(Custom, "Pick my own"));

    public static HudOption TrackColor { get; } = HudOption.Color("trackColor", "Track color", "Until one is picked, the overlay's own");
    public static HudOption EdgeColor { get; } = HudOption.Color("edgeColor", "Border color", "Until one is picked, the overlay's own");

    public static bool Picking(HudOptions o) => o.Choice("trackLook") == Custom;

    public static (Color Track, Color Edge) Colors(HudPainter p, HudOptions o)
    {
        var track = p.P.MapTrack ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.3);
        var edge = p.P.MapEdge ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.55);
        return o.Choice("trackLook") switch
        {
            BlackWhite => (Color.Black, Color.White),
            WhiteBlack => (Color.White, Color.Black),
            Custom => (o.ColorOf("trackColor") ?? track, o.ColorOf("edgeColor") ?? edge),
            _ => (track, edge),
        };
    }
}
