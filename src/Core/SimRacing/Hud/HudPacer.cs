using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace Core.SimRacing.Hud;

internal sealed class HudPacer : IDisposable
{
    private const uint HighResolution = 0x2, TimerAllAccess = 0x1F0003;

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
    }

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action _fire;
    private TimerHandle? _timer;
    private WaitHandle[]? _waits;
    private Thread? _thread;
    private volatile bool _running, _restart, _disposed;
    private double _periodMs = 250;
    private int _pending;

    public HudPacer() => _fire = Fire;

    public event Action? Tick;

    public bool Enabled => _running;

    public double IntervalMs
    {
        get => Volatile.Read(ref _periodMs);
        set => Volatile.Write(ref _periodMs, Math.Max(1, value));
    }

    public static double NextDeadline(double previous, double periodMs, double now)
    {
        double next = previous + periodMs;
        return next < now - periodMs ? now + periodMs : next;
    }

    public void Start()
    {
        if (_running || _disposed) return;
        if (_thread is null)
        {
            IntPtr handle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, HighResolution, TimerAllAccess);
            if (handle == IntPtr.Zero) handle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0, TimerAllAccess);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("The overlay timer couldn't be created.");
            _timer = new TimerHandle(handle);
            _waits = new WaitHandle[] { _timer, _wake };
            _thread = new Thread(Run) { IsBackground = true, Name = "Overlay pacer" };
            _thread.Start();
        }
        _restart = true;
        _running = true;
        _wake.Set();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _wake.Set();
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        double next = 0;
        while (!_disposed)
        {
            if (!_running) { _wake.WaitOne(); continue; }
            double now = clock.Elapsed.TotalMilliseconds;
            if (_restart) { _restart = false; next = now; }
            next = NextDeadline(next, IntervalMs, now);
            double wait = next - now;
            if (wait > 0)
            {
                long due = -(long)(wait * 10_000);
                if (!SetWaitableTimer(_timer!.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) Thread.Sleep((int)Math.Ceiling(wait));
                else if (WaitHandle.WaitAny(_waits!) == 1) continue;
            }
            if (_running && Interlocked.Exchange(ref _pending, 1) == 0) _dispatcher.BeginInvoke(DispatcherPriority.Input, _fire);
        }
    }

    private void Fire()
    {
        Volatile.Write(ref _pending, 0);
        if (_running) Tick?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _wake.Set();
        _thread?.Join(500);
        _timer?.Dispose();
        _wake.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, bool resume);
}
