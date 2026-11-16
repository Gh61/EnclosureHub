namespace EnclosureHub.Core.Camera;

public interface ICameraFrameSource
{
    /// <summary>Most recent frame, or null if no frame has been captured yet.</summary>
    CameraFrame? LatestFrame { get; }

    /// <summary>Completes when the next frame arrives.</summary>
    Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken);
}
