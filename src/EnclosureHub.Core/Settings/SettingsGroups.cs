namespace EnclosureHub.Core.Settings;

/// <summary>Tabs of the settings page (<see cref="ISettingsSection.Group"/>).</summary>
public static class SettingsGroups
{
    /// <summary>Hardware modules: camera, later lights and fans.</summary>
    public const string Devices = "Devices";

    public const string Timelapse = "Timelapse";

    /// <summary>Home Assistant, the web dashboard and other ways in and out.</summary>
    public const string Integrations = "Integrations";
}
