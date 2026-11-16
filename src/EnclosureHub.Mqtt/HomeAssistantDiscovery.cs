using System.Globalization;
using EnclosureHub.Core;
using EnclosureHub.Core.Entities;

namespace EnclosureHub.Mqtt;

/// <summary>
/// Maps entities to Home Assistant MQTT discovery configs and state payloads.
/// Supported now: camera, sensor, binary_sensor. Light and fan: see docs/ROADMAP.md.
/// </summary>
internal static class HomeAssistantDiscovery
{
    public static string? GetComponent(Entity entity) => entity.Kind switch
    {
        EntityKind.Camera => "camera",
        EntityKind.Sensor => "sensor",
        EntityKind.BinarySensor => "binary_sensor",
        _ => null,
    };

    public static Dictionary<string, object?> BuildConfig(Entity entity, MqttTopics topics, DeviceOptions device)
    {
        var config = new Dictionary<string, object?>
        {
            ["name"] = entity.Name,
            ["unique_id"] = $"{device.Id}_{entity.Id}",
            ["availability_topic"] = topics.Availability,
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { device.Id },
                ["name"] = device.Name,
                ["manufacturer"] = device.Manufacturer,
                ["model"] = device.Model,
            },
        };

        if (entity.Icon != null)
            config["icon"] = entity.Icon;

        if (entity.IsDiagnostic)
            config["entity_category"] = "diagnostic";

        switch (entity)
        {
            case CameraEntity:
                config["topic"] = topics.Image(entity.Id);
                break;

            case SensorEntity sensor:
                config["state_topic"] = topics.State(entity.Id);
                AddIfNotNull(config, "unit_of_measurement", sensor.Unit);
                AddIfNotNull(config, "device_class", sensor.DeviceClass);
                AddIfNotNull(config, "state_class", sensor.StateClass);
                break;

            case BinarySensorEntity binarySensor:
                config["state_topic"] = topics.State(entity.Id);
                AddIfNotNull(config, "device_class", binarySensor.DeviceClass);
                break;
        }

        return config;
    }

    /// <summary>Returns the state payload, or null when there is nothing to publish (unknown state / no state topic).</summary>
    public static string? FormatState(Entity entity) => entity switch
    {
        SensorEntity sensor => sensor.Value?.ToString(CultureInfo.InvariantCulture),
        BinarySensorEntity binarySensor => binarySensor.IsOn switch
        {
            true => "ON",
            false => "OFF",
            null => null,
        },
        _ => null,
    };

    private static void AddIfNotNull(Dictionary<string, object?> config, string key, string? value)
    {
        if (value != null)
            config[key] = value;
    }
}
