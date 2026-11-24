using EnclosureHub.Core.Printer;
using EnclosureHub.Core.Settings;
using EnclosureHub.Printer;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Tests;

public class PrinterTests
{
    /// <summary>Recorded from an MK4S (firmware 6.5.7) printing a small plate from the USB drive (temperatures thinned out).</summary>
    private static readonly string[] RecordedPrint =
    [
        "File opened: /usb/TVAR-D~1.BGC Size:40041",
        " 255 B@:255 HBR@:55 W:?",
        " T:95.19/175.00 B:34.53/85.00 X:28.74/36.00 A:42.41/0.00 @:255 B@:255 HBR@:55 W:?",
        "echo:busy: processing",
        " T:169.55/175.00 B:53.35/85.00 X:32.29/36.00 A:43.86/0.00 @:114 B@:255 HBR@:55 W:?",
        "echo:== Precise Homing axis X try 0 ==",
        "echo:endstops hit:  X:-253.46",
        "echo:endstops hit:  Z:-58.84",
        "echo:Probe classified as clean and OK",
        "X:-1.00 Y:-4.00 Z:2.00 E:-23.51 Count X:-100 Y:-400 Z:800",
        "echo:endstops hit:  Z:0.07",
        "Default storage slot 0 selected.",
        "Mesh invalidated. Probing mesh.",
        "X:87.50 Y:73.50 Z:2.00 E:-25.51 Count X:8750 Y:7350 Z:800",
        "X:12.50 Y:10.50 Z:2.00 E:-25.51 Count X:1250 Y:1050 Z:800",
        "Extrapolating mesh...",
        "done",
        "Unified Bed Leveling System v1.01 active",
        "X:0.00 Y:-4.00 Z:15.00 E:0.00 Count X:0 Y:-400 Z:6009",
        "X:0.00 Y:-4.00 Z:15.00 E:0.00 Count X:0 Y:-400 Z:6009",
        "X:51.00 Y:-4.00 Z:0.20 E:0.00 Count X:0 Y:-400 Z:6009",
        "X:114.52 Y:116.41 Z:0.20 E:0.00 Count X:11387 Y:10792 Z:191",
        "X:136.10 Y:116.10 Z:0.40 E:0.00 Count X:13294 Y:11625 Z:277",
        "X:136.10 Y:93.90 Z:0.60 E:0.00 Count X:13448 Y:9359 Z:340",
        "X:136.10 Y:116.10 Z:0.80 E:0.00 Count X:13292 Y:11618 Z:436",
        "X:113.54 Y:116.49 Z:1.00 E:0.00 Count X:11350 Y:9719 Z:503",
        "X:114.22 Y:93.30 Z:1.20 E:0.00 Count X:11364 Y:10153 Z:586",
        "X:136.10 Y:93.90 Z:1.40 E:0.00 Count X:13449 Y:9355 Z:659",
        "X:136.10 Y:116.10 Z:1.60 E:0.00 Count X:13294 Y:11616 Z:756",
        "X:136.10 Y:93.90 Z:1.80 E:0.00 Count X:13450 Y:9361 Z:820",
        "echo:busy: processing",
        "X:241.00 Y:170.00 Z:45.39 E:3.30 Count X:24100 Y:17000 Z:18156",
        "Done printing file",
    ];

