using System.IO.Ports;
using EnclosureHub.Core.Printer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Printer;

/// <summary>
/// Talks to a Prusa printer (Buddy firmware: MK4/S, MK3.9, MINI, XL, CORE One) over its USB-C port, which appears as
/// a CDC serial device. While <see cref="PrinterOptions.Linked"/> it keeps the port open, reconnects every few seconds
/// while the printer is switched off, tracks the print (start, layers, end, temperatures) and raises
/// <see cref="EventRaised"/>. Writes only read-only queries (M155, M27, M115) and only when <see cref="PrinterOptions.Query"/>
/// is on or the user asks for the printer information.
/// </summary>
public sealed class PrusaUsbPrinter : BackgroundService, IPrinterConnection
{
    private const int LogCapacity = 500;
    private static readonly TimeSpan ReadInfoTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A print survives a lost connection this long (USB hiccup, reconnect); longer = printer switched off.</summary>
    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(60);

    private readonly IOptionsMonitor<PrinterOptions> _options;
    private readonly PrinterEntities _entities;
    private readonly ILogger<PrusaUsbPrinter> _logger;
    private readonly IDisposable? _optionsChange;

    private readonly object _gate = new();
    private readonly LinkedList<PrinterLogEntry> _log = new();
    private readonly PrusaStateTracker _tracker = new();
    private long _sequence;

    private SerialPort? _port;          // open connection (guarded by _gate)
    private string? _portName;
    private string? _error;
    private DateTime? _lastReceivedUtc;
    private PrinterInfo? _info;
    private TaskCompletionSource<string>? _infoReply;  // waits for "FIRMWARE_NAME:" after M115
    private List<string>? _capabilities;

    // Cancelled when the link is switched on/off so the loop reacts immediately
    private CancellationTokenSource _wake = new();

    public PrusaUsbPrinter(IOptionsMonitor<PrinterOptions> options, PrinterEntities entities, ILogger<PrusaUsbPrinter> logger)
    {
        _options = options;
        _entities = entities;
        _logger = logger;
        _info = StoredInfo(options.CurrentValue);
        _applied = (options.CurrentValue.Linked, options.CurrentValue.Query);
        _optionsChange = options.OnChange(o =>
        {
            // Reconnect when the link or the query mode changes; other saves (and duplicate reloads) are ignored
            lock (_gate)
            {
                if ((o.Linked, o.Query) == _applied)
                    return;
                _applied = (o.Linked, o.Query);
            }

            try
            {
                Volatile.Read(ref _wake).Cancel();
            }
            catch (ObjectDisposedException)
            {
                // the loop is just replacing it and re-reads the options anyway
            }
        });
    }

    private (bool Linked, bool Query) _applied;

    // M155 auto-report was switched on in the printer; switch it off once when Query is turned off
    private bool _autoReportSent;

    public bool IsLinked => _options.CurrentValue.Linked;

    public event EventHandler<PrinterEvent>? EventRaised;

    public override void Dispose()
    {
        _optionsChange?.Dispose();
        base.Dispose();
    }

    // ------------------------------------------------------------------ connection loop

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        UpdateEntities();

        while (!stoppingToken.IsCancellationRequested)
        {
            var wake = new CancellationTokenSource();
            Interlocked.Exchange(ref _wake, wake).Dispose();
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, wake.Token);
            var options = _options.CurrentValue;

            // A print whose printer stayed away too long is over
            RaiseUnderLock(t => t.CheckLost(DateTime.UtcNow, DisconnectGrace));

