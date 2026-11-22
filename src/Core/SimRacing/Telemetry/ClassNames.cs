using System.Collections.Concurrent;
using System.IO;

namespace Core.SimRacing.Telemetry;

internal static class ClassNames
{
    private const int MostFilesRead = 2000;
    private static readonly ConcurrentDictionary<int, string> Known = new();
    private static int _version;

    public static int Version => Volatile.Read(ref _version);

    public static string? Of(int classId) => Known.TryGetValue(classId, out var name) ? name : null;

    public static void Load(IReadOnlyDictionary<int, string>? saved)
    {
        if (saved is null) return;
        foreach (var (id, name) in saved)
            if (id > 0 && name.Length > 0) Known.TryAdd(id, name);
    }

    public static Dictionary<int, string> Snapshot() => new(Known);

    public static void Learn(IEnumerable<DriverEntry> drivers)
    {
        foreach (var d in drivers)
        {
            if (!d.Races || d.ClassId <= 0 || d.ClassName.Length == 0) continue;
            if (Known.TryGetValue(d.ClassId, out var had) && had == d.ClassName) continue;
            Known[d.ClassId] = d.ClassName;
            Interlocked.Increment(ref _version);
        }
    }

    public static void ReadTelemetryFiles(string folder, CancellationToken stop)
    {
        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(folder).EnumerateFiles("*.ibt", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).Take(MostFilesRead).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var file in files)
        {
            if (stop.IsCancellationRequested) return;
            try
            {
                using var ibt = IbtFile.Open(file.FullName, out _);
                if (ibt is not null) SessionInfo.Parse(ibt.SessionInfoYaml);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static string? SharedWord(IReadOnlyList<string> carNames, IEnumerable<IReadOnlyList<string>> otherClasses)
    {
        if (carNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) return null;
        var others = otherClasses.Where(c => c.Count > 0).Select(c => c.Select(Words).ToList()).ToList();
        foreach (var word in Words(carNames[0]))
        {
            if (!word.Any(char.IsLetter) || word.Length < 2) continue;
            if (!carNames.All(n => Words(n).Contains(word, StringComparer.OrdinalIgnoreCase))) continue;
            if (others.Any(o => o.All(w => w.Contains(word, StringComparer.OrdinalIgnoreCase)))) continue;
            return word;
        }
        return null;
    }

    private static string[] Words(string name) =>
        name.Split(new[] { ' ', '.', '-', '(', ')', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
}
