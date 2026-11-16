namespace EnclosureHub.Camera;

/// <summary>Bound from "Modules:Camera".</summary>
public sealed class RpicamCameraOptions
{
    public string Id { get; set; } = "camera";

    public string Name { get; set; } = "Camera";

    /// <summary>rpicam-vid on current Raspberry Pi OS (libcamera-vid on older releases).</summary>
    public string Executable { get; set; } = "rpicam-vid";

    /// <summary>1640x1232 is the full-FOV binned mode of the IMX219 (Camera Module 2).</summary>
    public int Width { get; set; } = 1640;

    public int Height { get; set; } = 1232;

    /// <summary>Frames per second. 1 is plenty for monitoring; 0.2 = one frame every 5 s.</summary>
    public double Framerate { get; set; } = 1;

    /// <summary>JPEG quality 1–100.</summary>
    public int Quality { get; set; } = 85;

    /// <summary>Extra rpicam-vid arguments, e.g. "--rotation 180" or "--hflip --vflip".</summary>
    public string ExtraArgs { get; set; } = "";

    /// <summary>Delay before restarting rpicam-vid after it exits.</summary>
    public int RestartDelaySeconds { get; set; } = 5;

    /// <summary>The "running" sensor turns off when no frame arrived for this long.</summary>
    public int StaleAfterSeconds { get; set; } = 15;
}
