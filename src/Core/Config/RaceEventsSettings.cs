namespace Core.Config;

public sealed class RaceEventsSettings
{
    public static readonly int[] Leads = { 0, 3, 5, 10, 15 };
    public static readonly string[] Kinds = { "pass", "off", "incident", "pit", "fastest" };

    public int Lead { get; set; } = 5;

    public bool CameraOnDriver { get; set; } = true;

    public bool FollowNew { get; set; } = true;

    public bool OnTop { get; set; }

    public bool OpenWhenLeavingCar { get; set; }

    public string? Hotkey { get; set; }

    public List<string> Rivals { get; set; } = new();

    public List<string> HiddenKinds { get; set; } = new();

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public bool Shows(string kind) => !HiddenKinds.Contains(kind);

    public void Normalize()
    {
        if (!Leads.Contains(Lead)) Lead = 5;
        Rivals = (Rivals ?? new()).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.Ordinal).Take(63).ToList();
        HiddenKinds = (HiddenKinds ?? new()).Where(k => Kinds.Contains(k)).Distinct().ToList();
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? null : Hotkey;
        if (Width is not (> 0 and < 20000)) Width = null;
        if (Height is not (> 0 and < 20000)) Height = null;
        if (Left is not { } left || !double.IsFinite(left)) Left = null;
        if (Top is not { } top || !double.IsFinite(top)) Top = null;
    }
}
