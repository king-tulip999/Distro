using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Core.Controls;
using Core.Services;
using Core.SessionStats;
using Core.Theme;

namespace Core.Views.Pages;

internal sealed record AnalysisData(SessionSummary Session, List<(int Lap, double Time, LapTrace Trace)> Laps, (int Lap, LapTrace Trace)? SessionBest,
    (string Label, LapTrace Trace)? TrackBest, LapTrace? Optimal, IReadOnlyList<double> SectorStarts, string Driver,
    IReadOnlyList<SharedLapInfo> Shared, (SharedLapInfo Info, LapTrace Trace)? SharedPicked, SimRacing.TrackMaps.TrackMap? Map,
    List<(int Lap, double Time, bool Clean, LapTrace Trace)> All);

internal sealed record AnalysisActions(Action<string> PickReference, Action<int> PickLap, Action<int> ToggleExtra, Action<int> ExportLap, Action ImportLap,
    Action<long> DeleteShared, Action Save, Action Back);

internal enum MapColors { Laps, Speed, Delta }

internal sealed class AnalysisLine
{
    public required string Label { get; init; }
    public required LapTrace Trace { get; init; }
    public required string BrushKey { get; init; }
    public string Driver { get; init; } = "";
    public string Short { get; init; } = "";
    public int Lap { get; init; }
    public bool Reference { get; set; }
    public double[]? Delta { get; set; }
    public (double[] X, double[] Y)? Line { get; set; }
    public Dictionary<string, (double[]? A, double[]? B)> Lanes { get; } = new();
    public List<(double From, double To)> PushToPass { get; } = new();
    public (double Kph, double From)? Wind { get; set; }

    public bool BoostAt(double d) => PushToPass.Any(s => d >= s.From && d <= s.To);
}

internal sealed class AnalysisState
{
    public const double MinSpan = 60;
    public List<AnalysisLine> Lines { get; } = new();
    public AnalysisLine Reference { get; set; } = null!;
    public double Length { get; set; }
    public IReadOnlyList<double> SectorDistances { get; set; } = Array.Empty<double>();
    public bool Imperial { get; init; }
    public MapColors MapColors { get; set; }
    public double D0 { get; private set; }
    public double D1 { get; private set; }
    public double? Cursor { get; private set; }
    public (double From, double To)? Selecting { get; set; }

    public bool Zoomed => D0 > 0.5 || D1 < Length - 0.5;

    public bool Single => Lines.Count == 1;

    public event Action? ZoomChanged, CursorChanged;

    public void Zoom(double a, double b)
    {
        if (a > b) (a, b) = (b, a);
        if (b - a < MinSpan) { double mid = (a + b) / 2; a = mid - MinSpan / 2; b = mid + MinSpan / 2; }
        if (a < 0) { b -= a; a = 0; }
        if (b > Length) { a -= b - Length; b = Length; }
        D0 = Math.Max(0, a);
        D1 = Math.Min(Length, b);
        ZoomChanged?.Invoke();
    }

    public void ResetZoom() => Zoom(0, Length);

    public void SetCursor(double? d)
    {
        if (Cursor == d) return;
        Cursor = d;
        CursorChanged?.Invoke();
    }

    public void RaiseCursor() => CursorChanged?.Invoke();
}

internal static class LapAnalysisView
{
    public static readonly string[] LapBrushes = { "AccentText", "Tool7Deep", "Tool1Deep", "Tool3Deep" };

    private static readonly Dictionary<string, string> LaneNames = new()
    {
        ["delta"] = "Time delta", ["speed"] = "Speed", ["pedals"] = "Throttle / brake", ["steering"] = "Steering angle", ["gear"] = "Gear", ["rpm"] = "RPM", ["latg"] = "Lateral g",
        ["bias"] = "Brake bias", ["abs"] = "ABS setting", ["tc"] = "Traction control setting",
    };

    public static readonly HashSet<string> SettingLanes = new() { "bias", "abs", "tc" };

    public static readonly string[] Tabs = { "Coach", "Corners", "Consistency", "Whole lap", "Replay" };
    public const int ReplayTab = 4, MaxExtras = 2;
    private const double LapListWidth = 250;

    public static FrameworkElement Build(AnalysisData data, Config.SessionStatsSettings settings, string reference, AnalysisActions actions, ReplayMemory memory,
        out AnalysisState state)
    {
        var mine = data.Laps[0];
        var s = state = Make(data with { Laps = new() { mine } }, settings.Imperial, reference);
        var replayState = data.Laps.Count > 1 ? Make(data, settings.Imperial, reference) : s;
        var refTrace = s.Reference.Trace;
        bool compared = !ReferenceEquals(refTrace, mine.Trace);
        var coach = compared ? LapCoach.Build(mine.Trace, refTrace) : null;
        var (phrase, column) = RefNames(reference, s);
        memory.Tab = Math.Clamp(memory.Tab, 0, Tabs.Length - 1);

        var host = new ContentControl();
        var built = new FrameworkElement?[Tabs.Length];
        var ticks = new List<FrameworkElement>();
        LapReplayView? replay = null;
        Border tabs = null!;
        void Show(int tab)
        {
            memory.Tab = tab;
            foreach (var t in ticks) t.Visibility = tab == ReplayTab ? Visibility.Visible : Visibility.Collapsed;
            built[tab] ??= BuildTab(tab);
            host.Content = built[tab];
            Motion.FadeIn(built[tab]!);
        }
        void Select(int tab)
        {
            if (memory.Tab == tab) Show(tab);
            else Ui.SetSegment(tabs, tab);
        }
        CoachSetup? setup = null;
        if (coach is not null)
            setup = new CoachSetup
            {
                Data = data, Mine = mine.Trace, Lap = mine.Lap, Reference = refTrace, Phrase = phrase, Column = column, Coach = coach, Imperial = settings.Imperial, Memory = memory,
                Names = CoachSetup.NamesOf(coach, refTrace, data),
                OpenTurn = k => { memory.Turn = k; built[0] = built[1] = null; Select(0); },
                ReplayTurn = k =>
                {
                    var (from, to) = setup!.Window(k);
                    memory.Distance = from;
                    Select(ReplayTab);
                    var view = replay;
                    view?.Dispatcher.InvokeAsync(() => view.PlayBetween(mine.Trace, from, to), System.Windows.Threading.DispatcherPriority.Loaded);
                },
            };
        FrameworkElement BuildTab(int tab)
        {
            switch (tab)
            {
                case 3: return WholeLap(data, s, settings, actions, memory);
                case ReplayTab: return replay = new LapReplayView(data, replayState, settings, memory, actions.Save);
            }
            if (setup is null)
                return new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.EmptyState("I.Activity", "Nothing to compare against",
                    "This lap is the only one with a trace. Pick another lap under Against, or import a lap someone exported.") };
            return tab switch { 0 => LapCoachView.Coach(setup), 1 => LapCoachView.Corners(setup), _ => LapCoachView.Consistency(setup) };
        }

