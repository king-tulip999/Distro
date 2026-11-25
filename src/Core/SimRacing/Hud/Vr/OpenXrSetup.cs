using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Core.SimRacing.Hud.Vr;

internal static class OpenXrSetup
{
    private const string Root = @"SOFTWARE\Khronos\OpenXR\1";
    private const string ImplicitKey = Root + @"\ApiLayers\Implicit";
    private const int MaxManifestBytes = 64 * 1024;

    internal sealed record Layer(string Manifest, bool MachineWide, bool Enabled, string? Name);

    public static List<Layer> Layers()
    {
        var found = new List<Layer>();
        Read(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), true, found);
        Read(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64), false, found);
        return found;
    }

    private static void Read(RegistryKey hive, bool machineWide, List<Layer> found)
    {
        try
        {
            using (hive)
            using (var key = hive.OpenSubKey(ImplicitKey))
            {
                if (key is null) return;
                foreach (string manifest in key.GetValueNames())
                    found.Add(new Layer(manifest, machineWide, key.GetValue(manifest) is int on && on == 0, NameIn(ReadSmall(manifest), "api_layer")));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    public static string? RuntimeManifest()
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(Root);
            return key?.GetValue("ActiveRuntime") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    public static string? RuntimeName(string manifest) => NameIn(ReadSmall(manifest), "runtime");

    private static string? ReadSmall(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaxManifestBytes ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    internal static string? NameIn(string? json, string section)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(section, out var part) && part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    internal static string ResultName(int code) => code switch
    {
        0 => "success",
        -1 => "validation failure",
        -2 => "runtime failure",
        -3 => "out of memory",
        -8 => "feature unsupported",
        -12 => "handle invalid",
        -17 => "session lost",
        -23 => "layer invalid",
        -24 => "layer limit exceeded",
        -25 => "swapchain rect invalid",
        -26 => "swapchain format unsupported",
        -32 => "pose invalid",
        _ => "code " + code,
    };

    public static string LayerLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velocita", "OpenXR", "layer.log");

    private const int LayerLogLinesShown = 30;

    private static bool IracingRunning()
    {
        try
        {
            var found = System.Diagnostics.Process.GetProcessesByName(IracingFiles.IracingPaths.SimProcess);
            foreach (var p in found) p.Dispose();
            return found.Length > 0;
        }
        catch (InvalidOperationException) { return false; }
    }

    internal static IEnumerable<string> LayerLogLines() => LayerLogLines(ReadLayerLog());

    private const string OutsideMarker = "Loaded outside iRacing in ";

    internal static List<string> OutsideLoads(string[]? lines)
    {
        var last = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lines is null) return new List<string>();
        foreach (string line in lines)
        {
            int at = line.IndexOf(OutsideMarker, StringComparison.Ordinal);
            if (at < 0) continue;
            int start = at + OutsideMarker.Length;
            int end = line.IndexOf(':', start);
            if (end <= start) continue;
            string exe = line[start..end].Trim();
            last[exe] = at >= 19 ? line[..19] : "";
        }
        return last.Select(p => p.Value.Length > 0 ? $"{p.Key} (last {p.Value})" : p.Key).ToList();
    }

    internal static IEnumerable<string> LayerLogLines(string[]? lines)
    {
        if (lines is null)
        {
            yield return "- Layer log: none yet (the layer has never loaded in iRacing since this version)";
            yield break;
        }
        var kept = lines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(LayerLogLinesShown).ToList();
        yield return $"- Layer log, last {kept.Count} lines:";
        foreach (string line in kept) yield return "  - " + line.Trim();
    }

    private static string[]? ReadLayerLog()
    {
        try
        {
            using var stream = new FileStream(LayerLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxManifestBytes * 2) stream.Seek(-MaxManifestBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static IEnumerable<string> ReportLines(HudVr? vr, bool settingOn)
    {
        yield return $"- VR widgets: {(settingOn ? "on" : "off")}{(vr is null ? ", overlay not running" : vr.On ? "" : ", waiting for iRacing")}";
        if (vr is not null)
            yield return $"- SteamVR: {(vr.SteamVrConnected ? "connected" : vr.SteamVrRunning ? "running, not connected" + (vr.SteamVrProblem is { } p ? $" ({p})" : "") : "not running")}";
        if (vr?.IracingAsAdmin == true) yield return "- iRacing is running as administrator";
        string? runtime = RuntimeManifest();
        yield return runtime is null ? "- OpenXR runtime: none set" : $"- OpenXR runtime: {RuntimeName(runtime) ?? "unnamed"} ({runtime})";
        var layers = Layers();
        yield return layers.Count == 0 ? "- OpenXR layers: none" : "- OpenXR layers, in load order (game side first):";
        foreach (var layer in layers)
            yield return $"  - {layer.Name ?? "unreadable manifest"}: {(layer.MachineWide ? "machine-wide" : "per user")}{(layer.Enabled ? "" : ", turned off")} ({layer.Manifest})";
        yield return $"- Admin setup for iRacing: {(OpenXrLayer.MachineInstalled ? OpenXrLayer.MachineCurrent ? "set up" : "set up, out of date" : "not set up")}";
        yield return $"- iRacing running now: {(IracingRunning() ? "yes" : "no")}";
        yield return $"- Per-user layer registered now: {(OpenXrLayer.Registered ? "yes" : "no")} (only while iRacing runs with VR widgets on)";
        if (OpenXrLayer.LeftoverRemoved) yield return "- A leftover registration from an earlier run was removed at start";
        string[]? log = ReadLayerLog();
        var outside = OutsideLoads(log);
        yield return outside.Count == 0 ? "- Loaded by other games: none logged" : "- Loaded by other games: " + string.Join(", ", outside);
        foreach (string line in LayerLogLines(log)) yield return line;
        if (vr?.LayerReport is not { } r)
        {
            yield return "- Velocita's layer inside iRacing: not seen since Velocita started";
            yield break;
        }
        long ago = Math.Max(0, Environment.TickCount64 - r.Tick) / 1000;
        yield return $"- Velocita's layer inside iRacing: last seen {ago} s ago, runtime “{r.Runtime}”";
        yield return $"  - Widgets in the last frame: {r.WidgetsSent}; frames with widgets: {r.FramesWithWidgets}; rejected by the headset: {r.FramesRejected}{(r.Rejected ? " (widgets stopped for this session)" : "")}";
        yield return $"  - Last frame with widgets: {ResultName(r.EndFrameResult)}; last widget surface: {ResultName(r.SwapchainResult)}";
        yield return $"  - Direct3D 11 device: {(r.HasDevice ? "yes" : "no")}; picture format: {(r.HasFormat ? r.Format.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none found")}; curved widgets: {(r.Cylinder ? "yes" : "no")}; layer limit: {r.MaxLayers}";
    }
}
