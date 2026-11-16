namespace EnclosureHub.Mqtt;

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

    /// <summary>How often camera images are published (only new frames are sent).</summary>
    public int CameraImageIntervalSeconds { get; set; } = 5;

    public int ReconnectDelaySeconds { get; set; } = 10;
}
