using Core.Hotkeys;

namespace Core.Services;

internal sealed class HotkeyService : IDisposable
{
    private readonly AppServices _app;
    private HotkeyManager? _manager;
    private int _pauseCount;

    public HotkeyService(AppServices app)
    {
        _app = app;
    }

    public void RegisterAll()
    {
        if (_app.Demo || _pauseCount > 0)
            return;

        _manager ??= new HotkeyManager();
        _manager.UnregisterAll();
        var failures = new List<string>();

        void Register(string? hotkey, Action action, string label)
        {
            if (!string.IsNullOrWhiteSpace(hotkey) && !_manager!.TryRegister(hotkey, action, out var error))
                failures.Add($"{label}: {Hotkey.Display(hotkey)} {error}");
        }

        if (AppIdentity.SimHub)
        {
            foreach (var hotkey in PluginHotkeys().Where(h => SimHubKeybind(h.Id) && h.Live?.Invoke() != false))
                Register(hotkey.Get(), hotkey.Invoke, hotkey.Label);
            ReportFailures(failures);
            return;
        }

        var config = _app.Config;
        bool displays = _app.Plugins.IsActive(Plugins.PluginIds.Displays), windows = _app.Plugins.IsActive(Plugins.PluginIds.WindowProfiles);
        foreach (var profile in displays ? config.Displays.Profiles : new List<Config.DisplayProfile>())
        {
            var target = profile;
            Register(profile.Hotkey, () => _ = _app.DisplayProfiles.ApplyAsync(target), "Display setup " + profile.Name);
        }
        foreach (var hotkey in PluginHotkeys().Where(h => h.Live?.Invoke() != false))
            Register(hotkey.Get(), hotkey.Invoke, hotkey.Label);
        if (windows)
        {
            var w = config.WindowProfiles;
            var keys = w.Profiles.Select(p => (p.Hotkey, p.Name)).Concat(w.Groups.Select(g => (g.Hotkey, g.Name)))
                .Where(k => !string.IsNullOrWhiteSpace(k.Hotkey)).GroupBy(k => Hotkey.Display(k.Hotkey));
            foreach (var key in keys)
            {
                string hotkey = key.First().Hotkey!;
                Register(hotkey, () => _app.WindowProfiles.ApplyKey(hotkey), "Window placement " + string.Join(", ", key.Select(k => k.Name)));
            }
        }
        Register(config.Hotkeys.ShowWindow, _app.RequestToggleWindow, $"Show / hide {AppIdentity.Name}");
        ReportFailures(failures);
    }

    public static readonly IReadOnlySet<string> SimHubKeybinds = new HashSet<string>
    {
        "hud.toggle", "hud.dismiss", "hud.layout", "hud.layout.previous",
        "hud.vr.adjust", "hud.vr.recenter", "hud.vr.next", "hud.vr.setting", "hud.vr.left", "hud.vr.right", "hud.vr.up", "hud.vr.down", "hud.vr.brighter", "hud.vr.dimmer",
    };

    public static bool SimHubKeybind(string id) => SimHubKeybinds.Contains(id) || id.StartsWith("hud.widget:", StringComparison.Ordinal);

    private void ReportFailures(List<string> failures)
    {
        var failed = string.Join("\n", failures);
        if (failed.Length > 0 && failed != _lastFailures)
            _app.Notify("Some keybinds didn't register", failed, NoticeKind.Warning, important: true);
        _lastFailures = failed;
    }

    private string _lastFailures = "";

    public void Pause()
    {
        if (_pauseCount++ == 0)
            _manager?.UnregisterAll();
    }

    public void Resume()
    {
        if (_pauseCount > 0 && --_pauseCount == 0)
            RegisterAll();
    }

