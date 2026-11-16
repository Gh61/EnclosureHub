using System.Globalization;
using EnclosureHub.Core.Entities;
using Microsoft.Extensions.Options;

namespace EnclosureHub.App.Modules.SystemSensors;

/// <summary>Bound from "Modules:SystemSensors".</summary>
public sealed class SystemSensorsOptions
{
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>Health of the controller itself (currently CPU temperature).</summary>
public sealed class SystemSensorsService : BackgroundService
{
    private const string CpuTemperaturePath = "/sys/class/thermal/thermal_zone0/temp";

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CpuTemperature.SetValue(ReadCpuTemperature());

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                CpuTemperature.SetValue(ReadCpuTemperature());
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
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
}

public static class SystemSensorsServiceCollectionExtensions
{
    public static IServiceCollection AddSystemSensors(this IServiceCollection services, IConfiguration section)
    {
        services.Configure<SystemSensorsOptions>(section);

        services.AddSingleton<SystemSensorsService>();
        services.AddHostedService(sp => sp.GetRequiredService<SystemSensorsService>());
        services.AddSingleton<Entity>(sp => sp.GetRequiredService<SystemSensorsService>().CpuTemperature);

        return services;
    }
}
