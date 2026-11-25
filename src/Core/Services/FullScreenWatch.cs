using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Core.Services;

internal sealed class FullScreenWatch : IDisposable
{
    private const uint EventSystemForeground = 0x0003, OutOfContext = 0x0000;
    private const int StyleIndex = -16;
    private const long CaptionStyle = 0x00C00000;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private readonly WinEventProc _proc;
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly System.Text.StringBuilder _className = new(64);
    private IntPtr _hook;

    public bool Active { get; private set; }

    public event Action? Changed;

    public FullScreenWatch()
    {
        _proc = (_, _, _, _, _, _, _) => { Check(); _settle.Stop(); _settle.Start(); };
        _settle.Tick += (_, _) => { _settle.Stop(); Check(); };
    }

    public void Start()
    {
        if (_hook == IntPtr.Zero) _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, OutOfContext);
        Check();
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _settle.Stop();
        Set(false);
    }

    private void Check() => Set(_hook != IntPtr.Zero && FillsItsScreen(GetForegroundWindow()));

    private void Set(bool active)
    {
        if (active == Active) return;
        Active = active;
        Changed?.Invoke();
    }

    private bool FillsItsScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint process);
        if (process == Environment.ProcessId) return false;
        _className.Clear();
        if (GetClassName(hwnd, _className, _className.Capacity) > 0 && ShellClasses.Contains(_className.ToString())) return false;
        if ((GetWindowLongPtr(hwnd, StyleIndex).ToInt64() & CaptionStyle) == CaptionStyle) return false;
        if (!GetWindowRect(hwnd, out var window)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return false;
        var m = info.Monitor;
        return window.Left <= m.Left && window.Top <= m.Top && window.Right >= m.Right && window.Bottom >= m.Bottom;
    }

    public void Dispose() => Stop();
}
