using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Core.Controls;
using Core.Services;

namespace Core.Views;

internal sealed class SimHubWindow : Window
{
    private static SimHubWindow? _open;

    private readonly SimHubPanel _panel;
    private readonly CaptionGlyph _maxGlyph = new() { Kind = CaptionKind.Maximize };

    public static void Open(AppServices app)
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
            ("relative", () => window._panel.Section.PickWidget(PickId(app, "relative"))),
            ("settings", () => window._panel.Section.ShowSettings("none")),
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

    internal static void Save(FrameworkElement root, string file)
    {
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(file);
        png.Save(stream);
    }

    private static void Save(Window window, string file)
    {
        if (window.Content is FrameworkElement root) Save(root, file);
    }

    private SimHubWindow(AppServices app)
    {
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
        var bar = new DockPanel { LastChildFill = false, Height = 42, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(brand, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(brand);
        bar.Children.Add(buttons);

        _panel = new SimHubPanel(app) { Margin = new Thickness(0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.SetResourceReference(Panel.BackgroundProperty, "WindowBg");
        Grid.SetRow(_panel, 1);
        root.Children.Add(bar);
        root.Children.Add(_panel);
        RenderOptions.SetClearTypeHint(root, ClearTypeHint.Enabled);
        Content = root;

        StateChanged += (_, _) =>
        {
            _maxGlyph.Kind = WindowState == WindowState.Maximized ? CaptionKind.Restore : CaptionKind.Maximize;
            root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        };
        _panel.Show();
    }

    private static Button Caption(string style, CaptionGlyph glyph, string tip, Action click)
    {
        var b = new Button { Style = Ui.Res<Style>(style), Content = glyph, ToolTip = tip };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += (_, _) => click();
        return b;
    }

    protected override void OnClosed(EventArgs e)
    {
        _panel.Hide();
        if (ReferenceEquals(_open, this)) _open = null;
        base.OnClosed(e);
    }
}
