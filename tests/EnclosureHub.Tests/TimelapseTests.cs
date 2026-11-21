using System.Text.Json;
using EnclosureHub.Core;
using EnclosureHub.Core.Camera;
using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Settings;
using EnclosureHub.Core.Timelapse;
using EnclosureHub.Timelapse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Tests;

public sealed class TimelapseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "enclosurehub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeFrameSource _frames = new();
    private readonly CameraEntity _camera;

    public TimelapseTests()
    {
        _camera = new CameraEntity("camera", "Camera", _frames);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Jpeg_size_is_read_from_the_frame_header()
    {
        // SOI, APP0 (length 4, 2 payload bytes), SOF0 with height 1232 and width 1640
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x04, 0xD0, 0x06, 0x68, 0x03];

        Assert.True(JpegInfo.TryGetSize(jpeg, out int width, out int height));
        Assert.Equal(1640, width);
        Assert.Equal(1232, height);

        Assert.True(JpegInfo.TryGetSize(CameraPlaceholder.OffJpeg, out width, out height));
        Assert.True(width > 0 && height > 0);

        Assert.False(JpegInfo.TryGetSize([0x89, 0x50, 0x4E, 0x47], out _, out _));
        Assert.False(JpegInfo.TryGetSize([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02], out _, out _));
    }

    [Fact]
    public void Ffmpeg_arguments_use_even_size_of_the_first_frame()
    {
        var options = new TimelapseOptions();
        var args = TimelapseService.BuildArguments("frames", "out.mp4.part", 30, TimelapseOptions.SoftwareEncoder, options, (1641, 1233));
        string line = string.Join(' ', args);

        Assert.Contains("-framerate 30 -start_number 1 -i " + Path.Combine("frames", "%06d.jpg"), line);
        Assert.Contains("scale=1640:1232:force_original_aspect_ratio=decrease:out_range=tv,pad=1640:1232:(ow-iw)/2:(oh-ih)/2,format=yuv420p", line);
        Assert.Contains("-c:v libx264 -preset veryfast -crf 23", line);
        Assert.EndsWith("-pix_fmt yuv420p -movflags +faststart -f mp4 out.mp4.part", line);

        string hardware = string.Join(' ', TimelapseService.BuildArguments("frames", "out", 30, TimelapseOptions.HardwareEncoder, options, null));
        Assert.Contains("-c:v h264_v4l2m2m -b:v 10M", hardware);
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2:out_range=tv,format=yuv420p", hardware);
    }

    [Theory]
    [InlineData("timelapse_2026-11-21_14-30-05", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("../videos/x", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData(".hidden", false)]
    [InlineData("name.mp4", false)]
    public void Names_are_plain_file_names(string name, bool valid)
        => Assert.Equal(valid, TimelapseService.IsValidName(name));

    [Fact]
    public async Task Recording_saves_new_frames_at_the_interval_and_queues_the_render()
    {
        var service = CreateService(out var entities);
        await service.StartRecordingAsync(CancellationToken.None);
        Assert.True(entities.Switch.IsOn);
        var t0 = DateTime.UtcNow;
        _frames.Next(t0);

        await service.TickAsync(t0, CancellationToken.None);                // first frame right away
        await service.TickAsync(t0.AddSeconds(5), CancellationToken.None);  // interval not due
        await service.TickAsync(t0.AddSeconds(31), CancellationToken.None); // due, but the camera has no new frame
        Assert.Equal(1, service.GetStatus().Current!.Frames);

        _frames.Next(t0.AddSeconds(31));
        await service.TickAsync(t0.AddSeconds(31), CancellationToken.None);
        var current = service.GetStatus().Current!;
        Assert.Equal(2, current.Frames);
        Assert.Equal(2 * _frames.LatestFrame!.Jpeg.Length, current.Bytes);

        string sessionDir = Path.Combine(_root, "sessions", current.Name);
        Assert.True(File.Exists(Path.Combine(sessionDir, "000001.jpg")));
        Assert.True(File.Exists(Path.Combine(sessionDir, "000002.jpg")));

        await entities.Switch.SetAsync(false, CancellationToken.None);
        var status = service.GetStatus();
        Assert.False(status.Recording);
        Assert.False(entities.Switch.IsOn);
        var pending = Assert.Single(status.Pending);
        Assert.Equal(TimelapsePendingState.Queued, pending.State);
        Assert.Equal(2, pending.Frames);
        Assert.Equal("queued", ReadState(sessionDir));
    }

    [Fact]
    public async Task Frames_are_skipped_while_the_camera_is_unavailable()
    {
        var service = CreateService(out _);
        await service.StartRecordingAsync(CancellationToken.None);
        var t0 = DateTime.UtcNow.AddSeconds(1);
        _frames.Next(t0);
        _camera.SetAvailable(false);

        await service.TickAsync(t0, CancellationToken.None);
        Assert.Equal(0, service.GetStatus().Current!.Frames);
        Assert.False(service.GetStatus().CameraAvailable);

        _camera.SetAvailable(true);
        await service.TickAsync(t0, CancellationToken.None);
        Assert.Equal(1, service.GetStatus().Current!.Frames);
    }

    [Fact]
    public async Task Stopping_with_too_few_frames_discards_the_recording()
    {
        var service = CreateService(out _);
        _frames.Next(DateTime.UtcNow);

        await service.StartRecordingAsync(CancellationToken.None);
        await service.TickAsync(DateTime.UtcNow, CancellationToken.None);
        string name = service.GetStatus().Current!.Name;
        await service.StopRecordingAsync("test", CancellationToken.None);

        Assert.Empty(service.GetStatus().Pending);
        Assert.False(Directory.Exists(Path.Combine(_root, "sessions", name)));
    }

    [Fact]
    public async Task Recording_stops_after_the_maximum_duration()
    {
        var service = CreateService(out var entities, new TimelapseOptions { MaxDurationHours = 2 });
        await service.StartRecordingAsync(CancellationToken.None);
        var t0 = DateTime.UtcNow;
        _frames.Next(t0);
        await service.TickAsync(t0, CancellationToken.None);
        _frames.Next(t0.AddSeconds(30));
        await service.TickAsync(t0.AddSeconds(30), CancellationToken.None);

        await service.TickAsync(t0.AddHours(2.1), CancellationToken.None);

        Assert.False(entities.Switch.IsOn);
        Assert.Single(service.GetStatus().Pending);
    }

    [Fact]
    public async Task Restart_resumes_the_recording_and_requeues_stopped_ones()
    {
        var first = CreateService(out _);

        // A stopped recording waiting for its render
        await first.StartRecordingAsync(CancellationToken.None);
        var t0 = DateTime.UtcNow;
        _frames.Next(t0);
        await first.TickAsync(t0, CancellationToken.None);
        _frames.Next(t0.AddSeconds(30));
        await first.TickAsync(t0.AddSeconds(30), CancellationToken.None);
        string stopped = first.GetStatus().Current!.Name;
        await first.StopRecordingAsync("test", CancellationToken.None);

        // …and one still recording (names are per second)
        await Task.Delay(1100);
        await first.StartRecordingAsync(CancellationToken.None);
        _frames.Next(t0.AddSeconds(60));
        await first.TickAsync(DateTime.UtcNow.AddSeconds(1), CancellationToken.None);
        string recording = first.GetStatus().Current!.Name;

        var second = CreateService(out var entities);
        second.Restore();
        var status = second.GetStatus();

        Assert.True(entities.Switch.IsOn);
        Assert.Equal(recording, status.Current!.Name);
        Assert.Equal(1, status.Current.Frames);
        var pending = Assert.Single(status.Pending);
        Assert.Equal(stopped, pending.Name);
        Assert.Equal(TimelapsePendingState.Queued, pending.State);
    }

    [Fact]
    public async Task Videos_are_listed_newest_first_and_can_be_deleted()
    {
        var service = CreateService(out _);
        string videos = Path.Combine(_root, "videos");
        Directory.CreateDirectory(videos);
        await File.WriteAllBytesAsync(Path.Combine(videos, "older.mp4"), new byte[4800]);
        await File.WriteAllTextAsync(Path.Combine(videos, "older.json"),
            """{"startedUtc":"2020-11-01T10:00:00Z","stoppedUtc":"2020-11-01T14:00:00Z","frames":480,"fps":30}""");
        await File.WriteAllBytesAsync(Path.Combine(videos, "newer.mp4"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(videos, "newer.jpg"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(videos, "newer.mp4.part"), [1]);

        var list = service.GetVideos();
        Assert.Equal(["newer", "older"], list.Select(v => v.Name));
        Assert.True(list[0].HasThumbnail);
        Assert.Equal(16, list[1].DurationSeconds);
        Assert.Equal(480, list[1].Frames);

        Assert.Null(service.GetVideoFile("../videos/newer", thumbnail: false));
        Assert.NotNull(service.GetVideoFile("newer", thumbnail: true));

        Assert.True(service.DeleteVideo("newer"));
        Assert.False(File.Exists(Path.Combine(videos, "newer.jpg")));
        Assert.False(service.DeleteVideo("newer"));
        Assert.Equal(["older"], service.GetVideos().Select(v => v.Name));
        Assert.Equal(10, service.GetStatus().VideoBytesPerFrame); // 4800 bytes / 480 frames, measured after the delete
    }

    [Fact]
    public async Task Settings_map_fields_to_configuration_keys()
    {
        var store = new FakeStore();
        var section = new TimelapseSettingsSection(new Monitor(new TimelapseOptions()), store, "Modules:Timelapse");
        Assert.Equal(SettingsGroups.Timelapse, ((ISettingsSection)section).Group);
        Assert.Equal(30.0, section.GetValues()["interval"]);
        Assert.Equal(48.0, section.GetValues()["maxHours"]);

        await section.ApplyAsync(new Dictionary<string, string?>
        {
            ["interval"] = "10",
            ["maxHours"] = "12",
            ["videoFps"] = "25",
            ["encoder"] = TimelapseOptions.HardwareEncoder,
            ["turnCameraOn"] = "false",
        }, CancellationToken.None);

        Assert.Equal(10, store.Saved["Modules:Timelapse:IntervalSeconds"]);
        Assert.Equal(12.0, store.Saved["Modules:Timelapse:MaxDurationHours"]);
        Assert.Equal(25, store.Saved["Modules:Timelapse:VideoFps"]);
        Assert.Equal("h264_v4l2m2m", store.Saved["Modules:Timelapse:Encoder"]);
        Assert.Equal(false, store.Saved["Modules:Timelapse:TurnCameraOn"]);
    }

    [Theory]
    [InlineData("interval", "0")]
    [InlineData("interval", "abc")]
    [InlineData("maxHours", "500")]
    [InlineData("videoFps", "0")]
    [InlineData("encoder", "rm -rf")]
    [InlineData("turnCameraOn", "maybe")]
    public async Task Settings_reject_invalid_values_without_saving(string key, string value)
    {
        var store = new FakeStore();
        var section = new TimelapseSettingsSection(new Monitor(new TimelapseOptions()), store, "Modules:Timelapse");

        await Assert.ThrowsAsync<SettingsValidationException>(() =>
            section.ApplyAsync(new Dictionary<string, string?> { [key] = value }, CancellationToken.None));

        Assert.Empty(store.Saved);
    }

    private TimelapseService CreateService(out TimelapseEntities entities, TimelapseOptions? options = null)
    {
        options ??= new TimelapseOptions();
        options.FfmpegExecutable = "enclosurehub-test-no-ffmpeg";
        options.MinFreeMegabytes = 0;
        entities = new TimelapseEntities(options);
        var registry = new EntityRegistry([_camera, entities.Switch, entities.Rendering]);
        return new TimelapseService(new Monitor(options), registry, entities, _root, NullLogger<TimelapseService>.Instance);
    }

    private static string? ReadState(string sessionDir)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(sessionDir, "session.json"))).RootElement.GetProperty("state").GetString();

    private sealed class FakeFrameSource : ICameraFrameSource
    {
        private int _counter;

        public CameraFrame? LatestFrame { get; private set; }

        public void Next(DateTime timestampUtc)
            => LatestFrame = new CameraFrame([0xFF, 0xD8, (byte)++_counter, 0xFF, 0xD9], timestampUtc);

        public Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class Monitor(TimelapseOptions value) : IOptionsMonitor<TimelapseOptions>
    {
        public TimelapseOptions CurrentValue => value;

        public TimelapseOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TimelapseOptions, string?> listener) => null;
    }
}
