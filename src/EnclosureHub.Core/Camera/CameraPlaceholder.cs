using System.Reflection;

namespace EnclosureHub.Core.Camera;

/// <summary>Images shown instead of a live frame, e.g. "CAMERA OFF" (640×480 JPEG, embedded).</summary>
public static class CameraPlaceholder
{
    private static byte[]? _off;

    /// <summary>Picture published/served while a camera is switched off, so nobody mistakes a stale frame for live.</summary>
    public static byte[] OffJpeg => _off ??= Load("EnclosureHub.Core.Resources.camera-off.jpg");

    private static byte[] Load(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing embedded resource " + resourceName);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
