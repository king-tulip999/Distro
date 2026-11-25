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

internal sealed class CoachSetup
{
    public required AnalysisData Data { get; init; }
    public required LapTrace Mine { get; init; }
    public required int Lap { get; init; }
    public required LapTrace Reference { get; init; }
    public required string Phrase { get; init; }
    public required string Column { get; init; }
    public required LapCoach Coach { get; init; }
    public required bool Imperial { get; init; }
    public required ReplayMemory Memory { get; init; }
    public required Action<int> OpenTurn { get; init; }
    public required Action<int> ReplayTurn { get; init; }
    public required IReadOnlyList<string> Names { get; init; }

    public string Name(int k) => Names[k];

    public (double From, double To) Window(int k)
    {
        var t = Coach.Turns[k];
        var y = Coach.Mine[k];
        var r = Coach.Theirs[k];
        double first = new[] { y.Brake, r.Brake }.OfType<double>().Append(t.Apex - 120).Min();
        double last = new[] { y.FullThrottle, r.FullThrottle }.OfType<double>().Append(t.Apex + 80).Max();
        return (Math.Max(0, first - 60), Math.Min(Coach.Length - Mine.StepM, last + 50));
    }

    public IReadOnlyList<(int Lap, double Time, LapTrace Trace)> Pool
    {
        get
        {
            var clean = Data.All.Where(l => l.Clean).ToList();
            return (clean.Count >= 2 ? clean : Data.All).Select(l => (l.Lap, l.Time, l.Trace)).ToList();
        }
    }

    public static IReadOnlyList<string> NamesOf(LapCoach coach, LapTrace reference, AnalysisData data)
    {
        var numbered = coach.Turns.Select(t => "T" + t.Number).ToList();
        if (data.Map is not { } map) return numbered;
        var names = SimRacing.TrackMaps.TurnNames.From(map, data.Session.TrackKm);
        if (!names.HasTurns) return numbered;
        var found = coach.Turns.Select(t => names.Where(t.Apex / Math.Max(1, reference.LengthM))).ToList();
        return found.Distinct().Count() == found.Count && found.All(n => n.Length <= 12) ? found : numbered;
    }
}

internal static class CoachText
{
    private static CultureInfo C => CultureInfo.CurrentCulture;

    public static string Speed(double ms, bool imperial) => SpeedValue(ms, imperial).ToString("0", C) + " " + (imperial ? Units.Mph : Units.Kmh);

    public static double SpeedValue(double ms, bool imperial) => imperial ? ms * 2.236936 : ms * 3.6;

    public static string SpeedUnit(bool imperial) => imperial ? Units.Mph : Units.Kmh;

    public static double Length(double m, bool imperial) => imperial ? m * 3.28084 : m;

    public static string Metres(double m, bool imperial) => Length(m, imperial).ToString("0", C) + " " + (imperial ? Units.Ft : Units.M);

    public static string Position(double? v, double zero, bool imperial)
    {
        if (v is not { } d) return SimRacing.Race.RaceFormat.Missing;
        double x = Length(d - zero, imperial);
        return (x < -0.5 ? "−" : x > 0.5 ? "+" : "") + Math.Abs(x).ToString("0", C) + " " + (imperial ? Units.Ft : Units.M);
    }

    public static string Seconds(double s) => s.ToString("0.00", C) + " " + Units.S;

    public static string Signed(double v, string format = "0.00") => SimRacing.Race.RaceFormat.Signed(v, format, C);

    public static string LossBrush(double v, double eps = LapCoach.LeastLoss) => v > eps ? StatsUi.Hue("Coral").Deep : v < -eps ? StatsUi.Hue("Sage").Deep : "TextMuted";

    public static string PhaseName(CoachPhase p) => p switch { CoachPhase.Braking => "Braking", CoachPhase.Middle => "Middle of the turn", _ => "Before and after" };

    public static string Advice(CoachDiff? d, string phrase, bool imperial)
    {
        if (d is null) return $"Your pedal points match {phrase} here; the time goes between them.";
        double gap = Math.Abs(d.Mine - d.Theirs);
        return d.Kind switch
        {
            CoachKind.BrakePoint => $"Brake **{Metres(gap, imperial)} later**, where {phrase} does.",
            CoachKind.Release => $"Keep a little brake on **{Metres(gap, imperial)} longer**, as {phrase} does.",
            CoachKind.BrakeTime => $"Let go of the brake sooner: it's on for **{Seconds(d.Mine)}** against {Seconds(d.Theirs)}.",
            CoachKind.Peak => $"Brake harder at the start: **{d.Mine * 100:0}%** at most against {d.Theirs * 100:0}%.",
            CoachKind.LowSpeed => $"Carry **{SpeedValue(gap, imperial):0} {SpeedUnit(imperial)} more** through the slowest point: {SpeedValue(d.Mine, imperial):0} instead of {SpeedValue(d.Theirs, imperial):0} {SpeedUnit(imperial)}.",
            CoachKind.ThrottleOn => $"Back on the throttle **{Metres(gap, imperial)} sooner**.",
            CoachKind.Coast => $"Shorten the gap between pedals: **{Seconds(d.Mine)}** with neither pedal against {Seconds(d.Theirs)}.",
            CoachKind.FullThrottle => $"Full throttle **{Metres(gap, imperial)} sooner** after the turn.",
            _ => $"Leave the turn faster: **{SpeedValue(gap, imperial):0} {SpeedUnit(imperial)} slower** than {phrase} {Metres(LapCoach.SpeedAfterM, imperial)} after the slowest point.",
        };
    }

    public static TextBlock Rich(string text, string style = "Body")
    {
        var t = new TextBlock { Style = Ui.Res<Style>(style), TextWrapping = TextWrapping.Wrap };
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            if (i % 2 == 1) run.FontWeight = FontWeights.SemiBold;
            t.Inlines.Add(run);
        }
        return t;
    }
}

internal static class LapCoachView
{
    private const double LeftColumn = 380, RibbonHeight = 280, TracesHeight = 228;

