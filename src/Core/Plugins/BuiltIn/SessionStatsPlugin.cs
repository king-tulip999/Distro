using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Race;
using Core.Views;
using Core.Views.Pages;
using Core.Views.RaceEvents;
using WinForms = System.Windows.Forms;

namespace Core.Plugins.BuiltIn;

internal sealed class SessionStatsPlugin : Plugin
{
    public const string PageKey = "stats";

    public override string Id => PluginIds.SessionStats;
    public override AppKind App => AppKind.Velocita;
    public override string Name => "Session Stats";
    public override bool ShowsPages(AppServices app) => app.Config.SessionStats.ShowPage;
    public override string Description => "Your iRacing history: personal bests, races, incidents and lap analysis.";
    public override string Icon => "I.Candles";
    public override (string Soft, string Deep) Colors => ("Tool7Soft", "Tool7Deep");
    public override int Order => 60;
    public override string Reads =>
        "iRacing's live telemetry (its shared memory, read only) while iRacing runs, and whether it's running. The telemetry files in Documents\\iRacing\\telemetry: all of them once, then only new ones, when this plugin starts and shortly after iRacing closes.";
    public override string BackgroundWork =>
        "Waits for iRacing to start (process events, no polling); while it runs, adds up each lap from the telemetry the iRacing Overlay reads too and saves a lap's figures as it's completed. Reads new telemetry files in the background at the lowest priority. Otherwise never.";
    public override string Changes => "Nothing on your PC: your stats and lap traces are saved in Velocita's own folder (stats.db, traces.db). Telemetry files are only read.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PageKey, "Session Stats", "I.Candles", (app, window) => new StatsPage(app, window)),
    };

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("Session Stats", "Every stat from your iRacing sessions: time, cars and tracks, pace, races, incidents, driving style, conditions.", "Session Stats", PageKey, "stats.all", "stats statistics history laps time pb personal best races wins podiums incidents irating"),
        new("Recent sessions", "Every session with its laps and sectors; open one to analyze laps: line, inputs, delta.", "Session Stats", PageKey, "stats.sessions", "sessions laps sectors lap analysis telemetry trace compare delta racing line optimal"),
        new("Lap traces", "How long lap telemetry is kept, the space it takes, delete all.", "Session Stats", PageKey, "stats.traces", "traces telemetry storage retention keep delete disk space ibt"),
        new("Telemetry files", "Record telemetry to disk (exact GPS lines), the .ibt folder's size, open it, clear files already read.", "Session Stats", PageKey, "stats.telemetry", "ibt telemetry folder record disk gps accurate line clear delete space size recycle"),
        new("Race events", "Every pass, off track, incident and pit stop of the current session; click one to watch it in iRacing's replay. Can open when you leave the car.", "Session Stats", PageKey, "stats.raceevents", "race events replay passes overtakes off track incidents pit stops rivals standings lap times camera watch"),
    };

    private const string RaceEventsConsumer = "Race events";
    private RaceEventRecorder? _recorder;
    private Action<SimRacing.Telemetry.TelemetryFrame>? _feed;
    private DemoRaceEvents? _demoEvents;

    public static IRaceEventsFeed? RaceEventsFeed(AppServices app)
    {
        if (app.Plugins.Find(PluginIds.SessionStats) is not SessionStatsPlugin { IsActive: true } plugin) return null;
        if (app.Demo) return plugin._demoEvents ??= new DemoRaceEvents();
        return plugin._recorder is { } recorder ? new LiveRaceEvents(recorder, External.Allowed) : null;
    }

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var s = app.Config.SessionStats.RaceEvents;
        yield return new PluginHotkey("stats.raceevents", "Open race events", () => s.Hotkey, hk => s.Hotkey = hk, () => RaceEventsWindow.Toggle(app), Page: PageKey);
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app) =>
        items.Add(new WinForms.ToolStripMenuItem("Race events", null, (_, _) => RaceEventsWindow.Open(app))
        {
            ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(app.Config.SessionStats.RaceEvents.Hotkey),
        });

    public SessionStatsService? Service { get; private set; }

    public static IReadOnlyCollection<int>? LeagueIds(AppServices app)
    {
        HashSet<int>? ids = null;
        foreach (var plugin in app.Plugins.Active)
            if (plugin.IracingLeagueIds(app) is { } own) (ids ??= new()).UnionWith(own);
        return ids;
    }

    public static SessionStatsService? Of(AppServices app) => (app.Plugins.Find(PluginIds.SessionStats) as SessionStatsPlugin)?.Service;

    protected override void OnStart(AppServices app)
    {
        Service = new SessionStatsService(app);
        Service.Start();
        if (app.Demo) return;
        var recorder = _recorder = new RaceEventRecorder();
        Service.RaceEvents = recorder.Snapshot;
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        recorder.LeftCar += () => ui.BeginInvoke(() =>
        {
            if (app.Config.SessionStats.RaceEvents.OpenWhenLeavingCar && ReferenceEquals(_recorder, recorder)) RaceEventsWindow.Open(app, activate: false);
        });
        _feed = recorder.Feed;
        app.Telemetry.AddObserver(_feed);
        app.Telemetry.Acquire(RaceEventsConsumer);
    }

    protected override void OnStop(AppServices app)
    {
        Service?.Stop();
        Service = null;
        RaceEventsWindow.Showing?.Close();
        if (_feed is { } feed)
        {
            app.Telemetry.RemoveObserver(feed);
            app.Telemetry.Release(RaceEventsConsumer);
        }
        _feed = null;
        _recorder = null;
        _demoEvents = null;
    }
}
