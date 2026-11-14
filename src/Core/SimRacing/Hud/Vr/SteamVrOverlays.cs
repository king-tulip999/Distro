using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Config;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Core.SimRacing.Hud.Vr;

internal sealed class SteamVrOverlays : IDisposable
{
    private const int ApplicationOverlay = 2, UniverseSeated = 0, HeadDevice = 0, EventQuit = 700, EventSize = 64;
    private const int TextureDirectX = 0, ColorSpaceGamma = 1;
    private const uint FlagPremultiplied = 1u << 21;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FactoryFn([MarshalAs(UnmanagedType.LPStr)] string name, out int code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn(IntPtr self, int type, IntPtr startup);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CleanupFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GenericFn(IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string name, out int error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateFn([MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string name, out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleFn(ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FlagFn(ulong handle, uint flag, [MarshalAs(UnmanagedType.I1)] bool on);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FloatFn(ulong handle, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UintFn(ulong handle, uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AbsoluteFn(ulong handle, int origin, float[] matrix);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RelativeFn(ulong handle, uint device, float[] matrix);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TextureFn(ulong handle, ref Texture texture);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool PollFn(byte[] buffer, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void QuitFn();

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture
    {
        public IntPtr Handle;
        public int Type;
        public int ColorSpace;
    }

    private sealed class Overlay
    {
        public ulong Handle;
        public ID3D11Texture2D? Texture;
        public int W, H;
        public bool Shown;
        public (HudVrPlacement Mode, double X, double Y, double Distance, double Width, int Opacity) Placed;
    }

    private IntPtr _library, _core;
    private CleanupFn? _cleanup;
    private CreateFn? _create;
    private HandleFn? _destroy, _show, _hide;
    private FlagFn? _flag;
    private FloatFn? _alpha, _width;
    private UintFn? _sort;
    private AbsoluteFn? _absolute;
    private RelativeFn? _relative;
    private TextureFn? _texture;
    private PollFn? _poll;
    private QuitFn? _quit;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private readonly Dictionary<string, Overlay> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _event = new byte[EventSize];
    private uint _nextSort;

    public bool Connected { get; private set; }

    public string? Problem { get; private set; }

    public static string? RuntimeFolder()
    {
        try
        {
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("runtime", out var runtimes) || runtimes.ValueKind != JsonValueKind.Array) return null;
            foreach (var r in runtimes.EnumerateArray())
                if (r.GetString() is { Length: > 0 } path && Directory.Exists(path)) return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    public bool Connect()
    {
        if (Connected) return true;
        try
        {
            if (RuntimeFolder() is not { } runtime) { Problem = "SteamVR isn't installed."; return false; }
            string dll = new[] { Path.Combine(runtime, "bin", "win64", "vrclient_x64.dll"), Path.Combine(runtime, "bin", "vrclient_x64.dll") }.FirstOrDefault(File.Exists) ?? "";
            if (dll.Length == 0) { Problem = "SteamVR's files weren't found."; return false; }
            _library = NativeLibrary.Load(dll);
            var factory = Marshal.GetDelegateForFunctionPointer<FactoryFn>(NativeLibrary.GetExport(_library, "VRClientCoreFactory"));
            _core = factory("IVRClientCore_003", out _);
            if (_core == IntPtr.Zero) { Problem = "This SteamVR version isn't supported."; Unload(); return false; }
            IntPtr table = Marshal.ReadIntPtr(_core);
            var init = Marshal.GetDelegateForFunctionPointer<InitFn>(Marshal.ReadIntPtr(table, 0));
            _cleanup = Marshal.GetDelegateForFunctionPointer<CleanupFn>(Marshal.ReadIntPtr(table, IntPtr.Size));
            var generic = Marshal.GetDelegateForFunctionPointer<GenericFn>(Marshal.ReadIntPtr(table, 3 * IntPtr.Size));
            int error = init(_core, ApplicationOverlay, IntPtr.Zero);
            if (error != 0) { Problem = $"SteamVR didn't accept the connection (error {error})."; _cleanup = null; Unload(); return false; }
            IntPtr overlay = generic(_core, "FnTable:IVROverlay_028", out error);
            if (overlay == IntPtr.Zero) { Problem = "This SteamVR version is too old for overlays. Update SteamVR."; Disconnect(); return false; }
            BindOverlay(overlay);
            IntPtr system = generic(_core, "FnTable:IVRSystem_026", out _);
            if (system != IntPtr.Zero)
            {
                _poll = Marshal.GetDelegateForFunctionPointer<PollFn>(Marshal.ReadIntPtr(system, 30 * IntPtr.Size));
                _quit = Marshal.GetDelegateForFunctionPointer<QuitFn>(Marshal.ReadIntPtr(system, 47 * IntPtr.Size));
            }
            var result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0 }, out var device, out var context);
            if (result.Failure || device is null || context is null) { Problem = "Direct3D 11 isn't available."; Disconnect(); return false; }
            _device = device;
            _context = context;
            Connected = true;
            Problem = null;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException or InvalidOperationException)
        {
            Problem = "SteamVR couldn't be reached: " + ex.Message;
            Disconnect();
            return false;
        }
    }

    private void BindOverlay(IntPtr t)
    {
        T At<T>(int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(t, index * IntPtr.Size));
        _create = At<CreateFn>(1);
        _destroy = At<HandleFn>(3);
        _flag = At<FlagFn>(11);
        _alpha = At<FloatFn>(16);
        _sort = At<UintFn>(20);
        _width = At<FloatFn>(22);
        _absolute = At<AbsoluteFn>(33);
        _relative = At<RelativeFn>(35);
        _show = At<HandleFn>(43);
        _hide = At<HandleFn>(44);
        _texture = At<TextureFn>(60);
    }

    public void Pump()
    {
        if (!Connected || _poll is null) return;
        for (int i = 0; i < 64 && _poll(_event, EventSize); i++)
            if (BitConverter.ToUInt32(_event, 0) == EventQuit)
            {
                _quit?.Invoke();
                Disconnect();
                return;
            }
    }

    public void Show(string id, string name, HudWidgetConfig c, int width, int height, IntPtr pixels)
    {
        if (!Connected || _create is null || _device is null || _context is null) return;
        if (!_overlays.TryGetValue(id, out var o))
        {
            if (_create("velocita." + id, AppIdentity.Name + " " + name, out ulong handle) != 0) return;
            o = new Overlay { Handle = handle };
            _overlays[id] = o;
            _flag!(handle, FlagPremultiplied, true);
            _sort!(handle, ++_nextSort);
        }
        if (o.Texture is null || o.W != width || o.H != height)
        {
            o.Texture?.Dispose();
            o.Texture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.ShaderResource));
            o.W = width;
            o.H = height;
        }
        _context.UpdateSubresource(o.Texture, 0, null, pixels, (uint)(width * 4), 0);
        _context.Flush();
        var tex = new Texture { Handle = o.Texture.NativePointer, Type = TextureDirectX, ColorSpace = ColorSpaceGamma };
        _texture!(o.Handle, ref tex);
        var placed = (c.Vr, c.VrX, c.VrY, c.VrDistance, c.VrWidth, c.Opacity);
        if (placed != o.Placed)
        {
            o.Placed = placed;
            _width!(o.Handle, (float)c.VrWidth);
            _alpha!(o.Handle, c.Opacity / 100f);
            var m = VrPlacement.Matrix(c.VrX, c.VrY, c.VrDistance);
            if (c.Vr == HudVrPlacement.Head) _relative!(o.Handle, HeadDevice, m);
            else _absolute!(o.Handle, UniverseSeated, m);
        }
        if (!o.Shown)
        {
            _show!(o.Handle);
            o.Shown = true;
        }
    }

    public void Hide(string id)
    {
        if (!Connected || !_overlays.TryGetValue(id, out var o) || !o.Shown) return;
        _hide!(o.Handle);
        o.Shown = false;
    }

    public void Remove(string id)
    {
        if (!_overlays.Remove(id, out var o)) return;
        if (Connected) _destroy!(o.Handle);
        o.Texture?.Dispose();
    }

    public void Disconnect()
    {
        foreach (var id in _overlays.Keys.ToList()) Remove(id);
        _context?.ClearState();
        _context?.Dispose();
        _device?.Dispose();
        _context = null;
        _device = null;
        Connected = false;
        if (_core != IntPtr.Zero && _cleanup is not null)
        {
            try { _cleanup(_core); }
            catch (SEHException) { }
        }
        _cleanup = null;
        _create = null;
        _destroy = _show = _hide = null;
        _flag = null;
        _alpha = _width = null;
        _sort = null;
        _absolute = null;
        _relative = null;
        _texture = null;
        _poll = null;
        _quit = null;
        Unload();
    }

    private void Unload()
    {
        _core = IntPtr.Zero;
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
        _library = IntPtr.Zero;
    }

    public void Dispose() => Disconnect();
}
