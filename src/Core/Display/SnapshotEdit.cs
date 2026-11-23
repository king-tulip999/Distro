using System.Globalization;
using Core.Config;
using static Core.Display.Ccd;

namespace Core.Display;

internal static class SnapshotEdit
{
    public static DisplaySnapshot Move(DisplaySnapshot snapshot, IReadOnlyDictionary<(int X, int Y), (int X, int Y)> moves)
    {
        var copy = new DisplaySnapshot
        {
            Summary = new List<string>(snapshot.Summary),
            TargetDevicePaths = new List<string>(snapshot.TargetDevicePaths),
            Paths = snapshot.Paths,
            Modes = snapshot.Modes,
        };
        const string demo = "demo:";
        if (snapshot.Paths.StartsWith(demo, StringComparison.Ordinal))
        {
            var parts = snapshot.Paths[demo.Length..].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            {
                var f = part.Split(',');
                var at = (int.Parse(f[1], CultureInfo.InvariantCulture), int.Parse(f[2], CultureInfo.InvariantCulture));
                if (moves.TryGetValue(at, out var to))
                {
                    f[1] = to.X.ToString(CultureInfo.InvariantCulture);
                    f[2] = to.Y.ToString(CultureInfo.InvariantCulture);
                }
                return string.Join(",", f);
            });
            copy.Paths = demo + string.Join(";", parts);
            return copy;
        }

        var modes = DisplayManager.FromBase64<ModeInfo>(snapshot.Modes);
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].InfoType != MODE_INFO_TYPE_SOURCE) continue;
            var position = modes[i].SourceMode.Position;
            if (moves.TryGetValue((position.X, position.Y), out var to))
                modes[i].SourceMode.Position = new PointL { X = to.X, Y = to.Y };
        }
        copy.Modes = DisplayManager.ToBase64(modes);
        return copy;
    }
}
