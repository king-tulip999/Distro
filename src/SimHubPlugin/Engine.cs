using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace VelocitaSimHubPlugin;

internal static class Engine
{
    public const string ExeName = "VelocitaSimHub.exe";
    private const string InstanceName = @"Local\Velocita SimHub.SingleInstance";
    private const string EmbedName = @"Local\Velocita SimHub.Embed";

    public static string Folder => Path.GetDirectoryName(typeof(Engine).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;

    public static string ExePath => Path.Combine(Folder, ExeName);

    public static bool Installed => File.Exists(ExePath);

    public static string? Problem { get; private set; }

    private static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Velocita SimHub");

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

    public static void Start()
    {
        if (!Installed)
        {
            Problem = ExeName + " is missing from the SimHub folder.";
            return;
        }
        string simHub = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        int pid;
        using (var self = Process.GetCurrentProcess()) pid = self.Id;
        string args = "--simhub \"" + simHub + "\" --parent " + pid;
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

    public static void Embed(IntPtr panelParent)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(Path.Combine(DataFolder, "embed.txt"), panelParent.ToInt64().ToString(CultureInfo.InvariantCulture));
            using var embed = EventWaitHandle.OpenExisting(EmbedName);
            embed.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
        }
    }
}
