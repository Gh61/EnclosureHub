namespace EnclosureHub.Printer;

/// <summary>Bound from "Modules:Printer".</summary>
public sealed class PrinterOptions
{
    /// <summary>The only layer detection so far: the position report the printer sends when Z changes.</summary>
    public const string LayerDetectionZChange = "ZChange";

    /// <summary>Module present (file-only). Whether the hub actually talks to a printer is <see cref="Linked"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The user linked the hub with a printer (dashboard, after the printer answered). Persisted, so the hub keeps
    /// its settings and reconnects by itself while the printer is switched off.
    /// </summary>
    public bool Linked { get; set; }

    /// <summary>Serial device; empty = the first /dev/serial/by-id entry containing "Prusa".</summary>
    public string Port { get; set; } = "";

    public int BaudRate { get; set; } = 115200;

    /// <summary>
    /// Send read-only queries while connected: M155 (temperature report every <see cref="TemperatureIntervalSeconds"/>)
    /// and M27 (print status every <see cref="StatusIntervalSeconds"/>, detects a cancelled print). Off = listen only.
    /// </summary>
    public bool Query { get; set; } = true;

    public int StatusIntervalSeconds { get; set; } = 10;

    public int TemperatureIntervalSeconds { get; set; } = 5;

    /// <summary>How a new layer is recognised; see <see cref="LayerDetectionZChange"/>.</summary>
    public string LayerDetection { get; set; } = LayerDetectionZChange;

    /// <summary>Expose printer entities (connected, printing, temperatures, Z, layer) to Home Assistant and displays. Read at startup.</summary>
    public bool Entities { get; set; } = true;

    /// <summary>Seconds between reconnect attempts while the printer is off or unplugged.</summary>
    public int ReconnectSeconds { get; set; } = 5;

    /// <summary>What the printer reported when it was linked (shown while it is switched off).</summary>
    public string MachineType { get; set; } = "";

    public string Firmware { get; set; } = "";

    public string Uuid { get; set; } = "";
}
