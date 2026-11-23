using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Core.Config;

namespace Core.Services;

internal sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, string? ExeUrl, long ExeSize, string? ChecksumUrl, DateTime? Published = null, string? SignatureUrl = null, string? Title = null, bool SignatureList = false);

internal enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Failed, NotConfigured }

internal sealed class UpdateService
{
    public const string Repository = "king-tulip999/Distro";

    public static string ProjectPage => $"https://github.com/{Repository}";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";

    public static string DiscordInvite => AppIdentity.IsTecnica ? "https://discord.gg/Ehj8DUZrdr" : "https://discord.gg/WeWs6spYep";

    private readonly AppServices _app;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private static HttpClient? _http;
    internal static HttpClient Http => _http ??= CreateClient();

    private readonly DispatcherTimer _retry = new();
    private readonly DispatcherTimer _autoWait = new() { Interval = TimeSpan.FromMinutes(5) };
    private int _retries;
    private string? _autoFailedTag;
    private bool _autoInstalling;

    public UpdateService(AppServices app)
    {
        _app = app;
        _timer.Tick += (_, _) => _ = CheckAsync(manual: false);
        _retry.Tick += (_, _) => { _retry.Stop(); _ = CheckAsync(manual: false); };
        _autoWait.Tick += (_, _) => TryAutoInstall();
    }

