using EnclosureHub.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnclosureHub.Camera;

public static class CameraServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Raspberry Pi camera module (rpicam-vid). Adds two entities:
    /// the camera itself and a "{id}_running" diagnostic binary sensor.
    /// </summary>
    public static IServiceCollection AddRpicamCamera(this IServiceCollection services, IConfiguration section)
    {
        services.Configure<RpicamCameraOptions>(section);

        services.AddSingleton<RpicamCameraService>();
        services.AddHostedService(sp => sp.GetRequiredService<RpicamCameraService>());

        services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().Camera);
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<RpicamCameraService>().RunningSensor);

        return services;
    }
}
