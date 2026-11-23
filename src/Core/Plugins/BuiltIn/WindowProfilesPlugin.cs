using Core.Processes;
using Core.Services;
using Core.Views;
using Core.Views.Pages;
using Core.WindowProfiles;
using WinForms = System.Windows.Forms;

namespace Core.Plugins.BuiltIn;

internal sealed class WindowProfilesPlugin : Plugin
{
    public const string PageIcon = "I.Triple";

    public override string Id => PluginIds.WindowProfiles;
    public override AppKind App => AppKind.Tecnica;
    public override string Name => "Window placement";
    public override string Description => "Give a program's window an exact size and spot, e.g. a game stretched across triples.";
    public override string Icon => PageIcon;
    public override (string Soft, string Deep) Colors => ("AccentSoft", "AccentText");
    public override int Order => 30;
    public override string Reads => "Which programs start and close (only the ones with an automatic profile), and the size and spot of their windows.";
    public override string BackgroundWork => "Only with automatic profiles: Windows' program start events for those programs, and for about 35 s after a profile is applied a check once a second that the game didn't move its window back. Nothing set up: never.";
    public override string Changes => "The size, position and borders of the windows your profiles name. Nothing else.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PluginIds.WindowProfilesPage, "Window placement", PageIcon, (app, window) => new WindowProfilesPage(app, window)),
    };

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("Window placement", "Force a program's window to an exact size and spot: a game across triple screens, a fixed resolution, borderless.", "Window placement", PluginIds.WindowProfilesPage, null, "resize window size position borderless triple stretch span game resolution custom resolution windowed"),
        new("Apply window placements automatically", "Apply each automatic placement when its program starts.", "Window placement", PluginIds.WindowProfilesPage, WindowProfilesPage.AutoAnchor, "auto process watcher launch start detect"),
        new("Window placement groups", "One keybind or click moves every running program in the group.", "Window placement", PluginIds.WindowProfilesPage, null, "group several multiple programs keybind"),
    };

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var s = app.Config.WindowProfiles;
        if (s.Profiles.Count == 0) return;
        var menu = new WinForms.ToolStripMenuItem("Window placement");
        var running = AppWatcher.Snapshot();
        foreach (var group in s.Groups.Where(g => s.Members(g).Any()))
        {
            var g = group;
            menu.DropDownItems.Add(new WinForms.ToolStripMenuItem(g.Name, null, (_, _) => _ = app.WindowProfiles.ApplyGroupAsync(g))
            {
                ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(g.Hotkey),
                ToolTipText = "Moves every running program in this group",
            });
        }
        if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new WinForms.ToolStripSeparator());
        foreach (var profile in s.Profiles.OrderByDescending(p => WindowProfileService.IsRunning(p, running)))
        {
            var p = profile;
            bool on = WindowProfileService.IsRunning(p, running);
            menu.DropDownItems.Add(new WinForms.ToolStripMenuItem(on ? p.Name : p.Name + " (not running)", null, (_, _) => _ = app.WindowProfiles.ApplyAsync(p))
            {
                ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(p.Hotkey),
                Enabled = on,
                ToolTipText = on ? WindowProfileService.Describe(p) : $"{p.ProcessName} isn't running",
            });
        }
        items.Add(menu);
    }

    protected override void OnStart(AppServices app) => app.WindowProfiles.Refresh();

    protected override void OnStop(AppServices app) => app.WindowProfiles.Refresh();
}
