using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Core.Config;
using Core.Controls;
using Core.Services;
using Core.Theme;

namespace Core.Views;

internal sealed class AppThemeSource : ThemeSource
{
    private const string GlowId = "glow", CustomId = "custom", PresetPrefix = "preset:", TeamPrefix = "team:", OtherPrefix = "theme:";

    private readonly AppServices _app;
    private readonly List<ThemeEntry> _entries = new();
    private readonly Dictionary<string, ThemeSpec> _specs = new();
    private string? _accentAtStart;
    private string? _accent;

    public AppThemeSource(AppServices app)
    {
        _app = app;
        _accent = _accentAtStart = app.Config.Ui.Accent;
        Add(GlowId, Themes.AppGlow.Name, ThemeEntry.BuiltIn, null, Themes.AppGlow);
        Add(CustomId, Themes.DefaultCustomName, ThemeEntry.BuiltIn, null, Themes.Custom);
        foreach (var other in Themes.All.Where(t => !AppIdentity.IsTecnica && t.Id != Themes.AppGlow.Id && t.Id != AppTheme.Custom && ThemeSets.For(t.Id) is null))
            Add(OtherPrefix + other.Id.Id, other.Name, ThemeEntry.BuiltIn, null, other);
        foreach (var family in CustomPalette.Families)
            foreach (var (name, _, _) in CustomPalette.Presets.Where(p => p.Family == family))
                Add(PresetPrefix + name, name, family, ThemeEntry.Themes, Themes.ForSet(AppTheme.Custom, name));
        var teams = ThemeSets.Teams;
        foreach (var series in teams.Families)
            foreach (var (name, _, _) in teams.Presets.Where(p => p.Family == series))
                Add(TeamPrefix + name, name, series, ThemeEntry.Teams, Themes.ForSet(teams.Id, name));
    }

    private void Add(string id, string name, string group, string? part, ThemeSpec spec)
    {
        if (_specs.ContainsKey(id)) return;
        _specs[id] = spec;
        _entries.Add(new ThemeEntry(id, name, group, part, ThemeManager.Resolve(spec)["Accent"]));
    }

    public override string Title => "Theme";

    public override IReadOnlyList<ThemeEntry> Entries => _entries;

    public override string Applied => AppliedId(_app.Config.Ui);

    private string AppliedId(UiSettings ui)
    {
        if (ui.Theme == AppTheme.Glow) return GlowId;
        if (ui.Theme == AppTheme.Custom)
            return ui.OledCustom.InUse && ui.OledCustom.Name is { } name && _specs.ContainsKey(PresetPrefix + name) ? PresetPrefix + name : CustomId;
        if (ThemeSets.For(ui.Theme) is { } set) return TeamPrefix + set.Current(ui);
        return OtherPrefix + ui.Theme.Id;
    }

    public static Button Face(AppServices app, Action applied)
    {
        var source = new AppThemeSource(app);
        var entry = source._entries.FirstOrDefault(e => e.Id == source.Applied) ?? source._entries[0];
        var c = ThemeManager.Resolve(source.SpecOf(entry));
        return ThemeWindow.Face(entry.Name, entry.Part is null ? null : entry.GroupLabel, new[] { c["Bg"], c["Accent"], c["Heading"] },
            button => _ = ThemeWindow.Open(button, new AppThemeSource(app), applied));
    }

    private ThemeSpec SpecOf(ThemeEntry entry)
    {
        var spec = _specs[entry.Id];
        if (entry.Id != GlowId || _accent is null) return spec;
        return GlowAccent.Apply(spec.Colors, _accent).Colors is { } colors ? spec with { Colors = colors } : spec;
    }

