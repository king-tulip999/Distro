using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Core.Controls;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.RaceEvents;

internal sealed record PickerDriver(int Car, string Number, string Name, int Position, Brush? Class, bool Picked, bool IsYou);

internal sealed class DriverPicker
{
    private readonly Popup _popup;
    private readonly TextBox _search = Ui.Search(new TextBox());
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _count = new() { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button? _clear;
    private readonly bool _multi;
    private readonly Func<IReadOnlyList<PickerDriver>> _drivers;
    private readonly Action<int> _pick;

    public DriverPicker(FrameworkElement anchor, bool multi, string? note, Func<IReadOnlyList<PickerDriver>> drivers, Action<int> pick, Action? clear = null)
    {
        _multi = multi;
        _drivers = drivers;
        _pick = pick;
        var stack = new StackPanel();
        if (note is not null) stack.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(12, 9, 12, 0), TextWrapping = TextWrapping.Wrap });
        _search.Margin = new Thickness(8, 8, 8, 6);
        Ui.NameFor(_search, "Find a driver or car number");
        _search.TextChanged += (_, _) => Build();
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !_multi && Shown().FirstOrDefault() is { } first) { e.Handled = true; Choose(first.Car); }
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        stack.Children.Add(_search);
        _scroll = new ScrollViewer { Content = _rows, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 0, 4, 4) };
        stack.Children.Add(_scroll);
        if (multi)
        {
            var foot = new DockPanel { Margin = new Thickness(12, 6, 8, 7), LastChildFill = true };
            _clear = new Button { Content = "Clear all", Style = Ui.Res<Style>("GhostButton") };
            _clear.Click += (_, _) => { clear?.Invoke(); Build(); };
            DockPanel.SetDock(_clear, Dock.Right);
            foot.Children.Add(_clear);
            foot.Children.Add(_count);
            var footHost = Hairline.Frame(new Border { Child = foot }, Sides.Top);
            footHost.SetResourceReference(Border.BorderBrushProperty, "Line");
            stack.Children.Add(footHost);
        }
        var card = Hairline.Frame(new Border { Width = 312, CornerRadius = ThemeManager.Round(8), Child = stack });
        card.SetResourceReference(Border.BackgroundProperty, "TileRaised");
        card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        _popup = new Popup { Child = card, PlacementTarget = anchor, Placement = PlacementMode.Bottom, VerticalOffset = 4, StaysOpen = false, AllowsTransparency = true };
        _popup.Opened += (_, _) => { _search.Focus(); Keyboard.Focus(_search); };
    }

    public bool IsOpen => _popup.IsOpen;

    public void Open()
    {
        _search.Text = "";
        Build();
        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;

    public void Refresh()
    {
        if (_popup.IsOpen) Build();
    }

    private IEnumerable<PickerDriver> Shown()
    {
        string q = _search.Text.Trim().TrimStart('#');
        return _drivers().Where(d => q.Length == 0 || d.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || d.Number.StartsWith(q, StringComparison.Ordinal));
    }

    private void Choose(int car)
    {
        _pick(car);
        if (_multi) Build();
        else Close();
    }

    private void Build()
    {
        double offset = _scroll.VerticalOffset;
        _rows.Children.Clear();
        var shown = Shown().ToList();
        if (shown.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = $"No driver matches “{_search.Text.Trim()}”", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(8, 10, 8, 12) });
        }
        else if (_multi)
        {
            var picked = shown.Where(d => d.Picked).ToList();
            var rest = shown.Where(d => !d.Picked).ToList();
            if (picked.Count > 0) { _rows.Children.Add(Group("Picked")); foreach (var d in picked) _rows.Children.Add(Row(d)); }
            if (rest.Count > 0) { _rows.Children.Add(Group(picked.Count > 0 ? "Everyone else" : "Field")); foreach (var d in rest) _rows.Children.Add(Row(d)); }
        }
        else
        {
            foreach (var d in shown.Where(d => d.IsYou)) _rows.Children.Add(Row(d));
            var field = shown.Where(d => !d.IsYou).ToList();
            if (field.Count > 0) { _rows.Children.Add(Group("Field")); foreach (var d in field) _rows.Children.Add(Row(d)); }
        }
        if (_multi)
        {
            int n = _drivers().Count(d => d.Picked);
            _count.Text = n == 0 ? "Nobody picked" : RaceFormat.Count(n, "driver") + " picked";
            if (_clear is not null) _clear.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        _scroll.ScrollToVerticalOffset(offset);
    }

    private static TextBlock Group(string text) => new() { Text = text, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(8, 8, 8, 4) };

    private Border Row(PickerDriver d)
    {
        var grid = new Grid { Height = 32 };
        foreach (var w in new[] { 22.0, 36, 14 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        FrameworkElement mark;
        if (_multi)
        {
            var box = Hairline.Frame(new Border { Width = 16, Height = 16, CornerRadius = ThemeManager.Round(4), VerticalAlignment = VerticalAlignment.Center });
            box.SetResourceReference(Border.BorderBrushProperty, d.Picked ? "Accent" : "InputEdge");
            if (d.Picked)
            {
                box.SetResourceReference(Border.BackgroundProperty, "Accent");
                var tick = new Icon { Data = Ui.Res<Geometry>("I.Tick"), Width = 11, Height = 11 };
                tick.SetResourceReference(Icon.ForegroundProperty, "OnAccent");
                box.Child = tick;
            }
            mark = box;
        }
        else
        {
            var cam = new Icon { Data = Ui.Res<Geometry>("I.Video"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Visibility = d.Picked ? Visibility.Visible : Visibility.Hidden };
            cam.SetResourceReference(Icon.ForegroundProperty, "Slate");
            mark = cam;
        }
        grid.Children.Add(mark);
        var pos = new TextBlock { Text = RaceFormat.Pos(d.Position), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(pos, 1);
        grid.Children.Add(pos);
        var dot = new Border { Width = 8, Height = 8, CornerRadius = ThemeManager.Round(4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        if (d.Class is { } cls) dot.Background = cls; else dot.SetResourceReference(Border.BackgroundProperty, "TextFaint");
        Grid.SetColumn(dot, 2);
        grid.Children.Add(dot);
        var name = new TrimRow { VerticalAlignment = VerticalAlignment.Center };
        var nameText = TextFit.TipWhenTrimmed(new TextBlock { Text = d.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        nameText.SetResourceReference(TextBlock.ForegroundProperty, d.Picked ? "Heading" : "Text");
        var plate = RaceEventsWindow.NumberPlate(d.Number, small: true);
        plate.Margin = new Thickness(0, 0, 8, 0);
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(plate, Dock.Left);
        line.Children.Add(plate);
        if (d.IsYou)
        {
            var you = new TextBlock { Text = " (you)", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(you, Dock.Right);
            line.Children.Add(you);
        }
        line.Children.Add(nameText);
        name.Children.Add(line);
        Grid.SetColumn(name, 3);
        grid.Children.Add(name);
        var row = new Border { Child = grid, Padding = new Thickness(8, 0, 8, 0), CornerRadius = ThemeManager.Round(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = Ui.Res<Style>("FocusRing") };
        if (!_multi && d.Picked) row.SetResourceReference(Border.BackgroundProperty, "SlateSoft");
        row.MouseEnter += (_, _) => { if (!(!_multi && d.Picked)) row.SetResourceReference(Border.BackgroundProperty, "HoverRaised"); };
        row.MouseLeave += (_, _) => { if (!(!_multi && d.Picked)) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(d.Car); };
        row.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Choose(d.Car); } };
        System.Windows.Automation.AutomationProperties.SetName(row, d.Name);
        return row;
    }
}

internal sealed class FitTags : Panel
{
    private const double Gap = 6;
    private int _shown;

    public FitTags() => ClipToBounds = true;

    protected override Size MeasureOverride(Size available)
    {
        int n = InternalChildren.Count - 1;
        if (n < 0) return default;
        var more = InternalChildren[n];
        double height = 0;
        for (int i = 0; i < n; i++)
        {
            InternalChildren[i].Measure(new Size(double.PositiveInfinity, available.Height));
            height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
        }
        _shown = n;
        double used = 0;
        for (int i = 0; i < n; i++) used += InternalChildren[i].DesiredSize.Width + (i > 0 ? Gap : 0);
        if (used <= available.Width)
        {
            more.Measure(new Size(0, 0));
            return new Size(used, height);
        }
        for (int k = n - 1; k >= 0; k--)
        {
            if (more is FrameworkElement { Tag: Action<int> setCount }) setCount(n - k);
            more.Measure(new Size(double.PositiveInfinity, available.Height));
            double width = more.DesiredSize.Width;
            for (int i = 0; i < k; i++) width += InternalChildren[i].DesiredSize.Width + Gap;
            if (width <= available.Width || k == 0)
            {
                _shown = k;
                height = Math.Max(height, more.DesiredSize.Height);
                return new Size(Math.Min(width, available.Width), height);
            }
        }
        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int n = InternalChildren.Count - 1;
        if (n < 0) return final;
        double x = 0;
        for (int i = 0; i < n; i++)
        {
            var child = InternalChildren[i];
            if (i >= _shown) { child.Arrange(new Rect(-10000, 0, child.DesiredSize.Width, child.DesiredSize.Height)); continue; }
            child.Arrange(new Rect(x, (final.Height - child.DesiredSize.Height) / 2, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + Gap;
        }
        var more = InternalChildren[n];
        if (_shown < n) more.Arrange(new Rect(x, (final.Height - more.DesiredSize.Height) / 2, more.DesiredSize.Width, more.DesiredSize.Height));
        else more.Arrange(new Rect(-10000, 0, more.DesiredSize.Width, more.DesiredSize.Height));
        return final;
    }
}
