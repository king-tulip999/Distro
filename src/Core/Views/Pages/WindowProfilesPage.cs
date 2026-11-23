using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Core.Config;
using Core.Controls;
using Core.Services;
using Core.Theme;
using Core.WindowProfiles;

namespace Core.Views.Pages;

internal sealed class WindowProfilesPage : UserControl, IPage, IScreenshotStates
{
    public const string AutoAnchor = "windows.auto";
    private const string Consumer = "Window placement page";
    private const string NewKey = "new";
    private const double PreviewHeight = 150;
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(23, 0, 23, 23) };
    private readonly ListDetail _split = new();
    private bool _shown, _buildQueued, _scanned, _picked;
    private HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private List<OpenWindow> _open = new();
    private int _scan;
    private string? _selected;
    private WindowProfile? _draft;
    private ListDetail.Pending? _pending;
    private List<LayoutTile> _screens = new();

    public WindowProfilesPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
        var header = new Grid { Margin = Ui.HeaderMargin };
        header.Children.Add(Ui.PageTitle("Window placement"));
        header.Children.Add(AutoApplySwitch());
        _body.Children.Add(header);
        _body.Children.Add(_split.Card);
    }

    private WindowProfileSettings S => _app.Config.WindowProfiles;

    private bool Dirty => _pending?.Showing == true;

    public void OnShown()
    {
        _shown = true;
        Watch();
        Build();
        _app.Watcher.Started += OnProcess;
        _app.Watcher.Stopped += OnProcess;
        _app.WindowProfiles.Changed += OnChanged;
        _app.Displays.Changed += QueueBuild;
        _window.Activated += OnActivated;
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Watcher.Release(Consumer);
        _watched.Clear();
        _app.Watcher.Started -= OnProcess;
        _app.Watcher.Stopped -= OnProcess;
        _app.WindowProfiles.Changed -= OnChanged;
        _app.Displays.Changed -= QueueBuild;
        _window.Activated -= OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e) => _ = ScanAsync();

    private void Watch()
    {
        _watched = S.Profiles.Where(p => p.ProcessName.Length > 0).Select(p => Processes.AppWatcher.Normalize(p.ProcessName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_watched.Count > 0 && !_app.Demo) _app.Watcher.Acquire(Consumer, _watched);
        else _app.Watcher.Release(Consumer);
    }

    private void OnChanged()
    {
        Watch();
        QueueBuild();
    }

    private void OnProcess(string name)
    {
        if (_watched.Contains(name)) QueueBuild();
    }

    private void QueueBuild()
    {
        if (_buildQueued) return;
        _buildQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _buildQueued = false;
            if (_shown) Build();
        }, DispatcherPriority.Background);
    }

    private bool Running(WindowProfile p) =>
        _app.Demo ? p.ProcessName.Equals("SimHubWPF.exe", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("RRRE64.exe", StringComparison.OrdinalIgnoreCase)
            : p.ProcessName.Length > 0 && _app.Watcher.IsRunning(p.ProcessName);

    private void Build()
    {
        S.Normalize();
        _screens = Screens();
        if (!Valid(_selected)) { _selected = null; _picked = false; }
        if (!_picked) _selected = FirstKey();
        BuildList();
        if (!Dirty) ShowDetail(animate: false);
        _ = ScanAsync();
    }

    private bool Valid(string? key) => key switch
    {
        null => false,
        NewKey => _draft is not null,
        _ when key.StartsWith("open:", StringComparison.Ordinal) => Free().Any(w => OpenKey(w) == key),
        _ => S.Profiles.Any(p => WindowProfileSettings.KeyOf(p) == key) || S.Groups.Any(g => WindowProfileSettings.KeyOf(g) == key),
    };

    private string? FirstKey()
    {
        var group = GroupsInOrder().FirstOrDefault();
        if (group is not null) return WindowProfileSettings.KeyOf(group);
        var first = UngroupedInOrder().FirstOrDefault();
        if (first is not null) return WindowProfileSettings.KeyOf(first);
        return Free().FirstOrDefault() is { } open ? OpenKey(open) : null;
    }

    private int LayoutIndex(string key)
    {
        int i = S.Layout.FindIndex(s => s.Id == key);
        return i < 0 ? int.MaxValue : i;
    }

    private List<WindowGroup> GroupsInOrder() => S.Groups.OrderBy(g => LayoutIndex(WindowProfileSettings.KeyOf(g))).ToList();

    private List<WindowProfile> UngroupedInOrder() => S.Ungrouped.OrderBy(p => LayoutIndex(WindowProfileSettings.KeyOf(p))).ToList();

    private List<OpenWindow> Free() => _open.Where(w => Owners(w.Process).Count == 0).ToList();

    private void BuildList()
    {
        var list = _split.List;
        list.Children.Clear();
        list.Children.Add(ListDetail.Heading("Your placements", first: true));
        var groups = GroupsInOrder();
        var ungrouped = UngroupedInOrder();
        foreach (var group in groups)
        {
            var g = group;
            var members = S.Members(g).ToList();
            string key = WindowProfileSettings.KeyOf(g);
            list.Children.Add(ListDetail.Row(ListDetail.RowFace(g.Name, SimRacing.Race.RaceFormat.Count(members.Count, "placement"), icon: "I.Folder"), "WindowList", key == _selected, () => Pick(key)));
            if (members.Count == 0) continue;
            var rows = ListDetail.Reorder();
            rows.Margin = new Thickness(14, 0, 0, 0);
            foreach (var member in members) rows.Children.Add(PlacementRow(member));
            rows.Moved += (from, to) => { S.MoveWithinGroup(g, from, to); _app.Save(); };
            list.Children.Add(rows);
        }
        if (ungrouped.Count > 0)
        {
            var rows = ListDetail.Reorder();
            foreach (var p in ungrouped) rows.Children.Add(PlacementRow(p));
            rows.Moved += MoveUngrouped;
            list.Children.Add(rows);
        }
        if (_draft is not null)
            list.Children.Add(ListDetail.Row(ListDetail.RowFace("New placement", null), "WindowList", _selected == NewKey, () => Pick(NewKey)));
        if (groups.Count == 0 && ungrouped.Count == 0 && _draft is null)
            list.Children.Add(ListDetail.Note("None yet. Pick a program and the exact size and spot for its window."));
        else if (S.Profiles.Count > 1)
            list.Children.Add(ListDetail.DragHint());


        list.Children.Add(ListDetail.Rule());
        list.Children.Add(ListDetail.Heading("Open now"));
        var free = Free();
        if (free.Count > 0)
        {
            var hint = ListDetail.Note("Running with no placement yet. Pick one to save where its window is.");
            hint.Margin = new Thickness(5, 0, 5, 6);
            list.Children.Add(hint);
        }
        foreach (var w in free)
        {
            string key = OpenKey(w);
            var row = ListDetail.Row(ListDetail.RowFace(w.Process, null, verbatim: true), "WindowList", key == _selected, () => Pick(key));
            row.ToolTip = w.Title;
            list.Children.Add(row);
        }
        if (free.Count == 0 && _scanned)
            list.Children.Add(ListDetail.Note("Every program with a window open has a placement."));

        var newGroup = Ui.LabeledButton("I.Folder", "New group", () => _ = NewGroupAsync(), "GhostButton");
        var create = Ui.LabeledButton("I.Plus", "New placement", () => StartNew(new WindowProfile()), "PrimaryButton");
        var foot = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.ButtonGap) });
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        create.HorizontalAlignment = newGroup.HorizontalAlignment = HorizontalAlignment.Stretch;
        foot.Children.Add(create);
        Grid.SetColumn(newGroup, 2);
        foot.Children.Add(newGroup);
        list.Children.Add(foot);
    }

    private RadioButton PlacementRow(WindowProfile p)
    {
        string key = WindowProfileSettings.KeyOf(p);
        string? keys = p.Hotkey is { } hk ? Hotkeys.Hotkey.Display(hk) : null;
        var row = ListDetail.Row(ListDetail.RowFace(p.Name, keys, Running(p) ? "Sage" : null), "WindowList", key == _selected, () => Pick(key));
        if (Running(p)) row.ToolTip = "Running";
        return row;
    }

    private void MoveUngrouped(int from, int to)
    {
        var order = UngroupedInOrder();
        if (from < 0 || to < 0 || from >= order.Count || to >= order.Count) return;
        var moved = order[from];
        order.RemoveAt(from);
        order.Insert(to, moved);
        var keys = GroupsInOrder().Select(WindowProfileSettings.KeyOf).Concat(order.Select(WindowProfileSettings.KeyOf)).ToList();
        S.Layout = keys.Select(k => new PanelSlot { Id = k }).ToList();
        _app.Save();
    }

    private void Pick(string key)
    {
        if (_selected == key) return;
        bool dropped = _selected == NewKey && _draft is not null;
        if (dropped) _draft = null;
        _picked = true;
        _selected = key;
        Dispatcher.BeginInvoke(BuildList, DispatcherPriority.Background);
        ShowDetail(animate: true);
    }

    private void ShowDetail(bool animate)
    {
        _pending = null;
        FrameworkElement detail;
        if (_selected == NewKey && _draft is not null) detail = PlacementDetail(_draft, isNew: true);
        else if (_selected is null) detail = EmptyDetail();
        else if (_selected.StartsWith("open:", StringComparison.Ordinal) && _open.FirstOrDefault(w => OpenKey(w) == _selected) is { } open) detail = OpenDetail(open);
        else if (S.Groups.FirstOrDefault(g => WindowProfileSettings.KeyOf(g) == _selected) is { } group) detail = GroupDetail(group);
        else if (S.Profiles.FirstOrDefault(p => WindowProfileSettings.KeyOf(p) == _selected) is { } profile) detail = PlacementDetail(profile, isNew: false);
        else detail = EmptyDetail();
        _split.Show(detail, animate);
    }

    private FrameworkElement EmptyDetail()
    {
        var create = Ui.LabeledButton("I.Plus", "New placement", () => StartNew(new WindowProfile()), "PrimaryButton");
        var empty = Ui.EmptyState(Plugins.BuiltIn.WindowProfilesPlugin.PageIcon, "No window placements yet",
            "Pick a program and the exact size and spot for its window, e.g. a game stretched across triple screens or held at 1920×1080, borderless.", create);
        empty.Margin = new Thickness(0, 40, 0, 40);
        return empty;
    }

    private StackPanel PlacementDetail(WindowProfile p, bool isNew)
    {
        var stack = new StackPanel();
        var editor = new WindowProfileEditor(_app, p, isNew);
        var error = ErrorLine();
        var marks = new List<FrameworkElement>();
        var actions = new List<FrameworkElement>();
        bool running = !isNew && Running(p);
        if (running) marks.Add(Ui.Chip("Running", "SageSoft", "SageDeep"));
        if (!isNew)
        {
            actions.Add(Ui.LabeledButton("I.Trash", "Delete", () => _ = DeleteAsync(p), "DangerButton"));
        }
        var test = Ui.LabeledButton("I.Play", "Try it now", () => ShowError(error, editor.TryNow()), "GhostButton");
        test.ToolTip = "Move the program's window with these values now, without saving";
        actions.Add(test);
        if (!isNew)
        {
            var apply = Ui.LabeledButton("I.Zap", "Apply", () => _ = _app.WindowProfiles.ApplyAsync(p), "PrimaryButton");
            apply.ToolTip = running ? "Move the window now" : p.ProcessName.Length == 0 ? "No program picked" : $"{p.ProcessName} isn't running";
            actions.Add(apply);
        }
        stack.Children.Add(ListDetail.Head(isNew ? "New placement" : p.Name, isNew ? null : () => _ = RenameAsync(p), marks, actions));
        stack.Children.Add(ListDetail.Preview(editor.Preview, PreviewHeight));
        if (!isNew) stack.Children.Add(ListDetail.Facts("What applying it does", Summary(p, editor.ScreensFor(p))));
        stack.Children.Add(ListDetail.Divider());
        stack.Children.Add(editor.Columns);
        var pending = new ListDetail.Pending(isNew ? "Not saved yet" : $"Unsaved changes to “{p.Name}”", isNew ? "Save placement" : "Save changes",
            () => { if (isNew) DropDraft(); else ShowDetail(animate: false); },
            () => SavePlacement(p, editor, error, isNew));
        stack.Children.Add(pending.Bar);
        stack.Children.Add(error);
        _pending = pending;
        if (isNew) pending.Show();
        ListDetail.TrackEdits(editor.Columns, pending.Show);
        editor.Edited += pending.Show;
        return stack;
    }

    private static TextBlock ErrorLine()
    {
        var error = new TextBlock { Style = Ui.Res<Style>("Caption"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerDeep");
        return error;
    }

    private static void ShowError(TextBlock error, string? problem)
    {
        error.Text = problem ?? "";
        error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SavePlacement(WindowProfile p, WindowProfileEditor editor, TextBlock error, bool isNew)
    {
        string? oldHotkey = p.Hotkey;
        var problem = editor.Save();
        ShowError(error, problem);
        if (problem is not null) return;
        if (!Hotkeys.Hotkey.SameKey(oldHotkey, p.Hotkey)) NoteSharedHotkey(p, p.Hotkey);
        if (isNew)
        {
            S.Profiles.Add(p);
            _draft = null;
        }
        _pending?.Hide();
        _picked = true;
        _selected = WindowProfileSettings.KeyOf(p);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
        _app.Notify(isNew ? "Placement saved" : "Placement updated", $"{p.Name} · {WindowProfileService.Describe(p)}", NoticeKind.Success);
        BuildList();
        ShowDetail(animate: false);
    }

    private void StartNew(WindowProfile draft)
    {
        _draft = draft;
        _picked = true;
        _selected = NewKey;
        BuildList();
        ShowDetail(animate: true);
    }

    private void DropDraft()
    {
        _draft = null;
        _picked = false;
        _selected = null;
        Build();
    }

    private IEnumerable<(string, string)> Summary(WindowProfile p, IReadOnlyList<LayoutTile> screens)
    {
        bool sized = p.Width is not null && p.Height is not null;
        yield return ("Size", sized ? $"{p.Width}×{p.Height}" : "Kept as the program sets it");
        yield return ("Where", Spot(Where(new Rect(p.X, p.Y, p.Width ?? 1, p.Height ?? 1), screens)));
        if (p.RemoveBorders) yield return ("Borders", p.HideTitleBar ? "Removed, title bar hidden" : "Removed");
        yield return ("Applies", !p.Auto ? "By hand"
            : !S.AutoApply ? "By hand (automatic is off)"
            : p.DelayMs > 0 ? $"When it starts, after {p.DelayMs / 1000.0:0.##} {Units.S}" : "When it starts");
    }

    private static string Spot(string where) =>
        where.StartsWith(" on display", StringComparison.Ordinal) ? "Display" + where[" on display".Length..]
        : where.StartsWith(" across", StringComparison.Ordinal) ? "Across" + where[" across".Length..]
        : "At" + where[" at".Length..];

    private static string Where(Rect window, IReadOnlyList<LayoutTile> screens)
    {
        var hit = screens.Where(s => s.Label.Length > 0 && Rect.Intersect(new Rect(s.X, s.Y, s.Width, s.Height), window) is { IsEmpty: false, Width: > 0, Height: > 0 })
            .OrderBy(s => s.Y).ThenBy(s => s.X).ToList();
        if (hit.Count == 0) return $" at {window.X}, {window.Y}";
        var first = hit.OrderBy(s => s.X).ThenBy(s => s.Y).First();
        string at = $"display {first.Label} at {window.X - first.X}, {window.Y - first.Y}";
        if (hit.Count == 1) return " on " + at;
        var union = hit.Select(s => new Rect(s.X, s.Y, s.Width, s.Height)).Aggregate(Rect.Union);
        string across = $" across {SimRacing.Race.RaceFormat.Count(hit.Count, "display")}";
        return union == window ? across : $"{across} from {at}";
    }

    private StackPanel GroupDetail(WindowGroup g)
    {
        var members = S.Members(g).ToList();
        var stack = new StackPanel();
        var hotkey = new HotkeyBox { Width = 211, HorizontalAlignment = HorizontalAlignment.Left };
        hotkey.SetHotkey(g.Hotkey);
        hotkey.HotkeyChanged += hk => SetHotkey(hotkey, g, hk);
        var apply = Ui.LabeledButton("I.Zap", "Apply all", () => _ = _app.WindowProfiles.ApplyGroupAsync(g), "PrimaryButton");
        apply.ToolTip = "Move every program in the group that's running";
        apply.IsEnabled = members.Count > 0;
        stack.Children.Add(ListDetail.Head(g.Name, () => _ = RenameAsync(g), Array.Empty<FrameworkElement>(),
            new FrameworkElement[] { Ui.LabeledButton("I.Trash", "Delete", () => _ = DeleteGroupAsync(g), "DangerButton"), apply }));
        var map = new WindowMapView();
        map.SetScreens(_screens);
        map.SetWindows(members.Where(m => m.Width is not null && m.Height is not null)
            .Select(m => new MapWindow(m.Id, new Rect(m.X, m.Y, m.Width!.Value, m.Height!.Value), m.Label, false)).ToList());
        stack.Children.Add(ListDetail.Preview(map, PreviewHeight));
        if (members.Count > 0) stack.Children.Add(ListDetail.Facts("In this group", members.Select(m => (m.Name, (m.Width is { } w && m.Height is { } h ? $"{w}×{h} · " : "") + Spot(Where(new Rect(m.X, m.Y, m.Width ?? 1, m.Height ?? 1), _screens))))));
        else
        {
            var note = ListDetail.Note("Empty: pick this group in a placement's Group setting.");
            note.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(note);
        }
        stack.Children.Add(ListDetail.Divider());
        stack.Children.Add(ListDetail.Section("Keybind", first: true));
        stack.Children.Add(Ui.FieldRow("Move them all", hotkey, 100));
        return stack;
    }

    private StackPanel OpenDetail(OpenWindow w)
    {
        var stack = new StackPanel();
        var save = Ui.LabeledButton("I.Plus", "Save as placement", () => SaveOpen(w), "PrimaryButton");
        save.ToolTip = "Start a placement with the window's size and position now";
        stack.Children.Add(ListDetail.Head(w.Process, null, new FrameworkElement[] { Ui.Chip("Running", "SageSoft", "SageDeep") }, new FrameworkElement[] { save }));
        if (stack.Children[0] is Grid head && head.Children[0] is TrimRow row && row.Children[0] is TextBlock name) TextCase.Verbatim(name);
        var map = new WindowMapView();
        map.SetScreens(_screens);
        map.SetWindows(new List<MapWindow> { new(OpenKey(w), new Rect(w.X, w.Y, w.Width, w.Height), WindowPresets.FriendlyName(w.Process), false) });
        stack.Children.Add(ListDetail.Preview(map, PreviewHeight));
        stack.Children.Add(ListDetail.Facts("Where it is now", new[] { ("Size", $"{w.Width}×{w.Height}"), ("Where", Spot(Where(new Rect(w.X, w.Y, w.Width, w.Height), _screens))), ("Window title", w.Title) }));
        return stack;
    }

    private void SaveOpen(OpenWindow w)
    {
        string name = WindowPresets.FriendlyName(w.Process);
        if (name.Length > Ui.NameLength) name = name[..Ui.NameLength];
        StartNew(new WindowProfile { Name = S.UniqueName(name), ProcessName = w.Process, X = w.X, Y = w.Y, Width = w.Width, Height = w.Height });
    }

    private void SetHotkey(HotkeyBox box, object owner, string? hotkey)
    {
        string? current = owner switch { WindowProfile p => p.Hotkey, WindowGroup g => g.Hotkey, _ => null };
        if (_app.Hotkeys.Clash(hotkey, owner) is { } problem)
        {
            _app.Notify("Keybind not changed", problem, NoticeKind.Warning);
            box.SetHotkey(current);
            return;
        }
        if (owner is WindowProfile profile) profile.Hotkey = hotkey;
        else if (owner is WindowGroup group) group.Hotkey = hotkey;
        NoteSharedHotkey(owner, hotkey);
        _app.WindowProfiles.RaiseChanged();
    }

    private void NoteSharedHotkey(object owner, string? hotkey)
    {
        var shared = string.IsNullOrWhiteSpace(hotkey) ? new List<string>()
            : S.Profiles.Where(p => p != owner && Hotkeys.Hotkey.SameKey(p.Hotkey, hotkey)).Select(p => $"“{p.Name}”")
                .Concat(S.Groups.Where(g => g != owner && Hotkeys.Hotkey.SameKey(g.Hotkey, hotkey)).Select(g => $"the group “{g.Name}”")).ToList();
        if (shared.Count > 0)
            _app.Notify("Keybind shared", $"Also used by {string.Join(", ", shared)}: pressing it moves whichever of their programs are running.", NoticeKind.Info);
    }

    private async Task RenameAsync(WindowProfile p)
    {
        var name = await _window.Prompt("Rename placement", "", p.Name, text => S.NameProblem(text.Trim(), p));
        if (name is null || name.Trim() == p.Name) return;
        p.Name = name.Trim();
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task RenameAsync(WindowGroup g)
    {
        var name = await _window.Prompt("Rename group", "", g.Name, text => S.NameProblem(text.Trim(), g));
        if (name is null || name.Trim() == g.Name) return;
        g.Name = name.Trim();
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task NewGroupAsync()
    {
        var name = await _window.Prompt("New group", "Its placements move together.", S.UniqueName("Group"), text => S.NameProblem(text.Trim(), null));
        if (name is null) return;
        var group = new WindowGroup { Name = name.Trim() };
        S.Groups.Add(group);
        S.Normalize();
        _picked = true;
        _selected = WindowProfileSettings.KeyOf(group);
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task DeleteAsync(WindowProfile profile)
    {
        if (!await _window.Confirm($"Delete “{profile.Name}”?", "The placement is removed. Windows stay where they are now.", "Delete", danger: true)) return;
        S.Profiles.Remove(profile);
        S.Normalize();
        _picked = false;
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task DeleteGroupAsync(WindowGroup group)
    {
        if (!await _window.Confirm($"Delete the group “{group.Name}”?", "Its placements stay, outside any group.", "Delete", danger: true)) return;
        S.Remove(group);
        S.Normalize();
        _picked = false;
        _app.WindowProfiles.RaiseChanged();
    }

    private static string OpenKey(OpenWindow w) => "open:" + w.Process;

    private List<WindowProfile> Owners(string process)
    {
        string name = Processes.AppWatcher.Normalize(process);
        return S.Profiles.Where(p => p.ProcessName.Length > 0 && Processes.AppWatcher.Normalize(p.ProcessName).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task ScanAsync()
    {
        int scan = ++_scan;
        var open = _app.Demo ? DemoWindows() : await Task.Run(() =>
        {
            try { return WindowProfileService.OpenWindows(); }
            catch (Exception ex) { App.LogError(ex); return new List<OpenWindow>(); }
        });
        if (scan != _scan || !_shown) return;
        string before = string.Join("|", Free().Select(OpenKey)) + _scanned;
        _open = open;
        _scanned = true;
        if (string.Join("|", Free().Select(OpenKey)) + _scanned == before) return;
        bool wasEmpty = _selected is null;
        if (!Valid(_selected)) { _selected = null; _picked = false; }
        if (!_picked) _selected = FirstKey();
        BuildList();
        if (!Dirty && (wasEmpty || _selected is null || _selected.StartsWith("open:", StringComparison.Ordinal))) ShowDetail(animate: false);
    }

    private static List<OpenWindow> DemoWindows() => new()
    {
        new("acs.exe", "Assetto Corsa", 320, 180, 1920, 1080),
        new("Discord.exe", "Discord", 2900, 120, 1280, 900),
        new("iRacingSim64DX11.exe", "iRacing.com Simulator", -2240, 180, 1920, 1080),
        new("RRRE64.exe", "RaceRoom Racing Experience", 0, 0, 2560, 1440),
        new("SimHubWPF.exe", "SimHub", 2800, 300, 1600, 900),
        new("Spotify.exe", "Spotify Premium", -2400, 200, 1400, 1000),
    };

    private StackPanel AutoApplySwitch()
    {
        var toggle = Ui.Switch(S.AutoApply, v => { S.AutoApply = v; _app.WindowProfiles.RaiseChanged(); });
        var row = Ui.PageSwitch("Apply automatically", toggle,
            "On: each placement set to apply when its program starts does so. Off: placements apply only with Apply, their keybind or the tray.");
        Ui.Anchor(row, AutoAnchor);
        return row;
    }

    private List<LayoutTile> Screens()
    {
        try
        {
            return _app.Displays.GetMonitors().Where(m => m.Active).OrderBy(m => m.X)
                .Select(m => new LayoutTile(m.X, m.Y, m.Width, m.Height, m.Number > 0 ? m.Number.ToString() : "", "", m.Primary)).ToList();
        }
        catch { return new List<LayoutTile>(); }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("placement", () =>
        {
            if (UngroupedInOrder().FirstOrDefault() is { } first) Pick(WindowProfileSettings.KeyOf(first));
            return Task.CompletedTask;
        });
        yield return ("placement-edited", () =>
        {
            _pending?.Show();
            return Task.CompletedTask;
        });
        yield return ("group", () =>
        {
            if (GroupsInOrder().FirstOrDefault() is { } group) Pick(WindowProfileSettings.KeyOf(group));
            return Task.CompletedTask;
        });
        yield return ("open-window", () =>
        {
            if (Free().FirstOrDefault() is { } open) Pick(OpenKey(open));
            return Task.CompletedTask;
        });
        yield return ("new", () =>
        {
            StartNew(new WindowProfile());
            return Task.CompletedTask;
        });
        yield return ("new-dropped", () =>
        {
            DropDraft();
            return Task.CompletedTask;
        });
    }
}
