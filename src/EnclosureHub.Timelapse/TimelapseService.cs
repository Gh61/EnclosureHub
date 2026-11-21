using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using EnclosureHub.Core;
using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Timelapse;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Timelapse;

/// <summary>
/// Records timelapses from a camera entity: while recording, the latest camera frame is saved every
/// <see cref="TimelapseOptions.IntervalSeconds"/> into a session directory; stopping queues the session
/// for rendering with ffmpeg (one at a time, idle priority) into <c>videos/{name}.mp4</c> plus a thumbnail.
/// Everything lives on disk, so a restart resumes the recording and re-renders queued sessions.
/// <code>
/// {Directory}/sessions/{name}/session.json, 000001.jpg, 000002.jpg, …
/// {Directory}/videos/{name}.mp4, {name}.jpg (thumbnail), {name}.json (frames, fps, start, stop)
/// </code>
/// </summary>
public sealed partial class TimelapseService : BackgroundService, ITimelapseService
{
    private const string SessionFile = "session.json";
    private const string StateRecording = "recording";
    private const string StateQueued = "queued";
    private const string StateFailed = "failed";
    private const int MinFrames = 2;

    /// <summary>MP4 size relative to the JPEG frames until real videos exist (the dashboard uses the same figure).</summary>
    private const double AssumedVideoRatio = 0.15;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IOptionsMonitor<TimelapseOptions> _options;
    private readonly IEntityRegistry _registry;
    private readonly TimelapseEntities _entities;
    private readonly ILogger<TimelapseService> _logger;
    private readonly Ffmpeg _ffmpeg;
    private readonly string _rootDir;
    private readonly string _sessionsDir;
    private readonly string _videosDir;

    // Serializes start, stop and frame writes (a frame must never land in a session that is already being rendered)
    private readonly SemaphoreSlim _ops = new(1, 1);

    // Guards the in-memory state below; held only briefly
    private readonly object _gate = new();
    private readonly List<Pending> _pending = [];
    private readonly Channel<string> _renderQueue = Channel.CreateUnbounded<string>();
    private Recording? _current;
    private long? _videoBytesPerFrame;

    public TimelapseService(
        IOptionsMonitor<TimelapseOptions> options,
        IEntityRegistry registry,
        TimelapseEntities entities,
        string rootDirectory,
        ILogger<TimelapseService> logger)
    {
        _options = options;
        _registry = registry;
        _entities = entities;
        _logger = logger;
        _rootDir = rootDirectory;
        _sessionsDir = Path.Combine(rootDirectory, "sessions");
        _videosDir = Path.Combine(rootDirectory, "videos");

        _ffmpeg = new Ffmpeg(options.CurrentValue.FfmpegExecutable, logger);
        _ffmpeg.Installed += RetryFailedRenders;

        _entities.Switch.Handler = SetRecordingAsync;
    }

    public string SwitchId => _entities.Switch.Id;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // do not hold up the host start (directory scan, ffmpeg -version)

        try
        {
            Directory.CreateDirectory(_sessionsDir);
            Directory.CreateDirectory(_videosDir);
            _ffmpeg.Detect();
            Restore();
            UpdateVideoStats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Timelapse directory {Dir} is not usable; timelapse is inactive", _rootDir);
            return;
        }

