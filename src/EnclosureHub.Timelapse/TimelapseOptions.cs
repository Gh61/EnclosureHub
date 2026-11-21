namespace EnclosureHub.Timelapse;

/// <summary>Bound from "Modules:Timelapse".</summary>
public sealed class TimelapseOptions
{
    public const string SoftwareEncoder = "libx264";

    /// <summary>The VideoCore H.264 encoder of the Raspberry Pi (V4L2 M2M); much faster on a Pi 2, at most 1920 px wide.</summary>
    public const string HardwareEncoder = "h264_v4l2m2m";

    /// <summary>Module present. False = no timelapse entities, API or settings (file-only, needs a restart).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Id of the recording switch; the rendering sensor is "{Id}_rendering".</summary>
    public string Id { get; set; } = "timelapse";

    public string Name { get; set; } = "Timelapse";

    /// <summary>Camera entity the frames are taken from.</summary>
    public string CameraId { get; set; } = "camera";

    /// <summary>Turn the camera on (its "{CameraId}_active" switch) when a recording starts.</summary>
    public bool TurnCameraOn { get; set; } = true;

    /// <summary>Seconds between two saved frames.</summary>
    public double IntervalSeconds { get; set; } = 30;

    /// <summary>A recording stops (and is rendered) automatically after this many hours.</summary>
    public double MaxDurationHours { get; set; } = 48;

    /// <summary>Frame rate of the rendered video.</summary>
    public int VideoFps { get; set; } = 30;

    /// <summary><see cref="SoftwareEncoder"/> or <see cref="HardwareEncoder"/> (falls back to software when it fails).</summary>
    public string Encoder { get; set; } = SoftwareEncoder;

    /// <summary>libx264 quality, 0–51; lower is better and larger.</summary>
    public int Crf { get; set; } = 23;

    /// <summary>Bit rate for the hardware encoder (it has no constant-quality mode).</summary>
    public string HardwareBitrate { get; set; } = "10M";

    /// <summary>Data directory (frames of recordings, finished videos); relative paths are relative to the application directory. Read at startup.</summary>
    public string Directory { get; set; } = "timelapse";

    public string FfmpegExecutable { get; set; } = "ffmpeg";

    /// <summary>A recording stops (and is rendered) when less disk space is left.</summary>
    public int MinFreeMegabytes { get; set; } = 300;
}
