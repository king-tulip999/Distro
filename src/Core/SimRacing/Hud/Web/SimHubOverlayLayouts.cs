using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Core.SimRacing.Hud.Web;

internal sealed record RepeatedOverlay(string Layout, string Overlay, int Count, string Placed)
{
    public const char Separator = '|';

    public bool IsPlaced(string overlay) => Placed.Split(Separator).Contains(overlay, StringComparer.OrdinalIgnoreCase);
}

internal sealed class SimHubOverlayLayouts : IDisposable
{
    private const string Folder = "OverlayLayouts";
    private const string Pattern = "*.olayout";

    private readonly string _folder;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public SimHubOverlayLayouts(string simHubFolder)
    {
        _folder = Path.Combine(simHubFolder, Folder);
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Read();
        };
        try
        {
            Directory.CreateDirectory(_folder);
            _watcher = new FileSystemWatcher(_folder, Pattern) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            App.LogError(ex);
        }
        Read();
    }

    public IReadOnlyList<RepeatedOverlay> Repeated { get; private set; } = Array.Empty<RepeatedOverlay>();

    public event Action? Changed;

    private void OnFileEvent(object sender, FileSystemEventArgs e) => _settle.Dispatcher.BeginInvoke(() =>
    {
        _settle.Stop();
        _settle.Start();
    });

    private void Read()
    {
        var found = new List<RepeatedOverlay>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_folder, Pattern).Order(StringComparer.OrdinalIgnoreCase))
            {
                try { found.AddRange(Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (found.SequenceEqual(Repeated)) return;
        Repeated = found;
        Changed?.Invoke();
    }

    internal static List<RepeatedOverlay> Parse(string json, string fileName)
    {
        var list = new List<RepeatedOverlay>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("OverlayLayoutParts", out var parts) || parts.ValueKind != JsonValueKind.Array) return list;
            string layout = root.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } given ? given : fileName;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("DashboardName", out var dash) || dash.ValueKind != JsonValueKind.String) continue;
                if (OverlayName(dash.GetString()!) is not { } overlay) continue;
                if (counts.TryGetValue(overlay, out int n)) counts[overlay] = n + 1;
                else
                {
                    counts[overlay] = 1;
                    order.Add(overlay);
                }
            }
            string placed = string.Join(RepeatedOverlay.Separator, order);
            foreach (var overlay in order)
                if (counts[overlay] > 1) list.Add(new RepeatedOverlay(layout, overlay, counts[overlay], placed));
        }
        catch (JsonException) { }
        return list;
    }

    internal static string? OverlayName(string dashboard)
    {
        var pieces = dashboard.Split('\\', '/');
        string folder = pieces.Length >= 2 ? pieces[^2] : Path.GetFileNameWithoutExtension(pieces[^1]);
        return folder.StartsWith(SimHubDashboards.Prefix, StringComparison.OrdinalIgnoreCase) ? folder : null;
    }

    public void Dispose()
    {
        _settle.Stop();
        _watcher?.Dispose();
    }
}
