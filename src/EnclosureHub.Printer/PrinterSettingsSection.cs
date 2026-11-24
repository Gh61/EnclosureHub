using EnclosureHub.Core.Printer;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Printer;

/// <summary>
/// Dashboard "Settings → Printer": link the hub with the printer (only after it answered M115; the result is
/// stored so the hub works while the printer is off), layer detection, queries and entities. Fields of other
/// sections that depend on the link carry <see cref="SettingTags.Printer"/>; the dashboard lists them here.
/// </summary>
public sealed class PrinterSettingsSection : ISettingsSection
{
    private static readonly SettingChoice[] LayerDetections =
    [
        new(PrinterOptions.LayerDetectionZChange, "Z height change (position report)"),
    ];

    private readonly IOptionsMonitor<PrinterOptions> _options;
    private readonly IPrinterConnection _printer;
    private readonly ISettingsStore _store;
    private readonly string _configPath;
    private readonly bool _entitiesRegistered;

    /// <param name="entitiesRegistered">Whether the printer entities exist in this run (decided at startup).</param>
    public PrinterSettingsSection(IOptionsMonitor<PrinterOptions> options, IPrinterConnection printer, ISettingsStore store, string configPath, bool entitiesRegistered)
    {
        _options = options;
        _printer = printer;
        _store = store;
        _configPath = configPath;
        _entitiesRegistered = entitiesRegistered;
    }

    public string Id => "printer";

    public string Title => "Printer connection";

    public string? Description => "Connects the hub to an Original Prusa with Buddy firmware (MK4/S and others) through its USB-C port, "
        + "at the back of the electronics box above the LAN port. The printer reports when a print starts and ends and every layer change; "
        + "the timelapse and the camera can react to it. The printer must be switched on to link it; afterwards the hub reconnects by itself.";

    public string Group => SettingsGroups.Printer;

    public IReadOnlyList<SettingField> Fields
    {
        get
        {
            var o = _options.CurrentValue;
            var fields = new List<SettingField>
            {
                new("linked", "Printer connection", SettingFieldType.Toggle,
                    "Switching it on asks the printer who it is (M115) and links it only when it answers. Switching it off closes the port; "
                    + "the printer options on the Camera and Timelapse tabs disappear but keep their values."),
            };

            if (!string.IsNullOrEmpty(o.MachineType) || !string.IsNullOrEmpty(o.Firmware))
                fields.Add(new("printer", "Linked printer", SettingFieldType.Info, "As reported by the printer when it was linked."));

            if (o.Linked)
            {
                fields.Add(new("layerDetection", "Layer change detection", SettingFieldType.Choice,
                    "How the hub recognises a new layer. Z height change uses the position the printer reports for every layer and needs no change in the slicer. "
                    + "A custom G-code marker may follow later.",
                    LayerDetections));
                fields.Add(new("query", "Query the printer", SettingFieldType.Toggle,
                    "Asks for the print status every 10 s (M27) and for temperature reports every 5 s (M155); read-only commands. "
                    + "Off = listen only: start and end of a print are still recognised, temperatures only while heating."));
                fields.Add(new("entities", "Printer entities", SettingFieldType.Toggle,
                    "Printer connected, printing, nozzle and bed temperature, Z height and layer for Home Assistant and displays. "
                    + "Turn it off if Home Assistant already gets them through PrusaLink. Takes effect after a restart."));
            }

            return fields;
        }
    }

    public IReadOnlyDictionary<string, object?> GetValues()
    {
        var o = _options.CurrentValue;
        return new Dictionary<string, object?>
        {
            ["linked"] = o.Linked,
            ["printer"] = Describe(o),
            ["layerDetection"] = o.LayerDetection,
            ["query"] = o.Query,
            ["entities"] = o.Entities,
        };
    }

    public async Task<SettingsApplyResult> ApplyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue;
        var changes = new Dictionary<string, object?>();
        string? linkedMessage = null;

        bool linked = current.Linked;
        if (values.TryGetValue("linked", out var linkedText))
        {
            linked = ParseBool(linkedText, "Printer connection");
            if (linked && !current.Linked)
            {
                PrinterInfo info;
                try
                {
                    info = await _printer.ReadInfoAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new SettingsValidationException("Could not link the printer: " + ex.Message);
                }

                changes[Key("MachineType")] = info.MachineType ?? "";
                changes[Key("Firmware")] = info.Firmware ?? "";
                changes[Key("Uuid")] = info.Uuid ?? "";
                linkedMessage = $"Linked with {info.MachineType ?? "the printer"} ({info.Firmware ?? "unknown firmware"}). "
                    + "Printer options are now available on the Camera and Timelapse tabs.";
            }

            changes[Key("Linked")] = linked;
        }

        if (values.TryGetValue("layerDetection", out var detection) && detection != null)
        {
            if (LayerDetections.All(c => c.Value != detection))
                throw new SettingsValidationException("Unknown layer change detection.");
            changes[Key("LayerDetection")] = detection;
        }

        if (values.TryGetValue("query", out var queryText))
            changes[Key("Query")] = ParseBool(queryText, "Query the printer");

        bool entities = current.Entities;
        if (values.TryGetValue("entities", out var entitiesText))
        {
            entities = ParseBool(entitiesText, "Printer entities");
            changes[Key("Entities")] = entities;
        }

        if (changes.Count == 0)
            return new SettingsApplyResult(Message: "Nothing to save.");

        await _store.SaveAsync(changes, cancellationToken);

        bool restart = (linked && entities) != _entitiesRegistered;
        string message = linkedMessage ?? (linked ? "Saved." : "Saved. The printer connection is off.");
        if (restart)
            message += (linked && entities)
                ? " Restart the service to add the printer entities."
                : " Restart the service to remove the printer entities.";

        return new SettingsApplyResult(restart, message);
    }

    private string Key(string name) => _configPath + ":" + name;

    private static string? Describe(PrinterOptions o)
    {
        var parts = new[] { o.MachineType, o.Firmware }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static bool ParseBool(string? text, string label) => text?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "on" => true,
        "false" or "0" or "off" => false,
        _ => throw new SettingsValidationException($"{label} must be on or off."),
    };
}
