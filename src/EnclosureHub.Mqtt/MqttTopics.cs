namespace EnclosureHub.Mqtt;

/// <summary>
/// Topic layout: {base}/status, {base}/{entity}/state, {base}/{entity}/image, {base}/{entity}/url,
/// {base}/{entity}/attributes, {base}/{entity}/set.
/// </summary>
internal sealed class MqttTopics
{
    public MqttTopics(string baseTopic, string discoveryPrefix, string deviceId)
    {
        Base = baseTopic.TrimEnd('/');
        DiscoveryPrefix = discoveryPrefix.TrimEnd('/');
        DeviceId = deviceId;
    }

    public string Base { get; }

    public string DiscoveryPrefix { get; }

    public string DeviceId { get; }

    public string Availability => Base + "/status";

    /// <summary>Home Assistant publishes "online" here after it (re)starts.</summary>
    public string HomeAssistantStatus => DiscoveryPrefix + "/status";

    public string State(string entityId) => $"{Base}/{entityId}/state";

    /// <summary>Camera JPEG bytes (CameraPublishMode.Image).</summary>
    public string Image(string entityId) => $"{Base}/{entityId}/image";

    /// <summary>Snapshot URL for Home Assistant to fetch (CameraPublishMode.Link).</summary>
    public string Url(string entityId) => $"{Base}/{entityId}/url";

    /// <summary>JSON attributes, e.g. stream/snapshot URLs of a camera.</summary>
    public string Attributes(string entityId) => $"{Base}/{entityId}/attributes";

    public string Command(string entityId) => $"{Base}/{entityId}/set";

    public string DiscoveryConfig(string component, string entityId)
        => $"{DiscoveryPrefix}/{component}/{DeviceId}/{entityId}/config";
}
