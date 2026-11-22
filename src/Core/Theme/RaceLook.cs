using System.Windows;
using System.Windows.Media;

namespace Core.Theme;

internal static class RaceLook
{
    public const int Places = 4;
    public const string FontPath = "./Assets/Fonts/Saira/Condensed/#Saira Condensed";

    private static readonly Color[] DarkPlaces = { Color.FromRgb(0xFF, 0xB2, 0x24), Color.FromRgb(0xC9, 0xCE, 0xD6), Color.FromRgb(0xD9, 0x9A, 0x63) };
    private static readonly Color[] LightPlaces = { Color.FromRgb(0xB8, 0x86, 0x0B), Color.FromRgb(0x6E, 0x76, 0x81), Color.FromRgb(0xA0, 0x61, 0x2A) };

    public const string OffPodium = "Place5";

    private static readonly Color OffPodiumFill = Color.FromRgb(0x9A, 0x9A, 0xA8);

    public static string Key(int? finish) => finish is >= 1 and <= 3 ? "Place" + finish : OffPodium;

    public static string Backdrop(string place) => place == OffPodium ? "Place" + Places : place;

    public static void Install(ThemeSpec spec, IReadOnlyDictionary<string, Color> p, ResourceDictionary res)
    {
        var surface = p["Surface"];
        for (int n = 1; n <= Places; n++)
        {
            var fill = n < Places ? (spec.Dark ? DarkPlaces : LightPlaces)[n - 1] : p["Accent"];
            var text = n < Places ? fill : p["AccentText"];
            string key = "Place" + n;
            res[key] = Brush(fill);
            res[key + "Text"] = Brush(text);
            res[key + "Soft"] = Brush(ThemeManager.Mix(p["SurfaceAlt"], fill, 0.1));
            res[key + "Edge"] = Brush(ThemeManager.Mix(surface, fill, 0.55));
        }
        res[OffPodium] = Brush(OffPodiumFill);
        res[OffPodium + "Text"] = Brush(p["Heading"]);
        res[OffPodium + "Soft"] = Brush(ThemeManager.Mix(p["SurfaceAlt"], OffPodiumFill, 0.06));
        res[OffPodium + "Edge"] = Brush(ThemeManager.Mix(surface, OffPodiumFill, 0.4));
        res["RaceFont"] = ThemeManager.Family(FontPath);
    }

    private static SolidColorBrush Brush(Color c) => Freeze(new SolidColorBrush(c));

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
