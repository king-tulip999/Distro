using Core.Config;
using Core.Hardware;

namespace Core.Fans;

internal sealed record CoolingGroupPlan(string Name, string? CurveName, IReadOnlyList<string> FanIds, int Floor, IReadOnlyList<int> Steps);

internal sealed record CoolingProgress(string Status, double Progress, string? Group, int? Percent, IReadOnlyDictionary<string, float> Temps, CoolingStep? Finished, TimeSpan Left);

internal sealed class CoolingTrace
{
    public string Label { get; set; } = "";
    public List<double> Seconds { get; set; } = new();
    public Dictionary<string, List<double>> Temps { get; set; } = new();
    public Dictionary<string, double> Predicted { get; set; } = new();
}

internal sealed class CoolingTest
{
    public const int HoldPercent = 60;
    private double HotSeconds => _demo ? 1 : 10;
    public const string NoCurveGroup = "Fans without a curve";
    private static readonly int[] StepPercents = { 100, 70, 50, 30 };
    private static readonly string[] CpuSensorNames = { "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core (Tdie)", "Core Max", "Core Average" };

    private readonly HardwareService _hardware;
    private readonly bool _demo;
    private readonly CoolingTiming _timing;
    private readonly int _limit;

    public static volatile bool Running;

    public CoolingTest(HardwareService hardware, bool demo, int limitC)
    {
        _hardware = hardware;
        _demo = demo;
        _timing = demo ? CoolingTiming.Demo : CoolingTiming.Real;
        _limit = limitC;
    }

    public static int LimitFor(FanSettings fans) => Math.Max(60, fans.EmergencyTempC - 5);

    public static List<int> StepsFor(int floor)
    {
        int lowest = Math.Clamp(Math.Max(floor, StepPercents[^1]), 0, 100);
        var steps = StepPercents.Where(s => s > lowest).ToList();
        steps.Add(lowest);
        return steps.Distinct().OrderByDescending(s => s).ToList();
    }