    public static FrameworkElement Coach(CoachSetup c)
    {
        var coach = c.Coach;
        int turn = Math.Clamp(c.Memory.Turn, 0, coach.Turns.Count - 1);
        var worst = coach.Worst();
        if (c.Memory.Turn < 0 && worst.Count > 0) turn = worst[0].Turn.Index;
        c.Memory.Turn = turn;

        var left = new StackPanel();
        left.Children.Add(ToDo(c, worst, turn));
        var measured = Measured(c, turn);
        measured.Margin = new Thickness(0, Ui.PanelSpacing, 0, 0);
        left.Children.Add(measured);

        var right = Detail(c, turn);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftColumn) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 2);
        row.Children.Add(left);
        row.Children.Add(right);

        var adds = AddsUp(c);
        adds.Margin = new Thickness(0, Ui.PanelSpacing, 0, 0);
        return new StackPanel { Children = { row, adds } };
    }

    private static Tile Card(FrameworkElement child, Thickness? padding = null) => new()
    {
        Style = Ui.Res<Style>("Card"), Padding = padding ?? new Thickness(18, 15, 18, 16), Child = child,
    };

    private static FrameworkElement ToDo(CoachSetup c, List<CoachAdvice> worst, int turn)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "What to change, biggest first", Style = Ui.Res<Style>("H3") });
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        int n = 0;
        foreach (var a in worst)
        {
            n++;
            int k = a.Turn.Index;
            var dot = new System.Windows.Shapes.Ellipse();
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TileRaised");
            var digit = new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            digit.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            var badge = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Children = { dot, digit } };
            var text = CoachText.Rich($"**{c.Name(k)}** · " + CoachText.Advice(a.Change, c.Phrase, c.Imperial));
            text.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            string worth = a.Worth > 0.01 ? $"{CoachText.PhaseName(a.Phase)} {a.Worth.ToString("0.00", CultureInfo.CurrentCulture)} {Units.S} · " : "";
            var sub = new TextBlock { Text = $"{worth}whole turn {a.Lost.ToString("0.00", CultureInfo.CurrentCulture)} {Units.S}", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            var body = new StackPanel { Children = { text, sub } };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(body, 1);
            grid.Children.Add(badge);
            grid.Children.Add(body);
            list.Children.Add(PickRow(grid, k == turn, () => c.OpenTurn(k), new Thickness(0, 0, 0, 6)));
        }
        if (worst.Count == 0)
            list.Children.Add(new TextBlock { Text = $"This lap is level with or faster than {c.Phrase} in every turn.", Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(list);
        if (worst.Count > 0)
        {
            double rest = c.Coach.Sum.Total - worst.Sum(a => a.Lost);
            var note = new TextBlock { Text = $"The other {CoachText.Signed(rest)} {Units.S} is spread over the rest of the lap.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            stack.Children.Add(note);
        }
        return Card(stack);
    }

    internal static Border PickRow(FrameworkElement content, bool selected, Action click, Thickness margin)
    {
        var stripe = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
        stripe.SetResourceReference(Border.BackgroundProperty, selected ? "Accent" : "Transparent");
        content.Margin = new Thickness(9, 8, 10, 9);
        var inner = new Grid { Children = { content, stripe } };
        var row = new Tile { CornerRadius = ThemeManager.Round(8), BorderThickness = Hairline.Edges(), Child = inner, ClipChild = true, Margin = margin, Cursor = Cursors.Hand };
        row.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentSoft" : "Line");
        row.SetResourceReference(Border.BackgroundProperty, selected ? "AccentSoft" : "Transparent");
        if (!selected)
        {
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            row.MouseLeave += (_, _) => row.SetResourceReference(Border.BorderBrushProperty, "Line");
        }
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    private static FrameworkElement Measured(CoachSetup c, int k)
    {
        var y = c.Coach.Mine[k];
        var r = c.Coach.Theirs[k];
        bool imp = c.Imperial;
        double zero = r.LowAt;
        var stack = new StackPanel();
        var title = new TextBlock { Text = c.Name(k) + " measured", Style = Ui.Res<Style>("H3") };
        var gear = new TextBlock { Text = $"gear {y.Gear} at the slowest point", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Bottom };
        stack.Children.Add(Ui.SplitRow(title, gear, 8));
        stack.Children.Add(new TextBlock { Text = $"Distances from where {c.Phrase} is slowest", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        void Head()
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var (text, col) in new[] { ("You", 1), (c.Column, 2) })
            {
                var h = new TextBlock { Text = text, Style = Ui.Res<Style>("Eyebrow"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 5) };
                Grid.SetColumn(h, col);
                grid.Children.Add(h);
            }
            row++;
        }
        void Row(string label, string mine, string theirs, (string Text, string Brush) diff)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top }, Orientation.Horizontal);
            line.SetResourceReference(Border.BackgroundProperty, "GridLine");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, 4);
            grid.Children.Add(line);
            var cells = new (string Text, string Brush, bool Bold)[] { (label, "TextMuted", false), (mine, "AccentText", true), (theirs, StatsUi.Hue("Slate").Deep, false), (diff.Text, diff.Brush, true) };
            for (int col = 0; col < 4; col++)
            {
                var t = new TextBlock { Text = cells[col].Text, Style = Ui.Res<Style>("Body"), FontSize = 12, Margin = new Thickness(col == 0 ? 0 : 12, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
                if (col == 0) t.TextWrapping = TextWrapping.Wrap;
                else { t.HorizontalAlignment = HorizontalAlignment.Right; Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); }
                if (cells[col].Bold) t.FontWeight = FontWeights.SemiBold;
                t.SetResourceReference(TextBlock.ForegroundProperty, cells[col].Brush);
                Grid.SetRow(t, row);
                Grid.SetColumn(t, col);
                grid.Children.Add(t);
            }
            row++;
        }
        string missing = SimRacing.Race.RaceFormat.Missing;
        (string, string) None() => (missing, "TextMuted");
        (string, string) Where(double? a, double? b, string later, string earlier, bool laterIsBad)
        {
            if (a is not { } x || b is not { } z) return None();
            if (Math.Abs(x - z) < 3) return ("same", "TextMuted");
            bool later2 = x > z;
            return ($"{CoachText.Metres(Math.Abs(x - z), imp)} {(later2 ? later : earlier)}", CoachText.LossBrush(later2 == laterIsBad ? 1 : -1));
        }
        (string, string) Diff(double a, double b, Func<double, string> show, double least, bool higherIsBad)
        {
            double x = a - b;
            if (Math.Abs(x) < least) return ("same", "TextMuted");
            return ((x > 0 ? "+" : "−") + show(Math.Abs(x)), CoachText.LossBrush((x > 0) == higherIsBad ? 1 : -1));
        }
        string Pct(double v) => (v * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
        string Sp(double v) => CoachText.Speed(v, imp);
        Head();
        bool braked = y.Brake is not null && r.Brake is not null;
        Row("Brake starts", CoachText.Position(y.Brake, zero, imp), CoachText.Position(r.Brake, zero, imp), Where(y.Brake, r.Brake, "later", "earlier", false));
        Row("Brake let go", CoachText.Position(y.Release, zero, imp), CoachText.Position(r.Release, zero, imp), Where(y.Release, r.Release, "later", "earlier", false));
        Row("Most brake", y.Brake is null ? missing : Pct(y.Peak), r.Brake is null ? missing : Pct(r.Peak), braked ? Diff(y.Peak * 100, r.Peak * 100, v => v.ToString("0", CultureInfo.CurrentCulture) + "%", 0.5, false) : None());
        Row("Brake on for", y.BrakeSeconds is { } yb ? CoachText.Seconds(yb) : missing, r.BrakeSeconds is { } rb ? CoachText.Seconds(rb) : missing,
            y.BrakeSeconds is { } a1 && r.BrakeSeconds is { } b1 ? Diff(a1, b1, CoachText.Seconds, 0.005, true) : None());
        Row("Slowest point", Sp(y.LowSpeed), Sp(r.LowSpeed), Diff(y.LowSpeed, r.LowSpeed, Sp, 0.5 / 3.6, false));
        Row("Throttle back on", CoachText.Position(y.ThrottleOn, zero, imp), CoachText.Position(r.ThrottleOn, zero, imp), Where(y.ThrottleOn, r.ThrottleOn, "later", "sooner", true));
        Row("Full throttle", CoachText.Position(y.FullThrottle, zero, imp), CoachText.Position(r.FullThrottle, zero, imp), Where(y.FullThrottle, r.FullThrottle, "later", "sooner", true));
        Row("Neither pedal", CoachText.Seconds(y.Coast), CoachText.Seconds(r.Coast), Diff(y.Coast, r.Coast, CoachText.Seconds, 0.005, true));
        Row($"Speed {CoachText.Metres(LapCoach.SpeedAfterM, imp)} after", Sp(y.SpeedAfter), Sp(r.SpeedAfter), Diff(y.SpeedAfter, r.SpeedAfter, Sp, 0.5 / 3.6, false));
        Row("Time through the turn", CoachText.Seconds(y.Seconds), CoachText.Seconds(r.Seconds), Diff(y.Seconds, r.Seconds, CoachText.Seconds, 0.005, true));
        stack.Children.Add(grid);

        var phases = c.Coach.Phases[k];
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 12, -4, 0) };
        foreach (var p in Enum.GetValues<CoachPhase>())
        {
            double v = phases.Of(p);
            var tile = new Border { CornerRadius = ThemeManager.Round(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(4, 0, 4, 0) };
            tile.SetResourceReference(Border.BackgroundProperty, "TileRaised");
            var value = RaceParts.LiveFigure($"{CoachText.Signed(v)} {Units.S}", 18, CoachText.LossBrush(v));
            value.HorizontalAlignment = HorizontalAlignment.Left;
            tile.Child = new StackPanel { Children = { value, new TextBlock { Text = CoachText.PhaseName(p), Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap } } };
            tiles.Children.Add(tile);
        }
        stack.Children.Add(tiles);
        return Card(stack);
    }

    private static FrameworkElement Detail(CoachSetup c, int k)
    {
        var p = c.Coach.Phases[k];
        var stack = new StackPanel();
        var title = TextFit.TipWhenTrimmed(new TextBlock { Text = $"{c.Name(k)} · lap {c.Lap} against {c.Phrase}", Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var replay = Ui.LabeledButton("I.Play", "Replay this turn", () => c.ReplayTurn(k));
        replay.ToolTip = "Opens the Replay tab at this turn and stops at its end";
        var lost = RaceParts.LiveFigure($"{CoachText.Signed(p.Total)} {Units.S}", 20, CoachText.LossBrush(p.Total));
        lost.Margin = new Thickness(12, 0, 0, 0);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { replay, lost } };
        stack.Children.Add(Ui.SplitRow(title, tools, 10));

        var turns = new WrapPanel { Margin = new Thickness(0, 10, -Ui.ButtonGap, 4) };
        for (int i = 0; i < c.Coach.Turns.Count; i++)
        {
            int index = i;
            double v = c.Coach.Phases[i].Total;
            var name = new TextBlock { Text = c.Name(i), FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            var gap = new TextBlock { Text = CoachText.Signed(v), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            gap.SetResourceReference(TextBlock.ForegroundProperty, CoachText.LossBrush(v));
            Typography.SetNumeralAlignment(gap, FontNumeralAlignment.Tabular);
            var button = new Button { Content = new StackPanel { Children = { name, gap } }, Height = 40, MinWidth = 44, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, Ui.ButtonGap, Ui.ButtonGap), HorizontalContentAlignment = HorizontalAlignment.Center };
            Ui.SetToggled(button, i == k);
            button.Click += (_, _) => c.OpenTurn(index);
            turns.Children.Add(button);
        }
        stack.Children.Add(turns);

        var window = c.Window(k);
        var line = c.Reference[TraceChannel.X] is { } x && c.Reference[TraceChannel.Y] is { } yy ? (x, yy) : ((double[], double[])?)null;
        var ribbon = new TurnRibbon(c, k, window, line) { Height = RibbonHeight };
        var cars = new TurnCars(ribbon) { IsHitTestVisible = false };
        stack.Children.Add(new Grid { ClipToBounds = true, Children = { ribbon, cars } });

        var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Swatch(string key, string text, double opacity = 1)
        {
            var sw = new Border { Width = 14, Height = 8, CornerRadius = ThemeManager.Round(2), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = opacity };
            sw.SetResourceReference(Border.BackgroundProperty, key);
            legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2), Children = { sw, Ui.Text(text, "Caption") } });
        }
        void Mark(string letter, string text)
        {
            var circle = new System.Windows.Shapes.Ellipse { StrokeThickness = 1.5 };
            circle.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextMuted");
            var l = new TextBlock { Text = letter, FontSize = 9.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            TextCase.Verbatim(l);
            var ring = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Children = { circle, l } };
            legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2), Children = { ring, Ui.Text(text, "Caption") } });
        }
        Swatch("Danger", "Brake, darker = harder");
        Swatch("Warn", "Neither pedal");
        Swatch(StatsUi.Hue("Sage").Deep, "Throttle, brighter = more");
        Mark("B", "brake starts");
        Mark("R", "brake released");
        Mark("A", "slowest point");
        Mark("T", "throttle back on");
        Mark("F", "full throttle");
        stack.Children.Add(legend);

        var head = new TextBlock { Text = "Pedals and speed through the turn", Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center };
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, text, dashed) in new[] { ("AccentText", "You", false), (StatsUi.Hue("Slate").Deep, c.Column, true) })
        {
            var sw = new Border { Width = 14, Height = 2, Margin = new Thickness(14, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = dashed ? 0.8 : 1 };
            sw.SetResourceReference(Border.BackgroundProperty, key);
            who.Children.Add(sw);
            who.Children.Add(Ui.Text(text, "Caption"));
        }
        var headRow = Ui.SplitRow(head, who, 8);
        headRow.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(headRow);
        var gapText = new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), MinHeight = 18 };
        stack.Children.Add(gapText);
        var traces = new TurnTraces(c, k, window) { Height = TracesHeight };
        var cursor = new TurnTracesCursor(traces) { IsHitTestVisible = false };
        var tracesHost = new Grid { Margin = new Thickness(0, 6, 0, 0), Children = { traces, cursor } };
        stack.Children.Add(tracesHost);

        var card = Card(stack);
        _ = new TurnPlayer(card, c, window, cars, cursor, gapText);
        return card;
    }

    private static FrameworkElement AddsUp(CoachSetup c)
    {
        var sum = c.Coach.Sum;
        var grid = new Grid();
        void Part(int col, double v, string label, string sub, bool total = false)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var figure = RaceParts.LiveFigure(CoachText.Signed(v) + (total ? " " + Units.S : ""), 52, CoachText.LossBrush(v));
            figure.HorizontalAlignment = HorizontalAlignment.Left;
            var name = new TextBlock { Text = label, Style = Ui.Res<Style>("Body"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            var note = new TextBlock { Text = sub, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            var part = new StackPanel { Children = { figure, name, note }, VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumn(part, col);
            grid.Children.Add(part);
        }
        void Op(int col, string op)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            var t = new TextBlock { Text = op, FontSize = 40, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 36) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        Part(0, sum.Braking, "Braking", $"from where {c.Phrase} brakes to where it lets go");
        Op(1, "+");
        Part(2, sum.Middle, "Middle of the turns", "from there to its full throttle");
        Op(3, "+");
        Part(4, sum.Straights, "Straights", "everything else");
        Op(5, "=");
        Part(6, sum.Total, $"Lap {c.Lap} against {c.Phrase}", "every turn and straight added up", total: true);
        var stack = new StackPanel { Children = { new TextBlock { Text = "Where the time goes", Style = Ui.Res<Style>("H3"), Margin = new Thickness(0, 0, 0, 8) }, grid } };
        return Card(stack, new Thickness(22, 15, 22, 16));
    }

    public static FrameworkElement Corners(CoachSetup c)
    {
        var coach = c.Coach;
        bool imp = c.Imperial;
        var pool = c.Pool;
        var fastest = coach.Turns.Select(t => pool.Min(l => LapCoach.Seconds(l.Trace, t))).ToList();
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        string[] heads = { "Turn", "Lost", "Time through", "Brake starts", "Most brake", "Brake on for", "Slowest", "Gear", "Throttle on", "Full throttle", "Neither pedal", $"Speed {CoachText.Metres(LapCoach.SpeedAfterM, imp)} after" };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 64 });
        for (int i = 1; i < heads.Length; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < heads.Length; i++)
        {
            var h = new TextBlock { Text = heads[i], Style = Ui.Res<Style>("Eyebrow"), TextWrapping = TextWrapping.Wrap, TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(i == 0 ? 10 : 4, 0, 6, 7) };
            Grid.SetColumn(h, i);
            grid.Children.Add(h);
        }
        string missing = SimRacing.Race.RaceFormat.Missing;
        for (int k = 0; k < coach.Turns.Count; k++)
        {
            int index = k, row = k + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var y = coach.Mine[k];
            var r = coach.Theirs[k];
            double z = r.LowAt;
            var back = Hairline.Frame(new Border { Cursor = Cursors.Hand }, Sides.Top);
            back.SetResourceReference(Border.BorderBrushProperty, "GridLine");
            bool selected = k == c.Memory.Turn;
            back.SetResourceReference(Border.BackgroundProperty, selected ? "AccentSoft" : "Transparent");
            if (!selected)
            {
                back.MouseEnter += (_, _) => back.SetResourceReference(Border.BackgroundProperty, "Hover");
                back.MouseLeave += (_, _) => back.SetResourceReference(Border.BackgroundProperty, "Transparent");
            }
            back.MouseLeftButtonUp += (_, _) => c.OpenTurn(index);
            Grid.SetRow(back, row);
            Grid.SetColumnSpan(back, heads.Length);
            grid.Children.Add(back);
            var stripe = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
            stripe.SetResourceReference(Border.BackgroundProperty, selected ? "Accent" : "Transparent");
            Grid.SetRow(stripe, row);
            grid.Children.Add(stripe);
            void Cell(int col, string value, (string Text, string Brush)? diff = null, string brush = "Text", bool bold = false)
            {
                var v = new TextBlock { Text = value, Style = Ui.Res<Style>("Body"), FontSize = 12.5, HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                v.SetResourceReference(TextBlock.ForegroundProperty, brush);
                if (bold) v.FontWeight = FontWeights.SemiBold;
                Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
                var cell = new StackPanel { Margin = new Thickness(col == 0 ? 10 : 4, 5, 6, 5), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Children = { v } };
                if (diff is { } d)
                {
                    var s = new TextBlock { Text = d.Text, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Right };
                    s.SetResourceReference(TextBlock.ForegroundProperty, d.Brush);
                    Typography.SetNumeralAlignment(s, FontNumeralAlignment.Tabular);
                    cell.Children.Add(s);
                }
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);
                grid.Children.Add(cell);
            }
            (string, string) D(double? a, double? b, Func<double, string> show, double least, bool higherIsBad)
            {
                if (a is not { } x || b is not { } w) return ("", "TextMuted");
                double d = x - w;
                if (Math.Abs(d) < least) return ("same", "TextMuted");
                return ((d > 0 ? "+" : "−") + show(Math.Abs(d)), CoachText.LossBrush((d > 0) == higherIsBad ? 1 : -1));
            }
            string M(double v) => CoachText.Metres(v, imp);
            string Sp(double v) => CoachText.SpeedValue(v, imp).ToString("0", CultureInfo.CurrentCulture);
            double total = coach.Phases[k].Total;
            Cell(0, c.Name(k), brush: "Heading", bold: true);
            Cell(1, CoachText.Signed(total), brush: CoachText.LossBrush(total), bold: true);
            double behind = y.Seconds - fastest[k];
            Cell(2, CoachText.Seconds(y.Seconds), behind < 0.0005 ? ("fastest", "TimingBest") : ("+" + CoachText.Seconds(behind), StatsUi.Hue("Coral").Deep));
            double? Before(TurnMeasure m) => m.Brake is { } b ? z - b : null;
            double? After(double? v) => v is { } d ? d - z : null;
            Cell(3, Before(y) is { } yb ? M(yb) : missing, D(Before(y), Before(r), M, 1, true));
            Cell(4, y.Brake is null ? missing : (y.Peak * 100).ToString("0", CultureInfo.CurrentCulture) + "%", y.Brake is not null && r.Brake is not null ? D(y.Peak * 100, r.Peak * 100, v => v.ToString("0", CultureInfo.CurrentCulture) + "%", 0.5, false) : null);
            Cell(5, y.BrakeSeconds is { } bs ? CoachText.Seconds(bs) : missing, D(y.BrakeSeconds, r.BrakeSeconds, CoachText.Seconds, 0.005, true));
            Cell(6, CoachText.Speed(y.LowSpeed, imp), D(y.LowSpeed, r.LowSpeed, Sp, 0.5 / 3.6, false));
            Cell(7, y.Gear.ToString(CultureInfo.CurrentCulture));
            Cell(8, After(y.ThrottleOn) is { } on ? CoachText.Position(y.ThrottleOn, z, imp) : missing, D(After(y.ThrottleOn), After(r.ThrottleOn), M, 1, true));
            Cell(9, After(y.FullThrottle) is { } full ? CoachText.Position(y.FullThrottle, z, imp) : missing, D(After(y.FullThrottle), After(r.FullThrottle), M, 1, true));
            Cell(10, CoachText.Seconds(y.Coast), D(y.Coast, r.Coast, CoachText.Seconds, 0.005, true));
            Cell(11, CoachText.Speed(y.SpeedAfter, imp), D(y.SpeedAfter, r.SpeedAfter, Sp, 0.5 / 3.6, false));
        }
        var title = new TextBlock { Text = $"Every turn, lap {c.Lap} against {c.Phrase}", Style = Ui.Res<Style>("H3") };
        var caption = new TextBlock { Text = $"Small figures: difference to {c.Phrase}; distances are from where it's slowest. Click a turn to coach it.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        return Card(new StackPanel { Children = { title, caption, grid } });
    }

    public static FrameworkElement Consistency(CoachSetup c)
    {
        var coach = c.Coach;
        var pool = c.Pool.OrderBy(l => l.Time).ToList();
        int n = coach.Turns.Count;
        var times = pool.Select(l => coach.Turns.Select(t => LapCoach.Seconds(l.Trace, t)).ToArray()).ToList();
        var fastest = Enumerable.Range(0, n).Select(k => times.Min(t => t[k])).ToArray();
        var spread = Enumerable.Range(0, n).Select(k => times.Max(t => t[k]) - fastest[k]).ToArray();
        var brakes = Enumerable.Range(0, n).Select(k =>
        {
            var b = pool.Select(l => LapCoach.Measure(l.Trace, coach.Turns[k]).Brake).OfType<double>().ToList();
            return b.Count >= 2 ? b.Max() - b.Min() : (double?)null;
        }).ToArray();
        int worst = Array.IndexOf(spread, spread.Max());

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int k = 0; k < n; k++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        void NewRow() { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        TextBlock Add(int col, string text, string brush, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Center, string? back = null, double alpha = 0)
        {
            var t = new TextBlock { Text = text, FontSize = 11.5, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            var cell = new Border { Height = 23, Margin = new Thickness(1), CornerRadius = ThemeManager.Round(4), Padding = new Thickness(6, 0, 6, 0), Child = t };
            if (back is not null) cell.Background = new SolidColorBrush(LapAnalysisView.Brush(back).Color) { Opacity = alpha };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
            return t;
        }
        NewRow();
        Add(0, "Lap", "TextFaint", align: HorizontalAlignment.Left);
        for (int k = 0; k < n; k++) Add(k + 1, c.Name(k), "TextFaint", true);
        Add(n + 1, "Lap time", "TextFaint", align: HorizontalAlignment.Right);
        row++;
        for (int i = 0; i < pool.Count; i++)
        {
            NewRow();
            bool me = pool[i].Lap == c.Lap;
            Add(0, $"Lap {pool[i].Lap}", me ? "AccentText" : "TextMuted", me, HorizontalAlignment.Left);
            for (int k = 0; k < n; k++)
            {
                double x = times[i][k] - fastest[k];
                if (x < 0.0005) Add(k + 1, "0.00", "TimingBest", true, back: "TimingBest", alpha: 0.16);
                else Add(k + 1, x.ToString("0.00", CultureInfo.CurrentCulture), "Text", back: "Danger", alpha: 0.06 + 0.5 * Math.Min(1, x / 0.45));
            }
            Add(n + 1, StatFormat.LapTime(pool[i].Time), "Heading", true, HorizontalAlignment.Right);
            row++;
        }
        NewRow();
        var rule = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top }, Orientation.Horizontal);
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        Grid.SetRow(rule, row);
        Grid.SetColumnSpan(rule, n + 2);
        grid.Children.Add(rule);
        Add(0, "Fastest to slowest", "TextMuted", align: HorizontalAlignment.Left);
        for (int k = 0; k < n; k++) Add(k + 1, spread[k].ToString("0.00", CultureInfo.CurrentCulture), k == worst ? "DangerDeep" : "TextMuted", k == worst);
        row++;
        NewRow();
        Add(0, "Brake point moves", "TextMuted", align: HorizontalAlignment.Left);
        for (int k = 0; k < n; k++) Add(k + 1, brakes[k] is { } b ? CoachText.Metres(b, c.Imperial) : SimRacing.Race.RaceFormat.Missing, "TextMuted");
        row++;

        var title = new TextBlock { Text = "Turn by turn, every lap", Style = Ui.Res<Style>("H3") };
        var caption = new TextBlock { Text = "Seconds behind the fastest time through each turn across your clean laps; purple = that fastest time", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var note = CoachText.Rich($"Least steady: **{c.Name(worst)}**, {CoachText.Seconds(spread[worst])} between your fastest and slowest time through it.", "Caption");
        note.Margin = new Thickness(0, 10, 0, 0);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
        return Card(new StackPanel { Children = { title, caption, scroll, note } });
    }
}

internal sealed class TurnRibbon : FrameworkElement
{
    private const double HalfWidth = 13, InsetWidth = 120, InsetHeight = 96;
    private readonly CoachSetup _c;
    private readonly int _k;
    private readonly (double From, double To) _w;
    private readonly (double[] X, double[] Y)? _line;
    private double _ox, _oy, _scale, _x0, _y1;

    public TurnRibbon(CoachSetup c, int k, (double From, double To) window, (double[] X, double[] Y)? line)
    {
        _c = c;
        _k = k;
        _w = window;
        _line = line;
    }

    public bool HasLine => _line is not null && _scale > 0;

    public (double From, double To) Window => _w;

    private (double X, double Y) World(double d)
    {
        var (x, y) = _line!.Value;
        double step = _c.Reference.StepM;
        return (LapReplay.At(x, step, d), LapReplay.At(y, step, d));
    }

    public Point At(double d)
    {
        var (x, y) = World(d);
        return new Point(_ox + (x - _x0) * _scale, _oy + (_y1 - y) * _scale);
    }

    public Vector Normal(double d)
    {
        var a = At(d - 2);
        var b = At(d + 2);
        var v = b - a;
        if (v.Length < 1e-6) return new Vector(0, -1);
        v.Normalize();
        return new Vector(-v.Y, v.X);
    }

    public Point Lane(double d, int side) => At(d) + Normal(d) * (HalfWidth / 2 * side);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_line is null || w < 50)
        {
            var none = LapAnalysisView.Text(this, "This lap has no position data, so the turn can't be drawn.", LapAnalysisView.Brush("TextMuted"), 12);
            dc.DrawText(none, new Point((w - none.Width) / 2, (h - none.Height) / 2));
            return;
        }
        double step = _c.Reference.StepM;
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
        for (double d = _w.From; d <= _w.To; d += step)
        {
            var (x, y) = World(d);
            x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
        }
        double padL = 76, padR = InsetWidth + 76, padV = 62;
        _scale = Math.Min((w - padL - padR) / Math.Max(1, x1 - x0), (h - 2 * padV) / Math.Max(1, y1 - y0));
        _x0 = x0;
        _y1 = y1;
        _ox = padL + (w - padL - padR - (x1 - x0) * _scale) / 2;
        _oy = padV + (h - 2 * padV - (y1 - y0) * _scale) / 2;

        var road = new Pen(LapAnalysisView.Brush("MeterTrack"), HalfWidth * 2 + 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        road.Freeze();
        dc.DrawGeometry(null, road, Path(Math.Max(0, _w.From - 30), Math.Min(_c.Coach.Length - step, _w.To + 30), 2));

        Color brake = LapAnalysisView.Brush("Danger").Color, gas = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color, coast = LapAnalysisView.Brush("Warn").Color;
        const int buckets = 10;
        var geos = new StreamGeometry[2 * buckets + 1];
        var ctx = new StreamGeometryContext?[2 * buckets + 1];
        double seg = Math.Max(step, 2);
        foreach (var (lap, side) in new[] { (_c.Mine, 1), (_c.Reference, -1) })
            for (double d = _w.From; d < _w.To; d += seg)
            {
                double b = lap.At(TraceChannel.Brake, d) ?? 0, t = lap.At(TraceChannel.Throttle, d) ?? 0;
                int bucket = b >= LapCoach.PedalOn ? (int)Math.Round(Math.Clamp(b, 0, 1) * (buckets - 1))
                    : t >= LapCoach.PedalOn ? buckets + (int)Math.Round(Math.Clamp(t, 0, 1) * (buckets - 1)) : 2 * buckets;
                if (ctx[bucket] is null) { geos[bucket] = new StreamGeometry(); ctx[bucket] = geos[bucket].Open(); }
                var a = At(d);
                var e = At(Math.Min(_w.To, d + seg));
                var na = Normal(d) * (HalfWidth * side);
                var ne = Normal(Math.Min(_w.To, d + seg)) * (HalfWidth * side);
                ctx[bucket]!.BeginFigure(a, true, true);
                ctx[bucket]!.PolyLineTo(new[] { e, e + ne, a + na }, true, true);
            }
        for (int i = 0; i < geos.Length; i++)
        {
            if (ctx[i] is not { } c) continue;
            c.Close();
            geos[i].Freeze();
            double u = (i % buckets) / (double)(buckets - 1);
            Color color = i == 2 * buckets ? coast : i < buckets ? brake : gas;
            var fill = new SolidColorBrush(color) { Opacity = i == 2 * buckets ? 1 : i < buckets ? 0.3 + 0.7 * u : 0.22 + 0.7 * u };
            fill.Freeze();
            var edge = new Pen(fill, 0.6);
            edge.Freeze();
            dc.DrawGeometry(fill, edge, geos[i]);
        }
        var split = new Pen(LapAnalysisView.Brush("Bg"), 1.5);
        split.Freeze();
        dc.DrawGeometry(null, split, Path(_w.From, _w.To, step));

        Marks(dc, _c.Coach.Mine[_k], 1, LapAnalysisView.Brush("AccentText"));
        Marks(dc, _c.Coach.Theirs[_k], -1, LapAnalysisView.Brush(StatsUi.Hue("Slate").Deep));
        Label(dc, "YOU", 1, LapAnalysisView.Brush("AccentText"));
        Label(dc, TextCase.Capitals(_c.Column), -1, LapAnalysisView.Brush(StatsUi.Hue("Slate").Deep));
        Inset(dc, w);
    }

    private StreamGeometry Path(double a, double b, double step)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(a), false, false);
            var points = new List<Point>();
            for (double d = a + step; d <= b; d += step) points.Add(At(d));
            ctx.PolyLineTo(points, true, true);
        }
        g.Freeze();
        return g;
    }

    private void Marks(DrawingContext dc, TurnMeasure m, int side, SolidColorBrush color)
    {
        var list = new (string Letter, double? At)[] { ("B", m.Brake), ("R", m.Release), ("A", m.LowAt), ("T", m.ThrottleOn), ("F", m.FullThrottle) };
        var placed = new List<Point>();
        var ring = new Pen(color, 1.6);
        ring.Freeze();
        var lead = Hairline.Pen(color, this);
        var panel = LapAnalysisView.Brush("Panel");
        foreach (var (letter, at) in list)
        {
            if (at is not { } d || d < _w.From || d > _w.To) continue;
            var p = At(d);
            var n = Normal(d) * side;
            var along = new Vector(n.Y, -n.X);
            Point Inside(Point x) => new(Math.Clamp(x.X, 10, ActualWidth - 10), Math.Clamp(x.Y, 10, ActualHeight - 10));
            bool Free(Point x) => placed.All(o => (o - x).Length >= 20) && (x - At(d)).Length >= HalfWidth + 9;
            var q = Inside(p + n * (HalfWidth + 13));
            bool found = Free(q);
            for (int step = 1; step <= 6 && !found; step++)
                foreach (var tryAt in new[] { p + n * (HalfWidth + 13 + 18 * step), p + n * (HalfWidth + 13 + 9 * step) + along * (16 * step), p + n * (HalfWidth + 13 + 9 * step) - along * (16 * step) })
                {
                    var c = Inside(tryAt);
                    if (!Free(c)) continue;
                    q = c;
                    found = true;
                    break;
                }
            placed.Add(q);
            dc.DrawLine(lead, p + n * HalfWidth, q - n * 9);
            dc.DrawEllipse(panel, ring, q, 9, 9);
            var text = LapAnalysisView.Text(this, letter, LapAnalysisView.Brush("Heading"), 10, bold: true);
            dc.DrawText(text, new Point(q.X - text.Width / 2, q.Y - text.Height / 2));
        }
    }

    private void Label(DrawingContext dc, string text, int side, SolidColorBrush brush)
    {
        double d = _w.From + 6;
        var q = At(d) + Normal(d) * ((HalfWidth + 30) * side);
        var t = LapAnalysisView.Text(this, text, brush, 11, bold: true);
        dc.DrawText(t, new Point(Math.Clamp(q.X - t.Width / 2, 2, ActualWidth - t.Width - 2), Math.Clamp(q.Y - t.Height / 2, 2, ActualHeight - t.Height - 2)));
    }

    private void Inset(DrawingContext dc, double w)
    {
        var (xs, ys) = _line!.Value;
        double ix = w - InsetWidth - 6, iy = 6;
        double a0 = xs.Min(), a1 = xs.Max(), b0 = ys.Min(), b1 = ys.Max();
        double s = Math.Min((InsetWidth - 16) / Math.Max(1, a1 - a0), (InsetHeight - 16) / Math.Max(1, b1 - b0));
        double ox = ix + (InsetWidth - (a1 - a0) * s) / 2, oy = iy + (InsetHeight - (b1 - b0) * s) / 2;
        double step = _c.Reference.StepM;
        Point P(double d) => new(ox + (LapReplay.At(xs, step, d) - a0) * s, oy + (b1 - LapReplay.At(ys, step, d)) * s);
        var edge = Hairline.Pen(LapAnalysisView.Brush("Line"), this);
        dc.DrawRoundedRectangle(LapAnalysisView.Brush("Bg"), edge, new Rect(ix, iy, InsetWidth, InsetHeight), 8, 8);
        StreamGeometry Line(double a, double b, double by)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(P(a), false, false);
                var pts = new List<Point>();
                for (double d = a + by; d <= b; d += by) pts.Add(P(d));
                ctx.PolyLineTo(pts, true, true);
            }
            g.Freeze();
            return g;
        }
        var track = new Pen(LapAnalysisView.Brush("LineStrong"), 2) { LineJoin = PenLineJoin.Round };
        track.Freeze();
        dc.DrawGeometry(null, track, Line(0, _c.Reference.LengthM - step, step * 3));
        var here = new Pen(LapAnalysisView.Brush("Accent"), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        here.Freeze();
        dc.DrawGeometry(null, here, Line(_w.From, _w.To, step));
    }
}

