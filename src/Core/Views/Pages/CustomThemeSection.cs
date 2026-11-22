using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Core.Config;
using Core.Services;
using Core.Theme;

namespace Core.Views.Pages;

internal sealed class CustomThemeSection : StackPanel
{
    private readonly AppServices _app;

    private CustomThemeSettings C => _app.Config.Ui.OledCustom;

    public CustomThemeSection(AppServices app)
    {
        _app = app;
        if (_app.Config.Ui.Theme == AppTheme.Custom)
            Build("Colors", CustomPalette.Families, CustomPalette.Presets, CustomPalette.Build, PickedColors, name => _app.UseOledColors(name));
        else if (ThemeSets.For(_app.Config.Ui.Theme) is { } set)
            Build(set.Title, set.Families, set.Presets, CustomPalette.BuildBrand, name => set.Current(_app.Config.Ui) == name, name => _app.UsePick(set, name));
        var accent = new StackPanel();
        Ui.Anchor(accent, "settings.accent");
        Children.Add(accent);
        if (_app.Config.Ui.Theme == AppTheme.Glow) BuildAccent(accent);
    }

    private void BuildAccent(StackPanel host)
    {
        host.Children.Add(new TextBlock { Text = "Accent", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        string current = Palettes.ToHex(Themes.For(AppTheme.Glow).Colors["Accent"]);
        string original = Palettes.ToHex(Themes.AppGlow.Colors["Accent"]);
        var swatches = GlowAccent.Swatches.OrderBy(s => GlowAccent.SameAs(s.Hex, original) ? 0 : 1).ToArray();
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        host.Children.Add(chips);
        foreach (var (name, hex) in swatches)
            chips.Children.Add(ChipButton(name, Dot(CustomPalette.Parse(hex)!.Value), GlowAccent.SameAs(hex, current), () => _app.UseAccent(hex)));
        host.Children.Add(new Border { Height = 3 });
    }

    private static Ellipse Dot(Color color) =>
        new() { Width = DotSize, Height = DotSize, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private void Build(string title, string[] families, (string Name, string Family, ThemeColors Colors)[] presets,
        Func<ThemeColors, IReadOnlyDictionary<string, Color>> palette, Func<string, bool> picked, Action<string> use)
    {
        Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        var shown = families.Where(f => presets.Any(p => p.Family == f)).ToArray();
        if (shown.Length == 0) return;
        int pickedFamily = Array.FindIndex(shown, f => presets.Any(p => p.Family == f && picked(p.Name)));
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Show(int index)
        {
            chips.Children.Clear();
            foreach (var (name, _, colors) in presets.Where(p => p.Family == shown[index]))
                chips.Children.Add(ChipButton(name, palette(colors), picked(name), use));
        }
        Children.Add(Ui.PageTabs(shown, Math.Max(0, pickedFamily), index => { Show(index); Motion.FadeIn(chips); }, "theme-family", compact: true));
        Show(Math.Max(0, pickedFamily));
        Children.Add(chips);
        Children.Add(new Border { Height = 3 });
    }

    private bool PickedColors(string name) => C.Enabled ? string.Equals(C.Name, name, StringComparison.OrdinalIgnoreCase) : name == CustomPalette.NameOf(ThemeColors.Default);

    private const double DotSize = 16, NameWidth = 220;

    private static Button ChipButton(string name, IReadOnlyDictionary<string, Color> c, bool selected, Action<string> use) =>
        ChipButton(name, new Ellipse { Width = DotSize, Height = DotSize, Fill = Split(c["Accent"], c["ActiveModeFg"]), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
            selected, () => use(name));

    private static Button ChipButton(string name, Ellipse dot, bool selected, Action use)
    {
        var label = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = ThemeMarks.ShortName(name), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = NameWidth, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Ui.Res<Brush>(selected ? "Heading" : "Text"),
        });
        var face = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() } };
        face.Children.Add(dot);
        Grid.SetColumn(label, 1);
        face.Children.Add(label);

        var chip = new Border { Style = Ui.Res<Style>("Chip"), Margin = new Thickness(0), Padding = new Thickness(4, 0, 10, 0), Child = face };
        if (selected)
        {
            chip.Background = Ui.Res<Brush>("AccentSoft");
            chip.BorderBrush = Ui.Res<Brush>("Accent");
        }

        var button = new Button
        {
            Style = Ui.Res<Style>("Btn"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 6), Content = chip,
        };
        button.MouseEnter += (_, _) => chip.BorderBrush = Ui.Res<Brush>("Accent");
        button.MouseLeave += (_, _) => chip.BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("ChipBorder");
        System.Windows.Automation.AutomationProperties.SetName(button, name + (selected ? " (selected)" : ""));
        button.Click += (_, _) =>
        {
            if (!selected) use();
        };
        return button;
    }

    private static Brush Split(Color left, Color right)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(left, 0.5));
        brush.GradientStops.Add(new GradientStop(right, 0.5));
        brush.Freeze();
        return brush;
    }
}
