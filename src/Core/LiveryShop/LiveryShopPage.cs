using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Config;
using Core.Controls;
using Core.Processes;
using Core.Services;
using Core.SimRacing;
using Core.Theme;
using Core.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Core.LiveryShop;
internal sealed class LiveryShopPage : UserControl, IPage
{
    private const string Host = "liveryshop.velocita";
    private const string UiProcess = "iracingui";

    private readonly Grid _root = new();
    private readonly Grid _editor = new();
    private readonly Border _host = new();
    private FrameworkElement? _header;
    private readonly TextBlock _status;
    private readonly Button _undo, _redo;
    private WebView2CompositionControl? _web;
    private bool _started;
    private DispatcherTimer? _reload;

    private readonly bool _demo;
    private readonly AppServices _app;
    private PaintGuard? _guard;
    private string? _tradingPaints;
    private const string Consumer = "Livery Shop";

    public LiveryShopPage(AppServices app, MainWindow window)
    {
        _demo = app.Demo;
        _app = app;
        _status = new TextBlock
        {
            Text = "Opening Livery Shop…",
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _editor.Children.Add(_host);
        _editor.Children.Add(_status);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button Tool(string text, string id, string? style = null)
        {
            var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) };
            if (style is not null) button.Style = Ui.Res<Style>(style);
            button.Click += (_, _) => Press(id);
            tools.Children.Add(button);
            return button;
        }
        Tool("New design", "newB");
        Tool("Versions", "verB");
        _undo = Tool("Undo", "undo");
        _redo = Tool("Redo", "redo");
        _undo.IsEnabled = _redo.IsEnabled = false;
        Tool("Help", "helpB");
        Tool("Export", "exportB", "PrimaryButton");
        var titleText = Ui.PageTitle("Livery Shop");
        titleText.HorizontalAlignment = HorizontalAlignment.Left;
        var beta = new EchoTitle { Text = "Beta", Size = 12, Crop = -0.2 };
        beta.SetResourceReference(EchoTitle.FamilyProperty, "RaceFont");
        beta.SetResourceReference(EchoTitle.StrokeProperty, "SlateDeep");
        beta.HorizontalAlignment = HorizontalAlignment.Left;
        beta.VerticalAlignment = VerticalAlignment.Bottom;
        if (titleText.Children[0] is EchoTitle echo)
            titleText.SizeChanged += (_, _) => beta.Margin = new Thickness(Math.Ceiling(echo.DrawnWidth) + 12, 0, 0, 0);
        var title = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { titleText, beta } };
        DockPanel.SetDock(tools, Dock.Right);
        Panel.SetZIndex(title, -1);
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(Ui.HeaderIndent, Ui.HeaderTop, 0, Ui.HeaderGap), Children = { tools, title } };

        _editor.Margin = new Thickness(Ui.PageSide, 0, Ui.PageSide, Ui.PageSide);
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_editor, 1);
        _header = Ui.PageFrame(new Border { Margin = new Thickness(Ui.PageSide, 0, Ui.PageSide, 0), Child = header });
        _root.Children.Add(_header);
        _root.Children.Add(_editor);
        Ui.Anchor(_root, LiveryShopPlugin.Anchor);
        Content = _root;
        IsVisibleChanged += (_, _) => { if (IsVisible) Wake(); else Park(); };
        Unloaded += async (_, _) =>
        {
            await Task.Delay(SuspendAfter);
            if (!IsLoaded) Close();
        };
    }

    public void OnShown()
    {
        Wake();
        if (_started) return;
        _started = true;
        _ = StartAsync();
    }

    public void OnHidden() => Park();

    private static readonly TimeSpan SuspendAfter = TimeSpan.FromSeconds(2), CloseAfter = TimeSpan.FromMinutes(3);
    private DispatcherTimer? _park;
    private DateTime _hiddenSince;
    private bool _suspended;

    private void Park()
    {
        if (_web is null || IsVisible) return;
        _hiddenSince = DateTime.UtcNow;
        if (_park is null)
        {
            _park = new DispatcherTimer(DispatcherPriority.Background);
            _park.Tick += async (_, _) => await ParkStepAsync();
        }
        _park.Interval = SuspendAfter;
        _park.Start();
    }

    private async Task ParkStepAsync()
    {
        _park!.Stop();
        if (IsVisible || _web?.CoreWebView2 is not { } core) return;
        var hidden = DateTime.UtcNow - _hiddenSince;
        if (hidden >= CloseAfter)
        {
            Close();
            return;
        }
        if (!_suspended)
        {
            try { _suspended = await core.TrySuspendAsync(); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        }
        if (IsVisible) return;
        _park.Interval = CloseAfter - hidden;
        _park.Start();
    }

    private void Wake()
    {
        _park?.Stop();
        if (!_suspended) return;
        _suspended = false;
        try { _web?.CoreWebView2?.Resume(); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
    }

    private void Close()
    {
        StopGuards();
        _park?.Stop();
        ThemeManager.Changed -= OnThemeChanged;
        _host.Child = null;
        _web?.Dispose();
        _web = null;
        _suspended = false;
        _started = false;
        _undo.IsEnabled = _redo.IsEnabled = false;
        _status.Text = "Opening Livery Shop…";
        _status.Visibility = Visibility.Visible;
    }

    private static string Folder => Path.Combine(ConfigStore.Folder, "livery-shop");

    private static string DemoFolder => Path.Combine(Path.GetTempPath(), AppIdentity.DataName + " demo", "livery-shop");

    private string Root => _demo ? DemoFolder : Folder;

    private static bool _demoStorageCleared;

    private async Task StartAsync()
    {
        try
        {
            string root = Root;
            string site = await Task.Run(() => WriteSite(root));
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "webview"));
            var web = new WebView2CompositionControl { Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.Transparent };
            _host.Child = web;
            _web = web;
            await web.EnsureCoreWebView2Async(environment);
            if (!ReferenceEquals(_host.Child, web)) return;
            var core = web.CoreWebView2;
            if (_demo && !_demoStorageCleared)
            {
                _demoStorageCleared = true;
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllDomStorage);
                if (!ReferenceEquals(_web, web)) return;
            }
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.SetVirtualHostNameToFolderMapping(Host, site, CoreWebView2HostResourceAccessKind.Deny);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ThemeScript());
            if (!ReferenceEquals(_web, web)) return;
            ThemeManager.Changed += OnThemeChanged;
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenOutside(e.Uri); };
            if (_demo) core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) =>
            {
                if (IsOwn(e.Uri)) return;
                e.Cancel = true;
                OpenOutside(e.Uri);
            };
            StartGuards();
            core.NavigationCompleted += (_, _) =>
            {
                web.Visibility = Visibility.Visible;
                _status.Visibility = Visibility.Collapsed;
            };
            long version = File.GetLastWriteTimeUtc(Path.Combine(site, "index.html")).Ticks;
            core.Navigate($"https://{Host}/index.html?v={version}");
        }
        catch (ObjectDisposedException) { }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("Livery Shop needs Microsoft Edge WebView2. It comes with Windows 11; on Windows 10, install the WebView2 Runtime from Microsoft, then open this page again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException or ArgumentException)
        {
            App.LogError(ex);
            Fail("Livery Shop couldn't start: " + ex.Message);
        }
    }

    private void Press(string id)
    {
        if (_web?.CoreWebView2 is { } core) _ = core.ExecuteScriptAsync($"document.getElementById('{id}')?.click()");
    }

    private void OnThemeChanged()
    {
        if (_web?.CoreWebView2 is { } core) _ = core.ExecuteScriptAsync(ThemeScript());
    }

    private static string ThemeScript()
    {
        var css = new System.Text.StringBuilder(":root{color-scheme:dark;");
        foreach (var (name, key) in new[] { ("bg", "Bg"), ("panel", "Surface"), ("ink", "Text"), ("mute", "TextMuted"), ("line", "InputEdge"),
                     ("accent", "Accent"), ("accent-ink", "OnAccent"), ("sel", "AccentSoft"), ("edge", "InputEdge"), ("edge-hover", "InputEdgeHover") })
            if (Application.Current.TryFindResource(key) is SolidColorBrush brush)
                css.Append(CultureInfo.InvariantCulture, $"--{name}:rgba({brush.Color.R},{brush.Color.G},{brush.Color.B},{brush.Color.A / 255.0 * brush.Opacity:0.###});");
        css.Append('}');
        css.Append("@font-face{font-family:AppUi;src:url(fonts/Saira-Regular.ttf)}@font-face{font-family:AppUi;font-weight:600;src:url(fonts/Saira-SemiBold.ttf)}@font-face{font-family:AppUi;font-weight:700 900;src:url(fonts/Saira-Bold.ttf)}@font-face{font-family:AppRace;font-weight:700;src:url(fonts/SairaCondensed-Bold.ttf)}@font-face{font-family:AppRace;font-weight:800 900;src:url(fonts/SairaCondensed-ExtraBold.ttf)}");
        css.Append("html{background:transparent!important}body{background:transparent!important;font-family:AppUi,system-ui,sans-serif}header{display:none!important}.app{grid-template-rows:minmax(0,1fr)!important}");
        css.Append(ScrollBarCss());
        css.Append("h2,.modal h1{font-family:AppRace,sans-serif!important}");
        css.Append("body{font-size:13px!important}h2{font-size:15px!important;margin-bottom:6px!important}.main{grid-template-columns:250px minmax(0,1fr) 250px!important}aside{padding:10px!important;gap:12px!important;overflow-x:hidden}.tabs{margin:-10px -10px 0!important;top:-10px!important;padding:6px 10px 0!important}");
        css.Append(".btn,.hud .btn,.adds .btn{display:inline-flex;align-items:center;justify-content:center;min-height:28px!important;padding:0 10px!important;font-size:12.5px!important}.adds .btn{justify-content:flex-start}.adds svg{width:14px!important;height:14px!important}.ico{width:26px!important;height:26px!important}.zoom{line-height:28px!important;font-size:12.5px}");
        css.Append(".f label{font-size:12px!important}.f input[type=text],.f input[type=number],.f select,.f input[type=color]{min-height:28px!important;height:28px;font-size:13px!important}.chk{min-height:28px!important}.ro{min-height:28px!important;font-size:13px!important}");
        css.Append(".tabs button{min-height:34px!important;font-size:13px}.team input{height:28px!important}.chip{width:24px!important;height:24px!important}.layers li canvas{width:26px!important;height:26px!important}.note,.empty{font-size:12px!important}");
        css.Append(".main{gap:8px;background:transparent;padding:0 2px 2px 1px}aside,.stage{border:1px solid var(--edge);border-radius:8px;background:var(--panel)}aside.l,aside.r{border:1px solid var(--edge)}.modal .card,.cmenu,.qbar{backdrop-filter:blur(6px);-webkit-backdrop-filter:blur(6px)}.stage{background:var(--panel)!important}html,body,.app,.main{overflow:hidden!important}.modal .card,.cmenu,.qbar,.pv{background:var(--panel)!important}");
        css.Append(".btn{min-height:28px;border-radius:8px;border-color:var(--edge);background:transparent}.btn:hover{border-color:var(--edge-hover)}.btn.primary{background:var(--accent);border-color:var(--accent)}.btn[aria-pressed=true],.tools .btn[aria-pressed=true],.hud .btn[aria-pressed=true]{background:var(--sel);border-color:var(--accent);color:var(--ink)}");
        return "(()=>{const put=()=>{let s=document.getElementById('app-theme');if(!s){s=document.createElement('style');s.id='app-theme';(document.head||document.documentElement).appendChild(s);}"
            + "s.textContent=" + JsonSerializer.Serialize(css.ToString()) + ";if(s.nextSibling)s.parentNode.appendChild(s);};"
            + "const watch=()=>{const u=document.getElementById('undo'),r=document.getElementById('redo');if(!u||!r||!window.chrome?.webview)return;const send=()=>chrome.webview.postMessage({type:'history',undo:!u.disabled,redo:!r.disabled});new MutationObserver(send).observe(u,{attributes:true});new MutationObserver(send).observe(r,{attributes:true});send()};"
            + "if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',watch);else watch();"
            + "if(!window.__pinned){window.__pinned=1;document.addEventListener('scroll',e=>{const t=e.target===document?document.scrollingElement:e.target;if(t&&t.matches&&t.matches('html,body,.app,.main')&&(t.scrollLeft||t.scrollTop)){t.scrollLeft=0;t.scrollTop=0}},true)}"
            + "if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',put);else put();})();";
    }

    private static string CssColor(string key) =>
        Application.Current.TryFindResource(key) is SolidColorBrush brush
            ? string.Create(CultureInfo.InvariantCulture, $"rgba({brush.Color.R},{brush.Color.G},{brush.Color.B},{brush.Color.A / 255.0 * brush.Opacity:0.###})")
            : "transparent";

    private static string ScrollBarCss()
    {
        const int Taper = 7, Fade = 9, Bar = 11, RailFade = 48, RailInset = 2;
        string rail = CssColor("Line");

        static string Cap(string color, int thick, bool start, bool across)
        {
            string P(double along, double side) => across ? FormattableString.Invariant($"{along},{side}") : FormattableString.Invariant($"{side},{along}");
            string points = start
                ? $"{P(0, thick / 2.0)} {P(Taper, thick)} {P(Fade, thick)} {P(Fade, 0)} {P(Taper, 0)}"
                : $"{P(0, 0)} {P(0, thick)} {P(Fade - Taper, thick)} {P(Fade, thick / 2.0)} {P(Fade - Taper, 0)}";
            var (x2, y2) = across ? ("1", "0") : ("0", "1");
            var (from, to) = start ? ("0", "1") : ("1", "0");
            string w = (across ? Fade : thick).ToString(CultureInfo.InvariantCulture), h = (across ? thick : Fade).ToString(CultureInfo.InvariantCulture);
            string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}'><defs><linearGradient id='g' x2='{x2}' y2='{y2}'><stop offset='0' stop-color='{color}' stop-opacity='{from}'/><stop offset='1' stop-color='{color}' stop-opacity='{to}'/></linearGradient></defs><polygon points='{points}' fill='url(%23g)'/></svg>";
            return "url(\"data:image/svg+xml," + svg.Replace("<", "%3C").Replace(">", "%3E").Replace("\"", "'") + "\")";
        }

        static string Thumb(string color, int thick, bool across)
        {
            string size = across ? $"{Fade}px {thick}px" : $"{thick}px {Fade}px";
            string middle = across ? $"calc(100% - {2 * Fade}px) {thick}px" : $"{thick}px calc(100% - {2 * Fade}px)";
            var (first, last) = across ? ("left center", "right center") : ("center top", "center bottom");
            return $"background:{Cap(color, thick, true, across)} {first}/{size} no-repeat,{Cap(color, thick, false, across)} {last}/{size} no-repeat,linear-gradient({color},{color}) center/{middle} no-repeat";
        }

        string thumb = CssColor("ScrollThumbBrushHover"), drag = CssColor("ScrollThumbBrushDrag");
        return $"::-webkit-scrollbar{{width:{Bar}px;height:{Bar}px;background:transparent}}::-webkit-scrollbar-corner{{background:transparent}}"
            + $"::-webkit-scrollbar-track:vertical{{background:linear-gradient(to bottom,transparent {RailInset}px,{rail} {RailInset + RailFade}px,{rail} calc(100% - {RailInset + RailFade}px),transparent calc(100% - {RailInset}px)) center/1px 100% no-repeat}}"
            + $"::-webkit-scrollbar-track:horizontal{{background:linear-gradient(to right,transparent {RailInset}px,{rail} {RailInset + RailFade}px,{rail} calc(100% - {RailInset + RailFade}px),transparent calc(100% - {RailInset}px)) center/100% 1px no-repeat}}"
            + $"::-webkit-scrollbar-thumb:vertical{{{Thumb(thumb, 4, false)}}}::-webkit-scrollbar-thumb:vertical:hover,::-webkit-scrollbar-thumb:vertical:active{{{Thumb(drag, 6, false)}}}"
            + $"::-webkit-scrollbar-thumb:horizontal{{{Thumb(thumb, 4, true)}}}::-webkit-scrollbar-thumb:horizontal:hover,::-webkit-scrollbar-thumb:horizontal:active{{{Thumb(drag, 6, true)}}}"
            + "::-webkit-scrollbar-button{display:none}";
    }

    private void Fail(string message)
    {
        Close();
        _status.Text = message;
    }

    private static bool IsOwn(string uri) =>
        uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex OwnVersionName = new(@" \(\d+\)\.json$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string VersionsFolder => Path.Combine(Folder, "versions");

    private static object ListVersions(int? id)
    {
        var list = new List<JsonElement>();
        if (Directory.Exists(VersionsFolder))
            foreach (string file in Directory.EnumerateFiles(VersionsFolder, "*.json"))
            {
                try
                {
                    using var parsed = JsonDocument.Parse(File.ReadAllBytes(file));
                    if (parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("data", out _))
                        list.Add(parsed.RootElement.Clone());
                }
                catch (JsonException) { }
            }
        list.Sort((a, b) => VersionId(b).CompareTo(VersionId(a)));
        return new { id, list };
    }

    private static double VersionId(JsonElement version) =>
        version.TryGetProperty("id", out var value) && value.TryGetDouble(out double number) ? number : 0;

    private static string VersionFile(JsonElement version)
    {
        string name = Text(version, "name") ?? "Version";
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        if (safe.Length > 40) safe = safe[..40].Trim();
        return $"{(safe.Length == 0 ? "Version" : safe)} ({VersionId(version).ToString("0", CultureInfo.InvariantCulture)}).json";
    }

    private static object SetVersions(JsonElement message, int? id)
    {
        if (!message.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) throw new ArgumentException("No versions were sent.");
        Directory.CreateDirectory(VersionsFolder);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in list.EnumerateArray())
        {
            if (version.ValueKind != JsonValueKind.Object) continue;
            string path = Path.Combine(VersionsFolder, VersionFile(version));
            keep.Add(path);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(version.GetRawText());
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
            File.WriteAllBytes(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        foreach (string file in Directory.EnumerateFiles(VersionsFolder, "*.json"))
            if (!keep.Contains(Path.GetFullPath(file)) && OwnVersionName.IsMatch(Path.GetFileName(file))) File.Delete(file);
        return new { id };
    }

    private static object OpenVersions(int? id)
    {
        Directory.CreateDirectory(VersionsFolder);
        if (!External.Allowed("versions folder")) return new { id };
        try { Process.Start(new ProcessStartInfo(VersionsFolder) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return new { id, error = ex.Message }; }
        return new { id };
    }

    private static void OpenOutside(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var link) || link.Scheme is not ("https" or "http")) return;
        if (!External.Allowed("open a link")) return;
        try { Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { App.LogError(ex); }
    }
    private static string WriteSite(string root)
    {
        string site = Path.Combine(root, "app");
        Directory.CreateDirectory(site);
        var assembly = typeof(LiveryShopPage).Assembly;
        const string prefix = "livery-shop/";
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string relative = resource[prefix.Length..].Replace('\\', '/');
            string path = Path.GetFullPath(Path.Combine(site, relative));
            if (!path.StartsWith(Path.GetFullPath(site), StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        if (!File.Exists(Path.Combine(site, "index.html"))) throw new InvalidOperationException("Livery Shop's page is missing from this build.");
        string fonts = Path.Combine(site, "fonts");
        Directory.CreateDirectory(fonts);
        foreach (var font in new[] { "Saira/Saira-Regular.ttf", "Saira/Saira-SemiBold.ttf", "Saira/Saira-Bold.ttf", "Saira/Condensed/SairaCondensed-Bold.ttf", "Saira/Condensed/SairaCondensed-ExtraBold.ttf" })
        {
            string target = Path.Combine(fonts, Path.GetFileName(font));
            if (File.Exists(target)) continue;
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Core;component/Assets/Fonts/{font}"));
            if (info is null) continue;
            using var source = info.Stream;
            using var file = File.Create(target);
            source.CopyTo(file);
        }
        return site;
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement message;
        try
        {
            using var parsed = JsonDocument.Parse(e.WebMessageAsJson);
            message = parsed.RootElement.Clone();
        }
        catch (JsonException) { return; }
        if (message.ValueKind != JsonValueKind.Object) return;
        int? id = message.TryGetProperty("id", out var raw) && raw.TryGetInt32(out var n) ? n : null;
        try
        {
            switch (Text(message, "type"))
            {
                case "history":
                    _undo.IsEnabled = message.TryGetProperty("undo", out var u) && u.ValueKind == JsonValueKind.True;
                    _redo.IsEnabled = message.TryGetProperty("redo", out var r) && r.ValueKind == JsonValueKind.True;
                    break;
                case "versions" when !_demo:
                    Post(await Task.Run(() => ListVersions(id)));
                    break;
                case "versions-set" when !_demo:
                    Post(await Task.Run(() => SetVersions(message, id)));
                    break;
                case "versions-open" when !_demo:
                    Post(OpenVersions(id));
                    break;
                case "hello":
                    Post(await Task.Run(() => HostInfo(_demo)));
                    PostTradingPaints();
                    break;
                case "save" when _demo:
                    Post(new { id, error = "Saving into iRacing is off in demo mode." });
                    break;
                case "save":
                    Post(await SaveAsync(message, id));
                    break;
                case "preview":
                    Post(await PreviewAsync(message, id));
                    break;
                case "template":
                    Post(await TemplateAsync(message, id));
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            if (ex is IOException or UnauthorizedAccessException) App.LogError(ex);
            Post(new { id, error = ex.Message });
        }
    }

    private static string? _monkeyScript;

    internal async Task<(string Done, string[] Errors)?> MonkeyStepAsync(int seed)
    {
        if (_web?.CoreWebView2 is not { } core || _web.Visibility != Visibility.Visible) return null;
        if (_monkeyScript is null)
        {
            using var stream = typeof(LiveryShopPage).Assembly.GetManifestResourceStream("livery-monkey.js");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            _monkeyScript = reader.ReadToEnd();
        }
        string raw = await core.ExecuteScriptAsync(_monkeyScript.Replace("SEED", seed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        if (JsonSerializer.Deserialize<string>(raw) is not { } json) return ("script returned nothing", []);
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        return (Text(root, "done") ?? "?", root.GetProperty("errors").EnumerateArray().Select(e => e.GetString() ?? "").ToArray());
    }

    private void Post(object payload)
    {
        if (_web?.CoreWebView2 is { } core) core.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static object HostInfo(bool demo)
    {
        var (member, car) = IracingPaint.Recent();
        var cars = IracingPaint.Cars();
        if (car is not null && IracingPaint.IsCarDir(car) && !cars.Any(c => c.Dir.Equals(car, StringComparison.OrdinalIgnoreCase)))
            cars.Add((car, car));
        return new
        {
            type = "host",
            id = member,
            car,
            paintFolder = IracingPaint.Folder,
            versions = !demo,
            sample = demo,
            cars = cars.Select(c => new { dir = c.Dir, name = c.Name }).ToList(),
        };
    }

    private async Task<object> TemplateAsync(JsonElement message, int? id)
    {
        string kind = Text(message, "kind") is "helmet" or "suit" ? Text(message, "kind")! : "car";
        string? car = Text(message, "car");
        if (kind == "car" && (car is null || !IracingPaint.IsCarDir(car))) throw new InvalidOperationException("Pick the car in the Export tab first.");
        if (PaintTemplates.Url(kind, car) is not { } url)
            return new { id, error = "There's no template to download for this car. Cars with locked paint can't be painted; for any other car, download it in iRacing: My Content → Car Manager → pick the car → Download Template." };
        string site = Path.Combine(Root, "app");
        string path = await PaintTemplates.GetAsync(url, Path.Combine(site, "templates"));
        string relative = string.Join('/', Path.GetRelativePath(site, path).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString));
        return new { id, url = relative, name = Path.GetFileName(path) };
    }

    private async Task<object> SaveAsync(JsonElement message, int? id)
    {
        string kind = Text(message, "kind") is "helmet" or "suit" ? Text(message, "kind")! : "car";
        string? car = Text(message, "car");
        var files = new List<(string Name, byte[] Bytes)>();
        if (message.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var file in list.EnumerateArray())
                files.Add((Text(file, "name") ?? "", Convert.FromBase64String(Text(file, "data") ?? "")));
        if (files.Count == 0) throw new InvalidOperationException("There was nothing to save.");
        _guard?.Saving();
        string folder;
        try { folder = await Task.Run(() => IracingPaint.Save(kind, car, files)); }
        catch
        {
            _guard?.SaveFailed();
            throw;
        }
        _guard?.Saved(folder, files);
        bool inSim = AppWatcher.IsRunningNow(SimRacing.Telemetry.TelemetryService.SimProcess);
        if (inSim) ReloadSoon();
        return new { id, folder, reloaded = inSim };
    }
    private void StartGuards()
    {
        if (_demo || _guard is not null) return;
        _guard = new PaintGuard();
        _guard.Replaced += files => Post(new { type = "replaced", files });
        CompanionApp.Hold(CompanionApp.TradingPaints, Consumer, true);
        string exe = _app.Plugins.Find(Plugins.PluginIds.SimRacing) is Plugins.BuiltIn.SimRacingPlugin { TradingPaints: { } tp } && tp.ExePath is { } path
            ? path : "Trading Paints.exe";
        _tradingPaints = AppWatcher.Normalize(exe);
        _app.Watcher.Started += OnProcessChanged;
        _app.Watcher.Stopped += OnProcessChanged;
        _app.Watcher.Acquire(Consumer, new[] { _tradingPaints });
    }

    private void StopGuards()
    {
        if (_guard is null) return;
        _guard.Dispose();
        _guard = null;
        _app.Watcher.Started -= OnProcessChanged;
        _app.Watcher.Stopped -= OnProcessChanged;
        _app.Watcher.Release(Consumer);
        _tradingPaints = null;
        CompanionApp.Hold(CompanionApp.TradingPaints, Consumer, false);
    }

    private void OnProcessChanged(string name)
    {
        if (name == _tradingPaints) PostTradingPaints();
    }

    private void PostTradingPaints()
    {
        if (_tradingPaints is null) return;
        Post(new { type = "tp", running = _app.Watcher.IsRunning(_tradingPaints) });
    }

    private void ReloadSoon()
    {
        if (_reload is null)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _ = Task.Run(IracingPaint.ReloadInSim);
            };
            _reload = timer;
        }
        _reload.Stop();
        _reload.Start();
    }

    private static async Task<object> PreviewAsync(JsonElement message, int? id)
    {
        string? car = Text(message, "car"), file = Text(message, "file");
        if (car is null || !IracingPaint.IsCarDir(car) || file is null || !IracingPaint.IsPaintFile(file))
            throw new InvalidOperationException("Pick the car in the Export tab first.");
        if (IracingPaint.Folder is not { } paint) throw new InvalidOperationException("iRacing's Documents folder wasn't found. Is iRacing installed?");
        if (!AppWatcher.IsRunningNow(UiProcess))
            return new { id, error = "Open the iRacing app to see the preview: iRacing draws the car picture itself." };
        string path = Path.Combine(paint, car, file);
        if (!File.Exists(path)) throw new InvalidOperationException("Save your paint first.");
        try
        {
            byte[] png = await LiveryPictures.RenderAsync(car, path);
            return new { id, png = "data:image/png;base64," + Convert.ToBase64String(png) };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new { id, error = "iRacing didn't draw the car. The preview works from the iRacing app's menus, so leave any session, then press Refresh." };
        }
    }
}
