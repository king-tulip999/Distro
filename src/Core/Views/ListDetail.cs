using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Core.Controls;
using Core.Theme;

namespace Core.Views;

internal sealed class ListDetail
{
    public const double ListWidth = 250;

    public Tile Card { get; }

    public StackPanel List { get; } = new();

    public ContentControl Detail { get; } = new() { Focusable = false };

    public ContentControl ListFooter { get; } = new() { Focusable = false };

    public ListDetail()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ListWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var column = new DockPanel();
        DockPanel.SetDock(ListFooter, Dock.Bottom);
        column.Children.Add(ListFooter);
        column.Children.Add(List);
        Ui.NoSearch(List);
        grid.Children.Add(column);
        Grid.SetColumn(Detail, 2);
        grid.Children.Add(Detail);
        Card = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = grid };
    }

    public void Show(FrameworkElement? content, bool animate)
    {
        Detail.Content = content;
        if (animate && content is not null) Motion.FadeIn(content);
    }

    public static ReorderGrid Reorder() => new() { MaxColumns = 1, MinCellWidth = 200, Gap = 0 };

    public static RadioButton Row(UIElement content, string group, bool selected, Action picked)
    {
        var row = new RadioButton
        {
            Style = Ui.Res<Style>("NavItem"),
            GroupName = group,
            Content = content,
            IsChecked = selected,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 3, 0, 3),
        };
        Ui.NoSearch(row);
        row.Checked += (_, _) => picked();
        return row;
    }

    public static Grid RowFace(string name, string? side, string? dot = null, string? icon = null, bool verbatim = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement lead;
        if (icon is not null)
        {
            var glyph = Ui.Glyph(icon);
            glyph.Width = glyph.Height = 14;
            glyph.Foreground = Ui.Res<Brush>("TextMuted");
            lead = glyph;
        }
        else
        {
            var mark = new Ellipse { Width = 7, Height = 7 };
            if (dot is not null) mark.SetResourceReference(Shape.FillProperty, dot);
            lead = mark;
        }
        lead.VerticalAlignment = VerticalAlignment.Center;
        lead.Margin = new Thickness(2, 0, 8, 0);
        grid.Children.Add(lead);
        var text = TextFit.TipWhenTrimmed(new TextBlock { Text = name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (verbatim) TextCase.Verbatim(text);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (side is { Length: > 0 })
        {
            var caption = new TextBlock { Text = side, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            TextCase.Verbatim(caption);
            Grid.SetColumn(caption, 2);
            grid.Children.Add(caption);
        }
        return grid;
    }

    public static FrameworkElement Heading(string text, bool first = false)
    {
        var head = Section(text, first);
        head.Margin = new Thickness(5, first ? 3 : 15, 0, 7);
        return head;
    }

    public static FrameworkElement Section(string text, bool first = false)
    {
        var title = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.Bold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        TextCase.SetUpper(title, true);
        title.Margin = new Thickness(0, first ? 2 : 16, 0, 6);
        return title;
    }

    public static Border Rule()
    {
        var rule = Hairline.Rule(new Border { Margin = new Thickness(5, 10, 5, 0) });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        return rule;
    }

    public static FrameworkElement Note(string text, string icon = "I.Info")
    {
        var line = Ui.IconText(icon, new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap }, "TextFaint");
        line.Margin = new Thickness(5, 8, 5, 0);
        return line;
    }

    public static FrameworkElement DragHint() => Note("Drag to reorder", "I.Move");

    public static Grid Head(string title, Action? rename, IEnumerable<FrameworkElement> marks, IEnumerable<FrameworkElement> actions)
    {
        var grid = new Grid { MinHeight = 34, Margin = new Thickness(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TrimRow { VerticalAlignment = VerticalAlignment.Center };
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = title, Style = Ui.Res<Style>("H1"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        TextCase.SetUpper(name, false);
        left.Children.Add(name);
        if (rename is not null)
        {
            var pencil = Ui.IconButton("I.Pencil", "Rename", rename, "QuietIconButton");
            pencil.VerticalAlignment = VerticalAlignment.Center;
            pencil.Margin = new Thickness(0, 0, 10, 0);
            left.Children.Add(pencil);
        }
        foreach (var mark in marks)
        {
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(mark);
        }
        grid.Children.Add(left);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bool firstTool = true;
        foreach (var action in actions)
        {
            action.Margin = new Thickness(firstTool ? 0 : Ui.ButtonGap, 0, 0, 0);
            firstTool = false;
            tools.Children.Add(action);
        }
        Grid.SetColumn(tools, 1);
        grid.Children.Add(tools);
        return grid;
    }

    public static StackPanel Facts(string title, IEnumerable<(string Label, string Value)> facts)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(Section(title, first: true));
        var grid = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (label, value) in facts)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 28, 6) };
            var name = new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            cell.Children.Add(name);
            var shown = TextFit.TipWhenTrimmed(new TextBlock { Text = value, FontSize = 13, MaxWidth = 380, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            TextCase.Verbatim(shown);
            cell.Children.Add(shown);
            grid.Children.Add(cell);
        }
        panel.Children.Add(grid);
        return panel;
    }

    public static Tile SideFacts(string title, IReadOnlyList<(string Label, string Value)> facts)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < facts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = facts[i].Label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 5) };
            Grid.SetRow(name, i);
            grid.Children.Add(name);
            var shown = new TextBlock { Text = facts[i].Value, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 5) };
            TextCase.Verbatim(shown);
            Grid.SetRow(shown, i);
            Grid.SetColumn(shown, 1);
            grid.Children.Add(shown);
        }
        var box = new Tile
        {
            Style = Ui.Res<Style>("Inset"), Padding = new Thickness(12, 10, 12, 5), Margin = new Thickness(0, 15, 0, 0),
            Child = new StackPanel { Children = { Section(title, first: true), grid } },
        };
        box.Background = Ui.Res<Brush>("SurfaceAlt");
        return box;
    }

    public static Tile Preview(UIElement content, double height)
    {
        var box = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(10), Height = height, Child = content, ClipToBounds = true };
        box.Background = Ui.Res<Brush>("SurfaceAlt");
        return box;
    }

    public static Border Divider()
    {
        var rule = Hairline.Rule(new Border { Margin = new Thickness(0, 8, 0, 6) });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        return rule;
    }

    public static Grid Columns(UIElement left, UIElement right, double leftShare = 1.4)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftShare, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing * 1.5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    public sealed class Pending
    {
        private readonly TextBlock _text = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });

        public Border Bar { get; }

        public Pending(string text, string save, Action discard, Action commit)
        {
            _text.Text = text;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Ui.Badge("I.Info", "ApricotSoft", "ApricotDeep", 24, 9, 13));
            _text.Margin = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(_text, 1);
            grid.Children.Add(_text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var drop = new Button { Content = "Discard", Margin = Ui.AfterButton };
            drop.Click += (_, _) => discard();
            buttons.Children.Add(drop);
            buttons.Children.Add(Ui.LabeledButton("I.Check", save, commit, "PrimaryButton"));
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            Bar = Ui.PendingBar(new Tile { Child = grid, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed });
        }

        public bool Showing => Bar.Visibility == Visibility.Visible;

        public void Show()
        {
            if (Showing) return;
            Bar.Visibility = Visibility.Visible;
            Motion.Enter(Bar, distance: 8);
        }

        public void Hide() => Bar.Visibility = Visibility.Collapsed;
    }

    public static void TrackEdits(FrameworkElement root, Action edited)
    {
        bool armed = false;
        root.Dispatcher.BeginInvoke(() => armed = true, System.Windows.Threading.DispatcherPriority.ContextIdle);
        void Fire(object sender, RoutedEventArgs e) { if (armed) edited(); }
        root.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => Fire(s, e)));
        root.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => Fire(s, e)));
        root.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(Fire));
        root.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(Fire));
        root.AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => Fire(s, e)));
    }
}
