using System.Runtime.InteropServices;
using System.Windows.Interop;
using Core.Services;

namespace Core.Views;

internal static class SimHubEmbed
{
    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000, WsClipSiblings = 0x04000000;
    private const int WmDestroy = 0x0002;

    private static HwndSource? _source;
    private static SimHubPanel? _panel;

    public static void Attach(AppServices app, IntPtr parent)
    {
        if (!IsWindow(parent)) return;
        if (_source is { } open && GetParent(open.Handle) == parent) return;
        Detach();
        GetClientRect(parent, out var room);
        var source = new HwndSource(new HwndSourceParameters("Velocita")
        {
            ParentWindow = parent,
            WindowStyle = WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            PositionX = 0,
            PositionY = 0,
            Width = Math.Max(1, room.Right - room.Left),
            Height = Math.Max(1, room.Bottom - room.Top),
        });
        source.AddHook(Hook);
        _panel ??= new SimHubPanel(app);
        source.RootVisual = _panel;
        _source = source;
        _panel.Show();
    }

    public static void Detach()
    {
        if (_source is not { } source) return;
        _source = null;
        _panel?.Hide();
        source.RemoveHook(Hook);
        source.RootVisual = null;
        source.Dispose();
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDestroy && _source?.Handle == hwnd)
        {
            var source = _source;
            _source = null;
            _panel?.Hide();
            source.RemoveHook(Hook);
            source.RootVisual = null;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
}
