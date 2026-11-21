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

    /// <summary>Sub-heading inside a section (label + description, no value); the fields below belong to it.</summary>
    Heading,

    /// <summary>Read-only text from the values (e.g. what the printer reported); never sent back.</summary>
    Info,
}

public sealed record SettingChoice(string Value, string Label);

/// <summary>Describes one editable setting of a <see cref="ISettingsSection"/>; the dashboard renders it generically.</summary>
/// <param name="Tag">Groups related fields across sections, e.g. "printer" for everything the printer connection enables.</param>
public sealed record SettingField(
    string Key,
    string Label,
    SettingFieldType Type,
    string? Description = null,
    IReadOnlyList<SettingChoice>? Choices = null,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string? Tag = null);

/// <summary>Well-known <see cref="SettingField.Tag"/> values.</summary>
public static class SettingTags
{
    /// <summary>Fields that only exist while the printer is linked; the printer section lists them with their states.</summary>
    public const string Printer = "printer";
}
