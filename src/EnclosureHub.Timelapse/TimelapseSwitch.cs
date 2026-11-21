using EnclosureHub.Core.Entities;

namespace EnclosureHub.Timelapse;

/// <summary>
/// "timelapse" switch: ON starts a recording, OFF stops it and queues the video for rendering.
/// The state is reported by <see cref="TimelapseService"/> once the change is in effect.
/// </summary>
public sealed class TimelapseSwitch : SwitchEntity
{
    public TimelapseSwitch(string id, string name)
        : base(id, name)
    {
    }

    /// <summary>Set by the timelapse service: starts (true) or stops (false) the recording.</summary>
    internal Func<bool, CancellationToken, Task>? Handler { get; set; }

    public override Task SetAsync(bool on, CancellationToken cancellationToken)
        => Handler?.Invoke(on, cancellationToken) ?? Task.CompletedTask;

    internal void Reflect(bool recording) => UpdateState(recording);
}
