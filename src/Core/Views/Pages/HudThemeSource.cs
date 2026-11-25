using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Core.Config;
using Core.Plugins;
using Core.Plugins.BuiltIn;
using Core.Services;
using Core.SimRacing.Hud;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal sealed class HudThemeSource : ThemeSource
{
    private readonly AppServices _app;
    private readonly HudAppearance _look;
    private readonly List<ThemeEntry> _entries = new();
    private readonly ThemeColors _custom;
    private ThemeColors _customAtStart;
    private readonly HudPainter _painter = new();
    private System.Drawing.Bitmap? _bitmap;
    private PreviewSource? _preview;
    private readonly HudWidgetConfig _relative, _fuel;

    public HudThemeSource(AppServices app, HudAppearance look)
    {
        _app = app;
        _look = look;
        _custom = look.Colors == HudAppearance.CustomColors ? look.Custom.Copy() : HudThemes.StartingColors(look);
        _customAtStart = look.Custom.Copy();
        Add(HudAppearance.DefaultColors, "Default", ThemeEntry.BuiltIn, null);
        Add(HudAppearance.AppColors, "Same as the app theme", ThemeEntry.BuiltIn, null);
        Add(HudAppearance.CustomColors, "Custom", ThemeEntry.BuiltIn, null);
        foreach (var family in CustomPalette.Families)
            foreach (var (name, _, _) in CustomPalette.Presets.Where(p => p.Family == family))
                Add(name, name, family, ThemeEntry.Themes);
        foreach (var series in ThemeSets.Teams.Families)
            foreach (var (name, _, _) in ThemeSets.Teams.Presets.Where(p => p.Family == series))
                Add(name, name, series, ThemeEntry.Teams);
        _relative = Widget("relative", 448, 300, "theme-preview");
        _fuel = Widget("fuel", 520, 300, "theme-preview");
    }

    private static HudWidgetConfig Widget(string kind, double w, double h, string id) =>
        new() { Id = id + "-" + kind, Kind = kind, Enabled = true, W = w, H = h, Background = HudCatalog.Find(kind)?.Background };

    private void Add(string id, string name, string group, string? part)
    {
        if (_entries.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))) return;
        _entries.Add(new ThemeEntry(id, name, group, part, ThemeHues.Of(Palette(id).Accent)));
    }

    private HudPalette Palette(string id) => HudThemes.For(new HudAppearance { Colors = id, Custom = _custom });

    private HudHost? Host => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;

    public override string Title => "Overlay colors";

    public override IReadOnlyList<ThemeEntry> Entries => _entries;

    public override string Applied => HudThemes.Known(_look.Colors) ?? _entries.FirstOrDefault(e => string.Equals(e.Id, _look.Colors, StringComparison.OrdinalIgnoreCase))?.Id ?? HudAppearance.DefaultColors;

    public static Button Face(AppServices app, HudAppearance look, Action applied)
    {
        var source = new HudThemeSource(app, look);
        var entry = source._entries.FirstOrDefault(e => e.Id == source.Applied) ?? source._entries[0];
        var p = source.Palette(entry.Id);
        return ThemeWindow.Face(entry.Name, entry.Part is null ? null : entry.GroupLabel, new[] { ThemeHues.Of(p.Bg), ThemeHues.Of(p.Accent), ThemeHues.Of(p.Heading) },
            button => _ = ThemeWindow.Open(button, new HudThemeSource(app, look), applied));
    }

    private RaceState? Race()
    {
        _preview ??= new PreviewSource(() => _app.Config.SimRacing.Hud);
        return _preview.Race;
    }

    public override bool Moves => true;

    public override void Advance() => _preview?.Advance(Host);

    public override FrameworkElement Thumb(ThemeEntry entry, double width, double height, double pixels)
    {
        var p = Palette(entry.Id);
        Brush B(System.Drawing.Color c) => ThemeHues.Solid(ThemeHues.Of(c));
        var band = B(HudPalette.Mix(p.Bg, p.Text, 0.045));
        TextBlock T(string text, System.Drawing.Color ink, bool bold = false, double size = 11) =>
            new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, Foreground = B(ink), VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { Height = height, Background = B(p.Bg) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new Grid { Background = B(p.Surface) };
        head.Children.Add(new Border { Padding = new Thickness(8, 4, 8, 4), Child = T("RELATIVE", p.AccentText, bold: true, size: 9) });
        var lap = T("L8", p.Muted, size: 9);
        lap.HorizontalAlignment = HorizontalAlignment.Right;
        lap.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(lap);
        grid.Children.Add(head);
        var rows = new[]
        {
            ("13", "S. Delgado", "-0.5", band, p.Heading, p.WarnText),
            ("14", "You", "–", B(p.Player), p.PlayerText, p.PlayerText),
            ("15", "J. Sullivan", "+2.3", band, p.Heading, p.Heading),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var (pos, name, gap, fill, ink, gapInk) = rows[i];
            var row = new Grid { Background = fill };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var at = T(pos, ink, bold: true);
            at.HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(at);
            var who = T(name, ink);
            Grid.SetColumn(who, 1);
            row.Children.Add(who);
            var gapText = T(gap, gapInk, bold: true);
            gapText.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(gapText, 2);
            row.Children.Add(gapText);
            Grid.SetRow(row, i + 1);
            grid.Children.Add(row);
        }
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 5) };
        foot.Children.Add(T("Δ ", p.Muted, size: 9));
        foot.Children.Add(T("-0.453", p.GoodText, bold: true, size: 9));
        foot.Children.Add(new Border { Width = 10 });
        foot.Children.Add(T("FUEL ", p.Muted, size: 9));
        foot.Children.Add(T("4.2", p.WarnText, bold: true, size: 9));
        var footer = new Border { BorderBrush = B(p.Line), BorderThickness = Hairline.Edges(Sides.Top), Child = foot };
        Grid.SetRow(footer, 4);
        grid.Children.Add(footer);
        return grid;
    }

    public override FrameworkElement Preview(ThemeEntry entry, double width, double pixels)
    {
        var palette = Palette(entry.Id);
        var stack = new StackPanel { Width = width };
        foreach (var widget in new[] { _relative, _fuel })
        {
            if (Render(widget, palette, width, pixels) is not { } image) continue;
            stack.Children.Add(new Image { Source = image, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
        }
        if (stack.Children.Count == 0)
            stack.Children.Add(new Border { Height = 180, CornerRadius = ThemeManager.Round(8), Background = ThemeHues.Solid(ThemeHues.Of(palette.Bg)) });
        return stack;
    }

    private BitmapSource? Render(HudWidgetConfig widget, HudPalette palette, double width, double pixels)
    {
        if (Host is not { } host || Race() is not { } race) return null;
        try
        {
            float scale = (float)(width / widget.W * pixels);
            host.RenderPreview(widget, race, _painter, scale, ref _bitmap, palette);
            double drawn = _bitmap!.Width / pixels;
            if (drawn > width + 0.5)
            {
                scale = (float)(scale * width / drawn);
                host.RenderPreview(widget, race, _painter, scale, ref _bitmap, palette);
            }
            return ToImage(_bitmap!, pixels);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return null;
        }
    }

    private static BitmapSource ToImage(System.Drawing.Bitmap bitmap, double pixels)
    {
        var area = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96 * pixels, 96 * pixels, PixelFormats.Pbgra32, null, data.Scan0, data.Stride * bitmap.Height, data.Stride);
            image.Freeze();
            return image;
        }
        finally { bitmap.UnlockBits(data); }
    }

    public override string? Note(ThemeEntry entry) =>
        entry.Id == HudAppearance.AppColors ? $"Uses {Themes.For(_app.Config.Ui.Theme).Label}, the theme picked in Settings." : null;

    public override FrameworkElement? Options(ThemeEntry entry, Action changed)
    {
        if (entry.Id != HudAppearance.CustomColors) return null;
        var rows = new StackPanel();
        foreach (var (label, get, set) in new (string, Func<string>, Action<string>)[]
        {
            ("Panel", () => _custom.Background, v => _custom.Background = v),
            ("Text", () => _custom.Text, v => _custom.Text = v),
            ("Edges", () => _custom.Edges, v => _custom.Edges = v),
            ("Accent", () => _custom.Accent, v => _custom.Accent = v),
            ("Good", () => _custom.Success, v => _custom.Success = v),
            ("Warning", () => _custom.Warning, v => _custom.Warning = v),
            ("Danger", () => _custom.Error, v => _custom.Error = v),
        })
            rows.Children.Add(Ui.FieldRow(label, RaceHudSection.HexField(get, v => { set(v); changed(); }), 80));
        return rows;
    }

    public override bool Changed(ThemeEntry entry) => entry.Id != Applied || entry.Id == HudAppearance.CustomColors && !_custom.SameAs(_customAtStart);

    public override void Apply(ThemeEntry entry)
    {
        if (entry.Id == HudAppearance.CustomColors)
        {
            _look.Custom = _custom.Copy();
            _customAtStart = _custom.Copy();
        }
        _look.Colors = entry.Id;
    }

    public override void Closed()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _painter.Dispose();
    }
}
