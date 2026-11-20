using System.IO;
using Core.Config;
using Core.SimRacing.Hud.Vr;

namespace Core.Services;

internal static class Uninstaller
{
    private static readonly string[] LeftoverEndings = { ".cmd", ".download", ".xml", ".tmp", ".part" };

    public const string Action = "uninstall";

    public static string[] Prepare()
    {
        string exe = Environment.ProcessPath!;
        if (AppIdentity.IsTecnica) FanService.StopRunning(TimeSpan.FromSeconds(12));
        else OpenXrLayer.Remove();
        try { StartupManager.SetEnabled(false); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException) { }
        return Steps(exe, Folders(), Files(exe), Path.GetDirectoryName(exe) is { } home && AppHome.IsOwnFolder(home) ? home : StartupManager.InstallFolder);
    }

    internal static List<string> Folders()
    {
        string name = AppIdentity.Name;
        var folders = new List<string>
        {
            ConfigStore.Folder,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name),
            Path.Combine(Path.GetTempPath(), ".net", name),
        };
        string shared = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        try
        {
            foreach (var folder in Directory.GetDirectories(shared, name + "-*"))
                if (Program.IsRuntimeFolder(folder, shared) || App.IsProtectedScratch(folder)) folders.Add(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return folders;
    }

    internal static List<string> Files(string exe)
    {
        var files = new List<string> { exe + ".old" };
        if (!AppIdentity.IsTecnica) files.Add(Path.Combine(Path.GetTempPath(), "velocita-web-preview.txt"));
        if (Path.GetDirectoryName(exe) is not { Length: > 0 } folder) return files;
        try
        {
            foreach (var file in Directory.GetFiles(folder, AppIdentity.Name + "-*"))
                if (LeftoverEndings.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    files.Add(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return files;
    }

    internal static string[] Steps(string exe, IEnumerable<string> folders, IEnumerable<string> files, string installFolder)
    {
        string target = App.BatchPath(exe);
        var lines = new List<string>
        {
            "set /a tries=0",
            ":remove",
            $"del /f /q {target} >nul 2>&1",
            $"if not exist {target} goto removed",
            "set /a tries+=1",
            $"if %tries% lss 20 ({App.WaitASecond} & goto remove)",
            ":removed",
        };
        foreach (var file in files) lines.Add($"del /f /q {App.BatchPath(file)} >nul 2>&1");
        foreach (var folder in folders) lines.Add($"rd /s /q {App.BatchPath(folder)} >nul 2>&1");
        if (Path.GetDirectoryName(exe) is { } home && SamePath(home, installFolder))
            lines.Add($"cd /d \"%SystemRoot%\" & rd {App.BatchPath(installFolder)} >nul 2>&1");
        return lines.ToArray();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}