        var refChoices = new List<Choice>();
        if (data.SessionBest is { } sb && !Same(sb.Trace, mine.Trace)) refChoices.Add(new Choice("session", $"Session best · {StatFormat.LapTime(sb.Trace.LapTime)}"));
        if (data.TrackBest is { } tb) refChoices.Add(new Choice("track", $"Your best here · {tb.Label}"));
        if (data.Optimal is { } opt) refChoices.Add(new Choice("optimal", $"Optimal · {StatFormat.LapTime(opt.LapTime)}"));
        foreach (var l in data.All.Where(l => l.Lap != mine.Lap).OrderBy(l => l.Time)) refChoices.Add(new Choice("lap:" + l.Lap, $"Lap {l.Lap} · {StatFormat.LapTime(l.Time)}"));
        foreach (var shared in data.Shared) refChoices.Add(new Choice("shared:" + shared.Id, SharedLabel(shared, data.Session.Car)));
        var refPick = Ui.Combo(refChoices, reference, c => { if (c.Id is { } id && id != reference) actions.PickReference(id); }, 210);
        refPick.ToolTip = "What this lap is compared with";
        var against = new TextBlock { Text = "Against", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        against.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { against, refPick } };
        void Tool(Button b) { b.Margin = new Thickness(8, 0, 0, 0); b.VerticalAlignment = VerticalAlignment.Center; tools.Children.Add(b); }
        if (data.SharedPicked is { } picked && reference == "shared:" + picked.Info.Id)
            Tool(Ui.IconButton("I.Trash", "Remove this imported lap", () => actions.DeleteShared(picked.Info.Id)));
        Tool(Ui.IconButton("I.Folder", "Import a lap someone exported (same track) to compare against", actions.ImportLap));
        Tool(Ui.IconButton("I.Save", $"Export lap {mine.Lap} (to share or compare)", () => actions.ExportLap(mine.Lap)));

        tabs = Ui.PageTabs(Tabs, memory.Tab, Show, "lapTabs");
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        var bar = new TabBarRow();
        bar.Children.Add(tabs);
        tools.Margin = new Thickness(19, 0, 0, 0);
        bar.Children.Add(tools);
        var tabRow = Ui.TabBar(tabs, bar);

