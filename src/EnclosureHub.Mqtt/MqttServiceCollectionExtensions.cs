using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnclosureHub.Mqtt;

public static class MqttServiceCollectionExtensions
{
    /// <summary>Registers the MQTT bridge (Home Assistant discovery) for all entities.</summary>
    public static IServiceCollection AddMqttBridge(this IServiceCollection services, IConfiguration section)
    {
        services.Configure<MqttBridgeOptions>(section);
        services.AddHostedService<MqttBridgeService>();
        return services;
    }
}
