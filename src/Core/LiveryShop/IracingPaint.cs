using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Core.Processes;
using Core.SimRacing;
using Core.SimRacing.IracingFiles;
using Core.SimRacing.Telemetry;

namespace Core.LiveryShop;

internal static partial class IracingPaint
{
    public static string? Folder => IracingPaths.FindDocumentsFolder() is { } documents ? Path.Combine(documents, "paint") : null;

    [GeneratedRegex(@"^(car(_num)?(_spec)?(_team)?|helmet|suit)_\d{1,10}\.tga$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileName();

    public static bool IsPaintFile(string name) => FileName().IsMatch(name);

    public static List<(string Dir, string Name)> Cars()
    {
        var dirs = new HashSet<string>(CarArt.Dirs, StringComparer.OrdinalIgnoreCase);
        if (Folder is { } paint && Directory.Exists(paint))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(paint))
                    dirs.Add(Path.GetFileName(dir));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return dirs.Where(IsCarDir).Select(d => (d, NameOf(d))).OrderBy(c => c.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsCarDir(string dir) =>
        dir.Length is > 0 and < 80 && dir.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && dir.Trim('.').Length > 0;

    private static string NameOf(string dir)
    {
        if (CarArt.KeyOfDir(dir) is not { } key) return dir;
        var words = key.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Any(char.IsDigit) || w.Length <= 3 ? w.ToUpperInvariant() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w));
        return string.Join(' ', words);
    }

    public static (long? Member, string? Car) Recent()
    {
        if (IracingPaths.FindDocumentsFolder() is not { } documents) return (null, null);
        var telemetry = Path.Combine(documents, "telemetry");
        if (!Directory.Exists(telemetry)) return (null, null);
        try
        {
            foreach (var file in new DirectoryInfo(telemetry).EnumerateFiles("*.ibt").OrderByDescending(f => f.LastWriteTimeUtc).Take(3))
                if (LiveryPictures.Session(file.FullName)?.Player is { UserId: > 0 } player)
                    return (player.UserId, player.CarPath.Length > 0 ? player.CarPath : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (null, null);
    }

    public static string Save(string kind, string? car, IReadOnlyList<(string Name, byte[] Bytes)> files, WrittenPaints written, string keptFolder)
    {
        if (Folder is not { } paint) throw new InvalidOperationException("iRacing's Documents folder wasn't found. Is iRacing installed?");
        string folder = paint;
        if (kind == "car")
        {
            if (car is null || !IsCarDir(car)) throw new InvalidOperationException("Pick the car in the Export tab first.");
            folder = Path.Combine(paint, car);
        }
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in files)
        {
            if (!IsPaintFile(name)) throw new InvalidOperationException($"“{name}” isn't an iRacing paint file name.");
            if (kind != "car" && name.StartsWith("car", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Car paints go in a car's folder.");
            string path = Path.Combine(folder, name), temp = path + ".tmp";
            if (written.NeedsKeeping(path, ReadExisting(path), bytes)) Keep(path, Path.Combine(keptFolder, Path.GetFileName(folder)));
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            written.Record(path, bytes);
        }
        written.Store();
        return folder;
    }

    private static byte[]? ReadExisting(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static void Keep(string path, string keptFolder)
    {
        RecycleBin.Send(new[] { path });
        if (!File.Exists(path)) return;
        Directory.CreateDirectory(keptFolder);
        string name = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path);
        string target = Path.Combine(keptFolder, name + extension);
        for (int n = 2; File.Exists(target); n++)
            target = Path.Combine(keptFolder, $"{name} ({n.ToString(CultureInfo.InvariantCulture)}){extension}");
        File.Copy(path, target);
    }

    public static bool ReloadInSim()
    {
        if (!AppWatcher.IsRunningNow(TelemetryService.SimProcess)) return false;
        uint message = RegisterWindowMessage("IRSDK_BROADCASTMSG");
        if (message == 0) return false;
        const int ReloadTextures = 7, AllCars = 0;
        return SendNotifyMessage(new IntPtr(0xFFFF), message, new IntPtr(ReloadTextures | (AllCars << 16)), IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
