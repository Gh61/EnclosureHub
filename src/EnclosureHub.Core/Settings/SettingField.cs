namespace EnclosureHub.Core.Settings;

public enum SettingFieldType
{
    /// <summary>Boolean, rendered as an ON/OFF switch. Values "true"/"false".</summary>
    Toggle,

    /// <summary>Numeric input, optionally bounded by <see cref="SettingField.Min"/>/<see cref="SettingField.Max"/>.</summary>
    Number,

    /// <summary>Free text.</summary>
    Text,

    /// <summary>One of <see cref="SettingField.Choices"/>.</summary>
    Choice,
}

public sealed record SettingChoice(string Value, string Label);

/// <summary>Describes one editable setting of a <see cref="ISettingsSection"/>; the dashboard renders it generically.</summary>
public sealed record SettingField(
    string Key,
    string Label,
    SettingFieldType Type,
    string? Description = null,
    IReadOnlyList<SettingChoice>? Choices = null,
    double? Min = null,
    double? Max = null,
    double? Step = null);
