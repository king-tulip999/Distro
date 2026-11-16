using System.Runtime.InteropServices;

namespace Core.SimRacing.Hud.Vr;

internal sealed class MouseCapture : IDisposable
{
    private const int LowLevelMouse = 14, Injected = 1;
    private const int MouseMove = 0x200, LeftDown = 0x201, LeftUp = 0x202, RightDown = 0x204, RightUp = 0x205, Wheel = 0x20A;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HookData
    {
        public Point Pt;
        public uint MouseData, Flags, Time;
        public IntPtr Extra;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    private readonly HookProc _proc;
    private IntPtr _hook;

    public MouseCapture() => _proc = OnHook;

    public bool Active => _hook != IntPtr.Zero;

    public event Action<int, int>? Moved;
    public event Action<bool>? LeftButton;
    public event Action? RightClick;
    public event Action<int>? Wheeled;

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = SetWindowsHookEx(LowLevelMouse, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || _hook == IntPtr.Zero) return CallNextHookEx(IntPtr.Zero, code, message, data);
        var d = Marshal.PtrToStructure<HookData>(data);
        if ((d.Flags & Injected) != 0) return CallNextHookEx(IntPtr.Zero, code, message, data);
        try
        {
            switch ((int)message)
            {
                case MouseMove:
                    if (GetCursorPos(out var now)) Moved?.Invoke(d.Pt.X - now.X, d.Pt.Y - now.Y);
                    break;
                case LeftDown: LeftButton?.Invoke(true); break;
                case LeftUp: LeftButton?.Invoke(false); break;
                case RightUp: RightClick?.Invoke(); break;
                case RightDown: break;
                case Wheel: Wheeled?.Invoke((short)(d.MouseData >> 16) / 120); break;
            }
        }
        catch (Exception ex)
        {
            Stop();
            App.LogError(ex);
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        return 1;
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
