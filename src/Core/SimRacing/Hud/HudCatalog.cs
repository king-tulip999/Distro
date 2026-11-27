using Core.SimRacing.Hud.Widgets;

namespace Core.SimRacing.Hud;

internal static class HudCatalog
{
    public static readonly HudWidgetDef[] All =
    {
        new("relative", "Relative", "The cars nearest to you on track, with gaps in seconds, laps ahead / behind, classes, license badges, pit stops and battles.", 448, 300, () => new RelativeWidget())
            { MinW = 180, MinH = 90, DefaultHz = 15, Options = RelativeWidget.Options, Editor = Editors.RelativeEditor.Def,
              Limits = "Gaps are iRacing's own time estimates, stretched to your best lap so they read in your pace." },
        new("standings", "Standings", "The whole field: overall and class positions, gaps, laps, pit stops, out laps, licenses and more, grouped by class.", 488, 520, () => new StandingsWidget())
            { MinW = 200, MinH = 120, DefaultHz = 5, Options = StandingsWidget.Options, Editor = Editors.StandingsEditor.Def,
              Limits = "Pit stop counts, stints and out laps start when the overlay first sees the session. Other drivers' incidents come from iRacing's session information, which it updates every so often." },
        new("timing", "Lap timing and delta", "Live delta to your best, the session best or your optimal lap: a trace over the lap, a strip, times and sectors, a sector column or board, a lap ribbon or a pill, with predicted lap and lap times.", 380, 150, () => new TimingWidget())
            { MinW = 200, MinH = 70, DefaultHz = 20, Options = TimingWidget.Options, Editor = Editors.TimingEditor.Def,
              Limits = "Deltas are iRacing's own. Sector times are measured by Velocita at iRacing's sector lines. iRacing doesn't publish whether a lap counts, so “clean” means no incident points and no pit road." },
        new("fuel", "Fuel and strategy", "Laps to go, the next pit lap and stops left; the tank with the laps it lasts; a grid of this lap / average / most / least with per lap, laps, refuel and what's left at the flag; your recent laps' use; the plan's state.", 450, 300, () => new FuelWidget())
            { MinW = FuelWidget.WidgetMin, MinH = 120, DefaultHz = 4, Options = FuelWidget.Options, Editor = Editors.FuelEditor.Def,
              Limits = "Per-lap use is measured from your clean, green laps. Everything to the flag is an estimate." },
        new("session", "Session", "Session, time or laps left, lap, positions, phase, temperatures and progress.", 340, 120, () => new SessionWidget())
            { MinW = 140, MinH = 60, DefaultHz = 2, Options = SessionWidget.Options, Editor = Editors.SessionEditor.Def },
        new("alerts", "Alerts", "One place for every alert: fuel, pit limit, traffic, flags, incidents, best laps…", 340, 150, () => new AlertsWidget())
            { MinW = 160, MinH = 50, DefaultHz = 10, Options = AlertsWidget.Options, Editor = Editors.AlertsEditor.Def, HideWhenEmpty = true, Needed = "Hidden until an alert comes up." },
        new("traffic", "Multiclass traffic", "Other classes within a few seconds, closing rates and when you'll meet.", 320, 160, () => new TrafficWidget())
            { MinW = 180, MinH = 70, DefaultHz = 30, Options = TrafficWidget.Options, Editor = Editors.TrafficEditor.Def, HideWhenEmpty = true, HideAlways = true },
        new("headtohead", "Head to head", "The car right ahead and the car right behind once they're close: the gap, each sector and corner gained or lost, the trend and whether anyone catches before the flag. As duel rows, a ticker, corner bars, a track map, a battle line or a gap chart.", 520, 200, () => new HeadToHeadWidget())
            { MinW = 240, MinH = 60, DefaultHz = 10, Options = HeadToHeadWidget.Options, Editor = Editors.HeadToHeadEditor.Def, HideWhenEmpty = true, HideAlways = true,
              Limits = "iRacing doesn't send other cars' sector times, so Velocita times every car across iRacing's sector lines and 24 equal parts of the lap. A comparison shows once both cars have passed the same point, and laps through the pits are left out of the trend. In races the cars are the ones a place ahead and behind in your class; in other sessions, the nearest on track." },
        new("coach", "Coach", "Coaching from your own laps: one call per turn on where your laps lose most against your best, with your pace and pedals; or a racing line map turning with the car, a side-by-side compare with your best at this spot, or a lap debrief. Optional brake beep.", 860, 200, () => new CoachWidget())
            { MinW = 260, MinH = 110, DefaultHz = 20, Options = CoachWidget.Options, Editor = Editors.CoachEditor.Def, Natural = CoachWidget.Natural,
              Limits = "Needs Session Stats and at least 3 clean recorded laps in this car and track; your best is your fastest time through each turn across your recent clean laps. Turns are counted from how sharply the track bends. Your own line while driving is worked out from your speed and direction, so it can drift a little over a lap. iRacing doesn't give track edges, so lines are only compared with your best lap's." },
        new("radar", "Car radar", "The cars around you from above: alongside in red from iRacing's spotter signal, ahead and behind in amber getting stronger as they close, with the distance to the nearest.", 140, 220, () => new RadarWidget())
            { FixedSize = true, MinW = 80, MinH = 110, DefaultHz = 30, Options = RadarWidget.Options, Editor = Editors.RadarEditor.Def, HideWhenEmpty = true, Needed = "Hidden until a car is close around you.",
              Limits = "iRacing doesn't publish where other cars are across the track, only which side is taken, so with a car on each side Velocita can't tell which is which. Car lengths are learned from your first few overlaps; until then ahead and behind show as lines." },
        new("tyres", "Tires", "Pressures, temperatures (inner / middle / outer) and wear for each corner.", 280, 168, () => new TyresWidget())
            { MinW = 180, MinH = 60, DefaultHz = 2, Options = TyresWidget.Options, Editor = Editors.TyresEditor.Def, Natural = TyresWidget.Natural,
              Limits = "iRacing updates tire data in the pit box, not while driving." },
        new("weather", "Weather and track", "The sky, a compass with the wind turning with your car, air and track temperature, humidity, rain, rubber and the track's wetness.", WeatherWidget.PanelW, WeatherWidget.PanelH, () => new WeatherWidget())
            { FixedSize = true, MinW = 140, MinH = 80, DefaultHz = 5, Options = WeatherWidget.Options, Editor = Editors.WeatherEditor.Def,
              Limits = "No forecast: iRacing doesn't publish one." },
        new("wind", "Wind", "A pointer in a dotted dial showing where the wind blows, with its speed under it, turning with your car: red for a headwind, amber across, green behind.", 150, 150, () => new WindWidget())
            { FixedSize = true, MinW = 70, MinH = 70, DefaultHz = 5, Options = WindWidget.Options, Editor = Editors.WindEditor.Def,
              Limits = "iRacing gives the wind at the start / finish line, the same all round the track." },
        new("map", "Track map", "The track from iRacing's own maps, upright or turning with your car, whole or zoomed in on you, with the pit lane, start / finish and turn numbers, and every car as a dot in its class color; you stand out.", 300, 260, () => new MapWidget())
            { FixedSize = true, MinW = 140, MinH = 120, DefaultHz = 60, Background = 0, Options = MapWidget.Options, Editor = Editors.MapEditor.Def,
              Limits = "Cars are placed along the track's center line from their distance round the lap; tracks without a bundled map show none." },
        new("flatmap", "Flat track map", "The lap laid out as a line: the whole lap, the stretch around you, one line per class, a line that bends at each turn, or a thin bar along a screen edge, with every car as a dot in its class color.", 600, 118, () => new FlatMapWidget())
            { MinW = 160, MinH = 34, DefaultHz = 30, Options = FlatMapWidget.Options, Editor = Editors.FlatMapEditor.Def,
              Limits = "Cars are placed from their distance round the lap. Turn numbers, the pit lane and the bends come from the track map; tracks without a bundled map show a straight line." },
        new("laps", "Lap history", "Your laps with deltas, what happened on them, fuel used, and consistency.", 400, 440, () => new LapsWidget())
            { MinW = 200, MinH = 110, DefaultHz = 10, MovingHz = 30, Options = LapsWidget.Options, Editor = Editors.LapsEditor.Def, Natural = LapsWidget.Natural },
        new("notes", "Notes", "Your notes for this track and car: the next corner's note lights up as you get close, as a countdown, a slim ticker, a strip of the corners ahead or a list with distances, with the notes for the whole track under it.", 342, 160, () => new NotesWidget())
            { MinW = 180, MinH = 30, DefaultHz = 5, MovingHz = 20, Moves = NotesWidget.Moves, Options = NotesWidget.Options, Editor = Editors.NotesEditor.Def, HideWhenEmpty = true, Needed = "Hidden when there are no notes for this track and car.",
              Limits = "Notes are written in Session Stats, on the Performance card for each car and track." },
        new("events", "Race events", "A timeline of flags, pit stops, places gained or lost, incidents and best laps.", 320, 200, () => new EventsWidget())
            { MinW = 300, MinH = 130, DefaultHz = 2, Options = EventsWidget.Options, Editor = Editors.EventsEditor.Def },
        new("incidents", "Incident log", "Your incidents against the limit: as a budget bar with the latest ones, chips, a single line or a timeline of the race.", 400, 220, () => new IncidentsWidget())
            { MinW = 240, MinH = 90, DefaultHz = 10, Options = IncidentsWidget.Options, Editor = Editors.IncidentsEditor.Def,
              Limits = "Light contact gives no points, so iRacing doesn't report it. Incidents are named from their points; the log starts when the overlay first sees the session." },
        new("inputs", "Inputs", "Throttle, brake, clutch and steering with gear, speed and RPM: a gear dial with pedal bars, a trace and an RPM bar, or the gear dial and trace alone, with extra fields to pick.", 436, 96, () => new InputsWidget())
            { MinW = 225, MinH = 50, DefaultHz = 30, Options = InputsWidget.Options, Editor = Editors.InputsEditor.Def, Natural = InputsWidget.Natural,
              Limits = "The shift point, limiter flash and RPM scale come from iRacing's figures for your car." },
        new("ledboard", "LED board", "A square of lit dots like a trackside board: your gear, lap, position or laps left in big digits, and the flag that's out filling the board.", 150, 150, () => new LedBoardWidget())
            { FixedSize = true, MinW = 60, MinH = 60, DefaultHz = 30, Background = 0, Options = LedBoardWidget.Options, Editor = Editors.LedBoardEditor.Def,
              Limits = "Shows up to three digits." },
        new("block", "Data block", "Any single value (live, session or calculated) as a number, bar or graph.", 170, 80, () => new BlockWidget())
            { FixedSize = true, MinW = 150, MinH = 72, DefaultHz = 10, Options = BlockWidget.Options, Editor = Editors.BlockEditor.Def },
        new("pitbox", "Pit box helper", "Your pit stop from the lane to the exit: meters to your box against the speed limit, the lane with your box marked, or the stop counting down with each service and where you'll rejoin.", PitBoxWidget.W, PitBoxWidget.H, () => new PitBoxWidget())
            { MinW = 220, MinH = 110, DefaultHz = 15, Options = PitBoxWidget.Options, Editor = Editors.PitBoxEditor.Def, HideWhenEmpty = true, Needed = "Hidden until you are in the pit lane.",
              Limits = "Shows on pit road. iRacing doesn't publish how long a stop takes or how far along tire changes are, so the countdown follows the fuel going in at the rate it's measured, and repairs. The lane's ends are learned from where cars enter and leave pit road this session." },
        new("cover", "Stream cover", "Covers the setup screen on stream while you're in the garage: a wordmark with session info, a hatched cover or a driver card.", StreamCoverWidget.W, StreamCoverWidget.H, () => new StreamCoverWidget())
            { MinW = 200, MinH = 100, DefaultHz = 2, Options = StreamCoverWidget.Options, Editor = Editors.StreamCoverEditor.Def, HideWhenEmpty = true, Needed = "Hidden unless you are in the garage.",
              Limits = "Shows only while iRacing's garage screen is open." },
        new("gforce", "G-force meter", "How hard you're cornering, braking and accelerating: a friction circle with a trail, bars with a trace, or this lap's peaks against your best lap.", GForceWidget.W, GForceWidget.H, () => new GForceWidget())
            { MinW = 120, MinH = 100, DefaultHz = 30, Options = GForceWidget.Options, Editor = Editors.GForceEditor.Def },
        new("spread", "Lap time spread", "Every car's recent pace on one chart: dots on a time axis with a row per class, a pace ranking, or how your class's lap times are spread.", LapSpreadWidget.W, LapSpreadWidget.H, () => new LapSpreadWidget())
            { MinW = 260, MinH = 120, DefaultHz = 2, Options = LapSpreadWidget.Options, Editor = Editors.LapSpreadEditor.Def,
              Limits = "Pace is the average of each car's recent clean laps as iRacing reports them; laps on pit road or under caution are left out, so a car shows after its first clean lap." },
        new("hstandings", "Horizontal standings", "A thin position strip across the screen: the field as a ticker, the places around you, or one line per class.", TickerStandingsWidget.W, TickerStandingsWidget.H, () => new TickerStandingsWidget())
            { MinW = 360, MinH = 28, DefaultHz = 5, Options = TickerStandingsWidget.Options, Editor = Editors.TickerStandingsEditor.Def,
              Limits = "Closing and opening come from how the on-track gap changed over the last few seconds, shown per lap of yours." },
        new("burn", "Fuel burn zones", "Where the fuel goes: the track colored by how much each stretch burns, the lap as a bar with a lift-and-coast tip, or fuel and time per gear.", BurnZonesWidget.W, BurnZonesWidget.H, () => new BurnZonesWidget())
            { MinW = 180, MinH = 100, DefaultHz = 4, Options = BurnZonesWidget.Options, Editor = Editors.BurnZonesEditor.Def,
              Limits = "Learned from your own clean laps in this session." },
        new("ers", "Boost and ERS", "Hybrid battery, deploy and regen, push-to-pass and turbo boost, for the cars that have them: as a battery, a push-to-pass counter or a ring.", ErsWidget.W, ErsWidget.H, () => new ErsWidget())
            { MinW = 140, MinH = 70, DefaultHz = 15, Options = ErsWidget.Options, Editor = Editors.ErsEditor.Def, HideWhenEmpty = true, Needed = "Hidden in cars with no hybrid, push-to-pass or boost.",
              Limits = "Shows only what your car reports to iRacing." },
        new("overtake", "Overtake alert", "A short, big warning when a car is about to pass you: a banner at the top, a glow down the screen edge, or a card with who's coming and when.", OvertakeWidget.W, OvertakeWidget.H, () => new OvertakeWidget())
            { MinW = 60, MinH = 36, DefaultHz = 15, Options = OvertakeWidget.Options, Editor = Editors.OvertakeEditor.Def, HideWhenEmpty = true, HideAlways = true,
              Limits = "Which side a car is on comes from iRacing's spotter signal, so it's known only once they're alongside. Gaps and closing speed come from iRacing's own time estimates; the corner they reach you in assumes they keep closing at the same rate." },
        new("controls", "Car controls", "Your in-car adjustments (brake bias, traction control, ABS, maps and the rest): every control in a strip with the one you changed lit, the changed one alone in big digits, or a knob.", ControlsWidget.W, ControlsWidget.H, () => new ControlsWidget())
            { MinW = 120, MinH = 50, DefaultHz = 10, Options = ControlsWidget.Options, Editor = Editors.ControlsEditor.Def, HideWhenEmpty = true, Needed = "Hidden until you change a control.",
              Limits = "iRacing doesn't publish each control's range, so values show without their limits." },
        new("rejoin", "Rejoin helper", "After a spin or going off: the cars coming, their time to you and when it's safe to go, as a countdown, a wait / go sign or a line of traffic.", RejoinWidget.W, RejoinWidget.H, () => new RejoinWidget())
            { MinW = 200, MinH = 100, DefaultHz = 15, Options = RejoinWidget.Options, Editor = Editors.RejoinEditor.Def, HideWhenEmpty = true, Needed = "Hidden until you spin or go off.",
              Limits = "Time to you comes from iRacing's own estimates. iRacing doesn't say which side of the track cars are on." },
    };

    private static readonly Dictionary<string, HudWidgetDef> ByKind = All.ToDictionary(d => d.Kind, StringComparer.OrdinalIgnoreCase);

    public static HudWidgetDef? Find(string? kind) => kind is not null && ByKind.TryGetValue(kind, out var d) ? d : null;
}
