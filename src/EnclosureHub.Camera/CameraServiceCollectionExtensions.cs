using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Camera;

public static class CameraServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Raspberry Pi camera module (rpicam-vid) from the "Modules:Camera" section.
    /// The settings section is always registered (so the camera can be turned on from the dashboard);
    /// the service and its two entities (camera + "{id}_running" diagnostic binary sensor) only when
    /// <c>Enabled</c> is true at startup.
    /// </summary>
    public static IServiceCollection AddRpicamCamera(this IServiceCollection services, IConfigurationSection section)
    {
        services.Configure<RpicamCameraOptions>(section);

        bool enabled = section.GetValue("Enabled", true);

        services.AddSingleton<ISettingsSection>(sp => new RpicamCameraSettingsSection(
            sp.GetRequiredService<IOptionsMonitor<RpicamCameraOptions>>(),
            sp.GetRequiredService<ISettingsStore>(),
            section.Path,
            moduleLoaded: enabled));

        if (!enabled)
            return services;

        services.AddSingleton<RpicamCameraService>();
        services.AddHostedService(sp => sp.GetRequiredService<RpicamCameraService>());

        services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().Camera);
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().RunningSensor);

        return services;
    }
}
