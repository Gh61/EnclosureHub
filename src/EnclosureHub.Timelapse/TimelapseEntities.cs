using EnclosureHub.Core.Entities;

namespace EnclosureHub.Timelapse;

/// <summary>
/// The module's entities, created apart from <see cref="TimelapseService"/> because the service needs the
/// entity registry, which in turn collects these entities.
/// </summary>
public sealed class TimelapseEntities
{
    public TimelapseEntities(TimelapseOptions options)
    {
        Switch = new TimelapseSwitch(options.Id, options.Name)
        {
            Icon = "mdi:timelapse",
        };

        Rendering = new BinarySensorEntity(options.Id + "_rendering", options.Name + " rendering")
        {
            DeviceClass = "running",
            Icon = "mdi:movie-cog",
        };
    }

    /// <summary>"timelapse": ON = recording. OFF stops the recording and renders the video.</summary>
    public TimelapseSwitch Switch { get; }

    /// <summary>"timelapse_rendering": ON while ffmpeg turns a recording into a video.</summary>
    public BinarySensorEntity Rendering { get; }
}