        var renderer = RenderLoopAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Saving a timelapse frame failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down; the recording continues after the restart
        }

        await renderer;
    }

    // ------------------------------------------------------------------ recording

    private Task SetRecordingAsync(bool on, CancellationToken cancellationToken)
        => on ? StartRecordingAsync(cancellationToken) : StopRecordingAsync("switched off", cancellationToken);

    internal async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        await _ops.WaitAsync(cancellationToken);
        try
        {
            if (_current == null)
            {
                var now = DateTime.UtcNow;
                string name = NewName(DateTime.Now);
                string dir = Path.Combine(_sessionsDir, name);
                Directory.CreateDirectory(dir);
                WriteJson(Path.Combine(dir, SessionFile), new SessionInfo { StartedUtc = now, State = StateRecording });

                lock (_gate)
                    _current = new Recording(name, dir, now) { NextCaptureUtc = now };

                _logger.LogInformation("Timelapse {Name} started (a frame every {Interval} s)", name, options.IntervalSeconds);
            }

            _entities.Switch.Reflect(true);
        }
        finally
        {
            _ops.Release();
        }

        // Frames need a running camera; its switch persists the change like any other "ON"
        if (options.TurnCameraOn && _registry.Find(options.CameraId + "_active") is SwitchEntity { IsOn: not true } cameraSwitch)
        {
            _logger.LogInformation("Turning the camera on for the timelapse");
            await cameraSwitch.SetAsync(true, cancellationToken);
        }
    }

    internal async Task StopRecordingAsync(string reason, CancellationToken cancellationToken)
    {
        await _ops.WaitAsync(cancellationToken);
        try
        {
            Recording? recording;
            lock (_gate)
            {
                recording = _current;
                _current = null;
            }

            _entities.Switch.Reflect(false);
            if (recording == null)
                return;

            if (recording.Frames < MinFrames)
            {
                _logger.LogInformation("Timelapse {Name} stopped ({Reason}) with {Frames} frame(s); nothing to render, discarded",
                    recording.Name, reason, recording.Frames);
                TryDeleteDirectory(recording.Dir);
                return;
            }

            var stopped = DateTime.UtcNow;
            WriteJson(Path.Combine(recording.Dir, SessionFile),
                new SessionInfo { StartedUtc = recording.StartedUtc, StoppedUtc = stopped, State = StateQueued });

            _logger.LogInformation("Timelapse {Name} stopped ({Reason}) after {Frames} frames, queued for rendering",
                recording.Name, reason, recording.Frames);
            Enqueue(new Pending(recording.Name, recording.Dir, recording.StartedUtc, stopped)
            {
                Frames = recording.Frames,
                Bytes = recording.Bytes,
            });
        }
        finally
        {
            _ops.Release();
        }
    }

    /// <summary>One step of the capture loop (every second): limits, then a frame when the interval is due.</summary>
    internal async Task TickAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        Recording? recording;
        lock (_gate)
            recording = _current;

        if (recording == null)
            return;

        if (nowUtc - recording.StartedUtc >= TimeSpan.FromHours(Math.Max(0.01, options.MaxDurationHours)))
        {
            await StopRecordingAsync($"maximum duration of {options.MaxDurationHours} h reached", cancellationToken);
            return;
        }

        if (nowUtc < recording.NextCaptureUtc)
            return;

        // Only new frames from a working camera; otherwise try again on the next tick
        var camera = _registry.Find(options.CameraId) as CameraEntity;
        var frame = camera?.Source.LatestFrame;
        if (camera == null || !camera.IsAvailable || frame == null || frame.TimestampUtc <= recording.LastSourceFrameUtc)
            return;

        // Keep room for this frame and for the video the recording turns into (frames and video coexist while rendering)
        long? free = FreeBytes();
        long reserve = (long)options.MinFreeMegabytes * 1024 * 1024
            + frame.Jpeg.Length
            + (recording.Frames + 1) * VideoBytesPerFrame(frame.Jpeg.Length);
        if (free < reserve)
        {
            _logger.LogWarning("Only {Free} MB of disk space left, {Reserve} MB needed to keep recording and render the video",
                free / (1024 * 1024), reserve / (1024 * 1024));
            await StopRecordingAsync("disk almost full", cancellationToken);
            return;
        }

        await _ops.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(_current, recording))
                return; // stopped in the meantime

            await File.WriteAllBytesAsync(FramePath(recording.Dir, recording.Frames + 1), frame.Jpeg, cancellationToken);

            var interval = TimeSpan.FromSeconds(Math.Max(1, options.IntervalSeconds));
            lock (_gate)
            {
                recording.Frames++;
                recording.Bytes += frame.Jpeg.Length;
                recording.LastFrameUtc = nowUtc;
                recording.LastSourceFrameUtc = frame.TimestampUtc;

                // Keep the cadence; after a gap (camera off) continue from now
                var next = recording.NextCaptureUtc + interval;
                recording.NextCaptureUtc = next > nowUtc ? next : nowUtc + interval;
            }
        }
        finally
        {
            _ops.Release();
        }
    }

    /// <summary>Picks up what was on disk before a restart: resumes a recording, re-queues stopped sessions.</summary>
    internal void Restore()
    {
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;

        foreach (string dir in Directory.EnumerateDirectories(_sessionsDir).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            var info = ReadJson<SessionInfo>(Path.Combine(dir, SessionFile));
            if (info == null || !IsValidName(name))
            {
                _logger.LogWarning("Ignoring {Dir}: not a timelapse session", dir);
                continue;
            }

            var (frames, bytes, lastWrite) = CountFrames(dir);

            if (info.State == StateRecording && _current == null
                && now - info.StartedUtc < TimeSpan.FromHours(Math.Max(0.01, options.MaxDurationHours)))
            {
                lock (_gate)
                {
                    _current = new Recording(name, dir, info.StartedUtc)
                    {
                        Frames = frames,
                        Bytes = bytes,
                        LastFrameUtc = lastWrite,
                        NextCaptureUtc = now,
                    };
                }

                _logger.LogInformation("Resuming timelapse {Name} ({Frames} frames so far)", name, frames);
                continue;
            }

            if (frames < MinFrames)
            {
                _logger.LogInformation("Discarding timelapse {Name}: {Frames} frame(s)", name, frames);
                TryDeleteDirectory(dir);
                continue;
            }

            var pending = new Pending(name, dir, info.StartedUtc, info.StoppedUtc ?? lastWrite ?? now)
            {
                Frames = frames,
                Bytes = bytes,
            };

            if (info.State == StateFailed)
            {
                pending.State = TimelapsePendingState.Failed;
                pending.Error = info.Error;
                lock (_gate)
                    _pending.Add(pending);
                continue;
            }

            if (info.State != StateQueued)
                WriteJson(Path.Combine(dir, SessionFile), new SessionInfo { StartedUtc = info.StartedUtc, StoppedUtc = pending.StoppedUtc, State = StateQueued });

            _logger.LogInformation("Timelapse {Name} is queued for rendering", name);
            Enqueue(pending);
        }

        _entities.Switch.Reflect(_current != null);
        _entities.Rendering.SetState(false);
    }

    // ------------------------------------------------------------------ rendering

    private void Enqueue(Pending pending)
    {
        lock (_gate)
        {
            pending.State = TimelapsePendingState.Queued;
            pending.Progress = null;
            pending.Error = null;
            if (!_pending.Contains(pending))
                _pending.Add(pending);
        }

        _renderQueue.Writer.TryWrite(pending.Name);
    }

    private async Task RenderLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (string name in _renderQueue.Reader.ReadAllAsync(stoppingToken))
            {
                Pending? pending;
                lock (_gate)
                {
                    pending = _pending.FirstOrDefault(p => p.Name == name && p.State == TimelapsePendingState.Queued);
                    if (pending != null)
                    {
                        pending.State = TimelapsePendingState.Rendering;
                        pending.Progress = 0;
                    }
                }

                if (pending == null)
                    continue; // discarded or already rendered

                _entities.Rendering.SetState(true);
                try
                {
                    await RenderAsync(pending, stoppingToken);
                    lock (_gate)
                        _pending.Remove(pending);
                    UpdateVideoStats();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // stays "queued" on disk and is rendered after the restart
                }
                catch (Exception ex)
                {
                    _logger.LogError("Rendering timelapse {Name} failed: {Message}", name, ex.Message);
                    lock (_gate)
                    {
                        pending.State = TimelapsePendingState.Failed;
                        pending.Progress = null;
                        pending.Error = ex.Message;
                    }

                    TryWriteJson(Path.Combine(pending.Dir, SessionFile), new SessionInfo
                    {
                        StartedUtc = pending.StartedUtc,
                        StoppedUtc = pending.StoppedUtc,
                        State = StateFailed,
                        Error = ex.Message,
                    });
                }
                finally
                {
                    if (_renderQueue.Reader.Count == 0)
                        _entities.Rendering.SetState(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task RenderAsync(Pending pending, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var frames = Directory.GetFiles(pending.Dir, "*.jpg").Order(StringComparer.Ordinal).ToArray();
        if (frames.Length < MinFrames)
            throw new InvalidOperationException($"Only {frames.Length} frame(s) found.");

        int fps = Math.Clamp(options.VideoFps, 1, 120);
        var size = ReadFrameSize(frames[0]);
        string output = Path.Combine(_videosDir, pending.Name + ".mp4.part");
        var started = DateTime.UtcNow;

        _logger.LogInformation("Rendering timelapse {Name}: {Frames} frames at {Fps} fps with {Encoder}",
            pending.Name, frames.Length, fps, options.Encoder);

        void Report(double progress)
        {
            lock (_gate)
                pending.Progress = progress;
        }

        try
        {
            var result = await _ffmpeg.RenderAsync(
                BuildArguments(pending.Dir, output, fps, options.Encoder, options, size), frames.Length, Report, cancellationToken);

            if (result.ExitCode != 0 && options.Encoder != TimelapseOptions.SoftwareEncoder)
            {
                _logger.LogWarning("Encoder {Encoder} failed ({Output}); falling back to {Software}",
                    options.Encoder, result.Output, TimelapseOptions.SoftwareEncoder);
                Report(0);
                result = await _ffmpeg.RenderAsync(
                    BuildArguments(pending.Dir, output, fps, TimelapseOptions.SoftwareEncoder, options, size), frames.Length, Report, cancellationToken);
            }

            if (result.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg exited with code {result.ExitCode}: {result.Output}");

            string video = VideoPath(pending.Name, thumbnail: false);
            File.Move(output, video, overwrite: true);
            File.Copy(frames[^1], VideoPath(pending.Name, thumbnail: true), overwrite: true);
            WriteJson(Path.ChangeExtension(video, ".json"), new VideoInfo
            {
                StartedUtc = pending.StartedUtc,
                StoppedUtc = pending.StoppedUtc,
                Frames = frames.Length,
                Fps = fps,
            });

            TryDeleteDirectory(pending.Dir);
            _logger.LogInformation("Timelapse {Name} rendered in {Seconds:0} s: {Size} MB",
                pending.Name, (DateTime.UtcNow - started).TotalSeconds, new FileInfo(video).Length / (1024 * 1024));
        }
        catch
        {
            TryDeleteFile(output);
            throw;
        }
    }

    /// <summary>ffmpeg command line: numbered JPEGs → H.264 MP4 (yuv420p, faststart), every frame scaled/padded to the first frame's size.</summary>
    internal static List<string> BuildArguments(string framesDir, string output, int fps, string encoder, TimelapseOptions options, (int Width, int Height)? size)
    {
        // H.264 with 4:2:0 needs even dimensions; a fixed size also survives a camera resolution change mid-recording.
        // JPEGs are full range; out_range=tv gives standard yuv420p that every player handles (not yuvj420p).
        string filter = (size is { } s
            ? $"scale={Even(s.Width)}:{Even(s.Height)}:force_original_aspect_ratio=decrease:out_range=tv,pad={Even(s.Width)}:{Even(s.Height)}:(ow-iw)/2:(oh-ih)/2"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2:out_range=tv") + ",format=yuv420p";

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y",
            "-framerate", fps.ToString(CultureInfo.InvariantCulture),
            "-start_number", "1",
            "-i", Path.Combine(framesDir, "%06d.jpg"),
            "-vf", filter,
        };

        if (encoder == TimelapseOptions.HardwareEncoder)
            args.AddRange(["-c:v", TimelapseOptions.HardwareEncoder, "-b:v", options.HardwareBitrate]);
        else
            args.AddRange(["-c:v", TimelapseOptions.SoftwareEncoder, "-preset", "veryfast", "-crf", Math.Clamp(options.Crf, 0, 51).ToString(CultureInfo.InvariantCulture)]);

        args.AddRange(["-pix_fmt", "yuv420p", "-movflags", "+faststart", "-f", "mp4", output]);
        return args;

        static string Even(int value) => (value / 2 * 2).ToString(CultureInfo.InvariantCulture);
    }

    private static (int Width, int Height)? ReadFrameSize(string file)
    {
        var header = new byte[64 * 1024];
        using var stream = File.OpenRead(file);
        int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return JpegInfo.TryGetSize(header.AsSpan(0, read), out int width, out int height) ? (width, height) : null;
    }

    private void RetryFailedRenders()
    {
        List<string> failed;
        lock (_gate)
            failed = _pending.Where(p => p.State == TimelapsePendingState.Failed).Select(p => p.Name).ToList();

        foreach (string name in failed)
            RetryRender(name);
    }

    // ------------------------------------------------------------------ ITimelapseService

    public TimelapseStatus GetStatus()
    {
        var options = _options.CurrentValue;
        var camera = _registry.Find(options.CameraId) as CameraEntity;
        long? free = FreeBytes();
        var ffmpeg = _ffmpeg.GetStatus();

        lock (_gate)
        {
            var current = _current is { } r ? new TimelapseRecording(r.Name, r.StartedUtc, r.Frames, r.Bytes, r.LastFrameUtc) : null;

            // Frame size for the storage estimate: this recording's average, else the camera's current frame
            long? average = current is { Frames: > 0 }
                ? current.Bytes / current.Frames
                : camera?.Source.LatestFrame?.Jpeg.Length;

            return new TimelapseStatus(
                current != null,
                current,
                _pending.Select(p => new TimelapsePending(p.Name, p.State, p.Frames, p.Bytes, p.Progress, p.Error)).ToList(),
                camera?.IsAvailable == true,
                options.IntervalSeconds,
                options.MaxDurationHours,
                options.VideoFps,
                average,
                _videoBytesPerFrame,
                free,
                ffmpeg);
        }
    }

    /// <summary>Measured MP4 bytes per frame, or the assumed ratio of the JPEG frame size.</summary>
    private long VideoBytesPerFrame(long frameBytes)
    {
        lock (_gate)
            return _videoBytesPerFrame ?? (long)(frameBytes * AssumedVideoRatio);
    }

    /// <summary>Averages the size per frame over the existing videos (after start, each render and each delete).</summary>
    private void UpdateVideoStats()
    {
        long bytes = 0, frames = 0;
        foreach (var video in GetVideos())
        {
            if (video.Frames is > 0)
            {
                bytes += video.SizeBytes;
                frames += video.Frames.Value;
            }
        }

        lock (_gate)
            _videoBytesPerFrame = frames > 0 ? bytes / frames : null;
    }

    public IReadOnlyList<TimelapseVideo> GetVideos()
    {
        if (!Directory.Exists(_videosDir))
            return [];

        var videos = new List<TimelapseVideo>();
        foreach (var file in new DirectoryInfo(_videosDir).EnumerateFiles("*.mp4"))
        {
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (!IsValidName(name))
                continue;

            var info = ReadJson<VideoInfo>(Path.ChangeExtension(file.FullName, ".json"));
            double? duration = info is { Frames: > 0, Fps: > 0 } ? Math.Round((double)info.Frames.Value / info.Fps.Value, 1) : null;

            videos.Add(new TimelapseVideo(
                name,
                file.Length,
                file.LastWriteTimeUtc,
                info?.StartedUtc,
                info?.StoppedUtc,
                info?.Frames,
                info?.Fps,
                duration,
                File.Exists(VideoPath(name, thumbnail: true))));
        }

        return [.. videos.OrderByDescending(v => v.StoppedUtc ?? v.CreatedUtc)];
    }

    public string? GetVideoFile(string name, bool thumbnail)
    {
        if (!IsValidName(name))
            return null;

        string path = VideoPath(name, thumbnail);
        return File.Exists(path) ? path : null;
    }

    public bool DeleteVideo(string name)
    {
        string? video = GetVideoFile(name, thumbnail: false);
        if (video == null)
            return false;

        File.Delete(video);
        TryDeleteFile(VideoPath(name, thumbnail: true));
        TryDeleteFile(Path.ChangeExtension(video, ".json"));
        UpdateVideoStats();
        _logger.LogInformation("Timelapse video {Name} deleted", name);
        return true;
    }

    public bool RetryRender(string name)
    {
        Pending? pending;
        lock (_gate)
            pending = _pending.FirstOrDefault(p => p.Name == name && p.State == TimelapsePendingState.Failed);

        if (pending == null)
            return false;

        TryWriteJson(Path.Combine(pending.Dir, SessionFile),
            new SessionInfo { StartedUtc = pending.StartedUtc, StoppedUtc = pending.StoppedUtc, State = StateQueued });
        Enqueue(pending);
        return true;
    }

    public bool DiscardRecording(string name)
    {
        Pending? pending;
        lock (_gate)
        {
            pending = _pending.FirstOrDefault(p => p.Name == name && p.State == TimelapsePendingState.Failed);
            if (pending != null)
                _pending.Remove(pending);
        }

        if (pending == null)
            return false;

        TryDeleteDirectory(pending.Dir);
        _logger.LogInformation("Frames of timelapse {Name} discarded", name);
        return true;
    }

    public string? InstallFfmpeg() => _ffmpeg.StartInstall();

    // ------------------------------------------------------------------ helpers

    /// <summary>Names are file names; only letters, digits, '-' and '_' (no path tricks via the API).</summary>
    internal static bool IsValidName(string name) => NameRegex().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")]
    private static partial Regex NameRegex();

    private string NewName(DateTime local)
    {
        string baseName = "timelapse_" + local.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string name = baseName;
        for (int i = 2; Directory.Exists(Path.Combine(_sessionsDir, name)) || File.Exists(VideoPath(name, thumbnail: false)); i++)
            name = baseName + "_" + i.ToString(CultureInfo.InvariantCulture);
        return name;
    }

    private string VideoPath(string name, bool thumbnail) => Path.Combine(_videosDir, name + (thumbnail ? ".jpg" : ".mp4"));

    internal static string FramePath(string dir, int number) => Path.Combine(dir, number.ToString("D6", CultureInfo.InvariantCulture) + ".jpg");

    private static (int Frames, long Bytes, DateTime? LastWriteUtc) CountFrames(string dir)
    {
        int frames = 0;
        long bytes = 0;
        DateTime? last = null;
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.jpg"))
        {
            frames++;
            bytes += file.Length;
            if (last == null || file.LastWriteTimeUtc > last)
                last = file.LastWriteTimeUtc;
        }

        return (frames, bytes, last);
    }

    private long? FreeBytes()
    {
        try
        {
            return new DriveInfo(Path.GetFullPath(_rootDir)).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Write-then-rename, so a power cut never leaves a half-written file.</summary>
    private static void WriteJson<T>(string path, T value)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, overwrite: true);
    }

    private void TryWriteJson<T>(string path, T value)
    {
        try
        {
            WriteJson(path, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not write {Path}: {Message}", path, ex.Message);
        }
    }

    private void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not delete {Dir}: {Message}", dir, ex.Message);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class Recording(string name, string dir, DateTime startedUtc)
    {
        public string Name { get; } = name;
        public string Dir { get; } = dir;
        public DateTime StartedUtc { get; } = startedUtc;
        public int Frames { get; set; }
        public long Bytes { get; set; }
        public DateTime? LastFrameUtc { get; set; }
        public DateTime LastSourceFrameUtc { get; set; }
        public DateTime NextCaptureUtc { get; set; }
    }

    private sealed class Pending(string name, string dir, DateTime startedUtc, DateTime stoppedUtc)
    {
        public string Name { get; } = name;
        public string Dir { get; } = dir;
        public DateTime StartedUtc { get; } = startedUtc;
        public DateTime StoppedUtc { get; } = stoppedUtc;
        public TimelapsePendingState State { get; set; }
        public int Frames { get; init; }
        public long Bytes { get; init; }
        public double? Progress { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>{session}/session.json</summary>
    private sealed class SessionInfo
    {
        public DateTime StartedUtc { get; set; }
        public DateTime? StoppedUtc { get; set; }
        public string State { get; set; } = StateRecording;
        public string? Error { get; set; }
    }

    /// <summary>videos/{name}.json</summary>
    private sealed class VideoInfo
    {
        public DateTime? StartedUtc { get; set; }
        public DateTime? StoppedUtc { get; set; }
        public int? Frames { get; set; }
        public int? Fps { get; set; }
    }
}
