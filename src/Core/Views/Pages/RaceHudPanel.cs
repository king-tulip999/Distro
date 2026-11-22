using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Config;
using Core.Controls;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Editors;
using Core.Theme;
using Core.Views.HudSettings;

namespace Core.Views.Pages;

internal sealed partial class RaceHudWidgets
{
    private const double WellPad = 16, WellMinHeight = 160, PreviewShare = 0.85, SettingsMin = 380, TileGap = 6, TileHeight = 32;

    internal readonly record struct PanelTarget(string? SettingId, string? Key, string? Id, string? Tab = null);

    private sealed record EditorGroup(string Title, Func<List<FrameworkElement>> Rows, IReadOnlyList<string> Settings);

    private sealed record EditorTab(string Title, IReadOnlyList<EditorGroup> Groups);

    private Grid? _well;
    private Grid? _previewHost;
    private ScrollViewer? _pageScroll;
    private readonly TranslateTransform _previewFollow = new();
    private double _laidWidth, _laidHeight;
    private string? _tab;
    private StackPanel? _tabBody;
    private Border? _settings;
    private Border? _tabStrip;
    private List<EditorTab> _tabs = new();
    private (string? Setting, (string Key, string Id)? Item) _pin;
    private readonly List<(FrameworkElement Head, List<FrameworkElement> Rows)> _groupHeads = new();

