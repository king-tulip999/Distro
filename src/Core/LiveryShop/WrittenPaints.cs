using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.LiveryShop;

internal sealed class WrittenPaints
{
    private readonly string _file;
    private readonly Dictionary<string, string> _hashes;

    public WrittenPaints(string file)
    {
        _file = file;
        _hashes = Load(file);
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public bool NeedsKeeping(string path, byte[]? current, byte[] next) => NeedsKeeping(_hashes, path, current, next);

    internal static bool NeedsKeeping(IReadOnlyDictionary<string, string> written, string path, byte[]? current, byte[] next)
    {
        if (current is null || current.Length == 0) return false;
        if (current.AsSpan().SequenceEqual(next)) return false;
        return !(written.TryGetValue(Path.GetFullPath(path), out var hash) && hash == Hash(current));
    }

    public void Record(string path, byte[] bytes) => _hashes[Path.GetFullPath(path)] = Hash(bytes);

    public void Store()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        string temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_hashes));
        File.Move(temp, _file, overwrite: true);
    }

    private static Dictionary<string, string> Load(string file)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) is { } stored)
                foreach (var (path, hash) in stored)
                    if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(hash)) hashes[path] = hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return hashes;
    }
}
