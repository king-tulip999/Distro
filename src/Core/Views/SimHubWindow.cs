using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Core.Controls;
using Core.Services;
using Core.SimRacing.Hud;
using Core.Views.Pages;

namespace Core.Views;

internal sealed class SimHubWindow : Window, IModalHost
{
    private static SimHubWindow? _open;

    private readonly AppServices _app;
    private readonly RaceHudSection _section;
    private readonly Grid _modal = new() { Visibility = Visibility.Collapsed };
    private readonly CaptionGlyph _maxGlyph = new() { Kind = CaptionKind.Maximize };
    private TaskCompletionSource<bool>? _confirm;

    public static void Open(AppServices app, HudHost hud)
    {
        if (_open is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        _open = new SimHubWindow(app);
        _open.Show();
        _open.Activate();
    }

    public static void Shoot(AppServices app, string folder)
    {
        var window = new SimHubWindow(app) { Width = 1280, Height = 860, ShowActivated = false, Left = -20000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
        _open = window;
        window.Show();
        var steps = new Queue<(string Name, Action Enter)>(new (string, Action)[]
        {
            ("widgets", () => { }),
            ("relative", () => window._section.PickWidget(PickId(app, "relative"))),
            ("settings", () => window._section.ShowSettings("none")),
        });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        string? pending = null;
        timer.Tick += (_, _) =>
        {
            if (pending is not null) Save(window, System.IO.Path.Combine(folder, pending + ".png"));
            if (!steps.TryDequeue(out var step))
            {
                timer.Stop();
                App.Quit();
                return;
            }
            pending = step.Name;
            step.Enter();
        };
        System.IO.Directory.CreateDirectory(folder);
        timer.Start();
    }

    private static string PickId(AppServices app, string kind)
    {
        var hud = app.Config.SimRacing.Hud;
        return hud.Layout(hud.ActiveLayout)?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? "";
    }

    private static void Save(Window window, string file)
    {
        if (window.Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(file);
        png.Save(stream);
    }

    private SimHubWindow(AppServices app)
    {
        _app = app;
        Title = "Velocita for SimHub";
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        UseLayoutRounding = true;
        Resources[WindowScale.Key] = Transform.Identity;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "Text");
        SetResourceReference(BackgroundProperty, "Bg");
        FontSize = 12;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = Math.Min(1280, SystemParameters.WorkArea.Width * 0.92);
        Height = Math.Min(860, SystemParameters.WorkArea.Height * 0.92);
        MinWidth = 900;
        MinHeight = 560;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Core;component/Assets/Velocita.ico"));

        var wordmark = new WordmarkText { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(13, 6, 0, 0) };
        var subtitle = new TextBlock { Text = "for SimHub", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Children = { wordmark, subtitle } };
        var min = Caption("CaptionButton", new CaptionGlyph { Kind = CaptionKind.Minimize }, "Minimize", () => WindowState = WindowState.Minimized);
        var max = Caption("CaptionButton", _maxGlyph, "Maximize", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        var close = Caption("CloseCaptionButton", new CaptionGlyph { Kind = CaptionKind.Close }, "Close", Close);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { min, max, close } };
        var bar = new DockPanel { LastChildFill = false, Height = 42 };
        DockPanel.SetDock(brand, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(brand);
        bar.Children.Add(buttons);

        _section = new RaceHudSection(app, this);
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock { Text = "iRacing Overlay", Style = Ui.Res<Style>("H1"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_section.TitleTools, Dock.Right);
        heading.Children.Add(_section.TitleTools);
        heading.Children.Add(title);
        var note = new TextBlock
        {
            Text = "Show or move the widgets in SimHub: Dash Studio → Overlays, each one listed as “Velocita - <widget>”. Changes here show on them right away.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
        };
        var update = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -8, 0, 14), Visibility = Visibility.Collapsed };
        update.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        if (SimHubEngine.UpdateReady is { } ready)
        {
            update.Text = $"Version {UpdateService.Show(ready.Version)} is downloaded and installs when you close SimHub.";
            update.Visibility = Visibility.Visible;
        }
        var page = new ScrollViewer
        {
            Style = Ui.Res<Style>("PageScroll"), Focusable = false,
            Content = Ui.PageFrame(new StackPanel { Margin = new Thickness(23, 4, 23, 23), Children = { heading, note, update, _section } }),
        };
        Ui.ChainWheel(page);
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(page, 1);
        body.Children.Add(bar);
        body.Children.Add(page);
        body.SetResourceReference(Panel.BackgroundProperty, "WindowBg");
        var backdrop = new TopographyBackdrop();
        var root = new Grid { Children = { backdrop, body, _modal } };
        RenderOptions.SetClearTypeHint(root, ClearTypeHint.Enabled);
        Content = root;

        StateChanged += (_, _) =>
        {
            _maxGlyph.Kind = WindowState == WindowState.Maximized ? CaptionKind.Restore : CaptionKind.Maximize;
            root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled || _confirm is null) return;
            CloseModal(false);
            e.Handled = true;
        };
        _section.OnShown();
    }

    private static Button Caption(string style, CaptionGlyph glyph, string tip, Action click)
    {
        var b = new Button { Style = Ui.Res<Style>(style), Content = glyph, ToolTip = tip };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += (_, _) => click();
        return b;
    }

    public double ModalRoom => ActualHeight - 90;

    public Task<bool> ShowModal(FrameworkElement content)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>();
        content.VerticalAlignment = VerticalAlignment.Center;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        if (content.Width > ActualWidth - 40) content.Width = Math.Max(300, ActualWidth - 40);
        _modal.Children.Clear();
        var dim = new Border();
        dim.SetResourceReference(Border.BackgroundProperty, "Dim");
        _modal.Children.Add(dim);
        _modal.Children.Add(content);
        _modal.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_confirm is not null && !_modal.IsKeyboardFocusWithin)
                content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
        return _confirm.Task;
    }

    public void CloseModal(bool result)
    {
        var pending = _confirm;
        _confirm = null;
        _modal.Visibility = Visibility.Collapsed;
        _modal.Children.Clear();
        pending?.TrySetResult(result);
    }

    public Task<bool> Confirm(string title, string message, string confirmText, bool danger = false)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = message, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 11) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Margin = Ui.AfterButton };
        cancel.Click += (_, _) => CloseModal(false);
        var ok = new Button { Content = confirmText, Style = Ui.Res<Style>(danger ? "DangerConfirmButton" : "PrimaryButton") };
        ok.Click += (_, _) => CloseModal(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        var task = ShowModal(new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(16, 15, 16, 13), MaxWidth = 340, Child = stack });
        ok.Focus();
        return task;
    }

    protected override void OnClosed(EventArgs e)
    {
        CloseModal(false);
        _section.OnHidden();
        _app.FlushPendingSave();
        if (ReferenceEquals(_open, this)) _open = null;
        base.OnClosed(e);
    }
}
