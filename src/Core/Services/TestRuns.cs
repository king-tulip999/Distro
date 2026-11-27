using System.Windows;
using System.Windows.Media;

namespace Core.Services;

internal interface IScreenshotStates
{
    IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates();
}

internal static partial class ScreenshotRunner
{
    public static bool Active { get; private set; }

    public static string? Folder { get; private set; }

    public static bool RealDpi => Environment.GetCommandLineArgs().Contains("--real-dpi", StringComparer.OrdinalIgnoreCase);

    public static bool DrawsAt96 => Active && !RealDpi;

    public static bool Quick => Environment.GetCommandLineArgs().Contains("--quick", StringComparer.OrdinalIgnoreCase);

    public static void RecordError(Exception ex) => Record(ex);

    static partial void Record(Exception ex);

    public static void CaptureWindow(Window window, string path) => CaptureFor(window, path);

    static partial void CaptureFor(Window window, string path);

    public static List<string> Lint(Visual root, string where)
    {
        var found = new List<string>();
        LintInto(root, where, found);
        return found;
    }

    static partial void LintInto(Visual root, string where, List<string> found);
}

internal static partial class MonkeyRunner
{
    public static bool Active { get; private set; }

    public static void RecordError(Exception ex) => Record(ex);

    static partial void Record(Exception ex);
}

internal static partial class ControlSweep
{
    public static bool Active { get; private set; }

    public static void RecordError(Exception ex) => Record(ex);

    static partial void Record(Exception ex);
}

internal static partial class PerfProbe
{
    public static string? File { get; set; }

    public static void Mark(string name) => Record(name);

    static partial void Record(string name);
}