internal sealed class TurnCars : FrameworkElement
{
    private readonly TurnRibbon _ribbon;
    private double? _mine, _theirs;

    public TurnCars(TurnRibbon ribbon) => _ribbon = ribbon;

    public void Move(double mine, double theirs)
    {
        _mine = mine;
        _theirs = theirs;
        InvalidateVisual();
    }

    private Pen? _edge;

    protected override void OnRender(DrawingContext dc)
    {
        if (!_ribbon.HasLine || _mine is not { } m || _theirs is not { } t) return;
        if (_edge is null) { _edge = new Pen(LapAnalysisView.Brush("Heading"), 1.5); _edge.Freeze(); }
        dc.DrawEllipse(LapAnalysisView.Brush(StatsUi.Hue("Slate").Deep), _edge, _ribbon.Lane(t, -1), 5.5, 5.5);
        dc.DrawEllipse(LapAnalysisView.Brush("Accent"), _edge, _ribbon.Lane(m, 1), 5.5, 5.5);
    }
}

internal sealed class TurnTraces : FrameworkElement
{
    public const double Left = 70, Right = 10;
    private readonly CoachSetup _c;
    private readonly int _k;
    private readonly (double From, double To) _w;

    public TurnTraces(CoachSetup c, int k, (double From, double To) window)
    {
        _c = c;
        _k = k;
        _w = window;
    }

