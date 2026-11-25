using System.Globalization;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal sealed class ControlItem
{
    public ControlItem(string key, string label, string name)
    {
        Key = key;
        Label = label;
        Name = name;
    }

    public string Key { get; }
    public string Label { get; }
    public string Name { get; }
    public bool Present { get; internal set; }
    public double Start { get; internal set; }
    public double Value { get; internal set; }
    public double? Previous { get; internal set; }
    public double ChangedAt { get; internal set; } = double.NegativeInfinity;
    public int? ChangedLap { get; internal set; }
    public int Changes { get; internal set; }

    public double? Step => Previous is { } was ? Value - was : null;
}

internal readonly record struct ControlChange(ControlItem Item, double From, double To, double At, int? Lap);

internal sealed class ControlLog
{
    public const int HistoryCapacity = 16;
    private const double Same = 1e-4;
    public const string BrakeBiasKey = "dcBrakeBias", TractionKey = "dcTractionControl", AbsKey = "dcABS", MapKey = "map";

    private static readonly string[] Skipped =
    {
        "Toggle", "Trigger", "Starter", "Headlight", "TearOff", "Tearoff", "DashPage", "DashDisplay", "PushToTalk", "Wiper", "LowFuelAccept",
        "PitSpeedLimiter", "Ignition", "Horn", "Flash", "Radio", "Camera", "Replay", "Display", "Pit",
    };

    private static readonly Dictionary<string, (string Label, string Name)> Known = new(StringComparer.Ordinal)
    {
        [BrakeBiasKey] = ("BB", "Brake bias"),
        [TractionKey] = ("TC", "Traction control"),
        ["dcTractionControl2"] = ("TC2", "Traction control 2"),
        ["dcTractionControl3"] = ("TC3", "Traction control 3"),
        ["dcTractionControl4"] = ("TC4", "Traction control 4"),
        [AbsKey] = ("ABS", "ABS"),
        [MapKey] = ("Map", "Engine map"),
        ["dcEngineMap"] = ("Map", "Engine map"),
        ["dcFuelMixture"] = ("Mix", "Fuel mixture"),
        ["dcAntiRollFront"] = ("ARB F", "Front anti-roll bar"),
        ["dcAntiRollRear"] = ("ARB R", "Rear anti-roll bar"),
        ["dcThrottleShape"] = ("Throttle", "Throttle shape"),
        ["dcMGUKDeployMode"] = ("Deploy", "Deploy mode"),
        ["dcMGUKDeployFixed"] = ("Deploy", "Fixed deploy"),
        ["dcMGUKRegenGain"] = ("Regen", "Regen gain"),
        ["dcWeightJackerLeft"] = ("Jack L", "Weight jacker left"),
        ["dcWeightJackerRight"] = ("Jacker", "Weight jacker"),
        ["dcDiffEntry"] = ("Diff in", "Diff entry"),
        ["dcDiffMiddle"] = ("Diff mid", "Diff middle"),
        ["dcDiffExit"] = ("Diff out", "Diff exit"),
        ["dcDiffPreload"] = ("Preload", "Diff preload"),
        ["dcBrakeMisc"] = ("Brake misc", "Brake misc"),
        ["dcPeakBrakeBias"] = ("Peak BB", "Peak brake bias"),
        ["dcEngineBraking"] = ("EB", "Engine braking"),
        ["dcBoostLevel"] = ("Boost", "Boost level"),
        ["dcLaunchRPM"] = ("Launch", "Launch rpm"),
        ["dcRFBrakeAttachedToggle"] = ("RF brake", "Right front brake"),
    };

    private readonly Dictionary<string, ControlItem> _byKey = new(StringComparer.Ordinal);
    private readonly List<ControlItem> _items = new();
    private readonly List<IrsdkVar> _vars = new();
    private readonly ControlChange[] _history = new ControlChange[HistoryCapacity];
    private int _historyHead, _historyCount;
    private object? _varsFrom;
    private bool _driven;

    public IReadOnlyList<ControlItem> Items => _items;
    public ControlItem? LastChanged { get; private set; }
    public double LastChangeAt { get; private set; } = double.NegativeInfinity;
    public int HistoryCount => _historyCount;

    public double SmallestStep(ControlItem item)
    {
        double least = double.PositiveInfinity;
        for (int i = 0; i < _historyCount; i++)
        {
            var c = History(i);
            if (!ReferenceEquals(c.Item, item)) continue;
            double step = Math.Abs(c.To - c.From);
            if (step > Same && step < least) least = step;
        }
        return double.IsPositiveInfinity(least) ? 0 : least;
    }

    public ControlChange History(int newestFirst) => _history[(_historyHead - 1 - newestFirst + HistoryCapacity * 2) % HistoryCapacity];

    public int PresentCount
    {
        get
        {
            int n = 0;
            foreach (var i in _items) if (i.Present) n++;
            return n;
        }
    }

    public int ChangesOnLap(int? lap)
    {
        if (lap is null) return 0;
        int n = 0;
        for (int i = 0; i < _historyCount; i++) if (History(i).Lap == lap) n++;
        return n;
    }

    public bool ShownAfterChange(double now, double hold) => LastChanged is not null && now - LastChangeAt >= 0 && now - LastChangeAt < hold;

    public void Clear()
    {
        _byKey.Clear();
        _items.Clear();
        _vars.Clear();
        _varsFrom = null;
        _historyHead = _historyCount = 0;
        LastChanged = null;
        LastChangeAt = double.NegativeInfinity;
        _driven = false;
    }

