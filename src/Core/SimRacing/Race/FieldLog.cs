using System.Globalization;
using System.IO;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal sealed class FieldLog : IDisposable
{
    public const string Arg = "--field-log";
    private const string Consumer = "Field log";
    private const double Interval = 0.1;
    private const int LinesPerFlush = 400;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly TelemetryService _telemetry;
    private readonly string _folder;
    private readonly EstProfile _profile = new();
    private readonly Action<TelemetryFrame> _observer;
    private readonly object _gate = new();
    private StreamWriter? _file;
    private SessionInfo? _session;
    private double _last = double.MinValue;
    private int _unflushed;
    private bool _closed;

    public FieldLog(TelemetryService telemetry, string folder)
    {
        _telemetry = telemetry;
        _folder = folder;
        _observer = OnFrame;
        telemetry.AddObserver(_observer);
        telemetry.Acquire(Consumer);
    }

    private void OnFrame(TelemetryFrame f)
    {
        if (f.Status != SimStatus.Connected || f.Simulated || f.SessionTime is not { } time) return;
        _profile.Update(f);
        int player = f.PlayerCarIdx;
        if ((uint)player >= TelemetryFrame.MaxCars) return;
        if (time >= _last && time - _last < Interval) return;
        _last = time;
        lock (_gate)
        {
            if (_closed) return;
            var file = _file ??= Open();
            if (!ReferenceEquals(f.Session, _session))
            {
                _session = f.Session;
                file.WriteLine(string.Create(Inv, $"S,{f.Session.TrackId},{f.Session.TrackName},{f.Session.PlayerEstLapTime}"));
                foreach (var d in f.Session.Drivers)
                    file.WriteLine(string.Create(Inv, $"D,{d.CarIdx},{d.CarNumber},{d.CarPath},{d.ClassId},{d.ClassEstLapTime},{(d.IsAi ? 1 : 0)},{(d.IsPaceCar ? 1 : 0)}"));
            }
            long clock = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            file.WriteLine(string.Create(Inv, $"T,{clock},{time:0.000},{f.SessionNum},{player},{f.LapBest},{(f.IsReplayPlaying ? 1 : 0)},{(int)f.Flags}"));
            for (int i = 0; i < TelemetryFrame.MaxCars; i++)
            {
                if (f.CarLapDistPct[i] < 0 || f.CarSurface[i] == TrackSurface.NotInWorld) continue;
                double? shown = i == player ? 0 : FieldOrder.GapSeconds(f, player, i, _profile);
                double? plain = i == player ? 0 : FieldOrder.GapSeconds(f, player, i);
                file.WriteLine(string.Create(Inv,
                    $"C,{i},{f.CarLapDistPct[i]:0.000000},{f.CarEstTime[i]:0.0000},{f.CarF2Time[i]:0.000},{f.CarLap[i]},{(int)f.CarSurface[i]},{(f.CarOnPitRoad[i] ? 1 : 0)},{f.CarPosition[i]},{shown:0.000},{plain:0.000}"));
                _unflushed++;
            }
            if (_unflushed < LinesPerFlush) return;
            _unflushed = 0;
            file.Flush();
        }
    }

    private StreamWriter Open()
    {
        Directory.CreateDirectory(_folder);
        var file = new StreamWriter(Path.Combine(_folder, string.Create(Inv, $"field-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.csv")));
        file.WriteLine("S,trackId,track,playerEstLap");
        file.WriteLine("D,car,number,carPath,classId,estLap,ai,paceCar");
        file.WriteLine("T,unixMs,sessionTime,sessionNum,player,lapBest,replay,flags");
        file.WriteLine("C,car,pct,estTime,f2Time,lap,surface,pitRoad,position,gapShown,gapWithoutProfile");
        return file;
    }

    public void Dispose()
    {
        _telemetry.RemoveObserver(_observer);
        _telemetry.Release(Consumer);
        lock (_gate)
        {
            _closed = true;
            _file?.Dispose();
            _file = null;
        }
    }
}
