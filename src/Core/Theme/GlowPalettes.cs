using System.Windows.Media;

namespace Core.Theme;

internal static class GlowPalettes
{
    private static Color Hex(string hex) => Palettes.Hex(hex);

    private static Dictionary<string, Color> Shared() => new()
    {
        ["Bg"] = Hex("#08080B"), ["Panel"] = Hex("#0F0F13"), ["Surface"] = Hex("#0F0F13"), ["SurfaceAlt"] = Hex("#0C0C10"),
        ["TileRaised"] = Hex("#17171D"), ["TileEdge"] = Hex("#25252D"),
        ["Line"] = Hex("#25252D"), ["LineStrong"] = Hex("#393943"),
        ["Heading"] = Hex("#F8EEDB"), ["Text"] = Hex("#E4D9C5"), ["TextMuted"] = Hex("#ADA493"), ["TextFaint"] = Hex("#938B7C"),
        ["OnAccent"] = Hex("#0A0210"),
        ["Sage"] = Hex("#3DFF8A"), ["SageSoft"] = Hex("#152E28"), ["SageDeep"] = Hex("#3DFF8A"),
        ["Warn"] = Hex("#FF9F1C"), ["WarnSoft"] = Hex("#302119"), ["WarnDeep"] = Hex("#FF9F1C"),
        ["Danger"] = Hex("#FF4545"), ["DangerSoft"] = Hex("#30141E"), ["DangerDeep"] = Hex("#FF7A7A"),
        ["Gold"] = Hex("#FF9F1C"), ["GoldSoft"] = Hex("#302119"), ["GoldDeep"] = Hex("#FFB84F"), ["GoldEdge"] = Hex("#CC7F16"), ["OnGold"] = Hex("#1A1000"),
        ["Apricot"] = Hex("#D9865A"), ["ApricotSoft"] = Hex("#2A1D21"), ["ApricotDeep"] = Hex("#FFB088"),
        ["Coral"] = Hex("#FF6A3D"), ["CoralSoft"] = Hex("#30191D"), ["CoralDeep"] = Hex("#FF8C66"),
        ["Plum"] = Hex("#FF6A3D"), ["PlumSoft"] = Hex("#30191D"), ["PlumDeep"] = Hex("#FF8C66"),
        ["Knob"] = Hex("#66666F"),
        ["MeterTrack"] = Hex("#1C1C23"), ["GridLine"] = Hex("#18181E"),
        ["Meter4"] = Hex("#FF9F1C"), ["Meter5"] = Hex("#FF4545"),
        ["ScrollThumbBrushHover"] = Hex("#4A4A55"),
        ["TooltipBg"] = Hex("#17171D"), ["TooltipText"] = Hex("#E4D9C5"), ["CloseHover"] = Hex("#E02E2E"),
        ["Dim"] = Hex("#CC000000"),
        ["InputEdge"] = Hex("#62626D"), ["InputEdgeHover"] = Hex("#7A7A85"),
        ["OnDanger"] = Hex("#1A0303"),
    };

    private static Dictionary<string, Color> With(Dictionary<string, Color> shared, params (string Key, string Hex)[] own)
    {
        foreach (var (key, hex) in own) shared[key] = Hex(hex);
        return shared;
    }

    public static readonly IReadOnlyDictionary<string, Color> Tecnica = With(Shared(),
        ("Accent", "#49C8F5"), ("AccentHover", "#64D0F6"), ("AccentPressed", "#399CBF"), ("AccentEdge", "#49C8F5"),
        ("AccentSoft", "#172A3B"), ("AccentText", "#8ADCFA"),
        ("Hover", "#1D1D24"), ("HoverRaised", "#2A2A33"),
        ("Slate", "#A8E6FF"), ("SlateSoft", "#242B38"), ("SlateDeep", "#A8E6FF"), ("FocusEdge", "#A8E6FF"),
        ("Meter1", "#49C8F5"), ("Meter2", "#49C8F5"), ("Meter3", "#49C8F5"),
        ("GraphSoft", "#142D3B"), ("ScrollThumbBrushDrag", "#399CBF"),
        ("BgGlow", "#0A1A24"));

    public static readonly IReadOnlyDictionary<string, Color> Velocita = With(Shared(),
        ("Accent", "#DB156B"), ("AccentHover", "#E03881"), ("AccentPressed", "#AB1053"), ("AccentEdge", "#DB156B"),
        ("AccentSoft", "#2F0D25"), ("AccentText", "#F5559A"),
        ("Hover", "#1D1D24"), ("HoverRaised", "#2A2A33"),
        ("Slate", "#49C8F5"), ("SlateSoft", "#162637"), ("SlateDeep", "#8ADCFA"), ("FocusEdge", "#F5559A"),
        ("Meter1", "#DB156B"), ("Meter2", "#DB156B"), ("Meter3", "#DB156B"),
        ("GraphText", "#FF9CC4"), ("GraphSoft", "#310920"), ("ScrollThumbBrushDrag", "#AB1053"),
        ("BgGlow", "#150A10"));
}
