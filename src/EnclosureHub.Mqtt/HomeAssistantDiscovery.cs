using System.Globalization;
using EnclosureHub.Core;
using EnclosureHub.Core.Entities;

namespace EnclosureHub.Mqtt;

/// <summary>
/// Maps entities to Home Assistant MQTT discovery configs and state payloads.
/// Supported now: camera (as "camera" or "image" depending on <see cref="CameraPublishMode"/>),
/// sensor, binary_sensor. Light and fan: see docs/ROADMAP.md.
/// </summary>
internal static class HomeAssistantDiscovery
{
    public static string? GetComponent(Entity entity, CameraPublishMode cameraMode) => entity.Kind switch
    {
        EntityKind.Camera => CameraComponent(cameraMode),
        EntityKind.Sensor => "sensor",
        EntityKind.BinarySensor => "binary_sensor",
        _ => null,
    };

    public static string CameraComponent(CameraPublishMode mode)
        => mode == CameraPublishMode.Link ? "image" : "camera";

    /// <summary>The component a camera is NOT published as; its retained config must be cleared when switching.</summary>
    public static string OtherCameraComponent(CameraPublishMode mode)
        => mode == CameraPublishMode.Link ? "camera" : "image";

    public static Dictionary<string, object?> BuildConfig(Entity entity, MqttTopics topics, DeviceOptions device, CameraPublishMode cameraMode)
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
                if (cameraMode == CameraPublishMode.Link)
                    config["url_topic"] = topics.Url(entity.Id);
                else
                    config["topic"] = topics.Image(entity.Id);
                config["json_attributes_topic"] = topics.Attributes(entity.Id);
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

    /// <summary>Attributes published for a camera: where to reach the live stream and snapshot over HTTP.</summary>
    public static Dictionary<string, object?> BuildCameraAttributes(CameraEntity camera, string publicUrl) => new()
    {
        ["stream_url"] = StreamUrl(camera, publicUrl),
        ["snapshot_url"] = SnapshotUrl(camera, publicUrl),
        ["dashboard_url"] = publicUrl + "/",
    };

    public static string StreamUrl(CameraEntity camera, string publicUrl) => $"{publicUrl}/api/cameras/{camera.Id}/stream.mjpeg";

    public static string SnapshotUrl(CameraEntity camera, string publicUrl) => $"{publicUrl}/api/cameras/{camera.Id}/snapshot.jpg";

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
