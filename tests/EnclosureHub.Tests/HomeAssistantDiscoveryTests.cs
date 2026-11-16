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
        Assert.Equal("enclosurehub/camera/url", Topics.Url("camera"));
        Assert.Equal("enclosurehub/camera/attributes", Topics.Attributes("camera"));
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

        Assert.Equal("sensor", HomeAssistantDiscovery.GetComponent(sensor, CameraPublishMode.Image));

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

        Assert.Equal("binary_sensor", HomeAssistantDiscovery.GetComponent(sensor, CameraPublishMode.Image));

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
    public void Camera_in_image_mode_is_an_mqtt_camera_with_image_topic()
    {
        var camera = new CameraEntity("camera", "Camera", new NoFrames()) { Icon = "mdi:cctv" };

        Assert.Equal("camera", HomeAssistantDiscovery.GetComponent(camera, CameraPublishMode.Image));
        Assert.Equal("image", HomeAssistantDiscovery.OtherCameraComponent(CameraPublishMode.Image));

        var json = Serialize(camera, CameraPublishMode.Image);
        Assert.Equal("enclosurehub/camera/image", json.GetProperty("topic").GetString());
        Assert.Equal("enclosurehub/camera/attributes", json.GetProperty("json_attributes_topic").GetString());
        Assert.Equal("mdi:cctv", json.GetProperty("icon").GetString());
        Assert.False(json.TryGetProperty("url_topic", out _));
        Assert.False(json.TryGetProperty("state_topic", out _));

        Assert.Null(HomeAssistantDiscovery.FormatState(camera));
    }

    [Fact]
    public void Camera_in_link_mode_is_an_mqtt_image_with_url_topic()
    {
        var camera = new CameraEntity("camera", "Camera", new NoFrames());

        Assert.Equal("image", HomeAssistantDiscovery.GetComponent(camera, CameraPublishMode.Link));
        Assert.Equal("camera", HomeAssistantDiscovery.OtherCameraComponent(CameraPublishMode.Link));

        var json = Serialize(camera, CameraPublishMode.Link);
        Assert.Equal("enclosurehub/camera/url", json.GetProperty("url_topic").GetString());
        Assert.Equal("enclosurehub/camera/attributes", json.GetProperty("json_attributes_topic").GetString());
        Assert.False(json.TryGetProperty("topic", out _));

        var attributes = HomeAssistantDiscovery.BuildCameraAttributes(camera, "http://192.168.1.50:8080");
        Assert.Equal("http://192.168.1.50:8080/api/cameras/camera/stream.mjpeg", attributes["stream_url"]);
        Assert.Equal("http://192.168.1.50:8080/api/cameras/camera/snapshot.jpg", attributes["snapshot_url"]);
    }

    [Fact]
    public void Sensor_state_is_invariant_culture()
    {
        var sensor = new SensorEntity("t", "T");
        sensor.SetValue(42.5);

        Assert.Equal("42.5", HomeAssistantDiscovery.FormatState(sensor));
    }

    private static JsonElement Serialize(Entity entity, CameraPublishMode mode = CameraPublishMode.Image)
    {
        var config = HomeAssistantDiscovery.BuildConfig(entity, Topics, Device, mode);
        return JsonDocument.Parse(JsonSerializer.Serialize(config)).RootElement;
    }

    private sealed class NoFrames : ICameraFrameSource
    {
        public CameraFrame? LatestFrame => null;

        public Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken)
            => Task.FromCanceled<CameraFrame>(new CancellationToken(true));
    }
}
