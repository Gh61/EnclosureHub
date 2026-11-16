namespace EnclosureHub.Core;

/// <summary>
/// Identity of this controller as shown to integrations (Home Assistant device, API).
/// Bound from the "Device" configuration section.
/// </summary>
public sealed class DeviceOptions
{
    /// <summary>Stable identifier, used in MQTT topics and unique IDs. Lowercase, no spaces.</summary>
    public string Id { get; set; } = "enclosurehub";

    public string Name { get; set; } = "Printer Enclosure";

    public string Manufacturer { get; set; } = "DIY";

    public string Model { get; set; } = "Raspberry Pi";
}