    public void Update(TelemetryFrame f, double now)
    {
        if (!ReferenceEquals(f.Vars, _varsFrom)) Scan(f.Vars);
        bool driving = f.IsOnTrack && !f.IsReplayPlaying;
        int? lap = f.Lap;
        Set(BrakeBiasKey, f.BrakeBias, driving, now, lap);
        Set(TractionKey, f.TractionControl, driving, now, lap);
        Set(AbsKey, f.Abs, driving, now, lap);
        Set(MapKey, f.EngineMap, driving, now, lap);
        foreach (var v in _vars) Set(v.Name, f.RawValue(v.Name), driving, now, lap);
    }

    private void Scan(IReadOnlyDictionary<string, IrsdkVar> vars)
    {
        _varsFrom = vars;
        _vars.Clear();
        bool mapIsMixture = !vars.ContainsKey("dcEngineMap");
        foreach (var v in vars.Values)
            if (Counts(v.Name, v.Type, v.Count) && !(mapIsMixture && v.Name == "dcFuelMixture")) _vars.Add(v);
        _vars.Sort((a, b) => Order(a.Name).CompareTo(Order(b.Name)) is var c && c != 0 ? c : string.CompareOrdinal(a.Name, b.Name));
    }

    private static int Order(string name) => name switch
    {
        "dcTractionControl2" => 0,
        "dcTractionControl3" => 1,
        "dcAntiRollFront" => 2,
        "dcAntiRollRear" => 3,
        "dcFuelMixture" => 4,
        "dcMGUKDeployMode" or "dcMGUKDeployFixed" => 5,
        "dcMGUKRegenGain" => 6,
        _ => 10,
    };

    internal static bool Counts(string name, IrsdkType type, int count)
    {
        if (!name.StartsWith("dc", StringComparison.Ordinal) || name.Length <= 2 || count != 1) return false;
        if (type is IrsdkType.Bool or IrsdkType.Char or IrsdkType.BitField) return false;
        if (name is BrakeBiasKey or TractionKey or AbsKey or "dcEngineMap") return false;
        foreach (var s in Skipped)
            if (name.Contains(s, StringComparison.Ordinal)) return false;
        return true;
    }

    internal void Set(string key, double? value, bool driving, double now, int? lap)
    {
        if (driving && value is not null) _driven = true;
        if (!_byKey.TryGetValue(key, out var item))
        {
            if (value is not { } first || !double.IsFinite(first)) return;
            var (label, name) = Names(key);
            item = new ControlItem(key, label, name) { Start = first, Value = first };
            _byKey[key] = item;
            _items.Add(item);
        }
        if (value is not { } v || !double.IsFinite(v))
        {
            item.Present = false;
            return;
        }
        if (!item.Present && !_driven) item.Start = v;
        item.Present = true;
        if (Math.Abs(v - item.Value) <= Same) return;
        if (!driving)
        {
            if (!_driven) item.Start = v;
            item.Value = v;
            return;
        }
        double from = item.Value;
        item.Previous = from;
        item.Value = v;
        item.ChangedAt = now;
        item.ChangedLap = lap;
        item.Changes++;
        LastChanged = item;
        LastChangeAt = now;
        _history[_historyHead] = new ControlChange(item, from, v, now, lap);
        _historyHead = (_historyHead + 1) % HistoryCapacity;
        if (_historyCount < HistoryCapacity) _historyCount++;
    }

    internal static (string Label, string Name) Names(string key)
    {
        if (Known.TryGetValue(key, out var known)) return known;
        string words = Words(key.StartsWith("dc", StringComparison.Ordinal) ? key[2..] : key);
        return (words, words);
    }

    internal static string Words(string camel)
    {
        var sb = new System.Text.StringBuilder(camel.Length + 8);
        for (int i = 0; i < camel.Length; i++)
        {
            char c = camel[i];
            if (c == '_') { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); continue; }
            bool boundary = i > 0 && char.IsUpper(c) && (char.IsLower(camel[i - 1]) || i + 1 < camel.Length && char.IsLower(camel[i + 1]) && char.IsUpper(camel[i - 1]))
                || i > 0 && char.IsDigit(c) && !char.IsDigit(camel[i - 1]);
            if (boundary && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            sb.Append(c);
        }
        string text = sb.ToString().Trim();
        if (text.Length == 0) return camel;
        var parts = text.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            string w = parts[i];
            bool acronym = w.Length > 1 && w.All(ch => char.IsUpper(ch) || char.IsDigit(ch));
            if (acronym) continue;
            parts[i] = i == 0 ? char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..] : char.ToLower(w[0], CultureInfo.InvariantCulture) + w[1..];
        }
        return string.Join(' ', parts);
    }

    public static string Format(double value)
    {
        double r = Math.Round(value, 2);
        return Math.Abs(r - Math.Round(r)) < 0.005
            ? PooledText.Number("", (long)Math.Round(r))
            : PooledText.Number("", r, "0.0#", CultureInfo.InvariantCulture);
    }

    public static string FormatStep(double step)
    {
        double a = Math.Abs(Math.Round(step, 2));
        string sign = step < 0 ? "−" : "+";
        return Math.Abs(a - Math.Round(a)) < 0.005 ? PooledText.Number(sign, (long)Math.Round(a)) : PooledText.Number(sign, a, "0.0#", CultureInfo.InvariantCulture);
    }
}