    public double X(double d) => Left + (d - _w.From) / Math.Max(1, _w.To - _w.From) * Math.Max(1, ActualWidth - Left - Right);

    public const double PlotBottom = 204;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < Left + 40) return;
        bool imp = _c.Imperial;
        var lanes = new (TraceChannel Channel, string Label, double Top, double Height)[]
        {
            (TraceChannel.Speed, "Speed", 4, 92), (TraceChannel.Brake, "Brake", 108, 44), (TraceChannel.Throttle, "Throttle", 160, 44),
        };
        double step = Math.Max(_c.Mine.StepM, 1);
        double vmin = double.MaxValue, vmax = double.MinValue;
        foreach (var lap in new[] { _c.Mine, _c.Reference })
            for (double d = _w.From; d <= _w.To; d += step)
            {
                double v = CoachText.SpeedValue(lap.At(TraceChannel.Speed, d) ?? 0, imp);
                vmin = Math.Min(vmin, v);
                vmax = Math.Max(vmax, v);
            }
        vmin = Math.Floor(vmin / 10) * 10;
        vmax = Math.Max(vmin + 10, Math.Ceiling(vmax / 10) * 10);
        double px = Hairline.ScaleOf(this);
        var grid = Hairline.Pen(LapAnalysisView.Brush("GridLine"), px);
        var muted = LapAnalysisView.Brush("TextMuted");
        var faint = LapAnalysisView.Brush("TextFaint");
        var you = LapAnalysisView.Brush("AccentText");
        var them = LapAnalysisView.Brush(StatsUi.Hue("Slate").Deep);
        foreach (var lane in lanes)
        {
            dc.DrawText(LapAnalysisView.Text(this, lane.Label, muted, 11, bold: true), new Point(0, lane.Top));
            dc.DrawLine(grid, new Point(Left, Hairline.Snap(lane.Top, px)), new Point(w - Right, Hairline.Snap(lane.Top, px)));
            dc.DrawLine(grid, new Point(Left, Hairline.Snap(lane.Top + lane.Height, px)), new Point(w - Right, Hairline.Snap(lane.Top + lane.Height, px)));
            Func<double, double> y = lane.Channel == TraceChannel.Speed
                ? v => lane.Top + lane.Height - (CoachText.SpeedValue(v, imp) - vmin) / (vmax - vmin) * lane.Height
                : v => lane.Top + lane.Height - Math.Clamp(v, 0, 1) * lane.Height;
            if (lane.Channel == TraceChannel.Speed)
            {
                dc.DrawText(LapAnalysisView.Text(this, CoachText.SpeedUnit(imp), faint, 10), new Point(0, lane.Top + 16));
                var top = LapAnalysisView.Text(this, vmax.ToString("0", CultureInfo.CurrentCulture), faint, 10);
                var bottom = LapAnalysisView.Text(this, vmin.ToString("0", CultureInfo.CurrentCulture), faint, 10);
                dc.DrawText(top, new Point(Left - 6 - top.Width, lane.Top));
                dc.DrawText(bottom, new Point(Left - 6 - bottom.Width, lane.Top + lane.Height - bottom.Height));
            }
            else
            {
                var fill = new SolidColorBrush(LapAnalysisView.Brush(lane.Channel == TraceChannel.Brake ? "Danger" : StatsUi.Hue("Sage").Deep).Color) { Opacity = 0.22 };
                fill.Freeze();
                dc.DrawGeometry(fill, null, Curve(_c.Mine, lane.Channel, y, step, true, lane.Top + lane.Height));
            }
            var theirPen = new Pen(them, 1.6) { DashStyle = new DashStyle(new double[] { 2.5, 2 }, 0) };
            theirPen.Freeze();
            dc.DrawGeometry(null, theirPen, Curve(_c.Reference, lane.Channel, y, step, false, 0));
            var minePen = new Pen(lane.Channel == TraceChannel.Speed ? you : LapAnalysisView.Brush(lane.Channel == TraceChannel.Brake ? "Danger" : StatsUi.Hue("Sage").Deep), 1.6);
            minePen.Freeze();
            dc.DrawGeometry(null, minePen, Curve(_c.Mine, lane.Channel, y, step, false, 0));
        }
        double apex = _c.Coach.Theirs[_k].LowAt;
        var dashed = Hairline.Pen(LapAnalysisView.Brush("LineStrong"), px).Clone();
        dashed.DashStyle = new DashStyle(new double[] { 2, 3 }, 0);
        dashed.Freeze();
        double ax = Hairline.Snap(X(apex), px);
        dc.DrawLine(dashed, new Point(ax, 4), new Point(ax, PlotBottom));
        double tick = imp ? 150 / 3.28084 : 50;
        double lastRight = double.MinValue;
        for (double m = Math.Ceiling((_w.From - apex) / tick) * tick; m <= _w.To - apex; m += tick)
        {
            string label = Math.Abs(m) < 1e-6 ? "slowest point" : CoachText.Position(apex + m, apex, imp);
            var t = LapAnalysisView.Text(this, label, faint, 10);
            double x = X(apex + m) - t.Width / 2;
            if (x < Left - 10 || x + t.Width > w || x < lastRight + 6) continue;
            dc.DrawText(t, new Point(x, PlotBottom + 8));
            lastRight = x + t.Width;
        }
    }

    private StreamGeometry Curve(LapTrace lap, TraceChannel channel, Func<double, double> y, double step, bool closed, double baseY)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var first = new Point(X(_w.From), y(lap.At(channel, _w.From) ?? 0));
            ctx.BeginFigure(closed ? new Point(X(_w.From), baseY) : first, closed, closed);
            var pts = new List<Point>();
            if (closed) pts.Add(first);
            for (double d = _w.From + step; d <= _w.To; d += step) pts.Add(new Point(X(d), y(lap.At(channel, d) ?? 0)));
            if (closed) pts.Add(new Point(X(_w.To), baseY));
            ctx.PolyLineTo(pts, true, true);
        }
        g.Freeze();
        return g;
    }
}

