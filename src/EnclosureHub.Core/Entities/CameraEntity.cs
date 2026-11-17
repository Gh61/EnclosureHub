using EnclosureHub.Core.Camera;

namespace EnclosureHub.Core.Entities;

/// <summary>
/// Still/MJPEG camera. Frames are read from <see cref="Source"/>; new frames do NOT raise
/// <see cref="Entity.StateChanged"/> (that would flood listeners) – consumers poll the
/// source or wait for the next frame. <see cref="IsAvailable"/> (switched on and producing
/// frames) does raise it, so adapters can mark the camera unavailable instead of showing a stale picture.
/// </summary>
public sealed class CameraEntity : Entity
{
    public CameraEntity(string id, string name, ICameraFrameSource source)
        : base(id, name)
    {
        Source = source;
    }

    public override EntityKind Kind => EntityKind.Camera;

    public ICameraFrameSource Source { get; }

    /// <summary>False while the camera is switched off or not delivering frames.</summary>
    public bool IsAvailable { get; private set; } = true;

    public void SetAvailable(bool available)
    {
        if (IsAvailable == available)
            return;

        IsAvailable = available;
        NotifyStateChanged();
    }

    public override object GetState()
    {
        var frame = Source.LatestFrame;
        return new
        {
            available = IsAvailable,
            hasFrame = frame != null,
            lastFrameUtc = frame?.TimestampUtc,
            frameAgeSeconds = frame == null ? (double?)null : Math.Round((DateTime.UtcNow - frame.TimestampUtc).TotalSeconds, 1),
        };
    }
}
