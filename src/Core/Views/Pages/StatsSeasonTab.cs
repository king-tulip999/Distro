using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal static class StatsSeasonTab
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;
    private const int MaxBests = 8;

    public static FrameworkElement Build(StatsTabData data, IStatsHost host)
    {
        var r = data.Charts.Races ?? Racecraft.Empty;
        var bestsHost = new ContentControl { Focusable = false, Content = BestsCard(data, null) };
        _ = LoadBests();
        async Task LoadBests()
        {
            var store = data.Store;
            var pairs = data.Charts.Bests.Take(MaxBests).ToList();
            var lines = await Task.Run(() => pairs.Select(pb => StatsCharts.SessionBests(store.Db, pb.Car, pb.Track, pb.Config)).ToList());
            bestsHost.Content = BestsCard(data, lines);
        }
        var left = StatsTabs.Stacked(RatingCard(r, data), RacesCard(r, data));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        grid.Children.Add(left);
        Grid.SetColumn(bestsHost, 2);
        grid.Children.Add(bestsHost);
        Ui.Anchor(grid, "stats.season");
        return grid;
    }

    private static FrameworkElement RatingCard(Racecraft r, StatsTabData data)
    {
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var rating = new StackPanel { Margin = new Thickness(0, 0, 34, 0) };
        string category = r.Rated?.Category is { Length: > 0 } cat ? cat + " iRating" : "iRating";
        rating.Children.Add(new TextBlock { Text = $"{category} · {data.RangeName}", Style = Ui.Res<Style>("Eyebrow") });
        var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        figure.Children.Add(RaceParts.BigFigure(r.IrNow?.ToString("N0", C) ?? RaceFormat.Missing, 54, "Heading"));
        if (r.IrChange is { } change)
        {
            var delta = RaceParts.Big(StatFormat.Value(StatKind.Signed, change, false), 21, RaceParts.GainBrush(change));
            delta.VerticalAlignment = VerticalAlignment.Bottom;
            delta.Margin = new Thickness(10, 0, 0, 8);
            figure.Children.Add(delta);
        }
        rating.Children.Add(figure);
        var series = Series(r);
        if (series.Count > 0)
        {
            var high = series.MaxBy(p => p.Rating);
            rating.Children.Add(new TextBlock
            {
                Text = $"Started at {series[0].Before.ToString("N0", C)} · high {high.Rating.ToString("N0", C)} on {Dates.Day(high.When.ToLocalTime())}",
                Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0),
            });
        }
        top.Children.Add(rating);
        var tallies = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Tally(string value, string label, string brush = "Heading")
        {
            var t = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
            t.Children.Add(RaceParts.Big(value, 26, brush));
            t.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
            tallies.Children.Add(t);
        }
        Tally(r.Starts.ToString(C), "Races");
        Tally(r.Wins.ToString(C), "Wins", r.Wins > 0 ? StatsUi.Hue("Gold").Deep : "Heading");
        Tally(r.Podiums.ToString(C), "Podiums", r.Podiums > 0 ? StatsUi.Hue("Gold").Deep : "Heading");
        Tally(r.Finishes > 0 ? Racecraft.Share(r.CleanRaces, r.Finishes) + "%" : RaceFormat.Missing, "Clean races", r.CleanRaces > 0 ? StatsUi.Hue("Sage").Deep : "Heading");
        Grid.SetColumn(tallies, 1);
        top.Children.Add(tallies);
        stack.Children.Add(top);
        if (series.Count >= 2)
            stack.Children.Add(new RatingChart(series) { Height = 190, Margin = new Thickness(0, 16, 0, 0) });
        else stack.Children.Add(new TextBlock { Text = series.Count == 0 ? "No rated races in this period." : "The line starts after your second rated race.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 14, 0, 0) });
        return StatsTabs.Card(stack, new Thickness(22, 20, 22, 18));
    }

    internal readonly record struct RatingPoint(DateTime When, int Before, int Rating, bool Win, string Track);

    internal static List<RatingPoint> Series(Racecraft r)
    {
        if (r.Rated is not { } last) return new();
        return r.Races.Where(x => x.Category == last.Category && x.IrStart > RaceResult.PlaceholderIRating && x.IrDelta is not null)
            .Select(x => new RatingPoint(x.StartUtc, x.IrStart!.Value, x.IrStart!.Value + x.IrDelta!.Value, x.Finished && x.Finish == 1, StatsRacecraft.Place(x))).ToList();
    }

    private static FrameworkElement RacesCard(Racecraft r, StatsTabData data)
    {
        if (r.Starts == 0)
            return StatsTabs.Card(Ui.EmptyState("I.Flag", "No races", $"No races {data.RangeName}. Pick a longer period at the top."));
        var stack = new StackPanel();
        var head = new DockPanel();
        head.Children.Add(StatsTabs.ListTitle("Race by race", r.Races.Count > Racecraft.StripRaces ? $"Last {RaceFormat.Count(Racecraft.StripRaces, "race")}" : RaceFormat.Count(r.Races.Count, "race")));
        stack.Children.Add(head);
        var strip = StatsRacecraft.Strip(r);
        strip.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(strip);
        var columns = StatsRacecraft.Columns(r, data.Get, data.Query);
        columns.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(columns);
        return StatsTabs.Card(stack, new Thickness(22, 18, 22, 20));
    }

    private static FrameworkElement BestsCard(StatsTabData data, List<List<(DateTime When, double Lap)>>? lines)
    {
        var stack = new StackPanel();
        stack.Children.Add(StatsTabs.ListTitle("Personal bests", null));
        stack.Children.Add(new TextBlock { Text = "Your best clean lap per car and track, newest first · the line is your best after each session there", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
        var bests = data.Charts.Bests.Take(MaxBests).ToList();
        if (bests.Count == 0) stack.Children.Add(Ui.EmptyState("I.Stats", "No clean laps yet", "None in this period."));
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        for (int i = 0; i < bests.Count; i++)
        {
            var pb = bests[i];
            var row = new Grid { Margin = new Thickness(0, 9, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var track = TextFit.TipWhenTrimmed(new TextBlock { Text = StatFormat.TrackAndConfig(pb.Track, pb.Config), FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            track.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            names.Children.Add(track);
            names.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = pb.Car, Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis }));
            row.Children.Add(names);
            var line = lines is not null && i < lines.Count ? lines[i] : null;
            if (line is { Count: >= 2 })
            {
                var spark = new Spark(line.Select(p => p.Lap).ToList(), pb.When >= weekAgo) { Height = 30, Margin = new Thickness(10, 0, 8, 0), ToolTip = $"Best after each of {RaceFormat.Count(line.Count, "session")} here" };
                Grid.SetColumn(spark, 1);
                row.Children.Add(spark);
            }
            var figures = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var time = RaceParts.Big(StatFormat.LapTime(pb.Best), 17, pb.When >= weekAgo ? "AccentText" : "Heading");
            time.HorizontalAlignment = HorizontalAlignment.Right;
            figures.Children.Add(time);
            if (line is { Count: >= 2 } && line[0].Lap - pb.Best > 0.0005)
            {
                var gain = new TextBlock { Text = StatFormat.Value(StatKind.LapDelta, pb.Best - line[0].Lap, false), Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Gained since your first session here" };
                gain.SetResourceReference(TextBlock.ForegroundProperty, StatsUi.Hue("Sage").Deep);
                figures.Children.Add(gain);
            }
            else figures.Children.Add(new TextBlock { Text = Dates.Day(pb.When.ToLocalTime()), Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(figures, 2);
            row.Children.Add(figures);
            if (i > 0) stack.Children.Add(StatsTabs.Rule());
            stack.Children.Add(row);
        }
        if (bests.Count > 0)
            stack.Children.Add(new TextBlock { Text = "Pink = set in the last 7 days · green = gained since your first session there", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        return StatsTabs.Card(stack);
    }

    private sealed class Spark : FrameworkElement
    {
        private readonly List<double> _laps;
        private readonly bool _fresh;

        public Spark(List<double> laps, bool fresh)
        {
            _laps = laps;
            _fresh = fresh;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_laps.Count < 2 || w < 10) return;
            double lo = _laps.Min(), hi = _laps.Max(), span = Math.Max(0.001, hi - lo);
            Point P(int i) => new(2 + (w - 6) * i / (_laps.Count - 1), 4 + (h - 8) * (_laps[i] - lo) / span);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(0), false, false);
                for (int i = 1; i < _laps.Count; i++) c.LineTo(P(i), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(Ui.Res<Brush>("TextMuted"), 1.4) { LineJoin = PenLineJoin.Round }, g);
            dc.DrawEllipse(Ui.Res<Brush>(_fresh ? "Accent" : "TextMuted"), null, P(_laps.Count - 1), 2.8, 2.8);
        }
    }

    private sealed class RatingChart : StatsCharts.HoverChart
    {
        private readonly List<RatingPoint> _points;
        private readonly int _lo, _hi;

        public RatingChart(List<RatingPoint> points)
        {
            _points = points;
            int min = Math.Min(points[0].Before, points.Min(p => p.Rating)), max = Math.Max(points[0].Before, points.Max(p => p.Rating));
            int step = Step(max - min);
            _lo = (int)Math.Floor((min - step * 0.25) / step) * step;
            _hi = (int)Math.Ceiling((max + step * 0.25) / step) * step;
            if (_hi <= _lo) _hi = _lo + step;
        }

        private static int Step(int span) => new[] { 10, 20, 25, 50, 100, 200, 250, 500, 1000 }.FirstOrDefault(s => span / s <= 4, 2000);

        private Point At(int i)
        {
            double w = Math.Max(1, ActualWidth - Left - Right), h = Math.Max(1, ActualHeight - Top - Bottom);
            double x = Left + w * (i + 1) / _points.Count;
            return new Point(x, Top + h * (_hi - _points[i].Rating) / (double)(_hi - _lo));
        }

        private Point Start => new(Left, Top + Math.Max(1, ActualHeight - Top - Bottom) * (_hi - _points[0].Before) / (double)(_hi - _lo));

        protected override int HitAt(Point p)
        {
            int near = -1;
            double best = double.MaxValue;
            for (int i = 0; i < _points.Count; i++)
                if (Math.Abs(At(i).X - p.X) is var d && d < best) { best = d; near = i; }
            return best < 24 ? near : -1;
        }

        protected override string TipOf(int i)
        {
            var p = _points[i];
            int change = p.Rating - p.Before;
            return $"{p.Rating.ToString("N0", C)} ({StatFormat.Value(StatKind.Signed, change, false)}){(p.Win ? " · win" : "")}\n{p.Track}\n{Dates.DateTime(p.When.ToLocalTime())}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (w < Left + Right + 40) return;
            double px = Hairline.ScaleOf(this);
            var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
            int step = Step(_hi - _lo);
            for (int v = _lo; v <= _hi; v += step)
            {
                double y = Hairline.Snap(Top + (h - Top - Bottom) * (_hi - v) / (double)(_hi - _lo), px);
                dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
                var label = Text(v.ToString("N0", C));
                dc.DrawText(label, new Point(Left - 8 - label.Width, y - label.Height / 2));
            }
            string? lastMonth = null;
            for (int i = 0; i < _points.Count; i++)
            {
                string month = Dates.Month(_points[i].When.ToLocalTime());
                if (month == lastMonth) continue;
                lastMonth = month;
                var label = Text(month);
                double x = At(i).X;
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, Left, w - Right - label.Width), h - Bottom + 4));
            }
            var area = new StreamGeometry();
            var line = new StreamGeometry();
            double floor = h - Bottom;
            using (var a = area.Open())
            using (var l = line.Open())
            {
                a.BeginFigure(new Point(Left, floor), true, true);
                a.LineTo(Start, false, false);
                l.BeginFigure(Start, false, false);
                for (int i = 0; i < _points.Count; i++)
                {
                    a.LineTo(At(i), false, false);
                    l.LineTo(At(i), true, true);
                }
                a.LineTo(new Point(At(_points.Count - 1).X, floor), false, false);
            }
            area.Freeze();
            line.Freeze();
            var accent = Ui.Res<Brush>("Accent");
            dc.PushOpacity(0.14);
            dc.DrawGeometry(accent, null, area);
            dc.Pop();
            dc.DrawGeometry(null, new Pen(accent, 2.2) { LineJoin = PenLineJoin.Round }, line);
            var gold = Ui.Res<Brush>(StatsUi.Hue("Gold").Deep);
            for (int i = 0; i < _points.Count; i++)
            {
                if (!_points[i].Win) continue;
                var at = At(i);
                dc.DrawEllipse(gold, new Pen(Ui.Res<Brush>("Surface"), 1.5), at, 5, 5);
                var label = Text("Win", gold, bold: true);
                dc.DrawText(label, new Point(at.X - label.Width / 2, Math.Max(0, at.Y - 9 - label.Height)));
            }
            if (Hover >= 0)
            {
                var at = At(Hover);
                dc.DrawLine(Hairline.Pen(Ui.Res<Brush>("LineStrong"), px), new Point(at.X, Top), new Point(at.X, floor));
                dc.DrawEllipse(Ui.Res<Brush>("Heading"), null, at, 4.5, 4.5);
            }
        }
    }
}
