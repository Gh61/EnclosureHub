namespace EnclosureHub.Core.Camera;

public interface ICameraFrameSource
{
    /// <summary>Most recent frame, or null if no frame has been captured yet.</summary>
    CameraFrame? LatestFrame { get; }

    /// <summary>Completes when the next frame arrives.</summary>
    Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The camera captures a fresh frame on every printer layer change (camera setting); consumers that react
    /// to the same layer change should wait for it (<see cref="CameraFrameSourceExtensions.WaitForFrameAfterAsync"/>)
    /// instead of taking <see cref="LatestFrame"/>.
    /// </summary>
    bool CapturesOnPrinterLayer => false;
}

public static class CameraFrameSourceExtensions
{
    /// <summary>The first frame captured at or after <paramref name="utc"/>; null when none arrives within <paramref name="timeout"/>.</summary>
    public static async Task<CameraFrame?> WaitForFrameAfterAsync(this ICameraFrameSource source, DateTime utc, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        try
        {
            while (true)
            {
                // Ask for the next frame before checking the latest one, so a frame arriving in between is not missed
                var next = source.WaitForNextFrameAsync(limit.Token);
                if (source.LatestFrame is { } latest && latest.TimestampUtc >= utc)
                    return latest;

                var frame = await next;
                if (frame.TimestampUtc >= utc)
                    return frame;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
