using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;

namespace Core.LiveryShop;

internal sealed class PaintGuard : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);

    private readonly Dictionary<string, byte[]> _saved = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _settle;
    private readonly Dispatcher _dispatcher;
    private FileSystemWatcher? _watcher;
    private bool _saving, _told, _checking;

    public PaintGuard()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settle = new DispatcherTimer { Interval = Settle };
        _settle.Tick += async (_, _) => await CheckAsync();
    }

    public event Action<IReadOnlyList<string>>? Replaced;

    public void Saving() => _saving = true;

    public void Saved(string folder, IEnumerable<(string Name, byte[] Bytes)> files)
    {
        _saved.Clear();
        foreach (var (name, bytes) in files)
            _saved[Path.Combine(folder, name)] = SHA256.HashData(bytes);
        _saving = false;
        _told = false;
        Watch(folder);
    }

    public void SaveFailed() => _saving = false;

    internal static List<string> ReplacedFiles(IReadOnlyDictionary<string, byte[]> saved, Func<string, byte[]?> read)
    {
        var replaced = new List<string>();
        foreach (var (path, hash) in saved)
        {
            byte[]? now = read(path);
            if (now is { Length: 0 }) continue;
            if (now is null || !SHA256.HashData(now).AsSpan().SequenceEqual(hash))
                replaced.Add(Path.GetFileName(path));
        }
        return replaced;
    }

    private void Watch(string folder)
    {
        if (_watcher is not null && string.Equals(_watcher.Path, folder, StringComparison.OrdinalIgnoreCase)) return;
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            var watcher = new FileSystemWatcher(folder, "*.tga") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_watcher is null) return;
            _settle.Stop();
            _settle.Start();
        });
    }

    private async Task CheckAsync()
    {
        _settle.Stop();
        if (_saving || _told || _checking || _saved.Count == 0) return;
        _checking = true;
        var saved = new Dictionary<string, byte[]>(_saved, StringComparer.OrdinalIgnoreCase);
        List<string> replaced;
        try { replaced = await Task.Run(() => ReplacedFiles(saved, Read)); }
        finally { _checking = false; }
        if (_saving || _told || replaced.Count == 0 || _watcher is null) return;
        _told = true;
        Replaced?.Invoke(replaced);
    }

    private static byte[]? Read(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
            catch (IOException) when (attempt < 3) { Thread.Sleep(300); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<byte>(); }
        }
    }

    public void Dispose()
    {
        _settle.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _saved.Clear();
    }
}
