using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Core.Config;
using Core.Overlay;
using Core.Processes;
using Core.Services;

namespace Core.SimRacing.Hud.Vr;

internal sealed class HudVr : IDisposable
{
    private const string Consumer = "iRacing Overlay in VR", SteamVrServer = "vrserver";
    private const int RetrySeconds = 5;

    internal sealed class Target : IDisposable
    {
        public readonly DibSurface Surface = new();
        public readonly HudPainter Painter = new();
        public int Slot = -1;
        public uint Id;
        public bool Shown;
        public Size Size;

        public void Dispose()
        {
            Painter.Dispose();
            Surface.Dispose();
        }
    }

    private readonly AppServices _app;
    private readonly SteamVrOverlays _steam = new();
    private readonly VrShare _share = new();
    private readonly Dictionary<string, Target> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool[] _used = new bool[VrShare.MaxSlots];
    private bool? _installed;
    private bool _on, _steamSeen, _readerSeen;
    private uint _nextId;
    private int _retry;
    // Its own one-second beat: Velocita for SimHub never starts the app-wide tick.
    private readonly System.Windows.Threading.DispatcherTimer _beat = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };

    public HudVr(AppServices app)
    {
        _app = app;
        _beat.Tick += (_, _) => OnTick();
    }

    public bool On => _on;

    public string? Problem { get; private set; }

    public bool SteamVrConnected => _steam.Connected;

    public string? SteamVrProblem => _steam.Problem;

    public bool SteamVrRunning => _on && _app.Watcher.IsRunning(SteamVrServer);

    public bool OpenXrSeen => _share.ReaderAlive;

    public VrShare.LayerReport? LayerReport => _share.ReadReport();

    public event Action? Changed;

    public void Sync(bool setting, bool active)
    {
        if (_app.Demo) setting = false;
        if (_installed != setting)
        {
            OpenXrLayer.Claim(setting);
            if (setting) Problem = OpenXrLayer.Install();
            else
            {
                if ((_installed == true || OpenXrLayer.Installed) && !OpenXrLayer.ClaimedElsewhere()) OpenXrLayer.Remove();
                Problem = null;
            }
            _installed = setting;
        }
        bool want = setting && active;
        if (want == _on) return;
        _on = want;
        if (want)
        {
            try { _share.Start(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Problem ??= "VR widgets couldn't be shared with OpenXR games: " + ex.Message; }
            _app.Watcher.Started += OnStarted;
            _app.Watcher.Stopped += OnStopped;
            _app.Watcher.Acquire(Consumer, new[] { SteamVrServer, IracingFiles.IracingPaths.SimProcess });
            CheckIracingAdmin();
            _beat.Start();
            if (_app.Watcher.IsRunning(SteamVrServer)) _steam.Connect();
        }
        else
        {
            _beat.Stop();
            _app.Watcher.Started -= OnStarted;
            _app.Watcher.Stopped -= OnStopped;
            _app.Watcher.Release(Consumer);
            Clear();
            _steam.Disconnect();
            _share.Dispose();
            IracingAsAdmin = false;
        }
        Changed?.Invoke();
    }

    public bool IracingAsAdmin { get; private set; }

    private void CheckIracingAdmin()
    {
        bool admin = ProcessElevation.AnyElevated(IracingFiles.IracingPaths.SimProcess);
        if (admin == IracingAsAdmin) return;
        IracingAsAdmin = admin;
        Changed?.Invoke();
    }

    private void OnStarted(string name)
    {
        if (_on && string.Equals(name, IracingFiles.IracingPaths.SimProcess, StringComparison.OrdinalIgnoreCase)) CheckIracingAdmin();
        if (!_on || !string.Equals(name, SteamVrServer, StringComparison.OrdinalIgnoreCase)) return;
        _retry = 0;
        _steam.Connect();
        Changed?.Invoke();
    }

    private void OnStopped(string name)
    {
        if (string.Equals(name, IracingFiles.IracingPaths.SimProcess, StringComparison.OrdinalIgnoreCase) && IracingAsAdmin)
        {
            IracingAsAdmin = false;
            Changed?.Invoke();
        }
        if (!string.Equals(name, SteamVrServer, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var t in _targets.Values) t.Shown = false;
        _steam.Disconnect();
        Changed?.Invoke();
    }

    private void OnTick()
    {
        try { Beat(); }
        catch (Exception ex)
        {
            LogOnce("tick", ex);
            _steam.Disconnect();
        }
    }

    private void Beat()
    {
        if (!_on) return;
        _steam.Pump();
        if (!_steam.Connected && ++_retry >= RetrySeconds && _app.Watcher.IsRunning(SteamVrServer))
        {
            _retry = 0;
            _steam.Connect();
        }
        _share.Beat(SlotCount());
        bool reader = _share.ReaderAlive;
        if (reader != _readerSeen || _steam.Connected != _steamSeen)
        {
            _readerSeen = reader;
            _steamSeen = _steam.Connected;
            Changed?.Invoke();
        }
    }

    public bool Wants(HudWidgetConfig c) => _on && c.Vr != HudVrPlacement.Off && (_steam.Connected || _share.ReaderAlive);

    public bool Showing => _on && (_steam.Connected || _share.ReaderAlive);

    public Target Begin(string id, Size size, bool top = false)
    {
        if (!_targets.TryGetValue(id, out var t)) _targets[id] = t = new Target();
        if (t.Slot < 0)
        {
            int free = top ? Array.LastIndexOf(_used, false) : Array.IndexOf(_used, false);
            if (free >= 0)
            {
                _used[free] = true;
                t.Slot = free;
                t.Id = ++_nextId;
            }
        }
        t.Surface.Ensure(size);
        t.Size = size;
        return t;
    }

    public void Publish(Target t, string id, string name, HudWidgetConfig c, VrPose pose, bool pointer = false)
    {
        GdiFlush();
        if (_steam.Connected)
        {
            try { _steam.Show(id, name, c.Vr, pose, c.Opacity, t.Size.Width, t.Size.Height, t.Surface.Bits, pointer); }
            catch (Exception ex)
            {
                LogOnce("steamvr", ex);
                foreach (var other in _targets.Values) other.Shown = false;
                _steam.Disconnect();
                Changed?.Invoke();
            }
        }
        if (t.Slot >= 0 && _share.ReaderAlive)
        {
            _share.Publish(t.Slot, t.Id, c.Vr, pose, c.Opacity, t.Size.Width, t.Size.Height, t.Surface.Bits);
            _share.Beat(SlotCount());
        }
        t.Shown = true;
    }

    public bool Recenter()
    {
        if (!_on) return false;
        bool done = false;
        try
        {
            if (_steam.HeadPose() is { } head && VrAnchor.FromHead(head) is { } anchor)
            {
                _steam.Anchor = anchor;
                done = true;
            }
        }
        catch (Exception ex) when (ex is SEHException or InvalidOperationException) { LogOnce("recenter", ex); }
        if (_share.ReaderAlive)
        {
            _share.Recenter();
            done = true;
        }
        return done;
    }

    private readonly float[] _head = new float[12];

    public float[]? Head()
    {
        if (!_on) return null;
        try
        {
            if (_steam.Connected && _steam.HeadPose(_head))
            {
                _steam.Anchor.Unapply(_head);
                return _head;
            }
        }
        catch (Exception ex) when (ex is SEHException or InvalidOperationException) { LogOnce("head", ex); }
        return _share.ReaderAlive && _share.ReadHead(_head) ? _head : null;
    }

    public void PollPointer(string id, Action<int, float, float> pointed)
    {
        try { _steam.PollPointer(id, pointed); }
        catch (Exception ex) when (ex is SEHException or InvalidOperationException) { LogOnce("pointer", ex); }
    }

    public void Hide(string id)
    {
        if (!_targets.TryGetValue(id, out var t) || !t.Shown) return;
        t.Shown = false;
        _steam.Hide(id);
        if (t.Slot >= 0) _share.Hide(t.Slot);
    }

    public void HideAll()
    {
        foreach (var id in _targets.Keys) Hide(id);
    }

    public void Remove(string id)
    {
        if (!_targets.Remove(id, out var t)) return;
        _steam.Remove(id);
        if (t.Slot >= 0)
        {
            _share.Hide(t.Slot);
            _used[t.Slot] = false;
        }
        t.Dispose();
    }

    private void Clear()
    {
        foreach (var id in _targets.Keys.ToList()) Remove(id);
    }

    private int SlotCount()
    {
        for (int i = _used.Length - 1; i >= 0; i--)
            if (_used[i]) return i + 1;
        return 0;
    }

    public void Dispose()
    {
        if (_on)
        {
            _beat.Stop();
            _app.Watcher.Started -= OnStarted;
            _app.Watcher.Stopped -= OnStopped;
            _app.Watcher.Release(Consumer);
        }
        _on = false;
        Clear();
        _steam.Dispose();
        _share.Dispose();
    }

    private readonly HashSet<string> _logged = new();

    private void LogOnce(string kind, Exception ex)
    {
        if (_logged.Add(kind)) App.LogError(ex);
    }

    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
