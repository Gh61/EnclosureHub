namespace EnclosureHub.Core.Entities;

public sealed record FanState(bool IsOn, int Percentage);

/// <summary>Partial update – null properties are left unchanged. Percentage is 0–100.</summary>
public sealed record FanCommand(bool? On = null, int? Percentage = null);

/// <summary>
/// Controllable fan. Hardware modules derive from this class, implement
/// <see cref="SetAsync"/> and call <see cref="UpdateState"/> once the hardware
/// has been updated.
/// </summary>
public abstract class FanEntity : Entity
{
    protected FanEntity(string id, string name)
        : base(id, name)
    {
    }

    public override EntityKind Kind => EntityKind.Fan;

    public FanState State { get; private set; } = new(IsOn: false, Percentage: 0);

    public abstract Task SetAsync(FanCommand command, CancellationToken cancellationToken);

    protected void UpdateState(FanState state)
    {
        if (state == State)
            return;

        State = state;
        NotifyStateChanged();
    }

    public override object GetState() => State;
}
