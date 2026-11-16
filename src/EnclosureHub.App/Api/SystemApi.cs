using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using EnclosureHub.Core;
using Microsoft.Extensions.Options;

namespace EnclosureHub.App.Api;

/// <summary>
///   GET  /api/system          – device name, version, host information, uptime
///   POST /api/system/restart  – graceful shutdown; systemd (Restart=always) starts the service again
/// </summary>
internal static class SystemApi
{
    private static readonly DateTime StartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public static IEndpointRouteBuilder MapSystemApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system");

        group.MapGet("/", (IOptions<DeviceOptions> device) => Results.Json(new
        {
            device = device.Value.Name,
            deviceId = device.Value.Id,
            version = Version(),
            hostname = Environment.MachineName,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            runtime = RuntimeInformation.FrameworkDescription,
            startedUtc = StartedUtc,
            uptimeSeconds = Math.Round((DateTime.UtcNow - StartedUtc).TotalSeconds),
            workingSetBytes = Environment.WorkingSet,
            managedMemoryBytes = GC.GetTotalMemory(false),
            processorCount = Environment.ProcessorCount,
            underSystemd = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")),
        }, ApiJson.Options));

        group.MapPost("/restart", (IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory) =>
        {
            loggerFactory.CreateLogger("EnclosureHub.App").LogWarning("Restart requested via the API");

            // Let the response go out first, then shut down; systemd restarts the service after RestartSec
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                lifetime.StopApplication();
            });

            return Results.Accepted(value: new { message = "Shutting down; the service restarts in a few seconds." });
        });

        return app;
    }

    private static string Version()
    {
        var assembly = typeof(SystemApi).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "?";
    }
}
