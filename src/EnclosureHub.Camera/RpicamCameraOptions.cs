namespace EnclosureHub.Camera;

/// <summary>Bound from "Modules:Camera".</summary>
public sealed class RpicamCameraOptions
{
    /// <summary>Module present. False = no camera entities at all (file-only, needs a restart).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Camera switched on. Changed at runtime by the "{id}_active" switch (Home Assistant, dashboard, API):
    /// false stops the capture process, the entities stay registered.
    /// </summary>
    public bool Active { get; set; } = true;

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

    /// <summary>Image rotation in degrees; rpicam supports 0 and 180.</summary>
    public int Rotation { get; set; }

    /// <summary>Extra rpicam-vid arguments, e.g. "--hflip --vflip".</summary>
    public string ExtraArgs { get; set; } = "";

    /// <summary>
    /// With a linked printer: the first frame after every layer change is published as a layer frame, besides the
    /// regular frame rate. Below <see cref="OnDemandFramerate"/> rpicam-vid then runs at that rate and the service keeps
    /// only the regular and the layer frames (selective mode).
    /// </summary>
    public bool CaptureOnLayerChange { get; set; }

    /// <summary>Capture rate in selective mode; a layer frame arrives within 1/this seconds. Each fps costs about 15 % CPU on a Pi 2 at 1640×1232.</summary>
    public double OnDemandFramerate { get; set; } = 1;

    /// <summary>Delay before restarting rpicam-vid after it exits.</summary>
    public int RestartDelaySeconds { get; set; } = 5;

    /// <summary>The "running" sensor turns off when no frame arrived for this long.</summary>
    public int StaleAfterSeconds { get; set; } = 15;
}
