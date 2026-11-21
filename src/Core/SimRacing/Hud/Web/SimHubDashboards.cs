using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Core.Services;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Web;

internal static class SimHubDashboards
{
    public const string Prefix = "Velocita - ";
    private const string StampFile = "velocita.stamp";

    public static string Stamp(int port) =>
        string.Create(CultureInfo.InvariantCulture, $"{UpdateService.Current}|{port}|2");

    public static void Install(string simHubFolder, HudHost hud)
    {
        string root = Path.Combine(simHubFolder, "DashTemplates");
        if (!Directory.Exists(root)) return;
        string stamp = Stamp(SimHubEngine.Port);
        HudWebRenderer? pictures = null;
        try
        {
            foreach (var def in HudCatalog.All)
            {
                string folder = Path.Combine(root, Name(def));
                if (ReadStamp(folder) == stamp) continue;
                pictures ??= new HudWebRenderer();
                try { Write(folder, def, stamp, Picture(pictures, def, hud)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); }
            }
            RemoveGone(root);
        }
        finally
        {
            pictures?.Dispose();
        }
    }

    public static string Name(HudWidgetDef def) => Prefix + def.Name;

    private static string? ReadStamp(string folder)
    {
        try { return File.ReadAllText(Path.Combine(folder, StampFile)).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void RemoveGone(string root)
    {
        var known = HudCatalog.All.Select(Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.GetDirectories(root, Prefix + "*"))
        {
            if (known.Contains(Path.GetFileName(folder)) || !File.Exists(Path.Combine(folder, StampFile))) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); }
        }
    }

    private static byte[]? Picture(HudWebRenderer renderer, HudWidgetDef def, HudHost hud)
    {
        var client = new HudWebClient(def.Kind, sample: true) { Width = (int)def.W, Height = (int)def.H };
        var settings = hud.Settings;
        renderer.Render(new[] { client }, new TelemetryFrame(), hud.Race, settings, settings.Layout(settings.ActiveLayout), HudPalette.Of(settings.Appearance), true);
        return client.Offered is { Length: > 0 } png ? png : null;
    }

    internal static void Write(string folder, HudWidgetDef def, string stamp, byte[]? picture)
    {
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, Name(def) + ".djson");
        File.WriteAllText(file, Dashboard(def, SimHubEngine.Port).ToJsonString(), new UTF8Encoding(false));
        File.WriteAllText(file + ".metadata", Metadata(def).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        if (picture is not null)
        {
            File.WriteAllBytes(file + ".png", picture);
            File.WriteAllBytes(file + ".00.png", picture);
        }
        File.WriteAllText(Path.Combine(folder, StampFile), stamp);
    }

    internal static JsonObject Metadata(HudWidgetDef def) => new()
    {
        ["Category"] = "Velocita",
        ["Title"] = Name(def),
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

    internal static JsonObject Dashboard(HudWidgetDef def, int port)
    {
        var page = new JsonObject
        {
            ["$type"] = "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.WebPageItem, SimHub.Plugins",
            ["StartAddress"] = HudWebServer.Url(port, def.Kind),
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
            ["Name"] = def.Name,
            ["RenderingSkip"] = 0,
            ["MinimumRefreshIntervalMS"] = 0.0,
        };
        var screen = new JsonObject
        {
            ["Name"] = "Screen1",
            ["InGameScreen"] = true,
            ["IdleScreen"] = false,
            ["PitScreen"] = false,
            ["ScreenId"] = Id(def.Kind + "/screen").ToString(),
            ["IsForegroundLayer"] = false,
            ["IsBackgroundLayer"] = false,
            ["BackgroundColor"] = "#00000000",
            ["Items"] = new JsonArray(page),
        };
        return new JsonObject
        {
            ["Version"] = 2,
            ["Id"] = Id(def.Kind).ToString(),
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
            ["Metadata"] = Metadata(def),
            ["ShowOnScreenControls"] = false,
            ["IsOverlay"] = true,
            ["EnableClickThroughOverlay"] = true,
            ["EnableOnDashboardMessaging"] = false,
        };
    }

    internal static Guid Id(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes("velocita-simhub/" + key)));
}
