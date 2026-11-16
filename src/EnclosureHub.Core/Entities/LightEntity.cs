namespace EnclosureHub.Core.Entities;

[Flags]
public enum LightFeatures
{
    None = 0,
    Brightness = 1,
    ColorTemperature = 2,
    Rgb = 4,
}

public readonly record struct RgbColor(byte R, byte G, byte B);

public sealed record LightState(
    bool IsOn,
    byte Brightness = 255,
    int? ColorTemperatureKelvin = null,
    RgbColor? Color = null);

/// <summary>Partial update – null properties are left unchanged.</summary>
public sealed record LightCommand(
    bool? On = null,
    byte? Brightness = null,
    int? ColorTemperatureKelvin = null,
    RgbColor? Color = null);

/// <summary>
/// Controllable light. Hardware modules (PWM, PCA9685, WLED, …) derive from this class,
/// implement <see cref="SetAsync"/> and call <see cref="UpdateState"/> once the hardware
/// has been updated.
/// </summary>
public abstract class LightEntity : Entity
{
    protected LightEntity(string id, string name, LightFeatures features)
        : base(id, name)
    {
        Features = features;
    }

    public override EntityKind Kind => EntityKind.Light;

    public LightFeatures Features { get; }

    public int MinColorTemperatureKelvin { get; init; } = 2700;

    public int MaxColorTemperatureKelvin { get; init; } = 6500;

    public LightState State { get; private set; } = new(IsOn: false);

    public abstract Task SetAsync(LightCommand command, CancellationToken cancellationToken);

    protected void UpdateState(LightState state)
    {
        if (state == State)
            return;

        State = state;
        NotifyStateChanged();
    }

    public override object GetState() => State;
}
