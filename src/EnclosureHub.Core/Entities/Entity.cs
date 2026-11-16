namespace EnclosureHub.Core.Entities;

/// <summary>
/// Base class for everything the controller exposes: cameras, sensors, lights, fans…
/// Modules create and update entities; adapters (MQTT, HTTP API, displays) only read
/// their state, listen to <see cref="StateChanged"/> and send commands.
/// </summary>
public abstract class Entity
{
    protected Entity(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Entity id is required.", nameof(id));

        Id = id;
        Name = name;
    }

    /// <summary>Unique within this device. Lowercase snake_case, e.g. "cpu_temperature".</summary>
    public string Id { get; }

    public string Name { get; }

    public abstract EntityKind Kind { get; }

    /// <summary>Optional Material Design icon, e.g. "mdi:fan".</summary>
    public string? Icon { get; init; }

    /// <summary>Technical entity (shown as "diagnostic" in Home Assistant).</summary>
    public bool IsDiagnostic { get; init; }

    public DateTime LastChangedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Raised whenever the state changes. May be raised from any thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>JSON-serializable snapshot of the current state.</summary>
    public abstract object GetState();

    protected void NotifyStateChanged()
    {
        LastChangedUtc = DateTime.UtcNow;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