    public static List<CoolingGroupPlan> Plan(FanSettings fans, HardwareCatalog catalog)
    {
        var preset = fans.Active;
        var groups = new Dictionary<string, (string? Curve, List<string> Ids, int Floor)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var fan in catalog.Fans.Where(f => f.Controllable))
        {
            var hw = fans.Hardware.FirstOrDefault(h => h.ControlId == fan.ControlId);
            if (hw is { Hidden: true } or { IsPump: true }) continue;
            var entry = preset?.Fans.FirstOrDefault(e => e.ControlId == fan.ControlId);
            string? curve = entry is { Mode: FanMode.Curve, CurveName: { } name } && fans.Curves.Any(c => c.Name == name) ? name : null;
            if (curve is not null && fans.Curves.First(c => c.Name == curve) is { Type: FanCurveType.Flat }) curve = null;
            string key = curve ?? NoCurveGroup;
            if (!groups.TryGetValue(key, out var g))
            {
                g = (curve, new List<string>(), 0);
                order.Add(key);
            }
            int floor = hw is null ? FanController.UncalibratedFloor : Math.Max(hw.MinPercent, FanController.Lowest(hw));
            groups[key] = (g.Curve, g.Ids.Append(fan.ControlId).ToList(), Math.Max(g.Floor, floor));
        }
        return order.Select(k => new CoolingGroupPlan(k, groups[k].Curve, groups[k].Ids, groups[k].Floor, StepsFor(groups[k].Floor))).ToList();
    }

    public static List<CoolingSensor> Sensors(FanSettings fans, HardwareCatalog catalog, HardwareSnapshot? snapshot, IEnumerable<CoolingGroupPlan> plan)
    {
        var list = new List<CoolingSensor>();
        var cpu = CpuSensorNames.Select(n => catalog.Temperatures.FirstOrDefault(t => t.Name == n && !IsGpu(t.Id))).FirstOrDefault(t => t is not null);
        if (cpu is not null) list.Add(new CoolingSensor { Id = cpu.Id, Name = "CPU", Cpu = true });
        else if (snapshot?.CpuTemp is not null) list.Add(new CoolingSensor { Id = "cpu", Name = "CPU", Cpu = true });
        var gpu = catalog.Temperatures.FirstOrDefault(t => t.Name == "GPU Core" && IsGpu(t.Id));
        if (gpu is not null) list.Add(new CoolingSensor { Id = gpu.Id, Name = "GPU", Gpu = true });
        else if (snapshot?.GpuTemp is not null) list.Add(new CoolingSensor { Id = "gpu", Name = "GPU", Gpu = true });
        foreach (var group in plan)
        {
            if (fans.Curves.FirstOrDefault(c => c.Name == group.CurveName) is not { Type: FanCurveType.Graph, SensorId: { } id }) continue;
            if (list.Any(s => s.Id == id)) continue;
            var t = catalog.Temperatures.FirstOrDefault(x => x.Id == id);
            if (t is null) continue;
            list.Add(new CoolingSensor { Id = id, Name = t.Name, Gpu = IsGpu(id) });
        }
        return list;
    }

    public static bool IsGpu(string sensorId) => sensorId == "gpu" || sensorId.StartsWith("/gpu", StringComparison.OrdinalIgnoreCase);

    public static TimeSpan Estimate(IReadOnlyList<CoolingGroupPlan> plan, bool demo)
    {
        var t = demo ? CoolingTiming.Demo : CoolingTiming.Real;
        return TimeSpan.FromSeconds(t.IdleSeconds + t.TypicalStep * 1.5 + plan.Sum(g => g.Steps.Count) * t.TypicalStep);
    }

    public async Task<CoolingTestResult> RunAsync(IReadOnlyList<CoolingGroupPlan> plan, List<CoolingSensor> sensors, IProgress<CoolingProgress> progress, CancellationToken cancel)
    {
        var result = new CoolingTestResult { Date = DateTime.Now, LimitC = _limit, Sensors = sensors };
        var allFans = plan.SelectMany(g => g.FanIds).Distinct().ToList();
        int totalUnits = 2 + plan.Sum(g => g.Steps.Count);
        int done = 0;
        var started = DateTime.UtcNow;
        CpuLoad? cpuLoad = null;
        GpuLoad? gpuLoad = null;
        Running = true;

        TimeSpan Left()
        {
            if (done == 0) return Estimate(plan, _demo);
            var per = (DateTime.UtcNow - started) / done;
            return per * Math.Max(0, totalUnits - done);
        }
        void Report(string status, string? group, int? percent, IReadOnlyDictionary<string, float> temps, CoolingStep? finished = null) =>
            progress.Report(new CoolingProgress(status, Math.Clamp(done / (double)totalUnits, 0, 1), group, percent, temps, finished, Left()));

        try
        {
            if (sensors.Count == 0)
                throw new CalibrationAbortedException("No CPU or GPU temperature can be read, so the test can't watch what the fans do.", safety: true);

            _traceLabel = "idle";
            var idle = await MeasureAsync(sensors, _timing.IdleSeconds, _timing.IdleSeconds, double.PositiveInfinity, t => Report("Measuring idle temperatures…", null, null, t), cancel, guardOnly: true);
            foreach (var s in sensors)
            {
                if (idle.Temps.TryGetValue(s.Id, out var v)) s.Idle = (float)Math.Round(v, 1);
                s.IdlePower = s.Gpu ? idle.GpuPower : idle.CpuPower;
            }
            done++;

            if (_demo)
            {
                DemoHardware.Loaded = true;
                result.CpuLoaded = result.GpuLoaded = true;
            }
            else
            {
                cpuLoad = new CpuLoad();
                cpuLoad.Start();
                result.CpuLoaded = true;
                gpuLoad = new GpuLoad();
                result.GpuLoaded = await Task.Run(gpuLoad.Start, cancel);
                result.GpuProblem = gpuLoad.Problem;
            }

            foreach (var id in allFans) _hardware.SetOverride(id, 100);
            _traceLabel = "warm-up";
            await MeasureAsync(sensors, _timing.WarmMin, _timing.WarmMax, _timing.MaxSlopePerMinute, t => Report("Warming up with every fan at 100%…", null, 100, t), cancel);
            done++;

            foreach (var group in plan)
            {
                var others = allFans.Except(group.FanIds).ToList();
                foreach (var id in others) _hardware.SetOverride(id, HoldPercent);
                var record = new CoolingGroupResult { Name = group.Name, CurveName = group.CurveName, FanIds = group.FanIds.ToList(), Floor = group.Floor };
                result.Groups.Add(record);
                int index = 0;
                foreach (int percent in group.Steps)
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach (var id in group.FanIds) _hardware.SetOverride(id, percent);
                    _traceLabel = $"{group.Name} {percent}%";
                    var m = await MeasureAsync(sensors, _timing.MinStep, _timing.MaxStep, _timing.MaxSlopePerMinute,
                        t => Report($"{group.Name}: testing {percent}%…", group.Name, percent, t), cancel);
                    var step = new CoolingStep
                    {
                        Percent = percent,
                        Temps = m.Temps.ToDictionary(kv => kv.Key, kv => (float)Math.Round(kv.Value, 1)),
                        CpuPower = m.CpuPower,
                        GpuPower = m.GpuPower,
                        Settled = m.Settled,
                        Predicted = m.Predicted,
                        OverLimit = m.OverLimit,
                    };
                    record.Steps.Add(step);
                    done++;
                    index++;
                    Report(m.OverLimit ? $"{group.Name}: too hot at {percent}%" : $"{group.Name}: {percent}% measured", group.Name, percent, step.Temps, step);
                    if (m.OverLimit)
                    {
                        foreach (var id in group.FanIds) _hardware.SetOverride(id, 100);
                        done += group.Steps.Count - index;
                        break;
                    }
                }
                foreach (var id in group.FanIds) _hardware.SetOverride(id, HoldPercent);
            }
            Report("Done", null, null, new Dictionary<string, float>());
            return result;
        }
        finally
        {
            DemoHardware.Loaded = false;
            cpuLoad?.Dispose();
            gpuLoad?.Dispose();
            foreach (var id in allFans) _hardware.ClearOverride(id);
            Running = false;
        }
    }

    private sealed record Measurement(Dictionary<string, double> Temps, float? CpuPower, float? GpuPower, bool Settled, bool Predicted, bool OverLimit);

    public List<CoolingTrace> Traces { get; } = new();

    private string _traceLabel = "";

    private async Task<Measurement> MeasureAsync(List<CoolingSensor> sensors, double minSeconds, double maxSeconds, double maxSlope,
        Action<IReadOnlyDictionary<string, float>> report, CancellationToken cancel, bool guardOnly = false)
    {
        var samples = sensors.ToDictionary(s => s.Id, _ => new List<(double Seconds, double Temp)>());
        var cpuPower = new List<(double, double)>();
        var gpuPower = new List<(double, double)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool settled = false, overLimit = false;
        var predicted = new Dictionary<string, double>();
        var live = new Dictionary<string, float>();
        while (true)
        {
            await Task.Delay(_timing.SampleMs, cancel);
            double now = clock.Elapsed.TotalSeconds;
            if (_hardware.Catalog?.Emergency == true)
                throw new CalibrationAbortedException("The emergency temperature failsafe tripped, so every fan went to full speed.", safety: true);
            var snapshot = _hardware.Latest;
            live.Clear();
            foreach (var s in sensors)
            {
                if (Read(s.Id, snapshot) is not { } t) continue;
                samples[s.Id].Add((now, t));
                live[s.Id] = t;
                if (!guardOnly && !CoolingAnalysis.IsCpu(s) && now >= HotSeconds && CoolingAnalysis.Mean(samples[s.Id], HotSeconds) >= _limit) overLimit = true;
                else if (guardOnly && !CoolingAnalysis.IsCpu(s) && t >= _limit + 3)
                    throw new CalibrationAbortedException($"The {s.Name} is already at {t:0} °C before any load. Run the test with the PC idle.", safety: true);
            }
            if (snapshot?.CpuPower is { } cp) cpuPower.Add((now, cp));
            if (snapshot?.GpuPower is { } gp) gpuPower.Add((now, gp));
            report(live);
            if (overLimit) break;
            if (now >= maxSeconds) break;
            if (now >= minSeconds && !guardOnly)
            {
                predicted.Clear();
                bool all = true;
                foreach (var (id, v) in samples)
                {
                    if (v.Count == 0) continue;
                    if (CoolingAnalysis.Settled(v, minSeconds, _timing.Window, maxSlope)) continue;
                    if (CoolingAnalysis.PredictFinal(v, minSeconds) is { } final) predicted[id] = final;
                    else { all = false; break; }
                }
                if (all)
                {
                    settled = true;
                    break;
                }
            }
        }
        Traces.Add(new CoolingTrace { Label = _traceLabel, Seconds = samples.Values.FirstOrDefault(v => v.Count > 0)?.Select(x => Math.Round(x.Seconds, 1)).ToList() ?? new(),
            Temps = samples.ToDictionary(kv => kv.Key, kv => kv.Value.Select(x => Math.Round(x.Temp, 2)).ToList()), Predicted = predicted.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)) });
        double tail = Math.Min(30, Math.Max(_timing.Window / 2, _timing.SampleMs / 1000.0 * 3));
        var temps = samples.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => predicted.TryGetValue(kv.Key, out var p) ? p : CoolingAnalysis.Mean(kv.Value, tail));
        float? Power(List<(double, double)> p) => p.Count == 0 ? null : (float)Math.Round(CoolingAnalysis.Mean(p, tail), 1);
        return new Measurement(temps, Power(cpuPower), Power(gpuPower), settled, predicted.Count > 0, overLimit);
    }

    private float? Read(string id, HardwareSnapshot? snapshot) => id switch
    {
        "cpu" => snapshot?.CpuTemp,
        "gpu" => snapshot?.GpuTemp,
        _ => _hardware.Catalog?.Temperatures.FirstOrDefault(t => t.Id == id)?.Value,
    };
}
