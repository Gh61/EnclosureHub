namespace EnclosureHub.Core.Entities;

/// <summary>On/off read-only state (door open, camera running, …).</summary>
public sealed class BinarySensorEntity : Entity
{
    public BinarySensorEntity(string id, string name)
        : base(id, name)
    {
    }

    public override EntityKind Kind => EntityKind.BinarySensor;

    /// <summary>Home Assistant device class, e.g. "running", "door".</summary>
    public string? DeviceClass { get; init; }

    /// <summary>Null = unknown.</summary>
    public bool? IsOn { get; private set; }

    public void SetState(bool? isOn)
    {
        if (IsOn == isOn)
            return;

        IsOn = isOn;
        NotifyStateChanged();
    }

    public override object GetState() => new { isOn = IsOn };
}