        var right = new StackPanel();
        var summary = Summary(data, mine, s, compared, phrase, settings.Imperial);
        summary.Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);
        right.Children.Add(summary);
        right.Children.Add(tabRow);
        right.Children.Add(host);

        var list = LapList(data, mine, refTrace, coach, actions, ticks, settings.Imperial);
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LapListWidth) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 2);
        root.Children.Add(list);
        root.Children.Add(right);
        Show(memory.Tab);
        return root;
    }

    internal static bool Same(LapTrace a, LapTrace b) => ReferenceEquals(a, b) || (a.LapTime == b.LapTime && a.Points == b.Points && a.LengthM == b.LengthM);

    internal static string Usable(AnalysisData data, string reference)
    {
        var mine = data.Laps[0].Trace;
        LapTrace? Resolve(string r) =>
            r == "track" ? data.TrackBest?.Trace
            : r == "optimal" ? data.Optimal
            : r.StartsWith("lap:") && int.TryParse(r[4..], out int n) ? data.All.FirstOrDefault(l => l.Lap == n).Trace
            : r.StartsWith("shared:") ? data.SharedPicked?.Trace
            : data.SessionBest?.Trace;
        bool Fine(string r) => Resolve(r) is { } t && !Same(t, mine);
        if (Fine(reference)) return reference;
        foreach (var r in new[] { "session", "track" }) if (Fine(r)) return r;
        var other = data.All.Where(l => !Same(l.Trace, mine)).OrderByDescending(l => l.Clean).ThenBy(l => l.Time).FirstOrDefault();
        return other.Trace is null ? reference : "lap:" + other.Lap;
    }

    private static (string Phrase, string Column) RefNames(string reference, AnalysisState s) =>
        reference == "track" ? ("your best here", "Best here")
        : reference == "optimal" ? ("your optimal lap", "Optimal")
        : reference.StartsWith("shared:") ? ("the imported lap", "Imported")
        : reference.StartsWith("lap:") && s.Reference.Lap > 0 ? ($"lap {s.Reference.Lap}", $"Lap {s.Reference.Lap}")
        : ("your session best", "Best");

    internal static string SharedLabel(SharedLapInfo lap, string car) =>
        $"{(lap.Driver.Length > 0 ? lap.Driver : "Imported")} · {StatFormat.LapTime(lap.LapTime)}{(string.Equals(lap.Car, car, StringComparison.OrdinalIgnoreCase) ? "" : " · " + lap.Car)}";

    private static Tile Card(FrameworkElement content) => new()
    {
        Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 13, 16, 16), Child = content, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing),
    };

    private static FrameworkElement LapList(AnalysisData data, (int Lap, double Time, LapTrace Trace) mine, LapTrace refTrace, LapCoach? coach, AnalysisActions actions,
        List<FrameworkElement> ticks, bool imperial)
    {
        var stack = new StackPanel();
        var back = Ui.LabeledButton("I.ChevronLeft", "Back", actions.Back);
        back.ToolTip = "Back to the session (Esc)";
        var title = new TextBlock { Text = "Laps", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center };
        var heading = Ui.SplitRow(title, back, 8);
        heading.Margin = new Thickness(16, 0, 12, 0);
        stack.Children.Add(heading);
        var cols = new[] { GridLength.Auto, new GridLength(30), new GridLength(1, GridUnitType.Star), new GridLength(38), new GridLength(54) };
        Grid Row()
        {
            var g = new Grid();
            foreach (var w in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            return g;
        }
        var head = Row();
        head.Margin = new Thickness(16, 10, 16, 4);
        string[] names = { "", "Lap", "Time", "Top", "To best" };
        for (int i = 1; i < names.Length; i++)
        {
            var h = new TextBlock { Text = names[i], Style = Ui.Res<Style>("Eyebrow"), HorizontalAlignment = i >= 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            Grid.SetColumn(h, i);
            head.Children.Add(h);
        }
        var headTick = new Border { Width = 22 };
        ticks.Add(headTick);
        head.Children.Add(headTick);
        stack.Children.Add(head);

        double best = data.SessionBest?.Trace.LapTime ?? data.All.Min(l => l.Time);
        var extras = data.Laps.Skip(1).Select(l => l.Lap).ToHashSet();
        var rows = new StackPanel();
        foreach (var lap in data.All.OrderBy(l => l.Lap))
        {
            var number = lap.Lap;
            bool me = lap.Lap == mine.Lap, isRef = Same(lap.Trace, refTrace);
            var g = Row();
            g.Height = 28;
            var tick = new CheckBox { IsChecked = me || isRef || extras.Contains(lap.Lap), IsEnabled = !me && !isRef, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            tick.ToolTip = me ? "The lap being analyzed" : isRef ? "The lap it's compared with" : "Add this lap to the replay";
            tick.Click += (_, e) => { e.Handled = true; actions.ToggleExtra(number); };
            ticks.Add(tick);
            g.Children.Add(tick);
            var no = new TextBlock { Text = lap.Lap.ToString(CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Body"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            no.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            Grid.SetColumn(no, 1);
            var time = new TextBlock { Text = StatFormat.LapTime(lap.Time), Style = Ui.Res<Style>("Body"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            bool fastest = data.SessionBest is { } sb && sb.Lap == lap.Lap;
            time.SetResourceReference(TextBlock.ForegroundProperty, fastest ? "TimingBest" : lap.Clean ? "Heading" : "TextMuted");
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            var timeCell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { time } };
            timeCell.ToolTip = fastest ? "The session's fastest clean lap" : lap.Clean ? null : "Not a clean lap";
            Grid.SetColumn(timeCell, 2);
            var top = new TextBlock { Text = CoachText.SpeedValue(LapCoach.Top(lap.Trace), imperial).ToString("0", CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Body"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            top.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            Grid.SetColumn(top, 3);
            double gap = lap.Time - best;
            var toBest = new TextBlock { Text = Math.Abs(gap) < 0.0005 ? SimRacing.Race.RaceFormat.Missing : CoachText.Signed(gap), Style = Ui.Res<Style>("Body"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            toBest.SetResourceReference(TextBlock.ForegroundProperty, CoachText.LossBrush(gap, 0.0005));
            Typography.SetNumeralAlignment(toBest, FontNumeralAlignment.Tabular);
            Grid.SetColumn(toBest, 4);
            foreach (var e in new FrameworkElement[] { no, timeCell, top, toBest }) g.Children.Add(e);
            g.Margin = new Thickness(13, 0, 16, 0);
            var row = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Child = g, Cursor = me ? null : Cursors.Hand };
            row.SetResourceReference(Border.BorderBrushProperty, me ? "Accent" : "Transparent");
            row.SetResourceReference(Border.BackgroundProperty, me ? "AccentSoft" : "Transparent");
            if (!me)
            {
                row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Hover");
                row.MouseLeave += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Transparent");
                row.MouseLeftButtonUp += (_, _) => actions.PickLap(number);
            }
            rows.Children.Add(row);
        }
        stack.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var legendBest = new TextBlock { Text = "Purple = the session's fastest clean lap", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 8, 16, 0) };
        legendBest.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        if (data.SessionBest is not null) stack.Children.Add(legendBest);
        var note = new TextBlock { Text = $"Tick up to {SimRacing.Race.RaceFormat.Count(MaxExtras, "more lap")} to add them to the replay", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 8, 16, 0) };
        ticks.Add(note);
        stack.Children.Add(note);

        var set = new Grid { Margin = new Thickness(16, 12, 16, 14) };
        set.ColumnDefinitions.Add(new ColumnDefinition());
        set.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rule = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-4, -12, -4, 0) }, Orientation.Horizontal);
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        Grid.SetColumnSpan(rule, 2);
        set.Children.Add(rule);
        int r = 0;
        void Line(string label, string value)
        {
            set.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2) };
            var v = new TextBlock { Text = value, Style = Ui.Res<Style>("Body"), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
            v.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            set.Children.Add(l);
            set.Children.Add(v);
            r++;
        }
        Line("Best lap", StatFormat.LapTime(best));
        if (coach is not null)
        {
            var pool = data.All.Where(l => l.Clean).Select(l => l.Trace).ToList();
            if (pool.Count < 2) pool = data.All.Select(l => l.Trace).ToList();
            double theo = coach.Turns.Sum(t => pool.Min(l => LapCoach.Seconds(l, t)));
            if (theo > 0) Line("Best of every turn added up", StatFormat.LapTime(Math.Min(theo, best)));
        }
        Line("Laps with a trace", data.All.Count.ToString(CultureInfo.CurrentCulture));
        stack.Children.Add(set);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0, 14, 0, 0), VerticalAlignment = VerticalAlignment.Top, Child = stack };
    }

    private static FrameworkElement Summary(AnalysisData data, (int Lap, double Time, LapTrace Trace) mine, AnalysisState s, bool compared, string phrase, bool imperial)
    {
        var grid = new Grid();
        for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var lead = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        lead.Children.Add(new TextBlock { Text = $"Lap {mine.Lap}", Style = Ui.Res<Style>("Caption") });
        var lapTime = RaceParts.BigFigure(StatFormat.LapTime(mine.Time), 38, "Heading");
        lapTime.HorizontalAlignment = HorizontalAlignment.Left;
        lead.Children.Add(lapTime);
        grid.Children.Add(lead);
        int col = 1;
        FrameworkElement Cell(FrameworkElement body)
        {
            var cell = Hairline.Frame(new Border { Child = body, Padding = new Thickness(12, 2, 12, 2), VerticalAlignment = VerticalAlignment.Center }, Sides.Left);
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetColumn(cell, col++);
            grid.Children.Add(cell);
            return cell;
        }
        void Stat(string value, string label, string brush = "Heading")
        {
            var v = RaceParts.LiveFigure(value, 20, brush);
            v.HorizontalAlignment = HorizontalAlignment.Left;
            Cell(new StackPanel { Children = { v, new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") } } });
        }
        double gap = mine.Time - s.Reference.Trace.LapTime;
        Stat(compared ? $"{CoachText.Signed(gap, "0.000")} {Units.S}" : SimRacing.Race.RaceFormat.Missing, compared ? $"Against {phrase}" : "Nothing to compare with", compared ? CoachText.LossBrush(gap, 0.0005) : "TextMuted");
        var clean = data.All.Where(l => l.Clean).Select(l => l.Time).ToList();
        if (clean.Count == 0) clean = data.All.Select(l => l.Time).ToList();
        Stat($"{StatFormat.LapTime(clean.Min())} · {StatFormat.LapTime(clean.Average())}", "Best · average");
        double top = LapCoach.Top(mine.Trace), topAll = data.All.Max(l => LapCoach.Top(l.Trace));
        Stat(CoachText.Speed(top, imperial), $"Top speed · best {CoachText.SpeedValue(topAll, imperial).ToString("0", CultureInfo.CurrentCulture)}");
        Stat($"{(clean.Max() - clean.Min()).ToString("0.00", CultureInfo.CurrentCulture)} {Units.S}", "Fastest to slowest lap");

        var p = LapCoach.Pedals(mine.Trace);
        var pedals = new StackPanel();
        pedals.Children.Add(new TextBlock { Text = "Pedals, share of the lap", Style = Ui.Res<Style>("Caption") });
        var barGrid = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 6) };
        var share = new[] { (p.Throttle, StatsUi.Hue("Sage").Deep), (p.Brake, "Danger"), (p.Neither, "Warn") };
        foreach (var (v, key) in share)
        {
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, v), GridUnitType.Star) });
            var b = new Border();
            b.SetResourceReference(Border.BackgroundProperty, key);
            Grid.SetColumn(b, barGrid.ColumnDefinitions.Count - 1);
            barGrid.Children.Add(b);
        }
        pedals.Children.Add(new Tile { CornerRadius = ThemeManager.Round(4), ClipChild = true, Child = barGrid });
        var keys = new WrapPanel();
        foreach (var (label, (v, key)) in new[] { "Throttle", "Brake", "Neither" }.Zip(share))
        {
            var sw = new Border { Width = 8, Height = 8, CornerRadius = ThemeManager.Round(2), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            sw.SetResourceReference(Border.BackgroundProperty, key);
            var t = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            t.Inlines.Add(new Run(label + " "));
            t.Inlines.Add(new Run((v * 100).ToString("0", CultureInfo.CurrentCulture) + "%") { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Heading") });
            keys.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), Children = { sw, t } });
        }
        pedals.Children.Add(keys);
        Cell(pedals);
        var about = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = $"{data.Session.Car} · {data.Session.Place} · {Dates.Day(data.Session.StartUtc.ToLocalTime())}", Style = Ui.Res<Style>("Caption"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8),
        });
        about.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 12, 8, 14), Child = new StackPanel { Children = { about, grid } } };
    }

    private static FrameworkElement WholeLap(AnalysisData data, AnalysisState s, Config.SessionStatsSettings settings, AnalysisActions actions, ReplayMemory memory)
    {
        Action lanesChanged = () => { };
        var mapMode = s.Single
            ? Ui.Segmented(new[] { "Plain", "Speed" }, (int)s.MapColors, i => { s.MapColors = (MapColors)i; s.RaiseCursor(); }, "lapMapColors",
                new[] { "The lap in its color", "The lap colored by speed: slow to fast" })
            : Ui.Segmented(new[] { "Laps", "Speed", "Delta" }, (int)s.MapColors, i => { s.MapColors = (MapColors)i; s.RaiseCursor(); }, "lapMapColors",
                new[] { "Each lap in its color", "The first lap colored by speed: slow to fast", "The first lap colored by where it gains (or loses) time on the reference" });
        var reset = Ui.LabeledButton("I.Undo", "Reset zoom", s.ResetZoom);
        reset.IsEnabled = false;
        s.ZoomChanged += () => reset.IsEnabled = s.Zoomed;
        var lanesButton = Ui.LabeledButton("I.Sliders", "Lanes", () => { });
        lanesButton.Click += (_, _) => OpenLanes(lanesButton, settings, lanesChanged);
        lanesButton.Margin = new Thickness(8, 0, 0, 0);

        var map = new LapMap(s) { ToolTip = "The wheel zooms in on the spot under the pointer (the charts follow); double-click = the whole lap" };
        var mapCursor = new MapCursor(s, map) { IsHitTestVisible = false };
        var mapHost = new Grid { ClipToBounds = true, Children = { map, mapCursor } };
        var chartHost = new Grid();
        TraceChart? chart = null;
        ChartCursor? chartCursor = null;
        void ShowLanes()
        {
            var lanes = (settings.AnalysisLanes ?? Config.SessionStatsSettings.DefaultLanes.ToList()).Where(LaneNames.ContainsKey).Where(l => !(s.Single && l == "delta")).ToList();
            if (lanes.Count == 0) lanes.Add("speed");
            if (chart is not null && chart.Lanes.SequenceEqual(lanes)) return;
            chart?.Detach();
            chartCursor?.Detach();
            chart = new TraceChart(s, lanes);
            chartCursor = new ChartCursor(s, chart) { IsHitTestVisible = false };
            chartHost.Children.Clear();
            chartHost.Children.Add(chart);
            chartHost.Children.Add(chartCursor);
            map.Height = chart.Height;
        }
        ShowLanes();
        lanesChanged = () => { ShowLanes(); actions.Save(); };

        var mapTitle = StatsSections.Chart("Line", s.Lines.Any(l => l.Line is not null) ? (s.Single ? "Where the lap drove" : "Where each lap drove; the wide band is the reference's") : null, new Border());
        mapTitle.Children.RemoveAt(mapTitle.Children.Count - 1);
        mapMode.HorizontalAlignment = HorizontalAlignment.Left;
        mapMode.VerticalAlignment = VerticalAlignment.Top;
        mapMode.Margin = new Thickness(0, 10, 0, 0);
        var mapBody = new StackPanel { Children = { mapTitle, mapMode } };
        mapHost.Margin = new Thickness(0, 10, 0, 0);
        mapBody.Children.Add(mapHost);
        var mapCard = Card(mapBody);
        var chartTitle = StatsSections.Chart("Telemetry", "Drag across to zoom, double-click for the whole lap"
            + (s.Lines.Any(l => l.PushToPass.Count > 0) ? "; shaded spans = push-to-pass in use" : ""), new Border());
        chartTitle.Children.RemoveAt(chartTitle.Children.Count - 1);
        foreach (var t in chartTitle.Children.OfType<TextBlock>()) t.TextWrapping = TextWrapping.Wrap;
        var chartTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { reset, lanesButton } };
        chartHost.Margin = new Thickness(0, 10, 0, 0);
        var chartCard = Card(new StackPanel { Children = { Ui.SplitRow(chartTitle, chartTools, 10), chartHost } });
        mapCard.Margin = chartCard.Margin = new Thickness(0);
        double aspect = 1;
        if (s.Reference.Line is { } line && line.X.Length > 1)
            aspect = Math.Clamp((line.X.Max() - line.X.Min()) / Math.Max(10, line.Y.Max() - line.Y.Min()), 0.4, 2.5);
        var pair = new Grid();
        var mapColumn = new ColumnDefinition { Width = new GridLength(360) };
        pair.ColumnDefinitions.Add(mapColumn);
        pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(chartCard, 2);
        pair.Children.Add(mapCard);
        pair.Children.Add(chartCard);
        void Fit()
        {
            if (chart is null || !mapCard.IsLoaded || !chartCard.IsLoaded || pair.ActualWidth < 100) return;
            double mapHeight = Math.Max(120, map.ActualHeight + chartCard.ActualHeight - mapCard.ActualHeight);
            if (Math.Abs(mapHeight - map.Height) > 0.5) map.Height = mapHeight;
            double chrome = mapCard.Padding.Left + mapCard.Padding.Right + mapCard.BorderThickness.Left + mapCard.BorderThickness.Right;
            double width = Math.Clamp((mapHeight - 20) * aspect + 20 + chrome, 300, (pair.ActualWidth - Ui.PanelSpacing) * 0.55);
            if (Math.Abs(mapColumn.Width.Value - width) > 0.5) mapColumn.Width = new GridLength(width);
        }
        mapCard.SizeChanged += (_, _) => Fit();
        chartCard.SizeChanged += (_, _) => Fit();
        pair.SizeChanged += (_, _) => Fit();
        pair.Loaded += (_, _) => pair.Dispatcher.InvokeAsync(Fit, System.Windows.Threading.DispatcherPriority.Loaded);
        s.ResetZoom();
        pair.Loaded += (_, _) => { if (memory.Distance > 1) s.SetCursor(Math.Clamp(memory.Distance, 0, s.Length)); };
        return pair;
    }

    private static FrameworkElement LaneTag(string id, Action? remove)
    {
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
        var name = new TextBlock
        {
            Text = LaneNames[id], VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Res<Brush>("AccentText"),
        };
        if (remove is not null)
        {
            var x = Ui.IconButton("I.X", "Hide this lane", remove, "QuietIconButton");
            x.Width = x.Height = 18;
            x.MinHeight = 0;
            x.Foreground = Ui.Res<Brush>("AccentText");
            if (x.Content is Icon icon) icon.Width = icon.Height = 10;
            DockPanel.SetDock(x, Dock.Right);
            row.Children.Add(x);
        }
        row.Children.Add(name);
        return new Tile
        {
            Tag = id, Height = 28, Background = Ui.Res<Brush>("AccentSoft"), BorderBrush = Ui.Res<Brush>("AccentText"), BorderThickness = Hairline.Edges(),
            CornerRadius = ThemeManager.Round(4), Padding = new Thickness(12, 0, remove is null ? 10 : 4, 0), Child = row, Cursor = Cursors.SizeAll,
        };
    }

    private static void OpenLanes(FrameworkElement anchor, Config.SessionStatsSettings settings, Action changed)
    {
        var content = new ContentControl();
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            CustomPopupPlacementCallback = (size, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width - size.Width, target.Height), PopupPrimaryAxis.Horizontal) },
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(13, 10, 13, 11), Margin = new Thickness(0, 3, 0, 0), Width = 211, Child = content },
        };
        RenderOptions.SetClearTypeHint(popup.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(popup);
        void Fill()
        {
            var on = (settings.AnalysisLanes ?? Config.SessionStatsSettings.DefaultLanes.ToList()).ToList();
            void Save(List<string> lanes) { settings.AnalysisLanes = lanes; changed(); Fill(); }
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Shown · drag to reorder", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 5) });
            var grid = new ReorderGrid { MaxColumns = 1, MinCellWidth = 178, Gap = 5 };
            foreach (var id in on)
                grid.Children.Add(LaneTag(id, on.Count > 1 ? () => Save(on.Where(l => l != id).ToList()) : null));
            grid.Moved += (_, _) => Save(grid.Children.OfType<FrameworkElement>().Select(c => (string)c.Tag).ToList());
            stack.Children.Add(grid);
            var off = Config.SessionStatsSettings.Lanes.Where(l => !on.Contains(l)).ToList();
            if (off.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "Add", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 10, 0, 5) });
                var add = new StackPanel();
                foreach (var id in off)
                {
                    var lane = id;
                    var b = Ui.LabeledButton("I.Plus", LaneNames[id], () => Save(on.Append(lane).ToList()));
                    b.Margin = new Thickness(0, 0, 0, 5);
                    b.HorizontalContentAlignment = HorizontalAlignment.Left;
                    add.Children.Add(b);
                }
                stack.Children.Add(add);
            }
            content.Content = stack;
        }
        Fill();
        WindowScale.Follow(popup);
        popup.IsOpen = true;
    }

    internal static AnalysisState Make(AnalysisData data, bool imperial, string reference)
    {
        var s = new AnalysisState { Imperial = imperial };
        for (int i = 0; i < data.Laps.Count; i++)
            s.Lines.Add(new AnalysisLine
            {
                Label = $"Lap {data.Laps[i].Lap} · {StatFormat.LapTime(data.Laps[i].Time)}", Trace = data.Laps[i].Trace, BrushKey = LapBrushes[i % LapBrushes.Length],
                Driver = data.Driver, Short = $"Lap {data.Laps[i].Lap}", Lap = data.Laps[i].Lap,
            });
        AnalysisLine? refLine = null;
        if (reference.StartsWith("lap:") && int.TryParse(reference[4..], out int n) && data.Laps.FindIndex(l => l.Lap == n) is var at and >= 0) refLine = s.Lines[at];
        else if (reference.StartsWith("lap:") && int.TryParse(reference[4..], out int other) && data.All.FirstOrDefault(l => l.Lap == other) is { Trace: not null } found)
        {
            refLine = new AnalysisLine { Label = $"Lap {other} · {StatFormat.LapTime(found.Time)}", Trace = found.Trace, BrushKey = "TextMuted", Driver = data.Driver, Short = $"Lap {other}", Lap = other };
            s.Lines.Add(refLine);
        }
        else
        {
            var (label, shortName, driver, trace) = reference switch
            {
                _ when reference.StartsWith("shared:") && data.SharedPicked is { } sp => (SharedLabel(sp.Info, data.Session.Car), "Imported", sp.Info.Driver, sp.Trace),
                "track" when data.TrackBest is { } tb => ($"Your best here · {tb.Label}", "Your best", data.Driver, tb.Trace),
                "optimal" when data.Optimal is { } o => ($"Optimal · {StatFormat.LapTime(o.LapTime)}", "Optimal", data.Driver, o),
                _ when data.SessionBest is { } sb => ($"Session best · lap {sb.Lap} · {StatFormat.LapTime(sb.Trace.LapTime)}", "Session best", data.Driver, sb.Trace),
                _ => ("", "", "", null!),
            };
            if (trace is not null)
            {
                refLine = s.Lines.FirstOrDefault(l => ReferenceEquals(l.Trace, trace));
                if (refLine is null) { refLine = new AnalysisLine { Label = label, Trace = trace, BrushKey = "TextMuted", Driver = driver, Short = shortName }; s.Lines.Add(refLine); }
            }
        }
        refLine ??= s.Lines[0];
        refLine.Reference = true;
        s.Reference = refLine;
        s.Length = refLine.Trace.LengthM;
        s.SectorDistances = data.SectorStarts.Where(p => p > 0).Select(p => p * s.Length).ToList();
        foreach (var line in s.Lines)
        {
            var t = line.Trace;
            if (!line.Reference) line.Delta = LapTrace.Delta(t, refLine.Trace);
            line.Line = t.LineIn(refLine.Trace);
            double speedK = imperial ? 2.236936 : 3.6;
            line.Lanes["delta"] = (line.Delta, null);
            line.Lanes["speed"] = (Scaled(t[TraceChannel.Speed], speedK), null);
            line.Lanes["pedals"] = (Scaled(t[TraceChannel.Throttle], 100), Scaled(t[TraceChannel.Brake], 100));
            line.Lanes["steering"] = (Scaled(t[TraceChannel.Steering], -180 / Math.PI), null);
            line.Lanes["gear"] = (t[TraceChannel.Gear], null);
            line.Lanes["rpm"] = (t[TraceChannel.Rpm], null);
            line.Lanes["latg"] = (Scaled(t[TraceChannel.LatAccel], 1 / 9.80665), null);
            line.Lanes["bias"] = (t[TraceChannel.BrakeBias], null);
            line.Lanes["abs"] = (t[TraceChannel.Abs], null);
            line.Lanes["tc"] = (t[TraceChannel.Tc], null);
            line.PushToPass.AddRange(BoostSpans(t));
            line.Wind = t.WindOfLap();
        }
        s.Lines.Remove(refLine);
        s.Lines.Insert(0, refLine);
        return s;
    }

    private static double[]? Scaled(double[]? values, double k) => values?.Select(v => v * k).ToArray();

    internal static List<(double From, double To)> BoostSpans(LapTrace t)
    {
        var spans = new List<(double, double)>();
        if (t[TraceChannel.PushToPass] is not { } p) return spans;
        int start = -1;
        for (int i = 0; i <= p.Length; i++)
        {
            bool on = i < p.Length && p[i] > 0.5;
            if (on && start < 0) start = i;
            else if (!on && start >= 0) { spans.Add((t.Distance(start), t.Distance(i - 1))); start = -1; }
        }
        return spans;
    }

    internal static string LaneLabel(string id, bool imperial) => id switch
    {
        "delta" => "Time delta (seconds)", "speed" => imperial ? "Speed (mph)" : "Speed (km/h)", "pedals" => "Throttle / brake (%)", "steering" => "Steering angle (degrees)",
        _ => LaneNames.TryGetValue(id, out var n) ? n : id,
    };

    internal static string LaneHue(string lane) => lane switch
    {
        "delta" => "AccentText",
        "speed" => StatsUi.Hue("Slate").Deep,
        "pedals" => StatsUi.Hue("Sage").Deep,
        "steering" => StatsUi.Hue("Plum").Deep,
        "gear" => StatsUi.Hue("Gold").Deep,
        "rpm" => StatsUi.Hue("Coral").Deep,
        "bias" => "Danger",
        "abs" => StatsUi.Hue("Orange").Deep,
        "tc" => StatsUi.Hue("Plum").Deep,
        _ => StatsUi.Hue("Slate").Deep,
    };

    internal static string Format(string lane, double v) => lane switch
    {
        "delta" => Core.SimRacing.Race.RaceFormat.Signed(v, "0.000", CultureInfo.CurrentCulture),
        "latg" => Minus(v, "0.00"),
        "bias" => Minus(v, "0.0"),
        "gear" => v < 0 ? "R" : v == 0 ? "N" : v.ToString("0", CultureInfo.CurrentCulture),
        _ => Minus(v, "0"),
    };

    private static string Minus(double v, string format)
    {
        string magnitude = Math.Abs(v).ToString(format, CultureInfo.CurrentCulture);
        return v < 0 && magnitude.Any(c => c is >= '1' and <= '9') ? "−" + magnitude : magnitude;
    }

    internal static string DistanceText(double m, bool imperial) =>
        imperial ? (m / 1609.344).ToString("0.00", CultureInfo.CurrentCulture) + " " + Units.Mi : (m / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " " + Units.Km;

    internal static SolidColorBrush Brush(string key) => Ui.Res<Brush>(key) as SolidColorBrush ?? Brushes.Gray;

    internal static FormattedText Text(Visual v, string s, Brush brush, double size = 10.5, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiFace(bold: bold), size, brush, VisualTreeHelper.GetDpi(v).PixelsPerDip);
}

