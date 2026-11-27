using System.IO;

namespace Core.Config;

internal static class OldData
{
    private static readonly string[] TecnicaFiles = { "config.json", "temp-history.json" };

    private static readonly string[] VelocitaFiles =
    {
        "config.json", "pitloss.json",
        "stats.db", "stats.db-wal", "stats.db-shm",
        "traces.db", "traces.db-wal", "traces.db-shm",
    };

    private static readonly string[] VelocitaFolders = { "grr", "iRacing", "liveries" };

    public static string OldFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.OldDataFolder);

    public static string VelocitaFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Velocita");

    public const string LiveryFolder = "livery-shop";

    public static bool CopyOnFirstStart()
    {
        if (!AppIdentity.IsLivrea) return Copy(OldFolder, ConfigStore.Folder, AppIdentity.Kind);
        CopyLiveries(VelocitaFolder, ConfigStore.Folder);
        return false;
    }

    private static readonly HashSet<string> RebuiltFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "Cache", "Code Cache", "GPUCache", "GrShaderCache", "ShaderCache", "DawnGraphiteCache", "DawnWebGPUCache", "Crashpad",
    };

    internal static void CopyLiveries(string velocita, string to)
    {
        string from = Path.Combine(velocita, LiveryFolder), target = Path.Combine(to, LiveryFolder);
        if (!Directory.Exists(from) || File.Exists(Path.Combine(to, "config.json")) || Directory.Exists(target)) return;
        try { CopyFolder(from, target, RebuiltFolders); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static bool Copy(string from, string to, AppKind kind)
    {
        if (kind == AppKind.Livrea || !Directory.Exists(from) || File.Exists(Path.Combine(to, "config.json"))) return false;
        try
        {
            Directory.CreateDirectory(to);
            foreach (var name in kind == AppKind.Tecnica ? TecnicaFiles : VelocitaFiles)
                CopyFile(Path.Combine(from, name), Path.Combine(to, name));
            if (kind == AppKind.Velocita)
                foreach (var folder in VelocitaFolders)
                    CopyFolder(Path.Combine(from, folder), Path.Combine(to, folder));
            return File.Exists(Path.Combine(to, "config.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyFile(string from, string to)
    {
        if (!File.Exists(from) || File.Exists(to)) return;
        try { File.Copy(from, to); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void CopyFolder(string from, string to, HashSet<string>? skip = null)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            CopyFile(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.GetDirectories(from))
            if (skip is null || !skip.Contains(Path.GetFileName(folder)))
                CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)), skip);
    }
}
