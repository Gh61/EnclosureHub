using System.Globalization;
using EnclosureHub.Core.Entities;
using Microsoft.Extensions.Options;

namespace EnclosureHub.App.Modules.SystemSensors;

/// <summary>Bound from "Modules:SystemSensors".</summary>
public sealed class SystemSensorsOptions
{
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>
/// Health of the controller itself: CPU temperature, Wi-Fi signal strength and CPU load. The load is sampled every
/// second (/proc/stat) for the dashboard graph (<see cref="SensorEntity.Live"/>); the entity value – what goes to
/// Home Assistant – is the average over <see cref="SystemSensorsOptions.IntervalSeconds"/>.
/// </summary>
public sealed class SystemSensorsService : BackgroundService
{
    private const string CpuTemperaturePath = "/sys/class/thermal/thermal_zone0/temp";
    private const string WirelessStatsPath = "/proc/net/wireless";
    private const string CpuStatPath = "/proc/stat";

    private readonly SystemSensorsOptions _options;

    // /proc/stat counters of the previous sample and the samples of the current interval
    private (long Idle, long Total)? _lastStat;
    private double _loadSum;
    private int _loadSamples;
    private double _liveLoad = double.NaN; // NaN = no sample yet / not available

    public SystemSensorsService(IOptions<SystemSensorsOptions> options)
    {
        _options = options.Value;
        CpuLoad = new SensorEntity("cpu_load", "CPU load")
        {
            Unit = "%",
            StateClass = "measurement",
            IsDiagnostic = true,
            Icon = "mdi:chip",
            Live = () => double.IsNaN(Volatile.Read(ref _liveLoad)) ? null : Volatile.Read(ref _liveLoad),
        };
    }

    /// <summary>Average CPU load over the interval (all cores); the 1 s sample is available as the live value.</summary>
    public SensorEntity CpuLoad { get; }

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
        SampleCpuLoad();

        int interval = Math.Max(1, _options.IntervalSeconds);
        int tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                SampleCpuLoad();
                if (++tick % interval == 0)
                    Update();
            }
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
        CpuLoad.SetValue(_loadSamples > 0 ? Math.Round(_loadSum / _loadSamples) : null);
        _loadSum = 0;
        _loadSamples = 0;
    }

    /// <summary>One second of CPU load from the "cpu" line of /proc/stat (null where there is none, e.g. on Windows).</summary>
    private void SampleCpuLoad()
    {
        (long Idle, long Total)? stat;
        try
        {
            stat = ParseCpuStat(File.ReadAllText(CpuStatPath));
        }
        catch
        {
            stat = null;
        }

        double? load = null;
        if (stat is { } now && _lastStat is { } last && now.Total > last.Total)
            load = Math.Round(100.0 * (1 - (double)(now.Idle - last.Idle) / (now.Total - last.Total)), 1);

        _lastStat = stat;
        Volatile.Write(ref _liveLoad, load ?? double.NaN);
        if (load is { } value)
        {
            _loadSum += value;
            _loadSamples++;
        }
    }

    /// <summary>Idle (idle + iowait) and total jiffies from the first line of /proc/stat; null when it is not there.</summary>
    internal static (long Idle, long Total)? ParseCpuStat(string content)
    {
        // "cpu  user nice system idle iowait irq softirq steal guest guest_nice"
        foreach (string line in content.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
                continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return null;

            long total = 0, idle = 0;
            for (int i = 1; i < parts.Length && i <= 8; i++)
            {
                if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    return null;
                total += value;
                if (i == 4 || i == 5)
                    idle += value;
            }

            return (idle, total);
        }

        return null;
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
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<SystemSensorsService>().CpuLoad);

        return services;
    }
}