internal readonly record struct MapView(double MinX, double MaxY, double Scale, double Ox, double Oy)
{
    public Point At(double x, double y) => new(Ox + (x - MinX) * Scale, Oy + (MaxY - y) * Scale);
}

internal sealed class LapMap : FrameworkElement, IKeepsWheel
{
    private const int Buckets = 16;
    private readonly AnalysisState _s;

    public LapMap(AnalysisState s)
    {
        _s = s;
        s.ZoomChanged += InvalidateVisual;
        s.CursorChanged += () => { if (_mode != s.MapColors) InvalidateVisual(); };
        ClipToBounds = true;
    }

    private MapColors _mode;

    public MapView? View
    {
        get
        {
            if (_s.Reference.Line is not { } r || ActualWidth < 40 || ActualHeight < 40) return null;
            var t = _s.Reference.Trace;
            int i0 = (int)(_s.D0 / t.StepM), i1 = Math.Min(t.Points - 1, (int)Math.Ceiling(_s.D1 / t.StepM));
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = i0; i <= i1; i++)
            {
                minX = Math.Min(minX, r.X[i]); maxX = Math.Max(maxX, r.X[i]);
                minY = Math.Min(minY, r.Y[i]); maxY = Math.Max(maxY, r.Y[i]);
            }
            double pad = 10, w = ActualWidth - 2 * pad, h = ActualHeight - 2 * pad;
            double scale = Math.Min(w / Math.Max(10, maxX - minX), h / Math.Max(10, maxY - minY));
            return new MapView(minX, maxY, scale, pad + (w - (maxX - minX) * scale) / 2, pad + (h - (maxY - minY) * scale) / 2);
        }
    }

    private double? Nearest(Point p, double reach)
    {
        if (View is not { } view || _s.Reference.Line is not { } line) return null;
        var t = _s.Reference.Trace;
        int i0 = (int)(_s.D0 / t.StepM), i1 = Math.Min(t.Points - 1, (int)(_s.D1 / t.StepM));
        int best = -1;
        double near = reach * reach;
        for (int i = i0; i <= i1; i++)
        {
            var q = view.At(line.X[i], line.Y[i]);
            double d = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
            if (d < near) { near = d; best = i; }
        }
        return best >= 0 ? t.Distance(best) : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (View is null) return;
        _s.SetCursor(Nearest(e.GetPosition(this), 30));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = true;
        if (Nearest(e.GetPosition(this), double.MaxValue) is not { } at) return;
        double span = Math.Min(_s.Length, (_s.D1 - _s.D0) * (e.Delta > 0 ? 0.7 : 1 / 0.7));
        _s.Zoom(at - span / 2, at + span / 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { _s.ResetZoom(); e.Handled = true; }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _s.SetCursor(null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        _mode = _s.MapColors;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_s.Reference.Line is not { } r || View is not { } view)
        {
            var none = LapAnalysisView.Text(this, "No position data for these laps", Ui.Res<Brush>("TextMuted"), 9.5);
            dc.DrawText(none, new Point((ActualWidth - none.Width) / 2, (ActualHeight - none.Height) / 2));
            return;
        }
        var t = _s.Reference.Trace;
        var track = new Pen(Ui.Res<Brush>("Line"), Math.Clamp(12 * view.Scale, 6, 46)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        track.Freeze();
        dc.DrawGeometry(null, track, Path(r.X, r.Y, 0, t.Points - 1, view));

        bool zoomed = _s.Zoomed;
        var colored = _s.MapColors == MapColors.Laps ? null : _s.Lines.FirstOrDefault(l => !l.Reference) ?? _s.Lines[0];
        foreach (var line in _s.Lines)
        {
            if (line.Line is not { } xy) continue;
            var lt = line.Trace;
            if (lt.Points <= 0) continue;
            int b = Math.Clamp((int)Math.Ceiling(_s.D1 / lt.StepM), 0, lt.Points - 1), a = Math.Clamp((int)(_s.D0 / lt.StepM), 0, b);
            if (ReferenceEquals(line, colored) && Values(line) is { } values)
            {
                if (zoomed) dc.DrawGeometry(null, PenOf(line, 0.25, _s.Single), Path(xy.X, xy.Y, 0, lt.Points - 1, view));
                DrawBuckets(dc, xy, values, a, b, view);
                continue;
            }
            if (colored is not null && !line.Reference) continue;
            if (zoomed) dc.DrawGeometry(null, PenOf(line, 0.25, _s.Single), Path(xy.X, xy.Y, 0, lt.Points - 1, view));
            dc.DrawGeometry(null, PenOf(line, line.Reference && !_s.Single ? 0.7 : 1, _s.Single), Path(xy.X, xy.Y, a, b, view));
        }
        foreach (double d in _s.SectorDistances.Prepend(0))
        {
            int i = Math.Min(t.Points - 1, (int)(d / t.StepM));
            if (d < _s.D0 - 1 || d > _s.D1 + 1) continue;
            var p = view.At(r.X[i], r.Y[i]);
            var label = LapAnalysisView.Text(this, d == 0 ? "S/F" : "S" + (_s.SectorDistances.ToList().IndexOf(d) + 2), Ui.Res<Brush>("TextMuted"), 8.5, bold: true);
            dc.DrawText(label, new Point(p.X + 8, p.Y - label.Height - 4));
            dc.DrawEllipse(Ui.Res<Brush>("TextMuted"), null, p, 2.5, 2.5);
        }
    }

    private double[]? Values(AnalysisLine line)
    {
        if (_s.MapColors == MapColors.Speed) return line.Trace[TraceChannel.Speed];
        if (line.Delta is not { } delta) return null;
        var slope = new double[line.Trace.Points];
        for (int i = 0; i < slope.Length; i++)
        {
            int a = Math.Max(0, i - 15), b = Math.Min(delta.Length - 1, i + 15);
            slope[i] = b > a && b < delta.Length ? (delta[b] - delta[a]) / (b - a) : 0;
        }
        return slope;
    }

    private void DrawBuckets(DrawingContext dc, (double[] X, double[] Y) xy, double[] values, int a, int b, MapView view)
    {
        Color[] colors;
        Func<double, int> bucket;
        if (_s.MapColors == MapColors.Speed)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = a; i <= b; i++) { lo = Math.Min(lo, values[i]); hi = Math.Max(hi, values[i]); }
            Color slow = LapAnalysisView.Brush(StatsUi.Hue("Coral").Deep).Color, mid = LapAnalysisView.Brush(StatsUi.Hue("Gold").Deep).Color, fast = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color;
            colors = Enumerable.Range(0, Buckets).Select(k => { double u = k / (Buckets - 1.0); return u < 0.5 ? ThemeManager.Mix(slow, mid, u * 2) : ThemeManager.Mix(mid, fast, u * 2 - 1); }).ToArray();
            double span = Math.Max(1e-6, hi - lo);
            bucket = v => (int)Math.Clamp((v - lo) / span * (Buckets - 1), 0, Buckets - 1);
        }
        else
        {
            Color gain = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color, even = LapAnalysisView.Brush("TextFaint").Color, loss = LapAnalysisView.Brush(StatsUi.Hue("Coral").Deep).Color;
            colors = Enumerable.Range(0, Buckets).Select(k => { double u = k / (Buckets - 1.0); return u < 0.5 ? ThemeManager.Mix(gain, even, u * 2) : ThemeManager.Mix(even, loss, u * 2 - 1); }).ToArray();
            const double Full = 0.0015;
            bucket = v => (int)Math.Clamp((v / Full + 1) / 2 * (Buckets - 1), 0, Buckets - 1);
        }
        var geos = new StreamGeometry[Buckets];
        var ctx = new StreamGeometryContext?[Buckets];
        for (int i = a; i < b && i + 1 < values.Length; i++)
        {
            int k = bucket(values[i]);
            if (ctx[k] is null) { geos[k] = new StreamGeometry(); ctx[k] = geos[k].Open(); }
            ctx[k]!.BeginFigure(view.At(xy.X[i], xy.Y[i]), false, false);
            ctx[k]!.LineTo(view.At(xy.X[i + 1], xy.Y[i + 1]), true, false);
        }
        for (int k = 0; k < Buckets; k++)
        {
            if (ctx[k] is not { } c) continue;
            c.Close();
            geos[k].Freeze();
            var pen = new Pen(new SolidColorBrush(colors[k]), 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geos[k]);
        }
    }

    private static Pen PenOf(AnalysisLine line, double opacity, bool single)
    {
        var brush = LapAnalysisView.Brush(line.BrushKey).Clone();
        brush.Opacity = opacity;
        var pen = new Pen(brush, line.Reference && !single ? 2 : 2.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (line.Reference && !single) pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
        pen.Freeze();
        return pen;
    }

    private static StreamGeometry Path(double[] xs, double[] ys, int a, int b, MapView view)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(view.At(xs[a], ys[a]), false, false);
            var last = view.At(xs[a], ys[a]);
            for (int i = a + 1; i <= b; i++)
            {
                var p = view.At(xs[i], ys[i]);
                if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) < 0.75 && i < b) continue;
                g.LineTo(p, true, true);
                last = p;
            }
        }
        geo.Freeze();
        return geo;
    }
}

