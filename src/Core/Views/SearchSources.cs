using System.IO;
using System.Text.Json;
using Core.Services;
using Core.SimRacing.Hud;

namespace Core.Views;

internal sealed record HarvestedLabel(string Title, string Page, string? Section, string? Anchor, string[] Tabs, string Where, string Kind);

internal static class SearchSources
{
    public static string HarvestResource => "search-index-" + AppIdentity.Name + ".json";

    private const string IracingAnchor = "simracing.iracing", DisplaySetupsAnchor = "displays.profiles";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<SearchEntry[]> HarvestedEntries = new(LoadHarvest);

    public static IReadOnlyList<SearchEntry> Harvested => HarvestedEntries.Value;

    private static SearchEntry[] LoadHarvest()
    {
        try
        {
            using var stream = typeof(SearchSources).Assembly.GetManifestResourceStream(HarvestResource);
            if (stream is null) return Array.Empty<SearchEntry>();
            var labels = JsonSerializer.Deserialize<HarvestedLabel[]>(stream, Json);
            return labels?.Select(FromHarvest).ToArray() ?? Array.Empty<SearchEntry>();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return Array.Empty<SearchEntry>();
        }
    }

    public static SearchEntry FromHarvest(HarvestedLabel l) =>
        new(l.Title, "", l.Where, l.Page, l.Anchor, string.Join(' ', l.Tabs.Append(l.Where)))
        {
            Section = l.Section, Tabs = l.Tabs, Locate = l.Title,
        };

    public static IEnumerable<SearchEntry> Live(AppServices app)
    {
        var list = new List<SearchEntry>();
        try { list.AddRange(Keybinds(app)); }
        catch (Exception ex) { App.LogError(ex); }
        if (!AppIdentity.IsTecnica)
        {
            try { list.AddRange(Widgets()); }
            catch (Exception ex) { App.LogError(ex); }
            try { list.AddRange(IracingSettings(app)); }
            catch (Exception ex) { App.LogError(ex); }
        }
        return list;
    }

    private static IEnumerable<SearchEntry> Keybinds(AppServices app)
    {
        foreach (var e in app.Hotkeys.Catalog())
        {
            if (!KeybindHomes.Any(home => e.Id.StartsWith(home, StringComparison.Ordinal)) || !app.Hotkeys.IsAvailable(e)) continue;
            string where = string.Join(" › ", e.Path.Skip(e.Owner == HotkeyOwner.Plugin && e.Path.Count > 1 ? 1 : 0));
            string? anchor = KeybindAnchor(e.Id);
            bool tree = e.Id.StartsWith("hud.", StringComparison.Ordinal) && !e.Id.StartsWith("hud.vr.", StringComparison.Ordinal);
            yield return new SearchEntry(e.Label, e.Note ?? "", where, e.Page, anchor, "keybind keybinds key keys shortcut hotkey " + where)
            {
                Filter = e.Owner is HotkeyOwner.Iracing || tree ? e.Label : null,
                Locate = tree ? Pages.RaceHudSection.ShortKeybindLabel(e.Label) : e.Label,
            };
        }
    }

    private static readonly string[] KeybindHomes = ["hud.", "iracing:", "window:", "windowgroup:", "display:", "fanpreset:"];

    internal static string? KeybindAnchor(string id) =>
        id.StartsWith("hud.vr.", StringComparison.Ordinal) ? Pages.RaceHudSection.VrAnchor
        : id.StartsWith("hud.", StringComparison.Ordinal) ? Pages.RaceHudSection.SettingsAnchor
        : id.StartsWith("iracing:", StringComparison.Ordinal) ? Pages.IracingControllerSection.KeybindsAnchor
        : id.StartsWith("display:", StringComparison.Ordinal) ? DisplaySetupsAnchor
        : null;

    private static IEnumerable<SearchEntry> Widgets() =>
        HudCatalog.All.Select(w => new SearchEntry(w.Name, w.Description, "iRacing Overlay › Widgets", "hud", Pages.RaceHudSection.LayoutsAnchor, "widget overlay hud")
        {
            Locate = w.Name, LocateOptional = true,
        });

    private static IEnumerable<SearchEntry> IracingSettings(AppServices app)
    {
        foreach (var s in Pages.IracingControllerSection.SearchableSettings(app))
        {
            if (string.IsNullOrWhiteSpace(s.Def.Label)) continue;
            yield return new SearchEntry(s.Def.Label, s.Def.Help, "iRacing Controller › " + s.SectionInfo.Name, "simracing", IracingAnchor,
                $"iracing setting ini {s.Key} {s.Def.Section} {Path.GetFileNameWithoutExtension(s.Def.File)}")
            {
                Filter = s.Def.Label + " " + s.Key, Locate = s.Def.Label,
            };
        }
    }
}
