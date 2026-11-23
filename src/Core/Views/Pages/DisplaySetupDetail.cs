using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Audio;
using Core.Config;
using Core.Controls;
using Core.Services;

namespace Core.Views.Pages;

internal sealed class DisplaySetupDetail : StackPanel
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly DisplayProfile _setup;
    private readonly Func<bool> _hasPending;
    private readonly Action _rebuild;
    private readonly DisplayProfileEditor _editor;
    private readonly ListDetail.Pending _pending;
    private readonly TextBlock _error = new() { Style = Ui.Res<Style>("Caption"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

    private DisplaySnapshot? _moved;

    public bool Dirty => _pending.Showing;

    public FrameworkElement? Changes { get; }

    public DisplayProfile Setup => _setup;

    public void MarkEdited() => _pending.Show();

    public DisplaySetupDetail(AppServices app, MainWindow window, DisplayProfile setup, bool isActive, Func<bool> hasPending, Action rebuild, FrameworkElement saveCurrent)
    {
        _app = app;
        _window = window;
        _setup = setup;
        _hasPending = hasPending;
        _rebuild = rebuild;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerDeep");

        var marks = new List<FrameworkElement>();
        if (isActive) marks.Add(Ui.Chip("Active", "AccentSoft", "AccentText"));

        var actions = new List<FrameworkElement>
        {
            saveCurrent,
            Ui.LabeledButton("I.Trash", "Delete", () => _ = DeleteAsync(), "DangerButton"),
        };
        var more = Ui.IconButton("I.More", "More for this setup", () => { });
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            Ui.DropBelow(menu);
            var update = new MenuItem { Header = "Overwrite with current", ToolTip = "Save the setup that's on right now (layout, wallpapers and audio devices) into this one" };
            update.Click += (_, _) => UpdateToCurrent();
            menu.Items.Add(update);
            menu.IsOpen = true;
        };
        actions.Add(more);
        var changes = app.DisplayProfiles.Preview(setup);
        if (!isActive || changes.Count > 0)
        {
            var apply = Ui.LabeledButton("I.Zap", isActive ? "Reapply" : "Apply", () => _ = app.DisplayProfiles.ApplyAsync(setup), "PrimaryButton");
            apply.ToolTip = isActive ? "Something changed since it was applied" : string.Join("\n", changes.Select(c => c.Text));
            actions.Add(apply);
        }
        Children.Add(ListDetail.Head(setup.Name, () => _ = RenameAsync(), marks, actions));

        if (setup.Layout is null)
            Children.Add(ListDetail.Preview(new TextBlock { Text = "Leaves the arrangement as it is", Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 130));
        else
            Children.Add(Arrangement(setup.Layout));

        var facts = Summary(setup).ToList();
        if (facts.Count > 0) Changes = ListDetail.SideFacts("What applying it changes", facts);

        Children.Add(ListDetail.Divider());
        _editor = new DisplayProfileEditor(app, setup, hasPending());
        Children.Add(_editor.Columns);
        _pending = new ListDetail.Pending($"Unsaved changes to “{setup.Name}”", "Save changes", rebuild, Save);
        Children.Add(_pending.Bar);
        Children.Add(_error);
        ListDetail.TrackEdits(_editor.Columns, _pending.Show);
        _editor.Edited += _pending.Show;
    }

    private static IEnumerable<(string, string)> Summary(DisplayProfile p)
    {
        if (p.Hdr != HdrMode.Unchanged) yield return ("HDR", p.Hdr == HdrMode.On ? "On" : "Off");
        if (p.Taskbar != TaskbarMode.Unchanged) yield return ("Taskbar", p.Taskbar == TaskbarMode.MainOnly ? "Main display only" : "Every display");
        if (p.Wallpapers.Count > 0) yield return ("Wallpaper", "Set on " + SimRacing.Race.RaceFormat.Count(p.Wallpapers.Count, "display"));
        if (p.Audio?.PlaybackName is { } speakers) yield return ("Output", speakers);
        if (p.Audio?.RecordingName is { } mic) yield return ("Microphone", mic);
        if (p.Volume is { } vol) yield return ("Volume", $"{vol}%");
        if (p.Apps.Count > 0) yield return ("Opens", string.Join(", ", p.Apps.Select(a => Display.ProfileApps.DisplayName(a)).Where(n => n.Length > 0)));
        if (p.CloseAppsOnLeave && p.Apps.Count > 0) yield return ("When left", "Closes its apps");
    }

    private FrameworkElement Arrangement(DisplaySnapshot layout)
    {
        Dictionary<string, int> numbers;
        try { numbers = _app.Displays.GetMonitors().Where(m => m.Number > 0).ToDictionary(m => m.Key, m => m.Number); }
        catch { numbers = new Dictionary<string, int>(); }
        var origins = new Dictionary<string, (int X, int Y)>();
        var tiles = _app.Displays.Describe(layout)
            .GroupBy(m => (m.X, m.Y, m.Width, m.Height))
            .OrderBy(g => g.Key.X).ThenBy(g => g.Key.Y)
            .Select((g, i) =>
            {
                string key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                origins[key] = (g.Key.X, g.Key.Y);
                var on = g.Select(m => numbers.TryGetValue(m.Key, out int n) ? n : 0).Where(n => n > 0).ToList();
                var first = g.First();
                return new ArrangeTile
                {
                    Key = key,
                    Number = on.Count > 0 ? string.Join("+", on) : SimRacing.Race.RaceFormat.Missing,
                    Name = string.Join(" + ", g.Select(m => m.Name)),
                    X = first.X, Y = first.Y, Width = first.Width, Height = first.Height,
                    Primary = g.Any(m => m.Primary),
                    Detail = $"{first.Width}×{first.Height} · {first.Hz} {Units.Hz}",
                };
            }).ToList();

        var canvas = new ArrangementCanvas { MinFitHeight = 150, MaxFitHeight = 230, Animate = Motion.Enabled };
        canvas.Moved += _ =>
        {
            var main = tiles.FirstOrDefault(t => t.Primary) ?? tiles[0];
            var moves = tiles.ToDictionary(t => origins[t.Key], t => ((int)Math.Round(t.X - main.X), (int)Math.Round(t.Y - main.Y)));
            _moved = Display.SnapshotEdit.Move(layout, moves);
            _pending.Show();
        };
        canvas.SetTiles(tiles, "");
        var area = new Grid { Children = { canvas } };
        if (tiles.Count > 1)
            area.Children.Add(new TextBlock { Text = "Drag to arrange: monitors snap together edge to edge", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(11, 8, 11, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        var box = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(0), Child = area, ClipToBounds = true };
        box.Background = Ui.Res<Brush>("SurfaceAlt");
        return box;
    }

    private async Task RenameAsync()
    {
        var name = await _window.Prompt("Rename setup", "", _setup.Name, text => _app.DisplayProfiles.Validate(text.Trim(), _setup.Hotkey, _setup));
        if (name is null || name.Trim() == _setup.Name) return;
        string old = _setup.Name;
        _setup.Name = name.Trim();
        _app.DisplayProfiles.Renamed(old, _setup.Name);
        _app.DisplayProfiles.RaiseChanged();
    }

    private void UpdateToCurrent()
    {
        if (_hasPending())
        {
            _app.Notify("Apply your changes first", "Setups save what's actually on.", NoticeKind.Info, important: true);
            return;
        }
        var (layout, wallpapers, audio) = (_setup.Layout, _setup.Wallpapers, _setup.Audio);
        _app.DisplayProfiles.Recapture(_setup);
        _app.DisplayProfiles.RaiseChanged();
        _app.Notify("Setup updated", $"“{_setup.Name}” now matches what's on.", NoticeKind.Success, action: new NoticeAction("Undo", () =>
        {
            if (!_app.Config.Displays.Profiles.Contains(_setup)) return;
            (_setup.Layout, _setup.Wallpapers, _setup.Audio) = (layout, wallpapers, audio);
            _app.DisplayProfiles.RaiseChanged();
        }));
    }

    private async Task DeleteAsync()
    {
        if (!await _window.Confirm($"Delete “{_setup.Name}”?", "This can't be undone.", "Delete", danger: true)) return;
        _app.DisplayProfiles.Remove(_setup);
    }

    private void Save()
    {
        var problem = _editor.Validate(_setup.Name);
        _error.Text = problem ?? "";
        _error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        if (problem is not null) return;
        if (_moved is not null) _setup.Layout = _moved;
        _editor.SaveInto(_setup);
        _pending.Hide();
        _app.DisplayProfiles.RaiseChanged();
        _rebuild();
    }
}

internal sealed class DisplayProfileEditor
{
    private const double LeftLabel = 100, RightLabel = 128;
    private readonly AppServices _app;
    private readonly DisplayProfile _profile;
    private readonly Dictionary<string, TextBox> _wallpapers = new(StringComparer.OrdinalIgnoreCase);
    private DisplaySnapshot? _current;
    private int _layoutChoice;
    private HdrMode _hdr;
    private TaskbarMode _taskbar;
    private int? _volume;
    private readonly ComboBox _playback = new(), _recording = new();
    private readonly CheckBox _closeOthers = new() { Content = "Close them when left", ToolTip = "When switching to another setup: asked to close, then ended if they stay open. Apps the next setup also lists keep running." };
    private readonly CheckBox _comms = new() { Content = "Also use them for calls (Discord, Teams…)" };
    private readonly StackPanel _appRows = new();
    private readonly List<TextBox> _apps = new();
    private readonly HotkeyBox _hotkey = new() { Width = 150 };

    public Grid Columns { get; }

    public event Action? Edited;

    public DisplayProfileEditor(AppServices app, DisplayProfile profile, bool pendingEdits)
    {
        _app = app;
        _profile = profile;
        _hdr = profile.Hdr;
        _taskbar = profile.Taskbar;
        _layoutChoice = profile.Layout is null ? 2 : 0;
        try { _current = app.Displays.CaptureLayout(); } catch { _current = null; }

        var stack = new StackPanel();
        stack.Children.Add(ListDetail.Section("Monitors", first: true));
        string[] layoutOptions = { profile.Layout is null ? "" : "Keep saved layout", pendingEdits ? "" : "Use current layout", "Don't change displays" };
        var options = layoutOptions.Select((t, i) => (t, i)).Where(x => x.t.Length > 0).ToList();
        var layout = Ui.Segmented(options.Select(o => o.t).ToList(), Math.Max(0, options.FindIndex(o => o.i == _layoutChoice)),
            i => _layoutChoice = options[i].i, "layout");
        layout.ToolTip = "Which monitors are on, their arrangement, resolution, refresh rate, orientation and the main display";
        stack.Children.Add(Ui.FieldRow("Layout", layout, LeftLabel));
        stack.Children.Add(Ui.FieldRow("HDR", Ui.Segmented(new[] { "Leave as is", "On", "Off" }, (int)_hdr, i => _hdr = (HdrMode)i, "hdr",
            new[] { "Don't change HDR when this setup is applied", "Turn HDR on (on displays that support it)", "Turn HDR off" }), LeftLabel));
        stack.Children.Add(Ui.FieldRow("Taskbar", Ui.Segmented(new[] { "Leave as is", "Every display", "Main display only" }, (int)_taskbar, i => _taskbar = (TaskbarMode)i, "taskbar",
            new[] { "Don't change the taskbar", "Show the taskbar on every display", "Show the taskbar only on the main display (e.g. keeps it off side screens in a triple setup)" }), LeftLabel));

        stack.Children.Add(ListDetail.Section("Audio"));
        _volume = profile.Volume;
        AudioPicker.Fill(app, _playback, EDataFlow.Render, profile.Audio?.PlaybackId, profile.Audio?.PlaybackName);
        AudioPicker.Fill(app, _recording, EDataFlow.Capture, profile.Audio?.RecordingId, profile.Audio?.RecordingName);
        stack.Children.Add(Ui.FieldRow("Output", _playback, LeftLabel));
        stack.Children.Add(Ui.FieldRow("Microphone", _recording, LeftLabel));
        _comms.IsChecked = profile.Audio?.SetCommunications ?? true;
        stack.Children.Add(Ui.FieldRow("", _comms, LeftLabel));
        stack.Children.Add(Ui.FieldRow("Volume", AudioPicker.Volume(_volume, v => { _volume = v; Edited?.Invoke(); }), LeftLabel));

        stack.Children.Add(ListDetail.Section("Keybind"));
        _hotkey.SetHotkey(profile.Hotkey);
        _hotkey.HotkeyChanged += _ => Edited?.Invoke();
        _hotkey.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(Ui.FieldRow("Switch to it", _hotkey, LeftLabel));

        var right = new StackPanel();
        right.Children.Add(ListDetail.Section("Wallpapers", first: true));
        foreach (var (key, name) in WallpaperMonitors())
        {
            var box = new TextBox { Tag = "Keep its wallpaper", Text = profile.Wallpapers.FirstOrDefault(w => string.Equals(w.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "" };
            _wallpapers[key] = box;
            var row = new DockPanel();
            var browse = Ui.IconButton("I.Folder", "Choose an image", () =>
            {
                if (ProgramPicker.BrowseExe("Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*") is { } path)
                    box.Text = path;
            });
            browse.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse);
            row.Children.Add(box);
            right.Children.Add(Ui.FieldRow(name, row, RightLabel));
        }

        right.Children.Add(ListDetail.Section("Apps"));
        right.Children.Add(_appRows);
        foreach (var path in profile.Apps) AddAppRow(path);
        var addApp = Ui.LabeledButton("I.Plus", "Add app", () => { }, "SoftButton");
        addApp.Click += (_, _) => ShowAppChoices(addApp, startMenu: false);
        addApp.HorizontalAlignment = HorizontalAlignment.Left;
        _closeOthers.IsChecked = profile.CloseAppsOnLeave;
        _closeOthers.Margin = new Thickness(0, 8, 0, 0);
        var appTools = new StackPanel { Children = { addApp, _closeOthers } };
        right.Children.Add(Ui.FieldRow("", appTools, RightLabel));

        Columns = ListDetail.Columns(stack, right);
    }

    private void AddAppRow(string path)
    {
        var box = new TextBox { Tag = "Program, shortcut or script", Text = path };
        _apps.Add(box);
        var row = new DockPanel();
        FrameworkElement? field = null;
        var remove = Ui.IconButton("I.X", "Remove", () => { _apps.Remove(box); _appRows.Children.Remove(field); Edited?.Invoke(); });
        remove.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(box);
        field = Ui.FieldRow(Display.ProfileApps.DisplayName(path) is { Length: > 0 } n ? n : "App", row, RightLabel);
        _appRows.Children.Add(field);
    }

    private void ShowAppChoices(FrameworkElement anchor, bool startMenu)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, MaxHeight = 360 };
        Ui.DropBelow(menu);
        var listed = _apps.Select(b => Display.ProfileApps.Clean(b.Text)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = (startMenu ? StartMenuChoices() : RunningChoices()).Where(c => !listed.Contains(c.Path)).ToList();
        foreach (var (path, label) in choices)
        {
            var item = new MenuItem { Header = label };
            Core.Theme.TextCase.Verbatim(item);
            item.Click += (_, _) => AddAppRow(path);
            menu.Items.Add(item);
        }
        if (choices.Count == 0)
            menu.Items.Add(new MenuItem { Header = startMenu ? "No more Start menu apps" : "No other apps running with a window", IsEnabled = false });
        menu.Items.Add(new Separator());
        if (!startMenu)
        {
            var more = new MenuItem { Header = "Start menu apps" };
            more.Click += (_, _) => anchor.Dispatcher.BeginInvoke(() => ShowAppChoices(anchor, startMenu: true));
            menu.Items.Add(more);
        }
        var browse = new MenuItem { Header = "Browse for a file" };
        browse.Click += (_, _) => anchor.Dispatcher.BeginInvoke(() =>
        {
            if (ProgramPicker.BrowseExe() is { } path) AddAppRow(path);
        });
        menu.Items.Add(browse);
        menu.IsOpen = true;
    }

    private IEnumerable<(string Path, string Label)> RunningChoices()
    {
        if (_app.Demo)
            return new[] { (@"C:\Program Files\Dash\Dash.exe", "Dash  ·  Wheel dashboard"), (@"C:\Program Files\Spotter\Spotter.exe", "Spotter  ·  Race spotter") };
        return WindowProfiles.WindowProfileService.AppsWithWindows().Select(w => (w.Path, $"{Display.ProfileApps.DisplayName(w.Path)}  ·  {w.Title}"));
    }

    private IEnumerable<(string Path, string Label)> StartMenuChoices() =>
        _app.Demo ? new[] { (@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Music.lnk", "Music"), (@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Voice chat.lnk", "Voice chat") }
            : Display.ProfileApps.StartMenuShortcuts().Select(p => (p, Display.ProfileApps.DisplayName(p)));

    private IEnumerable<(string Key, string Name)> WallpaperMonitors()
    {
        List<Display.MonitorInfo> monitors;
        try { monitors = _app.Displays.GetMonitors(); }
        catch { monitors = new(); }
        var layout = _profile.Layout ?? _current;
        var listed = layout is null ? new() : _app.Displays.Describe(layout).Where(m => m.Key.Length > 0).Select(m => (m.Key, m.Name)).ToList();
        if (listed.Count == 0)
            listed = monitors.Where(m => m.Active).Select(m => (m.Key, m.Name)).ToList();
        var numbers = monitors.GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Number, StringComparer.OrdinalIgnoreCase);
        var shared = listed.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return listed.Select(m => shared.Contains(m.Name) && numbers.TryGetValue(m.Key, out int n) && n > 0 ? (m.Key, $"{m.Name} ({n})") : m).ToList();
    }

    public string? Validate(string name) =>
        _app.DisplayProfiles.Validate(name, _hotkey.Hotkey, _profile)
        ?? _wallpapers.Values.Select(b => WallpaperPath(b.Text)).Where(p => p.Length > 0 && !File.Exists(p)).Select(p => $"Can't find {Path.GetFileName(p)}.").FirstOrDefault()
        ?? _apps.Select(b => Display.ProfileApps.Clean(Environment.ExpandEnvironmentVariables(b.Text))).Where(p => p.Length > 0 && !p.Contains("://", StringComparison.Ordinal) && !File.Exists(p)).Select(p => $"Can't find {Path.GetFileName(p)}.").FirstOrDefault();

    internal static string WallpaperPath(string typed) => typed.Trim().Trim('"').Trim();

    public void SaveInto(DisplayProfile profile)
    {
        profile.Hotkey = _hotkey.Hotkey;
        profile.Hdr = _hdr;
        profile.Taskbar = _taskbar;
        profile.Layout = _layoutChoice switch { 1 => _current ?? profile.Layout, 2 => null, _ => profile.Layout };
        var wallpapers = profile.Wallpapers.Where(w => !_wallpapers.ContainsKey(w.Key)).ToDictionary(w => w.Key, w => w.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, box) in _wallpapers)
            if (WallpaperPath(box.Text) is { Length: > 0 } path) wallpapers[key] = path;
        profile.Wallpapers = wallpapers;
        var playback = _playback.SelectedItem as Choice;
        var recording = _recording.SelectedItem as Choice;
        profile.Audio = playback?.Id is null && recording?.Id is null ? null : new AudioSnapshot
        {
            PlaybackId = playback?.Id, PlaybackName = playback?.Extra,
            RecordingId = recording?.Id, RecordingName = recording?.Extra,
            SetCommunications = _comms.IsChecked == true,
        };
        profile.Volume = _volume;
        profile.CloseAppsOnLeave = _closeOthers.IsChecked == true;
        profile.Apps = _apps.Select(b => Display.ProfileApps.Clean(b.Text)).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
