using System.Diagnostics;
using System.Globalization;
using EnclosureHub.Core.Camera;
using EnclosureHub.Core.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Camera;

/// <summary>
/// Runs a single long-lived <c>rpicam-vid</c> process producing MJPEG on stdout and keeps
/// the latest frame in memory. A long-lived process keeps exposure and white balance
/// settled (no warm-up per frame). Restarts the process when it exits.
/// </summary>
public sealed class RpicamCameraService : BackgroundService, ICameraFrameSource
{
    private readonly RpicamCameraOptions _options;
    private readonly ILogger<RpicamCameraService> _logger;

    private CameraFrame? _latestFrame;
    private TaskCompletionSource<CameraFrame> _nextFrame = NewFrameSource();

    public RpicamCameraService(IOptions<RpicamCameraOptions> options, ILogger<RpicamCameraService> logger)
    {
        _options = options.Value;
        _logger = logger;

        Camera = new CameraEntity(_options.Id, _options.Name, this)
        {
            Icon = "mdi:cctv",
        };

        RunningSensor = new BinarySensorEntity(_options.Id + "_running", _options.Name + " running")
        {
            DeviceClass = "running",
            IsDiagnostic = true,
        };
    }

    public CameraEntity Camera { get; }

    public BinarySensorEntity RunningSensor { get; }

    public CameraFrame? LatestFrame => Volatile.Read(ref _latestFrame);

    public Task<CameraFrame> WaitForNextFrameAsync(CancellationToken cancellationToken)
        => Volatile.Read(ref _nextFrame).Task.WaitAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchdog = WatchFreshnessAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera process failed");
            }

            _logger.LogWarning("{Exe} exited, restarting in {Delay} s", _options.Executable, _options.RestartDelaySeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.RestartDelaySeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await watchdog;
    }

    private async Task RunProcessAsync(CancellationToken stoppingToken)
    {
        string arguments = string.Join(' ',
            "-t 0",           // run forever
            "-n",             // no preview window
            "--codec mjpeg",
            "--width " + _options.Width.ToString(CultureInfo.InvariantCulture),
            "--height " + _options.Height.ToString(CultureInfo.InvariantCulture),
            "--framerate " + _options.Framerate.ToString(CultureInfo.InvariantCulture),
            "-q " + _options.Quality.ToString(CultureInfo.InvariantCulture),
            _options.ExtraArgs,
            "-o -");          // write to stdout

        var startInfo = new ProcessStartInfo(_options.Executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        _logger.LogInformation("Starting {Exe} {Args}", _options.Executable, arguments);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start " + _options.Executable);

        using var killOnStop = stoppingToken.Register(() =>
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

        int read;
        while ((read = await stdout.ReadAsync(buffer, stoppingToken)) > 0)
        {
            splitter.Push(buffer.AsSpan(0, read), PublishFrame);

            if (splitter.OverflowCount != overflows)
            {
                overflows = splitter.OverflowCount;
                _logger.LogWarning("Dropped an oversized frame (total {Count})", overflows);
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await stderrTask;

        stoppingToken.ThrowIfCancellationRequested();
        _logger.LogWarning("{Exe} exited with code {Code}", _options.Executable, process.ExitCode);
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

    private async Task WatchFreshnessAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var frame = LatestFrame;
                bool fresh = frame != null
                    && (DateTime.UtcNow - frame.TimestampUtc).TotalSeconds < _options.StaleAfterSeconds;
                RunningSensor.SetState(fresh);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void PublishFrame(byte[] jpeg)
    {
        var frame = new CameraFrame(jpeg, DateTime.UtcNow);
        Volatile.Write(ref _latestFrame, frame);

        var waiting = Interlocked.Exchange(ref _nextFrame, NewFrameSource());
        waiting.TrySetResult(frame);
    }

    private static TaskCompletionSource<CameraFrame> NewFrameSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
