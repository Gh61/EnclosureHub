using System.Globalization;
using EnclosureHub.Core.Entities;
using Microsoft.Extensions.Options;

namespace EnclosureHub.App.Modules.SystemSensors;

/// <summary>Bound from "Modules:SystemSensors".</summary>
public sealed class SystemSensorsOptions
{
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>Health of the controller itself: CPU temperature and Wi-Fi signal strength.</summary>
public sealed class SystemSensorsService : BackgroundService
{
    private const string CpuTemperaturePath = "/sys/class/thermal/thermal_zone0/temp";
    private const string WirelessStatsPath = "/proc/net/wireless";

    private readonly SystemSensorsOptions _options;

    public SystemSensorsService(IOptions<SystemSensorsOptions> options)
    {
        _options = options.Value;
    }

    public SensorEntity CpuTemperature { get; } = new("cpu_temperature", "CPU temperature")
    {
        Unit = "°C",
        DeviceClass = "temperature",
        StateClass = "measurement",
        IsDiagnostic = true,
    };

    /// <summary>Signal level of the connected Wi-Fi network in dBm; 0 when there is no Wi-Fi link.</summary>
    public SensorEntity WifiSignal { get; } = new("wifi_signal", "Wi-Fi signal")
    {
        Unit = "dBm",
        DeviceClass = "signal_strength",
        StateClass = "measurement",
        IsDiagnostic = true,
        Icon = "mdi:wifi",
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Update();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                Update();
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void Update()
    {
        CpuTemperature.SetValue(ReadCpuTemperature());
        WifiSignal.SetValue(ReadWifiSignal());
    }

    private static double? ReadCpuTemperature()
    {
        try
        {
            string raw = File.ReadAllText(CpuTemperaturePath).Trim();
            return Math.Round(int.Parse(raw, CultureInfo.InvariantCulture) / 1000.0, 1);
        }
        catch
        {
            return null; // not on a Raspberry Pi (e.g. debugging on Windows)
        }
    }

    private static double ReadWifiSignal()
    {
        try
        {
            return ParseWirelessLevel(File.ReadAllText(WirelessStatsPath));
        }
        catch
        {
            return 0; // no /proc/net/wireless (not Linux) = no Wi-Fi
        }
    }

    /// <summary>
    /// Parses the "level" column of /proc/net/wireless (dBm on current drivers) for the first wireless
    /// interface. Returns 0 when there is no wireless interface or no link.
    /// </summary>
    internal static double ParseWirelessLevel(string content)
    {
        // Two header lines, then e.g. "  wlan0: 0000   52.  -58.  -256        0      0      0      0      0        0"
        foreach (string line in content.Split('\n').Skip(2))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !parts[0].EndsWith(':'))
                continue;

            if (double.TryParse(parts[3].TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double level))
                return Math.Round(level);
        }

        return 0;
    }
}

public static class SystemSensorsServiceCollectionExtensions
{
    public static IServiceCollection AddSystemSensors(this IServiceCollection services, IConfiguration section)
    {
        services.Configure<SystemSensorsOptions>(section);

        services.AddSingleton<SystemSensorsService>();
        services.AddHostedService(sp => sp.GetRequiredService<SystemSensorsService>());
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<SystemSensorsService>().CpuTemperature);
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<SystemSensorsService>().WifiSignal);

        return services;
    }
}
