using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Core.Controls;
using Core.Services;
using Core.SimRacing.Race;
using Core.Theme;
using Glyph = Core.Controls.Icon;

namespace Core.Views.RaceEvents;

internal sealed partial class RaceEventsWindow
{
    private const int MineTab = 0, RivalsTab = 1, WatchedTab = 2, StandingsTab = 3, LapsTab = 4;

    private readonly HashSet<int> _seen = new();
    private ScrollViewer? _scroll;
    private DriverPicker? _rivalPicker, _camPicker;
    private string _sortColumn = "best";
    private bool _sortDescending;
    private bool PickerOpen => _rivalPicker?.IsOpen == true || _camPicker?.IsOpen == true;

    private int? Perspective() => _tab switch
    {
        MineTab => _snap.Player,
        WatchedTab => _snap.Camera >= 0 ? _snap.Camera : _snap.Player,
        _ => null,
    };

    private HashSet<int> RivalCars()
    {
        var keys = new HashSet<string>(_settings.Rivals, StringComparer.Ordinal);
        return _snap.Cars.Values.Where(c => keys.Contains(c.Key)).Select(c => c.CarIdx).ToHashSet();
    }

    private Func<RaceMoment, bool> InView()
    {
        if (_tab == RivalsTab)
        {
            var set = RivalCars();
            set.Add(_snap.Player);
            return e => e.Kind == RaceEventKind.NotRecorded || e.Kind != RaceEventKind.Incident && (set.Contains(e.Car) || e.Other >= 0 && set.Contains(e.Other));
        }
        int who = _tab == WatchedTab ? Perspective() ?? _snap.Player : _snap.Player;
        return e => e.Kind == RaceEventKind.NotRecorded || e.Car == who || e.Other == who;
    }

    private static string KindKey(RaceEventKind k) => k switch
    {
        RaceEventKind.Pass => "pass",
        RaceEventKind.OffTrack => "off",
        RaceEventKind.Incident => "incident",
        RaceEventKind.PitStop => "pit",
        RaceEventKind.FastestLap => "fastest",
        _ => "",
    };

    private bool Shown(RaceMoment e) => e.Kind == RaceEventKind.NotRecorded || _settings.Shows(KindKey(e.Kind));

    private void Build(bool fade)
    {
        _body.Children.Clear();
        UIElement content;
        if (!_snap.Connected && !_snap.HasRace)
            content = Centered(Ui.EmptyState("I.Flag", "Waiting for iRacing", "Events show here once you're in a session."));
        else content = _tab switch
        {
            StandingsTab => Standings(),
            LapsTab => LapTimes(),
            _ => EventsView(),
        };
        _body.Children.Add(content);
        if (fade) Motion.FadeIn(_body);
        Timeline();
    }