internal sealed class MapCursor : FrameworkElement
{
    private readonly AnalysisState _s;
    private readonly LapMap _map;

    public MapCursor(AnalysisState s, LapMap map)
    {
        _s = s;
        _map = map;
        s.CursorChanged += InvalidateVisual;
        s.ZoomChanged += InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_s.Cursor is not { } d || _map.View is not { } view) return;
        var edge = Ui.Res<Brush>("Surface");
        foreach (var line in _s.Lines)
        {
            if (line.Line is not { } xy) continue;
            int i = Math.Clamp((int)Math.Round(d / line.Trace.StepM), 0, line.Trace.Points - 1);
            var p = view.At(xy.X[i], xy.Y[i]);
            dc.DrawEllipse(Ui.Res<Brush>(line.BrushKey), new Pen(edge, 2), p, 6, 6);
        }
    }
}

internal sealed class TraceChart : FrameworkElement
{
    public const double Left = 75, Right = 10, Axis = 21;
    private readonly AnalysisState _s;
    private Point? _press;
    private bool _dragging;

    public TraceChart(AnalysisState s, List<string> lanes)
    {
        _s = s;
        Lanes = lanes;
        Height = lanes.Sum(LaneHeight) + Axis;
        s.ZoomChanged += InvalidateVisual;
        ClipToBounds = true;
        Focusable = false;
    }

