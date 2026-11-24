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

    /// <summary>
    /// Optional instantaneous reading behind <see cref="Value"/> (e.g. a 1 s sample while the value is a 30 s average
    /// published to Home Assistant). Read on demand by displays that poll; never raises <see cref="Entity.StateChanged"/>.
    /// </summary>
    public Func<double?>? Live { get; init; }

    public double? Value { get; private set; }

    public void SetValue(double? value)
    {
        if (Nullable.Equals(Value, value))
            return;

        Value = value;
        NotifyStateChanged();
    }

    public override object GetState() => Live == null
        ? new { value = Value, unit = Unit }
        : new { value = Value, unit = Unit, live = Live() };
}
