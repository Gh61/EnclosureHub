using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Printer;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Camera;

public static class CameraServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Raspberry Pi camera module (rpicam-vid) from the "Modules:Camera" section unless
    /// <c>Enabled</c> is false. Adds two entities: the camera and the "{id}_active" switch (camera on/off,
    /// state = actually running), plus the camera settings section.
    /// </summary>
    public static IServiceCollection AddRpicamCamera(this IServiceCollection services, IConfigurationSection section)
    {
        if (!section.GetValue("Enabled", true))
            return services;

        services.Configure<RpicamCameraOptions>(section);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<RpicamCameraOptions>>().CurrentValue;
            return new CameraActiveSwitch(options.Id + "_active", options.Name + " active",
                sp.GetRequiredService<ISettingsStore>(), section.Path + ":Active")
            {
                Icon = "mdi:cctv",
            };
        });

        services.AddSingleton<RpicamCameraService>();
        services.AddHostedService(sp => sp.GetRequiredService<RpicamCameraService>());

        services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().Camera);
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().ActiveSwitch);

        services.AddSingleton<ISettingsSection>(sp => new RpicamCameraSettingsSection(
            sp.GetRequiredService<IOptionsMonitor<RpicamCameraOptions>>(),
            sp.GetRequiredService<ISettingsStore>(),
            section.Path,
            sp.GetService<IPrinterConnection>()));

        return services;
    }
}
