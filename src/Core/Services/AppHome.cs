using System.IO;
using System.Runtime.InteropServices;

namespace Core.Services;

internal enum PassingPlace { Downloads, Temporary }

internal static class AppHome
{
    public static string Folder => AppIdentity.NeedsAdmin
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppIdentity.Name)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppIdentity.Name);

    public static PassingPlace? PassingThrough(string exe) => PassingThrough(exe, Folder, Places());

    private static (PassingPlace, string?)[] Places() => new[] { (PassingPlace.Downloads, DownloadsFolder()), (PassingPlace.Temporary, Path.GetTempPath()) };

    internal static PassingPlace? PassingThrough(string exe, string home, IEnumerable<(PassingPlace Place, string? Folder)> places)
    {
        if (Path.GetDirectoryName(exe) is not { Length: > 0 } folder || home.Length > 0 && Inside(folder, home)) return null;
        foreach (var (place, root) in places)
            if (!string.IsNullOrWhiteSpace(root) && Inside(folder, root))
                return place;
        return null;
    }

    private static bool Inside(string folder, string root)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static string InstallFolderFor(string picked, string? name = null)
    {
        string folder = Path.TrimEndingDirectorySeparator(picked);
        return IsOwnFolder(folder, name) ? folder : Path.Combine(folder, name ?? AppIdentity.Name);
    }

    internal static bool IsOwnFolder(string? folder, string? name = null) =>
        folder is { Length: > 0 } && Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).Equals(name ?? AppIdentity.Name, StringComparison.OrdinalIgnoreCase);

    public static bool PassesThrough(string folder) => PassingThrough(Path.Combine(folder, AppIdentity.ExeName), "", Places()) is not null;

    public static string Move(string folder)
    {
        string exe = Environment.ProcessPath!;
        string target = Path.Combine(folder, AppIdentity.ExeName);
        Directory.CreateDirectory(folder);
        string part = target + ".part";
        File.Copy(exe, part, overwrite: true);
        File.Move(part, target, overwrite: true);
        bool startsWithWindows;
        try { startsWithWindows = StartupManager.State().Enabled; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { startsWithWindows = false; }
        if (startsWithWindows) StartupManager.SetEnabled(true, target);
        return target;
    }

    internal static string[] RemoveSteps(string exe)
    {
        string target = App.BatchPath(exe);
        return new[]
        {
            "set /a tries=0",
            ":remove",
            $"del /f /q {target} >nul 2>&1",
            $"if not exist {target} goto removed",
            "set /a tries+=1",
            $"if %tries% lss 10 ({App.WaitASecond} & goto remove)",
            ":removed",
            $"del /f /q {App.BatchPath(exe + ".old")} >nul 2>&1",
        };
    }

    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    private static string? DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out string path) == 0 && path.Length > 0) return path;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException) { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out string path);
}
