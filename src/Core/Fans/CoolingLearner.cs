using System.IO;
using System.Text.Json;
using Core.Config;
using Core.Services;

namespace Core.Fans;

internal sealed class LearnedSample
{
    public DateTime When { get; set; }
    public float Power { get; set; }
    public float Percent { get; set; }
    public float Temp { get; set; }
    public float Predicted { get; set; }
}

internal sealed record LearnedInsight(string CurveName, string SensorName, int Count, double Error);

internal sealed class CoolingLearner : IDisposable
{
    public const int WindowTicks = 120, SampleEveryTicks = 60, MaxSamples = 300, MinSamples = 12, KeepDays = 30;
    public const double MeaningfulErrorC = 3;
    private const long SaveEveryMs = 10 * 60 * 1000;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly AppServices _app;
    private readonly string? _path;
    private readonly Dictionary<string, List<LearnedSample>> _samples;
    private readonly Dictionary<string, Window> _windows = new(StringComparer.OrdinalIgnoreCase);
    private bool _unsaved;
    private long _savedAt = Environment.TickCount64;
    private bool _started;

    private sealed class Window
    {
        public readonly float[] Power = new float[WindowTicks], Percent = new float[WindowTicks], Temp = new float[WindowTicks];
        public int Count, Next, SinceSample;

        public void Add(float power, float percent, float temp)
        {
            Power[Next] = power;
            Percent[Next] = percent;
            Temp[Next] = temp;
            Next = (Next + 1) % WindowTicks;
            Count = Math.Min(WindowTicks, Count + 1);
            SinceSample++;
        }
    }

    public CoolingLearner(AppServices app)
    {
        _app = app;
        _path = app.Demo ? null : Path.Combine(ConfigStore.Folder, "fanlearn.json");
        _samples = Load(_path);
        if (app.Demo) Services.DemoData.SeedFanLearning(_samples);
    }

    private static Dictionary<string, List<LearnedSample>> Load(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
                return new Dictionary<string, List<LearnedSample>>(JsonSerializer.Deserialize<Dictionary<string, List<LearnedSample>>>(File.ReadAllText(path)) ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _app.Tick += OnTick;
    }

    public void Dispose()
    {
        if (_started) _app.Tick -= OnTick;
        _started = false;
        Flush();
    }

    private void OnTick()
    {
        var fans = _app.Config.Fans;
        var test = fans.CoolingTest;
        var catalog = _app.Hardware.Catalog;
        var snapshot = _app.Hardware.Latest;
        if (!fans.Enabled || CoolingTest.Running || test is null || catalog is null || snapshot is null)
        {
            _windows.Clear();
            return;
        }
        foreach (var group in test.Groups)
        {
            if (group.CurveName is not { } name || fans.Curves.FirstOrDefault(c => c.Name == name) is not { Type: FanCurveType.Graph, SensorId: { } sensorId } ) continue;
            var sensor = test.Sensors.FirstOrDefault(s => s.Id == sensorId);
            if (sensor?.IdlePower is not { } idlePower || !catalog.Curves.TryGetValue(name, out var live) || live is not { Temp: { } temp, Output: { } output }) continue;
            if ((sensor.Gpu ? snapshot.GpuPower : snapshot.CpuPower) is not { } power) continue;
            if (!_windows.TryGetValue(name, out var window)) _windows[name] = window = new Window();
            window.Add(power, output, temp);
            if (window.Count < WindowTicks || window.SinceSample < SampleEveryTicks) continue;
            if (Steady(window, idlePower) is not { } steady) continue;
            window.SinceSample = 0;
            if (CoolingAnalysis.Predict(test, group, sensorId, steady.Percent, steady.Power) is not { } predicted) continue;
            Add(name, new LearnedSample { When = DateTime.Now, Power = steady.Power, Percent = steady.Percent, Temp = steady.Temp, Predicted = (float)Math.Round(predicted, 1) });
        }
        if (_unsaved && Environment.TickCount64 - _savedAt >= SaveEveryMs) Flush();
    }

    internal static (float Power, float Percent, float Temp)? Steady(float[] power, float[] percent, float[] temp, float idlePower)
    {
        float pMin = power.Min(), pMax = power.Max(), pMean = power.Average();
        if (pMean < idlePower + 15) return null;
        if (pMax - pMin > Math.Max(10, pMean * 0.1f)) return null;
        if (percent.Max() - percent.Min() > 6) return null;
        if (temp.Max() - temp.Min() > 2.5f) return null;
        return (pMean, percent.Average(), temp.Average());
    }

    private static (float Power, float Percent, float Temp)? Steady(Window w, float idlePower) => Steady(w.Power, w.Percent, w.Temp, idlePower);

    private void Add(string curve, LearnedSample sample)
    {
        if (!_samples.TryGetValue(curve, out var list)) _samples[curve] = list = new List<LearnedSample>();
        list.Add(sample);
        var cutoff = DateTime.Now.AddDays(-KeepDays);
        list.RemoveAll(s => s.When < cutoff);
        if (list.Count > MaxSamples) list.RemoveRange(0, list.Count - MaxSamples);
        _unsaved = true;
    }

    public void Forget(string curve)
    {
        if (_samples.Remove(curve)) _unsaved = true;
        _windows.Remove(curve);
    }

    public int Count(string curve) => _samples.TryGetValue(curve, out var list) ? list.Count : 0;

    public LearnedInsight? Insight(string curve)
    {
        var test = _app.Config.Fans.CoolingTest;
        if (test is null || !_samples.TryGetValue(curve, out var list) || list.Count < MinSamples) return null;
        var c = _app.Config.Fans.Curves.FirstOrDefault(x => x.Name == curve);
        var sensor = test.Sensors.FirstOrDefault(s => s.Id == c?.SensorId);
        double error = Error(list);
        return new LearnedInsight(curve, sensor?.Name ?? c?.SensorName ?? "the sensor", list.Count, error);
    }

    internal static double Error(IReadOnlyList<LearnedSample> samples)
    {
        var errors = samples.Select(s => (double)(s.Temp - s.Predicted)).OrderBy(e => e).ToList();
        int cut = errors.Count / 10;
        var kept = errors.Skip(cut).Take(errors.Count - 2 * cut).ToList();
        return Math.Round(kept.Average(), 1);
    }

    public void Flush()
    {
        if (!_unsaved) return;
        _unsaved = false;
        _savedAt = Environment.TickCount64;
        if (_path is null) return;
        string json = JsonSerializer.Serialize(_samples, Options);
        string path = _path;
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }
}
