using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Core.SimRacing.Hud.Vr;

internal static class OpenXrLayer
{
    public const string ResourceName = "VelocitaXr.dll";
    public const string LayerName = "XR_APILAYER_VELOCITA_overlay";
    private const string ImplicitKey = @"Software\Khronos\OpenXR\1\ApiLayers\Implicit";
    private const string DllFile = "VelocitaXr.dll", ManifestFile = "VelocitaXr.json";

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Name, "OpenXR");

    public static string ManifestPath => Path.Combine(Folder, ManifestFile);

    public static bool Bundled => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ResourceName) is not null;

    public static string Manifest() =>
        "{\n" +
        "  \"file_format_version\": \"1.0.0\",\n" +
        "  \"api_layer\": {\n" +
        $"    \"name\": \"{LayerName}\",\n" +
        $"    \"library_path\": \".\\\\{DllFile}\",\n" +
        "    \"api_version\": \"1.0\",\n" +
        "    \"implementation_version\": \"1\",\n" +
        $"    \"description\": \"{AppIdentity.Name} VR widgets\",\n" +
        "    \"disable_environment\": \"DISABLE_XR_APILAYER_VELOCITA_OVERLAY\"\n" +
        "  }\n" +
        "}\n";

    public static string? Install()
    {
        if (!Bundled) return "This build has no OpenXR layer.";
        try
        {
            Directory.CreateDirectory(Folder);
            byte[] dll;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)!)
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                dll = memory.ToArray();
            }
            string dllPath = Path.Combine(Folder, DllFile);
            if (!File.Exists(dllPath) || !File.ReadAllBytes(dllPath).AsSpan().SequenceEqual(dll))
            {
                try { File.WriteAllBytes(dllPath, dll); }
                catch (IOException) when (File.Exists(dllPath)) { }
            }
            string manifest = Manifest();
            if (!File.Exists(ManifestPath) || File.ReadAllText(ManifestPath) != manifest) File.WriteAllText(ManifestPath, manifest);
            using var key = Registry.CurrentUser.CreateSubKey(ImplicitKey, true);
            if (key.GetValue(ManifestPath) is not int on || on != 0) key.SetValue(ManifestPath, 0, RegistryValueKind.DWord);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "The OpenXR layer couldn't be set up: " + ex.Message;
        }
    }

    public static void Remove()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ImplicitKey, true);
            if (key?.GetValue(ManifestPath) is not null) key.DeleteValue(ManifestPath, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
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
}
