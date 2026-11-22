using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace VelocitaSimHubPlugin;

internal sealed class SettingsView : UserControl
{
    private readonly CheckBox _on = new() { Content = "Run Velocita", Margin = new Thickness(24, 12, 24, 4), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = new() { Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly PanelHost _host = new() { Visibility = Visibility.Hidden };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _asked = DateTime.MinValue;

    public SettingsView()
    {
        MinHeight = 560;
        var body = new Grid { Children = { _status, _host } };
        Grid.SetRow(body, 1);
        Content = new Grid
        {
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() },
            Children = { _on, body },
        };
        _on.IsChecked = Engine.On;
        _on.Checked += (_, _) => { Engine.SetOn(true); Refresh(); };
        _on.Unchecked += (_, _) => { Engine.SetOn(false); Refresh(); };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        if (_on.IsChecked != Engine.On) _on.IsChecked = Engine.On;
        bool shown = Engine.On && _host.HasPanel;
        _host.Visibility = shown ? Visibility.Visible : Visibility.Hidden;
        _status.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        if (shown)
        {
            _host.FitPanel();
            return;
        }
        _status.Text = !Engine.On ? "Velocita is off: its overlays stay blank."
            : !Engine.Installed ? Engine.ExeName + " is missing from the SimHub folder: copy it next to Velocita.SimHub.Plugin.dll and restart SimHub."
            : Engine.Problem is { } problem && !Engine.Running ? "Velocita isn't running: " + problem
            : "Loading Velocita…";
        if (Engine.Closing || !Engine.On || !Engine.Installed || _host.Handle == IntPtr.Zero) return;
        if (!Engine.Running)
        {
            if (DateTime.UtcNow - _asked < TimeSpan.FromSeconds(5)) return;
            _asked = DateTime.UtcNow;
            Engine.Start();
            return;
        }
        if (DateTime.UtcNow - _asked < TimeSpan.FromSeconds(1)) return;
        _asked = DateTime.UtcNow;
        Engine.Embed(_host.Handle);
    }

    private sealed class PanelHost : HwndHost
    {
        private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000;
        private const uint GwChild = 5, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

        public bool HasPanel => Handle != IntPtr.Zero && GetWindow(Handle, GwChild) != IntPtr.Zero;

        public void FitPanel()
        {
            if (Handle == IntPtr.Zero || !GetClientRect(Handle, out var room)) return;
            var child = GetWindow(Handle, GwChild);
            if (child == IntPtr.Zero) return;
            SetWindowPos(child, IntPtr.Zero, 0, 0, room.Right - room.Left, room.Bottom - room.Top, SwpNoZOrder | SwpNoActivate);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var handle = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren, 0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Engine.AddHost(handle);
            return new HandleRef(this, handle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Engine.Release(hwnd.Handle);
            DestroyWindow(hwnd.Handle);
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            FitPanel();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
