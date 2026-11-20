using System.Diagnostics;
using System.IO;
using Core.Config;
using Core.Processes;

namespace Core.Display;

internal static class ProfileApps
{
    public static string Clean(string typed) => typed.Trim().Trim('"').Trim();

    public static string DisplayName(string path) => Path.GetFileNameWithoutExtension(Clean(path));

    public static string ProcessNameOf(string path) =>
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(path) : "";

    public static List<string> StartMenuShortcuts()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var menu in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
        {
            var folder = Environment.GetFolderPath(menu);
            if (!Directory.Exists(folder)) continue;
            string[] links;
            try { links = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var link in links)
            {
                string name = Path.GetFileNameWithoutExtension(link);
                if (IsHelperShortcut(name)) continue;
                found.TryAdd(name, link);
            }
        }
        return found.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase).Select(f => f.Value).ToList();
    }

    internal static bool IsHelperShortcut(string name) =>
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("readme", StringComparison.OrdinalIgnoreCase)
        || name.Contains("help", StringComparison.OrdinalIgnoreCase) || name.EndsWith(" website", StringComparison.OrdinalIgnoreCase);

    public static List<string> ToLaunch(IEnumerable<string> apps, IReadOnlySet<string> running) =>
        apps.Select(Clean).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(a => ProcessNameOf(a) is not { Length: > 0 } name || !running.Contains(name)).ToList();

    public static List<string> ToClose(DisplayProfile profile, IEnumerable<DisplayProfile> all)
    {
        var keep = profile.Apps.Select(a => ProcessNameOf(Clean(a))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(p => !ReferenceEquals(p, profile) && p.CloseAppsOnLeave).SelectMany(p => p.Apps).Select(a => ProcessNameOf(Clean(a)))
            .Where(n => n.Length > 0 && !keep.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<List<string>> CloseAsync(IReadOnlyList<string> processNames)
    {
        var problems = new List<string>();
        if (processNames.Count == 0) return problems;
        int session = Process.GetCurrentProcess().SessionId;
        var names = processNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            bool ours;
            try { ours = names.Contains(p.ProcessName) && p.SessionId == session; }
            catch { ours = false; }
            if (ours) targets.Add(p); else p.Dispose();
        }
        try
        {
            foreach (var p in targets) { try { p.CloseMainWindow(); } catch { } }
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && targets.Any(Running))
                await Task.Delay(250);
            foreach (var p in targets.Where(Running))
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); }
                catch (Exception ex) { problems.Add($"Apps: couldn't close {p.ProcessName} ({ex.Message})"); }
            }
        }
        finally
        {
            foreach (var p in targets) p.Dispose();
        }
        return problems.Distinct().ToList();
    }

    private static bool Running(Process p)
    {
        try { return !p.HasExited; }
        catch { return false; }
    }

    public static IEnumerable<string> Launch(IReadOnlyList<string> apps)
    {
        if (apps.Count == 0) return Array.Empty<string>();
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p) running.Add(p.ProcessName);
        }
        var problems = new List<string>();
        foreach (var app in ToLaunch(apps, running))
            if (ProcessLauncher.Launch(app) is { } error)
                problems.Add("Apps: " + error);
        return problems;
    }
}
