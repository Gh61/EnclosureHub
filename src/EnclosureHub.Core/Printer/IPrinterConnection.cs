namespace EnclosureHub.Core.Printer;

/// <summary>
/// Connection to the 3D printer (implemented by the printer module, e.g. Prusa over USB). Other modules
/// react to its events – the timelapse takes a frame per layer, the camera captures a fresh picture,
/// later lights and fans can follow the print – without referencing the printer module itself.
/// </summary>
public interface IPrinterConnection
{
    /// <summary>The user linked the hub with a printer (persisted; stays true while the printer is switched off).</summary>
    bool IsLinked { get; }

    PrinterStatus GetStatus();

    /// <summary>Print started/finished and layer changes. Raised on the connection's reader thread – handlers must be quick.</summary>
    event EventHandler<PrinterEvent>? EventRaised;

    /// <summary>Recent communication (sent and received lines), oldest first, with a sequence number greater than <paramref name="afterSequence"/>.</summary>
    IReadOnlyList<PrinterLogEntry> GetLog(long afterSequence);

    /// <summary>Asks the printer who it is (M115). Works without a link too (opens the port briefly). Throws when it does not answer.</summary>
    Task<PrinterInfo> ReadInfoAsync(CancellationToken cancellationToken);
}

public enum PrinterEventKind
{
    PrintStarted,
    PrintFinished,
    LayerChanged,
}

/// <param name="Layer">Layer number (1 = first layer) for <see cref="PrinterEventKind.LayerChanged"/>.</param>
/// <param name="Detail">File name for PrintStarted, reason for PrintFinished.</param>
public sealed record PrinterEvent(PrinterEventKind Kind, DateTime Utc, int? Layer = null, double? Z = null, string? Detail = null);

public sealed record PrinterInfo(
    string? MachineType,
    string? Firmware,
    string? Uuid,
    IReadOnlyList<string> Capabilities);

public sealed record PrinterStatus(
    bool Linked,
    bool Connected,
    string? Port,
    string? Error,
    PrinterInfo? Info,
    bool Printing,
    string? File,
    double? Progress,
    int? Layer,
    double? Z,
    double? NozzleTemperature,
    double? NozzleTarget,
    double? BedTemperature,
    double? BedTarget,
    DateTime? LastReceivedUtc);

public enum PrinterLogDirection
{
    /// <summary>Received from the printer.</summary>
    In,

    /// <summary>Sent to the printer.</summary>
    Out,

    /// <summary>Note from the hub (connected, disconnected, errors).</summary>
    Info,
}

public sealed record PrinterLogEntry(long Sequence, DateTime Utc, PrinterLogDirection Direction, string Text);
