using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal static class StatsCarsTab
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;

    internal sealed record Pair(string Car, string Track, string Config, int Sessions, long Laps, DateTime Last, double DriveS, double? Best, int? TrackId, string TrackName)
    {
        public string Key => $"{Car}|{Track}|{Config}";
        public string Place => StatFormat.TrackAndConfig(Track, Config);
    }

    internal sealed record PairDetail(Pair Pair, StatsCharts.ComboCharts Combo, List<SectorLine> Sectors, List<double> Clean, (DateTime When, double Lap)? First, DateTime? BestWhen);

    private static readonly (string Id, string Name)[] Groups = { ("car", "Car"), ("track", "Track"), ("none", "No groups") };
    private static readonly (string Id, string Name)[] Sorts = { ("driven", "Most driven"), ("latest", "Latest"), ("gain", "Fastest gain"), ("az", "A–Z") };

    internal static List<Pair> ReadPairs(StatsStore store, StatQuery q)
    {
        var db = store.Db;
        var bests = db.Query("""
            SELECT s.car, s.track, s.track_config, MIN(l.time) FROM laps l JOIN sessions s ON s.id = l.session_id
            WHERE l.clean = 1 GROUP BY s.car, s.track, s.track_config
            """).ToDictionary(r => $"{r[0]}|{r[1]}|{r[2]}", r => Convert.ToDouble(r[3]));
        return db.Query(StatRegistry.Scope + """
            SELECT car, track, track_config, COUNT(*), SUM(laps), MAX(start_utc), SUM(drive_s), MAX(track_id), MAX(track_name)
            FROM s GROUP BY car, track, track_config
            """, q.From, q.To)
            .Select(r =>
            {
                string car = (string)(r[0] ?? ""), track = (string)(r[1] ?? ""), config = (string)(r[2] ?? "");
                return new Pair(car, track, config, Convert.ToInt32(r[3]), Convert.ToInt64(r[4] ?? 0L), StatsStore.FromUnix(Convert.ToInt64(r[5])), Convert.ToDouble(r[6] ?? 0d),
                    bests.TryGetValue($"{car}|{track}|{config}", out double best) ? best : null, r[7] is null ? null : Convert.ToInt32(r[7]), r[8] as string ?? "");
            }).ToList();
    }

    private static PairDetail ReadDetail(StatsStore store, Pair p)
    {
        var combo = StatsCharts.ReadCombo(store, p.Car, p.Track, p.Config, p.TrackId, p.TrackName);
        var ids = store.Db.Query("SELECT id FROM sessions WHERE car = ?1 AND track = ?2 AND track_config = ?3 ORDER BY start_utc", p.Car, p.Track, p.Config).Select(r => Convert.ToInt64(r[0])).ToList();
        var laps = ids.SelectMany(store.Laps).ToList();
        var clean = laps.Where(l => l.Clean).Select(l => l.Time).ToList();
        var firsts = StatsCharts.SessionBests(store.Db, p.Car, p.Track, p.Config);
        return new PairDetail(p, combo, SectorReport.Make(laps), clean, firsts.Count > 0 ? firsts[0] : null, combo.Timeline.Count > 0 ? combo.Timeline[^1].When : null);
    }

    private static double Gain(Pair p, Dictionary<string, double> firsts) => p.Best is { } b && firsts.TryGetValue(p.Key, out double f) ? f - b : 0;

    public static FrameworkElement Build(StatsTabData data, IStatsHost host)
    {
        var state = host.State;
        var pairs = new List<Pair>();
        var firsts = new Dictionary<string, double>();
        var rows = new StackPanel();
        var detailHost = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var count = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) };
        var carsButton = StatsTabs.MenuButton("All cars", 120, "Which cars are listed");
        var viewButton = StatsTabs.MenuButton("", 150, "How the list is grouped and sorted");
        int version = 0;
        string? shown = null;

        void ShowPair(Pair p, bool animate)
        {
            int mine = ++version;
            shown = p.Key;
            var store = data.Store;
            _ = Load();
            async Task Load()
            {
                var detail = await Task.Run(() => ReadDetail(store, p));
                if (mine != version) return;
                var view = Detail(detail);
                detailHost.Content = view;
                if (animate) Motion.FadeIn(view);
            }
        }

        void SetText(Button button, string text) =>
            button.Content = Ui.MenuFace(TextFit.TipWhenTrimmed(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));

        void Fill()
        {
            rows.Children.Clear();
            var cars = pairs.Select(p => p.Car).Distinct().ToList();
            int shownCars = cars.Count(c => !state.HiddenCars.Contains(c));
            SetText(carsButton, shownCars == cars.Count ? "All cars" : shownCars == 0 ? "No cars" : shownCars == 1 ? cars.First(c => !state.HiddenCars.Contains(c)) : $"{shownCars} of {RaceFormat.Count(cars.Count, "car")}");
            carsButton.Style = Ui.Res<Style>(shownCars < cars.Count ? "SelectedButton" : "MenuButton");
            string groupName = Groups.First(g => g.Id == state.PairGroup).Name;
            SetText(viewButton, (state.PairGroup == "none" ? groupName : state.PairGroup == "car" ? "By car" : "By track") + " · " + Sorts.First(s => s.Id == state.PairSort).Name);
            string q = state.PairSearch.Trim();
            var list = pairs.Where(p => !state.HiddenCars.Contains(p.Car) && (q.Length == 0 || $"{p.Place} {p.Car}".Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
            Comparison<Pair> order = state.PairSort switch
            {
                "latest" => (a, b) => b.Last.CompareTo(a.Last),
                "gain" => (a, b) => Gain(b, firsts).CompareTo(Gain(a, firsts)),
                "az" => (a, b) => string.Compare(a.Place + a.Car, b.Place + b.Car, StringComparison.CurrentCultureIgnoreCase),
                _ => (a, b) => b.DriveS.CompareTo(a.DriveS),
            };
            list.Sort(order);
            count.Text = $"{list.Count} of {RaceFormat.Count(pairs.Count, "car and track pair")}";
            if (list.Count == 0)
            {
                rows.Children.Add(Ui.EmptyState("I.Search", pairs.Count == 0 ? "No cars or tracks" : "No matches",
                    pairs.Count == 0 ? $"Nothing driven {data.RangeName}." : q.Length > 0 ? $"No car or track matches “{q}”. Search looks in track and car names." : "No cars picked. Tick some in the car menu."));
                shown = null;
                detailHost.Content = null;
                return;
            }
            if (state.Pair is null || list.All(p => p.Key != state.Pair)) state.Pair = list[0].Key;

            void Add(Pair p, string top, string bottom, bool indent)
            {
                var pair = p;
                var figure = StatsTabs.Figure(p.Best is { } b ? StatFormat.LapTime(b) : RaceFormat.Missing, "Last " + Dates.Day(p.Last.ToLocalTime()));
                var content = StatsTabs.TwoLines(top, bottom, figure);
                if (indent) content.Margin = new Thickness(26, 0, 12, 0);
                rows.Children.Add(StatsTabs.Row(content, p.Key == state.Pair, "statsPairs", () => { state.Pair = pair.Key; ShowPair(pair, true); }));
            }

            if (state.PairGroup == "none")
                foreach (var p in list) Add(p, p.Place, $"{p.Car} · {StatsTabs.SessionsText(p.Sessions)}", false);
            else
            {
                bool byCar = state.PairGroup == "car";
                var groups = list.GroupBy(p => byCar ? p.Car : p.Place).ToList();
                groups = state.PairSort switch
                {
                    "az" => groups.OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    "latest" => groups.OrderByDescending(g => g.Max(p => p.Last)).ToList(),
                    "gain" => groups.OrderByDescending(g => g.Max(p => Gain(p, firsts))).ToList(),
                    _ => groups.OrderByDescending(g => g.Sum(p => p.DriveS)).ToList(),
                };
                foreach (var g in groups)
                {
                    string key = (byCar ? "car:" : "track:") + g.Key;
                    bool open = q.Length > 0 || !state.FoldedGroups.Contains(key);
                    var head = new Grid { Margin = new Thickness(10, 0, 12, 0) };
                    head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    head.ColumnDefinitions.Add(new ColumnDefinition());
                    head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    head.Children.Add(Ui.FoldChevron(open));
                    var name = TextFit.TipWhenTrimmed(new TextBlock { Text = g.Key, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                    name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                    Grid.SetColumn(name, 1);
                    head.Children.Add(name);
                    int n = g.Count();
                    var note = new TextBlock { Text = $"{RaceFormat.Count(n, byCar ? "track" : "car")} · {StatsTabs.SessionsText(g.Sum(p => p.Sessions))}", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                    Grid.SetColumn(note, 2);
                    head.Children.Add(note);
                    var fold = new Button { Style = Ui.Res<Style>("QuietButton"), Content = head, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 7, 0, 7), Margin = new Thickness(0, rows.Children.Count > 0 ? 4 : 2, 0, 0) };
                    fold.SetResourceReference(Control.BackgroundProperty, "SurfaceAlt");
                    System.Windows.Automation.AutomationProperties.SetName(fold, g.Key);
                    fold.Click += (_, _) =>
                    {
                        if (!state.FoldedGroups.Remove(key)) state.FoldedGroups.Add(key);
                        Fill();
                    };
                    rows.Children.Add(fold);
                    if (!open) continue;
                    foreach (var p in g) Add(p, byCar ? p.Place : p.Car, $"{StatsTabs.SessionsText(p.Sessions)} · {StatsTabs.Laps((int)p.Laps)}", true);
                }
            }
            if (list.FirstOrDefault(p => p.Key == state.Pair) is { } picked) { if (picked.Key != shown) ShowPair(picked, false); }
            else { shown = null; detailHost.Content = null; }
        }

        carsButton.Click += (_, _) =>
        {
            var menu = StatsTabs.Menu(carsButton, 260);
            var all = new MenuItem { Header = "Tick all", StaysOpenOnClick = false };
            all.Click += (_, _) => { state.HiddenCars.Clear(); Fill(); };
            var none = new MenuItem { Header = "Clear" };
            none.Click += (_, _) => { foreach (var c in pairs.Select(p => p.Car)) state.HiddenCars.Add(c); Fill(); };
            menu.Items.Add(all);
            menu.Items.Add(none);
            menu.Items.Add(new Separator());
            foreach (var car in pairs.GroupBy(p => p.Car).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                string c = car.Key;
                StatsTabs.Choice(menu, $"{c} ({car.Count()})", !state.HiddenCars.Contains(c), () =>
                {
                    if (!state.HiddenCars.Remove(c)) state.HiddenCars.Add(c);
                    Fill();
                }, staysOpen: true);
            }
            menu.IsOpen = true;
        };
        viewButton.Click += (_, _) =>
        {
            var menu = StatsTabs.Menu(viewButton);
            menu.Items.Add(new MenuItem { Header = "Group by", IsEnabled = false });
            foreach (var (id, name) in Groups) StatsTabs.Choice(menu, name, state.PairGroup == id, () => { state.PairGroup = id; Fill(); });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Sort by", IsEnabled = false });
            foreach (var (id, name) in Sorts)
                StatsTabs.Choice(menu, name, state.PairSort == id, () => { state.PairSort = id; Fill(); },
                    id == "gain" ? "Most time taken off since your first session there" : id == "driven" ? "Most driving time in the period" : null);
            menu.IsOpen = true;
        };

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Text("Car and track", "H2"));
        title.Children.Add(count);
        var search = StatsTabs.SearchBox(state.PairSearch, "Search tracks and cars", text => { state.PairSearch = text; Fill(); });
        search.Margin = new Thickness(0, 12, 0, 0);
        var menus = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        menus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        menus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        menus.ColumnDefinitions.Add(new ColumnDefinition());
        carsButton.MinWidth = 0;
        viewButton.MinWidth = 0;
        menus.Children.Add(carsButton);
        Grid.SetColumn(viewButton, 2);
        menus.Children.Add(viewButton);
        var head = new StackPanel { Children = { title, search, menus } };
        var listCard = StatsTabs.ListCard(head, rows);
        rows.Children.Add(StatsSections.Empty("Reading your cars and tracks…").Also(e => e.Margin = new Thickness(16, 12, 16, 12)));
        _ = LoadPairs();
        async Task LoadPairs()
        {
            var store = data.Store;
            var q = data.Query;
            var (read, first) = await Task.Run(() =>
            {
                var list = ReadPairs(store, q);
                var f = new Dictionary<string, double>();
                if (list.Count > 0)
                    foreach (var r in store.Db.Query("""
                        WITH b AS (SELECT s.car, s.track, s.track_config, s.start_utc, MIN(l.time) t FROM laps l JOIN sessions s ON s.id = l.session_id WHERE l.clean = 1 GROUP BY s.id)
                        SELECT car, track, track_config, t FROM b x WHERE start_utc = (SELECT MIN(start_utc) FROM b y WHERE y.car = x.car AND y.track = x.track AND y.track_config = x.track_config)
                        """))
                        f[$"{r[0]}|{r[1]}|{r[2]}"] = Convert.ToDouble(r[3]);
                return (list, f);
            });
            pairs = read;
            firsts = first;
            Fill();
        }
        var grid = StatsTabs.ListAndDetail(listCard, detailHost);
        Ui.Anchor(grid, "stats.pairs");
        return grid;
    }

    private static T Also<T>(this T item, Action<T> act)
    {
        act(item);
        return item;
    }

    private static FrameworkElement Detail(PairDetail d)
    {
        var p = d.Pair;
        var headStack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var best = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
        best.Children.Add(new TextBlock { Text = "Personal best", Style = Ui.Res<Style>("Eyebrow") });
        best.Children.Add(RaceParts.BigFigure(p.Best is { } b ? StatFormat.LapTime(b) : RaceFormat.Missing, 52, "Place4Text"));
        string set = d.BestWhen is { } when ? "Set " + Dates.Day(when.ToLocalTime()) : "No clean lap yet";
        if (d.First is { } first && p.Best is { } pb && first.Lap - pb > 0.0005)
            set += $" · {StatFormat.Value(StatKind.LapDelta, pb - first.Lap, false)} since your first session ({Dates.Day(first.When.ToLocalTime())})";
        best.Children.Add(new TextBlock { Text = set, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        top.Children.Add(best);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 20, 0) };
        names.Children.Add(new TextBlock { Text = p.Place, Style = Ui.Res<Style>("H2"), TextWrapping = TextWrapping.Wrap });
        names.Children.Add(new TextBlock { Text = p.Car, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(names, 1);
        top.Children.Add(names);
        var tallies = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 0, 0) };
        int sessions = d.Combo.Report?.Sessions ?? p.Sessions;
        long laps = d.Combo.Report?.Laps ?? p.Laps;
        double? avg = d.Clean.Count > 0 ? d.Clean.Average() : null;
        foreach (var (value, label) in new[]
        {
            (sessions.ToString(C), "Sessions"), (laps.ToString("N0", C), "Laps"),
            (avg is { } a && p.Best is { } pb2 ? StatFormat.Value(StatKind.LapDelta, a - pb2, false) : RaceFormat.Missing, "Average clean lap vs best"),
        })
        {
            var t = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
            t.Children.Add(RaceParts.Big(value, 22, "Heading"));
            t.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
            tallies.Children.Add(t);
        }
        Grid.SetColumn(tallies, 2);
        top.Children.Add(tallies);
        headStack.Children.Add(top);
        var timeline = d.Combo.Timeline;
        FrameworkElement progression = timeline.Count switch
        {
            0 => StatsSections.Empty("No clean laps for this pair yet."),
            1 => StatsSections.Empty($"One best so far: {StatFormat.LapTime(timeline[0].Lap)} on {Dates.Day(timeline[0].When.ToLocalTime())}. The line starts once you beat it."),
            _ => StatsCharts.Progression(timeline),
        };
        headStack.Children.Add(new TextBlock { Text = "Each dot is a new personal best", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 14, 0, 0) });
        progression.Margin = new Thickness(0, 6, 0, 0);
        headStack.Children.Add(progression);
        var headCard = StatsTabs.Card(headStack, new Thickness(22, 18, 22, 16));

        var sectorStack = new StackPanel();
        sectorStack.Children.Add(StatsTabs.ListTitle("Sectors", null));
        sectorStack.Children.Add(new TextBlock { Text = "Your average clean sector against your best one · longer bar = more time to find there", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        if (d.Sectors.Count < 2) sectorStack.Children.Add(StatsSections.Empty("No sector times recorded for this pair."));
        else
        {
            double most = Math.Max(0.001, d.Sectors.Max(s => s.Lost));
            int? worst = SectorReport.MostToFind(d.Sectors);
            foreach (var s in d.Sectors)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12), ToolTip = $"Best {StatFormat.LapTime(s.Best)} (lap {s.BestLap}) · average {StatFormat.LapTime(s.Average)} over {StatsTabs.Laps(s.Laps)}" };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.Children.Add(new TextBlock { Text = $"Sector {s.Index + 1}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                var back = new Border { CornerRadius = ThemeManager.Round(4) };
                back.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
                bar.Children.Add(back);
                double share = Math.Max(0.03, s.Lost / most);
                var fill = new Border { CornerRadius = ThemeManager.Round(4), HorizontalAlignment = HorizontalAlignment.Left };
                fill.SetResourceReference(Border.BackgroundProperty, s.Index == worst ? "AccentText" : "Knob");
                bar.Children.Add(fill);
                bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                var lost = RaceParts.Big(StatFormat.Value(StatKind.LapDelta, s.Lost, false), 15, "Heading");
                lost.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(lost, 2);
                row.Children.Add(lost);
                sectorStack.Children.Add(row);
            }
            if (worst is { } w) sectorStack.Children.Add(new TextBlock { Text = $"Most to find in sector {w + 1}.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0) });
        }
        var sectorCard = StatsTabs.Card(sectorStack);

        var spreadStack = new StackPanel();
        spreadStack.Children.Add(StatsTabs.ListTitle("Clean laps by time", null));
        if (d.Clean.Count < 3 || p.Best is not { } fastest) spreadStack.Children.Add(StatsSections.Empty("Needs 3 or more clean laps."));
        else
        {
            int near = d.Clean.Count(t => t - fastest <= 0.6);
            spreadStack.Children.Add(new TextBlock { Text = $"{Racecraft.Share(near, d.Clean.Count)}% of {RaceFormat.Count(d.Clean.Count, "clean lap")} within 0.6 s of your best", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 10) });
            spreadStack.Children.Add(new LapSpread(d.Clean, fastest) { Height = 170 });
        }
        var spreadCard = StatsTabs.Card(spreadStack);
        return StatsTabs.Stacked(headCard, StatsLayout.Pair(sectorCard, spreadCard, 820));
    }

    private sealed class LapSpread : StatsCharts.HoverChart
    {
        private const int Bins = 14;
        private readonly int[] _counts = new int[Bins];
        private readonly double _best, _width;

        public LapSpread(List<double> clean, double best)
        {
            _best = best;
            var sorted = clean.OrderBy(t => t).ToList();
            double p90 = StatsCharts.Quantile(sorted, 0.9);
            _width = Math.Max(0.05, Math.Round((p90 - best) / (Bins - 2), 2));
            foreach (double t in clean) _counts[Math.Clamp((int)((t - best) / _width), 0, Bins - 1)]++;
        }

        private new const double Left = 4;

        private Rect Bar(int i)
        {
            double w = Math.Max(1, ActualWidth - Left - Right), h = Math.Max(1, ActualHeight - Top - Bottom - 14);
            double slot = w / Bins, most = Math.Max(1, _counts.Max());
            double bh = _counts[i] == 0 ? 1 : Math.Max(3, h * _counts[i] / most);
            return new Rect(Left + slot * i + 2, ActualHeight - Bottom - bh, Math.Max(1, slot - 4), bh);
        }

        protected override int HitAt(Point p)
        {
            for (int i = 0; i < Bins; i++)
            {
                var r = Bar(i);
                if (p.X >= r.Left && p.X <= r.Right) return i;
            }
            return -1;
        }

        protected override string TipOf(int i) => i == Bins - 1
            ? $"{StatFormat.LapTime(_best + i * _width)} or slower · {StatsTabs.Laps(_counts[i])}"
            : $"{StatFormat.LapTime(_best + i * _width)} to {StatFormat.LapTime(_best + (i + 1) * _width)} · {StatsTabs.Laps(_counts[i])}";

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (w < 60) return;
            var bar = Ui.Res<Brush>("Knob");
            var top = Ui.Res<Brush>("Heading");
            for (int i = 0; i < Bins; i++)
            {
                var r = Bar(i);
                dc.DrawRectangle(i == 0 ? top : bar, null, r);
                if (_counts[i] > 0)
                {
                    var n = Text(_counts[i].ToString(C));
                    dc.DrawText(n, new Point(r.Left + r.Width / 2 - n.Width / 2, r.Top - n.Height - 2));
                }
                if (i == Hover) dc.DrawRectangle(null, Hairline.Pen(Ui.Res<Brush>("LineStrong"), this), r);
            }
            double px = Hairline.ScaleOf(this);
            double baseY = Hairline.Snap(h - Bottom, px);
            dc.DrawLine(Hairline.Pen(Ui.Res<Brush>("LineStrong"), px), new Point(Left, baseY), new Point(w - Right, baseY));
            var lo = Text(StatFormat.LapTime(_best) + " faster");
            var hi = Text(StatFormat.LapTime(_best + (Bins - 1) * _width) + "+ slower");
            dc.DrawText(lo, new Point(Left, h - Bottom + 4));
            dc.DrawText(hi, new Point(w - Right - hi.Width, h - Bottom + 4));
        }
    }
}
