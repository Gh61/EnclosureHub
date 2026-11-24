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

    /// <summary>
    /// With a linked, connected and printing printer: save a frame on every new layer instead of by the timer.
    /// Without one (switched off, not printing) the timer is used, so this never has to be changed back.
    /// </summary>
    public bool LayerFrames { get; set; } = true;

    /// <summary>
    /// In layer mode, ask the camera to capture at least this fast while recording, so the layer frame comes within
    /// 1/this seconds of the layer change (about 15 % CPU per fps on a Pi 2 at 1640×1232). 0 = the camera's own rate.
    /// </summary>
    public double CaptureFps { get; set; }

    /// <summary>In layer mode, also save a frame by the timer when a layer takes longer than the interval.</summary>
    public bool TimerFramesBetweenLayers { get; set; }

    /// <summary>In layer mode, skip layer changes that come sooner than the interval after the previous frame.</summary>
    public bool LayerMinInterval { get; set; } = true;

    /// <summary>Start recording when a print starts and stop (and render) when it ends. Off by default so it does not fight an existing Home Assistant automation.</summary>
    public bool FollowPrinter { get; set; }

    /// <summary>A recording stops (and is rendered) automatically after this many hours.</summary>
    public double MaxDurationHours { get; set; } = 48;

    /// <summary>Frame rate of the rendered video.</summary>
    public int VideoFps { get; set; } = 30;

    /// <summary>
    /// <see cref="HardwareEncoder"/> (default; about 10× faster than libx264 on a Pi 2 at the same file size) or
    /// <see cref="SoftwareEncoder"/>. The hardware encoder falls back to software when it fails (e.g. no VideoCore).
    /// </summary>
    public string Encoder { get; set; } = HardwareEncoder;

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
