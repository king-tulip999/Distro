using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Core.Config;
using Core.Controls;
using Core.Plugins.BuiltIn;
using Core.Services;
using Core.SimRacing.Race;
using Core.Theme;
using Glyph = Core.Controls.Icon;

namespace Core.Views.RaceEvents;

internal sealed partial class RaceEventsWindow : Window, IModalHost
{
    private const double Side = 12;
    private static RaceEventsWindow? _open;

    private readonly AppServices _app;
    private readonly IRaceEventsFeed _feed;
    private readonly RaceEventsSettings _settings;
    private readonly Border _tabs;
    private readonly TextBlock _leadText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _lead;
    private readonly Button _state = new() { Focusable = true, FocusVisualStyle = Ui.Res<Style>("FocusRing"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Border _stateFace = new() { Height = 28, Padding = new Thickness(10, 0, 10, 0) };
    private readonly System.Windows.Shapes.Ellipse _stateDot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _stateText = new() { FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly RaceTimeline _timeline = new() { Margin = new Thickness(Side, 10, Side, 0) };
    private readonly Grid _body = new();
    private readonly CheckBox _camTick, _followTick, _openTick;
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pin;
    private readonly System.Windows.Threading.DispatcherTimer _pulse = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private RaceEventsSnapshot _snap = RaceEventsSnapshot.Empty;
    private long _version = -1;
    private int _tab;
    private int? _selected;
    private bool _ready;
    private SimRacing.TrackMaps.TurnNames _turns = SimRacing.TrackMaps.TurnNames.None;
    private SimRacing.Telemetry.SessionInfo? _turnsFor;

    public static RaceEventsWindow? Showing => _open;

    public static void Open(AppServices app, bool activate = true)
    {
        if (_open is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            if (activate) open.Activate();
            return;
        }
        if (SessionStatsPlugin.RaceEventsFeed(app) is not { } feed)
        {
            app.Notify("Race events", "Session Stats isn't running, so no race is being recorded.", NoticeKind.Info);
            return;
        }
        _open = new RaceEventsWindow(app, feed);
        if (!activate) _open.ShowActivated = false;
        _open.Show();
        if (activate) _open.Activate();
    }

    public static void Toggle(AppServices app)
    {
        if (_open is { IsActive: true } open) open.Close();
        else Open(app);
    }

    private RaceEventsWindow(AppServices app, IRaceEventsFeed feed)
    {
        _app = app;
        _feed = feed;
        _settings = app.Config.SessionStats.RaceEvents;
        Title = "Velocita · Race events";
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 38, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = ThemeManager.Round(0), UseAeroCaptionButtons = false });
        UseLayoutRounding = true;
        Resources[WindowScale.Key] = Transform.Identity;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "Text");
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "LineStrong");
        Hairline.Frame(this);
        FontSize = 13;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Core;component/Assets/Velocita.ico"));
        MinWidth = 740;
        MinHeight = 560;
        Width = _settings.Width ?? 760;
        Height = _settings.Height ?? 820;
        Topmost = _settings.OnTop;
        Place();

