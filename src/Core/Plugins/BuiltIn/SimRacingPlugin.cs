using Core.Services;
using Core.SimRacing;
using Core.SimRacing.Hud;
using WinForms = System.Windows.Forms;
using Core.Views.Pages;

namespace Core.Plugins.BuiltIn;

internal sealed class SimRacingPlugin : Plugin
{
    public override string Id => PluginIds.SimRacing;
    public override AppKind App => AppKind.Velocita;
    public override string Name => "Sim Racing Tools";
    public override bool ShowsPage(AppServices app, string key) =>
        key == PluginIds.RaceOverlayPage ? app.Config.SimRacing.Hud.ShowPage : app.Config.SimRacing.ShowPage;
    public override string Description => "iRacing overlay, setups, fuel, FOV and iRacing settings.";
    public override string Icon => "I.FormulaWheel";
    public override (string Soft, string Deep) Colors => ("CoralSoft", "CoralDeep");
    public override int Order => 10;
    public override string Reads =>
        "With the iRacing Overlay on: iRacing's live telemetry (its shared memory, read only) while iRacing runs, and whether iRacing is running. With a keybind set to a wheel or button box button: that controller's button presses. iRacing's settings files and setups folder, and recent setups in Downloads, while its page is open. With “Car pictures in your paint” on: your paint files in iRacing's paint folder and the driver details in your newest telemetry files. With “Launch when iRacing starts” on: whether iRacing is running.";
    public override string BackgroundWork =>
        "With the iRacing Overlay switched on: waits for iRacing to start (process events), then reads its telemetry about 60 times a second and draws the widgets at their own rates; while iRacing is closed it only waits. Only with “Launch when iRacing starts” on for Trading Paints or Garage 61: watches for iRacing starting (process events, no polling). With “Car pictures in your paint” on: after iRacing opens or a session ends, asks iRacing to draw your car in each paint that changed (its own local service) and keeps the pictures in the Velocita folder. While the Setup Manager is open on screen: file events for the setups folder and Downloads. Otherwise never.";
    public override string Changes =>
        "The iRacing Overlay draws its own windows on top of the game; nothing is injected into iRacing or written to it. Only when you ask: iRacing's settings files when you save in iRacing Controller or write the FOV (backed up first), setup files and folders you import, rename, move, delete (to the Recycle Bin) or export in the Setup Manager, and starting or closing Trading Paints / Garage 61.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PluginIds.SimRacingPage, "Sim Racing Tools", "I.FormulaWheel", (app, window) => new SimRacingPage(app, window), Sections: SimRacingPage.SectionsFor),
        new PluginPage(PluginIds.RaceOverlayPage, "iRacing Overlay", "I.RaceOverlay", (app, window) => new RaceHudPage(app, window)),
    };

    private const string OverlayPage = PluginIds.RaceOverlayPage;

    public HudHost? Hud { get; private set; }

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var h = app.Config.SimRacing.Hud;
        yield return new PluginHotkey("hud.toggle", "Show / hide the iRacing Overlay", () => h.ToggleHotkey, k => h.ToggleHotkey = k, () => Hud?.ToggleHidden(), "iRacing Overlay", "Ctrl+Alt+H", OverlayPage);
        yield return new PluginHotkey("hud.edit", "Arrange the iRacing Overlay", () => h.EditHotkey, k => h.EditHotkey = k, () =>
        {
            if (Hud is null) return;
            if (Hud.Editing) Hud.EndEdit(keep: true); else Hud.BeginEdit();
        }, "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.quickedit", "iRacing Overlay in-game editor", () => h.QuickEditHotkey, k => h.QuickEditHotkey = k, () =>
        {
            if (Hud is { } hud) Core.Views.HudQuickEditor.Toggle(app, hud);
        }, "iRacing Overlay", Config.RaceHudSettings.QuickEditDefault, OverlayPage);
        yield return new PluginHotkey("hud.layout", "Next iRacing Overlay layout", () => h.NextLayoutHotkey, k => h.NextLayoutHotkey = k, () => Hud?.NextLayout(), "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.layout.previous", "Previous iRacing Overlay layout", () => h.PreviousLayoutHotkey, k => h.PreviousLayoutHotkey = k, () => Hud?.PreviousLayout(), "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.night", "Night mode on / off", () => h.NightHotkey, k => h.NightHotkey = k, () => Hud?.ToggleNight(), "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.dismiss", "Dismiss iRacing Overlay alerts", () => h.DismissHotkey, k => h.DismissHotkey = k, () => Hud?.DismissAlerts(), "iRacing Overlay", Page: OverlayPage);
        {
            const string vr = "iRacing Overlay in VR";
            bool Adjusting() => h.VrOverlays && Hud?.VrAdjusting == true;
            yield return new PluginHotkey("hud.vr.adjust", "Edit VR widgets in the headset", () => h.VrAdjustHotkey, k => h.VrAdjustHotkey = k, () => Hud?.ToggleVrAdjust(), vr, Config.RaceHudSettings.VrAdjustDefault, OverlayPage, () => h.VrOverlays);
            yield return new PluginHotkey("hud.vr.recenter", "Recenter VR widgets", () => h.VrRecenterHotkey, k => h.VrRecenterHotkey = k, () => Hud?.RecenterVr(), vr, Config.RaceHudSettings.VrRecenterDefault, OverlayPage, () => h.VrOverlays);
            yield return new PluginHotkey("hud.vr.brighter", "VR widgets brighter", () => h.VrBrighterHotkey, k => h.VrBrighterHotkey = k, () => Hud?.StepVrBrightness(1), vr, null, OverlayPage, () => h.VrOverlays);
            yield return new PluginHotkey("hud.vr.dimmer", "VR widgets dimmer", () => h.VrDimmerHotkey, k => h.VrDimmerHotkey = k, () => Hud?.StepVrBrightness(-1), vr, null, OverlayPage, () => h.VrOverlays);
            yield return new PluginHotkey("hud.vr.next", "Next VR widget (while adjusting)", () => h.VrNextHotkey, k => h.VrNextHotkey = k, () => Hud?.VrNext(), vr, Config.RaceHudSettings.VrNextDefault, OverlayPage, Adjusting);
            yield return new PluginHotkey("hud.vr.setting", "Next VR setting (while adjusting)", () => h.VrSettingHotkey, k => h.VrSettingHotkey = k, () => Hud?.VrNextSetting(), vr, Config.RaceHudSettings.VrSettingDefault, OverlayPage, Adjusting);
            yield return new PluginHotkey("hud.vr.left", "Nudge left (while adjusting)", () => h.VrLeftHotkey, k => h.VrLeftHotkey = k, () => Hud?.VrNudge(-1, 0), vr, Config.RaceHudSettings.VrLeftDefault, OverlayPage, Adjusting);
            yield return new PluginHotkey("hud.vr.right", "Nudge right (while adjusting)", () => h.VrRightHotkey, k => h.VrRightHotkey = k, () => Hud?.VrNudge(1, 0), vr, Config.RaceHudSettings.VrRightDefault, OverlayPage, Adjusting);
            yield return new PluginHotkey("hud.vr.up", "Nudge up (while adjusting)", () => h.VrUpHotkey, k => h.VrUpHotkey = k, () => Hud?.VrNudge(0, 1), vr, Config.RaceHudSettings.VrUpDefault, OverlayPage, Adjusting);
            yield return new PluginHotkey("hud.vr.down", "Nudge down (while adjusting)", () => h.VrDownHotkey, k => h.VrDownHotkey = k, () => Hud?.VrNudge(0, -1), vr, Config.RaceHudSettings.VrDownDefault, OverlayPage, Adjusting);
        }
        foreach (var layout in h.Layouts)
        {
            string group = WidgetGroup(layout);
            foreach (var widget in layout.Widgets)
            {
                var w = widget;
                if (HudCatalog.Find(w.Kind) is null) continue;
                yield return new PluginHotkey(WidgetHotkeyId(layout, w), WidgetName(layout, w), () => w.Hotkey, k => w.Hotkey = k, () => Hud?.ToggleWidget(w), group, Page: OverlayPage);
            }
        }
    }

    public static string WidgetGroup(Config.HudLayout layout) => $"Show / hide widgets in “{layout.Name}”";

    public static string WidgetHotkeyId(Config.HudLayout layout, Config.HudWidgetConfig widget) => $"hud.widget:{layout.Name}:{widget.Id}";

    public static string WidgetName(Config.HudLayout layout, Config.HudWidgetConfig widget)
    {
        string name = HudCatalog.Find(widget.Kind)?.Name ?? widget.Kind;
        int index = 0;
        foreach (var other in layout.Widgets)
        {
            if (ReferenceEquals(other, widget)) break;
            if (other.Kind == widget.Kind) index++;
        }
        return index > 0 ? $"{name} {index + 1}" : name;
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var h = app.Config.SimRacing.Hud;
        var item = new WinForms.ToolStripMenuItem("Show iRacing Overlay") { Checked = h.Enabled, ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(h.ToggleHotkey) };
        item.Click += (_, _) => Hud?.SetEnabled(!h.Enabled);
        items.Add(item);
        if (!h.VrOverlays) return;
        items.Add(new WinForms.ToolStripMenuItem("Edit VR widgets in the headset", null, (_, _) => Hud?.ToggleVrAdjust()) { ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(h.VrAdjustHotkey) });
        items.Add(new WinForms.ToolStripMenuItem("Recenter VR widgets", null, (_, _) => Hud?.RecenterVr()) { ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(h.VrRecenterHotkey) });
    }

    public CompanionApp? TradingPaints { get; private set; }

    public CompanionApp? Garage61 { get; private set; }

    public LiveryPictures? Liveries { get; private set; }

    protected override void OnStart(AppServices app)
    {
        if (AppIdentity.SimHub)
        {
            Hud = new HudHost(app);
            Hud.SetActive(true);
            return;
        }
        Liveries = new LiveryPictures(app);
        Liveries.SetActive(LiveryPictures.On(app));
        Hud = new HudHost(app);
        Hud.SetActive(true);
        TradingPaints = new CompanionApp(app, CompanionApp.TradingPaints, () => app.Config.SimRacing.TradingPaints);
        Garage61 = new CompanionApp(app, CompanionApp.Garage61, () => app.Config.SimRacing.Garage61);
        TradingPaints.Start();
        Garage61.Start();
    }

    protected override void OnStop(AppServices app)
    {
        Hud?.Dispose();
        Hud = null;
        Liveries?.SetActive(false);
        Liveries = null;
        TradingPaints?.Stop();
        Garage61?.Stop();
        TradingPaints = Garage61 = null;
    }
}
