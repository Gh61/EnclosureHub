using System.Globalization;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Camera;

/// <summary>
/// Runtime-editable camera settings (dashboard "Settings → Camera"). Values are written to the
/// local configuration via <see cref="ISettingsStore"/>; <see cref="RpicamCameraService"/> picks
/// them up through its options monitor and restarts rpicam-vid.
/// </summary>
public sealed class RpicamCameraSettingsSection : ISettingsSection
{
    private const string CustomPreset = "custom";

    private sealed record Preset(string Value, string Label, int Width, int Height, double Framerate);

    /// <summary>Resolution + frame rate combinations. The IMX219 delivers up to 47 fps at 1080p and 200 fps at 640×480.</summary>
    private static readonly Preset[] Presets =
    [
        new("monitoring", "Monitoring (1 fps)", 1640, 1232, 1),
        new("1080p30", "1080p30", 1920, 1080, 30),
        new("720p60", "720p60", 1280, 720, 60),
        new("480p90", "480p90", 640, 480, 90),
    ];

    private static readonly SettingChoice[] Resolutions =
    [
        new("640x480", "640 × 480"),
        new("1280x720", "1280 × 720"),
        new("1280x960", "1280 × 960"),
        new("1640x1232", "1640 × 1232 (full view)"),
        new("1920x1080", "1920 × 1080 (crop)"),
        new("3280x2464", "3280 × 2464 (slow)"),
    ];

    private static readonly SettingChoice[] Framerates =
    [
        new("0.2", "1 frame every 5 s"),
        new("0.5", "1 frame every 2 s"),
        new("1", "1 frame per second"),
        new("2", "2 frames per second"),
        new("5", "5 frames per second"),
        new("15", "15 frames per second"),
        new("30", "30 frames per second"),
        new("60", "60 frames per second"),
        new("90", "90 frames per second"),
    ];

    private static readonly SettingChoice[] Rotations =
    [
        new("0", "0°"),
        new("180", "180°"),
    ];

    private readonly IOptionsMonitor<RpicamCameraOptions> _options;
    private readonly ISettingsStore _store;
    private readonly string _configPath;

    /// <param name="configPath">Configuration path of the options, e.g. "Modules:Camera".</param>
    public RpicamCameraSettingsSection(
        IOptionsMonitor<RpicamCameraOptions> options,
        ISettingsStore store,
        string configPath)
    {
        _options = options;
        _store = store;
        _configPath = configPath;
    }

    public string Id => "camera";

    public string Title => "Camera";

    public string? Description => "Raspberry Pi camera captured with rpicam-vid. Changes apply within a few seconds.";

    public IReadOnlyList<SettingField> Fields
    {
        get
        {
            var o = _options.CurrentValue;
            return
            [
                new("active", "Camera", SettingFieldType.Toggle,
                    "Turning the camera off stops the capture process; the stream, snapshots and the Home Assistant image pause. "
                    + "The same switch is available in Home Assistant and on the Camera page."),
                new("preset", "Mode", SettingFieldType.Choice,
                    "Monitoring = 1640 × 1232 at 1 fps, full sensor view, low CPU. 1080p30, 720p60 and 480p90 are for the live "
                    + "stream (a Pi 2 manages roughly 35 fps at 720p) – use the Home Assistant link mode with them. Custom: set the fields below.",
                    [.. Presets.Select(p => new SettingChoice(p.Value, p.Label)), new SettingChoice(CustomPreset, "Custom")]),
                new("resolution", "Resolution", SettingFieldType.Choice,
                    "1640 × 1232 shows the whole sensor area; 1920 × 1080 is a crop of the centre.",
                    WithCurrent(Resolutions, Resolution(o))),
                new("framerate", "Frame rate", SettingFieldType.Choice,
                    "Higher rates cost CPU on the Pi and bandwidth for the stream.",
                    WithCurrent(Framerates, Fmt(o.Framerate))),
                new("quality", "JPEG quality", SettingFieldType.Number,
                    "1–100. 85 is a good balance; 70 roughly halves the image size.", Min: 1, Max: 100, Step: 1),
                new("rotation", "Rotation", SettingFieldType.Choice,
                    "Use 180° when the camera is mounted upside down.",
                    WithCurrent(Rotations, Fmt(o.Rotation))),
                new("extraArgs", "Extra arguments", SettingFieldType.Text,
                    "Additional rpicam-vid options, e.g. --hflip --vflip or --awb tungsten."),
            ];
        }
    }