        var wordmark = new WordmarkText { InkHeight = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
        var slash = new Border { Width = 1.5, Height = 14, Margin = new Thickness(11, 0, 10, 0), RenderTransform = new SkewTransform(-18, 0), VerticalAlignment = VerticalAlignment.Center };
        slash.SetResourceReference(Border.BackgroundProperty, "Accent");
        var name = new TextBlock { Text = "Race events", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Children = { wordmark, slash, name } };
        _pin = Caption("CaptionButton", PinGlyph(), "Keep on top of iRacing", TogglePin);
        var min = Caption("CaptionButton", new CaptionGlyph { Kind = CaptionKind.Minimize }, "Minimize", () => WindowState = WindowState.Minimized);
        var close = Caption("CloseCaptionButton", new CaptionGlyph { Kind = CaptionKind.Close }, "Close", Close);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { _pin, min, close } };
        var bar = new DockPanel { LastChildFill = false, Height = 38 };
        DockPanel.SetDock(brand, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(brand);
        bar.Children.Add(buttons);
        PaintPin();

        _tabs = Ui.PageTabs(new[] { "My race", "Rivals", "Watched car", "Standings", "Lap times" }, 0, i => { _tab = i; Build(fade: true); }, "raceevents", compact: true);
        _tabs.HorizontalAlignment = HorizontalAlignment.Left;
        _leadText.Text = LeadShort(_settings.Lead);
        var leadIcon = new Glyph { Data = Ui.Res<Geometry>("I.Undo"), Width = 12, Height = 12, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        leadIcon.SetResourceReference(Glyph.ForegroundProperty, "TextMuted");
        _lead = new Button { Style = Ui.Res<Style>("MenuButton"), Content = Ui.MenuFace(_leadText, leadIcon), ToolTip = "How far before each event the replay starts", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        _lead.Click += (_, _) => LeadMenu();
        _stateFace.CornerRadius = ThemeManager.Round(6);
        _stateFace.Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _stateDot, _stateText } };
        _state.Template = BareTemplate();
        _state.Content = _stateFace;
        _state.Click += (_, _) => BackToLive();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0), Children = { _lead, _state } };
        var tabRow = new TabBarRow();
        tabRow.Children.Add(_tabs);
        tabRow.Children.Add(tools);
        var tabBar = Ui.TabBar(_tabs, tabRow);
        tabBar.Margin = new Thickness(Side, 4, Side, 0);
        tabBar.Padding = new Thickness(12, 0, 10, 0);

        _timeline.EventClicked += id => { if (_snap.Events.FirstOrDefault(e => e.Id == id) is { } e) Watch(e); };
        _timeline.TimeClicked += t => JumpTo(t, null, null);

        _camTick = Tick("Camera on driver", "Clicking an event points iRacing's camera at the driver in it", _settings.CameraOnDriver, on => _settings.CameraOnDriver = on);
        _followTick = Tick("Follow new events", "New events scroll into view as they happen", _settings.FollowNew, on => { _settings.FollowNew = on; if (on) ScrollToNewest(); });
        _openTick = Tick("Open when I leave the car", "Opens this window when you get out of the car during a race", _settings.OpenWhenLeavingCar, on => _settings.OpenWhenLeavingCar = on);
        var clockIcon = new Glyph { Data = Ui.Res<Geometry>("I.Clock"), Width = 13, Height = 13, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        clockIcon.SetResourceReference(Glyph.ForegroundProperty, "TextMuted");
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var clock = new StackPanel { Orientation = Orientation.Horizontal, Children = { clockIcon, _clock }, ToolTip = "Race time at the replay" };
        var foot = new DockPanel { LastChildFill = false, Height = 46, Margin = new Thickness(Side + 2, 0, Side + 4, 0) };
        foreach (var t in new FrameworkElement[] { _camTick, _followTick, _openTick })
        {
            t.Margin = new Thickness(0, 0, 18, 0);
            DockPanel.SetDock(t, Dock.Left);
            foot.Children.Add(t);
        }
        DockPanel.SetDock(clock, Dock.Right);
        clock.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(clock);
        var keys = Ui.LabeledButton("I.Keyboard", "Replay keybinds", OpenReplayKeybinds);
        keys.ToolTip = "Set iRacing's own replay keys (play / pause, rewind, next incident, next car) in the iRacing Controller";
        keys.VerticalAlignment = VerticalAlignment.Center;
        keys.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(keys, Dock.Right);
        foot.Children.Add(keys);
        var footHost = Hairline.Frame(new Border { Child = foot }, Sides.Top);
        footHost.SetResourceReference(Border.BorderBrushProperty, "Line");
        footHost.SetResourceReference(Border.BackgroundProperty, "Surface");

        var root = new Grid();
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = h });
        root.SetResourceReference(Panel.BackgroundProperty, "WindowBg");
        Grid.SetRow(tabBar, 1);
        Grid.SetRow(_timeline, 2);
        Grid.SetRow(_body, 3);
        Grid.SetRow(footHost, 4);
        foreach (UIElement e in new UIElement[] { bar, tabBar, _timeline, _body, footHost }) root.Children.Add(e);
        Grid.SetRow(_modal, 1);
        Grid.SetRowSpan(_modal, 4);
        root.Children.Add(_modal);
        RenderOptions.SetClearTypeHint(root, ClearTypeHint.Enabled);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            if (_confirm is not null)
            {
                if (Keyboard.FocusedElement is HotkeyBox) return;
                e.Handled = true;
                CloseModal(false);
                return;
            }
            if (Keyboard.FocusedElement is TextBox) return;
            if (_snap.ReplayAt is not null) { e.Handled = true; BackToLive(); }
        };
        _pulse.Tick += (_, _) => _stateDot.Opacity = _stateDot.Opacity > 0.5 ? 0.3 : 1;
        _app.Tick += OnTick;
        Loaded += async (_, _) =>
        {
            if (_feed is DemoRaceEvents demo) await demo.Warm();
            _ready = true;
            Refresh(force: true);
        };
        Build(fade: false);
    }

    private readonly Grid _modal = new() { Visibility = Visibility.Collapsed };
    private TaskCompletionSource<bool>? _confirm;

    private void OpenReplayKeybinds()
    {
        var board = new Pages.KeybindsBoard(_app, new SimRacing.IracingFiles.Workspace(_app.Config.SimRacing.IracingController, _app.Demo), SimRacing.IracingKeybinds.ReplayGroup);
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => CloseModal(false);
        var buttons = new StackPanel { Children = { close } };
        _ = ShowModal(Ui.Dialog(this, "Replay keybinds", "", board, buttons, width: Math.Min(660, ActualWidth - 40)));
    }

    public double ModalRoom => ActualHeight - 90;

    public Task<bool> ShowModal(FrameworkElement content)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>();
        content.VerticalAlignment = VerticalAlignment.Center;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        _modal.Children.Clear();
        var dim = new Border();
        dim.SetResourceReference(Border.BackgroundProperty, "Dim");
        dim.MouseLeftButtonDown += (_, _) => CloseModal(false);
        _modal.Children.Add(dim);
        _modal.Children.Add(content);
        _modal.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (_confirm is not null && !_modal.IsKeyboardFocusWithin) content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
        return _confirm.Task;
    }

    public void CloseModal(bool result)
    {
        var pending = _confirm;
        _confirm = null;
        _modal.Visibility = Visibility.Collapsed;
        _modal.Children.Clear();
        pending?.TrySetResult(result);
    }

    public Task<bool> Confirm(string title, string message, string confirmText, bool danger = false)
    {
        var cancel = new Button { Content = "Cancel", Margin = Ui.AfterButton };
        cancel.Click += (_, _) => CloseModal(false);
        var ok = new Button { Content = confirmText, Style = Ui.Res<Style>(danger ? "DangerConfirmButton" : "PrimaryButton") };
        ok.Click += (_, _) => CloseModal(true);
        var task = ShowModal(Ui.Dialog(this, title, message, new Border(), new StackPanel { Children = { cancel, ok } }, width: 360));
        ok.Focus();
        return task;
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        if (ScreenshotRunner.Active)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000;
            Top = 0;
            Width = 760;
            Height = 820;
            ShowActivated = false;
            return;
        }
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_settings.Left is { } left && _settings.Top is { } top && screen.Contains(new Point(left + 40, top + 20)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private static ControlTemplate BareTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return template;
    }

    private static UIElement PinGlyph() => new Glyph { Data = Ui.Res<Geometry>("I.Pin"), Width = 13, Height = 13 };

    private Button Caption(string style, UIElement glyph, string tip, Action click)
    {
        var b = new Button { Style = Ui.Res<Style>(style), Content = glyph, ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += (_, _) => click();
        return b;
    }

    private void TogglePin()
    {
        _settings.OnTop = !_settings.OnTop;
        Topmost = _settings.OnTop;
        PaintPin();
        _app.Save();
    }

    private void PaintPin()
    {
        if (_pin.Content is Glyph icon) icon.SetResourceReference(Glyph.ForegroundProperty, _settings.OnTop ? "AccentText" : "TextMuted");
        _pin.ToolTip = _settings.OnTop ? "Kept on top of iRacing: click to stop" : "Keep on top of iRacing";
    }

    private CheckBox Tick(string label, string tip, bool on, Action<bool> changed)
    {
        var box = Ui.Switch(on, v => { changed(v); _app.Save(); });
        box.Content = label;
        box.ToolTip = tip;
        box.VerticalAlignment = VerticalAlignment.Center;
        return box;
    }

    private static string LeadShort(int seconds) => seconds == 0 ? "At the event" : $"{seconds} {Units.S} early";

    private static string LeadLong(int seconds) => seconds == 0 ? "Start at the event" : $"Start {seconds} {Units.S} early";

    private void LeadMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _lead, Placement = PlacementMode.Bottom, MinWidth = 178 };
        Ui.DropBelow(menu);
        foreach (int seconds in RaceEventsSettings.Leads)
        {
            int s = seconds;
            var item = new MenuItem { Header = LeadLong(s), IsCheckable = true, IsChecked = _settings.Lead == s };
            item.Click += (_, _) =>
            {
                _settings.Lead = s;
                _leadText.Text = LeadShort(s);
                _app.Save();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnTick()
    {
        if (_ready) Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        var s = _feed.Snapshot();
        bool events = s.Version != _version;
        _snap = s;
        _version = s.Version;
        SyncTurns();
        Chrome();
        if (PickerOpen) return;
        if (force || events || _tab >= 3) Build(fade: false);
        else _timeline.SetHeads(s.Now, s.ReplayAt);
    }

    private void SyncTurns()
    {
        var session = _snap.Session;
        if (ReferenceEquals(session, _turnsFor) || session.Drivers.Count == 0) return;
        _turnsFor = session;
        _turns = new SimRacing.TrackMaps.TurnNames(Array.Empty<(double, string)>(), session.TrackLengthKm);
        var loading = SimRacing.TrackMaps.TrackMapLibrary.ForRace(session, _app.Demo);
        loading.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || t.Result is not { } map) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(session, _turnsFor)) return;
                _turns = SimRacing.TrackMaps.TurnNames.From(map, session.TrackLengthKm);
                Build(fade: false);
            });
        }, TaskScheduler.Default);
    }

    private void Chrome()
    {
        var s = _snap;
        bool any = s.Connected || s.HasRace;
        _state.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        double behind = s.Behind;
        bool live = behind < RaceEventRecorder.LiveSlack;
        _stateFace.SetResourceReference(Border.BackgroundProperty, live ? "DangerSoft" : "WarnSoft");
        _stateText.SetResourceReference(TextBlock.ForegroundProperty, live ? "Danger" : "Warn");
        _stateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, live ? "Danger" : "Warn");
        TextCase.Set(_stateText, live ? "Live" : $"{RaceFormat.Clock(behind)} behind");
        _state.IsHitTestVisible = !live;
        _state.Cursor = live ? Cursors.Arrow : Cursors.Hand;
        _state.ToolTip = live ? "The replay is at the live race" : $"{RaceFormat.Clock(behind)} behind the live race. Click to go back to live.";
        System.Windows.Automation.AutomationProperties.SetName(_state, live ? "Live" : "Back to live");
        if (live && s.Connected) { if (!_pulse.IsEnabled) _pulse.Start(); }
        else { _pulse.Stop(); _stateDot.Opacity = 1; }
        TextCase.Set(_clock, any ? RaceFormat.Clock(RaceTime(s.ReplayAt ?? s.Now)) : "");
    }

    private double RaceStart => _snap.RaceStart ?? (_snap.Events.Count > 0 ? _snap.Events[0].Time : _snap.Now);

    private double RaceTime(double sessionTime) => Math.Max(0, sessionTime - RaceStart);

    private void Watch(RaceMoment e)
    {
        if (e.Kind == RaceEventKind.NotRecorded) return;
        double at = e.Kind == RaceEventKind.FastestLap ? e.Time - e.Seconds : e.Time - _settings.Lead;
        int car = e.Kind == RaceEventKind.Incident ? _snap.Player : Perspective() ?? e.Car;
        JumpTo(at, _settings.CameraOnDriver ? car : null, e.Id);
    }

    private void JumpTo(double sessionTime, int? car, int? selected)
    {
        if (_snap.Driving)
        {
            _app.Notify("Race events", "iRacing shows the replay once you leave the car.", NoticeKind.Info);
            return;
        }
        if (!_feed.JumpTo(Math.Max(RaceStart, sessionTime), car))
        {
            _app.Notify("Race events", "Couldn't reach iRacing's replay. Is iRacing running?", NoticeKind.Info);
            return;
        }
        _selected = selected;
        SetFollow(false);
        Refresh(force: true);
    }

    private void WatchCar(int car)
    {
        if (_snap.Driving)
        {
            _app.Notify("Race events", "iRacing shows other cars once you leave the car.", NoticeKind.Info);
            return;
        }
        _feed.Watch(car);
        Refresh(force: true);
    }

    private void BackToLive()
    {
        _feed.ToLive();
        _selected = null;
        SetFollow(true);
        Refresh(force: true);
    }

    private void SetFollow(bool on)
    {
        if (_settings.FollowNew == on) return;
        _settings.FollowNew = on;
        _followTick.IsChecked = on;
        _app.Save();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ScreenshotRunner.Active && WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Width = Width;
            _settings.Height = Height;
            _app.Save();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CloseModal(false);
        _app.Tick -= OnTick;
        _pulse.Stop();
        if (ReferenceEquals(_open, this)) _open = null;
        base.OnClosed(e);
    }

    public static Border NumberPlate(string number, bool small = false)
    {
        var text = new TextBlock { Text = "#" + number, FontSize = small ? 11.5 : 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "MetricFont");
        var plate = Hairline.Frame(new Border { MinWidth = small ? 34 : 38, Height = small ? 18 : 22, Padding = new Thickness(5, 0, 5, 0), CornerRadius = ThemeManager.Round(4), Child = text, VerticalAlignment = VerticalAlignment.Center });
        plate.SetResourceReference(Border.BackgroundProperty, "TileRaised");
        plate.SetResourceReference(Border.BorderBrushProperty, "InputEdge");
        return plate;
    }

    public static async Task ShootKeybindsAsync(AppServices app, string folder)
    {
        Open(app, activate: false);
        if (_open is not { } w) return;
        for (int i = 0; i < 40 && !w._ready; i++) await Task.Delay(100);
        w.OpenReplayKeybinds();
        await Task.Delay(1200);
        System.IO.Directory.CreateDirectory(folder);
        ScreenshotRunner.CaptureWindow(w, System.IO.Path.Combine(folder, "race-events-keybinds.png"));
        w.Close();
    }

    public static async Task ShootAsync(AppServices app, string folder, string prefix)
    {
        Open(app, activate: false);
        if (_open is not { } w) return;
        for (int i = 0; i < 40 && !w._ready; i++) await Task.Delay(100);
        System.IO.Directory.CreateDirectory(folder);
        string[] names = { "mine", "rivals", "watched", "standings", "laps" };
        if (w._snap.Field.Keys.Where(k => k != w._snap.Player).Take(2).Select(k => w._snap.Car(k)?.Key).OfType<string>().ToList() is { Count: > 0 } rivals && w._settings.Rivals.Count == 0)
            w._settings.Rivals.AddRange(rivals);
        for (int i = 0; i < names.Length; i++)
        {
            Ui.SetSegment(w._tabs, i);
            await Task.Delay(900);
            ScreenshotRunner.CaptureWindow(w, System.IO.Path.Combine(folder, $"{prefix}-{names[i]}.png"));
        }
        Ui.SetSegment(w._tabs, 0);
        await Task.Delay(500);
        var first = w._snap.Events.LastOrDefault(e => e.Kind == RaceEventKind.Pass && (e.Car == w._snap.Player || e.Other == w._snap.Player));
        if (first is not null) w.Watch(first);
        await Task.Delay(900);
        ScreenshotRunner.CaptureWindow(w, System.IO.Path.Combine(folder, $"{prefix}-behind.png"));
        w.Close();
    }
}
