using System.Text;
using EnclosureHub.Core;
using EnclosureHub.Core.Camera;
using EnclosureHub.Core.Entities;

namespace EnclosureHub.App.Api;

/// <summary>
///   GET /api/cameras/{id}/snapshot.jpg – latest frame ("CAMERA OFF" picture while switched off, 503 until the first frame)
///   GET /api/cameras/{id}/stream.mjpeg – multipart MJPEG stream of new frames
/// </summary>
internal static class CameraApi
{
    private static readonly byte[] CrLf = "\r\n"u8.ToArray();

    public static IEndpointRouteBuilder MapCameraApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cameras");

        group.MapGet("/{id}/snapshot.jpg", (string id, HttpContext context, IEntityRegistry registry) =>
        {
            if (registry.Find(id) is not CameraEntity camera)
                return Results.NotFound();

            context.Response.Headers.CacheControl = "no-cache, no-store";

            // Switched off: a "CAMERA OFF" picture rather than an error, so still-image clients show something sensible
            if (!camera.IsAvailable)
                return Results.File(CameraPlaceholder.OffJpeg, "image/jpeg");

            var frame = camera.Source.LatestFrame;
            if (frame == null)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            return Results.File(frame.Jpeg, "image/jpeg");
        });

        group.MapGet("/{id}/stream.mjpeg", async (string id, HttpContext context, IEntityRegistry registry) =>
        {
            if (registry.Find(id) is not CameraEntity camera)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var cancellationToken = context.RequestAborted;
            var body = context.Response.Body;

            context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
            context.Response.Headers.CacheControl = "no-cache, no-store";

            try
            {
                var frame = camera.Source.LatestFrame;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (frame != null)
                    {
                        var header = Encoding.ASCII.GetBytes(
                            "--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + frame.Jpeg.Length + "\r\n\r\n");
                        await body.WriteAsync(header, cancellationToken);
                        await body.WriteAsync(frame.Jpeg, cancellationToken);
                        await body.WriteAsync(CrLf, cancellationToken);
                        await body.FlushAsync(cancellationToken);
                    }

                    frame = await camera.Source.WaitForNextFrameAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // client disconnected
            }
        });

        return app;
    }
}
