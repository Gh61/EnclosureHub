using System.Globalization;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Timelapse;

/// <summary>
/// Runtime-editable timelapse settings (dashboard "Settings → Timelapse"). The interval and the limits
/// apply to a running recording on its next frame; video settings to the next render.
/// </summary>
public sealed class TimelapseSettingsSection : ISettingsSection
{
    private static readonly SettingChoice[] Fps =
    [
        new("10", "10 fps"),
        new("24", "24 fps"),
        new("25", "25 fps"),
        new("30", "30 fps"),
        new("60", "60 fps"),
    ];

    private static readonly SettingChoice[] Encoders =
    [
        new(TimelapseOptions.HardwareEncoder, "Raspberry Pi hardware encoder"),
        new(TimelapseOptions.SoftwareEncoder, "Software (libx264)"),
    ];

    private readonly IOptionsMonitor<TimelapseOptions> _options;
    private readonly ISettingsStore _store;
    private readonly string _configPath;

    /// <param name="configPath">Configuration path of the options, e.g. "Modules:Timelapse".</param>
    public TimelapseSettingsSection(IOptionsMonitor<TimelapseOptions> options, ISettingsStore store, string configPath)
    {
        _options = options;
        _store = store;
        _configPath = configPath;
    }

    public string Id => "timelapse";

    public string Title => "Timelapse";

    public string? Description => "Saves a camera frame at a fixed interval while recording and turns the frames into an MP4 video with ffmpeg "
        + "when the recording stops. Start and stop it on the Timelapse page, from Home Assistant or via the API.";

    public string Group => SettingsGroups.Timelapse;

    public IReadOnlyList<SettingField> Fields
    {
        get
        {
            var o = _options.CurrentValue;
            return
            [
                new("interval", "Snapshot interval", SettingFieldType.Number,
                    "Seconds between two saved frames. 30 s turns a 4-hour print into 16 s of video at 30 fps.",
                    Min: 1, Max: 3600, Step: 1),
                new("maxHours", "Maximum duration", SettingFieldType.Number,
                    "Hours after which a recording stops and is rendered automatically, in case nobody stops it.",
                    Min: 1, Max: 168, Step: 1),
                new("videoFps", "Video frame rate", SettingFieldType.Choice,
                    "Frames per second of the rendered video: higher means a shorter, faster video.",
                    WithCurrent(Fps, Fmt(o.VideoFps))),
                new("encoder", "Video encoder", SettingFieldType.Choice,
                    "The hardware encoder renders about 10× faster on a Pi 2 (a 4-hour print in about a minute) at the same file size; "
                    + "if it fails, the software encoder is used. Software (libx264) is slow on a Pi 2 but works on any device.",
                    Encoders),
                new("turnCameraOn", "Turn the camera on", SettingFieldType.Toggle,
                    "Switches the camera on when a recording starts. Without a running camera no frames are saved."),
            ];
        }
    }

    public IReadOnlyDictionary<string, object?> GetValues()
    {
        var o = _options.CurrentValue;
        return new Dictionary<string, object?>
        {
            ["interval"] = o.IntervalSeconds,
            ["maxHours"] = o.MaxDurationHours,
            ["videoFps"] = Fmt(o.VideoFps),
            ["encoder"] = o.Encoder,
            ["turnCameraOn"] = o.TurnCameraOn,
        };
    }

    public async Task<SettingsApplyResult> ApplyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, object?>();

        if (values.TryGetValue("interval", out var intervalText))
            changes[Key("IntervalSeconds")] = (int)Math.Round(ParseNumber(intervalText, 1, 3600, "Snapshot interval must be between 1 and 3600 seconds."));

        if (values.TryGetValue("maxHours", out var hoursText))
            changes[Key("MaxDurationHours")] = ParseNumber(hoursText, 1, 168, "Maximum duration must be between 1 and 168 hours.");

        if (values.TryGetValue("videoFps", out var fpsText))
            changes[Key("VideoFps")] = (int)Math.Round(ParseNumber(fpsText, 1, 120, "Video frame rate must be between 1 and 120 fps."));

        if (values.TryGetValue("encoder", out var encoder))
        {
            if (encoder != TimelapseOptions.SoftwareEncoder && encoder != TimelapseOptions.HardwareEncoder)
                throw new SettingsValidationException("Unknown video encoder.");

            changes[Key("Encoder")] = encoder;
        }

        if (values.TryGetValue("turnCameraOn", out var turnOnText))
        {
            changes[Key("TurnCameraOn")] = turnOnText?.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "on" => true,
                "false" or "0" or "off" => false,
                _ => throw new SettingsValidationException("Turn the camera on must be on or off."),
            };
        }

        if (changes.Count == 0)
            return new SettingsApplyResult(Message: "Nothing to save.");

        await _store.SaveAsync(changes, cancellationToken);
        return new SettingsApplyResult(Message: "Saved. A running recording uses the new interval from its next frame.");
    }

    private string Key(string name) => _configPath + ":" + name;

    private static double ParseNumber(string? text, double min, double max, string error)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < min || value > max)
            throw new SettingsValidationException(error);
        return value;
    }

    private static string Fmt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<SettingChoice> WithCurrent(SettingChoice[] choices, string current)
        => choices.Any(c => c.Value == current)
            ? choices
            : [.. choices, new SettingChoice(current, current + " fps (custom)")];
}
