using System.Diagnostics;
using System.IO;
using System.Reflection;
using Core.Services;
using Microsoft.Win32;

namespace Core.SimRacing.Hud.Vr;

internal static class OpenXrLayer
{
    public const string ResourceName = "VelocitaXr.dll";
    public const string LayerName = "XR_APILAYER_VELOCITA_overlay";
    private const string ImplicitKey = @"Software\Khronos\OpenXR\1\ApiLayers\Implicit";
    private const string DllFile = "VelocitaXr.dll", ManifestFile = "VelocitaXr.json";

    // Velocita and Velocita for SimHub register the same layer: each holds a named claim while its VR widgets are on,
    // and neither removes the layer while the other still has a claim.
    private static readonly string[] Claimants = { "Velocita", "Velocita SimHub" };
    private static Mutex? _claim;

    private static string ClaimName(string dataName) => @"Local\VelocitaXrLayer." + dataName.Replace(' ', '.');

    public static void Claim(bool on)
    {
        if (on) _claim ??= new Mutex(false, ClaimName(AppIdentity.DataName));
        else
        {
            _claim?.Dispose();
            _claim = null;
        }
    }

    public static bool ClaimedElsewhere()
    {
        foreach (string name in Claimants)
        {
            if (string.Equals(name, AppIdentity.DataName, StringComparison.OrdinalIgnoreCase)) continue;
            if (Mutex.TryOpenExisting(ClaimName(name), out var other)) { other.Dispose(); return true; }
        }
        return false;
    }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Name, "OpenXR");

    public static string ManifestPath => Path.Combine(Folder, ManifestFile);

    public static bool Bundled => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ResourceName) is not null;

    public static string Manifest() => Manifest(AppIdentity.Name);

    private static string Manifest(string owner) =>
        "{\n" +
        "  \"file_format_version\": \"1.0.0\",\n" +
        "  \"api_layer\": {\n" +
        $"    \"name\": \"{LayerName}\",\n" +
        $"    \"library_path\": \".\\\\{DllFile}\",\n" +
        "    \"api_version\": \"1.0\",\n" +
        "    \"implementation_version\": \"1\",\n" +
        $"    \"description\": \"{owner} VR widgets\",\n" +
        "    \"disable_environment\": \"DISABLE_XR_APILAYER_VELOCITA_OVERLAY\"\n" +
        "  }\n" +
        "}\n";

    private static byte[] EmbeddedDll()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool SameBytes(string path, byte[] bytes) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);

    public static string? Install()
    {
        if (!Bundled) return "This build has no OpenXR layer.";
        try
        {
            Directory.CreateDirectory(Folder);
            byte[] dll = EmbeddedDll();
            string dllPath = Path.Combine(Folder, DllFile);
            if (!SameBytes(dllPath, dll))
            {
                try { File.WriteAllBytes(dllPath, dll); }
                catch (IOException) when (File.Exists(dllPath)) { }
            }
            string manifest = Manifest();
            if (!File.Exists(ManifestPath) || File.ReadAllText(ManifestPath) != manifest) File.WriteAllText(ManifestPath, manifest);
            using var key = Registry.CurrentUser.CreateSubKey(ImplicitKey, true);
            if (key.GetValue(ManifestPath) is not int on || on != 0 || !IsLast(key.GetValueNames(), ManifestPath))
            {
                key.DeleteValue(ManifestPath, false);
                key.SetValue(ManifestPath, 0, RegistryValueKind.DWord);
            }
            RegisteredHere = true;
            SetSignInCleanup(true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "The OpenXR layer couldn't be set up: " + ex.Message;
        }
    }

    internal static bool IsLast(IReadOnlyList<string> names, string manifest) =>
        names.Count > 0 && string.Equals(names[^1], manifest, StringComparison.OrdinalIgnoreCase);

    internal static bool WantRegistered(bool vrOn, bool iracingRunning) => vrOn && iracingRunning;

    internal static bool MayUnregister(bool iracingRunning, bool claimedElsewhere) => !(iracingRunning && claimedElsewhere);

    public static bool RegisteredHere { get; private set; }

    public static bool LeftoverRemoved { get; private set; }

    public static void Unregister(bool leftover = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ImplicitKey, true);
            if (key?.GetValue(ManifestPath) is not null)
            {
                key.DeleteValue(ManifestPath, false);
                if (leftover) LeftoverRemoved = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        SetSignInCleanup(false);
        RegisteredHere = false;
    }

    public static void UnregisterAfterCrash()
    {
        try
        {
            if (RegisteredHere && !ClaimedElsewhere()) Unregister();
        }
        catch { }
    }

    public const string CleanupArg = "--openxr-cleanup";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce", RunOnceValue = "Velocita VR layer cleanup";
    internal const int RunOnceMaxChars = 260;

    internal static string? CleanupCommand(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || exe.Contains('"')) return null;
        string command = $"\"{exe}\" {CleanupArg}";
        return command.Length <= RunOnceMaxChars ? command : null;
    }

    private static void SetSignInCleanup(bool on)
    {
        try
        {
            if (on)
            {
                if (CleanupCommand(Environment.ProcessPath) is not { } command) return;
                using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey, true);
                key.SetValue(RunOnceValue, command, RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunOnceKey, true);
                if (key?.GetValue(RunOnceValue) is not null) key.DeleteValue(RunOnceValue, false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    public static bool RunCleanupStep(string[] args)
    {
        if (!args.Contains(CleanupArg, StringComparer.OrdinalIgnoreCase)) return false;
        Unregister();
        return true;
    }

    public static bool Registered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ImplicitKey);
                return key?.GetValue(ManifestPath) is not null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    public static void Remove()
    {
        Unregister();
        foreach (var file in new[] { Path.Combine(Folder, DllFile), ManifestPath })
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool Installed
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ImplicitKey);
                return key?.GetValue(ManifestPath) is int on && on == 0 && File.Exists(Path.Combine(Folder, DllFile));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    public const string MachineArg = "--openxr-for-admin";
    private const string MachineRemoveArg = "remove";
    internal const int MachineDone = 0, MachineFailed = 1, MachineNotAdmin = 2, MachineInUse = 3;

    public static string MachineFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Velocita", "OpenXR");

    public static string MachineManifestPath => Path.Combine(MachineFolder, ManifestFile);

    private static RegistryKey? OpenMachineKey(bool write) =>
        write
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(ImplicitKey, true)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(ImplicitKey);

    public static bool MachineInstalled
    {
        get
        {
            try
            {
                using var key = OpenMachineKey(false);
                return key?.GetValue(MachineManifestPath) is int on && on == 0 && File.Exists(Path.Combine(MachineFolder, DllFile));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    public static bool MachineCurrent
    {
        get
        {
            if (!Bundled || !MachineInstalled) return false;
            try { return SameBytes(Path.Combine(MachineFolder, DllFile), EmbeddedDll()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    public static int? RunMachineStep(string[] args)
    {
        int at = Array.FindIndex(args, a => string.Equals(a, MachineArg, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return null;
        if (!Environment.IsPrivilegedProcess) return MachineNotAdmin;
        bool remove = at + 1 < args.Length && string.Equals(args[at + 1], MachineRemoveArg, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (remove) RemoveMachine();
            else InstallMachine();
            return MachineDone;
        }
        catch (IOException) { return MachineInUse; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return MachineFailed; }
    }

    private static void InstallMachine()
    {
        if (!Bundled) throw new UnauthorizedAccessException("This build has no OpenXR layer.");
        Directory.CreateDirectory(MachineFolder);
        byte[] dll = EmbeddedDll();
        string dllPath = Path.Combine(MachineFolder, DllFile);
        if (!SameBytes(dllPath, dll)) File.WriteAllBytes(dllPath, dll);
        string manifest = Manifest("Velocita");
        if (!File.Exists(MachineManifestPath) || File.ReadAllText(MachineManifestPath) != manifest) File.WriteAllText(MachineManifestPath, manifest);
        using var key = OpenMachineKey(true)!;
        key.SetValue(MachineManifestPath, 0, RegistryValueKind.DWord);
    }

    private static void RemoveMachine()
    {
        using (var key = OpenMachineKey(true))
            if (key?.GetValue(MachineManifestPath) is not null) key.DeleteValue(MachineManifestPath, false);
        foreach (var file in new[] { Path.Combine(MachineFolder, DllFile), MachineManifestPath })
            if (File.Exists(file)) File.Delete(file);
        if (Directory.Exists(MachineFolder) && !Directory.EnumerateFileSystemEntries(MachineFolder).Any()) Directory.Delete(MachineFolder);
        string parent = Path.GetDirectoryName(MachineFolder)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }

    public static async Task<string?> SetUpMachine(bool remove)
    {
        if (!External.Allowed(remove ? "remove the VR layer for iRacing as administrator" : "set up the VR layer for iRacing as administrator")) return null;
        if (Environment.ProcessPath is not { } exe) return "Velocita's own file couldn't be found.";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, remove ? MachineArg + " " + MachineRemoveArg : MachineArg) { UseShellExecute = true, Verb = "runas" });
            if (process is null) return "Windows didn't start the setup.";
            await process.WaitForExitAsync();
            return process.ExitCode switch
            {
                MachineDone => null,
                MachineInUse => "Close iRacing first, then try again.",
                _ => "The setup didn't finish. Windows refused the change.",
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "The setup couldn't start: " + ex.Message;
        }
    }
}
