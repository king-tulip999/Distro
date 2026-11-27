using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Threading;

namespace Core.Services;

internal enum SimHubUpdateState { Idle, Checking, UpToDate, Ready, Failed }

internal sealed class SimHubUpdater
{
    public const string PluginFile = "Velocita.SimHub.Plugin.dll";
    internal static readonly string[] Files = { AppIdentity.SimHubExe, PluginFile };

    private readonly string _simHub;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _first = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _busy;

    public SimHubUpdater(string simHubFolder)
    {
        _simHub = simHubFolder;
        _timer.Tick += (_, _) => _ = CheckAsync();
        _first.Tick += (_, _) => { _first.Stop(); _ = CheckAsync(); };
    }

    public ReleaseInfo? Ready { get; private set; }
    public SimHubUpdateState State { get; private set; }
    public event Action? Changed;
    private string? _staged;

    public void Start()
    {
        _first.Start();
        _timer.Start();
    }

    public void Stop()
    {
        _first.Stop();
        _timer.Stop();
    }

    public void CheckNow() => _ = CheckAsync();

    private void SetState(SimHubUpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private async Task CheckAsync()
    {
        if (_busy || Ready is not null) return;
        _busy = true;
        SetState(SimHubUpdateState.Checking);
        var result = SimHubUpdateState.Failed;
        string? scratch = null;
        try
        {
            var latest = UpdateService.Newest(await UpdateService.ReleasesJsonAsync());
            if (latest is null || latest.Version <= UpdateService.Current)
            {
                result = SimHubUpdateState.UpToDate;
                return;
            }
            if (latest.ExeUrl is null || latest.SignatureUrl is null) return;
            scratch = Environment.IsPrivilegedProcess ? App.ProtectedScratch() : Path.Combine(Path.GetTempPath(), "velocita-simhub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            string zip = Path.Combine(scratch, UpdateService.SimHubZip);
            await Download(latest.ExeUrl, zip);
            byte[] hash;
            await using (var read = File.OpenRead(zip))
                hash = await SHA256.HashDataAsync(read);
            await UpdateService.VerifyAsync(latest, hash);
            Extract(zip, Path.Combine(scratch, "files"));
            File.Delete(zip);
            _staged = scratch;
            scratch = null;
            Ready = latest;
            result = SimHubUpdateState.Ready;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TaskCanceledException or System.Text.Json.JsonException)
        {
            App.LogError(ex);
        }
        finally
        {
            if (scratch is not null) Remove(scratch);
            _busy = false;
            SetState(result);
        }
    }

    private static async Task Download(string url, string file)
    {
        using var response = await UpdateService.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(file);
        var buffer = new byte[81920];
        using var stall = new CancellationTokenSource(UpdateService.StallTimeout);
        int read;
        while ((read = await UpdateService.ReadOrStall(source, buffer, stall)) > 0)
        {
            stall.CancelAfter(UpdateService.StallTimeout);
            await target.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    internal static void Extract(string zip, string folder)
    {
        Directory.CreateDirectory(folder);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var name in Files)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(name + " is missing from the update");
            entry.ExtractToFile(Path.Combine(folder, name), overwrite: true);
        }
    }

    public bool InstallOnExit()
    {
        if (Ready is null || _staged is not { } staged || Environment.ProcessPath is not { } exe) return false;
        if (!Core.Services.External.Allowed("update")) return false;
        string files = Path.Combine(staged, "files");
        App.RunAfterExit("update", InstallSteps(files, _simHub), exe, staged);
        return true;
    }

    internal static IEnumerable<string> InstallSteps(string files, string simHub)
    {
        string target = App.BatchPath(simHub.TrimEnd('\\'));
        var steps = new List<string> { "set /a tries=0", ":copy" };
        foreach (var name in Files)
            steps.Add($"copy /y {App.BatchPath(Path.Combine(files, name))} {target} >nul 2>&1 || goto retry");
        steps.Add("goto done");
        steps.Add(":retry");
        steps.Add("set /a tries+=1");
        steps.Add($"if %tries% lss 30 ({App.WaitASecond} & goto copy)");
        steps.Add(":done");
        steps.Add($"rd /s /q {App.BatchPath(files)} >nul 2>&1");
        return steps;
    }

    private static void Remove(string folder)
    {
        if (App.IsProtectedScratch(folder)) { App.RemoveScratch(folder); return; }
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