    public void Detach() => _s.ZoomChanged -= InvalidateVisual;

    public List<string> Lanes { get; }

    public AnalysisState State => _s;

    public static double LaneHeight(string lane) => lane switch { "gear" or "bias" or "abs" or "tc" => 58, "delta" => 96, "speed" => 112, "pedals" => 91, _ => 81 };

    public double PlotWidth => Math.Max(1, ActualWidth - Left - Right);

    public double X(double d) => Left + (d - _s.D0) / Math.Max(1, _s.D1 - _s.D0) * PlotWidth;

    public double DistanceAt(double x) => _s.D0 + Math.Clamp((x - Left) / PlotWidth, 0, 1) * (_s.D1 - _s.D0);

    public Dictionary<string, (double Top, double Height, double Lo, double Hi)> Layout { get; } = new();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { _s.ResetZoom(); e.Handled = true; return; }
        _press = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_press is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            if (Math.Abs(p.X - start.X) > 4) _dragging = true;
            if (_dragging) _s.Selecting = (DistanceAt(start.X), DistanceAt(p.X));
        }
        _s.SetCursor(p.X >= Left && p.X <= ActualWidth - Right ? DistanceAt(p.X) : null);
        if (_dragging) _s.RaiseCursor();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging && _s.Selecting is { } sel && Math.Abs(sel.To - sel.From) >= 5) _s.Zoom(sel.From, sel.To);
        _press = null;
        _dragging = false;
        _s.Selecting = null;
        ReleaseMouseCapture();
        _s.RaiseCursor();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_press is null) _s.SetCursor(null);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double at = DistanceAt(e.GetPosition(this).X), k = e.Delta > 0 ? 0.8 : 1.25;
        double span = Math.Min(_s.Length, (_s.D1 - _s.D0) * k);
        double share = (at - _s.D0) / Math.Max(1, _s.D1 - _s.D0);
        _s.Zoom(at - span * share, at - span * share + span);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        Layout.Clear();
        if (ActualWidth <= Left + Right + 20) return;
        double px = Hairline.ScaleOf(this);
        var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
        var strong = Hairline.Pen(Ui.Res<Brush>("LineStrong"), px);
        var muted = Ui.Res<Brush>("TextMuted");
        var boost = _s.Lines.Where(l => l.PushToPass.Count > 0).Select(l => (Faint(LapAnalysisView.Brush(l.BrushKey)), l.PushToPass)).ToList();
        double top = 0;
        foreach (var lane in Lanes)
        {
            double h = LaneHeight(lane), plotTop = top + 21, plotH = h - 28;
            var (lo, hi) = Range(lane);
            Layout[lane] = (plotTop, plotH, lo, hi);
            double Y(double v) => plotTop + plotH * (hi - v) / Math.Max(1e-9, hi - lo);
            dc.DrawText(LapAnalysisView.Text(this, LapAnalysisView.LaneLabel(lane, _s.Imperial), Ui.Res<Brush>("Heading"), 10, bold: true), new Point(0, top + 1));
            var hiText = LapAnalysisView.Text(this, LapAnalysisView.Format(lane, hi), muted, 9.5);
            var loText = LapAnalysisView.Text(this, LapAnalysisView.Format(lane, lo), muted, 9.5);
            dc.DrawText(hiText, new Point(Left - 8 - hiText.Width, plotTop - hiText.Height / 2));
            dc.DrawText(loText, new Point(Left - 8 - loText.Width, plotTop + plotH - loText.Height / 2));
            dc.DrawLine(grid, new Point(Left, Hairline.Snap(plotTop, px)), new Point(ActualWidth - Right, Hairline.Snap(plotTop, px)));
            dc.DrawLine(grid, new Point(Left, Hairline.Snap(plotTop + plotH, px)), new Point(ActualWidth - Right, Hairline.Snap(plotTop + plotH, px)));
            if (lane is "delta" or "steering" or "latg" && lo < 0 && hi > 0) dc.DrawLine(strong, new Point(Left, Hairline.Snap(Y(0), px)), new Point(ActualWidth - Right, Hairline.Snap(Y(0), px)));

            dc.PushClip(new RectangleGeometry(new Rect(Left, plotTop - 2, PlotWidth, plotH + 4)));
            for (int k = 0; k < boost.Count; k++)
                foreach (var (from, to) in boost[k].PushToPass)
                {
                    double a = X(from), b = X(to);
                    if (b >= Left && a <= ActualWidth - Right) dc.DrawRectangle(boost[k].Item1, null, new Rect(a, plotTop, Math.Max(1, b - a), plotH));
                }
            if (!_s.Lines.Any(l => l.Lanes.TryGetValue(lane, out var has) && has.A is not null))
            {
                var none = LapAnalysisView.Text(this, "Not recorded on these laps", muted, 10);
                dc.DrawText(none, new Point(Left + 8, plotTop + (plotH - none.Height) / 2));
            }
            foreach (var line in _s.Lines)
            {
                if (!line.Lanes.TryGetValue(lane, out var series)) continue;
                if (series.A is { } a) dc.DrawGeometry(null, PenFor(line, false, lane, _s.Single), Decimated(a, line.Trace.StepM, Y, lane == "gear" || LapAnalysisView.SettingLanes.Contains(lane)));
                if (series.B is { } b) dc.DrawGeometry(null, PenFor(line, true, lane, _s.Single), Decimated(b, line.Trace.StepM, Y, false));
            }
            dc.Pop();
            top += h;
        }
        var sectorPen = new Pen(Ui.Res<Brush>("LineStrong"), Hairline.Pixels(1, px)) { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) };
        sectorPen.Freeze();
        for (int k = 0; k < _s.SectorDistances.Count; k++)
        {
            double d = _s.SectorDistances[k];
            if (d <= _s.D0 || d >= _s.D1) continue;
            double x = Hairline.Snap(X(d), px);
            dc.DrawLine(sectorPen, new Point(x, 14), new Point(x, top));
            dc.DrawText(LapAnalysisView.Text(this, "S" + (k + 2), muted, 9.5, bold: true), new Point(x + 4, 1));
        }
        dc.DrawLine(strong, new Point(Left, Hairline.Snap(top, px)), new Point(ActualWidth - Right, Hairline.Snap(top, px)));
        double span = _s.D1 - _s.D0;
        double unit = _s.Imperial ? 1609.344 : 1000;
        double step = new[] { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2 }.Select(v => v * unit).FirstOrDefault(v => span / v <= 8, 5 * unit);
        for (double d = Math.Ceiling(_s.D0 / step) * step; d <= _s.D1; d += step)
        {
            double x = X(d);
            dc.DrawLine(strong, new Point(x, top), new Point(x, top + 4));
            var label = LapAnalysisView.Text(this, LapAnalysisView.DistanceText(d, _s.Imperial), muted, 9.5);
            if (x - label.Width / 2 >= Left - 4 && x + label.Width / 2 <= ActualWidth) dc.DrawText(label, new Point(x - label.Width / 2, top + 6));
        }
    }

    private (double Lo, double Hi) Range(string lane)
    {
        if (lane == "pedals") return (0, 100);
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var line in _s.Lines)
        {
            if (!line.Lanes.TryGetValue(lane, out var series)) continue;
            foreach (var v in new[] { series.A, series.B })
            {
                if (v is null) continue;
                int a = (int)(_s.D0 / line.Trace.StepM), b = Math.Min(v.Length - 1, (int)Math.Ceiling(_s.D1 / line.Trace.StepM));
                for (int i = a; i <= b; i++) { lo = Math.Min(lo, v[i]); hi = Math.Max(hi, v[i]); }
            }
        }
        if (lo > hi) return (0, 1);
        if (lane == "delta")
        {
            lo = Math.Min(lo, 0);
            hi = Math.Max(hi, 0);
            double margin = Math.Max(hi - lo, 0.05) * 0.08;
            return (lo - margin, hi + margin);
        }
        if (lane is "steering" or "latg")
        {
            double m = Math.Max(Math.Max(Math.Abs(lo), Math.Abs(hi)), lane == "delta" ? 0.05 : 0.1) * 1.08;
            return (-m, m);
        }
        if (lane == "gear") return (Math.Min(0, lo), Math.Max(lo + 1, hi));
        if (lane is "abs" or "tc") return (Math.Floor(lo) - 1, Math.Ceiling(hi) + 1);
        if (lane == "bias") return (lo - Math.Max(0.5, (hi - lo) * 0.2), hi + Math.Max(0.5, (hi - lo) * 0.2));
        double pad = Math.Max(1e-6, (hi - lo) * 0.06);
        return (lo - pad, hi + pad);
    }

    private static Brush Faint(SolidColorBrush brush)
    {
        var faint = brush.Clone();
        faint.Opacity = 0.14;
        faint.Freeze();
        return faint;
    }

    private static Pen PenFor(AnalysisLine line, bool second, string lane, bool single)
    {
        if (single)
        {
            string key = lane == "pedals" ? (second ? "Danger" : StatsUi.Hue("Sage").Deep) : line.BrushKey;
            var solid = new Pen(LapAnalysisView.Brush(key), 1.6) { LineJoin = PenLineJoin.Round };
            solid.Freeze();
            return solid;
        }
        var brush = LapAnalysisView.Brush(line.BrushKey);
        if (line.Reference || second) { brush = brush.Clone(); brush.Opacity = line.Reference && second ? 0.45 : 0.7; }
        var pen = new Pen(brush, line.Reference ? 1.4 : 1.6) { LineJoin = PenLineJoin.Round };
        if (second || (line.Reference && lane != "pedals")) pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
        pen.Freeze();
        return pen;
    }

    private StreamGeometry Decimated(double[] v, double step, Func<double, double> y, bool stepped)
    {
        var geo = new StreamGeometry();
        int a = Math.Max(0, (int)(_s.D0 / step) - 1), b = Math.Min(v.Length - 1, (int)Math.Ceiling(_s.D1 / step) + 1);
        if (b <= a) { geo.Freeze(); return geo; }
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(X(a * step), y(v[a])), false, false);
            double last = v[a];
            int column = int.MinValue;
            double min = 0, max = 0, minX = 0, maxX = 0;
            void Flush()
            {
                if (column == int.MinValue) return;
                if (minX <= maxX) { g.LineTo(new Point(minX, y(min)), true, true); if (max != min) g.LineTo(new Point(maxX, y(max)), true, true); }
                else { g.LineTo(new Point(maxX, y(max)), true, true); if (max != min) g.LineTo(new Point(minX, y(min)), true, true); }
            }
            for (int i = a + 1; i <= b; i++)
            {
                double x = X(i * step), value = v[i];
                if (stepped && value != last) g.LineTo(new Point(x, y(last)), true, true);
                last = value;
                int col = (int)x;
                if (col != column)
                {
                    Flush();
                    column = col;
                    min = max = value;
                    minX = maxX = x;
                }
                else
                {
                    if (value < min) { min = value; minX = x; }
                    if (value > max) { max = value; maxX = x; }
                }
            }
            Flush();
        }
        geo.Freeze();
        return geo;
    }
}