internal sealed class TurnTracesCursor : FrameworkElement
{
    private readonly TurnTraces _traces;
    private double? _mine, _theirs;

    public TurnTracesCursor(TurnTraces traces) => _traces = traces;

    public void Move(double mine, double theirs)
    {
        _mine = mine;
        _theirs = theirs;
        InvalidateVisual();
    }

    private Pen? _mineLine, _theirLine;

    protected override void OnRender(DrawingContext dc)
    {
        if (_mine is not { } m || _theirs is not { } t || ActualWidth < TurnTraces.Left + 40) return;
        if (_mineLine is null || _theirLine is null)
        {
            _mineLine = Hairline.Pen(LapAnalysisView.Brush("AccentText"), this);
            _theirLine = Hairline.Pen(LapAnalysisView.Brush(StatsUi.Hue("Slate").Deep), this);
        }
        dc.PushOpacity(0.7);
        double scale = Hairline.ScaleOf(this), xt = Hairline.Snap(_traces.X(t), scale), xm = Hairline.Snap(_traces.X(m), scale);
        dc.DrawLine(_theirLine, new Point(xt, 4), new Point(xt, TurnTraces.PlotBottom));
        dc.DrawLine(_mineLine, new Point(xm, 4), new Point(xm, TurnTraces.PlotBottom));
        dc.Pop();
    }
}

