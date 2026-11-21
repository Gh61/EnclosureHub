using EnclosureHub.Core;
using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Printer;
using EnclosureHub.Core.Settings;
using EnclosureHub.Core.Timelapse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Timelapse;

public static class TimelapseServiceCollectionExtensions
{
    /// <summary>
    /// Registers the timelapse module from the "Modules:Timelapse" section unless <c>Enabled</c> is false:
    /// the "timelapse" switch and "timelapse_rendering" binary sensor, <see cref="ITimelapseService"/>
    /// for the HTTP API and the settings section. Frames come from the camera entity <c>CameraId</c>.
    /// </summary>
    public static IServiceCollection AddTimelapse(this IServiceCollection services, IConfigurationSection section)
    {
        if (!section.GetValue("Enabled", true))
            return services;

        services.Configure<TimelapseOptions>(section);

        services.AddSingleton(sp => new TimelapseEntities(sp.GetRequiredService<IOptionsMonitor<TimelapseOptions>>().CurrentValue));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<TimelapseOptions>>();
            string root = Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, options.CurrentValue.Directory);
            return new TimelapseService(
                options,
                sp.GetRequiredService<IEntityRegistry>(),
                sp.GetRequiredService<TimelapseEntities>(),
                root,
                sp.GetRequiredService<ILogger<TimelapseService>>(),
                sp.GetService<IPrinterConnection>()); // optional: layer frames and following the print
        });
        services.AddHostedService(sp => sp.GetRequiredService<TimelapseService>());
        services.AddSingleton<ITimelapseService>(sp => sp.GetRequiredService<TimelapseService>());

        services.AddSingleton<Entity>(sp => sp.GetRequiredService<TimelapseEntities>().Switch);
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<TimelapseEntities>().Rendering);

        services.AddSingleton<ISettingsSection>(sp => new TimelapseSettingsSection(
            sp.GetRequiredService<IOptionsMonitor<TimelapseOptions>>(),
            sp.GetRequiredService<ISettingsStore>(),
            section.Path,
            sp.GetService<IPrinterConnection>()));

        return services;
    }
}