internal sealed class ChartCursor : FrameworkElement
{
    private readonly AnalysisState _s;
    private readonly TraceChart _chart;

    public ChartCursor(AnalysisState s, TraceChart chart)
    {
        _s = s;
        _chart = chart;
        s.CursorChanged += InvalidateVisual;
        s.ZoomChanged += InvalidateVisual;
    }

    public void Detach()
    {
        _s.CursorChanged -= InvalidateVisual;
        _s.ZoomChanged -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double bottom = _chart.ActualHeight - TraceChart.Axis;
        double px = Hairline.ScaleOf(this);
        if (_s.Selecting is { } sel)
        {
            double a = _chart.X(Math.Min(sel.From, sel.To)), b = _chart.X(Math.Max(sel.From, sel.To));
            var fill = LapAnalysisView.Brush("Accent").Clone();
            fill.Opacity = 0.14;
            dc.DrawRectangle(fill, Hairline.Pen(Ui.Res<Brush>("Accent"), px), Hairline.SnapBox(new Rect(a, 0, Math.Max(1, b - a), bottom), px));
        }
        if (_s.Cursor is not { } d || d < _s.D0 || d > _s.D1) return;
        double x = Hairline.Snap(_chart.X(d), px);
        dc.DrawLine(Hairline.Pen(Ui.Res<Brush>("Heading"), px), new Point(x, 0), new Point(x, bottom));
        var panel = Ui.Res<Brush>("Surface");
        foreach (var (lane, (top, height, lo, hi)) in _chart.Layout)
        {
            var parts = new List<FormattedText>();
            foreach (var line in _s.Lines)
            {
                if (!line.Lanes.TryGetValue(lane, out var series) || series.A is not { } values) continue;
                int i = Math.Clamp((int)Math.Round(d / line.Trace.StepM), 0, values.Length - 1);
                string text = LapAnalysisView.Format(lane, values[i]);
                if (series.B is { } b && i < b.Length) text += " / " + LapAnalysisView.Format(lane, b[i]);
                if (line.BoostAt(d)) text += "  P2P";
                parts.Add(LapAnalysisView.Text(this, text, Ui.Res<Brush>(line.BrushKey), 12.5, bold: true));
            }
            if (parts.Count == 0) continue;
            double w = parts.Sum(p => p.Width) + 14 * (parts.Count - 1) + 16, h = parts.Max(p => p.Height) + 4;
            double left = x + 8 + w > ActualWidth ? x - 8 - w : x + 8;
            dc.DrawRectangle(panel, Hairline.Pen(Ui.Res<Brush>("LineStrong"), px), Hairline.SnapBox(new Rect(left, top - h - 1, w, h), px));
            double at = left + 8;
            foreach (var p in parts) { dc.DrawText(p, new Point(at, top - h + 1)); at += p.Width + 14; }
        }
        var dist = LapAnalysisView.Text(this, LapAnalysisView.DistanceText(d, _s.Imperial), Ui.Res<Brush>("Heading"), 9.5, bold: true);
        double dl = Math.Clamp(x - dist.Width / 2 - 4, 0, Math.Max(0, ActualWidth - dist.Width - 8));
        dc.DrawRectangle(panel, null, new Rect(dl, bottom + 3, dist.Width + 8, dist.Height + 2));
        dc.DrawText(dist, new Point(dl + 4, bottom + 4));
    }
}
