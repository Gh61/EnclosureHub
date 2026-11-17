using EnclosureHub.Camera;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Tests;

public class RpicamCameraSettingsSectionTests
{
    [Fact]
    public async Task Apply_maps_fields_to_configuration_keys()
    {
        var store = new FakeStore();
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions()), store, "Modules:Camera");

        await section.ApplyAsync(new Dictionary<string, string?>
        {
            ["active"] = "false",
            ["resolution"] = "1920x1080",
            ["framerate"] = "0.5",
            ["quality"] = "70",
            ["rotation"] = "180",
            ["extraArgs"] = "  --hflip ",
        }, CancellationToken.None);

        Assert.Equal(false, store.Saved["Modules:Camera:Active"]);
        Assert.False(store.Saved.ContainsKey("Modules:Camera:Enabled"));
        Assert.Equal(1920, store.Saved["Modules:Camera:Width"]);
        Assert.Equal(1080, store.Saved["Modules:Camera:Height"]);
        Assert.Equal(0.5, store.Saved["Modules:Camera:Framerate"]);
        Assert.Equal(70, store.Saved["Modules:Camera:Quality"]);
        Assert.Equal(180, store.Saved["Modules:Camera:Rotation"]);
        Assert.Equal("--hflip", store.Saved["Modules:Camera:ExtraArgs"]);
    }

    [Theory]
    [InlineData("resolution", "abc")]
    [InlineData("resolution", "10x10")]
    [InlineData("framerate", "500")]
    [InlineData("quality", "0")]
    [InlineData("rotation", "90")]
    [InlineData("active", "maybe")]
    public async Task Apply_rejects_invalid_values_without_saving(string key, string value)
    {
        var store = new FakeStore();
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions()), store, "Modules:Camera");

        await Assert.ThrowsAsync<SettingsValidationException>(() =>
            section.ApplyAsync(new Dictionary<string, string?> { [key] = value }, CancellationToken.None));

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task New_preset_overrides_resolution_and_framerate()
    {
        var store = new FakeStore();
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions()), store, "Modules:Camera");
        Assert.Equal("monitoring", section.GetValues()["preset"]);

        // The UI sends the (stale) individual fields along with the newly chosen preset
        await section.ApplyAsync(new Dictionary<string, string?>
        {
            ["preset"] = "720p60",
            ["resolution"] = "1640x1232",
            ["framerate"] = "1",
        }, CancellationToken.None);

        Assert.Equal(1280, store.Saved["Modules:Camera:Width"]);
        Assert.Equal(720, store.Saved["Modules:Camera:Height"]);
        Assert.Equal(60.0, store.Saved["Modules:Camera:Framerate"]);
    }

    [Fact]
    public async Task Unchanged_preset_lets_individual_fields_through()
    {
        var store = new FakeStore();
        var options = new RpicamCameraOptions { Width = 1920, Height = 1080, Framerate = 30 };
        var section = new RpicamCameraSettingsSection(new Monitor(options), store, "Modules:Camera");
        Assert.Equal("1080p30", section.GetValues()["preset"]);

        await section.ApplyAsync(new Dictionary<string, string?>
        {
            ["preset"] = "1080p30",
            ["resolution"] = "1920x1080",
            ["framerate"] = "15",
        }, CancellationToken.None);

        Assert.Equal(15.0, store.Saved["Modules:Camera:Framerate"]);
        Assert.Equal("custom", new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions { Width = 1920, Height = 1080, Framerate = 15 }), store, "Modules:Camera").GetValues()["preset"]);
    }

    [Fact]
    public void Custom_resolution_is_offered_as_a_choice()
    {
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions { Width = 800, Height = 600 }), new FakeStore(), "Modules:Camera");

        var resolution = section.Fields.Single(f => f.Key == "resolution");
        Assert.Contains(resolution.Choices!, c => c.Value == "800x600");
        Assert.Equal("800x600", section.GetValues()["resolution"]);
    }

    [Fact]
    public void Build_arguments_includes_rotation_and_extra_args_once()
    {
        var options = new RpicamCameraOptions { Rotation = 180, ExtraArgs = " --hflip " };
        string args = RpicamCameraService.BuildArguments(options);

        Assert.Equal("-t 0 -n --codec mjpeg --width 1640 --height 1232 --framerate 1 -q 85 --rotation 180 --hflip -o -", args);
        Assert.DoesNotContain("--rotation", RpicamCameraService.BuildArguments(new RpicamCameraOptions()));
    }

    [Fact]
    public async Task Active_switch_writes_the_active_flag()
    {
        var store = new FakeStore();
        var sw = new CameraActiveSwitch("camera_active", "Camera active", store, "Modules:Camera:Active");

        Assert.Null(sw.IsOn);
        await sw.SetAsync(false, CancellationToken.None);

        Assert.Equal(false, store.Saved["Modules:Camera:Active"]);
        Assert.Null(sw.IsOn); // state is reported by the service once the change is in effect
    }

    private sealed class FakeStore : ISettingsStore
    {
        public Dictionary<string, object?> Saved { get; } = new();

        public Task SaveAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
        {
            foreach (var (key, value) in values)
                Saved[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class Monitor(RpicamCameraOptions value) : IOptionsMonitor<RpicamCameraOptions>
    {
        public RpicamCameraOptions CurrentValue => value;

        public RpicamCameraOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<RpicamCameraOptions, string?> listener) => null;
    }
}
