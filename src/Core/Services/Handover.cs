using System.Diagnostics;
using System.IO;
using Core.Processes;

namespace Core.Services;

internal sealed record RunningCopy(int Pid, string Path, Version Version);

internal static class Handover
{
    public static RunningCopy? OlderCopy()
    {
        int self = Environment.ProcessId;
        int session;
        using (var me = Process.GetCurrentProcess()) session = me.SessionId;
        RunningCopy? found = null;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppIdentity.ExeName)))
        {
            using (p)
            {
                try
                {
                    if (found is not null || p.Id == self || p.SessionId != session) continue;
                    if (ProcessImage.PathOf(p.Id) is not { } path || VersionOf(path) is not { } version) continue;
                    if (IsOlder(version, UpdateService.Current)) found = new RunningCopy(p.Id, path, version);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return found;
    }

    internal static bool IsOlder(Version running, Version starting) => running < starting;

    public static bool Close(RunningCopy copy)
    {
        Process process;
        try { process = Process.GetProcessById(copy.Pid); }
        catch (ArgumentException) { return true; }
        using (process)
        {
            try
            {
                if (SingleInstance.AskToQuit() && process.WaitForExit(10000)) return true;
                if (!process.HasExited) process.Kill();
                return process.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return process.HasExited; }
        }
    }

    public static bool ShouldReplace(string runningPath, string self) =>
        !Path.GetFullPath(runningPath).Equals(Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase)
        && AppHome.PassingThrough(self) is not null
        && AppHome.PassingThrough(runningPath) is null;

    private static Version? VersionOf(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).ProductVersion is { } text && UpdateService.TryVersion(text, out var v) ? v : null; }
        catch (FileNotFoundException) { return null; }
    }
}
