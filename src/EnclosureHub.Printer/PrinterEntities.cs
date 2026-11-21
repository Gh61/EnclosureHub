using EnclosureHub.Core.Entities;

namespace EnclosureHub.Printer;

/// <summary>
/// Printer entities for Home Assistant and displays (connected, printing, temperatures, Z, layer) – a base for
/// automations such as light and fan control. Created only when the printer is linked and
/// <see cref="PrinterOptions.Entities"/> is on at startup; otherwise <see cref="Enabled"/> is false and all are null.
/// </summary>
public sealed class PrinterEntities
{
    public PrinterEntities(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            return;

        Connected = new BinarySensorEntity("printer_connected", "Printer connected")
        {
            DeviceClass = "connectivity",
            IsDiagnostic = true,
            Icon = "mdi:usb",
        };
        Printing = new BinarySensorEntity("printer_printing", "Printer printing")
        {
            DeviceClass = "running",
            Icon = "mdi:printer-3d",
        };
        NozzleTemperature = new SensorEntity("printer_nozzle_temperature", "Nozzle temperature")
        {
            Unit = "°C",
            DeviceClass = "temperature",
            StateClass = "measurement",
            Icon = "mdi:printer-3d-nozzle-heat",
        };
        BedTemperature = new SensorEntity("printer_bed_temperature", "Bed temperature")
        {
            Unit = "°C",
            DeviceClass = "temperature",
            StateClass = "measurement",
            Icon = "mdi:radiator",
        };
        Z = new SensorEntity("printer_z", "Printer Z height")
        {
            Unit = "mm",
            DeviceClass = "distance",
            StateClass = "measurement",
            Icon = "mdi:arrow-expand-vertical",
        };
        Layer = new SensorEntity("printer_layer", "Printer layer")
        {
            StateClass = "measurement",
            Icon = "mdi:layers-triple",
        };
    }

    public bool Enabled { get; }

    public BinarySensorEntity? Connected { get; }

    public BinarySensorEntity? Printing { get; }

    public SensorEntity? NozzleTemperature { get; }

    public SensorEntity? BedTemperature { get; }

    public SensorEntity? Z { get; }

    public SensorEntity? Layer { get; }

    public IEnumerable<Entity> All => Enabled ? [Connected!, Printing!, NozzleTemperature!, BedTemperature!, Z!, Layer!] : [];
}
