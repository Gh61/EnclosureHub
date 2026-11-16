namespace EnclosureHub.Core.Settings;

/// <summary>Outcome of <see cref="ISettingsSection.ApplyAsync"/>.</summary>
public sealed record SettingsApplyResult(bool RestartRequired = false, string? Message = null);

/// <summary>Thrown by <see cref="ISettingsSection.ApplyAsync"/> for invalid input; the message is shown to the user.</summary>
public sealed class SettingsValidationException(string message) : Exception(message);

/// <summary>
/// A group of settings a module exposes for editing at runtime (web dashboard, future displays).
/// The module validates the values, persists them through <see cref="ISettingsStore"/> and applies
/// them live where possible. Values are exchanged as strings ("true"/"false", "85", "1640x1232").
/// </summary>
public interface ISettingsSection
{
    /// <summary>Stable identifier used in the API, e.g. "camera".</summary>
    string Id { get; }

    string Title { get; }

    string? Description { get; }

    /// <summary>Editable fields. May depend on current values (e.g. include a custom value as a choice).</summary>
    IReadOnlyList<SettingField> Fields { get; }

    /// <summary>Current values keyed by <see cref="SettingField.Key"/> (bool, double or string).</summary>
    IReadOnlyDictionary<string, object?> GetValues();

    /// <summary>Validates, persists and applies the given values. Missing keys are left unchanged.</summary>
    Task<SettingsApplyResult> ApplyAsync(IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken);
}
