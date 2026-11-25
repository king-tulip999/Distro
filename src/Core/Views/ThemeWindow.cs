using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Core.Controls;
using Core.Services;
using Core.Theme;

namespace Core.Views;

internal sealed record ThemeEntry(string Id, string Name, string Group, string? Part, Color Accent)
{
    public const string BuiltIn = "Built in", Themes = "Themes", Teams = "Teams";

    public string GroupLabel => Part == Teams ? $"{Teams} · {Group}" : Group;
}

internal abstract class ThemeSource
{
    public abstract string Title { get; }

    public abstract IReadOnlyList<ThemeEntry> Entries { get; }

    public abstract string Applied { get; }

    public abstract FrameworkElement Thumb(ThemeEntry entry, double width, double height, double pixels);

    public abstract FrameworkElement Preview(ThemeEntry entry, double width, double pixels);

    public virtual FrameworkElement? Options(ThemeEntry entry, Action changed) => null;

    public virtual string? Note(ThemeEntry entry) => null;

    public virtual bool Changed(ThemeEntry entry) => entry.Id != Applied;

    public abstract void Apply(ThemeEntry entry);

    public virtual bool Moves => false;

    public virtual void Advance() { }

    public virtual void Closed() { }
}

internal sealed class ThemeWindow : Tile
{
    private const double BaseWidth = 1180, RailWidth = 204, PreviewWidth = 392, TileMin = 118, TileGap = 6, ThumbDesign = 160, ThumbRatio = 0.68, SpyReach = 48;

    private sealed class TileParts
    {
        public required ThemeEntry Entry { get; init; }
        public required Button Button { get; init; }
        public required Border Frame { get; init; }
        public required Border Thumb { get; init; }
        public required Border Check { get; init; }
        public required TextBlock Name { get; init; }
        public string Words { get; init; } = "";
        public bool Drawn { get; set; }
    }

    private sealed class SectionParts
    {
        public required string Key { get; init; }
        public required FrameworkElement Panel { get; init; }
        public required Button Rail { get; init; }
        public required Border RailFace { get; init; }
        public required Ellipse Dot { get; init; }
        public required TextBlock Count { get; init; }
        public List<TileParts> Tiles { get; } = new();
        public Run? Heading { get; set; }
    }

    private readonly IModalHost _host;
    private readonly ThemeSource _source;
    private readonly List<SectionParts> _sections = new();
    private readonly StackPanel _gallery = new() { Margin = new Thickness(18, 2, 8, 18) };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _preview = new() { Margin = new Thickness(18, 16, 18, 18) };
    private readonly Border _previewHost = new();
    private readonly TextBlock _picked = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
    private readonly TextBox _search;
    private readonly Button _searchButton;
    private readonly DispatcherTimer? _timer;
    private ThemeEntry _pick;
    private ThemeEntry? _hover;
    private SectionParts? _active;
    private double _tileWidth;
    private bool _drawing;

    private ThemeWindow(IModalHost host, ThemeSource source)
    {
        _host = host;
        _source = source;
        _pick = source.Entries.FirstOrDefault(e => e.Id == source.Applied) ?? source.Entries[0];
        Style = Ui.Res<Style>("LiftedCard");
        Padding = new Thickness(0);
        Width = BaseWidth;
        SetValue(Ui.DialogBaseWidthProperty, BaseWidth);
        Height = Ui.DialogMaxHeight(host.ModalRoom);
        MaxHeight = Ui.DialogMaxHeight(host.ModalRoom);

        _search = Ui.Search(new TextBox { Width = 280, Tag = "Name, team or color", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) });
        _search.TextChanged += (_, _) => Filter();
        _searchButton = Ui.IconButton("I.Search", "Search (Ctrl+F)", OpenSearch);
        _scroll = new ScrollViewer { Content = new StackPanel { Children = { _gallery, _empty } }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _scroll.ScrollChanged += (_, _) => Spy();
        _gallery.SizeChanged += (_, e) => { if (e.WidthChanged) FitTiles(); };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };
        root.Children.Add(Header());
        var body = Body();
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var footer = Footer();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Child = root;

