using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Core.Controls;
using Core.Services;
using Core.SessionStats;
using Core.Theme;

namespace Core.Views.Pages;

internal sealed record StatsTabData(SessionStatsService Stats, StatsStore Store, StatQuery Query, StatRange Range, List<(StatDef Def, StatResult Result)> Results,
    StatsCharts.Data Charts, List<SessionSummary> Sessions, HashSet<long> Traced, RaceInfo Races)
{
    private Dictionary<string, (StatDef Def, StatResult Result)>? _byId;

    public (StatDef Def, StatResult Result)? Get(string id) =>
        (_byId ??= Results.GroupBy(r => r.Def.Id).ToDictionary(g => g.Key, g => g.First())).TryGetValue(id, out var r) ? r : null;

    public string RangeName => StatsUi.RangeName(Range);
}

internal interface IStatsHost
{
    AppServices App { get; }
    MainWindow Window { get; }
    StatsTabState State { get; }
    void OpenAnalysis(long id, List<int> laps);
    void ShowTab(string tab);
    void Changed();
    FrameworkElement TraceButton();
    FrameworkElement LiveEvents();
}

internal sealed class StatsTabState
{
    public long? Session;
    public string SessionTab = "Overview";
    public string SessionSearch = "";
    public int SessionType;
    public int SessionsShown = 60;
    public long? Race;
    public HashSet<string> EventKinds = new(StatsEventsTab.Kinds.Select(k => k.Id));
    public int? Event;
    public string? Pair;
    public string PairSearch = "";
    public string PairGroup = "car";
    public string PairSort = "driven";
    public HashSet<string> HiddenCars = new();
    public HashSet<string> FoldedGroups = new();
    public int? HeatCell;
}

internal static class StatsTabs
{
    public static readonly (string Id, string Name)[] All =
    {
        ("sessions", "Sessions"), ("events", "Race events"), ("season", "Season"), ("driving", "Driving"), ("cars", "Cars and tracks"),
    };

    public static readonly string[] Ranges = { "Week", "Month", "Season", "All time" };

    public static string Normalize(string? tab) => All.Any(t => t.Id == tab) ? tab! : "sessions";

    public static FrameworkElement Build(string tab, StatsTabData data, IStatsHost host) => tab switch
    {
        "events" => StatsEventsTab.Build(data, host),
        "season" => StatsSeasonTab.Build(data, host),
        "driving" => StatsDrivingTab.Build(data, host),
        "cars" => StatsCarsTab.Build(data, host),
        _ => StatsSessionsTab.Build(data, host),
    };

    public const double ListWidth = 380, ListMinHeight = 560;

    public static Grid ListAndDetail(FrameworkElement list, FrameworkElement detail, double listWidth = ListWidth)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(listWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        list.VerticalAlignment = VerticalAlignment.Top;
        detail.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(list);
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);
        ScrollViewer? page = null;
        void Fit()
        {
            if (page is null || !list.IsVisible || !page.IsAncestorOf(list)) return;
            var toPage = list.TransformToAncestor(page);
            double top = toPage.Transform(new Point(0, 0)).Y;
            double unit = toPage.Transform(new Point(0, 1)).Y - top;
            if (unit <= 0.01) return;
            double height = Math.Max(ListMinHeight, Math.Floor((page.ViewportHeight - (top + page.VerticalOffset)) / unit - ListBottomGap));
            if (Math.Abs(list.Height - height) <= 0.5) return;
            list.Height = height;
            detail.MinHeight = height;
        }
        void Resized(object sender, SizeChangedEventArgs e) => Fit();
        list.Loaded += (_, _) =>
        {
            DependencyObject? at = System.Windows.Media.VisualTreeHelper.GetParent(list);
            while (at is not null && at is not ScrollViewer) at = System.Windows.Media.VisualTreeHelper.GetParent(at);
            if (page is not null) page.SizeChanged -= Resized;
            page = at as ScrollViewer;
            if (page is not null) page.SizeChanged += Resized;
            list.Dispatcher.BeginInvoke(Fit, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        list.Unloaded += (_, _) => { if (page is not null) page.SizeChanged -= Resized; page = null; };
        list.Height = ListMinHeight;
        detail.MinHeight = ListMinHeight;
        return grid;
    }

    private const double ListBottomGap = 23;

    public static Tile ListCard(FrameworkElement head, FrameworkElement rows)
    {
        var scroll = new ScrollViewer { Name = "ListScroll", VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = rows, Focusable = false };
        Ui.ChainWheel(scroll);
        head.Margin = new Thickness(16, 14, 16, 12);
        var rule = Hairline.Rule(new Border());
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(rule, Dock.Top);
        var dock = new DockPanel { LastChildFill = true, Children = { head, rule, scroll } };
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = dock, ClipToBounds = true };
    }