    private FrameworkElement Editor(HudWidgetConfig w, HudWidgetDef def)
    {
        var preview = PreviewCard(w, def);
        var settings = SettingsColumn(w, def);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = SettingsMin });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewShare, GridUnitType.Star) });
        Grid.SetColumn(preview, 2);
        grid.Children.Add(settings);
        grid.Children.Add(preview);
        return grid;
    }

    private FrameworkElement PreviewCard(HudWidgetConfig w, HudWidgetDef def)
    {
        _context = new HudSettingContext
        {
            Def = def, W = w, Frame = _section.Preview.Race.Frame, KnownClasses = S.KnownClasses,
            Set = values => SetValues(w, def, values), Rebuild = Fill, Pictures = (id, choices) => Pictures(w, def, id, choices),
        };
        _preview = new Image { Stretch = Stretch.None, RenderTransform = _previewNudge };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        _previewMarks = new Canvas { IsHitTestVisible = false };
        _previewHighlight = new Canvas { IsHitTestVisible = false };
        var previewHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(WellPad),
            Background = Brushes.Transparent, Children = { _preview, _previewHighlight, _previewMarks },
        };
        _previewHost = previewHost;
        HookPreviewDrag(previewHost, w);
        var scroll = new ScrollViewer
        {
            Content = previewHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Top, Focusable = false, RenderTransform = _previewFollow,
        };
        _well = new Grid { ClipToBounds = true, MinHeight = WellMinHeight, Children = { scroll } };
        _well.MouseLeftButtonDown += (_, e) => { if (!e.Handled) ClosePanel(); };
        _well.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) DrawPreview(w);
            FollowScroll();
        };
        _well.Loaded += (_, _) => HookPageScroll();
        _well.Unloaded += (_, _) => UnhookPageScroll();
        _well.SetResourceReference(Panel.BackgroundProperty, "Bg");

        var title = new TextBlock { Text = "Preview", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _previewNote = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        _previewHidden = Ui.IconText("I.EyeOff", new TextBlock { Text = "Hidden in game", Style = Ui.Res<Style>("Caption") }, "TextMuted");
        _previewHidden.VerticalAlignment = VerticalAlignment.Center;
        _previewHidden.Margin = new Thickness(0, 0, 10, 0);
        _previewHidden.Visibility = w.Enabled ? Visibility.Collapsed : Visibility.Visible;
        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(_previewHidden, Dock.Left);
        head.Children.Add(title);
        head.Children.Add(_previewHidden);
        head.Children.Add(_previewNote);
        var headBar = Hairline.Frame(new Border { Padding = new Thickness(14, 10, 10, 10), Child = head }, Sides.Bottom);
        headBar.SetResourceReference(Border.BorderBrushProperty, "Line");

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(headBar, Dock.Top);
        body.Children.Add(headBar);
        body.Children.Add(_well);
        var card = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(0), ClipToBounds = true, Child = body };
        _previewWell = card;
        return card;
    }

    private FrameworkElement SettingsColumn(HudWidgetConfig w, HudWidgetDef def)
    {
        _tabs = Tabs(w, def);
        int index = Math.Max(0, _tabs.FindIndex(t => t.Title == _tab));
        _tab = _tabs[index].Title;
        _tabBody = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _tabStrip = Ui.PageTabs(_tabs.Select(t => t.Title).ToList(), index, i =>
        {
            _tab = _tabs[i].Title;
            ShowTab();
            if (Motion.Flourish && _tabBody is { } shown) Motion.FadeIn(shown);
        }, "hudtab");
        _settings = new Border { Child = new StackPanel { Children = { _tabStrip, _tabBody } } };
        ShowTab();
        return _settings;
    }

    private void ShowTab()
    {
        if (_tabBody is null || _tabs.Count == 0) return;
        _rows.Clear();
        _groupHeads.Clear();
        _tabBody.Children.Clear();
        var tab = _tabs.FirstOrDefault(t => t.Title == _tab) ?? _tabs[0];
        foreach (var group in tab.Groups)
        {
            var rows = group.Rows();
            var box = new SettingGroup(group.Title, true, null, foldable: false);
            foreach (var r in rows) box.Add(r);
            var tile = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(10, 2, 12, 8), Margin = new Thickness(0, 0, 0, 12), Child = box };
            _groupHeads.Add((tile, rows));
            _tabBody.Children.Add(tile);
        }
        if (_tabBody.Children.Count > 0 && _tabBody.Children[^1] is FrameworkElement last) last.Margin = new Thickness(0);
        RefreshVisibility();
    }

    private void RebuildTab() => _tabBody?.Dispatcher.BeginInvoke(ShowTab, System.Windows.Threading.DispatcherPriority.Background);

    private List<EditorTab> Tabs(HudWidgetConfig w, HudWidgetDef def)
    {
        var editor = def.EditorDef;
        var common = CommonGroups(w, def);
        List<EditorGroup> WidgetGroups(string page) => editor.Groups.Where(g => editor.PageOf(g) == page)
            .Select(g => new EditorGroup(g.Title ?? page, () => g.Settings.Select(s => IsList(def, s) ? ListRow(def, w, s) : Row(s)).ToList(), g.Settings.Select(s => s.Id).ToList()))
            .ToList();
        var commonTabs = common.Select(c => c.Tab).Distinct().ToList();
        var tabs = new List<EditorTab>();
        foreach (var page in editor.Pages.Where(p => !commonTabs.Contains(p)))
            tabs.Add(new EditorTab(page, WidgetGroups(page)));
        foreach (var tab in commonTabs)
            tabs.Add(new EditorTab(tab, common.Where(c => c.Tab == tab).Select(c => new EditorGroup(c.Title, c.Rows, Array.Empty<string>())).Concat(WidgetGroups(tab)).ToList()));
        return tabs;
    }

    private string? TabOf(string settingId) =>
        _tabs.FirstOrDefault(t => t.Groups.Any(g => g.Settings.Contains(settingId, StringComparer.OrdinalIgnoreCase)))?.Title;

    private void HookPageScroll()
    {
        UnhookPageScroll();
        DependencyObject? node = _well;
        while (node is not null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        _pageScroll = node as ScrollViewer;
        if (_pageScroll is not null) _pageScroll.ScrollChanged += OnPageScroll;
        FollowScroll();
    }

    private void UnhookPageScroll()
    {
        if (_pageScroll is not null) _pageScroll.ScrollChanged -= OnPageScroll;
        _pageScroll = null;
    }

    private void OnPageScroll(object sender, ScrollChangedEventArgs e) => FollowScroll();

    private void FollowScroll()
    {
        if (_well is null) return;
        double shift = 0;
        if (_pageScroll is not null && _well.IsVisible && _pageScroll.IsAncestorOf(_well))
        {
            double above = -_well.TranslatePoint(new Point(0, 0), _pageScroll).Y;
            double room = _well.ActualHeight - WellPad * 2 - _laidHeight * _previewScale;
            shift = Math.Round(Math.Clamp(above, 0, Math.Max(0, room)));
        }
        if (Math.Abs(_previewFollow.Y - shift) > 0.1) _previewFollow.Y = shift;
    }

    private double WellHeight() => Math.Max(WellMinHeight, Math.Ceiling(_laidHeight * _previewScale + WellPad * 2));

    private void ClosePanel()
    {
        _pin = (null, null);
        Highlight(null);
    }

    private void RebuildPanel() => RebuildTab();

    private void OpenPanel(PanelTarget target)
    {
        string? tab = target.Tab ?? (target.SettingId is { } sid ? TabOf(sid) : _tabs.FirstOrDefault()?.Title);
        if (tab is not null && tab != _tab)
        {
            int index = _tabs.FindIndex(t => t.Title == tab);
            if (index >= 0 && _tabStrip is not null) Ui.SetSegment(_tabStrip, index);
            if (_tab != tab)
            {
                _tab = tab;
                ShowTab();
            }
        }
        _pin = (target.SettingId, target.Key is { } k && target.Id is { } id ? (k, id) : null);
        Highlight(_pin.Setting, _pin.Item);
        if (target.SettingId is { } setting && _rows.FirstOrDefault(r => string.Equals(r.Setting.Id, setting, StringComparison.OrdinalIgnoreCase)).Row is { } row)
            row.Dispatcher.BeginInvoke(() => row.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static bool IsList(HudWidgetDef def, HudSetting s) =>
        s is PickerSetting or LanesSetting || s is OptionSetting o && def.Option(o.Key) is { Kind: HudOptionKind.Columns };

    private FrameworkElement Row(HudSetting setting)
    {
        var row = Linked(setting, SettingRows.Build(setting, _context!));
        _rows.Add((setting, row));
        return row;
    }

    private FrameworkElement ListRow(HudWidgetDef def, HudWidgetConfig w, HudSetting setting)
    {
        var row = Linked(setting, PickerGrid(def, w, setting));
        _rows.Add((setting, row));
        return row;
    }

    private FrameworkElement PickerGrid(HudWidgetDef def, HudWidgetConfig w, HudSetting setting)
    {
        var picker = setting switch
        {
            PickerSetting p => p,
            OptionSetting o => new PickerSetting(o.Key, FieldSettings.NoCategories) { Label = o.Label },
            _ => null,
        };
        var lanes = setting is LanesSetting ls ? ls.Lanes.Select(x => (x.Key, Lane: (string?)x.Lane)).ToList() : new List<(string Key, string? Lane)> { (picker!.Key, null) };
        var option = def.Option(lanes[0].Key)!;
        var options = _context!.Options;
        Func<string, bool> offered = id => picker?.Offers?.Invoke(options, id) ?? true;
        var choices = option.Choices.Where(c => offered(c.Id)).ToList();
        var sourceCategories = setting is LanesSetting lc ? lc.Categories : picker!.Categories;
        var known = choices.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var order = sourceCategories.SelectMany(c => c.Ids).Where(known.Contains).Distinct().ToList();
        order.AddRange(choices.Select(c => c.Id).Where(id => !order.Contains(id)));
        var model = new ItemPickerModel(choices, lanes.Select(l => ItemPickerModel.Split(options.Value(l.Key))));
        var hidden = (_painter.Overflow ?? new()).ToHashSet();
        var before = lanes.Select(l => options.Value(l.Key)).ToList();

        void Apply()
        {
            var values = model.Values;
            var change = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lanes.Count; i++)
                change[lanes[i].Key] = picker?.Offers is null ? values[i] : ItemPickerModel.KeepUnoffered(values[i], options.Value(lanes[i].Key), offered);
            _context!.Set(change);
        }
        void Cancel()
        {
            var change = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lanes.Count; i++) change[lanes[i].Key] = before[i];
            _context!.Set(change);
            RebuildTab();
        }

        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
        for (int i = 0; i < lanes.Count; i++)
        {
            int lane = i;
            if (lanes.Count > 1)
                root.Children.Add(new TextBlock { Text = (lanes[lane].Lane ?? "") + " bar", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, lane == 0 ? 2 : 12, 0, 6) });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileGap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var tiles = new List<(Border Tile, string Id)>();
            var ids = model.Lanes[lane].ToList();
            ids.AddRange(order.Where(id => !model.IsAdded(id)));
            foreach (var id in ids)
            {
                var choice = model.Choice(id);
                bool on = model.Lanes[lane].Contains(id);
                var tile = PickTile(choice, on, hidden.Contains((lanes[lane].Key, id)), now =>
                {
                    if (now) model.Add(id, lane); else model.Remove(lane, id);
                    Apply();
                    RebuildTab();
                });
                if (on) HookTileDrag(tile, id, lane, grid, tiles, model, Apply, Cancel);
                tiles.Add((tile, id));
                grid.Children.Add(tile);
            }
            PlaceTiles(grid, tiles.Select(t => t.Tile).ToList());
            root.Children.Add(grid);
        }
        return root;
    }

    private static void PlaceTiles(Grid grid, IReadOnlyList<Border> tiles)
    {
        int rows = (tiles.Count + 1) / 2;
        while (grid.RowDefinitions.Count < rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (grid.RowDefinitions.Count > rows) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        for (int i = 0; i < tiles.Count; i++)
        {
            Grid.SetRow(tiles[i], i / 2);
            Grid.SetColumn(tiles[i], i % 2 * 2);
        }
    }

    private static Border PickTile(HudChoice choice, bool on, bool doesntFit, Action<bool> toggle)
    {
        var grip = new Icon { Data = Ui.Res<Geometry>("I.Move"), Width = 11, Height = 11, Margin = new Thickness(8, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = on ? Visibility.Visible : Visibility.Hidden };
        grip.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = choice.Label, Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (!on) name.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var tick = ToggleCards.Row(Ui.Switch(on, toggle), name, null);
        tick.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(tick, choice.Label);
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(grip, Dock.Left);
        dock.Children.Add(grip);
        if (doesntFit)
        {
            var tag = new TextBlock { Text = "Doesn't fit", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), ToolTip = "No room for it in the widget, so it isn't drawn" };
            DockPanel.SetDock(tag, Dock.Right);
            dock.Children.Add(tag);
        }
        dock.Children.Add(tick);
        var tile = Hairline.Frame(new Border
        {
            Height = TileHeight, Margin = new Thickness(0, 0, 0, TileGap), CornerRadius = ThemeManager.Round(8), Child = dock, Tag = choice.Id,
            Cursor = on ? Cursors.SizeAll : null, ToolTip = choice.Help ?? (on ? "Drag to reorder" : null),
        });
        TileLook(tile, false);
        return tile;
    }

    private static void TileLook(Border tile, bool lifted)
    {
        tile.SetResourceReference(Border.BackgroundProperty, lifted ? "AccentSoft" : "TileRaised");
        tile.SetResourceReference(Border.BorderBrushProperty, lifted ? "Accent" : "Line");
        Panel.SetZIndex(tile, lifted ? 1 : 0);
    }

    private static void HookTileDrag(Border tile, string id, int lane, Grid grid, List<(Border Tile, string Id)> tiles, ItemPickerModel model, Action apply, Action cancel)
    {
        Point? down = null;
        bool dragging = false, moved = false;
        tile.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<CheckBox>(source) is not null) return;
            down = e.GetPosition(tile);
        };
        tile.MouseMove += (_, e) =>
        {
            if (down is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            if (!dragging)
            {
                if ((e.GetPosition(tile) - from).Length < 4) return;
                dragging = true;
                moved = false;
                tile.CaptureMouse();
                TileLook(tile, true);
            }
            var at = e.GetPosition(grid);
            foreach (var (other, otherId) in tiles)
            {
                if (ReferenceEquals(other, tile) || !model.Lanes[lane].Contains(otherId)) continue;
                var p = other.TranslatePoint(new Point(0, 0), grid);
                if (at.X < p.X || at.X > p.X + other.ActualWidth || at.Y < p.Y || at.Y > p.Y + other.ActualHeight) continue;
                var list = model.Lanes[lane];
                int now = list.IndexOf(id), target = list.IndexOf(otherId);
                if (model.Move(lane, id, lane, target + (now < target ? 1 : 0)))
                {
                    var sorted = list.Select(x => tiles.First(t => t.Id == x)).Concat(tiles.Where(t => !list.Contains(t.Id))).ToList();
                    tiles.Clear();
                    tiles.AddRange(sorted);
                    PlaceTiles(grid, tiles.Select(t => t.Tile).ToList());
                    moved = true;
                    apply();
                }
                break;
            }
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            down = null;
            if (!dragging) return;
            dragging = false;
            tile.ReleaseMouseCapture();
            TileLook(tile, false);
        };
        tile.LostMouseCapture += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            down = null;
            TileLook(tile, false);
            if (moved) cancel();
        };
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (DependencyObject? at = node; at is not null; at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is T found) return found;
        return null;
    }

    private (string Setting, string Key, string? Id)? PartAt(HudEditorDef editor, Point at)
    {
        var p = PainterPoint(at);
        (string, string, string?)? best = null;
        float bestArea = float.MaxValue;
        void Consider(string key, string? id, System.Drawing.RectangleF box)
        {
            float area = box.Width * box.Height;
            if (!box.Contains(p) || area >= bestArea || editor.SettingForRegion(key) is not { } setting) return;
            best = (setting, key, id);
            bestArea = area;
        }
        foreach (var (id, box) in _painter.Regions ?? new()) Consider(id, null, box);
        foreach (var (key, id, box) in _painter.Items ?? new()) Consider(key, id, box);
        return best;
    }

    internal (string? Selected, string? Tab) Keep() => (_selected, _tab);

    internal void Restore((string? Selected, string? Tab) keep)
    {
        if (keep.Selected is null || Layout?.Widgets.All(w => w.Id != keep.Selected) != false) return;
        _selected = keep.Selected;
        _tab = keep.Tab;
        Fill();
    }

    internal void SweepSelect(string kind)
    {
        if (Layout is not { } layout || HudCatalog.Find(kind) is not { } def) return;
        if (layout.Widgets.Any(w => w.Kind == kind)) Select(kind);
        else Add(def);
    }

    internal IReadOnlyList<(string Name, PanelTarget Target)> SweepParts()
    {
        var list = new List<(string, PanelTarget)>();
        if (Selected() is null) return list;
        foreach (var tab in _tabs)
            list.Add(("tab-" + string.Join("-", tab.Title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)), new PanelTarget(null, null, null, tab.Title)));
        if (_tabs.Count > 0) list.Add(("all", new PanelTarget(null, null, null, _tabs[0].Title)));
        return list;
    }

    internal void SweepOpen(PanelTarget target) => OpenPanel(target);

    private List<ComboBox> PanelCombos()
    {
        var found = new List<ComboBox>();
        void Walk(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is ComboBox { IsVisible: true } combo) found.Add(combo);
                else Walk(child);
            }
        }
        if (_tabBody is not null) Walk(_tabBody);
        return found;
    }

    internal int SweepDropdowns() => PanelCombos().Count;

    internal void SweepOpenDropdown(int index)
    {
        var combos = PanelCombos();
        foreach (var c in combos) c.IsDropDownOpen = false;
        if (index < combos.Count)
        {
            combos[index].BringIntoView();
            combos[index].IsDropDownOpen = true;
        }
    }

    private List<Control> PanelControls()
    {
        var found = new List<Control>();
        void Walk(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is UIElement { IsVisible: false } or UIElement { IsEnabled: false }) continue;
                if (child is ComboBox combo) { found.Add(combo); continue; }
                if (child is HotkeyBox or TextBox) continue;
                if (child is System.Windows.Controls.Primitives.ButtonBase button) { found.Add(button); continue; }
                Walk(child);
            }
        }
        if (_tabBody is not null) Walk(_tabBody);
        return found;
    }

    internal async Task SweepExercise(string kind)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        void Report(string issue)
        {
            if (reported.Add(issue)) Services.ScreenshotRunner.RecordError(new InvalidOperationException(issue));
        }
        foreach (var (name, target) in SweepParts())
        {
            OpenPanel(target);
            await Task.Delay(40);
            for (int i = 0; i < SweepActionCap; i++)
            {
                var controls = PanelControls();
                if (i >= controls.Count) break;
                var control = controls[i];
                string label = System.Windows.Automation.AutomationProperties.GetName(control) is { Length: > 0 } n ? n : control is ContentControl { Content: string text } ? text : control.GetType().Name;
                try
                {
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control);
                    switch (control)
                    {
                        case ComboBox combo:
                            int before = combo.SelectedIndex;
                            for (int j = 0; j < combo.Items.Count; j++)
                            {
                                combo.SelectedIndex = j;
                                await Task.Delay(10);
                            }
                            if (before >= 0 && before < combo.Items.Count) combo.SelectedIndex = before;
                            break;
                        case RadioButton when peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem) is System.Windows.Automation.Provider.ISelectionItemProvider pick:
                            pick.Select();
                            break;
                        case System.Windows.Controls.Primitives.ToggleButton when peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle) is System.Windows.Automation.Provider.IToggleProvider toggle:
                            toggle.Toggle();
                            await Task.Delay(10);
                            toggle.Toggle();
                            break;
                        default:
                            if (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is System.Windows.Automation.Provider.IInvokeProvider invoke) invoke.Invoke();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Report($"hud {kind} {name}: “{label}” threw {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Delay(30);
                if (_tab != target.Tab) OpenPanel(target);
                await Task.Delay(10);
                if (_tabBody is { } settle) await settle.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                _settings?.UpdateLayout();
                if (_settings is not null)
                    foreach (var finding in Services.LayoutLint.Inspect(_settings, $"hud {kind} {name}")) Report(finding.ToString());
            }
        }
    }

    private const int SweepActionCap = 160;

    private bool HasParts(HudEditorDef editor) =>
        (_painter.Regions ?? new()).Any(r => editor.SettingForRegion(r.Id) is not null) || (_painter.Items ?? new()).Any(i => editor.SettingForRegion(i.Key) is not null);
}
