using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Core.Config;

namespace Core.SimRacing.Hud.Vr;

internal sealed class VrShare : IDisposable
{
    public const string MapName = "Local\\VelocitaVr";
    public const uint Magic = 0x31525856, Version = 1;
    public const int MaxSlots = 16, HeaderBytes = 4096, SlotPixelBytes = 2 * 1024 * 1024, MaxPixels = SlotPixelBytes / 4;
    private const int RecenterAt = 28, SlotHeaderStart = 32, SlotHeaderBytes = 64, ReaderFreshMs = 3000;

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

    public void Publish(int slot, uint id, HudVrPlacement mode, VrPose pose, int opacity, int width, int height, IntPtr pixels)
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
        v.Write(at + 16, (float)pose.X);
        v.Write(at + 20, (float)pose.Y);
        v.Write(at + 24, (float)pose.Distance);
        v.Write(at + 28, (float)pose.Width);
        v.Write(at + 32, (uint)width);
        v.Write(at + 36, (uint)height);
        var q = VrPlacement.Rotation(pose.Turn, pose.Tilt);
        v.Write(at + 40, q.X);
        v.Write(at + 44, q.Y);
        v.Write(at + 48, q.Z);
        v.Write(at + 52, q.W);
        v.Write(at + 56, (float)pose.CurveRadius);
        IntPtr target = _base + HeaderBytes + slot * SlotPixelBytes;
        if (opacity >= 100) CopyMemory(target, pixels, (UIntPtr)bytes);
        else
        {
            if (_faded.Length < bytes) _faded = new byte[SlotPixelBytes];
            Marshal.Copy(pixels, _faded, 0, bytes);
            VrPlacement.Fade(_faded.AsSpan(0, bytes), opacity);
            Marshal.Copy(_faded, 0, target, bytes);
        }
        Thread.MemoryBarrier();
        v.Write(at, seq + 2);
    }

    public const int ReportAt = 2048, ReportBytes = 176, RuntimeNameBytes = 128;

    internal readonly record struct LayerReport(long Tick, int EndFrameResult, uint WidgetsSent, uint FramesWithWidgets, uint FramesRejected,
        uint MaxLayers, uint Flags, long Format, int SwapchainResult, string Runtime)
    {
        public bool Cylinder => (Flags & 1) != 0;
        public bool Rejected => (Flags & 2) != 0;
        public bool HasDevice => (Flags & 4) != 0;
        public bool HasFormat => (Flags & 8) != 0;
    }

    public LayerReport? ReadReport()
    {
        if (_view is not { } v) return null;
        long tick = v.ReadInt64(ReportAt);
        if (tick == 0) return null;
        var name = new byte[RuntimeNameBytes];
        v.ReadArray(ReportAt + 48, name, 0, name.Length);
        int end = Array.IndexOf(name, (byte)0);
        string runtime = System.Text.Encoding.UTF8.GetString(name, 0, end < 0 ? name.Length : end);
        return new LayerReport(tick, v.ReadInt32(ReportAt + 8), v.ReadUInt32(ReportAt + 12), v.ReadUInt32(ReportAt + 16), v.ReadUInt32(ReportAt + 20),
            v.ReadUInt32(ReportAt + 24), v.ReadUInt32(ReportAt + 28), v.ReadInt64(ReportAt + 32), v.ReadInt32(ReportAt + 40), runtime);
    }

    public const int HeadAt = 2304, HeadBytes = 40, HeadFreshMs = 500;

    public bool ReadHead(float[] into)
    {
        if (_view is not { } v || into.Length < 12) return false;
        long tick = v.ReadInt64(HeadAt);
        if (tick == 0 || Environment.TickCount64 - tick > HeadFreshMs) return false;
        VrPlacement.FromQuaternion(v.ReadSingle(HeadAt + 8), v.ReadSingle(HeadAt + 12), v.ReadSingle(HeadAt + 16),
            v.ReadSingle(HeadAt + 20), v.ReadSingle(HeadAt + 24), v.ReadSingle(HeadAt + 28), v.ReadSingle(HeadAt + 32), into);
        for (int i = 0; i < 12; i++)
            if (!float.IsFinite(into[i])) return false;
        return true;
    }

    public void Recenter()
    {
        if (_view is not { } v) return;
        v.Write(RecenterAt, v.ReadUInt32(RecenterAt) + 1);
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
