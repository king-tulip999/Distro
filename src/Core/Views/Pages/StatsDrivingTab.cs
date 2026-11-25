using System.Windows;
using Core.SessionStats;
using Core.SimRacing.Race;

namespace Core.Views.Pages;

internal static class StatsDrivingTab
{
    public static FrameworkElement Build(StatsTabData data, IStatsHost host)
    {
        var charts = data.Charts;
        IEnumerable<(StatDef, StatResult)> Many(params string[] ids) => ids.Select(data.Get).OfType<(StatDef, StatResult)>();

        string? total = data.Get("time.total") is { Result: { NoData: false } t } ? t.Value + " driving" : null;
        var activity = StatsSections.Section("Driving activity", string.Join(" · ", new[] { RaceFormat.Count(charts.Sessions, "session"), total }.OfType<string>()), null,
            StatsTabs.Stacked(StatsSections.Chart("Session mix", null, StatsCharts.SessionMix(charts.SessionTypes)),
                StatsSections.Metrics(Many(data.Range == StatRange.Week
                    ? new[] { "time.week", "time.month", "time.session", "time.stint", "time.garage" }
                    : new[] { "time.week", "time.month", "time.weekly", "time.session", "time.stint", "time.garage" }))));
        Ui.Anchor(activity, "stats.activity");

        var when = StatsTabs.Card(StatsSections.Chart("When you drive", "Brighter squares are the hours you drive most", StatsCharts.Heatmap(charts.Heatmap, charts.HeatmapDays)));
        var trend = StatsTabs.Card(StatsSections.Chart(charts.Trend.Title, $"Each bar is your total driving time for that {charts.Trend.Unit}", StatsCharts.Trend(charts.Trend)));

        string variety = string.Join(" · ", new[] { Count(data.Get("car.unique"), "car"), charts.Cars.Count > 0 ? "by driving time" : null }.OfType<string>());
        var cars = StatsSections.Section("Cars", variety.Length > 0 ? variety : null, null,
            StatsTabs.Stacked(StatsCharts.Ranked(charts.Cars, "No cars in this range", cars: true), StatsCharts.Totals(charts)));
        string trackNote = string.Join(" · ", new[] { Count(data.Get("track.unique"), "track"), charts.Tracks.Count > 0 ? "by driving time" : null }.OfType<string>());
        var all = Ui.LabeledButton("I.ArrowRight", "All in Cars and tracks", () => host.ShowTab("cars"));
        var tracks = StatsSections.Section("Tracks", trackNote.Length > 0 ? trackNote : null, all,
            StatsCharts.Ranked(charts.Tracks, "No tracks in this range"));
        Ui.Anchor(cars, "stats.cars");

        return StatsTabs.Stacked(activity,
            StatsLayout.Pair(when, trend, 1050),
            StatsLayout.Pair(cars, tracks, 1050));
    }

    private static string? Count((StatDef Def, StatResult Result)? stat, string noun) =>
        stat is { Result: { NoData: false } r } && int.TryParse(r.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out int n) ? RaceFormat.Count(n, noun) : null;
}