    public string? Clash(string? hotkey, object? except)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return null;
        var config = _app.Config;
        string shown = Hotkey.Display(hotkey);
        if (config.Displays.Profiles.FirstOrDefault(p => !ReferenceEquals(p, except) && Hotkey.SameKey(p.Hotkey, hotkey)) is { } profile)
            return $"{shown} is already used by the display setup “{profile.Name}”.";
        foreach (var owner in _app.Plugins.All)
            foreach (var plugin in owner.Hotkeys(_app))
            {
                if (!Equals(except, plugin.Id) && Hotkey.SameKey(hotkey, plugin.Get()))
                    return owner.IsActive ? $"{shown} is already used for “{plugin.Label}”." : $"{shown} is already used for “{plugin.Label}” ({owner.Name}, now off).";
            }
        if (except is not Config.WindowProfile and not Config.WindowGroup)
        {
            var w = config.WindowProfiles;
            if (w.Profiles.FirstOrDefault(p => Hotkey.SameKey(p.Hotkey, hotkey)) is { } windowProfile)
                return $"{shown} is already used by the window placement “{windowProfile.Name}”.";
            if (w.Groups.FirstOrDefault(g => Hotkey.SameKey(g.Hotkey, hotkey)) is { } group)
                return $"{shown} is already used by the window placement group “{group.Name}”.";
        }
        if (!Equals(except, HotkeyCatalog.ShowWindowId) && Hotkey.SameKey(hotkey, config.Hotkeys.ShowWindow))
            return $"{shown} is already used for “Show / hide {AppIdentity.Name}”.";
        return null;
    }

    public List<HotkeyEntry> Catalog() => HotkeyCatalog.Build(_app.Config,
        _app.Plugins.Active.SelectMany(p => p.Hotkeys(_app).Select(h => (p.Id, p.Name, h.Page ?? p.Pages.FirstOrDefault()?.Key ?? "settings", h))),
        _app.Plugins.IsActive(Plugins.PluginIds.SimRacing) ? _app.IracingKeys.Actions() : null, SetIracing);

    private void SetIracing(string action, string? hotkey)
    {
        SimRacing.IracingFiles.IracingKey? key = null;
        if (!string.IsNullOrWhiteSpace(hotkey))
            key = SimRacing.IracingFiles.IracingKey.FromHotkey(hotkey)
                ?? throw new InvalidOperationException(hotkey.Contains("NumPad", StringComparison.OrdinalIgnoreCase)
                    ? "Turn Num Lock off, then press the number-pad key again."
                    : $"{Hotkey.Display(hotkey)} can't be set from {AppIdentity.Name} (use letters, numbers, F1–F12, arrows, Page Up / Down, Home, End, Insert, Delete, Space, [ ] = - with Ctrl / Alt / Shift); set that key in iRacing.");
        _app.IracingKeys.Set(action, key);
    }

    public string? GlobalUsing(string? hotkey, object? except) =>
        Catalog().FirstOrDefault(e => e.Global && e.Owner != HotkeyOwner.Iracing && !Equals(e.ClashKey, except) && IsAvailable(e)
            && SimRacing.IracingFiles.IracingKey.FromHotkey(e.Get()) is { } mine && SimRacing.IracingFiles.IracingKey.FromHotkey(hotkey) is { } theirs && mine.SameAs(theirs))?.Label;

    public bool IsAvailable(HotkeyEntry entry) => entry.PluginId is null || _app.Plugins.IsActive(entry.PluginId);

    public string? Change(HotkeyEntry entry, string? hotkey)
    {
        if (entry.Locked is { } locked)
            return locked + ".";
        if (entry.Owner == HotkeyOwner.Iracing)
        {
            try { entry.Set(string.IsNullOrWhiteSpace(hotkey) ? null : hotkey); }
            catch (InvalidOperationException ex) { return ex.Message; }
            if (GlobalUsing(hotkey, entry.ClashKey) is { } mine)
                _app.Notify($"Saved, but {AppIdentity.Name} uses those keys too",
                    $"“{mine}” in {AppIdentity.Name} has the same keys and takes them first, so iRacing won't see them while {AppIdentity.Name} runs.", NoticeKind.Warning);
            return null;
        }
        if (Clash(hotkey, entry.ClashKey) is { } clash)
            return clash;
        entry.Set(string.IsNullOrWhiteSpace(hotkey) ? null : hotkey);
        switch (entry.Owner)
        {
            case HotkeyOwner.WindowProfile: _app.WindowProfiles.RaiseChanged(); break;
            case HotkeyOwner.DisplayProfile: _app.DisplayProfiles.RaiseChanged(); break;
            default: _app.Save(); RegisterAll(); break;
        }
        return null;
    }

    public IEnumerable<Plugins.PluginHotkey> PluginHotkeys() => _app.Plugins.Active.SelectMany(p => p.Hotkeys(_app));

    public void Dispose() => _manager?.Dispose();
}
