using System.Globalization;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Mqtt;

/// <summary>
/// Runtime-editable Home Assistant settings (dashboard "Settings → Home Assistant"): how the camera is
/// published and where Home Assistant can reach this hub. Broker address and credentials stay file-only.
/// </summary>
public sealed class MqttSettingsSection : ISettingsSection
{
    private static readonly SettingChoice[] CameraModes =
    [
        new("Image", "Image (JPEG over MQTT)"),
        new("Link", "Link (URLs only)"),
    ];

    private readonly IOptionsMonitor<MqttBridgeOptions> _options;
    private readonly ISettingsStore _store;
    private readonly string _configPath;

    public MqttSettingsSection(IOptionsMonitor<MqttBridgeOptions> options, ISettingsStore store, string configPath)
    {
        _options = options;
        _store = store;
        _configPath = configPath;
    }

    public string Id => "homeassistant";

    public string Title => "Home Assistant";

    public string? Description => "How the camera appears in Home Assistant via MQTT. Broker address and login are set in appsettings.Local.json.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("cameraMode", "Camera in Home Assistant", SettingFieldType.Choice,
            "Image sends every picture through the broker (MQTT camera). Link publishes only URLs and Home Assistant fetches "
            + "the snapshot itself (MQTT image), so fast camera modes do not load the broker. For live video add the "
            + "MJPEG IP Camera integration with the stream URL from the entity attributes.",
            CameraModes),
        new("imageInterval", "Image interval", SettingFieldType.Number,
            "Seconds between pictures (or snapshot links) sent to Home Assistant. Only new frames are sent.",
            Min: 1, Max: 3600, Step: 1),
        new("publicUrl", "Hub address", SettingFieldType.Text,
            "URL of this hub as seen from Home Assistant, e.g. http://192.168.1.50:8080. Leave empty to detect it automatically."),
    ];

    public IReadOnlyDictionary<string, object?> GetValues()
    {
        var o = _options.CurrentValue;
        return new Dictionary<string, object?>
        {
            ["cameraMode"] = o.CameraMode.ToString(),
            ["imageInterval"] = (double)o.CameraImageIntervalSeconds,
            ["publicUrl"] = o.PublicUrl,
        };
    }

    public async Task<SettingsApplyResult> ApplyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, object?>();

        if (values.TryGetValue("cameraMode", out var modeText))
        {
            if (!Enum.TryParse<CameraPublishMode>(modeText, ignoreCase: true, out var mode))
                throw new SettingsValidationException("Camera mode must be Image or Link.");

            changes[_configPath + ":CameraMode"] = mode.ToString();
        }

        if (values.TryGetValue("imageInterval", out var intervalText))
        {
            if (!double.TryParse(intervalText, NumberStyles.Float, CultureInfo.InvariantCulture, out double interval)
                || interval < 1 || interval > 3600)
                throw new SettingsValidationException("Image interval must be between 1 and 3600 seconds.");

            changes[_configPath + ":CameraImageIntervalSeconds"] = (int)Math.Round(interval);
        }

        if (values.TryGetValue("publicUrl", out var url))
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length > 0
                && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")))
                throw new SettingsValidationException("Hub address must be empty or a full http(s):// URL.");

            changes[_configPath + ":PublicUrl"] = url;
        }

        if (changes.Count == 0)
            return new SettingsApplyResult(Message: "Nothing to save.");

        await _store.SaveAsync(changes, cancellationToken);
        return new SettingsApplyResult(Message: "Saved. Home Assistant discovery is republished.");
    }
}