    [Fact]
    public void Recorded_print_gives_start_layers_and_end()
    {
        var tracker = new PrusaStateTracker();
        var events = new List<PrinterEvent>();
        var t = DateTime.UtcNow;
        foreach (string line in RecordedPrint)
            events.AddRange(tracker.Consume(line, t = t.AddSeconds(1)));

        Assert.Equal(PrinterEventKind.PrintStarted, events[0].Kind);
        Assert.Equal("/usb/TVAR-D~1.BGC", events[0].Detail);

        var layers = events.Where(e => e.Kind == PrinterEventKind.LayerChanged).ToList();
        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9], layers.Select(e => e.Layer!.Value));
        Assert.Equal([0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8], layers.Select(e => e.Z!.Value));

        var end = events[^1];
        Assert.Equal(PrinterEventKind.PrintFinished, end.Kind);
        Assert.Equal("done printing file", end.Detail);
        Assert.False(tracker.Printing);
        Assert.Equal(45.39, tracker.Z);
        Assert.Equal(169.55, tracker.NozzleTemperature);
        Assert.Equal(175, tracker.NozzleTarget);
        Assert.Equal(53.35, tracker.BedTemperature);
    }

    [Fact]
    public void Status_answers_and_disconnect()
    {
        var tracker = new PrusaStateTracker();
        var t = DateTime.UtcNow;

        Assert.Empty(tracker.Consume("ok T:28.00/0.00 B:28.21/0.00 X:28.14/36.00 A:41.72/0.00 @:0 B@:0 HBR@:0", t));
        Assert.Equal(28.0, tracker.NozzleTemperature);
        Assert.Equal(28.21, tracker.BedTemperature);
        Assert.Empty(tracker.Consume("Not SD printing", t));

        // Connected in the middle of a print: M27 says it is printing
        var started = Assert.Single(tracker.Consume("SD printing byte 10000/40000", t));
        Assert.Equal(PrinterEventKind.PrintStarted, started.Kind);
        Assert.Equal(25.0, tracker.Progress);

        // Paused prints also answer "Not SD printing" (firmware: is_printing_state) – must not end the print
        Assert.Empty(tracker.Consume("Not SD printing", t));
        Assert.True(tracker.Printing);

        // A lost connection is not the end of the print – only when the printer stays away
        tracker.ConnectionLost(t);
        Assert.Empty(tracker.CheckLost(t.AddSeconds(30), TimeSpan.FromSeconds(60)));
        Assert.True(tracker.Printing);
        var finished = Assert.Single(tracker.CheckLost(t.AddSeconds(61), TimeSpan.FromSeconds(60)));
        Assert.Equal("printer disconnected", finished.Detail);
        Assert.Empty(tracker.CheckLost(t.AddSeconds(120), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Reconnect_during_a_print_continues_it()
    {
        // Recorded on 2026-11-23: write timeouts during mesh probing dropped the connection every minute
        var tracker = new PrusaStateTracker();
        var t = DateTime.UtcNow;
        tracker.Consume("File opened: /usb/BATTER~2.BGC Size:8375974", t);

        tracker.ConnectionLost(t);
        tracker.Connected(expectStatus: true);
        Assert.Empty(tracker.Consume("ok", t));
        Assert.Empty(tracker.Consume("SD printing byte 543/8375974", t)); // still printing: no new start
        Assert.True(tracker.Printing);
        Assert.Empty(tracker.CheckLost(t.AddMinutes(5), TimeSpan.FromSeconds(60)));

        // Later "Not SD printing" (pause) does not end it …
        Assert.Empty(tracker.Consume("Not SD printing", t));
        Assert.True(tracker.Printing);

        // … but right after a reconnect it does (printer restarted / print ended meanwhile)
        tracker.ConnectionLost(t);
        tracker.Connected(expectStatus: true);
        var finished = Assert.Single(tracker.Consume("Not SD printing", t));
        Assert.Equal(PrinterEventKind.PrintFinished, finished.Kind);
    }

    [Fact]
    public void Queries_wait_for_the_answer_and_while_the_printer_is_busy()
    {
        var tracker = new PrusaStateTracker();
        var t = DateTime.UtcNow;
        Assert.True(tracker.CanQuery(t));

        tracker.QuerySent(t);
        Assert.False(tracker.CanQuery(t.AddSeconds(10)));
        tracker.Consume("ok", t);
        Assert.True(tracker.CanQuery(t.AddSeconds(10)));

        // Blocking command: keepalive every 2 s, the printer does not read the input
        tracker.Consume("echo:busy: processing", t.AddSeconds(20));
        Assert.False(tracker.CanQuery(t.AddSeconds(22)));
        Assert.True(tracker.CanQuery(t.AddSeconds(26)));

        // A lost "ok" blocks queries only for a while
        tracker.QuerySent(t.AddSeconds(30));
        Assert.False(tracker.CanQuery(t.AddSeconds(60)));
        Assert.True(tracker.CanQuery(t.AddSeconds(91)));
    }

    [Fact]
    public void Firmware_line_is_parsed()
    {
        var info = PrusaUsbPrinter.ParseFirmwareLine(
            "FIRMWARE_NAME:Prusa-Firmware-Buddy 6.5.7+12836 (Github) SOURCE_CODE_URL:https://github.com/prusa3d/Prusa-Firmware-Buddy "
            + "PROTOCOL_VERSION:1.0 MACHINE_TYPE:Prusa-MK4S EXTRUDER_COUNT:1 UUID:cede2a2f-41a2-4748-9b12-c55c62f367ff",
            ["AUTOREPORT_TEMP:1"]);

        Assert.Equal("Prusa-MK4S", info.MachineType);
        Assert.Equal("Prusa-Firmware-Buddy 6.5.7+12836", info.Firmware);
        Assert.Equal("cede2a2f-41a2-4748-9b12-c55c62f367ff", info.Uuid);
        Assert.Equal(["AUTOREPORT_TEMP:1"], info.Capabilities);
    }

    [Fact]
    public async Task Linking_requires_an_answer_and_stores_the_printer()
    {
        var store = new FakeStore();
        var printer = new FakePrinter { Info = new PrinterInfo("Prusa-MK4S", "Prusa-Firmware-Buddy 6.5.7", "uuid", []) };
        var section = new PrinterSettingsSection(new Monitor<PrinterOptions>(new PrinterOptions()), printer, store, "Modules:Printer", entitiesRegistered: false);

        Assert.Equal(["linked"], section.Fields.Select(f => f.Key));

        var result = await section.ApplyAsync(new Dictionary<string, string?> { ["linked"] = "true" }, CancellationToken.None);

        Assert.Equal(true, store.Saved["Modules:Printer:Linked"]);
        Assert.Equal("Prusa-MK4S", store.Saved["Modules:Printer:MachineType"]);
        Assert.Equal("Prusa-Firmware-Buddy 6.5.7", store.Saved["Modules:Printer:Firmware"]);
        Assert.True(result.RestartRequired); // entities appear after a restart
        Assert.Contains("Prusa-MK4S", result.Message);
    }

    [Fact]
    public async Task Linking_fails_without_saving_when_the_printer_does_not_answer()
    {
        var store = new FakeStore();
        var printer = new FakePrinter { Error = new InvalidOperationException("No printer found on USB.") };
        var section = new PrinterSettingsSection(new Monitor<PrinterOptions>(new PrinterOptions()), printer, store, "Modules:Printer", entitiesRegistered: false);

        var ex = await Assert.ThrowsAsync<SettingsValidationException>(() =>
            section.ApplyAsync(new Dictionary<string, string?> { ["linked"] = "true" }, CancellationToken.None));

        Assert.Contains("No printer found", ex.Message);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task Linked_section_shows_its_options_and_does_not_ask_the_printer_again()
    {
        var store = new FakeStore();
        var printer = new FakePrinter { Error = new InvalidOperationException("switched off") };
        var options = new PrinterOptions { Linked = true, MachineType = "Prusa-MK4S", Firmware = "6.5.7", Entities = false };
        var section = new PrinterSettingsSection(new Monitor<PrinterOptions>(options), printer, store, "Modules:Printer", entitiesRegistered: false);

        Assert.Equal(["linked", "printer", "layerDetection", "query", "entities"], section.Fields.Select(f => f.Key));
        Assert.Equal("Prusa-MK4S · 6.5.7", section.GetValues()["printer"]);

        // Saving while the printer is off must work (it stays linked)
        var result = await section.ApplyAsync(new Dictionary<string, string?>
        {
            ["linked"] = "true",
            ["layerDetection"] = "ZChange",
            ["query"] = "false",
            ["entities"] = "false",
        }, CancellationToken.None);

        Assert.Equal(false, store.Saved["Modules:Printer:Query"]);
        Assert.False(result.RestartRequired);
        await Assert.ThrowsAsync<SettingsValidationException>(() =>
            section.ApplyAsync(new Dictionary<string, string?> { ["layerDetection"] = "Gcode" }, CancellationToken.None));
    }

    internal sealed class FakePrinter : IPrinterConnection
    {
        public PrinterInfo? Info { get; set; }

        public Exception? Error { get; set; }

        public bool IsLinked { get; set; }

        public bool Connected { get; set; } = true;

        public bool Printing { get; set; }

        public event EventHandler<PrinterEvent>? EventRaised;

        public void Raise(PrinterEvent e) => EventRaised?.Invoke(this, e);

        public PrinterStatus GetStatus() => new(IsLinked, Connected, "/dev/ttyACM0", null, Info, Printing, null, null, null, null, null, null, null, null, null);

        public IReadOnlyList<PrinterLogEntry> GetLog(long afterSequence) => [];

        public Task<PrinterInfo> ReadInfoAsync(CancellationToken cancellationToken)
            => Error != null ? Task.FromException<PrinterInfo>(Error) : Task.FromResult(Info!);
    }

    internal sealed class FakeStore : ISettingsStore
    {
        public Dictionary<string, object?> Saved { get; } = new();

        public Task SaveAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
        {
            foreach (var (key, value) in values)
                Saved[key] = value;
            return Task.CompletedTask;
        }
    }

    internal sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
