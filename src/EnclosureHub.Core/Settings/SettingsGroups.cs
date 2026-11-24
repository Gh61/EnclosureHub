namespace EnclosureHub.Core.Settings;

/// <summary>Tabs of the settings page (<see cref="ISettingsSection.Group"/>).</summary>
public static class SettingsGroups
{
    /// <summary>The camera module. Tabs stay small: further hardware (lights, fans) gets tabs of its own.</summary>
    public const string Camera = "Camera";

    /// <summary>Connection to the 3D printer.</summary>
    public const string Printer = "Printer";

    public const string Timelapse = "Timelapse";

    /// <summary>Home Assistant, the web dashboard and other ways in and out.</summary>
    public const string Integrations = "Integrations";
}
