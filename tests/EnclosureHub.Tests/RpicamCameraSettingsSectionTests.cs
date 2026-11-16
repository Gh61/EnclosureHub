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
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions()), store, "Modules:Camera", moduleLoaded: true);

        var result = await section.ApplyAsync(new Dictionary<string, string?>
        {
            ["enabled"] = "false",
            ["resolution"] = "1920x1080",
            ["framerate"] = "0.5",
            ["quality"] = "70",
            ["rotation"] = "180",
            ["extraArgs"] = "  --hflip ",
        }, CancellationToken.None);

        Assert.False(result.RestartRequired);
        Assert.Equal(false, store.Saved["Modules:Camera:Enabled"]);
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
    [InlineData("framerate", "100")]
    [InlineData("quality", "0")]
    [InlineData("rotation", "90")]
    [InlineData("enabled", "maybe")]
    public async Task Apply_rejects_invalid_values_without_saving(string key, string value)
    {
        var store = new FakeStore();
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions()), store, "Modules:Camera", moduleLoaded: true);

        await Assert.ThrowsAsync<SettingsValidationException>(() =>
            section.ApplyAsync(new Dictionary<string, string?> { [key] = value }, CancellationToken.None));

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task Requires_restart_when_module_was_not_loaded()
    {
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions { Enabled = false }), new FakeStore(), "Modules:Camera", moduleLoaded: false);

        var result = await section.ApplyAsync(new Dictionary<string, string?> { ["enabled"] = "true" }, CancellationToken.None);

        Assert.True(result.RestartRequired);
    }

    [Fact]
    public void Custom_resolution_is_offered_as_a_choice()
    {
        var section = new RpicamCameraSettingsSection(new Monitor(new RpicamCameraOptions { Width = 800, Height = 600 }), new FakeStore(), "Modules:Camera", moduleLoaded: true);

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
