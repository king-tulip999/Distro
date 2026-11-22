using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace VelocitaSimHubPlugin;

internal static class Engine
{
    public const string ExeName = "VelocitaSimHub.exe";
    private const string InstanceName = @"Local\Velocita SimHub.SingleInstance";
    private const string EmbedName = @"Local\Velocita SimHub.Embed";
    private const string DetachName = @"Local\Velocita SimHub.Detach";
    private const string ClosingName = @"Local\Velocita SimHub.Closing";
    private const string QuitName = @"Local\Velocita SimHub.Quit";
    private const string OffFile = "off.txt";
    private const uint GwChild = 5, PmNoRemove = 0;
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);
    private static readonly HashSet<IntPtr> Hosts = new();

    private static bool? _on;

    public static bool Closing { get; private set; }

    public static bool On => _on ??= !File.Exists(Path.Combine(DataFolder, OffFile));

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
        if (Closing || !On) return;
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

    public static void Toggle() => SetOn(!On);

    public static void SetOn(bool on)
    {
        if (Closing || on == On) return;
        try
        {
            Directory.CreateDirectory(DataFolder);
            string path = Path.Combine(DataFolder, OffFile);
            if (on) File.Delete(path);
            else File.WriteAllText(path, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimHub.Logging.Current.Error("Velocita: couldn't save the on / off choice", ex);
        }
        _on = on;
        if (on)
        {
            if (!Running) Start();
            return;
        }
        IntPtr[] hosts;
        lock (Hosts) hosts = Hosts.ToArray();
        if (Signal(DetachName)) WaitForRelease(hosts);
        Signal(QuitName);
    }

    public static void AddHost(IntPtr host)
    {
        lock (Hosts) Hosts.Add(host);
    }

    public static void Release(IntPtr host)
    {
        lock (Hosts) Hosts.Remove(host);
        if (HasPanel(host) && Signal(DetachName)) WaitForRelease(new[] { host });
    }

    public static void Close()
    {
        Closing = true;
        IntPtr[] hosts;
        lock (Hosts) hosts = Hosts.ToArray();
        if (Signal(ClosingName)) WaitForRelease(hosts);
    }

    public static bool HasPanel(IntPtr host) => host != IntPtr.Zero && IsWindow(host) && GetWindow(host, GwChild) != IntPtr.Zero;

    private static bool Signal(string name)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(name);
            signal.Set();
            return true;
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void WaitForRelease(IntPtr[] hosts)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < ReleaseTimeout && Array.Exists(hosts, HasPanel))
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            Thread.Sleep(10);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
}
