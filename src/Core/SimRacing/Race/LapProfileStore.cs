using System.IO;
using System.Text.Json;

namespace Core.SimRacing.Race;

internal sealed class LapProfileData
{
    public double Lap { get; set; }
    public double[] Pct { get; set; } = [];
    public double[] Time { get; set; } = [];
}

internal sealed class LapProfileStore
{
    private static readonly JsonSerializerOptions Opts = new();
    private const long SaveEveryMs = 30_000;
    private readonly string? _path;
    private readonly Dictionary<string, LapProfileData> _profiles;
    private long _savedAt = long.MinValue / 2;
    private bool _unsaved;

    public LapProfileStore(string? path)
    {
        _path = path;
        try { _profiles = path is not null && File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, LapProfileData>>(File.ReadAllText(path), Opts) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _profiles = new(); }
    }

    public static string Key(int track, string carPath) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{track}|{carPath}");

    public LapProfileData? Get(string key, double lap) =>
        _profiles.TryGetValue(key, out var p) && p is not null && p.Lap == lap && p.Pct is not null && p.Time is not null && p.Pct.Length == p.Time.Length ? p : null;

    public void Put(string key, LapProfileData data)
    {
        _profiles[key] = data;
        _unsaved = true;
        if (Environment.TickCount64 - _savedAt >= SaveEveryMs) Flush();
    }

    public void Flush()
    {
        if (!_unsaved) return;
        _unsaved = false;
        _savedAt = Environment.TickCount64;
        if (_path is null) return;
        string json = JsonSerializer.Serialize(_profiles, Opts);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
