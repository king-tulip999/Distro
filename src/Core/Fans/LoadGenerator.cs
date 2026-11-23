using System.Runtime.InteropServices;
using System.Text;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Core.Fans;

internal sealed class CpuLoad : IDisposable
{
    private readonly List<Thread> _threads = new();
    private volatile bool _stop;
    public static double Sink;

    public void Start()
    {
        _stop = false;
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            var thread = new Thread(Spin) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Cooling test CPU load" };
            _threads.Add(thread);
            thread.Start(i);
        }
    }

    private void Spin(object? seed)
    {
        double a = 1.0001 + (int)seed! * 1e-6, b = 0.9999, c = 0.5, d = 1.5;
        while (!_stop)
        {
            for (int i = 0; i < 2_000_000; i++)
            {
                a = Math.FusedMultiplyAdd(a, b, 1e-9);
                b = Math.FusedMultiplyAdd(b, c, 0.5);
                c = Math.FusedMultiplyAdd(c, d, -0.25);
                d = Math.FusedMultiplyAdd(d, a, -d * 0.5);
                if (a > 1e6) a = 1.0001;
                if (Math.Abs(c) > 1e6) c = 0.5;
                if (Math.Abs(d) > 1e6) d = 1.5;
            }
        }
        Sink = a + b + c + d;
    }

    public void Dispose()
    {
        _stop = true;
        foreach (var t in _threads) t.Join(2000);
        _threads.Clear();
    }
}

internal sealed class GpuLoad : IDisposable
{
    private const string Shader = """
        RWStructuredBuffer<float> o : register(u0);
        [numthreads(256, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            float a = id.x * 0.0001f, b = 1.0001f, c = 0.9999f, d = 0.5f;
            [loop] for (int i = 0; i < 2048; i++)
            {
                a = mad(a, b, c); b = mad(b, c, d * 1e-7f); c = mad(c, d, -a * 1e-7f); d = mad(d, a, -b * 1e-7f);
                a = mad(a, c, b); b = mad(b, d, -c * 1e-7f); c = mad(c, a, d * 1e-7f); d = mad(d, b, -a * 1e-7f);
            }
            o[id.x & 65535] = a + b + c + d;
        }
        """;

    private Thread? _thread;
    private volatile bool _stop;

    public string? Problem { get; private set; }

    public string? Adapter { get; private set; }

    public bool Start()
    {
        var ready = new ManualResetEventSlim();
        bool started = false;
        _stop = false;
        _thread = new Thread(() =>
        {
            ID3D11Device? device = null;
            ID3D11DeviceContext? context = null;
            ID3D11ComputeShader? shader = null;
            ID3D11Buffer? buffer = null;
            ID3D11UnorderedAccessView? view = null;
            var queries = new ID3D11Query?[4];
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                IDXGIAdapter1? best = null;
                ulong bestMemory = 0;
                for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
                {
                    var desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) == 0 && (ulong)desc.DedicatedVideoMemory >= bestMemory)
                    {
                        best?.Dispose();
                        best = adapter;
                        bestMemory = (ulong)desc.DedicatedVideoMemory;
                        Adapter = desc.Description;
                    }
                    else adapter.Dispose();
                }
                if (best is null) { Problem = "No graphics card was found."; return; }
                var result = D3D11.D3D11CreateDevice(best, DriverType.Unknown, DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_0 }, out device, out context);
                best.Dispose();
                if (result.Failure || device is null || context is null) { Problem = "Direct3D 11 isn't available on the graphics card."; return; }
                var bytecode = Compile(Shader);
                if (bytecode is null) { Problem = "The graphics load couldn't be prepared."; return; }
                shader = device.CreateComputeShader(bytecode);
                buffer = device.CreateBuffer(new BufferDescription(65536 * 4, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
                view = device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription(buffer, Format.Unknown, 0, 65536, BufferUnorderedAccessViewFlags.None));
                for (int i = 0; i < queries.Length; i++) queries[i] = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
                context.CSSetShader(shader);
                context.CSSetUnorderedAccessView(0, view);
                started = true;
                ready.Set();
                for (long batch = 0; !_stop; batch++)
                {
                    for (int i = 0; i < 16; i++) context.Dispatch(2048, 1, 1);
                    context.End(queries[batch % queries.Length]);
                    context.Flush();
                    if (batch < queries.Length - 1) continue;
                    var oldest = queries[(batch + 1) % queries.Length]!;
                    var spin = new SpinWait();
                    while (!_stop && context.GetData(oldest, IntPtr.Zero, 0, AsyncGetDataFlags.None).Code != 0) spin.SpinOnce(-1);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException or SharpGen.Runtime.SharpGenException or InvalidOperationException or COMException)
            {
                Problem = "The graphics load couldn't start: " + ex.Message;
            }
            finally
            {
                ready.Set();
                context?.ClearState();
                foreach (var q in queries) q?.Dispose();
                view?.Dispose();
                buffer?.Dispose();
                shader?.Dispose();
                context?.Dispose();
                device?.Dispose();
            }
        }) { IsBackground = true, Name = "Cooling test GPU load" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(15));
        return started;
    }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(byte[] source, nuint size, string? name, IntPtr defines, IntPtr include, string entry, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate nuint BlobSize(IntPtr self);

    private static byte[]? Compile(string source)
    {
        var bytes = Encoding.ASCII.GetBytes(source);
        int hr = D3DCompile(bytes, (nuint)bytes.Length, null, IntPtr.Zero, IntPtr.Zero, "main", "cs_5_0", 1u << 14, 0, out var code, out var errors);
        if (errors != IntPtr.Zero) Marshal.Release(errors);
        if (hr < 0 || code == IntPtr.Zero) return null;
        try
        {
            var table = Marshal.ReadIntPtr(code);
            var pointer = Marshal.GetDelegateForFunctionPointer<BlobPointer>(Marshal.ReadIntPtr(table, 3 * IntPtr.Size))(code);
            var size = Marshal.GetDelegateForFunctionPointer<BlobSize>(Marshal.ReadIntPtr(table, 4 * IntPtr.Size))(code);
            var result = new byte[(int)size];
            Marshal.Copy(pointer, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Release(code);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(5000);
        _thread = null;
    }
}