    private static FrameworkElement ListCard(UIElement content, double top) =>
        Hairline.Frame(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), ClipChild = true, Child = content, Margin = new Thickness(Side, top, Side, Side) });

    private static UIElement Centered(UIElement inner) => new Border { Child = inner, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    private UIElement EventsView()
    {
        var dock = new DockPanel { LastChildFill = true };
        FrameworkElement head = _tab == RivalsTab ? RivalBar() : DriverCard(Perspective() ?? _snap.Player, _tab == WatchedTab);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var filters = Filters();
        DockPanel.SetDock(filters, Dock.Top);
        dock.Children.Add(filters);
        dock.Children.Add(EventList());
        return dock;
    }

    private FrameworkElement Filters()
    {
        var row = new WrapPanel { Margin = new Thickness(Side + 4, 0, Side, 10), VerticalAlignment = VerticalAlignment.Center };
        var show = new TextBlock { Text = "Show", Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        row.Children.Add(show);
        bool incidents = _tab == MineTab || _tab == WatchedTab && Perspective() == _snap.Player;
        foreach (var (key, label, icon, brush) in new[]
        {
            ("pass", "Passes", "I.ArrowUp", "Sage"), ("off", "Off track", "I.OffTrack", "Warn"), ("incident", "Incidents", "I.Alert", "Danger"),
            ("pit", "Pit stops", "I.Wrench", "Slate"), ("fastest", "Fastest laps", "I.Clock", "AccentText"),
        })
        {
            if (key == "incident" && !incidents) continue;
            string k = key;
            var chip = new ToggleChip(label, icon, brush, _settings.Shows(k)) { Margin = new Thickness(0, 0, 6, 0) };
            chip.Changed += on =>
            {
                if (on) _settings.HiddenKinds.Remove(k);
                else if (!_settings.HiddenKinds.Contains(k)) _settings.HiddenKinds.Add(k);
                _app.Save();
                Build(fade: false);
            };
            row.Children.Add(chip);
        }
        var host = new Border { Child = row };
        if (_tab == RivalsTab)
        {
            Hairline.Frame(host, Sides.Top);
            host.SetResourceReference(Border.BorderBrushProperty, "Line");
            host.Padding = new Thickness(0, 10, 0, 0);
        }
        return host;
    }

    private FrameworkElement DriverCard(int car, bool picker)
    {
        var c = _snap.Car(car);
        _snap.Field.TryGetValue(car, out var now);
        bool multi = _snap.Multiclass;
        int position = Position(car);
        int classSize = c is null ? _snap.Cars.Count : _snap.Cars.Values.Count(x => !multi || x.ClassId == c.ClassId);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 56 };
        var big = new TextBlock { Text = RaceFormat.Pos(position), FontSize = 34, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 36, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        big.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        big.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        pos.Children.Add(big);
        pos.Children.Add(new TextBlock { Text = multi && c is not null ? $"of {classSize} in {c.ClassName}" : $"of {classSize}", Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center });
        var posHost = Hairline.Frame(new Border { Child = pos, Padding = new Thickness(0, 0, 16, 0) }, Sides.Right);
        posHost.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        grid.Children.Add(posHost);

        var id = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 12, 0) };
        FrameworkElement nameRow;
        var nameText = TextFit.TipWhenTrimmed(new TextBlock { Text = c?.Name ?? RaceFormat.Missing, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        var plate = NumberPlate(c?.Number ?? "–");
        plate.Margin = new Thickness(0, 0, 9, 0);
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(plate, Dock.Left);
        line.Children.Add(plate);
        line.Children.Add(nameText);
        if (picker)
        {
            var button = new Button { Style = Ui.Res<Style>("MenuButton"), Content = Ui.MenuFace(line), HorizontalAlignment = HorizontalAlignment.Left, Height = 32, Padding = new Thickness(4, 0, 9, 0), ToolTip = "Pick the car iRacing's camera shows" };
            System.Windows.Automation.AutomationProperties.SetName(button, "Watched car: " + (c?.Name ?? ""));
            button.Click += (_, _) => OpenCameraPicker(button);
            nameRow = button;
        }
        else nameRow = new TrimRow { Children = { line } };
        id.Children.Add(nameRow);
        var stats = new WrapPanel { Margin = new Thickness(picker ? 2 : 0, 5, 0, 0) };
        var laps = _snap.LapsOf(car);
        int lapNow = Math.Max(1, now.LapsDone + 1);
        if (_snap.LapsTotal is { } total) lapNow = Math.Min(lapNow, total);
        stats.Children.Add(Stat("Lap", _snap.LapsTotal is { } t2 ? $"{lapNow} of {t2}" : lapNow.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        double? best = Best(laps);
        double? classBest = ClassBest(c?.ClassId);
        stats.Children.Add(Stat("Best", RaceFormat.LapTime(best), best is { } b && classBest is { } cb && b <= cb ? "AccentText" : null));
        stats.Children.Add(Stat("Last", RaceFormat.LapTime(laps.Count > 0 ? laps[^1].Time : null)));
        id.Children.Add(stats);
        Grid.SetColumn(id, 1);
        grid.Children.Add(id);

        if (c?.Grid is { } start && position > 0)
        {
            int moved = start - position;
            var move = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var value = new TextBlock { Text = moved > 0 ? "+" + moved : moved < 0 ? "−" + (-moved) : "0", FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
            value.SetResourceReference(TextBlock.ForegroundProperty, moved > 0 ? "Sage" : moved < 0 ? "DangerDeep" : "TextMuted");
            move.Children.Add(value);
            move.Children.Add(new TextBlock { Text = "Started " + RaceFormat.Pos(start), Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Right });
            move.ToolTip = moved == 0 ? "Same place as the start" : $"{RaceFormat.Count(Math.Abs(moved), "place")} {(moved > 0 ? "gained" : "lost")} since the start";
            var moveHost = Hairline.Frame(new Border { Child = move, Padding = new Thickness(16, 0, 0, 0) }, Sides.Left);
            moveHost.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            Grid.SetColumn(moveHost, 2);
            grid.Children.Add(moveHost);
        }

        var stripe = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-17, 2, 0, 2), CornerRadius = ThemeManager.Round(2) };
        bool you = car == _snap.Player;
        if (you || ClassBrush(c) is not { } classBrush) stripe.SetResourceReference(Border.BackgroundProperty, "Accent");
        else stripe.Background = classBrush;
        grid.Children.Add(stripe);
        Grid.SetColumnSpan(stripe, 1);
        var card = new Tile { Style = Ui.Res<Style>("Card"), Child = grid, Padding = new Thickness(17, 11, 18, 11), Margin = new Thickness(Side, 10, Side, 12) };
        if (you && TryFindResource("AccentSoft") is SolidColorBrush soft && TryFindResource("Surface") is SolidColorBrush surface)
        {
            var wash = new LinearGradientBrush(soft.Color, surface.Color, new Point(0, 0.5), new Point(0.6, 0.5));
            wash.Freeze();
            card.Background = wash;
        }
        return card;
    }

    private static FrameworkElement Stat(string label, string value, string? brush = null)
    {
        var text = new TextBlock { Margin = new Thickness(0, 0, 16, 0), FontSize = 12.5 };
        var l = new Run(label + " ");
        l.SetResourceReference(TextElement.ForegroundProperty, "TextFaint");
        var v = new Run(value);
        v.SetResourceReference(TextElement.ForegroundProperty, brush ?? "Text");
        text.Inlines.Add(l);
        text.Inlines.Add(v);
        return text;
    }

    private Brush? ClassBrush(RaceCar? c)
    {
        if (c is null || !_snap.Multiclass) return null;
        var d = _snap.Session.ByCar.TryGetValue(c.CarIdx, out var entry) ? entry : null;
        return d is null ? null : ClassColors.For(d.ClassColor);
    }

    private int Position(int car)
    {
        if (!_snap.Field.TryGetValue(car, out var now)) return 0;
        return _snap.Multiclass && now.ClassPosition > 0 ? now.ClassPosition : now.Position;
    }

    private static double? Best(IReadOnlyList<RaceLap> laps)
    {
        double? best = null;
        foreach (var lap in laps)
            if (lap.Number > 1 && !lap.PitLap && lap.Time is { } t && (best is null || t < best)) best = t;
        return best;
    }

    private double? ClassBest(int? classId)
    {
        double? best = null;
        foreach (var (car, laps) in _snap.Laps)
        {
            if (classId is { } cls && _snap.Car(car)?.ClassId != cls) continue;
            if (Best(laps) is { } b && (best is null || b < best)) best = b;
        }
        return best;
    }

    private FrameworkElement RivalBar()
    {
        var rivals = RivalCars();
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        var plus = new Glyph { Data = Ui.Res<Geometry>("I.Plus"), Width = 12, Height = 12, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(plus);
        face.Children.Add(new TextBlock { Text = "Drivers", VerticalAlignment = VerticalAlignment.Center });
        if (rivals.Count > 0)
        {
            var count = new TextBlock { Text = rivals.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            count.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            var countHost = new Border { Child = count, Padding = new Thickness(6, 0, 6, 0), Height = 18, Margin = new Thickness(8, 0, 0, 0), CornerRadius = ThemeManager.Round(9) };
            countHost.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            face.Children.Add(countHost);
        }
        var button = new Button { Style = Ui.Res<Style>("MenuButton"), Content = Ui.MenuFace(face), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(button, "Pick drivers");
        button.Click += (_, _) => OpenRivals(button);
        _rivalAnchor = button;

        FrameworkElement tags;
        if (rivals.Count == 0)
            tags = new TextBlock { Text = "Pick drivers to see their events next to yours", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center };
        else
        {
            var panel = new FitTags { VerticalAlignment = VerticalAlignment.Center, Height = 28 };
            foreach (int car in rivals.OrderBy(Position))
                if (_snap.Car(car) is { } c) panel.Children.Add(RivalTag(c));
            var moreText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            var more = new Button { Style = Ui.Res<Style>("GhostButton"), Content = moreText, Height = 28, Padding = new Thickness(10, 0, 10, 0), Tag = new Action<int>(n => moreText.Text = $"+{n} more") };
            more.Click += (_, _) => OpenRivals(button);
            panel.Children.Add(more);
            tags = panel;
        }
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(Side + 4, 12, Side, 10) };
        DockPanel.SetDock(button, Dock.Left);
        row.Children.Add(button);
        row.Children.Add(tags);
        return row;
    }

    private Button? _rivalAnchor;

    private FrameworkElement RivalTag(RaceCar c)
    {
        var grid = new Grid { Height = 28 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Border();
        if (ClassBrush(c) is { } brush) bar.Background = brush; else bar.SetResourceReference(Border.BackgroundProperty, "Accent");
        grid.Children.Add(bar);
        var plate = NumberPlate(c.Number, small: true);
        plate.Margin = new Thickness(7, 0, 7, 0);
        Grid.SetColumn(plate, 1);
        grid.Children.Add(plate);
        var name = new TextBlock { Text = RaceFormat.Name(c.Name, "initial"), FontWeight = FontWeights.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        var remove = Ui.IconButton("I.X", "Remove " + c.Name, () => { _settings.Rivals.Remove(c.Key); _app.Save(); Build(fade: false); }, "QuietIconButton");
        remove.Margin = new Thickness(4, 0, 2, 0);
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);
        var tag = Hairline.Frame(new Border { Child = grid, CornerRadius = ThemeManager.Round(6), ClipToBounds = true, ToolTip = c.Name });
        tag.SetResourceReference(Border.BackgroundProperty, "TileRaised");
        tag.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        return tag;
    }

    private IReadOnlyList<PickerDriver> PickerDrivers(Func<int, bool> picked) =>
        _snap.Cars.Values
            .Select(c => new PickerDriver(c.CarIdx, c.Number, c.Name, Position(c.CarIdx), ClassBrush(c), picked(c.CarIdx), c.CarIdx == _snap.Player))
            .OrderBy(d => d.Position > 0 ? d.Position : int.MaxValue).ThenBy(d => d.Name, StringComparer.CurrentCulture).ToList();

    internal void OpenRivals(Button? anchor = null)
    {
        if ((anchor ?? _rivalAnchor) is not { } a) return;
        _rivalPicker = new DriverPicker(a, multi: true, note: null,
            () => PickerDrivers(car => RivalCars().Contains(car)).Where(d => !d.IsYou).ToList(),
            car =>
            {
                if (_snap.Car(car) is not { } c) return;
                if (!_settings.Rivals.Remove(c.Key)) _settings.Rivals.Add(c.Key);
                _app.Save();
                RebuildUnderPicker();
            },
            () => { _settings.Rivals.Clear(); _app.Save(); RebuildUnderPicker(); });
        _rivalPicker.Open();
    }

    private void RebuildUnderPicker()
    {
        var picker = _rivalPicker;
        _rivalPicker = null;
        Build(fade: false);
        _rivalPicker = picker;
    }

    private void OpenCameraPicker(Button anchor)
    {
        _camPicker = new DriverPicker(anchor, multi: false, note: "Picking a driver moves iRacing's camera",
            () => PickerDrivers(car => car == (Perspective() ?? -1)),
            WatchCar);
        _camPicker.Open();
    }

    private UIElement EventList()
    {
        var who = Perspective();
        var inView = InView();
        var rows = _snap.Events.Where(e => inView(e) && Shown(e)).ToList();
        var list = new StackPanel();
        if (rows.Count == 0)
        {
            bool any = _snap.Events.Any(inView);
            list.Children.Add(Centered(Ui.EmptyState("I.Flag", "No events yet", any ? "Every kind is switched off above." : "Passes, off tracks, incidents and pit stops show here as they happen.")));
        }
        int owner = who ?? _snap.Player;
        var lapTimes = _snap.LapsOf(owner);
        double? best = Best(lapTimes);
        int lastLap = -1;
        bool firstBuild = _seen.Count == 0;
        for (int k = rows.Count - 1; k >= 0; k--)
        {
            var e = rows[k];
            int lap = LapAt(lapTimes, e.Time) ?? e.Lap;
            if (lap != lastLap)
            {
                lastLap = lap;
                list.Children.Add(LapHeader(owner, lap, lapTimes, best));
            }
            var row = EventRow(e, who);
            list.Children.Add(row);
            if (!_seen.Contains(e.Id))
            {
                _seen.Add(e.Id);
                if (!firstBuild) Motion.FadeIn(row);
            }
        }
        double keep = _scroll?.VerticalOffset ?? 0;
        var scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = list, Focusable = false };
        var host = ListCard(scroll, top: 0);
        _scroll = scroll;
        scroll.Loaded += (_, _) =>
        {
            scroll.ScrollToVerticalOffset(_settings.FollowNew ? 0 : keep);
            scroll.ScrollChanged += (_, a) =>
            {
                if (a.VerticalChange > 0 && scroll.VerticalOffset > 4 && _settings.FollowNew && Mouse.LeftButton == MouseButtonState.Released) SetFollow(false);
            };
        };
        return host;
    }

    private static int? LapAt(IReadOnlyList<RaceLap> laps, double time)
    {
        if (laps.Count == 0) return null;
        RaceLap? current = null;
        foreach (var lap in laps)
            if (lap.Start <= time) current = lap;
        if (current is null) return Math.Max(1, laps[0].Number - 1);
        bool after = ReferenceEquals(current, laps[^1]) && current.Time is { } t && time >= current.Start + t;
        return after ? current.Number + 1 : current.Number;
    }

    private void ScrollToNewest() => _scroll?.ScrollToVerticalOffset(0);

    private FrameworkElement LapHeader(int owner, int lap, IReadOnlyList<RaceLap> laps, double? best)
    {
        var info = laps.FirstOrDefault(l => l.Number == lap);
        var dock = new DockPanel { LastChildFill = false, Height = 30, Margin = new Thickness(Side + 4, 0, Side + 4, 0) };
        var label = new TextBlock { Text = "Lap " + lap.ToString(System.Globalization.CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        dock.Children.Add(label);
        var time = new TextBlock { Text = RaceFormat.LapTime(info?.Time), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        time.SetResourceReference(TextBlock.ForegroundProperty, info?.Time is { } t && best is { } b && t <= b && info.Number > 1 && !info.PitLap ? "AccentText" : "Text");
        DockPanel.SetDock(time, Dock.Right);
        dock.Children.Add(time);
        var head = Hairline.Frame(new Border { Child = dock }, Sides.Bottom);
        head.SetResourceReference(Border.BorderBrushProperty, "Line");
        head.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        if (info?.Start is { } s)
        {
            head.Cursor = Cursors.Hand;
            head.ToolTip = "Watch this lap from its start";
            head.MouseEnter += (_, _) => head.SetResourceReference(Border.BackgroundProperty, "Hover");
            head.MouseLeave += (_, _) => head.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            head.MouseLeftButtonUp += (_, e) => { e.Handled = true; JumpTo(s, _settings.CameraOnDriver ? owner : null, null); };
        }
        return head;
    }

    private (string Kind, string Icon, string Brush) Look(RaceMoment e, int? who) => e.Kind switch
    {
        RaceEventKind.Pass when who is { } w && e.Other == w => ("loss", "I.ArrowDown", "DangerDeep"),
        RaceEventKind.Pass => ("gain", "I.ArrowUp", "Sage"),
        RaceEventKind.OffTrack => ("off", "I.OffTrack", "Warn"),
        RaceEventKind.Incident => ("incident", "I.Alert", "Danger"),
        RaceEventKind.PitStop => ("pit", "I.Wrench", "Slate"),
        RaceEventKind.FastestLap => ("fastest", "I.Clock", "AccentText"),
        _ => ("none", "I.Info", "TextFaint"),
    };

    private string NameOf(int car, bool start)
    {
        if (car == _snap.Player) return start ? "You" : "you";
        return _snap.Car(car) is { } c ? RaceFormat.Name(c.Name, "initial") : RaceFormat.Missing;
    }

    private string Where(double pct) => pct >= 0 ? _turns.Where(pct) : RaceFormat.Missing;

    internal (List<(string Text, bool Strong, bool Soft)> Parts, string Detail, string? DetailBrush) Describe(RaceMoment e, int? who)
    {
        var parts = new List<(string, bool, bool)>();
        void Add(string text, bool strong = false, bool soft = false) => parts.Add((text, strong, soft));
        void Name(int car, bool start) { string n = NameOf(car, start); Add(n, car != _snap.Player); }
        bool mine = who is not null;
        string detail = "";
        string? brush = null;
        switch (e.Kind)
        {
            case RaceEventKind.Pass when mine && who == e.Car:
                Add("Passed "); Name(e.Other, false);
                detail = "Up to " + RaceFormat.Pos(e.Position); brush = "Sage";
                break;
            case RaceEventKind.Pass when mine && who == e.Other:
                Add("Passed by "); Name(e.Car, false);
                detail = "Down to " + RaceFormat.Pos(e.Position + 1); brush = "DangerDeep";
                break;
            case RaceEventKind.Pass:
                Name(e.Car, true); Add(" passed "); Name(e.Other, false);
                detail = RaceFormat.Pos(e.Position);
                break;
            case RaceEventKind.OffTrack:
                if (mine) { Add("Off track"); Add(" at " + Where(e.Pct), soft: true); }
                else { Name(e.Car, true); Add(" off track at " + Where(e.Pct), soft: true); }
                break;
            case RaceEventKind.Incident:
                Add("Incident"); Add($" {e.Total}x so far", soft: true);
                detail = $"+{e.Points}x"; brush = "DangerDeep";
                break;
            case RaceEventKind.PitStop:
                if (mine) { Add("Pit stop"); Add($" {e.Seconds:0.0} {Units.S} in pit lane", soft: true); }
                else { Name(e.Car, true); Add(" pit stop", soft: true); }
                if (e.PositionBefore > 0 && e.Position > 0 && e.Position != e.PositionBefore) detail = $"{RaceFormat.Pos(e.PositionBefore)} → {RaceFormat.Pos(e.Position)}";
                break;
            case RaceEventKind.FastestLap:
                if (mine && e.Car == who) Add("Fastest lap of the race");
                else { Name(e.Car, true); Add(" fastest lap", soft: true); }
                detail = RaceFormat.LapTime(e.Seconds); brush = "AccentText";
                break;
            default:
                Add("Not recorded while you watched the replay", soft: true);
                detail = $"{RaceFormat.Clock(RaceTime(e.Time))}–{RaceFormat.Clock(RaceTime(e.End))}";
                break;
        }
        return (parts, detail, brush);
    }

    internal string Plain(RaceMoment e, int? who) => string.Concat(Describe(e, who).Parts.Select(p => p.Text));

    private FrameworkElement EventRow(RaceMoment e, int? who)
    {
        var (_, iconKey, brushKey) = Look(e, who);
        var (parts, detail, detailBrush) = Describe(e, who);
        var grid = new Grid { MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        var icon = new Glyph { Data = Ui.Res<Geometry>(iconKey), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        icon.SetResourceReference(Glyph.ForegroundProperty, brushKey);
        grid.Children.Add(icon);
        var text = TextFit.TipWhenTrimmed(new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        text.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        foreach (var (t, strong, soft) in parts)
        {
            var run = new Run(t);
            if (strong) run.FontWeight = FontWeights.SemiBold;
            if (soft) run.SetResourceReference(TextElement.ForegroundProperty, "TextMuted");
            text.Inlines.Add(run);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var det = new TextBlock { Text = detail, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        det.SetResourceReference(TextBlock.ForegroundProperty, detailBrush ?? "TextMuted");
        Grid.SetColumn(det, 2);
        grid.Children.Add(det);
        var time = new TextBlock { Text = e.Kind == RaceEventKind.NotRecorded ? "" : RaceFormat.Clock(RaceTime(e.Time)), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        time.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        Grid.SetColumn(time, 3);
        grid.Children.Add(time);
        bool selected = _selected == e.Id;
        var row = new Border { Child = grid, Padding = new Thickness(Side + 1, 0, Side + 4, 0), BorderThickness = new Thickness(3, 0, 0, 0) };
        var line = Hairline.Frame(new Border { Child = row }, Sides.Bottom);
        line.SetResourceReference(Border.BorderBrushProperty, "GridLine");
        if (selected)
        {
            row.SetResourceReference(Border.BorderBrushProperty, "Accent");
            row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        }
        else
        {
            row.BorderBrush = Brushes.Transparent;
            row.Background = Brushes.Transparent;
        }
        if (e.Kind == RaceEventKind.NotRecorded) return line;
        row.Cursor = Cursors.Hand;
        row.Focusable = true;
        row.FocusVisualStyle = Ui.Res<Style>("FocusRing");
        row.ToolTip = "Watch this in iRacing's replay";
        System.Windows.Automation.AutomationProperties.SetName(row, Plain(e, who) + " " + detail);
        if (!selected) Ui.HoverHighlight(row);
        row.MouseLeftButtonUp += (_, a) => { a.Handled = true; Watch(e); };
        row.KeyDown += (_, a) => { if (a.Key is Key.Enter or Key.Space) { a.Handled = true; Watch(e); } };
        return line;
    }

    private UIElement Standings()
    {
        bool multi = _snap.Multiclass;
        var all = _snap.Cars.Values
            .Select(c => (Car: c, Now: _snap.Field.TryGetValue(c.CarIdx, out var n) ? n : default))
            .OrderBy(x => x.Now.Position > 0 ? x.Now.Position : int.MaxValue).ThenByDescending(x => x.Now.Progress).ToList();
        var classOrder = all.Select(x => x.Car.ClassId).Distinct().ToList();
        var cars = multi
            ? all.OrderBy(x => classOrder.IndexOf(x.Car.ClassId)).ThenBy(x => x.Now.ClassPosition > 0 ? x.Now.ClassPosition : int.MaxValue).ThenByDescending(x => x.Now.Progress).ToList()
            : all;
        var leaders = new Dictionary<int, RaceCarNow>();
        foreach (var x in cars)
            if (!leaders.ContainsKey(multi ? x.Car.ClassId : 0) && x.Now.Position > 0) leaders[multi ? x.Car.ClassId : 0] = x.Now;
        var table = new Table(new (string, double, bool)[] { ("Pos", 46, false), ("Driver", -1, false), ("Laps", 60, true), ("Gap", 84, true), ("Stops", 60, true), ("", 104, true) });
        var stops = _snap.Events.Where(e => e.Kind == RaceEventKind.PitStop).GroupBy(e => e.Car).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (car, now) in cars)
        {
            int idx = car.CarIdx;
            var leader = leaders.GetValueOrDefault(multi ? car.ClassId : 0);
            bool leads = now.Position > 0 && leader == now;
            string gap = leads ? "Leader"
                : now.Position <= 0 ? RaceFormat.Missing
                : leader.LapsDone - now.LapsDone >= 1 && leader.Progress - now.Progress >= 1 ? RaceFormat.Count(leader.LapsDone - now.LapsDone, "lap")
                : now.Gap - leader.Gap > 0 ? RaceFormat.Gap(now.Gap - leader.Gap) : RaceFormat.Missing;
            int n = stops.GetValueOrDefault(idx);
            FrameworkElement action;
            if (idx == _snap.Camera)
            {
                var watching = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var cam = new Glyph { Data = Ui.Res<Geometry>("I.Video"), Width = 13, Height = 13, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                cam.SetResourceReference(Glyph.ForegroundProperty, "Slate");
                var w = new TextBlock { Text = "Watching", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                w.SetResourceReference(TextBlock.ForegroundProperty, "Slate");
                watching.Children.Add(cam);
                watching.Children.Add(w);
                action = watching;
            }
            else action = HoverButton("Watch", "Put iRacing's camera on " + car.Name, () => WatchCar(idx));
            table.Add(idx == _snap.Player, () => WatchCar(idx),
                Cell(RaceFormat.Pos(now.Position > 0 ? (_snap.Multiclass ? now.ClassPosition : now.Position) : 0), big: true),
                DriverCell(car),
                Cell(now.LapsDone.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                Cell(gap),
                Cell(n > 0 ? n.ToString(System.Globalization.CultureInfo.CurrentCulture) : RaceFormat.Missing, n > 0 ? "Slate" : "TextFaint"),
                action);
        }
        return ListCard(table.Build(), top: 12);
    }

    private UIElement LapTimes()
    {
        var rows = _snap.Cars.Values.Select(c =>
        {
            var laps = _snap.LapsOf(c.CarIdx);
            var clean = laps.Where(l => l.Number > 1 && !l.PitLap && l.Time is not null).ToList();
            var bestLap = clean.OrderBy(l => l.Time).FirstOrDefault();
            var last5 = clean.TakeLast(5).ToList();
            double? avg = last5.Count == 5 ? last5.Average(l => l.Time!.Value) : null;
            return (Car: c, Laps: laps.Count > 0 ? laps[^1].Number : 0, Best: bestLap, Last: laps.Count > 0 ? laps[^1].Time : null, Avg: avg);
        }).ToList();
        Func<(RaceCar Car, int Laps, RaceLap? Best, double? Last, double? Avg), double> key = _sortColumn switch
        {
            "laps" => r => -r.Laps,
            "last" => r => r.Last ?? double.MaxValue,
            "avg" => r => r.Avg ?? double.MaxValue,
            _ => r => r.Best?.Time ?? double.MaxValue,
        };
        rows = (_sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ToList();
        var classBests = new Dictionary<int, double>();
        foreach (var r in rows)
            if (r.Best?.Time is { } t && (!classBests.TryGetValue(r.Car.ClassId, out var b) || t < b)) classBests[r.Car.ClassId] = t;
        var table = new Table(new (string, double, bool)[] { ("Driver", -1, false), ("Laps", 60, true), ("Best", 96, true), ("Last", 96, true), ("Last 5 avg", 104, true), ("", 116, true) },
            new[] { "", "laps", "best", "last", "avg", "" }, _sortColumn, _sortDescending, col =>
            {
                if (_sortColumn == col) _sortDescending = !_sortDescending;
                else { _sortColumn = col; _sortDescending = false; }
                Build(fade: false);
            });
        foreach (var r in rows)
        {
            bool fastest = r.Best?.Time is { } bt && classBests.TryGetValue(r.Car.ClassId, out var cb) && bt <= cb;
            int idx = r.Car.CarIdx;
            var best = r.Best;
            FrameworkElement action = best is not null
                ? HoverButton("Watch best", $"Replay lap {best.Number} from its start", () => JumpTo(best.Start, _settings.CameraOnDriver ? idx : null, null))
                : new Border();
            table.Add(idx == _snap.Player, null,
                DriverCell(r.Car),
                Cell(r.Laps.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                Cell(RaceFormat.LapTime(best?.Time), fastest ? "AccentText" : null),
                Cell(RaceFormat.LapTime(r.Last)),
                Cell(RaceFormat.LapTime(r.Avg), r.Avg is null ? "TextFaint" : null),
                action);
        }
        var dock = new DockPanel { LastChildFill = true };
        var note = new TextBlock { Text = "Best and average leave out the first lap and pit laps", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(Side + 4, 12, Side, 10) };
        DockPanel.SetDock(note, Dock.Top);
        dock.Children.Add(note);
        dock.Children.Add(ListCard(table.Build(), top: 0));
        return dock;
    }

    private static TextBlock Cell(string text, string? brush = null, bool big = false)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = big ? 15 : 13.5 };
        if (big)
        {
            t.FontWeight = FontWeights.Bold;
            t.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        }
        t.SetResourceReference(TextBlock.ForegroundProperty, brush ?? (big ? "Heading" : "Text"));
        return t;
    }

    private FrameworkElement DriverCell(RaceCar c)
    {
        var plate = NumberPlate(c.Number, small: true);
        plate.Margin = new Thickness(0, 0, 9, 0);
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = c.Name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        var line = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(plate, Dock.Left);
        line.Children.Add(plate);
        if (ClassBrush(c) is { } brush)
        {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = ThemeManager.Round(4), Background = brush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = c.ClassName };
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Insert(0, dot);
        }
        line.Children.Add(name);
        return new TrimRow { Children = { line }, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Button HoverButton(string text, string tip, Action click)
    {
        var b = new Button { Content = text, Style = Ui.Res<Style>("GhostButton"), ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, Height = 26, Padding = new Thickness(10, 0, 10, 0), FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, e) => { e.Handled = true; click(); };
        b.GotKeyboardFocus += (_, _) => b.Opacity = 1;
        b.LostKeyboardFocus += (_, _) => b.Opacity = b.IsMouseOver ? 1 : 0;
        return b;
    }

    private void Timeline()
    {
        var s = _snap;
        bool off = !s.HasRace && !s.Connected;
        double start = RaceStart;
        double end = Math.Max(s.Now + 1, start + 60);
        int owner = Perspective() ?? s.Player;
        var laps = s.LapsOf(owner);
        if (s.TimeTotal is { } total && total < 7 * 24 * 3600 && total > s.Now) end = total;
        else if (s.LapsTotal is { } lapsTotal && Best(laps) is { } typical) end = Math.Max(end, start + lapsTotal * typical * 1.02);
        else end = Math.Max(end, s.Now + (s.Now - start) * 0.08);
        var inView = InView();
        var who = Perspective();
        var ticks = s.Events.Where(e => e.Kind != RaceEventKind.NotRecorded && inView(e) && Shown(e))
            .Select(e => new TimelineTick(e.Time, Look(e, who).Brush, e.Id, $"{Plain(e, who)} · Lap {e.Lap} · {RaceFormat.Clock(RaceTime(e.Time))}")).ToList();
        var marks = laps.Select(l => l.Start).Where(t => t > start).ToList();
        var labels = new List<(double, string)> { (start, "Lap 1") };
        int maxLap = s.LapsTotal ?? (laps.Count > 0 ? laps[^1].Number + 1 : 1);
        foreach (int lap in new[] { maxLap / 3, maxLap * 2 / 3 }.Where(l => l > 1).Distinct())
            if (laps.FirstOrDefault(l => l.Number == lap) is { } known) labels.Add((known.Start, "Lap " + lap));
        if (s.LapsTotal is { } lt && lt > 1) labels.Add((end, "Lap " + lt));
        _timeline.Set(start, end, s.Now, s.ReplayAt, ticks, marks, labels, _selected, off, t =>
        {
            var lap = laps.LastOrDefault(l => l.Start <= t);
            return $"Watch from {RaceFormat.Clock(RaceTime(t))}" + (lap is not null ? $" · Lap {lap.Number}" : "");
        });
    }

    private sealed class Table
    {
        private readonly (string Title, double Width, bool Right)[] _cols;
        private readonly string[]? _sortKeys;
        private readonly string _sorted;
        private readonly bool _descending;
        private readonly Action<string>? _sort;
        private readonly StackPanel _rows = new();

        public Table((string, double, bool)[] cols, string[]? sortKeys = null, string sorted = "", bool descending = false, Action<string>? sort = null)
        {
            _cols = cols;
            _sortKeys = sortKeys;
            _sorted = sorted;
            _descending = descending;
            _sort = sort;
        }

        private Grid Columns()
        {
            var g = new Grid();
            foreach (var c in _cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(c.Width) });
            return g;
        }

        public void Add(bool you, Action? click, params FrameworkElement[] cells)
        {
            var g = Columns();
            g.Height = 38;
            for (int i = 0; i < cells.Length && i < _cols.Length; i++)
            {
                var cell = cells[i];
                cell.HorizontalAlignment = _cols[i].Right ? HorizontalAlignment.Right : cell is TrimRow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                cell.Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0);
                Grid.SetColumn(cell, i);
                g.Children.Add(cell);
            }
            var row = new Border { Child = g, Padding = new Thickness(Side + 1, 0, Side + 4, 0), BorderThickness = new Thickness(3, 0, 0, 0) };
            if (you)
            {
                row.SetResourceReference(Border.BorderBrushProperty, "Accent");
                row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            }
            else
            {
                row.BorderBrush = Brushes.Transparent;
                row.Background = Brushes.Transparent;
            }
            var buttons = cells.OfType<Button>().ToList();
            row.MouseEnter += (_, _) => { if (!you) row.SetResourceReference(Border.BackgroundProperty, "Hover"); foreach (var b in buttons) b.Opacity = 1; };
            row.MouseLeave += (_, _) => { if (!you) row.Background = Brushes.Transparent; foreach (var b in buttons) if (!b.IsKeyboardFocused) b.Opacity = 0; };
            if (click is not null)
            {
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (_, e) => { if (e.Handled) return; e.Handled = true; click(); };
            }
            var line = Hairline.Frame(new Border { Child = row }, Sides.Bottom);
            line.SetResourceReference(Border.BorderBrushProperty, "GridLine");
            _rows.Children.Add(line);
        }

        public UIElement Build()
        {
            var head = Columns();
            head.Height = 30;
            for (int i = 0; i < _cols.Length; i++)
            {
                FrameworkElement cell;
                string? sortKey = _sortKeys is not null && i < _sortKeys.Length && _sortKeys[i].Length > 0 ? _sortKeys[i] : null;
                if (sortKey is not null && _sort is not null)
                {
                    var label = new StackPanel { Orientation = Orientation.Horizontal };
                    var text = Ui.TableHead(_cols[i].Title);
                    if (_sorted == sortKey) text.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                    label.Children.Add(text);
                    if (_sorted == sortKey)
                    {
                        var arrow = new Glyph { Data = Ui.Res<Geometry>(_descending ? "I.ChevronDown" : "I.ChevronUp"), Width = 10, Height = 10, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        arrow.SetResourceReference(Glyph.ForegroundProperty, "AccentText");
                        label.Children.Add(arrow);
                    }
                    var b = new Button { Style = Ui.Res<Style>("QuietButton"), Content = label, Padding = new Thickness(0), ToolTip = "Sort by this column" };
                    string k = sortKey;
                    b.Click += (_, _) => _sort(k);
                    cell = b;
                }
                else cell = Ui.TableHead(_cols[i].Title);
                cell.VerticalAlignment = VerticalAlignment.Center;
                cell.HorizontalAlignment = _cols[i].Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                cell.Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0);
                Grid.SetColumn(cell, i);
                head.Children.Add(cell);
            }
            var headHost = Hairline.Frame(new Border { Child = head, Padding = new Thickness(Side + 4, 0, Side + 4, 0) }, Sides.Bottom);
            headHost.SetResourceReference(Border.BorderBrushProperty, "Line");
            headHost.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            var scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _rows, Focusable = false };
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(headHost, Dock.Top);
            dock.Children.Add(headHost);
            dock.Children.Add(scroll);
            return dock;
        }
    }
}
