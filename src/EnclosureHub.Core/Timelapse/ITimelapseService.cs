namespace EnclosureHub.Core.Timelapse;

/// <summary>
/// Timelapse recording and the library of finished videos. Implemented by the timelapse module; used by
/// adapters (HTTP API, dashboard) that must not know the module itself. Recording on/off is also
/// exposed as a switch entity, so Home Assistant and the entity API control it the usual way.
/// </summary>
public interface ITimelapseService
{
    /// <summary>Id of the switch entity that starts (ON) and stops (OFF) a recording.</summary>
    string SwitchId { get; }

    TimelapseStatus GetStatus();

    /// <summary>Finished videos, newest first.</summary>
    IReadOnlyList<TimelapseVideo> GetVideos();

    /// <summary>Full path of a video file (<c>.mp4</c>) or its thumbnail (<c>.jpg</c>); null when it does not exist.</summary>
    string? GetVideoFile(string name, bool thumbnail);

    /// <summary>Deletes a finished video with its thumbnail. False when it does not exist.</summary>
    bool DeleteVideo(string name);

    /// <summary>Queues a failed recording for rendering again. False when there is no such failed recording.</summary>
    bool RetryRender(string name);

    /// <summary>Deletes the frames of a failed recording. False when there is no such failed recording.</summary>
    bool DiscardRecording(string name);

    /// <summary>
    /// Starts installing ffmpeg with the system package manager in the background (progress in
    /// <see cref="TimelapseStatus.Ffmpeg"/>). Returns an error message when it cannot be started, otherwise null.
    /// </summary>
    string? InstallFfmpeg();
}

/// <summary>
/// State of the current recording, the render queue and the storage. <c>AverageFrameBytes</c> (saved JPEG) and
/// <c>VideoBytesPerFrame</c> (rendered MP4, averaged over the existing videos; null before the first one) feed
/// the storage estimates: while a recording is rendered, its frames and its video are on disk together.
/// </summary>
public sealed record TimelapseStatus(
    bool Recording,
    TimelapseRecording? Current,
    IReadOnlyList<TimelapsePending> Pending,
    bool CameraAvailable,
    double IntervalSeconds,
    double MaxDurationHours,
    int VideoFps,
    long? AverageFrameBytes,
    long? VideoBytesPerFrame,
    long? FreeBytes,
    FfmpegStatus Ffmpeg,
    TimelapseTrigger Trigger,
    int? LastLayer);

/// <summary>What currently decides when a frame is saved.</summary>
public enum TimelapseTrigger
{
    /// <summary>Every interval (no printer, or layer frames off, or the printer is not printing).</summary>
    Interval,

    /// <summary>On new layers reported by the printer.</summary>
    Layers,

    /// <summary>On new layers, plus by the timer when a layer takes longer than the interval.</summary>
    LayersAndInterval,
}

public enum FfmpegInstallState
{
    Idle,
    Running,
    Succeeded,
    Failed,
}

/// <summary>ffmpeg renders the videos; it is required and can be installed from the dashboard.</summary>
public sealed record FfmpegStatus(
    bool Available,
    string? Version,
    bool CanInstall,
    FfmpegInstallState InstallState,
    string? InstallMessage);

/// <summary>The recording in progress.</summary>
public sealed record TimelapseRecording(
    string Name,
    DateTime StartedUtc,
    int Frames,
    long Bytes,
    DateTime? LastFrameUtc);

public enum TimelapsePendingState
{
    /// <summary>Waiting for the renderer (one video is rendered at a time).</summary>
    Queued,

    Rendering,

    /// <summary>ffmpeg failed; the frames are kept so the video can be rendered again or discarded.</summary>
    Failed,
}

/// <summary>A stopped recording that has not become a video yet.</summary>
public sealed record TimelapsePending(
    string Name,
    TimelapsePendingState State,
    int Frames,
    long Bytes,
    double? Progress,
    string? Error);

/// <summary>A finished video in the library.</summary>
public sealed record TimelapseVideo(
    string Name,
    long SizeBytes,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? StoppedUtc,
    int? Frames,
    int? Fps,
    double? DurationSeconds,
    bool HasThumbnail);
