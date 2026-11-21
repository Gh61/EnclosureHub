using System.Diagnostics;
using System.Globalization;
using EnclosureHub.Core.Camera;
using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Printer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Camera;

/// <summary>
/// Runs a single long-lived <c>rpicam-vid</c> process producing MJPEG on stdout and keeps
/// the latest frame in memory. A long-lived process keeps exposure and white balance
/// settled (no warm-up per frame). Restarts the process when it exits or when the
/// camera settings change; stops it while <see cref="RpicamCameraOptions.Active"/> is false.
/// With <see cref="RpicamCameraOptions.CaptureOnLayerChange"/> and a linked printer, the first frame after every layer
/// change is a "layer frame" consumers can wait for. Below <see cref="RpicamCameraOptions.OnDemandFramerate"/> rpicam-vid
/// runs at that rate and only the regular frames (configured rate) and the layer frames are published (selective mode).
/// rpicam-vid's own pause (--signal) does not help: it keeps encoding while paused (measured 70 % CPU at 5 fps on a Pi 2).
/// </summary>
public sealed class RpicamCameraService : BackgroundService, ICameraFrameSource
{
    private readonly IOptionsMonitor<RpicamCameraOptions> _options;
    private readonly ILogger<RpicamCameraService> _logger;
    private readonly IDisposable? _optionsChange;
    private readonly IPrinterConnection? _printer;

    // Selective mode: publish only regular frames (every _selectivePeriodTicks) and the first frame after a layer change
    private volatile bool _selective;
    private long _selectivePeriodTicks;
    private long _nextPublishTicks;
    private int _layerRequested;

    private CameraFrame? _latestFrame;
    private TaskCompletionSource<CameraFrame> _nextFrame = NewFrameSource();

    // Cancelled to interrupt the current process cycle when settings change
    private CancellationTokenSource? _cycle;
    private string? _activeSignature;

    // When the current capture process was started (UTC ticks); grants a grace period before "not running"
    private long _captureStartedTicks;

    public RpicamCameraService(IOptionsMonitor<RpicamCameraOptions> options, CameraActiveSwitch activeSwitch, ILogger<RpicamCameraService> logger,
        IPrinterConnection? printer = null)
    {
        _options = options;
        _logger = logger;
        _printer = printer;
        if (_printer != null)
            _printer.EventRaised += OnPrinterEvent;

        var initial = options.CurrentValue;

        Camera = new CameraEntity(initial.Id, initial.Name, this)
        {
            Icon = "mdi:cctv",
        };

        ActiveSwitch = activeSwitch;
        ActiveSwitch.IsConfiguredOn = () => _options.CurrentValue.Active;
        ActiveSwitch.RestartCapture = RestartCapture;
        Volatile.Write(ref _captureStartedTicks, DateTime.UtcNow.Ticks);
        SetRunning(initial.Active);

        _optionsChange = options.OnChange(OnOptionsChanged);
    }

    public CameraEntity Camera { get; }

    /// <summary>"{id}_active": camera on/off and running state in one switch (Home Assistant, dashboard, API).</summary>
    public CameraActiveSwitch ActiveSwitch { get; }

    public CameraFrame? LatestFrame => Volatile.Read(ref _latestFrame);

