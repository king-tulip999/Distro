namespace Core.Hardware;

internal enum SensorKind
{
    Temperature, Load, Clock, Power, Voltage, Current, Fan, Control, Data, SmallData, Throughput,
    Frequency, Level, Factor, Energy, TimeSpan, Flow, Noise, Other,
}

internal enum DeviceKind { Cpu, Gpu, Memory, Motherboard, SuperIO, Storage, Network, Controller, Battery, Psu, Other }

internal sealed record SensorReading(string Id, string Name, SensorKind Kind, float? Value, float? Min = null, float? Max = null, string? Unit = null)
{
}

internal sealed record DeviceNode(string Id, string Name, DeviceKind Kind, IReadOnlyList<SensorReading> Sensors, IReadOnlyList<DeviceNode> Children, string Source)
{
    public IEnumerable<SensorReading> AllSensors => Sensors.Concat(Children.SelectMany(c => c.AllSensors));
}

internal sealed record SensorTree(IReadOnlyList<DeviceNode> Devices);

internal interface ISensorSource
{
    string Name { get; }

    TimeSpan Interval { get; }

    IReadOnlyList<DeviceNode> Read();
}
