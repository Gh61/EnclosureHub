namespace EnclosureHub.Mqtt;

/// <summary>How a camera is represented in Home Assistant.</summary>
public enum CameraPublishMode
{
    /// <summary>MQTT camera: the JPEG is pushed to the broker every <see cref="MqttBridgeOptions.CameraImageIntervalSeconds"/>.</summary>
    Image,

    /// <summary>
    /// MQTT image entity with a URL: Home Assistant downloads the snapshot from this hub itself;
    /// stream and snapshot URLs are published as attributes. Suited to fast camera modes.
    /// </summary>
    Link,
}

/// <summary>Bound from "Mqtt".</summary>
public sealed class MqttBridgeOptions
{
    /// <summary>Broker hostname or IP (usually Home Assistant). Empty = bridge disabled.</summary>
    public string Host { get; set; } = "";

    public int Port { get; set; } = 1883;

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>MQTT client id. Empty = Device:Id.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Root topic for this device. Empty = Device:Id.</summary>
    public string BaseTopic { get; set; } = "";

    /// <summary>Home Assistant MQTT discovery prefix.</summary>
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    /// <summary>How often camera images (or snapshot URLs) are published (only new frames are sent).</summary>
    public int CameraImageIntervalSeconds { get; set; } = 5;

    /// <summary>Image (JPEG over MQTT) or Link (URLs; Home Assistant fetches the picture itself).</summary>
    public CameraPublishMode CameraMode { get; set; } = CameraPublishMode.Image;

    /// <summary>
    /// Address of this hub as reachable from Home Assistant, e.g. "http://192.168.1.50:8080".
    /// Empty = detected from the first LAN interface and the configured listening port.
    /// </summary>
    public string PublicUrl { get; set; } = "";

    public int ReconnectDelaySeconds { get; set; } = 10;
}
