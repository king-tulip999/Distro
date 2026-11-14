using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal static class TireLetters
{
    public static string? Of(TelemetryFrame f, int car)
    {
        if ((uint)car >= TelemetryFrame.MaxCars) return null;
        int index = f.CarTyreCompound[car];
        if (index < 0) return null;
        var s = f.Session;
        bool sameModel = s.Player is { } me && s.ByCar.TryGetValue(car, out var d) && d.CarId == me.CarId;
        if (sameModel && s.TireCompounds.TryGetValue(index, out var name) && Letter(name) is { } letter)
        {
            if (letter == "W") return letter;
            int dry = 0;
            foreach (var other in s.TireCompounds.Values) if (Letter(other) != "W") dry++;
            return dry > 1 ? letter : "D";
        }
        return index switch { 0 => "D", 1 => "W", _ => PooledText.Number("", index) };
    }

    internal static string? Letter(string name)
    {
        string n = name.Trim().ToLowerInvariant();
        if (n.Length == 0) return null;
        if (n.Contains("wet") || n.Contains("rain")) return "W";
        if (n.Contains("inter")) return "I";
        if (n.Contains("soft")) return "S";
        if (n.Contains("medium")) return "M";
        if (n.Contains("hard")) return "H";
        if (n.Contains("dry")) return "D";
        if (n.Contains("primary")) return "P";
        if (n.Contains("alternate")) return "A";
        return char.IsLetterOrDigit(name.Trim()[0]) ? char.ToUpperInvariant(name.Trim()[0]).ToString() : null;
    }
}
