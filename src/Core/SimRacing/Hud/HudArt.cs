using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Core.SimRacing.Hud;

internal static class HudArt
{
    private const string Assets = "pack://application:,,,/Core;component/Assets/";
    private static readonly object Gate = new();

    private static byte[]? Read(string path)
    {
        var info = System.Windows.Application.GetResourceStream(new Uri(Assets + path));
        if (info?.Stream is not { } stream) return null;
        using (stream)
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    private static readonly Dictionary<string, Image?> Logos = new(StringComparer.OrdinalIgnoreCase);

    public static Image? Logo(string? brand)
    {
        if (string.IsNullOrEmpty(brand)) return null;
        lock (Gate)
        {
            if (Logos.TryGetValue(brand, out var known)) return known;
            Image? image = null;
            try
            {
                if (Read($"Logos/{brand}.png") is { } bytes)
                {
                    using var stream = new MemoryStream(bytes);
                    using var loaded = Image.FromStream(stream);
                    image = new Bitmap(loaded);
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or UriFormatException)
            {
                App.LogError(ex);
            }
            return Logos[brand] = image;
        }
    }
}
