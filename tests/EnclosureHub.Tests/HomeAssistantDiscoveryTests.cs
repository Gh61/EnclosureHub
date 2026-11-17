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

        // Device status AND the camera's own availability must both be online
        Assert.False(json.TryGetProperty("availability_topic", out _));
        Assert.Equal("all", json.GetProperty("availability_mode").GetString());
        var availability = json.GetProperty("availability").EnumerateArray().Select(a => a.GetProperty("topic").GetString()).ToList();
        Assert.Equal(["enclosurehub/status", "enclosurehub/camera/available"], availability);

        Assert.Null(HomeAssistantDiscovery.FormatState(camera));
    }

    [Fact]
    public void Camera_availability_changes_raise_state_changed_once()
    {
        var camera = new CameraEntity("camera", "Camera", new NoFrames());
        int raised = 0;
        camera.StateChanged += (_, _) => raised++;

        Assert.True(camera.IsAvailable);
        camera.SetAvailable(false);
        camera.SetAvailable(false);
        camera.SetAvailable(true);

        Assert.Equal(2, raised);
        Assert.True(CameraPlaceholder.OffJpeg.Length > 1000);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, CameraPlaceholder.OffJpeg[..2]);
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
    public void Switch_config_has_command_topic_and_on_off_payloads()
    {
        var sw = new TestSwitch("camera_active", "Camera active");

        Assert.Equal("switch", HomeAssistantDiscovery.GetComponent(sw, CameraPublishMode.Image));

        var json = Serialize(sw);
        Assert.Equal("enclosurehub/camera_active/state", json.GetProperty("state_topic").GetString());
        Assert.Equal("enclosurehub/camera_active/set", json.GetProperty("command_topic").GetString());
        Assert.Equal("ON", json.GetProperty("payload_on").GetString());
        Assert.Equal("OFF", json.GetProperty("payload_off").GetString());

        Assert.Null(HomeAssistantDiscovery.FormatState(sw));
        sw.Set(true);
        Assert.Equal("ON", HomeAssistantDiscovery.FormatState(sw));
    }

    [Theory]
    [InlineData("enclosurehub/camera_active/set", true, "camera_active")]
    [InlineData("enclosurehub/camera_active/state", false, "")]
    [InlineData("enclosurehub/a/b/set", false, "")]
    [InlineData("other/camera_active/set", false, "")]
    [InlineData("enclosurehub//set", false, "")]
    public void Command_topics_are_parsed(string topic, bool expected, string id)
    {
        Assert.Equal(expected, Topics.TryParseCommand(topic, out string entityId));
        Assert.Equal(id, entityId);
        Assert.Equal("enclosurehub/+/set", Topics.CommandFilter);
    }

    [Theory]
    [InlineData("ON", true)]
    [InlineData(" off ", false)]
    [InlineData("1", true)]
    [InlineData("banana", null)]
    public void On_off_payloads_are_parsed(string payload, bool? expected)
        => Assert.Equal(expected, HomeAssistantDiscovery.ParseOnOff(payload));

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

    private sealed class TestSwitch(string id, string name) : SwitchEntity(id, name)
    {
        public override Task SetAsync(bool on, CancellationToken cancellationToken)
        {
            Set(on);
            return Task.CompletedTask;
        }

        public void Set(bool on) => UpdateState(on);
    }

    private sealed class NoFrames : ICameraFrameSource
    {
        public CameraFrame? LatestFrame => null;

        public Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken)
            => Task.FromCanceled<CameraFrame>(new CancellationToken(true));
    }
}
