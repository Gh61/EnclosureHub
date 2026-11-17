using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Settings;

namespace EnclosureHub.Camera;

/// <summary>
/// "{camera}_active" switch: turns the capture process on and off from Home Assistant, the dashboard
/// or the API. Writes <c>Modules:Camera:Active</c>; <see cref="RpicamCameraService"/> reacts through
/// its options monitor and reports the effective state back via <see cref="Reflect"/>.
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

    public override Task SetAsync(bool on, CancellationToken cancellationToken)
        => _store.SaveAsync(new Dictionary<string, object?> { [_configKey] = on }, cancellationToken);

    internal void Reflect(bool active) => UpdateState(active);
}
