using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Core.Config;
using Core.Plugins;
using Core.Plugins.BuiltIn;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Web;
using Core.Theme;
using Core.Views;

namespace Core.Services;

internal static class SimHubEngine
{
    public const int Port = 8648;
    public const string ParentArg = "--parent";
    public const string FolderArg = "--simhub";
    public const string SettingsArg = "--settings";

    private static AppServices? _services;
    private static HudHost? _hud;
    private static SimHubUpdater? _updater;
    private static SimHubTray? _tray;

    public static HudHost? Hud => _hud;

    public static SimHubUpdater? Updater => _updater;

    public static SimHubOverlayLayouts? OverlayLayouts { get; private set; }

    public const string EmbedFile = "embed.txt";
    public const string OffFile = "off.txt";

    private static string EmbedEventName => $@"Local\{AppIdentity.DataName}.Embed";
    private static string DetachEventName => $@"Local\{AppIdentity.DataName}.Detach";
    private static string ClosingEventName => $@"Local\{AppIdentity.DataName}.Closing";
    private static string ResumedEventName => $@"Local\{AppIdentity.DataName}.Resumed";
    private static string EditVrEventName => $@"Local\{AppIdentity.DataName}.EditVr";
    private static string RecenterVrEventName => $@"Local\{AppIdentity.DataName}.RecenterVr";
    private static string NextLayoutEventName => $@"Local\{AppIdentity.DataName}.NextLayout";
    private static string PreviousLayoutEventName => $@"Local\{AppIdentity.DataName}.PreviousLayout";
    private static readonly TimeSpan ParentExitGrace = TimeSpan.FromSeconds(10);
    private static bool _watchingParent;
    private static bool _parentHandled;
    private static DispatcherTimer? _parentGrace;

