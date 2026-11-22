using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Core.Config;
using Core.Services;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Web;

internal static class SimHubDashboards
{
    public const string Prefix = "Velocita - ";
    private const string StampFile = "velocita.stamp";

    public static string Stamp(int port) =>
        string.Create(CultureInfo.InvariantCulture, $"{UpdateService.Current}|{port}|2");

    private static string? _root;
    private static readonly HashSet<string> Current = new(StringComparer.OrdinalIgnoreCase);

    public static void Install(string simHubFolder, HudHost hud)
    {
        string root = Path.Combine(simHubFolder, "DashTemplates");
        if (!Directory.Exists(root)) return;
        _root = root;
        Current.Clear();
        Sync(hud);
    }

    public static void Sync(HudHost hud)
    {
        if (_root is not { } root) return;
        var settings = hud.Settings;
        var wanted = Wanted(settings.Layout(settings.ActiveLayout));
        var names = wanted.Select(w => Name(w.Def, w.Slot)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.SetEquals(Current)) return;
        string stamp = Stamp(SimHubEngine.Port);
        HudWebRenderer? pictures = null;
        try
        {
            foreach (var (def, slot) in wanted)
            {
                string name = Name(def, slot);
                if (Current.Contains(name)) continue;
                string folder = Path.Combine(root, name);
                if (ReadStamp(folder) == stamp) continue;
                pictures ??= new HudWebRenderer();
                try { Write(folder, def, slot, stamp, Picture(pictures, def, slot, hud)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); }
            }
            RemoveGone(root, names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); }
        finally
        {
            pictures?.Dispose();
        }
        Current.Clear();
        Current.UnionWith(names);
    }

    internal static List<(HudWidgetDef Def, int Slot)> Wanted(HudLayout? layout)
    {
        var list = HudCatalog.All.Select(d => (d, 1)).ToList();
        if (layout is null) return list;
        foreach (var def in HudCatalog.All)
        {
            int count = Math.Min(HudWebServer.MaxSlot, layout.Widgets.Count(w => string.Equals(w.Kind, def.Kind, StringComparison.OrdinalIgnoreCase)));
            for (int slot = 2; slot <= count; slot++) list.Add((def, slot));
        }
        return list;
    }

    public static string Name(HudWidgetDef def) => Name(def, 1);

    public static string Name(HudWidgetDef def, int slot) => Prefix + Title(def, slot);

    private static string Title(HudWidgetDef def, int slot) => slot > 1 ? def.Name + " " + slot.ToString(CultureInfo.InvariantCulture) : def.Name;

    private static string Key(HudWidgetDef def, int slot) => slot > 1 ? def.Kind + "/" + slot.ToString(CultureInfo.InvariantCulture) : def.Kind;

    private static string? ReadStamp(string folder)
    {
        try { return File.ReadAllText(Path.Combine(folder, StampFile)).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void RemoveGone(string root, HashSet<string> known)
    {
        foreach (var folder in Directory.GetDirectories(root, Prefix + "*"))
        {
            if (known.Contains(Path.GetFileName(folder)) || !File.Exists(Path.Combine(folder, StampFile))) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); }
        }
    }

    private static byte[]? Picture(HudWebRenderer renderer, HudWidgetDef def, int slot, HudHost hud)
    {
        var client = new HudWebClient(def.Kind, sample: true, slot) { Width = (int)def.W, Height = (int)def.H };
        var settings = hud.Settings;
        renderer.Render(new[] { client }, new TelemetryFrame(), hud.Race, settings, settings.Layout(settings.ActiveLayout), HudPalette.Of(settings.Appearance), true);
        return client.Offered is { Length: > 0 } png ? png : null;
    }

    internal static void Write(string folder, HudWidgetDef def, int slot, string stamp, byte[]? picture)
    {
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, Name(def, slot) + ".djson");
        File.WriteAllText(file, Dashboard(def, SimHubEngine.Port, slot).ToJsonString(), new UTF8Encoding(false));
        File.WriteAllText(file + ".metadata", Metadata(def, slot).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        if (picture is not null)
        {
            File.WriteAllBytes(file + ".png", picture);
            File.WriteAllBytes(file + ".00.png", picture);
        }
        File.WriteAllText(Path.Combine(folder, StampFile), stamp);
    }

    internal static JsonObject Metadata(HudWidgetDef def, int slot = 1) => new()
    {
        ["Category"] = "Velocita",
        ["Title"] = Name(def, slot),
        ["Description"] = def.Description,
        ["Author"] = "Velocita",
        ["ScreenCount"] = 1.0,
        ["InGameScreensIndexs"] = new JsonArray(0),
        ["IdleScreensIndexs"] = new JsonArray(),
        ["MainPreviewIndex"] = 0,
        ["IsOverlay"] = true,
        ["Width"] = def.W,
        ["Height"] = def.H,
        ["OverlaySizeWarning"] = false,
        ["MetadataVersion"] = 2.0,
        ["EnableOnDashboardMessaging"] = false,
        ["PitScreensIndexs"] = new JsonArray(),
    };

    internal static JsonObject Dashboard(HudWidgetDef def, int port, int slot = 1)
    {
        var page = new JsonObject
        {
            ["$type"] = "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.WebPageItem, SimHub.Plugins",
            ["StartAddress"] = HudWebServer.Url(port, def.Kind, slot),
            ["AllowTransparency"] = true,
            ["ClickThrough"] = true,
            ["BackgroundColor"] = "#00FFFFFF",
            ["Height"] = def.H,
            ["Left"] = 0.0,
            ["Top"] = 0.0,
            ["Visible"] = true,
            ["BlinkPhasisInverted"] = false,
            ["Width"] = def.W,
            ["IsFreezed"] = false,
            ["Name"] = Title(def, slot),
            ["RenderingSkip"] = 0,
            ["MinimumRefreshIntervalMS"] = 0.0,
        };
        var screen = new JsonObject
        {
            ["Name"] = "Screen1",
            ["InGameScreen"] = true,
            ["IdleScreen"] = false,
            ["PitScreen"] = false,
            ["ScreenId"] = Id(Key(def, slot) + "/screen").ToString(),
            ["IsForegroundLayer"] = false,
            ["IsBackgroundLayer"] = false,
            ["BackgroundColor"] = "#00000000",
            ["Items"] = new JsonArray(page),
        };
        return new JsonObject
        {
            ["Version"] = 2,
            ["Id"] = Id(Key(def, slot)).ToString(),
            ["BaseHeight"] = (int)def.H,
            ["BaseWidth"] = (int)def.W,
            ["BackgroundColor"] = "#00000000",
            ["Screens"] = new JsonArray(screen),
            ["SnapToGrid"] = true,
            ["HideLabels"] = false,
            ["ShowForeground"] = true,
            ["ForegroundOpacity"] = 50.0,
            ["ShowBackground"] = true,
            ["BackgroundOpacity"] = 50.0,
            ["ShowBoundingRectangles"] = false,
            ["GridSize"] = 10,
            ["Images"] = new JsonArray(),
            ["Metadata"] = Metadata(def, slot),
            ["ShowOnScreenControls"] = false,
            ["IsOverlay"] = true,
            ["EnableClickThroughOverlay"] = true,
            ["EnableOnDashboardMessaging"] = false,
        };
    }

    internal static Guid Id(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes("velocita-simhub/" + key)));
}