    public Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken)
        => Volatile.Read(ref _nextFrame).Task.WaitAsync(cancellationToken);

    /// <summary>The first frame after every layer change is published (camera setting, printer linked).</summary>
    public bool CapturesOnPrinterLayer => _options.CurrentValue.CaptureOnLayerChange && _printer is { IsLinked: true };

    public override void Dispose()
    {
        _optionsChange?.Dispose();
        if (_printer != null)
            _printer.EventRaised -= OnPrinterEvent;
        base.Dispose();
    }

    private void OnPrinterEvent(object? sender, PrinterEvent e)
    {
        // Selective mode publishes the next frame; faster modes deliver their next frame within 1/fps anyway
        if (e.Kind == PrinterEventKind.LayerChanged && CapturesOnPrinterLayer)
            Interlocked.Exchange(ref _layerRequested, 1);
    }

    /// <summary>Selective mode: layer frames wanted and the configured rate below the on-demand rate.</summary>
    private bool Selective(RpicamCameraOptions options)
        => CapturesOnPrinterLayer && options.Framerate < options.OnDemandFramerate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchdog = WatchRunningAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Volatile.Write(ref _cycle, cycle);

            // Read the options after publishing the cycle so a change arriving in between still interrupts it
            var options = _options.CurrentValue;
            bool selective = Selective(options);
            string arguments = BuildArguments(options, selective);
            _activeSignature = Signature(options, arguments);
            StartSelective(options, selective);

            try
            {
                if (!options.Active)
                {
                    _logger.LogInformation("Camera is turned off (Modules:Camera:Active = false)");
                    ClearFrame();
                    await Task.Delay(Timeout.Infinite, cycle.Token);
                }
                else
                {
                    Volatile.Write(ref _captureStartedTicks, DateTime.UtcNow.Ticks);
                    SetRunning(true); // on trust until the grace period ends
                    await RunProcessAsync(options, arguments, cycle.Token);
                    _logger.LogWarning("{Exe} exited, restarting in {Delay} s", options.Executable, options.RestartDelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(options.RestartDelaySeconds), cycle.Token);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // settings changed or restart requested – start the next cycle immediately
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera process failed, retrying in {Delay} s", options.RestartDelaySeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.RestartDelaySeconds), cycle.Token);
                }
                catch (OperationCanceledException)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;
                }
            }
            finally
            {
                Volatile.Write(ref _cycle, null);
            }
        }

        await watchdog;
    }

    private void OnOptionsChanged(RpicamCameraOptions options)
    {
        string signature = Signature(options, BuildArguments(options, Selective(options)));
        if (signature == _activeSignature)
            return; // e.g. another section was saved, or the reload fired twice – nothing relevant changed

        _activeSignature = signature;
        _logger.LogInformation("Camera settings changed, applying");
        RestartCapture();
    }

    /// <summary>Interrupts the current cycle; the loop re-reads the options and starts the process again.</summary>
    private void RestartCapture()
    {
        try
        {
            Volatile.Read(ref _cycle)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // cycle just ended; the loop picks up the new options anyway
        }
    }

    private static string Signature(RpicamCameraOptions options, string arguments)
        => options.Active ? options.Executable + " " + arguments : "off";

    /// <summary>Builds the rpicam-vid command line for the given options.</summary>
    public static string BuildArguments(RpicamCameraOptions options) => BuildArguments(options, selective: false);

    /// <summary>In selective mode the sensor runs at <see cref="RpicamCameraOptions.OnDemandFramerate"/>; the service keeps the configured rate.</summary>
    public static string BuildArguments(RpicamCameraOptions options, bool selective)
    {
        var parts = new List<string>
        {
            "-t 0",           // run forever
            "-n",             // no preview window
            "--codec mjpeg",
            "--width " + options.Width.ToString(CultureInfo.InvariantCulture),
            "--height " + options.Height.ToString(CultureInfo.InvariantCulture),
            "--framerate " + (selective ? options.OnDemandFramerate : options.Framerate).ToString(CultureInfo.InvariantCulture),
            "-q " + options.Quality.ToString(CultureInfo.InvariantCulture),
        };

        if (options.Rotation != 0)
            parts.Add("--rotation " + options.Rotation.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(options.ExtraArgs))
            parts.Add(options.ExtraArgs.Trim());

        parts.Add("-o -");    // write to stdout
        return string.Join(' ', parts);
    }

    private async Task RunProcessAsync(RpicamCameraOptions options, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.Executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        _logger.LogInformation("Starting {Exe} {Args}", options.Executable, arguments);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start " + options.Executable);

        using var killOnStop = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already exited
            }
        });

        // stderr must be drained, otherwise rpicam-vid blocks once the pipe buffer is full
        var stderrTask = PumpStderrAsync(process.StandardError);

        var splitter = new MjpegFrameSplitter();
        var buffer = new byte[64 * 1024];
        var stdout = process.StandardOutput.BaseStream;
        int overflows = 0;

        try
        {
            int read;
            while ((read = await stdout.ReadAsync(buffer, cancellationToken)) > 0)
            {
                splitter.Push(buffer.AsSpan(0, read), PublishFrame);

                if (splitter.OverflowCount != overflows)
                {
                    overflows = splitter.OverflowCount;
                    _logger.LogWarning("Dropped an oversized frame (total {Count})", overflows);
                }
            }
        }
        finally
        {
            // Make sure the process is gone before the next cycle starts (the camera can be opened only once)
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already exited
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await stderrTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogWarning("{Exe} exited with code {Code}", options.Executable, process.ExitCode);
    }

    private async Task PumpStderrAsync(StreamReader stderr)
    {
        string? line;
        while ((line = await stderr.ReadLineAsync()) != null)
        {
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                _logger.LogError("rpicam: {Line}", line);
            else
                _logger.LogDebug("rpicam: {Line}", line);
        }
    }

    /// <summary>
    /// Keeps the switch state honest: ON only while the camera is switched on and a frame arrived within
    /// StaleAfterSeconds (or the process started less than StaleAfterSeconds ago).
    /// </summary>
    private async Task WatchRunningAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var options = _options.CurrentValue;
                if (!options.Active)
                {
                    SetRunning(false);
                    continue;
                }

                // The printer link may change without a camera settings change (its options reload separately)
                if (Selective(options) != _selective)
                {
                    _logger.LogInformation("Printer link or layer capture changed, restarting the camera");
                    _selective = !_selective; // no repeat until the new cycle sets it
                    RestartCapture();
                }

                var now = DateTime.UtcNow;
                var frame = LatestFrame;
                bool fresh = frame != null && (now - frame.TimestampUtc).TotalSeconds < options.StaleAfterSeconds;
                bool starting = (now - new DateTime(Volatile.Read(ref _captureStartedTicks), DateTimeKind.Utc)).TotalSeconds < options.StaleAfterSeconds;
                SetRunning(fresh || starting);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void ClearFrame()
    {
        Volatile.Write(ref _latestFrame, null);
        SetRunning(false);
    }

    /// <summary>One source of truth for "the camera works": the switch state and the camera entity's availability.</summary>
    private void SetRunning(bool running)
    {
        ActiveSwitch.Reflect(running);
        Camera.SetAvailable(running);
    }

    private void StartSelective(RpicamCameraOptions options, bool selective)
    {
        Interlocked.Exchange(ref _layerRequested, 0);
        Volatile.Write(ref _selectivePeriodTicks, TimeSpan.FromSeconds(1 / Math.Max(0.01, options.Framerate)).Ticks);
        Volatile.Write(ref _nextPublishTicks, 0);
        _selective = selective;
        if (selective)
            _logger.LogInformation("Camera runs at {Rate} fps and keeps a frame every {Period:0.#} s plus one per layer change",
                options.OnDemandFramerate, 1 / Math.Max(0.01, options.Framerate));
    }

    /// <summary>Selective mode: true for a regular frame that is due or the first frame after a layer change.</summary>
    internal bool ShouldPublish(long nowTicks)
    {
        long next = Volatile.Read(ref _nextPublishTicks);
        if (nowTicks >= next)
        {
            long period = Volatile.Read(ref _selectivePeriodTicks);
            long following = next + period;
            Volatile.Write(ref _nextPublishTicks, following > nowTicks ? following : nowTicks + period);
            Interlocked.Exchange(ref _layerRequested, 0); // this frame serves a pending layer request too
            return true;
        }

        return Interlocked.Exchange(ref _layerRequested, 0) == 1;
    }

    private void PublishFrame(byte[] jpeg)
    {
        if (_selective && !ShouldPublish(DateTime.UtcNow.Ticks))
            return;

        var frame = new CameraFrame(jpeg, DateTime.UtcNow);
        Volatile.Write(ref _latestFrame, frame);

        var waiting = Interlocked.Exchange(ref _nextFrame, NewFrameSource());
        waiting.TrySetResult(frame);
    }

    private static TaskCompletionSource<CameraFrame> NewFrameSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
