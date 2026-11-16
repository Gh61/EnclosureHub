using EnclosureHub.Core.Camera;

namespace EnclosureHub.Core.Entities;

/// <summary>
/// Still/MJPEG camera. Frames are read from <see cref="Source"/>; new frames do NOT raise
/// <see cref="Entity.StateChanged"/> (that would flood listeners) – consumers poll the
/// source or wait for the next frame.
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

    public override object GetState()
    {
        var frame = Source.LatestFrame;
        return new
        {
            hasFrame = frame != null,
            lastFrameUtc = frame?.TimestampUtc,
            frameAgeSeconds = frame == null ? (double?)null : Math.Round((DateTime.UtcNow - frame.TimestampUtc).TotalSeconds, 1),
        };
    }
}
