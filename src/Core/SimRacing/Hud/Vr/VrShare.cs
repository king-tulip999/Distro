using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Core.Config;

namespace Core.SimRacing.Hud.Vr;

internal sealed class VrShare : IDisposable
{
    public const string MapName = "Local\\VelocitaVr";
    public const uint Magic = 0x31525856, Version = 1;
    public const int MaxSlots = 16, HeaderBytes = 4096, SlotPixelBytes = 2 * 1024 * 1024, MaxPixels = SlotPixelBytes / 4;
    private const int SlotHeaderStart = 32, SlotHeaderBytes = 64, ReaderFreshMs = 3000;

    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private IntPtr _base;
    private byte[] _faded = Array.Empty<byte>();
    private readonly string _name;

    public VrShare(string name = MapName) => _name = name;

    public bool Open => _view is not null;

    public bool ReaderAlive => _view is { } v && Environment.TickCount64 - v.ReadInt64(16) < ReaderFreshMs;

    public void Start()
    {
        if (_view is not null) return;
        _file = MemoryMappedFile.CreateOrOpen(_name, HeaderBytes + (long)MaxSlots * SlotPixelBytes, MemoryMappedFileAccess.ReadWrite);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        _base = _view.SafeMemoryMappedViewHandle.DangerousGetHandle() + (nint)_view.PointerOffset;
        _view.Write(8, Environment.TickCount64);
        _view.Write(24, 0u);
        _view.Write(4, Version);
        Thread.MemoryBarrier();
        _view.Write(0, Magic);
    }

    public void Beat(int count)
    {
        if (_view is not { } v) return;
        v.Write(8, Environment.TickCount64);
        v.Write(24, (uint)Math.Clamp(count, 0, MaxSlots));
    }

    public void Publish(int slot, uint id, HudVrPlacement mode, HudWidgetConfig c, int width, int height, IntPtr pixels)
    {
        if (_view is not { } v || slot is < 0 or >= MaxSlots) return;
        int bytes = width * height * 4;
        if (bytes <= 0 || bytes > SlotPixelBytes) return;
        long at = SlotHeaderStart + (long)slot * SlotHeaderBytes;
        ulong seq = v.ReadUInt64(at);
        if ((seq & 1) != 0) seq++;
        v.Write(at, seq + 1);
        Thread.MemoryBarrier();
        v.Write(at + 8, id);
        v.Write(at + 12, (uint)mode);
        v.Write(at + 16, (float)c.VrX);
        v.Write(at + 20, (float)c.VrY);
        v.Write(at + 24, (float)c.VrDistance);
        v.Write(at + 28, (float)c.VrWidth);
        v.Write(at + 32, (uint)width);
        v.Write(at + 36, (uint)height);
        IntPtr target = _base + HeaderBytes + slot * SlotPixelBytes;
        if (c.Opacity >= 100) CopyMemory(target, pixels, (UIntPtr)bytes);
        else
        {
            if (_faded.Length < bytes) _faded = new byte[SlotPixelBytes];
            Marshal.Copy(pixels, _faded, 0, bytes);
            VrPlacement.Fade(_faded.AsSpan(0, bytes), c.Opacity);
            Marshal.Copy(_faded, 0, target, bytes);
        }
        Thread.MemoryBarrier();
        v.Write(at, seq + 2);
    }

    public void Hide(int slot)
    {
        if (_view is not { } v || slot is < 0 or >= MaxSlots) return;
        long at = SlotHeaderStart + (long)slot * SlotHeaderBytes;
        ulong seq = v.ReadUInt64(at);
        if ((seq & 1) != 0) seq++;
        v.Write(at, seq + 1);
        Thread.MemoryBarrier();
        v.Write(at + 12, 0u);
        Thread.MemoryBarrier();
        v.Write(at, seq + 2);
    }

    public void Dispose()
    {
        if (_view is { } v)
        {
            v.Write(24, 0u);
            v.Write(8, 0L);
        }
        _view?.Dispose();
        _file?.Dispose();
        _view = null;
        _file = null;
        _base = IntPtr.Zero;
        _faded = Array.Empty<byte>();
    }

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);
}
