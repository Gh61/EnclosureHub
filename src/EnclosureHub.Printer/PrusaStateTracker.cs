using System.Globalization;
using System.Text.RegularExpressions;
using EnclosureHub.Core.Printer;

namespace EnclosureHub.Printer;

/// <summary>
/// Turns the lines a Prusa (Buddy firmware) sends over USB into state and events. Observed on an MK4S with
/// firmware 6.5.7 while printing from the USB drive, and checked against the firmware sources:
/// <list type="bullet">
/// <item>"File opened: /usb/NAME~1.BGC Size:40041" when a print starts,</item>
/// <item>"X:136.10 Y:93.90 Z:0.60 E:0.00 Count …" once per layer (when the Z move is processed),</item>
/// <item>"Done printing file" when a print ends – finished or cancelled (<c>finalize_print</c>); not on pause,</item>
/// <item>" T:28.00/0.00 B:28.21/0.00 …" temperatures (while heating, or every few seconds after M155),</item>
/// <item>"SD printing byte 123/456" / "Not SD printing" for M27. "Not SD printing" also while paused, so it never ends a print.</item>
/// </list>
/// Not thread-safe; the connection feeds it from its reader thread.
/// </summary>
internal sealed partial class PrusaStateTracker
{
    /// <summary>Z steps larger than this are travel (start position, parking at the end), not a new layer.</summary>
    private const double MaxLayerStep = 1.5;

    private double? _layerZ;

    public bool Printing { get; private set; }

    public string? File { get; private set; }

    public double? Progress { get; private set; }

    public int? Layer { get; private set; }

    public double? Z { get; private set; }

    public double? NozzleTemperature { get; private set; }

    public double? NozzleTarget { get; private set; }

    public double? BedTemperature { get; private set; }

    public double? BedTarget { get; private set; }

    /// <summary>Processes one received line; returns the events it caused (usually none).</summary>
    public IReadOnlyList<PrinterEvent> Consume(string line, DateTime utc)
    {
        line = line.Trim();
        if (line.StartsWith("ok", StringComparison.Ordinal))
            line = line[2..].Trim(); // "ok T:…" answers carry the report after "ok"

        if (line.Length == 0)
            return [];

        if (line.StartsWith("File opened:", StringComparison.Ordinal))
        {
            var match = FileOpenedRegex().Match(line);
            return StartPrint(match.Success ? match.Groups[1].Value : line["File opened:".Length..].Trim(), utc);
        }

        if (line.StartsWith("Done printing file", StringComparison.Ordinal))
            return FinishPrint("done printing file", utc);

        if (line.StartsWith("SD printing byte", StringComparison.Ordinal))
        {
            // Connected in the middle of a print
            var events = Printing ? [] : StartPrint(File, utc);

            var match = ProgressRegex().Match(line);
            if (match.Success && long.TryParse(match.Groups[2].Value, out long size) && size > 0)
                Progress = Math.Round(100.0 * long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / size, 1);

            return events;
        }

        if (line.StartsWith("X:", StringComparison.Ordinal))
            return Position(line, utc);

        if (line.StartsWith("T:", StringComparison.Ordinal))
            Temperatures(line);

        return [];
    }

    /// <summary>The printer disappeared (switched off, cable pulled): a running print is over for us.</summary>
    public IReadOnlyList<PrinterEvent> Disconnected(DateTime utc)
        => FinishPrint("printer disconnected", utc);

    private IReadOnlyList<PrinterEvent> StartPrint(string? file, DateTime utc)
    {
        Printing = true;
        File = file;
        Progress = 0;
        Layer = null;
        _layerZ = null;
        return [new PrinterEvent(PrinterEventKind.PrintStarted, utc, Detail: file)];
    }

    private IReadOnlyList<PrinterEvent> FinishPrint(string reason, DateTime utc)
    {
        if (!Printing)
            return [];

        Printing = false;
        Progress = null;
        return [new PrinterEvent(PrinterEventKind.PrintFinished, utc, Layer, Z, reason)];
    }

    /// <summary>
    /// Layer detection from Z: while printing, a Z increase of up to <see cref="MaxLayerStep"/> mm over the last
    /// layer is a new layer; a decrease (start of the print, after the purge position) restarts at layer 1.
    /// </summary>
    private IReadOnlyList<PrinterEvent> Position(string line, DateTime utc)
    {
        var match = ZRegex().Match(line);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
            return [];

        Z = z;
        if (!Printing)
            return [];

        if (_layerZ is not { } last)
        {
            _layerZ = z;
            return [];
        }

        double step = z - last;
        if (step < -0.001)
        {
            // Back down: the first layer (after the start/purge position)
            _layerZ = z;
            Layer = 1;
            return [];
        }

        if (step > 0.001 && step <= MaxLayerStep)
        {
            _layerZ = z;
            Layer = (Layer ?? 1) + 1;
            return [new PrinterEvent(PrinterEventKind.LayerChanged, utc, Layer, z)];
        }

        return []; // same height, or a big travel move (parking)
    }

    private void Temperatures(string line)
    {
        var nozzle = TemperatureRegex("T").Match(line);
        if (nozzle.Success)
        {
            NozzleTemperature = Parse(nozzle.Groups[1].Value);
            NozzleTarget = Parse(nozzle.Groups[2].Value);
        }

        var bed = TemperatureRegex("B").Match(line);
        if (bed.Success)
        {
            BedTemperature = Parse(bed.Groups[1].Value);
            BedTarget = Parse(bed.Groups[2].Value);
        }
    }

    private static double? Parse(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    private static Regex TemperatureRegex(string name) => name == "T" ? NozzleRegex() : BedRegex();

    [GeneratedRegex(@"^File opened:\s*(.+?)\s+Size:\s*\d+")]
    private static partial Regex FileOpenedRegex();

    [GeneratedRegex(@"SD printing byte\s+(\d+)\s*/\s*(\d+)")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"\bZ:(-?\d+(?:\.\d+)?)")]
    private static partial Regex ZRegex();

    [GeneratedRegex(@"(?:^|\s)T:(-?\d+(?:\.\d+)?)\s*/\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex NozzleRegex();

    [GeneratedRegex(@"(?:^|\s)B:(-?\d+(?:\.\d+)?)\s*/\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex BedRegex();
}
