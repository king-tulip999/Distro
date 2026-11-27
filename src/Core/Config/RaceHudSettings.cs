namespace Core.Config;

public enum HudNightMode
{
    Off,
    Auto,
    On,
}

public enum HudNightColor
{
    Red,
    Blue,
    Purple,
    Orange,
}

public enum HudShowWhen
{
    Driving,
    Connected,
    Always,
}

public enum HudUnits { Metric, Imperial }

public enum HudDensity { Compact, Standard, Expanded }

public enum HudAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

public enum HudVrPlacement { Off, Cockpit, Head }


public sealed class RaceHudSettings
{
    public bool ShowPage { get; set; } = true;

    public const int CurrentVersion = 9;

    public int Version { get; set; }

    public bool Enabled { get; set; }

    public HudShowWhen ShowWhen { get; set; } = HudShowWhen.Driving;

    public bool Interactive { get; set; }

    public string ActiveLayout { get; set; } = SimRacing.Hud.HudPresets.DefaultName;

    [System.Text.Json.Serialization.JsonPropertyName("autoSwitch")]
    public bool? OldAutoSwitch { get; set; }

    public string? DefaultLayout { get; set; }

    public List<HudLayout> Layouts { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("rules")]
    public List<HudLayoutRule>? OldRules { get; set; }

    public HudAppearance Appearance { get; set; } = new();

    public HudUnits Units { get; set; } = HudUnits.Metric;

