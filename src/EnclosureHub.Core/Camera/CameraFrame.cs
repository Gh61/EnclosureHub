namespace EnclosureHub.Core.Camera;

/// <summary>One JPEG image with the time it was captured.</summary>
public sealed record CameraFrame(byte[] Jpeg, DateTime TimestampUtc);
