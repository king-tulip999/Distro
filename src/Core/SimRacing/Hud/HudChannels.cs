using Core.Config;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud;

internal enum ChannelKind
{
    Live,
    Session,
    Calculated,
    History,
    Pits,
}

internal enum ChannelUnit { None, Speed, Temp, Pressure, Fuel, Seconds, LapTime, Percent, Rpm, G, Degrees, Count }

internal sealed record HudChannel(string Id, string Label, ChannelKind Kind, ChannelUnit Unit, Func<HudView, double?> Read, int Decimals = 1)
{
    public Func<double, string>? Text { get; init; }
}

internal static class HudChannels
{
    public static readonly HudChannel[] All =
    {
        new("speed", "Speed", ChannelKind.Live, ChannelUnit.Speed, v => v.Frame.Speed, 0),
        new("rpm", "RPM", ChannelKind.Live, ChannelUnit.Rpm, v => v.Frame.Rpm, 0),
        new("gear", "Gear", ChannelKind.Live, ChannelUnit.None, v => v.Frame.Gear, 0),
        new("throttle", "Throttle", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Throttle, 0),
        new("brake", "Brake", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Brake, 0),
        new("clutch", "Clutch pedal", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Clutch is { } c ? 1 - c : null, 0),
        new("steer", "Steering angle", ChannelKind.Live, ChannelUnit.Degrees, v => v.Frame.Steering is { } s ? s * 180 / Math.PI : null, 0),
        new("fuel", "Fuel", ChannelKind.Live, ChannelUnit.Fuel, v => v.Frame.Fuel),
        new("fuelPct", "Fuel (% of tank)", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.FuelPct, 0),
        new("fuelHour", "Fuel use per hour (kg)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.FuelUsePerHour),
        new("water", "Water temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.WaterTemp, 0),
        new("oil", "Oil temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.OilTemp, 0),
        new("oilPress", "Oil pressure (bar)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.OilPress),
        new("fuelPress", "Fuel pressure (bar)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.FuelPress),
        new("voltage", "Voltage", ChannelKind.Live, ChannelUnit.None, v => v.Frame.Voltage),
        new("bias", "Brake bias", ChannelKind.Live, ChannelUnit.None, v => v.Frame.BrakeBias),
        new("tc", "Traction control", ChannelKind.Live, ChannelUnit.None, v => v.Frame.TractionControl, 0),
        new("abs", "ABS", ChannelKind.Live, ChannelUnit.None, v => v.Frame.Abs, 0),
        new("latG", "Lateral G", ChannelKind.Live, ChannelUnit.G, v => v.Frame.LatAccel, 2),
        new("longG", "Longitudinal G", ChannelKind.Live, ChannelUnit.G, v => v.Frame.LongAccel, 2),
        new("battery", "Battery / ERS", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.BatteryPct is { } b ? (b > 1 ? b / 100 : b) : null, 0),
        new("air", "Air temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.AirTemp),
        new("track", "Track temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.TrackTemp),
        new("wind", "Wind speed", ChannelKind.Live, ChannelUnit.Speed, v => v.Frame.WindSpeed, 0),
        new("humidity", "Humidity", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Humidity, 0),
        new("rain", "Precipitation", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Precipitation, 0),
        new("lap", "Current lap time", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.LapCurrent),
        new("last", "Last lap", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.LapLast),
        new("best", "Best lap", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.LapBest),
        new("deltaBest", "Delta to best lap", ChannelKind.Live, ChannelUnit.Seconds, v => v.Frame.DeltaBestOk ? v.Frame.DeltaBest : null, 2),
        new("deltaSession", "Delta to session best", ChannelKind.Live, ChannelUnit.Seconds, v => v.Frame.DeltaSessionBestOk ? v.Frame.DeltaSessionBest : null, 2),
        new("incidents", "My incidents", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.Incidents, 0),
        new("position", "Position (overall)", ChannelKind.Live, ChannelUnit.Count, v => Pos(v, v.Frame.CarPosition), 0),
        new("classPosition", "Position in class", ChannelKind.Live, ChannelUnit.Count, v => v.Race.StandingOf(v.Frame.PlayerCarIdx)?.ClassPosition is > 0 and var c ? c : null, 0),
        new("timeLeft", "Session time left", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.SessionTimeRemain, 0),
        new("lapsLeft", "Session laps left", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.SessionLapsRemain, 0),
        new("trackLength", "Track length (km)", ChannelKind.Session, ChannelUnit.None, v => v.Frame.Session.TrackLengthKm, 2),
        new("pitLimit", "Pit speed limit", ChannelKind.Session, ChannelUnit.Speed, v => v.Frame.Session.PitSpeedLimitKph / 3.6, 0),
        new("tank", "Usable tank", ChannelKind.Session, ChannelUnit.Fuel, v => v.Race.Tank),
        new("incLimit", "Incident limit", ChannelKind.Session, ChannelUnit.Count, v => v.Frame.Session.IncidentLimit, 0),
        new("fuelPerLap", "Fuel per lap (average)", ChannelKind.Calculated, ChannelUnit.Fuel, v => v.Race.FuelPerLap, 2),
        new("fuelLaps", "Laps of fuel", ChannelKind.Calculated, ChannelUnit.None, v => v.Race.Fuel?.LapsOfFuel),
        new("fuelToFinish", "Fuel to the flag (estimate)", ChannelKind.Calculated, ChannelUnit.Fuel, v => v.Race.Fuel?.ToFinish),
        new("fuelSurplus", "Spare fuel at the flag (estimate)", ChannelKind.Calculated, ChannelUnit.Fuel, v => v.Race.Fuel?.Surplus),
        new("lapsToGo", "Laps to go (estimate)", ChannelKind.Calculated, ChannelUnit.None, v => v.Race.Fuel?.LapsToGo ?? v.Race.LapsToGo(v.Frame)),
        new("gapAhead", "Gap to the car ahead", ChannelKind.Calculated, ChannelUnit.Seconds, v => Neighbour(v, true), 1),
        new("gapBehind", "Gap to the car behind", ChannelKind.Calculated, ChannelUnit.Seconds, v => Neighbour(v, false), 1),
        new("pitTimer", "Time on pit road", ChannelKind.Calculated, ChannelUnit.Seconds, v => v.Race.PitTimer, 1),
        new("stint", "Stint time", ChannelKind.History, ChannelUnit.LapTime, v => v.Race.StintStart is { } s && v.Frame.SessionTime is { } t ? t - s : null, 0),
        new("stintLaps", "Stint laps", ChannelKind.History, ChannelUnit.Count, v => v.Frame.LapCompleted is { } l ? l - v.Race.StintStartLap : null, 0),
        new("avgLap", "Average lap (last 5 clean)", ChannelKind.History, ChannelUnit.LapTime, v => LapMath.Stats(v.Race.Laps.Laps, 5)?.Average),
        new("consistency", "Lap spread (std. dev.)", ChannelKind.History, ChannelUnit.Seconds, v => LapMath.Stats(v.Race.Laps.Laps, 10)?.StdDev, 3),
        new("spread5", "Consistency (± last 5 clean laps)", ChannelKind.History, ChannelUnit.Seconds, v => LapMath.Stats(v.Race.Laps.Laps, 5) is { Count: >= 3 } s ? s.Spread / 2 : null, 2),
        new("offTracks", "Off-tracks", ChannelKind.History, ChannelUnit.Count, v => v.Frame.Status == SimStatus.Connected ? v.Race.TrackLimits.Count : null, 0),
        new("irChange", "iRating gain / loss (est.)", ChannelKind.Calculated, ChannelUnit.None, v => v.Race.IRatingChangeOf(v.Frame.PlayerCarIdx) is { } c ? Math.Round(c) : null, 0),
        new("trackTrend", "Track temperature change (10 min)", ChannelKind.Calculated, ChannelUnit.None, v => v.Race.Weather.TrackPerWindow is { } t ? (v.Units == HudUnits.Imperial ? t * 9 / 5 : t) : null, 1),
        new("pitsOpen", "Pits open", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("PitsOpen"), 0) { Text = x => x > 0 ? "Open" : "Closed" },
        new("pitLimiter", "Pit limiter", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("dcPitSpeedLimiterToggle"), 0) { Text = x => x > 0 ? "On" : "Off" },
        new("tireSetsLeft", "Tire sets left", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.RawValue("TireSetsAvailable"), 0) { Text = Sets },
        new("tireSetsUsed", "Tire sets used", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.RawValue("TireSetsUsed"), 0),
        new("tireSetLimit", "Dry tire set limit", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.RawValue("PlayerCarDryTireSetLimit"), 0) { Text = x => x <= 0 ? "None" : Sets(x) },
        new("pitTires", "Tires changed at the next stop", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.Vars.ContainsKey("PitSvFlags") ? System.Numerics.BitOperations.PopCount((uint)v.Frame.PitFlags & 0xF) : null, 0),
        new("pitPressLF", "Pit stop pressure LF", ChannelKind.Live, ChannelUnit.Pressure, v => v.Frame.RawValue("PitSvLFP")),
        new("pitPressRF", "Pit stop pressure RF", ChannelKind.Live, ChannelUnit.Pressure, v => v.Frame.RawValue("PitSvRFP")),
        new("pitPressLR", "Pit stop pressure LR", ChannelKind.Live, ChannelUnit.Pressure, v => v.Frame.RawValue("PitSvLRP")),
        new("pitPressRR", "Pit stop pressure RR", ChannelKind.Live, ChannelUnit.Pressure, v => v.Frame.RawValue("PitSvRRP")),
        new("deltaLast", "Delta to last lap", ChannelKind.Live, ChannelUnit.Seconds, v => v.Frame.RawValue("LapDeltaToSessionLastlLap_OK") is > 0 ? v.Frame.RawValue("LapDeltaToSessionLastlLap") : null, 2),
        new("deltaSessionOptimal", "Delta to session optimal lap", ChannelKind.Live, ChannelUnit.Seconds, v => v.Frame.RawValue("LapDeltaToSessionOptimalLap_OK") is > 0 ? v.Frame.RawValue("LapDeltaToSessionOptimalLap") : null, 2),
        new("distAhead", "Distance to the car ahead (m)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("CarDistAhead") is > 0 and var a ? a : null, 0),
        new("distBehind", "Distance to the car behind (m)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("CarDistBehind") is > 0 and var b ? b : null, 0),
        new("absCut", "ABS brake cut", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.RawValue("BrakeABScutPct"), 0),
        new("handbrake", "Handbrake", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.RawValue("HandbrakeRaw"), 0),
        new("boost", "Manifold pressure (bar)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("ManifoldPress"), 2),
        new("oilLevel", "Oil level (L)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("OilLevel")),
        new("waterLevel", "Coolant level (L)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("WaterLevel")),
        new("jokerLeft", "Joker laps left", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.RawValue("SessionJokerLapsRemain"), 0),
        new("pacing", "Pacing", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("PaceMode"), 0) { Text = Pacing },
        new("bopWeight", "Weight penalty (kg)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.RawValue("PlayerCarWeightPenalty"), 0),
        new("bopPower", "Power adjustment", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.RawValue("PlayerCarPowerAdjust") is { } pa ? pa / 100 : null, 1),
        TirePressure("LF"), TirePressure("RF"), TirePressure("LR"), TirePressure("RR"),
        TireTemp("LF"), TireTemp("RF"), TireTemp("LR"), TireTemp("RR"),
        TireWear("LF", 0), TireWear("RF", 1), TireWear("LR", 2), TireWear("RR", 3),
    };

    private static string Sets(double x) => x >= 255 ? "Unlimited" : x.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Pacing(double x) => (int)x switch
    {
        0 => "Single file start",
        1 => "Double file start",
        2 => "Single file restart",
        3 => "Double file restart",
        _ => "Not pacing",
    };

    private static HudChannel TirePressure(string corner) =>
        new("tirePress" + corner, "Tire pressure " + corner, ChannelKind.Pits, ChannelUnit.Pressure, v => v.Frame.RawValue(corner + "pressure"));

    private static HudChannel TireTemp(string corner) =>
        new("tireSurface" + corner, "Tire surface temperature " + corner, ChannelKind.Pits, ChannelUnit.Temp,
            v => v.Frame.RawValue(corner + "tempL") is { } l && v.Frame.RawValue(corner + "tempM") is { } m && v.Frame.RawValue(corner + "tempR") is { } r ? (l + m + r) / 3 : null, 0);

    private static HudChannel TireWear(string corner, int i) =>
        new("tireTread" + corner, "Tire tread left " + corner, ChannelKind.Pits, ChannelUnit.Percent,
            v => v.Frame.TyreWearInner[i] is { } a && v.Frame.TyreWearMiddle[i] is { } b && v.Frame.TyreWearOuter[i] is { } c ? Math.Min(a, Math.Min(b, c)) : null, 0);

    public static HudChannel? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    private static double? Pos(HudView v, int[] positions) =>
        (uint)v.Frame.PlayerCarIdx < TelemetryFrame.MaxCars && positions[v.Frame.PlayerCarIdx] > 0 ? positions[v.Frame.PlayerCarIdx] : null;

    private static double? Neighbour(HudView v, bool ahead)
    {
        var rows = FieldOrder.Relative(v.Frame, ahead ? 1 : 0, ahead ? 0 : 1, profile: v.Race.Profile);
        foreach (var r in rows) if (!r.IsPlayer) return r.Gap is { } g ? Math.Abs(g) : null;
        return null;
    }

    public static double Convert(double value, ChannelUnit unit, HudUnits u) => unit switch
    {
        ChannelUnit.Speed => RaceFormat.SpeedValue(value, u),
        ChannelUnit.Temp => u == HudUnits.Imperial ? value * 9 / 5 + 32 : value,
        ChannelUnit.Pressure => u == HudUnits.Imperial ? value * 0.145038 : value,
        ChannelUnit.Fuel => RaceFormat.FuelValue(value, u),
        ChannelUnit.Percent => value * 100,
        ChannelUnit.G => value / 9.81,
        _ => value,
    };

    public static string UnitText(ChannelUnit unit, HudUnits u) => unit switch
    {
        ChannelUnit.Speed => RaceFormat.SpeedUnit(u),
        ChannelUnit.Temp => u == HudUnits.Imperial ? "°F" : "°C",
        ChannelUnit.Pressure => u == HudUnits.Imperial ? Units.Psi : Units.KPa,
        ChannelUnit.Fuel => RaceFormat.FuelUnit(u),
        ChannelUnit.Seconds => Units.S,
        ChannelUnit.Percent => "%",
        ChannelUnit.Rpm => Units.Rpm,
        ChannelUnit.G => "g",
        ChannelUnit.Degrees => "°",
        _ => "",
    };

    public static string KindText(ChannelKind k) => k switch
    {
        ChannelKind.Live => "live",
        ChannelKind.Session => "session",
        ChannelKind.Calculated => "calculated",
        ChannelKind.Pits => "only updates while the car is in the pits",
        _ => "recorded",
    };
}
