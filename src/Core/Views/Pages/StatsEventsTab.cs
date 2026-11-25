using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Controls;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Race;
using Core.SimRacing.TrackMaps;
using Core.Theme;

namespace Core.Views.Pages;

internal static class StatsEventsTab
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;

    internal sealed record Kind(string Id, string Name, string Noun, string Icon, string Brush, string Single)
    {
        public string Tally(int n) => Id == "lost" ? n.ToString(CultureInfo.CurrentCulture) + " lost" : RaceFormat.Count(n, Noun);
    }

    public static readonly Kind[] Kinds =
    {
        new("pass", "Passes", "pass", "I.ArrowUp", "Sage", "Pass"),
        new("lost", "Places lost", "place lost", "I.ArrowDown", "Coral", "Place lost"),
        new("off", "Off tracks", "off track", "I.OffTrack", "Warn", "Off track"),
        new("incident", "Contact", "incident", "I.Alert", "Danger", "Incident"),
        new("pit", "Pit stops", "pit stop", "I.Wrench", "Slate", "Pit stop"),
        new("fastest", "Fastest laps", "fastest lap", "I.Clock", "AccentText", "Fastest lap"),
    };

    private static Kind KindOf(string id) => Kinds.FirstOrDefault(k => k.Id == id) ?? Kinds[2];

    internal sealed record Shown(SavedRaceEvent Event, string Text);

    internal sealed record RaceDetail(SessionSummary Session, List<SavedRaceEvent> Events, bool Saved, List<LapRow> Laps, TurnNames Turns);

    public static List<SavedRaceEvent> FromFlags(IReadOnlyList<LapRow> laps)
    {
        var list = new List<SavedRaceEvent>();
        bool inPit = false;
        foreach (var lap in laps)
        {
            bool pit = lap.Flags.HasFlag(LapFlags.PitLane);
            if (pit && !inPit) list.Add(Flag(list.Count, lap, "pit"));
            inPit = pit;
            if (lap.Flags.HasFlag(LapFlags.OffTrack)) list.Add(Flag(list.Count, lap, "off"));
            if (lap.Flags.HasFlag(LapFlags.Incident) || lap.Incidents is > 0) list.Add(Flag(list.Count, lap, "incident") with { Points = lap.Incidents });
        }
        return list;
    }

    private static SavedRaceEvent Flag(int seq, LapRow lap, string kind) => new(seq, lap.Lap, kind, 0, null, lap.Position, null, "", "", null, null);

    public static FrameworkElement Build(StatsTabData data, IStatsHost host)
    {
        var state = host.State;
        var races = data.Sessions.Where(s => s.Type == "Race").ToList();
        var rows = new StackPanel();
        var detailHost = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Dictionary<long, Dictionary<string, int>> counts = new();
        int version = 0;

        void ShowRace(SessionSummary s, bool animate)
        {
            int mine = ++version;
            var store = data.Store;
            _ = Load();
            async Task Load()
            {
                var detail = await Task.Run(() => LoadDetail(s, store));
                if (mine != version) return;
                var view = Detail(detail, host, () => ShowRace(s, false), inline: false);
                detailHost.Content = view;
                if (animate) Motion.FadeIn(view);
            }
        }

        void Fill()
        {
            rows.Children.Clear();
            if (races.Count == 0)
            {
                rows.Children.Add(Ui.EmptyState("I.Flag", "No races", $"No races {data.RangeName}. Pick a longer period at the top."));
                detailHost.Content = Ui.EmptyState("I.Flag", "Nothing to show", "Race events show here once you've raced in the period.");
                return;
            }
            if (state.Race is null || races.All(r => r.Id != state.Race)) state.Race = races[0].Id;
            foreach (var s in races)
            {
                var race = s;
                rows.Children.Add(StatsTabs.Row(RaceRow(s, counts.GetValueOrDefault(s.Id)), s.Id == state.Race, "statsRaces", () =>
                {
                    state.Race = race.Id;
                    state.Event = null;
                    ShowRace(race, true);
                }));
            }
        }

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Text("Races", "H2"));
        title.Children.Add(new TextBlock { Text = $"{races.Count} {data.RangeName} · newest first", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) });
        var live = host.LiveEvents();
        live.Margin = new Thickness(0, 12, 0, 0);
        var head = new StackPanel { Children = { title, live } };
        var list = StatsTabs.ListCard(head, rows);
        Fill();
        if (state.Race is { } first && races.FirstOrDefault(r => r.Id == first) is { } picked) ShowRace(picked, false);
        _ = LoadCounts();
        async Task LoadCounts()
        {
            var ids = races.Select(r => r.Id).ToList();
            var store = data.Store;
            counts = await Task.Run(() => store.RaceEventCounts(ids));
            if (counts.Count > 0) Fill();
        }
        var grid = StatsTabs.ListAndDetail(list, detailHost);
        Ui.Anchor(grid, "stats.events");
        return grid;
    }

    private static async Task<RaceDetail> LoadDetail(SessionSummary s, StatsStore store)
    {
        var saved = store.RaceEvents(s.Id);
        var laps = store.Laps(s.Id);
        TurnNames turns = TurnNames.None;
        try
        {
            if (await TrackMapLibrary.ForSession(s.TrackId, s.TrackName, s.Track, s.Config) is { } map) turns = TurnNames.From(map, s.TrackKm);
        }
        catch (Exception ex) { App.LogError(ex); }
        return new RaceDetail(s, saved.Count > 0 ? saved : FromFlags(laps), saved.Count > 0, laps, turns);
    }

    public static FrameworkElement Inline(SessionSummary s, StatsStore store, IStatsHost host)
    {
        var holder = new ContentControl { Focusable = false, Content = StatsSections.Empty("Reading the race events…") };
        void Show()
        {
            _ = Load();
            async Task Load()
            {
                var detail = await Task.Run(() => LoadDetail(s, store));
                holder.Content = Detail(detail, host, Show, inline: true);
            }
        }
        Show();
        return holder;
    }

    private static FrameworkElement RaceRow(SessionSummary s, Dictionary<string, int>? counts)
    {
        var local = s.StartUtc.ToLocalTime();
        FrameworkElement figure = RaceParts.Raced(s)
            ? StatsTabs.Figure("P" + s.ShownPos!.Value.ToString(C), s.Gained is { } g ? StatsTabs.Places(g) : "Finished", s.Gained is { } g2 ? RaceParts.GainBrush(g2) : "TextFaint", size: 19)
            : StatsTabs.Figure(s.Finished ? RaceFormat.Missing : "DNF", s.StartPos is > 0 and var sp ? $"Started P{sp}" : "", size: 19);
        var lines = StatsTabs.TwoLines(s.Track, $"{Dates.Short(local)} · {s.Car}", figure);
        if (counts is null || counts.Count == 0) return lines;
        var tally = new WrapPanel { Margin = new Thickness(6, 4, 12, 0) };
        foreach (var k in Kinds.Where(k => k.Id is "pass" or "lost" or "off" or "incident"))
        {
            int n = counts.GetValueOrDefault(k.Id);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            item.Children.Add(StatsTabs.Dot(BrushOf(k), 6));
            var text = new TextBlock { Text = k.Tally(n), Style = Ui.Res<Style>("Caption"), Margin = new Thickness(4, 0, 0, 0) };
            item.Children.Add(text);
            tally.Children.Add(item);
        }
        return new StackPanel { Children = { lines, tally } };
    }

    private static string BrushOf(Kind k) => k.Brush is "Sage" or "Coral" ? StatsUi.Hue(k.Brush).Deep : k.Brush;

    private static string Describe(SavedRaceEvent e, TurnNames turns, bool saved)
    {
        string where = e.Pct is { } pct && pct >= 0 ? (turns.HasTurns ? " at " : " · ") + turns.Where(pct) : "";
        string other = e.OtherNumber.Length > 0 ? "#" + e.OtherNumber + (e.OtherName.Length > 0 ? " " + e.OtherName : "") : e.OtherName;
        string place = e.Position is > 0 and var p ? $" for P{p}" : "";
        return e.Kind switch
        {
            "pass" => (other.Length > 0 ? $"Passed {other}" : "Gained a place") + where + place,
            "lost" => (other.Length > 0 ? $"{other} got past" : "Lost a place") + where + (e.Position is > 0 and var lp ? $", down to P{lp}" : ""),
            "off" => "All four wheels off" + where + (e.Points is > 0 and var op ? $" · {op}x" : ""),
            "incident" => (other.Length > 0 ? $"Contact with {other}" : saved ? "Incident" : "Incident points on this lap") + where + (e.Points is > 0 and var ip ? $" · {ip}x" : ""),
            "pit" => e.Seconds is > 0 and var secs ? $"In the pit lane {secs.ToString("0.0", C)} {Units.S}" : "Pit stop",
            "fastest" => e.Seconds is > 0 and var lap ? $"Fastest lap of the race so far · {StatFormat.LapTime(lap)}" : "Fastest lap of the race so far",
            _ => KindOf(e.Kind).Single,
        };
    }

    private static FrameworkElement Detail(RaceDetail d, IStatsHost host, Action redraw, bool inline)
    {
        var s = d.Session;
        var state = host.State;
        var stack = new StackPanel();
        var head = new Grid();
        if (!inline) stack.Children.Add(head);

        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var big = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Top };
        big.Children.Add(RaceParts.BigFigure(RaceParts.Raced(s) ? "P" + s.ShownPos!.Value.ToString(C) : s.Finished ? RaceFormat.Missing : "DNF", 58, "Heading"));
        if (s.Gained is { } gained)
        {
            var gain = RaceParts.Big(gained == 0 ? "±0" : RaceParts.Signed(gained), 19, RaceParts.GainBrush(gained));
            gain.Margin = new Thickness(6, 2, 0, 0);
            gain.VerticalAlignment = VerticalAlignment.Top;
            big.Children.Add(gain);
        }
        head.Children.Add(big);
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = "Race" + (s.ShownStart is > 0 and var st ? $" · started P{st}" : "") + " · " + StatsTabs.Laps(s.Laps), Style = Ui.Res<Style>("Eyebrow") });
        info.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = s.Track, Style = Ui.Res<Style>("H1"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }));
        string config = StatFormat.Config(s.Config);
        info.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { config, s.Car, Dates.DayTime(s.StartUtc.ToLocalTime()) }.Where(x => x.Length > 0)), Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        var open = Ui.LabeledButton("I.ArrowRight", "Open session", () => { state.Session = s.Id; state.SessionTab = "Overview"; host.ShowTab("sessions"); });
        open.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(open, 2);
        head.Children.Add(open);

        var chips = new WrapPanel { Margin = new Thickness(0, inline ? 0 : 14, 0, 0) };
        var present = Kinds.Where(k => d.Saved ? k.Id != "fastest" || d.Events.Any(e => e.Kind == "fastest") : k.Id is "off" or "incident" or "pit").ToList();
        foreach (var k in present)
        {
            int n = d.Events.Count(e => e.Kind == k.Id);
            var chip = new ToggleChip($"{k.Name} {n}", k.Icon, BrushOf(k), state.EventKinds.Contains(k.Id));
            string id = k.Id;
            chip.Changed += on =>
            {
                if (on) state.EventKinds.Add(id); else state.EventKinds.Remove(id);
                redraw();
            };
            chips.Children.Add(chip);
        }
        stack.Children.Add(chips);
        if (!d.Saved)
            stack.Children.Add(new TextBlock
            {
                Text = "Passes and places lost are saved for races you drive from now on. This race only has the off tracks, incidents and pit stops kept per lap.",
                Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            });
        FrameworkElement top = inline ? stack : StatsTabs.Card(stack, new Thickness(22, 20, 22, 18));

        var shown = d.Events.Select((e, i) => (e, i)).Where(x => state.EventKinds.Contains(x.e.Kind) && present.Any(k => k.Id == x.e.Kind))
            .Select(x => (x.i, new Shown(x.e, Describe(x.e, d.Turns, d.Saved)))).ToList();
        var places = d.Laps.Where(l => l.Position is > 0).Select(l => (l.Lap, l.Position!.Value)).ToList();
        var lanes = new EventLanes(present, d.Events, shown.Select(x => x.i).ToHashSet(), places, Math.Max(1, Math.Max(s.Laps, d.Laps.Count)), state.Event,
            i => Describe(d.Events[i], d.Turns, d.Saved)) { Height = EventLanes.HeightFor(present.Count, places.Count >= 2) };
        var chartStack = new StackPanel
        {
            Children =
            {
                StatsTabs.ListTitle("Lap by lap", "Each dot is one event" + (places.Count >= 2 ? " · the line at the bottom is your place" : "") + " · click a row below to ring its dot"),
                lanes.Also(l => l.Margin = new Thickness(0, 12, 0, 0)),
            },
        };
        FrameworkElement chartCard = inline ? chartStack : StatsTabs.Card(chartStack);

        var table = new StackPanel();
        var headRow = EventGrid();
        string[] heads = { "Lap", "Event", "What happened", "Place" };
        for (int c = 0; c < heads.Length; c++)
        {
            var h = Ui.TableHead(heads[c]);
            if (c == 3) h.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(h, c);
            headRow.Children.Add(h);
        }
        headRow.Margin = new Thickness(9, 0, 12, 6);
        table.Children.Add(headRow);
        table.Children.Add(StatsTabs.Rule(0, 2));
        if (shown.Count == 0) table.Children.Add(StatsSections.Empty(d.Events.Count == 0 ? "Nothing happened worth noting: no events in this race." : "No events of the picked kinds."));
        foreach (var (index, item) in shown)
        {
            var k = KindOf(item.Event.Kind);
            var grid = EventGrid();
            grid.Children.Add(Ui.Text(item.Event.Lap > 0 ? "Lap " + item.Event.Lap.ToString(C) : "Start", "Muted"));
            var kind = new StackPanel { Orientation = Orientation.Horizontal };
            kind.Children.Add(StatsTabs.Dot(BrushOf(k), 8));
            kind.Children.Add(new TextBlock { Text = k.Single, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(kind, 1);
            grid.Children.Add(kind);
            var what = TextFit.TipWhenTrimmed(new TextBlock { Text = item.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            what.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Grid.SetColumn(what, 2);
            grid.Children.Add(what);
            if (item.Event.Position is > 0 and var p)
            {
                var place = RaceParts.Big("P" + p.ToString(C), 15, "Heading");
                place.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(place, 3);
                grid.Children.Add(place);
            }
            int i = index;
            table.Children.Add(StatsTabs.Row(grid, state.Event == i, "statsEvents", () => { state.Event = i; lanes.Ring(i); }));
        }
        FrameworkElement listCard = inline ? table : StatsTabs.Card(table, new Thickness(10, 14, 10, 12));
        if (inline) table.Margin = new Thickness(-6, 0, -6, 0);
        return StatsTabs.Stacked(top, chartCard, listCard);
    }

    private static Grid EventGrid()
    {
        var grid = new Grid { Margin = new Thickness(6, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        return grid;
    }

    private static T Also<T>(this T item, Action<T> act)
    {
        act(item);
        return item;
    }

    private sealed class EventLanes : StatsCharts.HoverChart
    {
        private const double LaneGap = 24, PlaceHeight = 64, PlaceGap = 22, LabelWidth = 96;
        private readonly List<Kind> _lanes;
        private readonly List<SavedRaceEvent> _events;
        private readonly HashSet<int> _shown;
        private readonly List<(int Lap, int Place)> _places;
        private readonly int _laps;
        private readonly Func<int, string> _describe;
        private int? _ring;

        public EventLanes(List<Kind> lanes, List<SavedRaceEvent> events, HashSet<int> shown, List<(int Lap, int Place)> places, int laps, int? ring, Func<int, string> describe)
        {
            _lanes = lanes;
            _events = events;
            _shown = shown;
            _places = places;
            _laps = laps;
            _ring = ring;
            _describe = describe;
        }

        public static double HeightFor(int lanes, bool places) => 8 + lanes * LaneGap + 22 + (places ? PlaceGap + PlaceHeight : 0);

        public void Ring(int index)
        {
            _ring = index;
            InvalidateVisual();
        }

        private double X(double lap) => LabelWidth + (ActualWidth - LabelWidth - Right) * (lap - 0.5) / _laps;

        private double LaneY(int lane) => 12 + lane * LaneGap;

        private Point At(int i) => new(X(Math.Max(1, _events[i].Lap)), LaneY(_lanes.FindIndex(k => k.Id == _events[i].Kind)));

        protected override int HitAt(Point p)
        {
            int near = -1;
            double best = 10;
            foreach (int i in _shown)
                if ((At(i) - p).Length is var d && d < best) { best = d; near = i; }
            return near;
        }

        protected override string TipOf(int i) => $"Lap {_events[i].Lap} · {_describe(i)}";

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (w < LabelWidth + 40) return;
            double px = Hairline.ScaleOf(this);
            var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
            for (int lane = 0; lane < _lanes.Count; lane++)
            {
                double y = Hairline.Snap(LaneY(lane), px);
                dc.DrawLine(grid, new Point(LabelWidth, y), new Point(w - Right, y));
                var label = Text(_lanes[lane].Name);
                dc.DrawText(label, new Point(0, y - label.Height / 2));
            }
            double ticksY = LaneY(_lanes.Count - 1) + 14;
            int step = _laps <= 12 ? 1 : _laps <= 30 ? 5 : 10;
            for (int lap = 1; lap <= _laps; lap++)
            {
                if (lap != 1 && lap != _laps && lap % step != 0) continue;
                var label = Text(lap.ToString(C));
                dc.DrawText(label, new Point(X(lap) - label.Width / 2, ticksY));
            }
            foreach (int i in _shown)
            {
                if (_lanes.FindIndex(k => k.Id == _events[i].Kind) < 0) continue;
                var at = At(i);
                var fill = Ui.Res<Brush>(BrushOf(KindOf(_events[i].Kind)));
                bool on = i == _ring || i == Hover;
                dc.DrawEllipse(fill, on ? new Pen(Ui.Res<Brush>("Heading"), 2) : null, at, on ? 6.5 : 4.5, on ? 6.5 : 4.5);
            }
            if (_places.Count < 2) return;
            double top = ticksY + PlaceGap, bottom = top + PlaceHeight - 6;
            int worst = Math.Max(2, _places.Max(p => p.Place));
            var label1 = Text("Place");
            dc.DrawText(label1, new Point(0, top - label1.Height / 2));
            foreach (var (place, y) in new[] { (1, top), (worst, bottom) })
            {
                double sy = Hairline.Snap(y, px);
                dc.DrawLine(grid, new Point(LabelWidth, sy), new Point(w - Right, sy));
                var t = Text("P" + place.ToString(C));
                dc.DrawText(t, new Point(LabelWidth - 8 - t.Width, sy - t.Height / 2));
            }
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                Point P(int k) => new(X(_places[k].Lap), top + (bottom - top) * (_places[k].Place - 1) / (worst - 1));
                g.BeginFigure(P(0), false, false);
                for (int k = 1; k < _places.Count; k++) g.LineTo(P(k), true, true);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(Ui.Res<Brush>("Heading"), 2) { LineJoin = PenLineJoin.Round }, line);
        }
    }
}