    public bool SpeedInMph { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public HudUnits SpeedUnits => SpeedInMph ? HudUnits.Imperial : Units;

    public bool Clock24h { get; set; }

    public bool HideAppOnConnect { get; set; }

    public bool WebRemote { get; set; }

    public const int DefaultWebPort = 8642, MinWebPort = 1024, MaxWebPort = 65535;

    public bool WebOverlays { get; set; }

    public bool VrOverlays { get; set; }

    public bool VrEditButton { get; set; }

    public bool VrEditorIntroDone { get; set; }

    public HudNightMode NightMode { get; set; } = HudNightMode.Off;

    public HudNightColor NightColor { get; set; } = HudNightColor.Red;

    public string? NightHotkey { get; set; }

    public const int MinVrBrightness = 20;

    public int VrBrightness { get; set; } = 100;

    public const double MaxVrShiftDistance = 1;

    public double VrShiftX { get; set; }

    public double VrShiftY { get; set; }

    public double VrShiftDistance { get; set; }

    public void ClampVrShift()
    {
        VrShiftX = double.IsFinite(VrShiftX) ? Math.Clamp(VrShiftX, -HudWidgetConfig.VrReach, HudWidgetConfig.VrReach) : 0;
        VrShiftY = double.IsFinite(VrShiftY) ? Math.Clamp(VrShiftY, -HudWidgetConfig.VrReach, HudWidgetConfig.VrReach) : 0;
        VrShiftDistance = double.IsFinite(VrShiftDistance) ? Math.Clamp(VrShiftDistance, -MaxVrShiftDistance, MaxVrShiftDistance) : 0;
    }

    public const double VrPanelDistance = 0.55, VrPanelReach = 0.4, DefaultVrPanelY = -0.2, DefaultVrPanelWidth = 0.28, MinVrPanelWidth = 0.16, MaxVrPanelWidth = 0.5;

    public double VrPanelX { get; set; }

    public double VrPanelY { get; set; } = DefaultVrPanelY;

    public double VrPanelWidth { get; set; } = DefaultVrPanelWidth;

    public void ClampVrPanel()
    {
        VrPanelX = double.IsFinite(VrPanelX) ? Math.Clamp(VrPanelX, -VrPanelReach, VrPanelReach) : 0;
        VrPanelY = double.IsFinite(VrPanelY) ? Math.Clamp(VrPanelY, -VrPanelReach, VrPanelReach) : DefaultVrPanelY;
        VrPanelWidth = double.IsFinite(VrPanelWidth) ? Math.Clamp(VrPanelWidth, MinVrPanelWidth, MaxVrPanelWidth) : DefaultVrPanelWidth;
    }

    public int WebPort { get; set; } = DefaultWebPort;

    public const int MinGrid = 2, MaxGrid = 64, GridStep = 2;

    public int GridSize { get; set; } = 12;

    public bool Snap { get; set; } = true;

    public bool GridShown { get; set; }

    public HudFuelSettings Fuel { get; set; } = new();

    public Dictionary<string, HudAlertSetting> Alerts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> AlertGroupOrder { get; set; } = new();

    public List<string> AlertGroupsFolded { get; set; } = new();

    public List<string> FoldedPanels { get; set; } = new();

    public List<TrackNote> Notes { get; set; } = new();

    public const int MaxNotes = 500, NoteLength = 160;

    public IEnumerable<TrackNote> NotesFor(string track, string config, string car) =>
        Notes.Where(n => n.Matches(track, config, car));

    public List<string> KnownClasses { get; set; } = new();
    public List<string> KnownCars { get; set; } = new();
    public List<string> KnownTracks { get; set; } = new();
    public Dictionary<int, string> ClassNames { get; set; } = new();
    public bool ClassNamesRead { get; set; }

    public const int MaxKnown = 200;

    public bool Remember(string? carClass, string? car, string? track)
    {
        static bool Add(List<string> list, string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || list.Count >= MaxKnown || list.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)) return false;
            list.Add(name.Trim());
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return true;
        }
        bool a = Add(KnownClasses, carClass), b = Add(KnownCars, car), c = Add(KnownTracks, track);
        return a || b || c;
    }

    public static string SessionKind(string session) => string.Equals(session, "Warmup", StringComparison.OrdinalIgnoreCase) ? "Practice" : session;

    public static readonly string[] RemovedKinds = { "blindspot", "spotter", "h2h", "pit", "flags", "dash", "trackmap", "radio", "spotterbox", "goals", "result" };
    public static readonly string[] RemovedAlerts = { "goal.done" };

    public string? ToggleHotkey { get; set; }
    public string? EditHotkey { get; set; }
    public string? NextLayoutHotkey { get; set; }
    public string? PreviousLayoutHotkey { get; set; }
    public string? DismissHotkey { get; set; }
    public string? QuickEditHotkey { get; set; } = QuickEditDefault;
    public const string QuickEditDefault = "Ctrl+Alt+E";
    public string? VrAdjustHotkey { get; set; } = VrAdjustDefault;
    public string? VrRecenterHotkey { get; set; } = VrRecenterDefault;
    public string? VrNextHotkey { get; set; } = VrNextDefault;
    public string? VrSettingHotkey { get; set; } = VrSettingDefault;
    public string? VrLeftHotkey { get; set; } = VrLeftDefault;
    public string? VrRightHotkey { get; set; } = VrRightDefault;
    public string? VrUpHotkey { get; set; } = VrUpDefault;
    public string? VrDownHotkey { get; set; } = VrDownDefault;
    public string? VrBrighterHotkey { get; set; }
    public string? VrDimmerHotkey { get; set; }
    public const string VrAdjustDefault = "Ctrl+Alt+V", VrRecenterDefault = "Ctrl+Alt+C", VrNextDefault = "Ctrl+Alt+N", VrSettingDefault = "Ctrl+Alt+M",
        VrLeftDefault = "Ctrl+Alt+Left", VrRightDefault = "Ctrl+Alt+Right", VrUpDefault = "Ctrl+Alt+Up", VrDownDefault = "Ctrl+Alt+Down";

    public HudLayout? Layout(string? name) => Layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public HudLayout? Resolve(string car, string carClass) =>
        LayoutForCar(car) ?? LayoutForClass(car, carClass)?.Layout ?? Fallback;

    [System.Text.Json.Serialization.JsonIgnore]
    public HudLayout? Fallback => NoDefault ? null : Layout(DefaultLayout) ?? Layout(ActiveLayout) ?? Layouts.FirstOrDefault();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool NoDefault => DefaultLayout is { Length: 0 };

    public HudLayout? LayoutForCar(string car)
    {
        car = car.Trim();
        if (car.Length == 0) return null;
        HudLayout? best = null;
        int bestScore = -1;
        foreach (var layout in Layouts)
            foreach (var want in layout.Cars)
            {
                if (string.IsNullOrWhiteSpace(want) || !car.Contains(want.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                int score = want.Trim().Length * 2 + (string.Equals(want.Trim(), car, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                if (score > bestScore) { best = layout; bestScore = score; }
            }
        return best;
    }

    public (HudLayout Layout, string Class)? LayoutForClass(string car, string carClass)
    {
        foreach (var layout in Layouts)
            foreach (var want in layout.Classes)
                if (carClass.Trim().Length > 0 && SimRacing.Race.SessionNames.ClassMatches(carClass.Trim(), want)) return (layout, want);
        if (car.Trim().Length == 0) return null;
        string guessed = SimRacing.Setups.SetupCars.ClassOfName(car);
        foreach (var layout in Layouts)
            foreach (var want in layout.Classes)
                if (string.Equals(want, guessed, StringComparison.OrdinalIgnoreCase)) return (layout, want);
        return null;
    }

    public HudLayout? ClassOwner(string carClass) =>
        Layouts.FirstOrDefault(l => l.Classes.Contains(carClass.Trim(), StringComparer.OrdinalIgnoreCase));

    public void AssignClass(string layoutName, string carClass)
    {
        carClass = carClass.Trim();
        if (carClass.Length == 0 || Layout(layoutName) is not { } layout) return;
        foreach (var l in Layouts) l.Classes.RemoveAll(c => string.Equals(c, carClass, StringComparison.OrdinalIgnoreCase));
        layout.Classes.Add(carClass);
        StopBeingDefault(layout);
    }

    public HudLayout? CarOwner(string car) =>
        Layouts.FirstOrDefault(l => l.Cars.Contains(car.Trim(), StringComparer.OrdinalIgnoreCase));

    public void AssignCar(string layoutName, string car)
    {
        car = car.Trim();
        if (car.Length == 0 || Layout(layoutName) is not { } layout) return;
        foreach (var l in Layouts) l.Cars.RemoveAll(c => string.Equals(c, car, StringComparison.OrdinalIgnoreCase));
        layout.Cars.Add(car);
        StopBeingDefault(layout);
    }

    private void StopBeingDefault(HudLayout layout)
    {
        if (string.Equals(DefaultLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) DefaultLayout = "";
    }

    public bool RenameLayout(string from, string to)
    {
        to = to.Trim();
        var layout = Layout(from);
        if (layout is null || to.Length == 0 || IsDefaultName(layout.Name) || IsDefaultName(to)
            || Layouts.Any(l => l != layout && string.Equals(l.Name, to, StringComparison.OrdinalIgnoreCase))) return false;
        if (string.Equals(ActiveLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) ActiveLayout = to;
        if (string.Equals(DefaultLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) DefaultLayout = to;
        layout.Name = to;
        return true;
    }

    public bool DeleteLayout(string name)
    {
        var layout = Layout(name);
        if (layout is null || Layouts.Count <= 1 || IsDefaultName(layout.Name)) return false;
        Layouts.Remove(layout);
        if (string.Equals(ActiveLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) ActiveLayout = Layouts[0].Name;
        if (string.Equals(DefaultLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) DefaultLayout = ActiveLayout;
        return true;
    }

    public static bool IsDefaultName(string name) => string.Equals(name, SimRacing.Hud.HudPresets.DefaultName, StringComparison.OrdinalIgnoreCase);

    public string? MarkCustomized(Func<HudLayout, bool> isDefault, Func<HudLayout> preset)
    {
        var layout = Layouts.FirstOrDefault(l => IsDefaultName(l.Name));
        if (layout is null || isDefault(layout)) return null;
        var custom = new HudLayout { Name = UniqueName(SimRacing.Hud.HudPresets.CustomName), Widgets = layout.Widgets, Cars = layout.Cars, Classes = layout.Classes };
        layout.Widgets = preset().Widgets.Select(w => w.Copy()).ToList();
        layout.Cars = new();
        layout.Classes = new();
        foreach (var w in layout.Widgets) w.Clamp();
        Layouts.Insert(Layouts.IndexOf(layout) + 1, custom);
        if (IsDefaultName(ActiveLayout)) ActiveLayout = custom.Name;
        if (DefaultLayout is not null && IsDefaultName(DefaultLayout)) DefaultLayout = custom.Name;
        return custom.Name;
    }

    public string ResetToDefault(string name, HudLayout preset)
    {
        var layout = Layout(name);
        if (layout is null) return name;
        layout.Widgets = preset.Widgets.Select(w => w.Copy()).ToList();
        foreach (var w in layout.Widgets) w.Clamp();
        return layout.Name;
    }

    public string UniqueName(string wanted)
    {
        wanted = string.IsNullOrWhiteSpace(wanted) ? "Layout" : wanted.Trim();
        string name = Fit(wanted, "");
        for (int n = 2; Layout(name) is not null; n++) name = Fit(wanted, $" {n}");
        return name;
    }

    private static string Fit(string name, string suffix) =>
        (name.Length + suffix.Length > Views.Ui.NameLength ? name[..(Views.Ui.NameLength - suffix.Length)].TrimEnd() : name) + suffix;

    public void Normalize(Func<List<HudLayout>> defaults)
    {
        Layouts ??= new();
        if (Version < 1 && Layouts.Count == 0) Layouts.AddRange(defaults());
        bool before2 = Version < 2, before3 = Version < 3, before4 = Version < 4, before5 = Version < 5, before6 = Version < 6, before7 = Version < 7, before8 = Version < 8;
        Version = CurrentVersion;
        Appearance ??= new();
        if (before4) { Appearance.Colors = HudAppearance.DefaultColors; Appearance.Custom = new(); }
        if (before5 && Appearance.Corner == 0) Appearance.Corner = HudAppearance.DefaultCorner;
        if (WebPort is < MinWebPort or > MaxWebPort) WebPort = DefaultWebPort;
        ClampVrShift();
        ClampVrPanel();
        VrBrightness = Math.Clamp(VrBrightness, MinVrBrightness, 100);
        var builtIn = defaults();
        if (!Layouts.Any(l => l is not null && IsDefaultName(l.Name ?? ""))) Layouts.Add(builtIn[0]);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in Layouts.ToList())
        {
            layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "Layout" : layout.Name.Trim();
            if (!names.Add(layout.Name)) { layout.Name = UniqueNameAmong(names, layout.Name); names.Add(layout.Name); }
            layout.Widgets ??= new();
            layout.Widgets.RemoveAll(w => w is null || RemovedKinds.Contains(w.Kind, StringComparer.OrdinalIgnoreCase));
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in layout.Widgets)
            {
                w.Kind ??= "";
                if (string.IsNullOrWhiteSpace(w.Id) || !ids.Add(w.Id)) { w.Id = HudWidgetConfig.NewId(w.Kind); ids.Add(w.Id); }
                SideBannerToTop(w);
                OldInputStyles(w);
                OldWeatherStrip(w);
                if (before6) InputsShowGear(w);
                if (before7) SwitchToNewLooks(w);
                if (before2) ShowClassesInsteadOfHiding(w);
                if (before3 || before8 && string.Equals(w.Kind, "tyres", StringComparison.OrdinalIgnoreCase)) ScaleFromNaturalSize(w);
                w.Clamp();
            }
        }
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in Layouts)
        {
            layout.Cars = (layout.Cars ?? new()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
            layout.Cars.RemoveAll(c => !assigned.Add(c));
            layout.Classes = (layout.Classes ?? new()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
            layout.Classes.RemoveAll(c => !assignedClasses.Add(c));
        }
        if (OldAutoSwitch == true) CarRulesToLayouts(assigned, assignedClasses);
        OldRules = null;
        OldAutoSwitch = null;
        AlertGroupOrder = (AlertGroupOrder ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AlertGroupsFolded = (AlertGroupsFolded ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        FoldedPanels = (FoldedPanels ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Layout(ActiveLayout) is null) ActiveLayout = Layouts[0].Name;
        if (!NoDefault && (Layout(DefaultLayout) ?? Layout(ActiveLayout)!) is { } fallback)
        {
            DefaultLayout = fallback.Name;
            fallback.Cars.Clear();
            fallback.Classes.Clear();
        }
        Appearance ??= new();
        Appearance.Clamp();
        Fuel ??= new();
        Fuel.Clamp();
        GridSize = Math.Clamp(GridSize, MinGrid, MaxGrid);
        var alerts = new Dictionary<string, HudAlertSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, a) in Alerts ?? new()) if (id is not null && a is not null && !RemovedAlerts.Contains(id, StringComparer.OrdinalIgnoreCase)) alerts[id] = a;
        Alerts = alerts;
        static List<string> Clean(List<string>? list) =>
            (list ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(MaxKnown).ToList();
        KnownClasses = Clean(KnownClasses);
        KnownCars = Clean(KnownCars);
        KnownTracks = Clean(KnownTracks);
        ClassNames = (ClassNames ?? new()).Where(c => c.Key > 0 && !string.IsNullOrWhiteSpace(c.Value)).ToDictionary(c => c.Key, c => c.Value.Trim());
        var noteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Notes = (Notes ?? new()).Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Track) && !string.IsNullOrWhiteSpace(n.Text)).Take(MaxNotes).ToList();
        foreach (var n in Notes)
        {
            n.Clean();
            if (string.IsNullOrWhiteSpace(n.Id) || !noteIds.Add(n.Id)) { n.Id = Guid.NewGuid().ToString("N"); noteIds.Add(n.Id); }
        }
    }

    private void CarRulesToLayouts(HashSet<string> assigned, HashSet<string> assignedClasses)
    {
        foreach (var rule in OldRules ?? new())
        {
            if (rule is null || Layout(rule.Layout) is not { } layout || !string.IsNullOrWhiteSpace(rule.Track) || !string.IsNullOrWhiteSpace(rule.Session)) continue;
            if (!string.IsNullOrWhiteSpace(rule.Car))
            {
                if (assigned.Add(rule.Car.Trim())) layout.Cars.Add(rule.Car.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(rule.Class) && assignedClasses.Add(rule.Class.Trim())) layout.Classes.Add(rule.Class.Trim());
        }
    }

    internal void ShowClassesInsteadOfHiding(HudWidgetConfig w) =>
        SimRacing.Hud.Editors.HudClassFilter.Migrate(w, (KnownClasses ?? new()).Concat(SimRacing.Race.SessionNames.Classes));

    internal static void ScaleFromNaturalSize(HudWidgetConfig w)
    {
        if (SimRacing.Hud.HudCatalog.Find(w.Kind) is not { Natural: { } natural } def || w.Options is null) return;
        var size = natural(new SimRacing.Hud.HudOptions(def, w));
        if (size.Height > 0 && double.IsFinite(w.H)) w.Scale = w.H / size.Height;
    }

    internal static void InputsShowGear(HudWidgetConfig w)
    {
        if (!string.Equals(w.Kind, "inputs", StringComparison.OrdinalIgnoreCase) || w.Options is null || !w.Options.TryGetValue("show", out var show) || show is null) return;
        if (!show.Split(',').Contains("gear")) w.Options["show"] = show.Length == 0 ? "gear" : show + ",gear";
    }

    internal static void OldWeatherStrip(HudWidgetConfig w)
    {
        if (!string.Equals(w.Kind, "weather", StringComparison.OrdinalIgnoreCase) || w.H >= w.W) return;
        if (w.Options is not null && w.Options.ContainsKey("look")) return;
        double factor = w.H / 124;
        w.W = Math.Max(1, Math.Round(SimRacing.Hud.Widgets.WeatherWidget.PanelW * factor));
        w.H = Math.Max(1, Math.Round(SimRacing.Hud.Widgets.WeatherWidget.PanelH * factor));
    }

    internal static void SwitchToNewLooks(HudWidgetConfig w)
    {
        if (w.Options is null) return;
        if (string.Equals(w.Kind, "weather", StringComparison.OrdinalIgnoreCase) && w.Options.TryGetValue("look", out var look) && look != SimRacing.Hud.Widgets.WeatherWidget.PanelLook)
        {
            w.Options.Remove("look");
            double factor = w.H / 124;
            w.W = Math.Max(1, Math.Round(SimRacing.Hud.Widgets.WeatherWidget.PanelW * factor));
            w.H = Math.Max(1, Math.Round(SimRacing.Hud.Widgets.WeatherWidget.PanelH * factor));
        }
        else if (string.Equals(w.Kind, "weather", StringComparison.OrdinalIgnoreCase) && w.W > 0 && Math.Abs(w.H / w.W - 400.0 / 230) < 0.02)
            w.H = Math.Max(1, Math.Round(w.W * SimRacing.Hud.Widgets.WeatherWidget.PanelH / SimRacing.Hud.Widgets.WeatherWidget.PanelW));
        if (string.Equals(w.Kind, "inputs", StringComparison.OrdinalIgnoreCase) && w.Options.TryGetValue("style", out var style) && style != SimRacing.Hud.Widgets.InputsWidget.Dial
            && SimRacing.Hud.HudCatalog.Find(w.Kind) is { } def)
        {
            var before = SimRacing.Hud.Widgets.InputsWidget.Natural(new SimRacing.Hud.HudOptions(def, w));
            w.Options.Remove("style");
            var after = SimRacing.Hud.Widgets.InputsWidget.Natural(new SimRacing.Hud.HudOptions(def, w));
            if (before.Height <= 0 || after.Height <= 0 || !double.IsFinite(w.H)) return;
            double factor = w.H / before.Height;
            w.W = Math.Max(1, Math.Round(after.Width * factor));
            w.H = Math.Max(1, Math.Round(after.Height * factor));
        }
    }

    internal static void OldInputStyles(HudWidgetConfig w)
    {
        if (!string.Equals(w.Kind, "inputs", StringComparison.OrdinalIgnoreCase) || w.Options is null || !w.Options.TryGetValue("style", out var style)) return;
        if (style == "tach")
        {
            w.Options.Remove("style");
            double factor = w.W / 188;
            w.W = Math.Max(1, Math.Round(420 * factor));
            w.H = Math.Max(1, Math.Round(110 * factor));
        }
        else if (style is "strip" or "lights" or "trace" && SimRacing.Hud.HudCatalog.Find(w.Kind) is { } def)
        {
            double oldH = style switch { "strip" => 50, "lights" => 66, _ => SimRacing.Hud.Widgets.InputsWidget.Parse(new SimRacing.Hud.HudOptions(def, w)).Rpm ? 110 : 100 };
            w.Options.Remove("style");
            var after = SimRacing.Hud.Widgets.InputsWidget.Natural(new SimRacing.Hud.HudOptions(def, w));
            if (!double.IsFinite(w.H) || w.H <= 0) return;
            double factor = w.H / oldH;
            w.W = Math.Max(1, Math.Round(after.Width * factor));
            w.H = Math.Max(1, Math.Round(after.Height * factor));
        }
    }

    internal static void SideBannerToTop(HudWidgetConfig w)
    {
        if (w.Options is null || !w.Options.TryGetValue(SimRacing.Hud.HudFlags.BannerOption.Key, out var where) || where != SimRacing.Hud.HudFlags.OldSide) return;
        var old = SimRacing.Hud.HudFlags.OldSideRoom;
        var (l, t, r, b) = SimRacing.Hud.HudFlags.Room("top");
        w.Options[SimRacing.Hud.HudFlags.BannerOption.Key] = "top";
        w.W = Math.Max(1, w.W + l + r - old.L - old.R);
        w.H = Math.Max(1, w.H + t + b - old.T - old.B);
    }

    private static string UniqueNameAmong(HashSet<string> taken, string wanted)
    {
        string name = wanted;
        for (int n = 2; taken.Contains(name); n++) name = $"{wanted} {n}";
        return name;
    }
}

public sealed class HudLayout
{
    public string Name { get; set; } = "Layout";
    public List<HudWidgetConfig> Widgets { get; set; } = new();
    public List<string> Cars { get; set; } = new();
    public List<string> Classes { get; set; } = new();

    public HudLayout Copy(string name) => new()
    {
        Name = name,
        Widgets = Widgets.Select(w => w.Copy()).ToList(),
    };
}

public sealed class HudWidgetConfig
{
    public const double MinSize = 30, MaxSize = 4000;

    public string Id { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Monitor { get; set; } = "primary";

    public HudAnchor Anchor { get; set; } = HudAnchor.TopLeft;

    public double X { get; set; }
    public double Y { get; set; }

    public double W { get; set; } = 300;
    public double H { get; set; } = 200;

    public const double MinScale = 0.5, MaxScale = 3;

    public double Scale { get; set; } = 1;

    public double ScreenHeight { get; set; }

    public bool Locked { get; set; }

    public int Opacity { get; set; } = 100;

    public int UpdateHz { get; set; }

    public List<string> Sessions { get; set; } = new();

    public bool HideInReplay { get; set; } = true;

    public bool? OnlyWhenNeeded { get; set; }

    public bool OnlyOnTrack { get; set; }

    public int? Background { get; set; }

    public string? Hotkey { get; set; }

    public const double VrReach = 2, MinVrDistance = 0.2, MaxVrDistance = 3, MinVrWidth = 0.05, MaxVrWidth = 2, MinVrHeight = 0.02, MaxVrHeight = 2, MaxVrAngle = 80;

    public HudVrPlacement Vr { get; set; }

    public double VrX { get; set; }

    public double VrY { get; set; }

    public double VrDistance { get; set; } = 1;

    public double VrWidth { get; set; }

    public double VrHeight { get; set; }

    public double VrTurn { get; set; }

    public double VrTilt { get; set; }

    public bool VrCurve { get; set; }

    public bool VrOnly { get; set; }

    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string NewId(string kind) => $"{(string.IsNullOrEmpty(kind) ? "widget" : kind)}-{Guid.NewGuid().ToString("N")[..6]}";

    public void Clamp()
    {
        W = double.IsFinite(W) ? Math.Clamp(W, MinSize, MaxSize) : 300;
        H = double.IsFinite(H) ? Math.Clamp(H, MinSize, MaxSize) : 200;
        X = double.IsFinite(X) ? Math.Clamp(X, -MaxSize, MaxSize) : 0;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, -MaxSize, MaxSize) : 0;
        ScreenHeight = double.IsFinite(ScreenHeight) && ScreenHeight > 0 ? Math.Clamp(ScreenHeight, MinSize, MaxSize) : 0;
        Scale = double.IsFinite(Scale) && Scale > 0 ? Math.Round(Math.Clamp(Scale, MinScale, MaxScale), 3) : 1;
        Opacity = Math.Clamp(Opacity, 20, 100);
        if (Background is { } bg) Background = Math.Clamp(bg, 0, 100);
        UpdateHz = Math.Clamp(UpdateHz, 0, 60);
        Monitor = string.IsNullOrWhiteSpace(Monitor) ? "primary" : Monitor;
        if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = null;
        if (!Enum.IsDefined(Vr)) Vr = HudVrPlacement.Off;
        VrX = double.IsFinite(VrX) ? Math.Clamp(VrX, -VrReach, VrReach) : 0;
        VrY = double.IsFinite(VrY) ? Math.Clamp(VrY, -VrReach, VrReach) : 0;
        VrDistance = double.IsFinite(VrDistance) ? Math.Clamp(VrDistance, MinVrDistance, MaxVrDistance) : 1;
        VrWidth = double.IsFinite(VrWidth) && VrWidth > 0 ? Math.Clamp(VrWidth, MinVrWidth, MaxVrWidth) : 0;
        VrHeight = double.IsFinite(VrHeight) && VrHeight > 0 ? Math.Clamp(VrHeight, MinVrHeight, MaxVrHeight) : 0;
        VrTurn = double.IsFinite(VrTurn) ? Math.Clamp(VrTurn, -MaxVrAngle, MaxVrAngle) : 0;
        VrTilt = double.IsFinite(VrTilt) ? Math.Clamp(VrTilt, -MaxVrAngle, MaxVrAngle) : 0;
        Sessions = (Sessions ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(RaceHudSettings.SessionKind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Options ?? new()) if (k is not null && v is not null) options[k] = v;
        Options = options;
    }

    public HudWidgetConfig Copy()
    {
        var copy = (HudWidgetConfig)MemberwiseClone();
        copy.Hotkey = null;
        copy.Sessions = new List<string>(Sessions);
        copy.Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    public bool ShowsIn(string session)
    {
        if (Sessions.Count == 0) return true;
        foreach (var s in Sessions)
            if (string.Equals(s, session, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

public sealed class TrackNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Track { get; set; } = "";
    public string Config { get; set; } = "";
    public string? Car { get; set; }
    public double? Pct { get; set; }
    public string? Turn { get; set; }
    public string Text { get; set; } = "";

    public bool Matches(string track, string config, string car) =>
        string.Equals(Track, track.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(Config, config.Trim(), StringComparison.OrdinalIgnoreCase)
        && (Car is null || string.Equals(Car, car.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Clean()
    {
        Track = (Track ?? "").Trim();
        Config = (Config ?? "").Trim();
        Car = string.IsNullOrWhiteSpace(Car) ? null : Car.Trim();
        Text = (Text ?? "").Trim();
        if (Text.Length > RaceHudSettings.NoteLength) Text = Text[..RaceHudSettings.NoteLength].TrimEnd();
        Pct = Pct is { } p && double.IsFinite(p) ? p - Math.Floor(p) : null;
        Turn = Pct is null || string.IsNullOrWhiteSpace(Turn) ? null : Turn.Trim();
    }
}

public sealed class HudLayoutRule
{
    public string Layout { get; set; } = "";
    public string? Session { get; set; }
    public string? Car { get; set; }
    public string? Track { get; set; }
    public string? Class { get; set; }
}

public sealed class HudAppearance
{
    public const string DefaultColors = "Default", AppColors = "App", CustomColors = "Custom";

    public string Colors { get; set; } = DefaultColors;

    public ThemeColors Custom { get; set; } = new();

    public int Background { get; set; } = 75;

    public int FontScale { get; set; } = 100;

    public HudDensity Density { get; set; } = HudDensity.Compact;

    public const int DefaultCorner = 8;

    public int Corner { get; set; } = DefaultCorner;

    public bool ReducedMotion { get; set; }

    public bool Outline { get; set; } = false;

    public void Clamp()
    {
        Colors = Colors?.Trim() switch
        {
            null or "" => DefaultColors,
            var c when string.Equals(c, DefaultColors, StringComparison.OrdinalIgnoreCase) => DefaultColors,
            var c when string.Equals(c, AppColors, StringComparison.OrdinalIgnoreCase) => AppColors,
            var c when string.Equals(c, CustomColors, StringComparison.OrdinalIgnoreCase) => CustomColors,
            var c => SimRacing.Hud.HudThemes.Known(c) ?? DefaultColors,
        };
        Custom = SimRacing.Hud.HudThemes.Cleaned(Custom);
        Background = Math.Clamp(Background, 0, 100);
        FontScale = 100;
        Density = HudDensity.Compact;
        ReducedMotion = false;
        Corner = Math.Clamp(Corner, 0, 24);
    }
}

public enum FuelSafety { Aggressive, Balanced, Conservative }

public sealed class HudFuelSettings
{
    public int Window { get; set; } = 5;

    public FuelSafety Safety { get; set; } = FuelSafety.Balanced;

    public double MarginLaps { get; set; } = 0.5;

    public double ReserveLiters { get; set; }

    public double ExtraPercent { get; set; }

    public double ExtraKm { get; set; }

    public double MaxStintMinutes { get; set; }

    public double PitLaneLossSeconds { get; set; } = 25;

    public void Clamp()
    {
        Window = Math.Clamp(Window, 1, 30);
        MarginLaps = double.IsFinite(MarginLaps) ? Math.Clamp(MarginLaps, 0, 10) : 0.5;
        ReserveLiters = double.IsFinite(ReserveLiters) ? Math.Clamp(ReserveLiters, 0, 50) : 0;
        ExtraPercent = double.IsFinite(ExtraPercent) ? Math.Clamp(ExtraPercent, 0, 25) : 0;
        ExtraKm = double.IsFinite(ExtraKm) ? Math.Clamp(ExtraKm, 0, 50) : 0;
        MaxStintMinutes = double.IsFinite(MaxStintMinutes) ? Math.Clamp(MaxStintMinutes, 0, 600) : 0;
        PitLaneLossSeconds = double.IsFinite(PitLaneLossSeconds) ? Math.Clamp(PitLaneLossSeconds, 0, 300) : 25;
    }
}

public sealed class HudAlertSetting
{
    public bool? Enabled { get; set; }
    public double? Cooldown { get; set; }
    public double? Threshold { get; set; }
    public double? Duration { get; set; }
}
