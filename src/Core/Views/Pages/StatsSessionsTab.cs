using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Controls;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal static class StatsSessionsTab
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;
    private static readonly string?[] Types = { null, "Practice", "Qualify", "Race" };
    private static readonly string[] TypeNames = { "All", "Practice", "Qualifying", "Race" };

    internal sealed record Detail(SessionSummary Session, List<LapRow> Laps, SessionConditions? Conditions, double? AllTimeBest, Dictionary<string, int> Events, bool Traced, List<int> TracedLaps);

    public static FrameworkElement Build(StatsTabData data, IStatsHost host)
    {
        var state = host.State;
        var rows = new StackPanel();
        var detailHost = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var count = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) };
        int version = 0;
        long? shown = null;

        List<SessionSummary> Filtered()
        {
            string q = state.SessionSearch.Trim();
            return data.Sessions.Where(s => (Types[state.SessionType] is not { } type || s.Type == type)
                && (q.Length == 0 || $"{s.Track} {s.Config} {s.Car}".Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
        }

        void ShowDetail(SessionSummary s, bool animate)
        {
            int mine = ++version;
            shown = s.Id;
            var store = data.Store;
            var traces = data.Stats.Traces;
            _ = Load();
            async Task Load()
            {
                var detail = await Task.Run(() =>
                {
                    var laps = store.Laps(s.Id);
                    var traced = traces?.LapsWithTrace(s.Id) ?? new HashSet<int>();
                    var events = store.RaceEventCounts(new[] { s.Id }).TryGetValue(s.Id, out var counts) ? counts : new Dictionary<string, int>();
                    return new Detail(s, laps, store.Conditions(s.Id), store.BestLap(s.Car, s.Track, s.Config), events, data.Traced.Contains(s.Id),
                        laps.Where(l => traced.Contains(l.Lap)).OrderBy(l => l.Clean ? 0 : 1).ThenBy(l => l.Time).Select(l => l.Lap).Take(1).ToList());
                });
                if (mine != version) return;
                var view = DetailView(detail, data, host);
                detailHost.Content = view;
                if (animate) Motion.FadeIn(view);
            }
        }

        void Fill()
        {
            rows.Children.Clear();
            var list = Filtered();
            count.Text = $"{list.Count} {data.RangeName}";
            if (list.Count == 0)
            {
                shown = null;
                rows.Children.Add(Ui.EmptyState("I.Stats", "No sessions", state.SessionSearch.Trim().Length > 0 ? $"Nothing matches “{state.SessionSearch.Trim()}”." : "None of this kind in the range."));
                detailHost.Content = null;
                return;
            }
            if (state.Session is null || list.All(s => s.Id != state.Session)) state.Session = list[0].Id;
            DateTime? day = null;
            foreach (var s in list.Take(state.SessionsShown))
            {
                var local = s.StartUtc.ToLocalTime();
                if (day != local.Date)
                {
                    rows.Children.Add(StatsTabs.DayHead(Dates.Weekday(local) + " " + Dates.Short(local), day is null));
                    day = local.Date;
                }
                var session = s;
                rows.Children.Add(StatsTabs.Row(SessionRow(s, local), s.Id == state.Session, "statsSessions", () =>
                {
                    state.Session = session.Id;
                    ShowDetail(session, true);
                }));
            }
            if (list.Count > state.SessionsShown)
            {
                var more = Ui.LabeledButton("I.ChevronDown", $"Show more ({list.Count - state.SessionsShown} left)", () => { state.SessionsShown += 60; Fill(); });
                more.Margin = new Thickness(12, 8, 0, 12);
                more.HorizontalAlignment = HorizontalAlignment.Left;
                rows.Children.Add(more);
            }
            if (list.FirstOrDefault(s => s.Id == state.Session) is { } picked && picked.Id != shown) ShowDetail(picked, false);
        }

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Text("Sessions", "H2"));
        title.Children.Add(count);
        var gear = host.TraceButton();
        gear.VerticalAlignment = VerticalAlignment.Center;
        var titleRow = Ui.SplitRow(title, gear);
        var totals = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        string?[] ids = { "pace.laps", "time.total", "race.wins", "race.irating" };
        string[] labels = { "Laps", "Driving", "Wins", "iRating" };
        for (int i = 0; i < ids.Length; i++)
        {
            totals.ColumnDefinitions.Add(new ColumnDefinition());
            var stat = data.Get(ids[i]!);
            string value = stat is { Result: { NoData: false } r } ? r.Value : RaceFormat.Missing;
            string brush = stat is { } st && StatsUi.ToneOf(st.Def, st.Result) is { } tone ? tone.Deep : "Heading";
            var figure = new StackPanel { ToolTip = stat is { } tipped ? StatsUi.TipOf(tipped.Def, tipped.Result, false) : null, Background = Brushes.Transparent };
            figure.Children.Add(RaceParts.Big(value, 17, brush));
            figure.Children.Add(new TextBlock { Text = labels[i], Style = Ui.Res<Style>("Caption") });
            Grid.SetColumn(figure, i);
            totals.Children.Add(figure);
        }
        var search = StatsTabs.SearchBox(state.SessionSearch, "Search tracks and cars", text => { state.SessionSearch = text; state.SessionsShown = 60; Fill(); });
        search.Margin = new Thickness(0, 12, 0, 0);
        var types = Ui.Segmented(TypeNames, state.SessionType, i => { state.SessionType = i; state.SessionsShown = 60; Fill(); }, "statsSessionKinds");
        types.Margin = new Thickness(0, 8, 0, 0);
        types.HorizontalAlignment = HorizontalAlignment.Left;
        var head = new StackPanel { Children = { titleRow, totals, search, types } };
        var list = StatsTabs.ListCard(head, rows);
        Ui.Anchor(list, "stats.sessions");
        Fill();
        return StatsTabs.ListAndDetail(list, detailHost);
    }

    private static FrameworkElement SessionRow(SessionSummary s, DateTime local)
    {
        var when = new StackPanel { Width = 66 };
        var time = new TextBlock { Text = Dates.Time(local), FontSize = 12.5, TextWrapping = TextWrapping.NoWrap };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        when.Children.Add(time);
        var kind = new TextBlock { Text = StatsSessions.TypeName(s.Type), Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 1, 0, 0) };
        kind.SetResourceReference(TextBlock.ForegroundProperty, StatsSessions.TypeBrush(s.Type) == "Accent" ? "Heading" : StatsSessions.TypeBrush(s.Type));
        when.Children.Add(kind);
        string config = StatFormat.Config(s.Config);
        FrameworkElement figure;
        if (RaceParts.Raced(s))
        {
            int gain = s.Gained ?? 0;
            figure = StatsTabs.Figure("P" + s.ShownPos!.Value.ToString(C), s.Gained is null ? "Finished" : StatsTabs.Places(gain), s.Gained is null ? "TextFaint" : RaceParts.GainBrush(gain));
        }
        else figure = StatsTabs.Figure(s.Best is { } best ? StatFormat.LapTime(best) : RaceFormat.Missing, StatsTabs.Laps(s.Laps));
        return StatsTabs.TwoLines(s.Track, config.Length > 0 ? $"{config} · {s.Car}" : s.Car, figure, when);
    }

    private static FrameworkElement DetailView(Detail d, StatsTabData data, IStatsHost host)
    {
        var s = d.Session;
        var state = host.State;
        bool raced = RaceParts.Raced(s);
        bool imperial = data.Stats.Settings.Imperial;
        var card = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var big = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 22, 0) };
        string place = RaceParts.Place(s);
        if (raced)
        {
            big.Children.Add(RaceParts.BigFigure("P" + s.ShownPos!.Value.ToString(C), 64, "Heading"));
            if (s.Gained is { } gained)
            {
                var gain = RaceParts.Big(RaceParts.Signed(gained), 20, RaceParts.GainBrush(gained));
                gain.Margin = new Thickness(6, 2, 0, 0);
                gain.VerticalAlignment = VerticalAlignment.Top;
                big.Children.Add(gain);
            }
        }
        else big.Children.Add(RaceParts.BigFigure(s.Best is { } b ? StatFormat.LapTime(b) : RaceFormat.Missing, 44, "Heading"));
        head.Children.Add(big);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        string eyebrow = StatsSessions.TypeName(s.Type) + (raced && s.ShownStart is > 0 and var start ? $" · started P{start}" : "") + $" · {StatsTabs.Laps(s.Laps)}";
        var kind = new TextBlock { Text = eyebrow, Style = Ui.Res<Style>("Eyebrow") };
        kind.SetResourceReference(TextBlock.ForegroundProperty, s.Type == "Race" ? "Heading" : StatsSessions.TypeBrush(s.Type));
        info.Children.Add(kind);
        var track = TextFit.TipWhenTrimmed(new TextBlock { Text = s.Track, Style = Ui.Res<Style>("H1"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        info.Children.Add(track);
        string config = StatFormat.Config(s.Config);
        info.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { config, s.Car, Dates.DayTime(s.StartUtc.ToLocalTime()) }.Where(x => x.Length > 0)), Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Chip(FrameworkElement chip) { chip.Margin = new Thickness(0, 0, 8, 4); chips.Children.Add(chip); }
        if (s.IrStart is > RaceResult.PlaceholderIRating and var ir)
            Chip(Ui.Chip($"iRating {(ir + (s.IrDelta ?? 0)).ToString("N0", C)}{(s.IrDelta is { } delta && delta != 0 ? " " + StatFormat.Value(StatKind.Signed, delta, false) : "")}"));
        if (s.SrStart is { } sr) Chip(Ui.Chip($"Safety {(sr + (s.SrDelta ?? 0)).ToString("0.00", C)}"));
        if (s.Incidents is { } inc) Chip(Ui.Chip($"{inc}x incidents"));
        if (s.Best is { } mine && d.AllTimeBest is { } pb && Math.Abs(mine - pb) < 0.0005) Chip(Ui.Chip("Personal best", "AccentSoft", "AccentText"));
        if (chips.Children.Count > 0) info.Children.Add(chips);
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        var sketch = new TrackSketch(s.Track, s.Config, "Accent", 3.5, lap: true) { PlayOnLoad = true, TrackId = s.TrackId, TrackName = s.TrackName, Width = 170, Height = 100, Margin = new Thickness(16, 0, 0, 0), IsHitTestVisible = false };
        Grid.SetColumn(sketch, 2);
        head.Children.Add(sketch);
        card.Children.Add(head);

        var body = new ContentControl { Focusable = false, Margin = new Thickness(0, 14, 0, 0) };
        var names = new List<string> { "Overview", "Lap by lap" };
        if (s.Type == "Race") names.Add("Race events");
        bool traced = d.TracedLaps.Count > 0 && data.Stats.Traces is not null;
        if (!names.Contains(state.SessionTab)) state.SessionTab = "Overview";
        var analysis = Ui.LabeledButton("I.Activity", "Lap analysis", () => host.OpenAnalysis(s.Id, d.TracedLaps));
        analysis.IsEnabled = traced;
        analysis.ToolTip = traced ? "Coach, corners, consistency, the whole lap and replay, against your best or any other lap"
            : StatsSessions.NoTraceReason(s, data.Stats.Settings, DateTime.UtcNow);
        ToolTipService.SetShowOnDisabled(analysis, true);
        FrameworkElement Content(string tab) => tab switch
        {
            "Lap by lap" => LapByLap(d),
            "Race events" => StatsEventsTab.Inline(s, data.Store, host),
            _ => Overview(d, data, host, imperial),
        };
        void Pick(string tab)
        {
            state.SessionTab = tab;
            body.Content = Content(tab);
            Motion.FadeIn(body);
        }
        var tabRow = StatsTabs.SubTabs(names, state.SessionTab, Pick, out var strip, analysis);
        var tabs = Ui.TabBar(strip, tabRow);
        tabs.Margin = new Thickness(0, 14, 0, 0);
        card.Children.Add(tabs);
        body.Content = Content(state.SessionTab);
        card.Children.Add(body);
        return StatsTabs.Card(card, new Thickness(22, 20, 22, 20));
    }

    private static FrameworkElement Overview(Detail d, StatsTabData data, IStatsHost host, bool imperial)
    {
        var s = d.Session;
        var laps = d.Laps;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        var main = new StackPanel();
        var clean = laps.Where(l => l.Clean).ToList();
        double? avg = clean.Count > 0 ? clean.Average(l => l.Time) : null;
        double? best = laps.Count > 0 ? (clean.Count > 0 ? clean : laps).Min(l => l.Time) : s.Best;
        bool pb = best is { } bl && d.AllTimeBest is { } atb && Math.Abs(bl - atb) < 0.0005;
        double? consistency = data.Races.ConsistencyOf(s.Id);
        int pits = laps.Count(l => l.Flags.HasFlag(LapFlags.PitLane));
        var tiles = new StatGrid { MinTileWidth = 150, Gap = 10, FillLastRow = true };
        (string Label, string Value, string Note, string NoteBrush, string? Tip)[] items =
        {
            ("Best lap", best is { } x ? StatFormat.LapTime(x) : RaceFormat.Missing, pb ? "Personal best" : d.AllTimeBest is { } all && best is { } y ? StatFormat.Value(StatKind.LapDelta, y - all, false) + " off your best" : "", pb ? "AccentText" : "TextFaint", "Fastest clean lap of the session"),
            ("Average clean lap", avg is { } a ? StatFormat.LapTime(a) : RaceFormat.Missing, avg is { } a2 && best is { } b2 ? StatFormat.Value(StatKind.LapDelta, a2 - b2, false) + " on best" : "", "TextFaint", "Mean of the clean laps"),
            ("Consistency", consistency is { } g ? g.ToString("0.0", C) + "%" : RaceFormat.Missing, RaceFormat.Count(clean.Count, "clean lap"), "TextFaint", RaceParts.ConsistencyTip),
            ("Incidents", s.Incidents is { } inc ? inc + "x" : RaceFormat.Missing, s.Incidents == 0 ? "Clean session" : laps.Count > 0 && s.Incidents is { } n ? (n * 10.0 / laps.Count).ToString("0.0", C) + " every 10 laps" : "", s.Incidents == 0 ? "SageDeep" : "TextFaint", null),
            ("Laps", laps.Count.ToString(C), pits == 0 ? "No pit stops" : RaceFormat.Count(pits, "pit stop"), "TextFaint", null),
        };
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var stack = new StackPanel();
            stack.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = it.Label, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }));
            var value = RaceParts.Big(it.Value, 21, "Heading");
            value.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(value);
            var note = TextFit.TipWhenTrimmed(new TextBlock { Text = it.Note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            note.SetResourceReference(TextBlock.ForegroundProperty, it.NoteBrush);
            stack.Children.Add(note);
            var tile = new Tile { CornerRadius = ThemeManager.Round(8), BorderThickness = Hairline.Edges(), Padding = new Thickness(12, 10, 12, 10), Child = stack, ToolTip = it.Tip };
            tile.SetResourceReference(Tile.BackgroundProperty, "SurfaceAlt");
            tile.SetResourceReference(Tile.BorderBrushProperty, "Line");
            tiles.Children.Add(tile);
        }
        main.Children.Add(tiles);

        var lapHead = new DockPanel { Margin = new Thickness(0, 20, 0, 8) };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        void Key(string brush, string text, double opacity = 1)
        {
            var swatch = new Border { Width = 8, Height = 8, Margin = new Thickness(legend.Children.Count > 0 ? 12 : 0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = opacity };
            swatch.SetResourceReference(Border.BackgroundProperty, brush);
            legend.Children.Add(swatch);
            legend.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        }
        string place = RaceParts.Place(s);
        Key(place, "Clean", LapStrip.CleanOpacity);
        Key("Coral", "Not clean", LapStrip.DirtyOpacity);
        Key("Heading", "Best");
        DockPanel.SetDock(legend, Dock.Right);
        lapHead.Children.Add(legend);
        int dirty = laps.Count(l => !l.Clean);
        var lapTitle = new TextBlock { Style = Ui.Res<Style>("Eyebrow") };
        lapTitle.Inlines.Add(new System.Windows.Documents.Run("Lap times"));
        var lapNote = new System.Windows.Documents.Run(laps.Count > 0 ? $"   {dirty} of {StatsTabs.Laps(laps.Count)} not clean · taller = faster" : "") { FontWeight = FontWeights.Normal };
        lapNote.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextMuted");
        lapTitle.Inlines.Add(lapNote);
        lapHead.Children.Add(lapTitle);
        main.Children.Add(lapHead);
        if (laps.Count > 1)
            main.Children.Add(new LapStrip(laps.Select(l => (l.Time, l.Clean)).ToList(), place, scale: true) { Height = 150, ToolTip = "Lap times in order: taller = faster" });
        else main.Children.Add(StatsSections.Empty("No complete laps in this session."));

        if (s.Type == "Race")
        {
            main.Children.Add(new TextBlock { Text = "Position each lap", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 22, 0, 8) });
            var places = laps.Where(l => l.Position is > 0).Select(l => (l.Lap, l.Position!.Value)).ToList();
            if (places.Count >= 2)
            {
                if (s.StartPos is > 0 and var startPos && s.ShownStart is not null && !s.HasTier) places.Insert(0, (0, startPos));
                main.Children.Add(new PlaceChart(places) { Height = 140 });
            }
            else main.Children.Add(StatsSections.Empty("Your place on each lap is saved for races from now on."));
        }
        grid.Children.Add(main);

        var side = new DockPanel { LastChildFill = false };
        var conditions = Conditions(s, d.Conditions, imperial);
        DockPanel.SetDock(conditions, Dock.Top);
        side.Children.Add(conditions);
        if (s.Type == "Race")
        {
            var events = EventsCard(d, host);
            DockPanel.SetDock(events, Dock.Bottom);
            side.Children.Add(events);
        }
        var divider = Hairline.Frame(new Border { Child = side, Padding = new Thickness(22, 0, 0, 0) }, Sides.Left);
        divider.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetColumn(divider, 1);
        Grid.SetColumnSpan(divider, 2);
        divider.Margin = new Thickness(13, 0, 0, 0);
        grid.Children.Add(divider);
        return grid;
    }

    private static FrameworkElement Conditions(SessionSummary s, SessionConditions? c, bool imperial)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Conditions", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 0, 0, 10) });
        string type = s.Weather.Split(" · ")[0];
        string sky = c?.Skies is { } k && k >= 0 && k < ConditionsText.SkyNames.Length ? ConditionsText.SkyNames[k] : type.Length > 0 ? type : "Weather unknown";
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var glyph = new Icon { Data = Ui.Res<Geometry>(c?.Skies == 0 ? "I.Sun" : "I.Cloud"), Width = 28, Height = 28, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(Control.ForegroundProperty, "TextMuted");
        top.Children.Add(glyph);
        var skyText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        skyText.Children.Add(new TextBlock { Text = sky, Style = Ui.Res<Style>("H3") });
        string? simTime = c is { SimStart.Length: > 0 } ? Dates.SimTime(c.SimStart) : null;
        string sub = string.Join(" · ", new[] { c?.Dynamic == true ? "Dynamic weather" : type.Length > 0 && type != sky ? type : null, simTime is null ? null : "Start " + simTime + " sim time" }.OfType<string>());
        if (sub.Length > 0) skyText.Children.Add(new TextBlock { Text = sub, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(skyText);
        stack.Children.Add(top);
        void Row(string label, string? value)
        {
            if (value is null) return;
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var v = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, MaxWidth = 150 };
            v.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            DockPanel.SetDock(v, Dock.Right);
            line.Children.Add(v);
            line.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Muted"), FontSize = 12.5 });
            stack.Children.Add(line);
            stack.Children.Add(StatsTabs.Rule(0, 7));
        }
        if (c is not null)
        {
            if (ConditionsText.Surface(c.WetnessMax, c.RainMax, 0, s.DriveS) is { } surface) Row("Surface", surface.Value);
            Row("Track temperature", ConditionsText.Temp(c.TrackTempMin, c.TrackTempMax, null, imperial));
            Row("Air temperature", ConditionsText.Temp(c.AirTempMin, c.AirTempMax, c.AirTemp, imperial));
            Row("Humidity", ConditionsText.Humidity(null, null, c.Humidity));
            string? wind = ConditionsText.Wind(c.WindMin, c.WindMax, c.WindKph, imperial);
            Row("Wind", wind is null ? null : c.WindDir is { } dir ? wind + " " + ConditionsText.Compass(dir) : wind);
            if (c.Rubber.Length > 0) Row("Rubber", ConditionsText.Rubber(c.Rubber));
        }
        else stack.Children.Add(StatsSections.Empty("Not recorded for this session."));
        return stack;
    }

    private static FrameworkElement EventsCard(Detail d, IStatsHost host)
    {
        var stack = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(StatsTabs.Dot("SageDeep"));
        title.Children.Add(new TextBlock { Text = "Race events", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var chevron = new Icon { Data = Ui.Res<Geometry>("I.ChevronRight"), Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(Control.ForegroundProperty, "TextMuted");
        var line = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Right);
        line.Children.Add(chevron);
        line.Children.Add(title);
        stack.Children.Add(line);
        string text = d.Events.Count > 0
            ? string.Join(" · ", StatsEventsTab.Kinds.Where(k => d.Events.GetValueOrDefault(k.Id) > 0).Select(k => k.Tally(d.Events[k.Id])))
            : StatsEventsTab.FromFlags(d.Laps).Count is var n and > 0 ? $"{RaceFormat.Count(n, "event")} from lap flags" : "Saved for races from now on";
        stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var button = new Button
        {
            Style = Ui.Res<Style>("GhostButton"), Content = stack, Height = double.NaN, Padding = new Thickness(13, 10, 13, 11), Margin = new Thickness(0, 16, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Every pass, place lost, off track, incident and pit stop of this race",
        };
        button.SetResourceReference(Control.BackgroundProperty, "TileRaised");
        long id = d.Session.Id;
        button.Click += (_, _) => { host.State.Race = id; host.State.Event = null; host.ShowTab("events"); };
        System.Windows.Automation.AutomationProperties.SetName(button, "Race events");
        return button;
    }

    private static FrameworkElement LapByLap(Detail d)
    {
        var laps = d.Laps;
        if (laps.Count == 0) return StatsSections.Empty("No complete laps in this session.");
        bool places = laps.Any(l => l.Position is > 0);
        var clean = laps.Where(l => l.Clean).ToList();
        double best = (clean.Count > 0 ? clean : laps).Min(l => l.Time);
        double worst = laps.Max(l => l.Time);
        double span = Math.Max(0.5, Math.Min(worst, best * 1.07) - best);
        var table = new Grid();
        double[] widths = { 54, -1, 92, 86, places ? 56 : 0, 170 };
        foreach (var w in widths) table.ColumnDefinitions.Add(w < 0 ? new ColumnDefinition() : new ColumnDefinition { Width = new GridLength(w) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Cell(int row, int col, FrameworkElement e, bool right = false)
        {
            e.Margin = new Thickness(col == 0 ? 2 : 10, 5, 0, 5);
            e.VerticalAlignment = VerticalAlignment.Center;
            if (right) e.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            table.Children.Add(e);
        }
        string[] heads = { "Lap", "Faster →", "Time", "To best", places ? "Place" : "", "Notes" };
        for (int c = 0; c < heads.Length; c++) if (heads[c].Length > 0) Cell(0, c, Ui.TableHead(heads[c]), c is 2 or 3 or 4);
        int row = 0;
        foreach (var lap in laps)
        {
            row++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rule = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top });
            rule.SetResourceReference(Border.BackgroundProperty, "Line");
            Grid.SetRow(rule, row);
            Grid.SetColumnSpan(rule, widths.Length);
            table.Children.Add(rule);
            bool isBest = Math.Abs(lap.Time - best) < 0.0005;
            Cell(row, 0, Ui.Text(lap.Lap.ToString(C), "Muted"));
            double share = Math.Clamp(1 - (lap.Time - best) / span * 0.85, 0.08, 1);
            var track = new Grid { Height = 6 };
            var back = new Border { CornerRadius = ThemeManager.Round(3) };
            back.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
            track.Children.Add(back);
            var fill = new Border { CornerRadius = ThemeManager.Round(3), HorizontalAlignment = HorizontalAlignment.Left, Opacity = lap.Clean ? 1 : LapStrip.DirtyOpacity };
            fill.SetResourceReference(Border.BackgroundProperty, isBest ? "Heading" : lap.Clean ? "Knob" : "Coral");
            track.Children.Add(fill);
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
            Cell(row, 1, track);
            Cell(row, 2, StatsSessions.Time(lap.Time, isBest ? "AccentText" : "Heading"), true);
            Cell(row, 3, Ui.Text(isBest ? "Best" : StatFormat.Value(StatKind.LapDelta, lap.Time - best, false), "Muted"), true);
            if (places && lap.Position is > 0 and var p) Cell(row, 4, RaceParts.Big("P" + p.ToString(C), 14, "Heading"), true);
            var notes = new List<string>();
            if (!lap.Clean) notes.Add("Not clean");
            if (lap.Flags.HasFlag(LapFlags.PitLane)) notes.Add("Pit lane");
            if (lap.Flags.HasFlag(LapFlags.OffTrack)) notes.Add("Off track");
            if (lap.Incidents is > 0 and var inc) notes.Add(inc + "x");
            else if (lap.Flags.HasFlag(LapFlags.Incident)) notes.Add("Incident");
            Cell(row, 5, TextFit.TipWhenTrimmed(new TextBlock { Text = notes.Count > 0 ? string.Join(" · ", notes.Distinct()) : "", Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis }));
        }
        return table;
    }

    private sealed class PlaceChart : StatsCharts.HoverChart
    {
        private readonly List<(int Lap, int Place)> _places;
        private readonly int _worst;

        public PlaceChart(List<(int Lap, int Place)> places)
        {
            _places = places;
            _worst = Math.Max(2, places.Max(p => p.Place));
        }

        private new const double Left = 40;

        private Point At(int i)
        {
            double w = Math.Max(1, ActualWidth - Left - Right), h = Math.Max(1, ActualHeight - Top - Bottom);
            int first = _places[0].Lap, last = _places[^1].Lap;
            return new Point(Left + w * (_places[i].Lap - first) / Math.Max(1, last - first), Top + h * (_places[i].Place - 1) / (_worst - 1));
        }

        protected override int HitAt(Point p)
        {
            int near = -1;
            double best = double.MaxValue;
            for (int i = 0; i < _places.Count; i++)
                if (Math.Abs(At(i).X - p.X) is var d && d < best && d < 16) { best = d; near = i; }
            return near;
        }

        protected override string TipOf(int i) => _places[i].Lap == 0 ? $"Start · P{_places[i].Place}" : $"Lap {_places[i].Lap} · P{_places[i].Place}";

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_places.Count < 2 || w < Left + Right + 20) return;
            double px = Hairline.ScaleOf(this);
            var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
            foreach (int place in new[] { 1, (_worst + 1) / 2, _worst }.Distinct())
            {
                double y = Hairline.Snap(Top + (h - Top - Bottom) * (place - 1) / (_worst - 1), px);
                dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
                var label = Text("P" + place.ToString(C));
                dc.DrawText(label, new Point(Left - 8 - label.Width, y - label.Height / 2));
            }
            int first = _places[0].Lap, last = _places[^1].Lap;
            foreach (int lap in new[] { first, last }.Distinct())
            {
                var label = Text(lap == 0 ? "Start" : "Lap " + lap.ToString(C));
                double x = Left + (w - Left - Right) * (lap - first) / Math.Max(1, last - first);
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, Left, w - Right - label.Width), h - Bottom + 4));
            }
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(At(0), false, false);
                for (int i = 1; i < _places.Count; i++) g.LineTo(At(i), true, true);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(Ui.Res<Brush>("Heading"), 2) { LineJoin = PenLineJoin.Round }, line);
            var end = At(_places.Count - 1);
            dc.DrawEllipse(Ui.Res<Brush>("Accent"), new Pen(Ui.Res<Brush>("Surface"), 2), end, 4.5, 4.5);
            if (Hover >= 0) dc.DrawEllipse(Ui.Res<Brush>("Heading"), null, At(Hover), 4, 4);
        }
    }
}