            try
            {
                if (!options.Linked)
                {
                    SetError(null);
                    RaiseUnderLock(t => t.Unlinked(DateTime.UtcNow));
                    UpdateEntities();
                    await Task.Delay(Timeout.Infinite, cycle.Token);
                    continue;
                }

                string? portName = ResolvePort(options);
                if (portName == null)
                {
                    SetError("Printer not found – is it switched on and connected over USB?");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.ReconnectSeconds)), cycle.Token);
                    continue;
                }

                await RunConnectionAsync(portName, options, cycle.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // link switched on/off – re-read the options
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                _logger.LogDebug(ex, "Printer connection failed");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.ReconnectSeconds)), cycle.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    private async Task RunConnectionAsync(string portName, PrinterOptions options, CancellationToken cancellationToken)
    {
        using var port = Open(portName, options);
        lock (_gate)
        {
            _port = port;
            _portName = portName;
            _error = null;
            _tracker.Connected(expectStatus: options.Query);
        }

        AddLog(PrinterLogDirection.Info, "Connected to " + portName);
        _logger.LogInformation("Printer connected on {Port}", portName);
        UpdateEntities();

        // A dedicated thread: SerialPort reads block, and on Linux they are the reliable way to notice an unplugged printer
        var reader = Task.Factory.StartNew(() => ReadLoop(port, cancellationToken), TaskCreationOptions.LongRunning);

        try
        {
            if (options.Query)
            {
                Send(port, "M155 S" + Math.Max(1, options.TemperatureIntervalSeconds));
                Send(port, "M27");
                _autoReportSent = true;
            }
            else if (_autoReportSent)
            {
                Send(port, "M155 S0"); // queries were just switched off: stop the temperature reports we asked for
                _autoReportSent = false;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, options.StatusIntervalSeconds)));
            while (!reader.IsCompleted && await WhenTickOrDoneAsync(timer, reader, cancellationToken))
            {
                // Only when the last query was answered and no blocking command runs (the printer does not read then)
                bool canQuery;
                lock (_gate)
                    canQuery = _tracker.CanQuery(DateTime.UtcNow);

                if (_options.CurrentValue.Query && canQuery)
                    Send(port, "M27");
            }
        }
        finally
        {
            lock (_gate)
                _port = null;

            try
            {
                // Unlinked: leave the printer as we found it (no temperature reports)
                if (_autoReportSent && !_options.CurrentValue.Linked && port.IsOpen)
                {
                    Send(port, "M155 S0");
                    _autoReportSent = false;
                }

                port.Close();
            }
            catch
            {
                // already gone
            }

            await reader;
            AddLog(PrinterLogDirection.Info, "Disconnected");
            _logger.LogInformation("Printer disconnected");

            // Not the end of a print yet: the printer may be back in a few seconds (see DisconnectGrace)
            lock (_gate)
                _tracker.ConnectionLost(DateTime.UtcNow);
            UpdateEntities();
        }
    }

    private static async Task<bool> WhenTickOrDoneAsync(PeriodicTimer timer, Task reader, CancellationToken cancellationToken)
    {
        var tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
        var done = await Task.WhenAny(tick, reader);
        if (done == tick)
            return await tick;
        return false;
    }

    private static SerialPort Open(string portName, PrinterOptions options)
    {
        var port = new SerialPort(portName, options.BaudRate)
        {
            NewLine = "\n",
            ReadTimeout = 500,
            WriteTimeout = 2000,
            Encoding = System.Text.Encoding.ASCII,
        };
        port.Open();
        return port;
    }

    /// <summary>Reads raw bytes and splits lines itself (no ReadLine: a read timeout must never drop a partial line).</summary>
    private void ReadLoop(SerialPort port, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var line = new System.Text.StringBuilder(256);

        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = port.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetError("Connection lost: " + ex.Message);
                return; // unplugged / switched off / closed
            }

            for (int i = 0; i < read; i++)
            {
                char c = (char)buffer[i];
                if (c == '\n')
                {
                    OnLine(line.ToString().TrimEnd('\r'));
                    line.Clear();
                }
                else if (line.Length < 4096)
                {
                    line.Append(c);
                }
            }
        }
    }

    internal void OnLine(string line)
    {
        if (line.Length == 0)
            return;

        var now = DateTime.UtcNow;
        AddLog(PrinterLogDirection.In, line);

        IReadOnlyList<PrinterEvent> events;
        lock (_gate)
        {
            _lastReceivedUtc = now;
            events = _tracker.Consume(line, now);
            CollectInfo(line);
        }

        Raise(events);
        UpdateEntities();
    }

    private void Raise(IEnumerable<PrinterEvent> events)
    {
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case PrinterEventKind.PrintStarted:
                    _logger.LogInformation("Print started: {File}", e.Detail ?? "?");
                    break;
                case PrinterEventKind.PrintFinished:
                    _logger.LogInformation("Print finished ({Reason})", e.Detail);
                    break;
                default:
                    _logger.LogDebug("Layer {Layer} at Z {Z}", e.Layer, e.Z);
                    break;
            }

            try
            {
                EventRaised?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Printer event handler failed");
            }
        }
    }

    /// <summary>
    /// Writes one command. A write that times out (the printer is not reading, e.g. during a blocking command) is
    /// logged and skipped – it never drops the connection.
    /// </summary>
    private bool Send(SerialPort port, string command)
    {
        try
        {
            lock (port)
                port.Write(command + "\n");
        }
        catch (TimeoutException)
        {
            AddLog(PrinterLogDirection.Info, "Printer busy, not sent: " + command);
            return false;
        }

        lock (_gate)
            _tracker.QuerySent(DateTime.UtcNow);
        AddLog(PrinterLogDirection.Out, command);
        return true;
    }

    private void RaiseUnderLock(Func<PrusaStateTracker, IReadOnlyList<PrinterEvent>> action)
    {
        List<PrinterEvent> events;
        lock (_gate)
            events = [.. action(_tracker)];
        Raise(events);
    }

    // ------------------------------------------------------------------ printer information (M115)

    public async Task<PrinterInfo> ReadInfoAsync(CancellationToken cancellationToken)
    {
        SerialPort? port;
        lock (_gate)
            port = _port;

        if (port != null)
            return await QueryInfoAsync(port, refreshStatus: true, cancellationToken);

        // Not connected (not linked yet): open the port just for this question
        var options = _options.CurrentValue;
        string portName = ResolvePort(options)
            ?? throw new InvalidOperationException("No printer found on USB. Switch the printer on and connect its USB-C port to the hub.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var temporary = Open(portName, options);
        AddLog(PrinterLogDirection.Info, "Opened " + portName + " to read the printer information");
        var reader = Task.Factory.StartNew(() => ReadLoop(temporary, cts.Token), TaskCreationOptions.LongRunning);
        try
        {
            return await QueryInfoAsync(temporary, refreshStatus: false, cancellationToken); // the port closes right after
        }
        finally
        {
            cts.Cancel();
            temporary.Close();
            await reader;
            AddLog(PrinterLogDirection.Info, "Closed " + portName);
        }
    }

    private async Task<PrinterInfo> QueryInfoAsync(SerialPort port, bool refreshStatus, CancellationToken cancellationToken)
    {
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _infoReply = reply;
            _capabilities = [];
        }

        Send(port, "M115");

        string firmwareLine;
        try
        {
            firmwareLine = await reply.Task.WaitAsync(ReadInfoTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            lock (_gate)
                _infoReply = null;
            throw new InvalidOperationException("The printer did not answer M115 within 5 s.");
        }

        // Capabilities follow right after the firmware line
        await Task.Delay(300, cancellationToken);

        PrinterInfo info;
        lock (_gate)
        {
            info = ParseFirmwareLine(firmwareLine, _capabilities ?? []);
            _capabilities = null;
            _info = info;
        }

        // Also refresh the status (temperatures, printing) for the printer page
        if (refreshStatus)
        {
            Send(port, "M105");
            Send(port, "M27");
        }

        return info;
    }

    /// <summary>Called under _gate for every received line.</summary>
    private void CollectInfo(string line)
    {
        if (line.StartsWith("FIRMWARE_NAME:", StringComparison.Ordinal) && _infoReply != null)
        {
            _infoReply.TrySetResult(line);
            _infoReply = null;
        }
        else if (line.StartsWith("Cap:", StringComparison.Ordinal) && _capabilities != null)
        {
            _capabilities.Add(line[4..]);
        }
    }

    /// <summary>"FIRMWARE_NAME:Prusa-Firmware-Buddy 6.5.7+12836 (Github) SOURCE_CODE_URL:… MACHINE_TYPE:Prusa-MK4S EXTRUDER_COUNT:1 UUID:…"</summary>
    internal static PrinterInfo ParseFirmwareLine(string line, IReadOnlyList<string> capabilities)
    {
        string? Field(string name)
        {
            int start = line.IndexOf(name + ":", StringComparison.Ordinal);
            if (start < 0)
                return null;
            start += name.Length + 1;

            // Values may contain spaces (firmware name); they end where the next KEY: begins
            int end = line.Length;
            foreach (string next in new[] { "SOURCE_CODE_URL:", "PROTOCOL_VERSION:", "MACHINE_TYPE:", "EXTRUDER_COUNT:", "UUID:", "FIRMWARE_NAME:" })
            {
                int index = line.IndexOf(" " + next, start, StringComparison.Ordinal);
                if (index >= 0 && index < end)
                    end = index;
            }

            string value = line[start..end].Trim();
            return value.Length == 0 ? null : value;
        }

        string? firmware = Field("FIRMWARE_NAME");
        if (firmware != null && firmware.EndsWith(" (Github)", StringComparison.Ordinal))
            firmware = firmware[..^" (Github)".Length];

        return new PrinterInfo(Field("MACHINE_TYPE"), firmware, Field("UUID"), capabilities);
    }

    private static PrinterInfo? StoredInfo(PrinterOptions options)
        => string.IsNullOrEmpty(options.MachineType) && string.IsNullOrEmpty(options.Firmware)
            ? null
            : new PrinterInfo(NullIfEmpty(options.MachineType), NullIfEmpty(options.Firmware), NullIfEmpty(options.Uuid), []);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    // ------------------------------------------------------------------ status, log

    public PrinterStatus GetStatus()
    {
        var options = _options.CurrentValue;
        lock (_gate)
        {
            return new PrinterStatus(
                options.Linked,
                _port != null,
                _portName ?? ResolvePortQuietly(options),
                _error,
                _info ?? StoredInfo(options),
                _tracker.Printing,
                _tracker.File,
                _tracker.Progress,
                _tracker.Layer,
                _tracker.Z,
                _tracker.NozzleTemperature,
                _tracker.NozzleTarget,
                _tracker.BedTemperature,
                _tracker.BedTarget,
                _lastReceivedUtc);
        }
    }

    public IReadOnlyList<PrinterLogEntry> GetLog(long afterSequence)
    {
        lock (_log)
            return [.. _log.Where(e => e.Sequence > afterSequence)];
    }

    private void AddLog(PrinterLogDirection direction, string text)
    {
        lock (_log)
        {
            _log.AddLast(new PrinterLogEntry(++_sequence, DateTime.UtcNow, direction, text));
            while (_log.Count > LogCapacity)
                _log.RemoveFirst();
        }
    }

    private void SetError(string? error)
    {
        bool changed;
        lock (_gate)
        {
            changed = _error != error;
            _error = error;
        }

        if (changed && error != null)
            AddLog(PrinterLogDirection.Info, error);
    }

    private void UpdateEntities()
    {
        if (!_entities.Enabled)
            return;

        lock (_gate)
        {
            _entities.Connected!.SetState(_options.CurrentValue.Linked ? _port != null : null);
            _entities.Printing!.SetState(_port != null || _tracker.Printing ? _tracker.Printing : null);
            _entities.NozzleTemperature!.SetValue(Round(_tracker.NozzleTemperature));
            _entities.BedTemperature!.SetValue(Round(_tracker.BedTemperature));
            _entities.Z!.SetValue(_tracker.Z);
            _entities.Layer!.SetValue(_tracker.Layer);
        }

        // Whole degrees: the reports come every few seconds and would otherwise flood MQTT with tenths
        static double? Round(double? value) => value is { } v ? Math.Round(v) : null;
    }

    // ------------------------------------------------------------------ port discovery

    /// <summary>The configured port, or the first /dev/serial/by-id entry of a Prusa printer; null when there is none.</summary>
    internal static string? ResolvePort(PrinterOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Port))
            return File.Exists(options.Port.Trim()) || !OperatingSystem.IsLinux() ? options.Port.Trim() : null;

        const string byId = "/dev/serial/by-id";
        if (!Directory.Exists(byId))
            return null;

        return Directory.EnumerateFileSystemEntries(byId)
            .Where(p => Path.GetFileName(p).Contains("Prusa", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? ResolvePortQuietly(PrinterOptions options)
    {
        try
        {
            return ResolvePort(options);
        }
        catch
        {
            return null;
        }
    }
}