    public IReadOnlyDictionary<string, object?> GetValues()
    {
        var o = _options.CurrentValue;
        return new Dictionary<string, object?>
        {
            ["active"] = o.Active,
            ["preset"] = PresetOf(o),
            ["resolution"] = Resolution(o),
            ["framerate"] = Fmt(o.Framerate),
            ["quality"] = (double)o.Quality,
            ["rotation"] = Fmt(o.Rotation),
            ["extraArgs"] = o.ExtraArgs,
        };
    }

    public async Task<SettingsApplyResult> ApplyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue;
        var changes = new Dictionary<string, object?>();
        bool? active = null;

        if (values.TryGetValue("active", out var activeText))
        {
            active = ParseBool(activeText, "Camera");
            changes[Key("Active")] = active.Value;
        }

        // A newly chosen preset wins over the individual fields; an unchanged preset lets the fields through,
        // so the user can tweak frame rate or resolution without first switching to "Custom".
        bool presetApplied = false;
        if (values.TryGetValue("preset", out var presetValue)
            && presetValue != CustomPreset
            && presetValue != PresetOf(current))
        {
            var preset = Presets.FirstOrDefault(p => p.Value == presetValue)
                ?? throw new SettingsValidationException("Unknown camera mode.");

            changes[Key("Width")] = preset.Width;
            changes[Key("Height")] = preset.Height;
            changes[Key("Framerate")] = preset.Framerate;
            presetApplied = true;
        }

        if (!presetApplied && values.TryGetValue("resolution", out var resolution))
        {
            var parts = (resolution ?? "").Split('x', 'X', '×');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
                || width < 64 || height < 64 || width > 4096 || height > 4096)
                throw new SettingsValidationException("Resolution must be WIDTHxHEIGHT between 64 and 4096 pixels.");

            changes[Key("Width")] = width;
            changes[Key("Height")] = height;
        }

        if (!presetApplied && values.TryGetValue("framerate", out var framerateText))
        {
            if (!double.TryParse(framerateText, NumberStyles.Float, CultureInfo.InvariantCulture, out double framerate)
                || framerate < 0.05 || framerate > 120)
                throw new SettingsValidationException("Frame rate must be between 0.05 and 120 frames per second.");

            changes[Key("Framerate")] = framerate;
        }

        if (values.TryGetValue("quality", out var qualityText))
        {
            if (!double.TryParse(qualityText, NumberStyles.Float, CultureInfo.InvariantCulture, out double quality)
                || quality < 1 || quality > 100)
                throw new SettingsValidationException("JPEG quality must be between 1 and 100.");

            changes[Key("Quality")] = (int)Math.Round(quality);
        }

        if (values.TryGetValue("rotation", out var rotationText))
        {
            if (!int.TryParse(rotationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rotation)
                || (rotation != 0 && rotation != 180))
                throw new SettingsValidationException("Rotation must be 0 or 180.");

            changes[Key("Rotation")] = rotation;
        }

        if (values.TryGetValue("extraArgs", out var extraArgs))
        {
            extraArgs = (extraArgs ?? "").Trim();
            if (extraArgs.Contains('\n') || extraArgs.Contains('\r'))
                throw new SettingsValidationException("Extra arguments must be a single line.");

            changes[Key("ExtraArgs")] = extraArgs;
        }

        if (changes.Count == 0)
            return new SettingsApplyResult(Message: "Nothing to save.");

        await _store.SaveAsync(changes, cancellationToken);

        return new SettingsApplyResult(Message: active == false
            ? "Saved. The camera is stopping."
            : "Saved. The camera restarts with the new settings.");
    }

    private string Key(string name) => _configPath + ":" + name;

    private static string PresetOf(RpicamCameraOptions o)
        => Presets.FirstOrDefault(p => p.Width == o.Width && p.Height == o.Height && Math.Abs(p.Framerate - o.Framerate) < 0.001)?.Value
           ?? CustomPreset;

    private static string Resolution(RpicamCameraOptions o) => Fmt(o.Width) + "x" + Fmt(o.Height);

    private static string Fmt(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool ParseBool(string? text, string label) => text?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "on" => true,
        "false" or "0" or "off" => false,
        _ => throw new SettingsValidationException($"{label} must be on or off."),
    };

    /// <summary>Appends the current (custom) value to the choices when it is not one of the presets.</summary>
    private static IReadOnlyList<SettingChoice> WithCurrent(SettingChoice[] presets, string current)
        => presets.Any(c => c.Value == current)
            ? presets
            : [.. presets, new SettingChoice(current, current + " (custom)")];
}
