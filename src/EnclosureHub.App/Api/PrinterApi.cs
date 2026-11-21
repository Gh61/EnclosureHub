using EnclosureHub.Core.Printer;

namespace EnclosureHub.App.Api;

/// <summary>
/// Printer connection (only mapped when the printer module is present):
///   GET  /api/printer               – link, connection, print state, temperatures, stored printer information
///   GET  /api/printer/log?after=N   – communication log entries with a sequence number greater than N (last 500 kept)
///   POST /api/printer/info          – ask the printer who it is (M115) now; also refreshes temperatures and print state
/// </summary>
internal static class PrinterApi
{
    public static IEndpointRouteBuilder MapPrinterApi(this IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IPrinterConnection)))
            return app;

        var group = app.MapGroup("/api/printer");

        group.MapGet("/", (IPrinterConnection printer) => Results.Json(printer.GetStatus(), ApiJson.Options));

        group.MapGet("/log", (long? after, IPrinterConnection printer) =>
            Results.Json(printer.GetLog(after ?? 0), ApiJson.Options));

        group.MapPost("/info", async (IPrinterConnection printer, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Json(await printer.ReadInfoAsync(cancellationToken), ApiJson.Options);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.Json(new { error = ex.Message }, ApiJson.Options, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return app;
    }
}
