using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Mqtt;

public static class MqttServiceCollectionExtensions
{
    /// <summary>Registers the MQTT bridge (Home Assistant discovery) for all entities and its settings section.</summary>
    public static IServiceCollection AddMqttBridge(this IServiceCollection services, IConfigurationSection section)
    {
        services.Configure<MqttBridgeOptions>(section);
        services.AddHostedService<MqttBridgeService>();
        services.AddSingleton<ISettingsSection>(sp => new MqttSettingsSection(
            sp.GetRequiredService<IOptionsMonitor<MqttBridgeOptions>>(),
            sp.GetRequiredService<ISettingsStore>(),
            section.Path));
        return services;
    }
}
