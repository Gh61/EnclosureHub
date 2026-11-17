using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Settings;

namespace EnclosureHub.Camera;

/// <summary>
/// "{camera}_active" switch – the single on/off entity of a camera. Its state is the real state:
/// ON while the camera is switched on and producing frames (with a short grace period after start),
/// OFF when switched off or when no frames arrive. Turning it on persists <c>Modules:Camera:Active</c>;
/// turning it on while it is already on but not running restarts the capture process.
/// </summary>
public sealed class CameraActiveSwitch : SwitchEntity
{
    private readonly ISettingsStore _store;
    private readonly string _configKey;

    public CameraActiveSwitch(string id, string name, ISettingsStore store, string configKey)
        : base(id, name)
    {
        _store = store;
        _configKey = configKey;
    }

    /// <summary>Set by the camera service: whether the configuration currently says "on".</summary>
    internal Func<bool>? IsConfiguredOn { get; set; }

    /// <summary>Set by the camera service: restarts the capture process.</summary>
    internal Action? RestartCapture { get; set; }

    public override async Task SetAsync(bool on, CancellationToken cancellationToken)
    {
        bool wasConfiguredOn = IsConfiguredOn?.Invoke() ?? false;

        await _store.SaveAsync(new Dictionary<string, object?> { [_configKey] = on }, cancellationToken);

        // Already on but not running (ON requested again): kick the capture process
        if (on && wasConfiguredOn && IsOn != true)
            RestartCapture?.Invoke();
    }

    internal void Reflect(bool running) => UpdateState(running);
}
