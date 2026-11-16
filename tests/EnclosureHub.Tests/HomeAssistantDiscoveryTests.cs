using System.Text.Json;
using EnclosureHub.Core;
using EnclosureHub.Core.Camera;
using EnclosureHub.Core.Entities;
using EnclosureHub.Mqtt;

namespace EnclosureHub.Tests;

public class HomeAssistantDiscoveryTests
{
    private static readonly DeviceOptions Device = new() { Id = "enclosurehub", Name = "Printer Enclosure" };
    private static readonly MqttTopics Topics = new("enclosurehub", "homeassistant", "enclosurehub");

    [Fact]
    public void Topic_layout_is_stable()
    {
        Assert.Equal("enclosurehub/status", Topics.Availability);
        Assert.Equal("homeassistant/status", Topics.HomeAssistantStatus);
        Assert.Equal("enclosurehub/cpu_temperature/state", Topics.State("cpu_temperature"));
        Assert.Equal("enclosurehub/camera/image", Topics.Image("camera"));
        Assert.Equal("enclosurehub/light/set", Topics.Command("light"));
        Assert.Equal("homeassistant/sensor/enclosurehub/cpu_temperature/config", Topics.DiscoveryConfig("sensor", "cpu_temperature"));
    }

    [Fact]
    public void Sensor_config_contains_state_topic_and_classes()
    {
        var sensor = new SensorEntity("cpu_temperature", "CPU temperature")
        {
            Unit = "°C",
            DeviceClass = "temperature",
            StateClass = "measurement",
            IsDiagnostic = true,
        };

        Assert.Equal("sensor", HomeAssistantDiscovery.GetComponent(sensor));

        var json = Serialize(sensor);
        Assert.Equal("CPU temperature", json.GetProperty("name").GetString());
        Assert.Equal("enclosurehub_cpu_temperature", json.GetProperty("unique_id").GetString());
        Assert.Equal("enclosurehub/status", json.GetProperty("availability_topic").GetString());
        Assert.Equal("enclosurehub/cpu_temperature/state", json.GetProperty("state_topic").GetString());
        Assert.Equal("°C", json.GetProperty("unit_of_measurement").GetString());
        Assert.Equal("temperature", json.GetProperty("device_class").GetString());
        Assert.Equal("measurement", json.GetProperty("state_class").GetString());
        Assert.Equal("diagnostic", json.GetProperty("entity_category").GetString());
        Assert.Equal("enclosurehub", json.GetProperty("device").GetProperty("identifiers")[0].GetString());
        Assert.Equal("Printer Enclosure", json.GetProperty("device").GetProperty("name").GetString());
    }

    [Fact]
    public void Binary_sensor_config_and_state()
    {
        var sensor = new BinarySensorEntity("camera_running", "Camera running") { DeviceClass = "running" };

        Assert.Equal("binary_sensor", HomeAssistantDiscovery.GetComponent(sensor));

        var json = Serialize(sensor);
        Assert.Equal("enclosurehub/camera_running/state", json.GetProperty("state_topic").GetString());
        Assert.Equal("running", json.GetProperty("device_class").GetString());
        Assert.False(json.TryGetProperty("entity_category", out _));

        Assert.Null(HomeAssistantDiscovery.FormatState(sensor));
        sensor.SetState(true);
        Assert.Equal("ON", HomeAssistantDiscovery.FormatState(sensor));
        sensor.SetState(false);
        Assert.Equal("OFF", HomeAssistantDiscovery.FormatState(sensor));
    }

    [Fact]
    public void Camera_config_uses_image_topic_and_has_no_state()
    {
        var camera = new CameraEntity("camera", "Camera", new NoFrames()) { Icon = "mdi:cctv" };

        Assert.Equal("camera", HomeAssistantDiscovery.GetComponent(camera));

        var json = Serialize(camera);
        Assert.Equal("enclosurehub/camera/image", json.GetProperty("topic").GetString());
        Assert.Equal("mdi:cctv", json.GetProperty("icon").GetString());
        Assert.False(json.TryGetProperty("state_topic", out _));

        Assert.Null(HomeAssistantDiscovery.FormatState(camera));
    }

    [Fact]
    public void Sensor_state_is_invariant_culture()
    {
        var sensor = new SensorEntity("t", "T");
        sensor.SetValue(42.5);

        Assert.Equal("42.5", HomeAssistantDiscovery.FormatState(sensor));
    }

    private static JsonElement Serialize(Entity entity)
    {
        var config = HomeAssistantDiscovery.BuildConfig(entity, Topics, Device);
        return JsonDocument.Parse(JsonSerializer.Serialize(config)).RootElement;
    }

    private sealed class NoFrames : ICameraFrameSource
    {
        public CameraFrame? LatestFrame => null;

        public Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken)
            => Task.FromCanceled<CameraFrame>(new CancellationToken(true));
    }
}
