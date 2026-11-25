using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Core.Controls;
using Core.Theme;

namespace Core.Views;

internal static class ThemePreview
{
    internal sealed record SampleText(string Label, string Value, string Change, string Warning, string Setting, string[] Choices, (string Name, string Detail)[] Rows);

    internal static SampleText Sample => AppIdentity.IsTecnica
        ? new("CPU temperature", "64 °C", "−3 °C", "High load", "Background usage", new[] { "Standard", "Reduced" }, new[] { ("Desk", "2560 × 1440"), ("TV", "3840 × 2160") })
        : new("Last lap", "1:47.382", "−0.214", "Pit window", "Units", new[] { "Metric", "Imperial" }, new[] { ("Spa-Francorchamps", "2:17.884"), ("Monza", "1:47.382") });

    public static FrameworkElement For(ThemeSpec spec, double width)
    {
        var resources = ThemeManager.Build(spec);
        using var scope = Ui.Scope(resources);
        var tile = Build(spec.Label);
        tile.Resources = resources;
        tile.Width = width;
        tile.Background = Ui.Res<Brush>("WindowBg");
        return tile;
    }

    private static Tile Build(string themeName)
    {
        var stack = new StackPanel();

        var top = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        top.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = themeName, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis }));
        stack.Children.Add(top);

        var lap = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        var lapLabel = new TextBlock { Text = Sample.Label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        var lapTime = new TextBlock { Text = Sample.Value, Style = Ui.Res<Style>("H2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var lapDelta = new TextBlock { Text = Sample.Change, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("SageDeep"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lapTime, 1);
        Grid.SetColumn(lapDelta, 2);
        lap.Children.Add(lapLabel);
        lap.Children.Add(lapTime);
        lap.Children.Add(lapDelta);
        stack.Children.Add(new Tile { Background = Ui.Res<Brush>("Surface"), BorderBrush = Ui.Res<Brush>("Line"), BorderThickness = Hairline.Edges(), CornerRadius = (CornerRadius)Application.Current.FindResource("R.Small"), Padding = new Thickness(10, 6, 10, 6), Child = lap });

        var buttons = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (var (text, style) in new[] { ("Apply", "PrimaryButton"), ("Cancel", "GhostButton"), ("Delete", "DangerButton") })
            buttons.Children.Add(new Button { Content = text, Style = Ui.Res<Style>(style), IsHitTestVisible = false, Focusable = false, Margin = Ui.AfterButton });
        stack.Children.Add(buttons);

        var chips = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (var (text, soft, deep) in new[] { ("Auto", "AccentSoft", "AccentText"), ("Connected", "SageSoft", "SageDeep"), (Sample.Warning, "WarnSoft", "WarnDeep"), ("Fan stopped", "DangerSoft", "DangerDeep") })
            chips.Children.Add(Ui.Chip(text, soft, deep));
        stack.Children.Add(chips);

        var bar = new UniformGrid { Rows = 1, Height = 14, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var key in new[] { "Bg", "LineStrong", "Text", "Accent", "ActiveModeFg", "Sage", "Warn", "Danger" })
            bar.Children.Add(new Rectangle { Fill = Ui.Res<Brush>(key) });
        stack.Children.Add(new Tile { CornerRadius = (CornerRadius)Application.Current.FindResource("R.Tiny"), ClipToBounds = true, Child = bar });

        return new Tile
        {
            Background = Ui.Res<Brush>("Bg"), BorderBrush = Ui.Res<Brush>("LineStrong"), BorderThickness = Hairline.Edges(),
            CornerRadius = (CornerRadius)Application.Current.FindResource("R.Inset"), Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Top,
            Child = stack,
        };
    }
}
