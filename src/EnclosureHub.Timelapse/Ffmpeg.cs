using System.Diagnostics;
using System.Globalization;
using EnclosureHub.Core.Timelapse;
using Microsoft.Extensions.Logging;

namespace EnclosureHub.Timelapse;

/// <summary>
/// Finds ffmpeg, installs it with apt-get on request (the service user needs passwordless sudo, as the
/// Raspberry Pi OS default user has) and runs the render command at idle priority.
/// </summary>
internal sealed class Ffmpeg
{
    private static readonly TimeSpan RedetectInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);
    private const string AptGet = "/usr/bin/apt-get";

    private readonly string _executable;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private string? _path;
    private string? _version;
    private DateTime _lastDetectUtc;
    private FfmpegInstallState _installState;
    private string? _installMessage;

    public Ffmpeg(string executable, ILogger logger)
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? "ffmpeg" : executable.Trim();
        _logger = logger;
    }

    /// <summary>Raised after a successful installation from the dashboard.</summary>
    public event Action? Installed;

    /// <summary>Full path of ffmpeg, or null when it is not installed.</summary>
    public string? Path
    {
        get
        {
            DetectIfMissing();
            lock (_gate)
                return _path;
        }
    }

    public static bool CanInstall => OperatingSystem.IsLinux() && File.Exists(AptGet);

    public FfmpegStatus GetStatus()
    {
        DetectIfMissing();
        lock (_gate)
            return new FfmpegStatus(_path != null, _version, CanInstall, _installState, _installMessage);
    }

    /// <summary>Looks for the executable; reads the version once it is found.</summary>
    public void Detect()
    {
        string? path = FindOnPath(_executable);
        string? version = null;

        lock (_gate)
        {
            bool firstCheck = _lastDetectUtc == default;
            _lastDetectUtc = DateTime.UtcNow;
            if (path == _path && (_version != null || (path == null && !firstCheck)))
                return; // unchanged – log only when the state changes
        }

        if (path != null)
            version = ReadVersion(path);

        lock (_gate)
        {
            _path = path;
            _version = version;
        }

        if (path != null)
            _logger.LogInformation("Using ffmpeg {Version} ({Path})", version ?? "?", path);
        else
            _logger.LogWarning("ffmpeg was not found; timelapse videos cannot be rendered until it is installed (Settings → Timelapse)");
    }

    /// <summary>Re-checks now and then while ffmpeg is missing, so a manual installation is noticed.</summary>
    private void DetectIfMissing()
    {
        lock (_gate)
        {
            if (_path != null || DateTime.UtcNow - _lastDetectUtc < RedetectInterval)
                return;
        }

        Detect();
    }

    /// <summary>Starts "apt-get install ffmpeg" in the background; returns an error message or null.</summary>
    public string? StartInstall()
    {
        if (!CanInstall)
            return "Automatic installation needs Linux with apt-get. Install ffmpeg manually.";

        lock (_gate)
        {
            if (_installState == FfmpegInstallState.Running)
                return "The installation is already running.";

            _installState = FfmpegInstallState.Running;
            _installMessage = "Installing ffmpeg… this takes a few minutes on a Raspberry Pi 2.";
        }

        _ = Task.Run(InstallAsync);
        return null;
    }

    private async Task InstallAsync()
    {
        _logger.LogInformation("Installing ffmpeg (apt-get install ffmpeg)");
        using var timeout = new CancellationTokenSource(InstallTimeout);

        try
        {
            var result = await RunAptAsync(["install", "-y", "--no-install-recommends", "ffmpeg"], timeout.Token);

            if (result.ExitCode != 0 && !NeedsPassword(result.Output))
            {
                // Stale package lists are the usual cause (404 / unable to locate): update and try once more
                SetInstallMessage("Updating the package lists…");
                await RunAptAsync(["update"], timeout.Token);
                SetInstallMessage("Installing ffmpeg… this takes a few minutes on a Raspberry Pi 2.");
                result = await RunAptAsync(["install", "-y", "--no-install-recommends", "ffmpeg"], timeout.Token);
            }

            if (result.ExitCode != 0)
            {
                string message = NeedsPassword(result.Output)
                    ? "The service user cannot run sudo without a password. Install ffmpeg on the device: sudo apt install ffmpeg"
                    : "apt-get failed: " + result.Output;
                Finish(FfmpegInstallState.Failed, message);
                _logger.LogError("ffmpeg installation failed: {Output}", result.Output);
                return;
            }

            Detect();
            if (Path == null)
            {
                Finish(FfmpegInstallState.Failed, "apt-get finished, but ffmpeg is still not found.");
                return;
            }

            Finish(FfmpegInstallState.Succeeded, "ffmpeg " + (GetStatus().Version ?? "") + " is installed.");
            _logger.LogInformation("ffmpeg installed");
            Installed?.Invoke();
        }
        catch (Exception ex)
        {
            Finish(FfmpegInstallState.Failed, ex is OperationCanceledException ? "The installation timed out." : ex.Message);
            _logger.LogError(ex, "ffmpeg installation failed");
        }
    }

    private void SetInstallMessage(string message)
    {
        lock (_gate)
            _installMessage = message;
    }

    private void Finish(FfmpegInstallState state, string message)
    {
        lock (_gate)
        {
            _installState = state;
            _installMessage = message;
        }
    }

    private static bool NeedsPassword(string output)
        => output.Contains("password is required", StringComparison.OrdinalIgnoreCase)
           || output.Contains("a terminal is required", StringComparison.OrdinalIgnoreCase);

    private static Task<ProcessResult> RunAptAsync(string[] arguments, CancellationToken cancellationToken)
        => RunAsync("sudo", ["-n", "env", "DEBIAN_FRONTEND=noninteractive", AptGet, .. arguments], null, lowPriority: false, cancellationToken);

    /// <summary>Runs ffmpeg with the given arguments at idle priority; reports progress from "-progress pipe:1" output.</summary>
    public async Task<ProcessResult> RenderAsync(IReadOnlyList<string> arguments, int totalFrames, Action<double> progress, CancellationToken cancellationToken)
    {
        string path = Path ?? throw new FileNotFoundException("ffmpeg is not installed. Install it under Settings → Timelapse.");

        return await RunAsync(path, arguments, line =>
        {
            if (totalFrames > 0
                && line.StartsWith("frame=", StringComparison.Ordinal)
                && int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
                progress(Math.Clamp((double)frame / totalFrames, 0, 1));
        }, lowPriority: true, cancellationToken);
    }

    internal sealed record ProcessResult(int ExitCode, string Output);

    /// <summary>Runs a process, passing stdout lines to <paramref name="onLine"/>; Output holds the last lines of stdout and stderr.</summary>
    private static async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, Action<string>? onLine, bool lowPriority, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // ffmpeg reads stdin for interactive keys otherwise
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start " + file);
        process.StandardInput.Close();

        if (lowPriority)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.Idle; // nice 19: never slows down the camera or the web server
            }
            catch
            {
                // not permitted / already exited
            }
        }

        using var kill = cancellationToken.Register(() =>
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

        var tail = new Queue<string>();
        void Keep(string line)
        {
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > 6)
                    tail.Dequeue();
            }
        }

        var stdout = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (onLine != null)
                    onLine(line);
                else
                    Keep(line);
            }
        });
        var stderr = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
            {
                if (line.Length > 0)
                    Keep(line);
            }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(stdout, stderr);
        cancellationToken.ThrowIfCancellationRequested();

        lock (tail)
            return new ProcessResult(process.ExitCode, string.Join(" ", tail).Trim());
    }

    private string? ReadVersion(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo(path, "-hide_banner -version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            string? first = process.StandardOutput.ReadLine();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            // "ffmpeg version 7.1.5-0+deb13u1+rpt2 Copyright (c) …"
            var parts = (first ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 && parts[1] == "version" ? parts[2] : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the ffmpeg version");
            return null;
        }
    }

    internal static string? FindOnPath(string executable)
    {
        if (executable.Contains('/') || executable.Contains('\\'))
            return File.Exists(executable) ? executable : null;

        var names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable } : [executable];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(OperatingSystem.IsWindows() ? [] : ["/usr/local/bin", "/usr/bin", "/bin"]);

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                string candidate = System.IO.Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
