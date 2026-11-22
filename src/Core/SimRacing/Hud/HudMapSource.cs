using Core.SimRacing.Telemetry;
using Core.SimRacing.TrackMaps;

namespace Core.SimRacing.Hud;

internal sealed class HudMapSource
{
    private Task<TrackMap?>? _loading;
    private (int? Id, string Name, string Display, bool Sample, int Generation) _for;
    private TrackMap? _map;

    public bool Loading => _loading is { IsCompleted: false };

    public TrackMap? Get(HudPainter p, TelemetryFrame f)
    {
        var s = f.Session;
        bool sample = f.Simulated;
        var key = (s.TrackId, s.TrackName, s.TrackDisplayName, sample, TrackMapLibrary.Generation);
        if (_loading is null || key != _for)
        {
            _for = key;
            _map = null;
            _loading = sample ? TrackMapLibrary.ByKey(TrackMapLibrary.SampleKey) : TrackMapLibrary.ForSession(s.TrackId, s.TrackName, s.TrackDisplayName, s.TrackConfig);
        }
        if (!_loading.IsCompleted && p.IsPreview) _loading.Wait(TimeSpan.FromSeconds(3));
        if (_loading.IsCompletedSuccessfully) _map = _loading.Result;
        return _map;
    }
}