    public override FrameworkElement Thumb(ThemeEntry entry, double width, double height, double pixels)
    {
        var c = ThemeManager.Resolve(SpecOf(entry));
        Brush B(string key) => ThemeHues.Solid(c.TryGetValue(key, out var v) ? v : Colors.Gray);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (key, w) in new[] { ("Heading", 34.0), ("AccentText", 24.0), ("TextMuted", 24.0) })
            nav.Children.Add(new Border { Width = w, Height = 4, CornerRadius = ThemeManager.Round(2), Background = B(key), Margin = new Thickness(0, 0, 6, 0) });
        var lap = new Grid();
        lap.Children.Add(new TextBlock { Text = ThemePreview.Sample.Label, FontSize = 10, Foreground = B("TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        lap.Children.Add(new TextBlock { Text = ThemePreview.Sample.Value, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B("Heading"), HorizontalAlignment = HorizontalAlignment.Right });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        buttons.Children.Add(Pill("Apply", B("Accent"), B("Accent"), B("OnAccent")));
        buttons.Children.Add(Pill("Cancel", Brushes.Transparent, B("InputEdge"), B("Heading")));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var key in new[] { "SageDeep", "WarnDeep", "DangerDeep" })
            chips.Children.Add(new Border { Width = 22, Height = 6, CornerRadius = ThemeManager.Round(3), Background = B(key), Margin = new Thickness(0, 0, 5, 0) });
        var card = new Border
        {
            Background = B("Surface"), BorderBrush = B("Line"), BorderThickness = Hairline.Edges(), CornerRadius = ThemeManager.Round(6), Padding = new Thickness(8, 6, 8, 7),
            Child = new StackPanel { Children = { lap, buttons, chips } },
        };
        return new Border
        {
            Height = height, Background = ThemeHues.Glow(c["Bg"], c.TryGetValue("BgGlow", out var glow) ? glow : c["Bg"]), Padding = new Thickness(10, 9, 10, 8),
            Child = new StackPanel { Children = { nav, card } },
        };
    }

    private static Border Pill(string text, Brush fill, Brush edge, Brush ink) => new()
    {
        Background = fill, BorderBrush = edge, BorderThickness = Hairline.Edges(), CornerRadius = ThemeManager.Round(4), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 5, 0),
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ink },
    };

    public override FrameworkElement Preview(ThemeEntry entry, double width, double pixels)
    {
        var spec = SpecOf(entry) with { Variant = null, Name = entry.Name };
        return new StackPanel { Children = { ThemePreview.For(spec, width), Sample(spec, width) } };
    }

    private static FrameworkElement Sample(ThemeSpec spec, double width)
    {
        var resources = ThemeManager.Build(spec);
        using var scope = Ui.Scope(resources);
        var rows = new StackPanel();
        rows.Children.Add(Ui.SettingRow("Close in background", null, Ui.Switch(true, _ => { })));
        var sample = ThemePreview.Sample;
        rows.Children.Add(Ui.SettingRow(sample.Setting, null, Ui.Segmented(sample.Choices, 0, _ => { }, "theme-sample")));
        for (int i = 0; i < sample.Rows.Length; i++)
        {
            var (track, time) = sample.Rows[i];
            bool picked = i == 0;
            var line = new Grid();
            line.Children.Add(new TextBlock { Text = track, FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.Res<Brush>(picked ? "Heading" : "Text") });
            line.Children.Add(new TextBlock { Text = time, Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            rows.Children.Add(new Border
            {
                Child = line, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 4, 0, 0),
                BorderThickness = new Thickness(picked ? 3 : 0, 0, 0, 0), BorderBrush = Ui.Res<Brush>("Accent"), Background = picked ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent,
            });
        }
        return new Tile
        {
            Resources = resources, Width = width, Margin = new Thickness(0, 12, 0, 0), IsHitTestVisible = false, Padding = new Thickness(12, 6, 12, 10),
            Background = Ui.Res<Brush>("Surface"), BorderBrush = Ui.Res<Brush>("LineStrong"), BorderThickness = Hairline.Edges(), CornerRadius = ThemeManager.Round(8),
            Child = rows,
        };
    }

    public override FrameworkElement? Options(ThemeEntry entry, Action changed)
    {
        if (entry.Id != GlowId) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.SubHeader("Accent", first: true));
        string original = Palettes.ToHex(Themes.AppGlow.Colors["Accent"]);
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var buttons = new List<(Button Button, string Hex)>();
        foreach (var (name, hex) in GlowAccent.Swatches.OrderBy(s => GlowAccent.SameAs(s.Hex, original) ? 0 : 1))
        {
            var dot = new Ellipse { Width = 12, Height = 12, Fill = ThemeHues.Solid(CustomPalette.Parse(hex)!.Value), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } } }, Margin = new Thickness(0, 0, 6, 6) };
            System.Windows.Automation.AutomationProperties.SetName(button, name + " accent");
            buttons.Add((button, hex));
            button.Click += (_, _) =>
            {
                _accent = GlowAccent.SameAs(hex, original) ? null : hex;
                foreach (var (b, h) in buttons) Ui.SetToggled(b, GlowAccent.SameAs(h, _accent ?? original));
                changed();
            };
            Ui.SetToggled(button, GlowAccent.SameAs(hex, _accent ?? original));
            chips.Children.Add(button);
        }
        stack.Children.Add(chips);
        return stack;
    }

    public override bool Changed(ThemeEntry entry) => entry.Id != Applied || entry.Id == GlowId && !string.Equals(_accent, _accentAtStart, StringComparison.OrdinalIgnoreCase);

    public override void Apply(ThemeEntry entry)
    {
        if (entry.Id == GlowId)
        {
            if (_app.UseAccent(_accent) is { } problem) _app.Notify("Accent not changed", problem, NoticeKind.Warning);
            _accentAtStart = _app.Config.Ui.Accent;
            _accent = _accentAtStart;
        }
        else if (entry.Id == CustomId) _app.UseOledColors(null);
        else if (entry.Id.StartsWith(PresetPrefix, StringComparison.Ordinal)) _app.UseOledColors(entry.Name);
        else if (entry.Id.StartsWith(TeamPrefix, StringComparison.Ordinal)) _app.UsePick(ThemeSets.Teams, entry.Name);
        else _app.SetTheme(_specs[entry.Id].Id);
    }
}
