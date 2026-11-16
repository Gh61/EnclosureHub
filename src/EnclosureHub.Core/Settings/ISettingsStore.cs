namespace EnclosureHub.Core.Settings;

/// <summary>
/// Persists configuration values so they survive a restart and reloads the configuration afterwards,
/// so option monitors see the change. Keys use configuration paths, e.g. "Modules:Camera:Quality".
/// </summary>
public interface ISettingsStore
{
    Task SaveAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken);
}
