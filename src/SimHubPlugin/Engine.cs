using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VelocitaSimHubPlugin;

internal static class Engine
{
    public const string ExeName = "VelocitaSimHub.exe";
    private const string InstanceName = @"Local\Velocita SimHub.SingleInstance";
    private const string ShowName = @"Local\Velocita SimHub.ShowWindow";

    public static string Folder => Path.GetDirectoryName(typeof(Engine).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;

    public static string ExePath => Path.Combine(Folder, ExeName);

    public static bool Installed => File.Exists(ExePath);

    public static string? Problem { get; private set; }

    private static string UpdateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Velocita SimHub", "update.txt");

    public static string? UpdateNote
    {
        get
        {
            try { return File.Exists(UpdateFile) ? File.ReadAllText(UpdateFile).Trim() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    public static bool Running
    {
        get
        {
            try
            {
                using (Mutex.OpenExisting(InstanceName)) return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    public static void Start(bool settings = false)
    {
        if (!Installed)
        {
            Problem = ExeName + " is missing from the SimHub folder.";
            return;
        }
        string simHub = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        int pid;
        using (var self = Process.GetCurrentProcess()) pid = self.Id;
        string args = "--simhub \"" + simHub + "\" --parent " + pid + (settings ? " --settings" : "");
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, args) { UseShellExecute = false, WorkingDirectory = Folder })?.Dispose();
            Problem = null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Problem = ex.Message;
            SimHub.Logging.Current.Error("Velocita: couldn't start " + ExeName, ex);
        }
    }

    public static void OpenSettings()
    {
        if (Running)
        {
            try
            {
                using (var show = EventWaitHandle.OpenExisting(ShowName)) show.Set();
                return;
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
            }
        }
        Start(settings: true);
    }
}