        BuildSections();
        ShowPreview();
        ShowPicked();
        PreviewKeyDown += OnKey;
        Loaded += (_, _) =>
        {
            FitTiles();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { ScrollToPick(); Spy(); DrawThumbs(); });
            _timer?.Start();
        };
        Unloaded += (_, _) => _timer?.Stop();
        if (source.Moves)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => { _source.Advance(); RefreshPreviewPicture(); };
        }
    }

    public static Task Open(FrameworkElement from, ThemeSource source, Action? applied = null) =>
        HostOf(from) is { } host ? OpenOn(host, source, applied) : Task.CompletedTask;

    public static async Task OpenOn(IModalHost host, ThemeSource source, Action? applied = null)
    {
        var window = new ThemeWindow(host, source) { _applied = applied };
        Showing = window;
        await host.ShowModal(window);
        if (Showing == window) Showing = null;
        window._timer?.Stop();
        source.Closed();
    }

    private Action? _applied;
    private Button? _apply;

    private void ApplyPick()
    {
        if (!_source.Changed(_pick)) return;
        try
        {
            _source.Apply(_pick);
            _applied?.Invoke();
        }
        catch (Exception ex) { App.LogError(ex); }
        ShowPreview();
        ShowPicked();
    }

    public static ThemeWindow? Showing { get; private set; }

    public void SearchFor(string text)
    {
        OpenSearch();
        _search.Text = text;
    }

    private static IModalHost? HostOf(DependencyObject from)
    {
        for (DependencyObject? at = from; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) ?? LogicalTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is IModalHost host) return host;
        return Window.GetWindow(from) as IModalHost;
    }

    public static Button Face(string name, string? group, IReadOnlyList<Color> chip, Action<Button> open)
    {
        var strip = new Grid();
        double[] shares = { 2, 1, 1 };
        for (int i = 0; i < Math.Min(chip.Count, shares.Length); i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shares[i], GridUnitType.Star) });
            var cell = new Rectangle { Fill = ThemeHues.Solid(chip[i]) };
            Grid.SetColumn(cell, i);
            strip.Children.Add(cell);
        }
        var swatch = Hairline.Frame(new Tile { Width = 34, Height = 18, CornerRadius = ThemeManager.Round(4), ClipToBounds = true, Child = strip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
        swatch.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var text = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Inlines.Add(new Run(name));
        if (group is not null)
        {
            var tail = new Run("  " + group) { FontWeight = FontWeights.Normal };
            tail.SetResourceReference(TextElement.ForegroundProperty, "TextMuted");
            text.Inlines.Add(tail);
        }
        var icon = new Icon { Data = Ui.Res<Geometry>("I.Palette"), Width = 13, Height = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Control.ForegroundProperty, "TextMuted");
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(icon, Dock.Right);
        var button = new Button
        {
            Style = Ui.Res<Style>("GhostButton"), MinWidth = 188, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(5, 2, 10, 2), Content = new DockPanel { LastChildFill = true, Children = { swatch, icon, text } },
            ToolTip = "Opens every theme with a preview",
        };
        System.Windows.Automation.AutomationProperties.SetName(button, "Theme: " + name);
        button.Click += (_, _) => open(button);
        return button;
    }

    private FrameworkElement Header()
    {
        var grid = new Grid { Margin = new Thickness(21, 14, 14, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(_source.Title, "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);
        Grid.SetColumn(_search, 1);
        grid.Children.Add(_search);
        Grid.SetColumn(_searchButton, 2);
        grid.Children.Add(_searchButton);
        var close = Ui.IconButton("I.X", "Close", () => _host.CloseModal(false));
        close.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(close, 3);
        grid.Children.Add(close);
        return Lined(grid, Sides.Bottom);
    }

    private FrameworkElement Body()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewWidth) });
        var rail = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        _railPanel = rail;
        grid.Children.Add(Lined(new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Sides.Right));
        Grid.SetColumn(_scroll, 1);
        grid.Children.Add(_scroll);
        _previewHost.HorizontalAlignment = HorizontalAlignment.Left;
        var pane = Lined(new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Sides.Left);
        pane.SetResourceReference(Border.BackgroundProperty, "Bg");
        Grid.SetColumn(pane, 2);
        grid.Children.Add(pane);
        return grid;
    }

    private StackPanel _railPanel = new();

    private FrameworkElement Footer()
    {
        var grid = new Grid { Margin = new Thickness(21, 12, 21, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_picked);
        var cancel = new Button { Content = "Close", Style = Ui.Res<Style>("GhostButton"), Margin = Ui.AfterButton };
        cancel.Click += (_, _) => _host.CloseModal(false);
        var apply = new Button { Content = "Apply", Style = Ui.Res<Style>("PrimaryButton"), IsDefault = true };
        apply.Click += (_, _) => ApplyPick();
        _apply = apply;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), Children = { cancel, apply } };
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return Lined(grid, Sides.Top);
    }

    private static Border Lined(UIElement child, Sides sides)
    {
        var border = new Border { Child = child, BorderThickness = Hairline.Edges(sides) };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private void BuildSections()
    {
        string? part = null;
        foreach (var group in _source.Entries.GroupBy(e => (e.Part, e.Group)))
        {
            if (group.Key.Part != part)
            {
                part = group.Key.Part;
                if (part is not null)
                    _railPanel.Children.Add(new TextBlock { Text = part, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(15, 14, 14, 4) });
            }
            var entries = group.ToList();
            var heading = new TextBlock { Style = Ui.Res<Style>("Eyebrow") };
            heading.Inlines.Add(new Run(entries[0].GroupLabel));
            var count = new Run("  " + entries.Count) { FontWeight = FontWeights.Normal };
            count.SetResourceReference(TextElement.ForegroundProperty, "TextFaint");
            heading.Inlines.Add(count);
            var wrap = new WrapPanel();
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { heading, wrap } };
            heading.Margin = new Thickness(0, 0, 0, 6);
            var section = RailEntry(entries[0].GroupLabel, entries[0].Group, panel);
            section.Count.Text = entries.Count.ToString();
            section.Heading = count;
            foreach (var entry in entries)
            {
                var tile = Tile(entry);
                section.Tiles.Add(tile);
                wrap.Children.Add(tile.Button);
            }
            section.Dot.Visibility = section.Tiles.Any(t => t.Entry.Id == _pick.Id) ? Visibility.Visible : Visibility.Hidden;
            _sections.Add(section);
            _gallery.Children.Add(panel);
        }
        var clear = new Button { Content = "Clear search", Style = Ui.Res<Style>("GhostButton") };
        clear.Click += (_, _) => { _search.Clear(); _search.Focus(); };
        _emptyClear = clear;
    }

    private Button? _emptyClear;

    private SectionParts RailEntry(string key, string label, FrameworkElement panel)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentText");
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var count = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(dot);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        Grid.SetColumn(count, 2);
        row.Children.Add(count);
        var face = new Border { Child = row, Padding = new Thickness(12, 0, 14, 0), Height = 30, BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent };
        var button = new Button { Style = Ui.Res<Style>("Btn"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = face };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        var section = new SectionParts { Key = key, Panel = panel, Rail = button, RailFace = face, Dot = dot, Count = count };
        button.Click += (_, _) => JumpTo(section);
        button.MouseEnter += (_, _) => { if (_active != section) face.SetResourceReference(Border.BackgroundProperty, "Hover"); };
        button.MouseLeave += (_, _) => { if (_active != section) face.Background = Brushes.Transparent; };
        _railPanel.Children.Add(button);
        return section;
    }

    private TileParts Tile(ThemeEntry entry)
    {
        var thumb = new Border { CornerRadius = ThemeManager.Round(7), ClipToBounds = true };
        var frame = new Border { CornerRadius = ThemeManager.Round(10), Padding = new Thickness(3), BorderThickness = Hairline.Edges(), Child = thumb };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        var check = new Border
        {
            Width = 18, Height = 18, CornerRadius = ThemeManager.Round(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -6, 0), Visibility = Visibility.Collapsed,
            Child = new Icon { Data = Ui.Res<Geometry>("I.Check"), Width = 10, Height = 10, StrokeThickness = 2.43 },
        };
        check.SetResourceReference(Border.BackgroundProperty, "Accent");
        ((Icon)check.Child).SetResourceReference(Control.ForegroundProperty, "OnAccent");
        var name = new TextBlock
        {
            Text = ThemeMarks.ShortName(entry.Name), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        };
        var nameBox = new Border { Child = name, Padding = new Thickness(2, 0, 2, 5) };
        var face = new StackPanel { Children = { nameBox, new Grid { Children = { frame, check } } } };
        var button = new Button
        {
            Style = Ui.Res<Style>("Btn"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, TileGap, TileGap), Content = face,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, entry.Name);
        var tile = new TileParts
        {
            Entry = entry, Button = button, Frame = frame, Thumb = thumb, Check = check, Name = name,
            Words = string.Join(' ', entry.Name, entry.Group, entry.Part ?? ThemeEntry.BuiltIn, ThemeHues.Words(entry.Accent)).ToLowerInvariant(),
        };
        button.Click += (_, _) => Pick(entry);
        button.MouseDoubleClick += (_, e) => { e.Handled = true; Pick(entry); ApplyPick(); };
        button.MouseEnter += (_, _) => { Edge(tile, hover: true); Hover(entry); };
        button.MouseLeave += (_, _) => { Edge(tile, hover: false); Hover(null); };
        button.GotKeyboardFocus += (_, _) => Hover(entry);
        button.LostKeyboardFocus += (_, _) => Hover(null);
        Edge(tile, hover: false);
        return tile;
    }

    private void Edge(TileParts tile, bool hover)
    {
        bool on = tile.Entry.Id == _pick.Id;
        tile.Frame.BorderThickness = on ? new Thickness(Hairline.Pixels(2, Hairline.Scale)) : Hairline.Edges();
        tile.Frame.Padding = new Thickness(on ? 2 : 3);
        tile.Frame.SetResourceReference(Border.BorderBrushProperty, on ? "Accent" : hover ? "LineStrong" : "Line");
        tile.Check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        tile.Name.SetResourceReference(TextBlock.ForegroundProperty, on || hover ? "Heading" : "Text");
    }

    private double Pixels => WindowScale.Of(this) * (ScreenshotRunner.DrawsAt96 ? 1 : VisualTreeHelper.GetDpi(this).DpiScaleX);

    private void FitTiles()
    {
        double room = _gallery.ActualWidth;
        if (room <= 0) return;
        int columns = Math.Max(2, (int)Math.Floor(room / (TileMin + TileGap)));
        double width = Math.Floor(room / columns - TileGap);
        if (Math.Abs(width - _tileWidth) < 0.5) return;
        bool redraw = false;
        _tileWidth = width;
        foreach (var section in _sections)
        {
            double tallest = 0;
            foreach (var tile in section.Tiles)
            {
                tile.Name.Measure(new Size(width - 4, double.PositiveInfinity));
                tallest = Math.Max(tallest, tile.Name.DesiredSize.Height);
            }
            foreach (var tile in section.Tiles) ((Border)tile.Name.Parent).Height = tallest + 5;
        }
        foreach (var tile in _sections.SelectMany(s => s.Tiles))
        {
            tile.Button.Width = width;
            tile.Thumb.Height = Math.Round((width - 8) * ThumbRatio);
            if (redraw) tile.Drawn = false;
        }
        if (redraw) DrawThumbs();
    }

    private void DrawThumbs()
    {
        if (_drawing || _tileWidth <= 0) return;
        _drawing = true;
        var queue = new Queue<TileParts>(_sections.SelectMany(s => s.Tiles).Where(t => !t.Drawn)
            .OrderBy(t => t.Button.IsVisible && IsShown(t.Button) ? 0 : 1));
        void Next()
        {
            for (int n = 0; n < 6 && queue.Count > 0; n++) Draw(queue.Dequeue());
            if (queue.Count > 0 && IsLoaded) Dispatcher.BeginInvoke(DispatcherPriority.Background, Next);
            else _drawing = false;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Next);
    }

    private bool IsShown(FrameworkElement element)
    {
        if (!element.IsVisible || _scroll.ViewportHeight <= 0) return false;
        double top = element.TranslatePoint(new Point(), _scroll).Y;
        return top + element.ActualHeight > 0 && top < _scroll.ViewportHeight;
    }

    private void Draw(TileParts tile)
    {
        tile.Drawn = true;
        try
        {
            var design = _source.Thumb(tile.Entry, ThumbDesign, Math.Round(ThumbDesign * ThumbRatio), Pixels);
            design.Width = ThumbDesign;
            tile.Thumb.Child = new Viewbox { Stretch = Stretch.Uniform, Child = design };
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void Pick(ThemeEntry entry)
    {
        if (entry.Id == _pick.Id) return;
        var before = _pick;
        _pick = entry;
        foreach (var tile in _sections.SelectMany(s => s.Tiles).Where(t => t.Entry.Id == before.Id || t.Entry.Id == entry.Id))
            Edge(tile, hover: tile.Button.IsMouseOver);
        foreach (var section in _sections)
            section.Dot.Visibility = section.Tiles.Any(t => t.Entry.Id == entry.Id) ? Visibility.Visible : Visibility.Hidden;
        _hover = null;
        ShowPreview();
        ShowPicked();
    }

    private void Hover(ThemeEntry? entry)
    {
        if (entry == _hover) return;
        _hover = entry;
        ShowPreview();
    }

    private ThemeEntry Shown => _hover ?? _pick;

    private void ShowPreview()
    {
        var entry = Shown;
        _preview.Children.Clear();
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = entry.Name, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis });
        _preview.Children.Add(name);
        var state = new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 12), TextWrapping = TextWrapping.Wrap };
        state.Inlines.Add(new Run(entry.Part is null ? ThemeEntry.BuiltIn : entry.GroupLabel));
        state.Inlines.Add(new Run(" · "));
        var mark = new Run(entry.Id == _source.Applied ? "In use" : entry.Id == _pick.Id ? "Picked" : "Preview") { FontWeight = FontWeights.SemiBold };
        mark.SetResourceReference(TextElement.ForegroundProperty, "AccentText");
        state.Inlines.Add(mark);
        _preview.Children.Add(state);
        _preview.Children.Add(_previewHost);
        RefreshPreviewPicture();
        if (_source.Note(entry) is { } note)
            _preview.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        if (entry.Id == _pick.Id && _source.Options(entry, OptionsChanged) is { } options)
        {
            options.Margin = new Thickness(0, 14, 0, 0);
            _preview.Children.Add(options);
        }
    }

    private void OptionsChanged()
    {
        RefreshPreviewPicture();
        ShowPicked();
        foreach (var tile in _sections.SelectMany(s => s.Tiles).Where(t => t.Entry.Id == _pick.Id)) Draw(tile);
    }

    private void RefreshPreviewPicture()
    {
        try { _previewHost.Child = _source.Preview(Shown, PreviewWidth - 36, Pixels); }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void ShowPicked()
    {
        if (_apply is not null) _apply.IsEnabled = _source.Changed(_pick);
        _picked.Inlines.Clear();
        _picked.Inlines.Add(new Bold(new Run(_pick.Name)));
        if (_pick.Id == _source.Applied) _picked.Inlines.Add(new Run(" is in use"));
        else if (_source.Entries.FirstOrDefault(e => e.Id == _source.Applied) is { } applied) _picked.Inlines.Add(new Run(" replaces " + applied.Name));
        _picked.Inlines.FirstInline.SetResourceReference(TextElement.ForegroundProperty, "Heading");
    }

    private void OpenSearch()
    {
        _search.Visibility = Visibility.Visible;
        _searchButton.Visibility = Visibility.Collapsed;
        Motion.FadeIn(_search);
        _search.Focus();
    }

    private void CloseSearch()
    {
        _search.Clear();
        _search.Visibility = Visibility.Collapsed;
        _searchButton.Visibility = Visibility.Visible;
        _searchButton.Focus();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _search.Visibility == Visibility.Visible && _search.Text.Length == 0)
        {
            CloseSearch();
            e.Handled = true;
        }
    }

    private void Filter()
    {
        var words = _search.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int total = 0;
        foreach (var section in _sections)
        {
            int shown = 0;
            foreach (var tile in section.Tiles)
            {
                bool match = words.All(w => tile.Words.Contains(w, StringComparison.Ordinal));
                tile.Button.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) shown++;
            }
            total += shown;
            section.Panel.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
            section.Rail.IsEnabled = shown > 0;
            section.Rail.Opacity = shown > 0 ? 1 : 0.45;
            section.Count.Text = shown.ToString();
            if (section.Heading is { } heading) heading.Text = "  " + shown;
        }
        _empty.Children.Clear();
        _empty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (total == 0 && _emptyClear is not null)
        {
            if (_emptyClear.Parent is Panel old) old.Children.Remove(_emptyClear);
            _empty.Children.Add(Ui.EmptyState("I.Palette", "No themes found", $"No themes match “{_search.Text.Trim()}”.", _emptyClear));
        }
        _scroll.ScrollToTop();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { Spy(); DrawThumbs(); });
    }

    private void JumpTo(SectionParts section)
    {
        if (section.Panel.Visibility != Visibility.Visible) return;
        double top = section.Panel.TranslatePoint(new Point(), _gallery).Y + _gallery.Margin.Top;
        _scroll.ScrollToVerticalOffset(Math.Max(0, top - 4));
    }

    private void ScrollToPick()
    {
        if (_sections.FirstOrDefault(s => s.Tiles.Any(t => t.Entry.Id == _pick.Id)) is { } section && section != _sections[0]) JumpTo(section);
    }

    private void Spy()
    {
        SectionParts? current = null;
        foreach (var section in _sections)
        {
            if (section.Panel.Visibility != Visibility.Visible || !section.Panel.IsLoaded) continue;
            double top = section.Panel.TranslatePoint(new Point(), _scroll).Y;
            if (current is null || top <= SpyReach) current = section;
            if (top > SpyReach) break;
        }
        if (_scroll.VerticalOffset >= _scroll.ScrollableHeight - 1 && _scroll.ScrollableHeight > 0)
            current = _sections.LastOrDefault(s => s.Panel.Visibility == Visibility.Visible && s.Panel.TranslatePoint(new Point(), _scroll).Y < _scroll.ViewportHeight) ?? current;
        if (current == _active) { DrawThumbs(); return; }
        if (_active is { } old)
        {
            old.RailFace.BorderBrush = Brushes.Transparent;
            old.RailFace.Background = Brushes.Transparent;
            ((TextBlock)((Grid)old.RailFace.Child).Children[1]).FontWeight = FontWeights.Normal;
        }
        _active = current;
        if (current is not null)
        {
            current.RailFace.SetResourceReference(Border.BorderBrushProperty, "Accent");
            current.RailFace.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            ((TextBlock)((Grid)current.RailFace.Child).Children[1]).FontWeight = FontWeights.SemiBold;
        }
        DrawThumbs();
    }
}