    public static FrameworkElement ListTitle(string title, string? note)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var text = Ui.Text(title, "H2");
        text.VerticalAlignment = VerticalAlignment.Bottom;
        row.Children.Add(text);
        if (note is not null)
            row.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) });
        return row;
    }

    public static RadioButton Row(FrameworkElement content, bool selected, string group, Action pick, string? tip = null)
    {
        var row = new RadioButton
        {
            Style = Ui.Res<Style>("NavItem"), GroupName = group, Content = content, IsChecked = selected, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 2, 0, 2), Margin = new Thickness(0), ToolTip = tip,
        };
        row.Checked += (_, _) => pick();
        return row;
    }

    public static Grid TwoLines(string top, string bottom, FrameworkElement? right = null, FrameworkElement? lead = null)
    {
        var grid = new Grid { Margin = new Thickness(6, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (lead is not null)
        {
            lead.Margin = new Thickness(0, 0, 12, 0);
            lead.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(lead);
        }
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = top, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        var sub = TextFit.TipWhenTrimmed(new TextBlock { Text = bottom, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        if (right is not null)
        {
            right.Margin = new Thickness(12, 0, 0, 0);
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }
        return grid;
    }

    public static StackPanel Figure(string big, string small, string smallBrush = "TextFaint", string bigBrush = "Heading", double size = 18)
    {
        var top = RaceParts.Big(big, size, bigBrush);
        top.HorizontalAlignment = HorizontalAlignment.Right;
        var bottom = new TextBlock { Text = small, Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1, 0, 0) };
        bottom.SetResourceReference(TextBlock.ForegroundProperty, smallBrush);
        System.Windows.Documents.Typography.SetNumeralAlignment(bottom, FontNumeralAlignment.Tabular);
        return new StackPanel { Children = { top, bottom } };
    }

    public static TextBlock DayHead(string text, bool first)
    {
        var head = new TextBlock { Text = text, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(12, first ? 6 : 9, 0, 2) };
        return head;
    }

    public static Tile Card(FrameworkElement content, Thickness? padding = null) =>
        new() { Style = Ui.Res<Style>("Card"), Padding = padding ?? new Thickness(19, 16, 19, 19), Child = content };

    public static StackPanel Stacked(params FrameworkElement?[] parts)
    {
        var stack = new StackPanel();
        foreach (var part in parts.OfType<FrameworkElement>())
        {
            if (stack.Children.Count > 0) part.Margin = new Thickness(part.Margin.Left, Ui.PanelSpacing, part.Margin.Right, part.Margin.Bottom);
            stack.Children.Add(part);
        }
        return stack;
    }

    public static Button MenuButton(string text, double minWidth, string tip)
    {
        var button = new Button
        {
            Style = Ui.Res<Style>("MenuButton"), MinWidth = minWidth, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = tip,
            Content = Ui.MenuFace(TextFit.TipWhenTrimmed(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis })),
        };
        return button;
    }

    public static ContextMenu Menu(FrameworkElement anchor, double minWidth = 211)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MinWidth = minWidth };
        Ui.DropBelow(menu);
        return menu;
    }

    public static MenuItem Choice(ContextMenu menu, string header, bool on, Action pick, string? tip = null, bool staysOpen = false)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on, ToolTip = tip, StaysOpenOnClick = staysOpen };
        item.Click += (_, _) => pick();
        menu.Items.Add(item);
        return item;
    }

    public static System.Windows.Shapes.Ellipse Dot(string brush, double size = 7)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        return dot;
    }

    public static TextBox SearchBox(string text, string placeholder, Action<string> changed)
    {
        var box = Ui.Search(new TextBox { Text = text, Tag = placeholder });
        System.Windows.Automation.AutomationProperties.SetName(box, placeholder);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) => { timer.Stop(); changed(box.Text); };
        box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        box.Unloaded += (_, _) => timer.Stop();
        return box;
    }

    public static Grid SubTabs(IReadOnlyList<string> names, string selected, Action<string> pick, out Border tabs, FrameworkElement? right = null)
    {
        tabs = Ui.PageTabs(names, Math.Max(0, names.ToList().IndexOf(selected)), i => pick(names[i]), "statsSub", compact: true);
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(tabs);
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        return grid;
    }

    public static Border Rule(double top = 0, double bottom = 0)
    {
        var rule = Hairline.Rule(new Border { Margin = new Thickness(0, top, 0, bottom) });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        return rule;
    }

    public static string Laps(int n) => SimRacing.Race.RaceFormat.Count(n, "lap");

    public static string Places(int gain) => gain > 0 ? "+" + SimRacing.Race.RaceFormat.Count(gain, "place") : gain < 0 ? "−" + SimRacing.Race.RaceFormat.Count(-gain, "place") : "Same as start";

    public static string SessionsText(int n) => SimRacing.Race.RaceFormat.Count(n, "session");
}
