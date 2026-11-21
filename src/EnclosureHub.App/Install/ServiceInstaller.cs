using System.Diagnostics;
using System.Runtime.Versioning;

namespace EnclosureHub.App.Install;

/// <summary>
/// Handles the <c>--install</c> / <c>--uninstall</c> command-line options: creates or removes the
/// systemd unit for this executable. Run as root from the deployed location, e.g.
/// <c>sudo /opt/enclosurehub/enclosurehub --install</c>. The service runs as the user who
/// invoked sudo (<c>SUDO_USER</c>) unless <c>--user NAME</c> is given.
/// </summary>
internal static class ServiceInstaller
{
    public const string ServiceName = "enclosurehub";

    private static readonly string UnitPath = $"/etc/systemd/system/{ServiceName}.service";

    /// <summary>Returns true when the arguments were an install/uninstall command and the process should exit.</summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;

        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return true;
        }

        bool install = args.Contains("--install");
        bool uninstall = args.Contains("--uninstall");
        if (!install && !uninstall)
            return false;

        if (install && uninstall)
        {
            Console.Error.WriteLine("Specify either --install or --uninstall.");
            exitCode = 2;
            return true;
        }

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("--install/--uninstall create a systemd service and only work on Linux.");
            exitCode = 1;
            return true;
        }

        if (!RuntimeEnvironment.IsRoot)
        {
            Console.Error.WriteLine($"Run as root: sudo {RuntimeEnvironment.ExecutablePath} {(install ? "--install" : "--uninstall")}");
            exitCode = 1;
            return true;
        }

        try
        {
            exitCode = install ? Install(args) : Uninstall();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            exitCode = 1;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Enclosure Hub

            Usage:
              enclosurehub                       run in the foreground (or under systemd)
              sudo enclosurehub --install        install, enable and start the systemd service
                                                 [--user NAME]  run the service as NAME (default: $SUDO_USER)
              sudo enclosurehub --uninstall      stop, disable and remove the systemd service
                                                 (the application directory and its configuration are kept)

            Configuration: appsettings.json next to the executable, overridden by appsettings.Local.json.
            """);
    }

    [SupportedOSPlatform("linux")]
    private static int Install(string[] args)
    {
        string user = ResolveUser(args);
        string exePath = RuntimeEnvironment.ExecutablePath;
        string appDir = Path.GetDirectoryName(exePath)!;

        if (!UserExists(user))
        {
            Console.Error.WriteLine($"User '{user}' does not exist. Use --user NAME to choose the service account.");
            return 1;
        }

        // Make sure the binary is executable even if it was copied without the x bit.
        File.SetUnixFileMode(exePath, File.GetUnixFileMode(exePath)
            | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        string unit = $"""
            [Unit]
            Description=Enclosure Hub (camera, sensors, lights, fans for a 3D printer enclosure)
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=notify
            User={user}
            SupplementaryGroups=video dialout
            WorkingDirectory={appDir}
            ExecStart={exePath}
            Environment=DOTNET_ENVIRONMENT=Production
            Restart=always
            RestartSec=5

            [Install]
            WantedBy=multi-user.target

            """;

        File.WriteAllText(UnitPath, unit);
        Console.WriteLine($"Wrote {UnitPath} (User={user}, ExecStart={exePath})");

        Run("systemctl", "daemon-reload");
        Run("systemctl", "enable", ServiceName);
        Run("systemctl", "restart", ServiceName);
        Console.WriteLine($"Service '{ServiceName}' enabled and started.");

        string localConfig = Path.Combine(appDir, "appsettings.Local.json");
        if (!File.Exists(localConfig))
        {
            Console.WriteLine();
            Console.WriteLine($"Note: {localConfig} not found. Create it with your MQTT broker settings, e.g.");
            Console.WriteLine("""  { "Mqtt": { "Host": "192.168.1.10", "Username": "enclosurehub", "Password": "..." } }""");
            Console.WriteLine($"then run: sudo systemctl restart {ServiceName}");
        }

        string cameraExe = CameraExecutable(appDir);
        if (FindOnPath(cameraExe) == null)
        {
            // Only a warning: the service runs without a camera and keeps retrying.
            Console.WriteLine();
            Console.WriteLine($"Warning: '{cameraExe}' was not found. Install the camera tools with:");
            Console.WriteLine("  sudo apt install rpicam-apps-lite");
        }

        if (TimelapseEnabled(appDir) && FindOnPath("ffmpeg") == null)
        {
            // Required to render timelapse videos; frames are still recorded without it.
            Console.WriteLine();
            Console.WriteLine("Warning: 'ffmpeg' was not found; timelapse videos cannot be rendered. Install it with:");
            Console.WriteLine("  sudo apt install ffmpeg      (or: dashboard → Settings → Timelapse → Install)");
        }

        Console.WriteLine();
        Console.WriteLine($"Logs:   journalctl -u {ServiceName} -f");
        Console.WriteLine($"Status: systemctl status {ServiceName}");
        Console.WriteLine("Web:    http://<this-host>:8080/");

        return 0;
    }

    private static int Uninstall()
    {
        if (!File.Exists(UnitPath))
        {
            Console.WriteLine($"{UnitPath} does not exist; nothing to uninstall.");
            return 0;
        }

        // May fail if the unit is broken or already disabled; remove it anyway.
        Run("systemctl", ["disable", "--now", ServiceName], ignoreErrors: true);
        File.Delete(UnitPath);
        Run("systemctl", "daemon-reload");
        Console.WriteLine($"Service '{ServiceName}' stopped, disabled and {UnitPath} removed.");
        Console.WriteLine($"The application directory {Path.GetDirectoryName(RuntimeEnvironment.ExecutablePath)} was kept.");
        return 0;
    }

    private static string ResolveUser(string[] args)
    {
        int index = Array.IndexOf(args, "--user");
        if (index >= 0)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
                throw new ArgumentException("--user requires a user name.");
            return args[index + 1];
        }

        string? sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (!string.IsNullOrWhiteSpace(sudoUser) && sudoUser != "root")
            return sudoUser;

        throw new InvalidOperationException(
            "Could not determine the service user (SUDO_USER is not set). Run via sudo as a normal user or pass --user NAME.");
    }

    private static bool UserExists(string user)
    {
        try
        {
            return File.ReadLines("/etc/passwd").Any(line => line.StartsWith(user + ":", StringComparison.Ordinal));
        }
        catch
        {
            return true; // cannot check; let systemd complain
        }
    }

    /// <summary>Reads Modules:Camera:Executable from the config files so a custom executable name is honoured.</summary>
    private static string CameraExecutable(string appDir)
    {
        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(appDir)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Local.json", optional: true)
                .Build();

            return config["Modules:Camera:Executable"] ?? "rpicam-vid";
        }
        catch
        {
            return "rpicam-vid";
        }
    }

    private static bool TimelapseEnabled(string appDir)
    {
        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(appDir)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Local.json", optional: true)
                .Build();

            return config.GetValue("Modules:Timelapse:Enabled", true);
        }
        catch
        {
            return true;
        }
    }

    private static string? FindOnPath(string executable)
    {
        if (executable.Contains('/'))
            return File.Exists(executable) ? executable : null;

        // sudo resets PATH to secure_path; also look in the usual places explicitly.
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/usr/local/bin", "/usr/bin", "/bin"]);

        foreach (var dir in dirs)
        {
            string candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void Run(string file, params string[] arguments) => Run(file, arguments, ignoreErrors: false);

    private static void Run(string file, string[] arguments, bool ignoreErrors)
    {
        var startInfo = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {file}");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr.Trim()} {stdout.Trim()}".Trim());
    }

    private static class RuntimeEnvironment
    {
        public static bool IsRoot => Environment.IsPrivilegedProcess;

        public static string ExecutablePath
        {
            get
            {
                // Environment.ProcessPath is the native host (the published single executable or "dotnet" when
                // run via dotnet run). Prefer the apphost next to the assembly when it exists.
                string? processPath = Environment.ProcessPath;
                string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                string appHost = Path.Combine(baseDir, ServiceName);

                if (processPath != null && Path.GetDirectoryName(processPath) == baseDir)
                    return processPath;

                return appHost;
            }
        }
    }
}