    public static void Run(App app, string[] args)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!SingleInstance.TryAcquire())
        {
            if (Arg(args, FolderArg) is null || args.Contains(SettingsArg, StringComparer.OrdinalIgnoreCase)) SingleInstance.SignalExisting();
            app.Shutdown();
            return;
        }
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            App.LogError(e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            App.LogError(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) App.LogError(ex);
            SimRacing.Hud.Vr.OpenXrLayer.UnregisterAfterCrash();
        };

        PluginManager.DiscoverEarly();
        Controls.ClickGuard.Install();
        MenuFollow.Install();
        Motion.InstallPress();
        Ui.InstallDropDownWheel();
        Controls.SliderFace.InstallDragAnywhere();
        TipPlacement.Install();
        TextBalance.Install();

        var config = ConfigStore.Load(out _);
        if (Arg(args, ShotArg) is not null) ConfigStore.ReadOnly = true;
        Prepare(config.SimRacing.Hud);
        config.Ui.Theme = AppTheme.Glow;
        Grr.GrrResources.Install();
        Themes.Register(Grr.GrrTheme.Spec);
        foreach (var themeSet in ThemeSets.All)
            Themes.SetPick(themeSet, themeSet.Get(config.Ui));
        ThemeManager.Apply(config.Ui.Theme);

        _services = new AppServices(false, config);
        if (_services.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin racing)
        {
            racing.Start(_services);
            _hud = racing.Hud;
        }
        if (Arg(args, ShotArg) is null)
        {
            _services.Hotkeys.RegisterAll();
            if (Arg(args, ParentArg) is not null) _tray = new SimHubTray(_services, () => _hud, ShowSettings);
        }

        if (Arg(args, FolderArg) is { } folder)
        {
            if (_hud is not null) SimHubDashboards.Install(folder, _hud);
            if (Arg(args, ParentArg) is not null && Arg(args, ShotArg) is null)
            {
                _updater = new SimHubUpdater(folder);
                _updater.Start();
            }
            OverlayLayouts = new SimHubOverlayLayouts(folder);
        }

        SingleInstance.Listen(() => app.Dispatcher.BeginInvoke(new Action(ShowSettings)));
        ListenForEmbed(app);
        Listen(app, DetachEventName, "SimHub detach", SimHubEmbed.Detach);
        Listen(app, ClosingEventName, "SimHub closing", SimHubClosing);
        Listen(app, ResumedEventName, "SimHub resumed", StopParentGrace);
        Listen(app, EditVrEventName, "SimHub edit VR", () => _hud?.ToggleVrAdjust());
        Listen(app, RecenterVrEventName, "SimHub recenter VR", () => _hud?.RecenterVr());
        Listen(app, NextLayoutEventName, "SimHub next layout", () => _hud?.NextLayout());
        Listen(app, PreviousLayoutEventName, "SimHub previous layout", () => _hud?.PreviousLayout());
        SingleInstance.ListenForQuit(() => app.Dispatcher.BeginInvoke(new Action(App.Quit)));
        if (int.TryParse(Arg(args, ParentArg), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parent)) WatchParent(app, parent);
        if (args.Contains(SettingsArg, StringComparer.OrdinalIgnoreCase)) ShowSettings();
        if (Arg(args, ShotArg) is { } shots && _services is not null) SimHubWindow.Shoot(_services, shots);
    }

    public const string ShotArg = "--shot";

    internal static void Prepare(RaceHudSettings hud)
    {
        hud.Enabled = false;
        hud.WebOverlays = true;
        hud.WebRemote = false;
        hud.WebPort = Port;
        hud.Interactive = false;
    }

    public static void TurnOff()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.Folder);
            File.WriteAllText(Path.Combine(ConfigStore.Folder, OffFile), "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError(ex);
            return;
        }
        SimHubEmbed.Detach();
        App.Quit();
    }

    private static void ShowSettings()
    {
        if (_services is null || _hud is null) return;
        SimHubWindow.Open(_services);
    }

    private static void ListenForEmbed(App app)
    {
        var embed = new EventWaitHandle(false, EventResetMode.AutoReset, EmbedEventName);
        var thread = new Thread(() =>
        {
            while (embed.WaitOne())
            {
                if (ReadEmbedParent() is { } parent)
                    app.Dispatcher.BeginInvoke(new Action(() => { if (_services is { } services) SimHubEmbed.Attach(services, parent); }));
            }
        })
        { IsBackground = true, Name = "SimHub embed" };
        thread.Start();
    }

    private static void Listen(App app, string eventName, string threadName, Action onUi)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
                app.Dispatcher.BeginInvoke(onUi);
        })
        { IsBackground = true, Name = threadName };
        thread.Start();
    }

    private static void SimHubClosing()
    {
        SimHubEmbed.Detach();
        if (!_watchingParent || _parentHandled || _parentGrace is not null) return;
        _parentGrace = new DispatcherTimer { Interval = ParentExitGrace };
        _parentGrace.Tick += OnParentGraceOver;
        _parentGrace.Start();
    }

    private static void OnParentGraceOver(object? sender, EventArgs e)
    {
        StopParentGrace();
        ParentClosed();
    }

    private static void StopParentGrace()
    {
        if (_parentGrace is not { } grace) return;
        grace.Stop();
        grace.Tick -= OnParentGraceOver;
        _parentGrace = null;
    }

    private static IntPtr? ReadEmbedParent()
    {
        try
        {
            string text = File.ReadAllText(Path.Combine(ConfigStore.Folder, EmbedFile)).Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long handle) && handle != 0 ? new IntPtr(handle) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WatchParent(App app, int pid)
    {
        Process parent;
        try { parent = Process.GetProcessById(pid); }
        catch (ArgumentException)
        {
            app.Dispatcher.BeginInvoke(new Action(App.Quit), DispatcherPriority.Background);
            return;
        }
        _watchingParent = true;
        Task.Run(async () =>
        {
            using (parent)
                await parent.WaitForExitAsync().ConfigureAwait(false);
            await app.Dispatcher.BeginInvoke(new Action(ParentClosed));
        });
    }

    private static void ParentClosed()
    {
        if (_parentHandled) return;
        _parentHandled = true;
        SimHubEmbed.Detach();
        if (_updater?.InstallOnExit() != true) App.Quit();
    }

    public static void Stop()
    {
        SimHubEmbed.Detach();
        _tray?.Dispose();
        _tray = null;
        _updater?.Stop();
        _updater = null;
        if (_services is not { } services) return;
        _services = null;
        _hud = null;
        services.Dispose();
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
