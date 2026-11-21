using EnclosureHub.Core.Timelapse;

namespace EnclosureHub.App.Api;

/// <summary>
/// Timelapse library (only mapped when the timelapse module is enabled). Start/stop goes through the
/// switch entity (<c>POST /api/entities/{switchId}/command</c>), like Home Assistant does.
///   GET    /api/timelapse                          – recording state, render queue, storage, ffmpeg
///   GET    /api/timelapse/videos                   – finished videos, newest first
///   GET    /api/timelapse/videos/{name}.mp4        – the video (range requests; ?download=1 as attachment)
///   GET    /api/timelapse/videos/{name}.jpg        – its thumbnail (last frame)
///   DELETE /api/timelapse/videos/{name}            – delete a video
///   POST   /api/timelapse/recordings/{name}/render – render a failed recording again
///   DELETE /api/timelapse/recordings/{name}        – discard the frames of a failed recording
///   POST   /api/timelapse/ffmpeg/install           – install ffmpeg (apt-get) in the background
/// </summary>
internal static class TimelapseApi
{
    public static IEndpointRouteBuilder MapTimelapseApi(this IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ITimelapseService)))
            return app; // module disabled: everything below answers 404

        var group = app.MapGroup("/api/timelapse");

        group.MapGet("/", (ITimelapseService timelapse) =>
            Results.Json(new { switchId = timelapse.SwitchId, status = timelapse.GetStatus() }, ApiJson.Options));

        group.MapGet("/videos", (ITimelapseService timelapse) =>
            Results.Json(timelapse.GetVideos(), ApiJson.Options));

        group.MapGet("/videos/{name}.mp4", (string name, bool? download, ITimelapseService timelapse) =>
            timelapse.GetVideoFile(name, thumbnail: false) is { } path
                ? Results.File(path, "video/mp4", download == true ? name + ".mp4" : null,
                    File.GetLastWriteTimeUtc(path), enableRangeProcessing: true)
                : Results.NotFound());

        group.MapGet("/videos/{name}.jpg", (string name, ITimelapseService timelapse) =>
            timelapse.GetVideoFile(name, thumbnail: true) is { } path
                ? Results.File(path, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(path))
                : Results.NotFound());

        group.MapDelete("/videos/{name}", (string name, ITimelapseService timelapse) =>
            timelapse.DeleteVideo(name) ? Results.NoContent() : Results.NotFound());

        group.MapPost("/recordings/{name}/render", (string name, ITimelapseService timelapse) =>
            timelapse.RetryRender(name) ? Results.Accepted() : Results.NotFound(new { error = "No failed recording with this name." }));

        group.MapDelete("/recordings/{name}", (string name, ITimelapseService timelapse) =>
            timelapse.DiscardRecording(name) ? Results.NoContent() : Results.NotFound(new { error = "No failed recording with this name." }));

        group.MapPost("/ffmpeg/install", (ITimelapseService timelapse) =>
            timelapse.InstallFfmpeg() is { } error
                ? Results.BadRequest(new { error })
                : Results.Accepted(value: new { message = "Installing ffmpeg…" }));

        return app;
    }
}
