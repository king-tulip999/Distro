using Core.Services;
using Core.Views.Pages;

namespace Core.Plugins.BuiltIn;

internal sealed class DisplaysPlugin : Plugin
{
    public override string Id => PluginIds.Displays;
    public override AppKind App => AppKind.Tecnica;
    public override string Name => "Displays";
    public override string Description => "Switch monitors, arrangement, HDR, wallpapers and audio in one click.";
    public override string Icon => "I.Monitor";
    public override (string Soft, string Deep) Colors => ("SlateSoft", "SlateDeep");
    public override int Order => 20;
    public override string Reads =>
        "Your monitors (arrangement, resolution, refresh rate, HDR), taskbar, wallpapers and audio devices, only while its page is open or a setup is applied.";
    public override string BackgroundWork => "Never. Only its setup keybinds are registered.";
    public override string Changes =>
        "Only when you apply a display setup: monitor arrangement, resolution, HDR, taskbar, wallpapers and default audio device.";

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var k = app.Config.Hotkeys;
        void Step(int delta)
        {
            if (HotkeyCatalog.Step(app.Config.Displays.Profiles, app.DisplayProfiles.Active, delta) is { } profile)
                _ = app.DisplayProfiles.ApplyAsync(profile);
        }
        yield return new PluginHotkey("displays.next", "Next display setup", () => k.NextDisplayProfile, hk => k.NextDisplayProfile = hk, () => Step(1), "Switch setups");
        yield return new PluginHotkey("displays.previous", "Previous display setup", () => k.PreviousDisplayProfile, hk => k.PreviousDisplayProfile = hk, () => Step(-1), "Switch setups");
    }

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage("displays", "Displays", "I.Monitor", (app, window) => new DisplaysPage(app, window)),
    };

    protected override void OnStart(AppServices app) { }

    protected override void OnStop(AppServices app) { }
}
