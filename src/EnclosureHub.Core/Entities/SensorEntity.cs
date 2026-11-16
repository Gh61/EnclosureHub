namespace EnclosureHub.Core.Entities;

/// <summary>Numeric read-only value (temperature, humidity, …).</summary>
public sealed class SensorEntity : Entity
{
    public SensorEntity(string id, string name)
        : base(id, name)
    {
    }

    public override EntityKind Kind => EntityKind.Sensor;

    /// <summary>Unit of measurement, e.g. "°C".</summary>
    public string? Unit { get; init; }

    /// <summary>Home Assistant device class, e.g. "temperature".</summary>
    public string? DeviceClass { get; init; }

    /// <summary>Home Assistant state class, e.g. "measurement".</summary>
    public string? StateClass { get; init; }

    public double? Value { get; private set; }

    public void SetValue(double? value)
    {
        if (Nullable.Equals(Value, value))
            return;

        Value = value;
        NotifyStateChanged();
    }

    public override object GetState() => new { value = Value, unit = Unit };
}
