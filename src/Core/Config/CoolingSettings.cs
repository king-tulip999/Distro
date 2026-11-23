namespace Core.Config;

public enum CoolingGoal { Quiet, Balanced, Cool }

public sealed class CoolingTestResult
{
    public DateTime Date { get; set; }

    public bool CpuLoaded { get; set; }

    public bool GpuLoaded { get; set; }

    public string? GpuProblem { get; set; }

    public int LimitC { get; set; }

    public List<CoolingSensor> Sensors { get; set; } = new();

    public List<CoolingGroupResult> Groups { get; set; } = new();
}

public sealed class CoolingSensor
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Gpu { get; set; }

    public bool Cpu { get; set; }

    public float? Idle { get; set; }

    public float? IdlePower { get; set; }
}

public sealed class CoolingGroupResult
{
    public string Name { get; set; } = "";

    public string? CurveName { get; set; }

    public List<string> FanIds { get; set; } = new();

    public int Floor { get; set; }

    public List<CoolingStep> Steps { get; set; } = new();
}

public sealed class CoolingStep
{
    public int Percent { get; set; }

    public Dictionary<string, float> Temps { get; set; } = new();

    public float? CpuPower { get; set; }

    public float? GpuPower { get; set; }

    public bool Settled { get; set; }

    public bool Predicted { get; set; }

    public bool OverLimit { get; set; }
}