internal sealed class TurnPlayer
{
    private const double Pause = 1.2;
    private readonly FrameworkElement _host;
    private readonly CoachSetup _c;
    private readonly (double From, double To) _w;
    private readonly TurnCars _cars;
    private readonly TurnTracesCursor _cursor;
    private readonly TextBlock _gap;
    private readonly double _mineStart, _theirStart, _duration;
    private TimeSpan? _start;
    private bool _hooked;
    private long _lastText;

    public TurnPlayer(FrameworkElement host, CoachSetup c, (double From, double To) window, TurnCars cars, TurnTracesCursor cursor, TextBlock gap)
    {
        _host = host;
        _c = c;
        _w = window;
        _cars = cars;
        _cursor = cursor;
        _gap = gap;
        _mineStart = c.Mine.TimeAt(window.From);
        _theirStart = c.Reference.TimeAt(window.From);
        _duration = Math.Max(c.Mine.TimeAt(window.To) - _mineStart, c.Reference.TimeAt(window.To) - _theirStart);
        host.Loaded += (_, _) => Sync();
        host.Unloaded += (_, _) => Stop();
        host.IsVisibleChanged += (_, _) => Sync();
    }

    private void Sync()
    {
        if (_host.IsLoaded && _host.IsVisible) Start();
        else Stop();
    }

    private void Start()
    {
        if (_hooked) return;
        _hooked = true;
        _start = null;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs r) return;
        _start ??= r.RenderingTime;
        double elapsed = (r.RenderingTime - _start.Value).TotalSeconds % (_duration + Pause);
        double t = Math.Min(elapsed, _duration);
        double mine = Math.Min(_w.To, _c.Mine.DistanceAtTime(_mineStart + t));
        double theirs = Math.Min(_w.To, _c.Reference.DistanceAtTime(_theirStart + t));
        _cars.Move(mine, theirs);
        _cursor.Move(mine, theirs);
        long now = Environment.TickCount64;
        if (now - _lastText < 120) return;
        _lastText = now;
        double metres = theirs - mine;
        double seconds = (_c.Mine.TimeAt(mine) - _mineStart) - (_c.Reference.TimeAt(mine) - _theirStart);
        string where = Math.Abs(metres) < 1 ? "level" : $"{CoachText.Metres(Math.Abs(metres), _c.Imperial)} {(metres > 0 ? "behind" : "ahead")}";
        TextCase.Set(_gap, $"Replay from the same spot: you're {where}, {CoachText.Signed(seconds)} {Units.S}");
    }
}
