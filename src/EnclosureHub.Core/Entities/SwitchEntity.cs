namespace EnclosureHub.Core.Entities;

/// <summary>Command for a switch; <c>On</c> is required.</summary>
public sealed record SwitchCommand(bool? On = null);

/// <summary>
/// Controllable on/off entity (camera power, a relay, …). Modules derive from this class,
/// implement <see cref="SetAsync"/> and call <see cref="UpdateState"/> once the new state is in effect.
/// </summary>
public abstract class SwitchEntity : Entity
{
    protected SwitchEntity(string id, string name)
        : base(id, name)
    {
    }

    public override EntityKind Kind => EntityKind.Switch;

    /// <summary>Null = unknown.</summary>
    public bool? IsOn { get; private set; }

    public abstract Task SetAsync(bool on, CancellationToken cancellationToken);

    protected void UpdateState(bool? isOn)
    {
        if (IsOn == isOn)
            return;

        IsOn = isOn;
        NotifyStateChanged();
    }

    public override object GetState() => new { isOn = IsOn };
}