    internal static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60) };

    public UpdateState State { get; private set; } = Repository.Length == 0 ? UpdateState.NotConfigured : UpdateState.Idle;
    public ReleaseInfo? Latest { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }

    public event Action? Changed;

    public static Version Current { get; } = RunningVersion();

    private static Version RunningVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var named = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        return named is not null && TryVersion(named, out var version) ? version : Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));
    }

    public Version Installed => InstalledVersion(Current, S.InstalledTag);

    internal static Version InstalledVersion(Version current, string? installedTag) =>
        installedTag is not null && TryVersion(installedTag, out var tagged) && tagged > current ? tagged : current;

    public bool Available => Latest is { } latest
        && (State is UpdateState.Available or UpdateState.Downloading || State == UpdateState.Checking && latest.Version > Installed);

    public static bool CanSelfInstall => Environment.ProcessPath is not null && !File.Exists(Path.Combine(AppContext.BaseDirectory, AppIdentity.Name + ".dll"));

    private UpdateSettings S => _app.Config.Updates;


    public void Start()
    {
        if (Environment.GetCommandLineArgs().Contains("--update-failed", StringComparer.OrdinalIgnoreCase))
        {
            S.InstalledTag = null;
            _app.Save();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                _app.Notify("Update didn't install", "The file couldn't be replaced (in use, or no permission), so this version kept running. You can download it from the release page instead.",
                    NoticeKind.Warning, important: true)));
        }
        if (!_app.Demo) NoteSeenVersion(Environment.GetCommandLineArgs().Contains("--updated", StringComparer.OrdinalIgnoreCase));
        if (JustUpdated && Environment.GetCommandLineArgs().Contains(StayInTrayArg, StringComparer.OrdinalIgnoreCase))
            _app.Notify($"{AppIdentity.Name} updated to {Show(Installed)}", "Open it to see what's new.", NoticeKind.Success);
        if (_app.Demo || Repository.Length == 0) return;
        _timer.Start();
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        delay.Tick += (_, _) => { delay.Stop(); _ = CheckAsync(manual: false); };
        delay.Start();
    }

    public bool JustUpdated { get; private set; }

    public Version? UpdatedFrom { get; private set; }

    private void NoteSeenVersion(bool updatedArg)
    {
        (JustUpdated, UpdatedFrom) = Seen(Installed, S.SeenVersion, updatedArg);
        var tag = Tag(Installed);
        if (S.SeenVersion == tag) return;
        S.SeenVersion = tag;
        _app.Save();
    }

    internal static (bool JustUpdated, Version? From) Seen(Version installed, string? seenVersion, bool updatedArg)
    {
        if (string.IsNullOrWhiteSpace(seenVersion) || !TryVersion(seenVersion, out var seen)) return (updatedArg, null);
        return installed > seen ? (true, seen) : (false, null);
    }

    public const string StayInTrayArg = "--stay-in-tray";

    public bool CanInstallItself(ReleaseInfo release) =>
        !_app.Demo && CanSelfInstall && release.ExeUrl is not null && release.SignatureUrl is not null;

    public void AutoInstallChanged()
    {
        if (S.AutoInstall) TryAutoInstall();
        else _autoWait.Stop();
    }

    public string WaitingFor => AppIdentity.IsTecnica ? "a program with an automatic window placement is open" : "iRacing is open";

    private void TryAutoInstall()
    {
        _autoWait.Stop();
        if (!S.AutoInstall || _autoInstalling || State != UpdateState.Available || Latest is not { } release || release.Version <= Installed
            || !CanInstallItself(release) || release.Tag == _autoFailedTag) return;
        if (!SafeToInstall())
        {
            _autoWait.Start();
            return;
        }
        _autoInstalling = true;
        _ = InstallAsync(auto: true).ContinueWith(_ => _autoInstalling = false, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool SafeToInstall()
    {
        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true, IsActive: true }) return false;
        return !BusyPrograms().Any(_app.Watcher.IsRunning);
    }

    private IEnumerable<string> BusyPrograms() => AppIdentity.IsTecnica
        ? _app.Config.WindowProfiles.Profiles.Where(p => p.Auto && p.ProcessName.Length > 0).Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase)
        : new[] { SimRacing.IracingFiles.IracingPaths.SimProcess };

    public async Task CheckAsync(bool manual)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading) return;
        if (Repository.Length == 0)
        {
            Set(UpdateState.NotConfigured);
            return;
        }
        Set(UpdateState.Checking);
        try
        {
            Latest = Newest(await Http.GetStringAsync(ReleasesApi));
            S.LastCheckUtc = DateTime.UtcNow;
            _app.Save();
            _retries = 0;
            if (Latest is not null && Latest.Version > Installed)
            {
                Set(UpdateState.Available);
                bool auto = S.AutoInstall && CanInstallItself(Latest) && Latest.Tag != _autoFailedTag;
                if (manual || !auto && S.NotifiedVersion != Latest.Tag)
                {
                    S.NotifiedVersion = Latest.Tag;
                    _app.Notify("Update available", auto ? $"It installs by itself when {AppIdentity.Name} isn't in use, or install it now from the title bar."
                        : "Install it from the title bar or Settings → About and updates.", NoticeKind.Info, important: !auto);
                }
                if (auto) TryAutoInstall();
            }
            else
            {
                Set(UpdateState.UpToDate);
                if (manual) _app.Notify("You're up to date", $"You have the latest version of {AppIdentity.Name}.", NoticeKind.Success);
            }
        }
        catch (Exception ex)
        {
            Error = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? "The release page couldn't be found." : ex.Message;
            bool stillAvailable = !manual && Latest is { } known && known.Version > Installed;
            Set(stillAvailable ? UpdateState.Available : UpdateState.Failed);
            if (!manual && _retries < RetryDelays.Length)
            {
                _retry.Interval = RetryDelays[_retries++];
                _retry.Start();
            }
            if (manual) _app.Notify("Couldn't check for updates", Error, NoticeKind.Warning);
        }
    }

    public async Task InstallAsync(bool auto = false)
    {
        if (Latest is not { } release || State is UpdateState.Downloading or UpdateState.Checking) return;
        if (_app.Demo || release.ExeUrl is null || release.SignatureUrl is null || !CanSelfInstall)
        {
            OpenReleasePage();
            return;
        }
        var current = Environment.ProcessPath!;
        string? installedBefore = S.InstalledTag;
        string scratch = "", download = "";
        try
        {
            scratch = App.Scratch(current);
            download = Path.Combine(scratch, $"{AppIdentity.Name}-{Show(release.Version)}.download");
            Set(UpdateState.Downloading);
            Progress = 0;
            using (var response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? release.ExeSize;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(download);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                using var stall = new CancellationTokenSource(StallTimeout);
                while ((read = await ReadOrStall(source, buffer, stall)) > 0)
                {
                    stall.CancelAfter(StallTimeout);
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0 && (int)(100.0 * done / total) != (int)(100 * Progress))
                    {
                        Progress = (double)done / total;
                        Changed?.Invoke();
                    }
                }
            }
            byte[] hash;
            await using (var check = File.OpenRead(download))
                hash = await SHA256.HashDataAsync(check);
            await VerifyAsync(release, hash);

            S.InstalledTag = release.Tag;
            _app.SaveNow();
            bool hidden = System.Windows.Application.Current?.MainWindow is not { IsVisible: true };
            App.RelaunchAfterExit(auto && hidden ? "--updated " + StayInTrayArg : "--updated", SwapSteps(current, download), current, scratch);
        }
        catch (Exception ex)
        {
            try { if (download.Length > 0) File.Delete(download); } catch { }
            App.RemoveScratch(scratch);
            if (S.InstalledTag != installedBefore)
            {
                S.InstalledTag = installedBefore;
                _app.SaveNow();
            }
            Error = ex.Message;
            if (auto) _autoFailedTag = release.Tag;
            Set(UpdateState.Available);
            _app.Notify("Update didn't install", ex.Message + " You can download it from the release page instead.", NoticeKind.Warning, important: true);
        }
    }

    internal const string ReleaseKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEkPe9uVC+Yxp3Ybb9OI3KTwO31FqWYIYNlcelxIxeQR14LsmoHUPRFBjKYwhhESQso/8IA2RaIcLqhEedLBW4dw==";

    public const string SignatureListName = "signatures.txt";

    internal static async Task VerifyAsync(ReleaseInfo release, byte[] hash)
    {
        if (release.ChecksumUrl is not null)
        {
            var expected = (await Http.GetStringAsync(release.ChecksumUrl)).Trim().Split(' ', '\t')[0];
            if (!Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The download didn't match the release's checksum, so it wasn't installed.");
        }
        string text = release.SignatureUrl is null ? "" : await Http.GetStringAsync(release.SignatureUrl);
        if (!SignedByRelease(hash, release.SignatureList ? ListedSignature(text, AssetName) ?? "" : text))
            throw new InvalidOperationException($"The download isn't signed with {AppIdentity.Name}'s release key, so it wasn't installed.");
    }

    internal static string? ListedSignature(string list, string asset)
    {
        foreach (var line in list.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Equals(asset, StringComparison.OrdinalIgnoreCase)) return parts[1].Trim();
        }
        return null;
    }

    internal static bool SignedByRelease(byte[] hash, string signature, string publicKey = ReleaseKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyHash(hash, Convert.FromBase64String(signature.Trim()));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    internal static string TrustedPage(string? url) =>
        url is not null && url.StartsWith($"https://github.com/{Repository}/", StringComparison.OrdinalIgnoreCase) ? url : ReleasesPage;

    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    internal static async Task<int> ReadOrStall(Stream source, byte[] buffer, CancellationTokenSource stall)
    {
        try { return await source.ReadAsync(buffer, stall.Token); }
        catch (OperationCanceledException) when (stall.IsCancellationRequested) { throw new IOException("The download stopped (no data for 30 seconds)."); }
    }

    internal static string[] SwapSteps(string current, string download)
    {
        string exe = App.BatchPath(current), old = App.BatchPath(current + ".old"), downloaded = App.BatchPath(download);
        string failed = $"(start \"\" {exe} --update-failed & del {downloaded} >nul 2>&1 & del \"%~f0\" & exit /b 1)";
        return new[]
        {
            "set /a tries=0",
            ":swap",
            $"move /y {exe} {old} >nul 2>&1 && goto copy",
            "set /a tries+=1",
            $"if %tries% lss 10 ({App.WaitASecond} & goto swap) else {failed}",
            ":copy",
            $"copy /y {downloaded} {exe} >nul || (move /y {old} {exe} >nul & {failed[1..^1]})",
            $"del {old} >nul 2>&1",
            $"del {downloaded} >nul 2>&1",
        };
    }

    public void OpenReleasePage()
    {
        if (Repository.Length > 0) Views.Ui.OpenUrl(Latest?.PageUrl ?? ReleasesPage);
    }

    public static string Show(Version v) => $"{v.Major}.{MinorText(v.Minor)}.{MinorText(Math.Max(0, v.Build))}" + (v.Revision > 0 ? $".{v.Revision}" : "");

    public static string Tag(Version v) =>
        $"v{v.Major}.{MinorText(v.Minor)}" + (v.Build > 0 || v.Revision > 0 ? $".{MinorText(Math.Max(0, v.Build))}" : "") + (v.Revision > 0 ? $".{v.Revision}" : "");

    private const int MinorDigits = 3;

    private static int DecimalMinor(int minor) => DecimalMinor(minor.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static int DecimalMinor(string digits) =>
        int.Parse(digits.Length >= MinorDigits ? digits : digits.PadRight(MinorDigits, '0'), System.Globalization.CultureInfo.InvariantCulture);

    private static string MinorText(int minor)
    {
        if (minor >= 1000) return minor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text = minor.ToString("000", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
        return text.Length == 0 ? "0" : text;
    }

    internal static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    internal static string ReleasesApi => $"https://api.github.com/repos/{Repository}/releases?per_page=30";

    public async Task<List<ReleaseInfo>> ReleasesUpToInstalledAsync()
    {
        if (Repository.Length == 0) throw new InvalidOperationException("No release page is set up for this build.");
        try { return Releases(await Http.GetStringAsync(ReleasesApi), Installed); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { throw new InvalidOperationException("The release page couldn't be found.", ex); }
    }

    internal static List<ReleaseInfo> Releases(string json, Version? upTo = null)
    {
        using var doc = JsonDocument.Parse(json);
        IEnumerable<ReleaseInfo?> found = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.Object && !(r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True))
                .Select(Parse)
            : new[] { Parse(doc.RootElement) };
        return found.OfType<ReleaseInfo>()
            .Where(r => upTo is null || r.Version <= upTo)
            .OrderByDescending(r => r.Version)
            .ThenByDescending(r => r.Published)
            .ToList();
    }

    internal static ReleaseInfo? Newest(string json) => Releases(json).FirstOrDefault();

    private static ReleaseInfo? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagName) || tagName.ValueKind != JsonValueKind.String) return null;
        var tag = tagName.GetString() ?? "";
        if (!TryVersion(tag, out var version)) return null;
        string? exe = null, checksum = null, signature = null;
        bool listed = false;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            static string? Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var list = assets.EnumerateArray()
                .Select(a => (Name: Str(a, "name") ?? "", Url: Str(a, "browser_download_url"), Size: a.ValueKind == JsonValueKind.Object && a.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0))
                .ToList();
            var pick = list.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase));
            if (pick.Url is not null)
            {
                exe = pick.Url;
                size = pick.Size;
                checksum = list.FirstOrDefault(a => a.Name.Equals(pick.Name + ".sha256", StringComparison.OrdinalIgnoreCase)).Url;
                signature = list.FirstOrDefault(a => a.Name.Equals(pick.Name + ".sig", StringComparison.OrdinalIgnoreCase)).Url;
                if (signature is null)
                {
                    signature = list.FirstOrDefault(a => a.Name.Equals(SignatureListName, StringComparison.OrdinalIgnoreCase)).Url;
                    listed = signature is not null;
                }
            }
        }
        DateTime? published = root.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var when) ? when : null;
        string? title = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(title) || string.Equals(title, tag, StringComparison.OrdinalIgnoreCase)) title = null;
        return new ReleaseInfo(version, tag, root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "",
            TrustedPage(root.TryGetProperty("html_url", out var page) && page.ValueKind == JsonValueKind.String ? page.GetString() : null), exe, size, checksum, published, signature, title, listed);
    }

    public const string SimHubZip = "Velocita-SimHub.zip";

    internal static string AssetName => AppIdentity.SimHub ? SimHubZip : AppIdentity.ExeName;

    internal static bool TryVersion(string tag, out Version version)
    {
        var digits = new string(tag.SkipWhile(c => !char.IsDigit(c)).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var v))
        {
            var parts = digits.Split('.');
            int minor = parts.Length > 1 ? DecimalMinor(parts[1]) : 0;
            int build = parts.Length > 2 ? DecimalMinor(parts[2]) : 0;
            version = v.Revision > 0 ? new(v.Major, minor, build, v.Revision) : new(v.Major, minor, build);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static Version Normalize(Version v) =>
        v.Revision > 0 ? new(v.Major, DecimalMinor(v.Minor), DecimalMinor(Math.Max(0, v.Build)), v.Revision) : new(v.Major, DecimalMinor(v.Minor), DecimalMinor(Math.Max(0, v.Build)));

    internal void ShowForScreenshots(ReleaseInfo? release)
    {
        Latest = release;
        Set(release is null ? UpdateState.UpToDate : UpdateState.Available);
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.Name + "/" + Current);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
