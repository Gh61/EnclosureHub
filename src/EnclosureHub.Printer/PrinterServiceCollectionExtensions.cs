using EnclosureHub.Core.Entities;
using EnclosureHub.Core.Printer;
using EnclosureHub.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnclosureHub.Printer;

public static class PrinterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the printer connection (Prusa over USB) from "Modules:Printer" unless <c>Enabled</c> is false:
    /// <see cref="IPrinterConnection"/> for other modules and the API, the settings section, and – when the printer
    /// is linked and <c>Entities</c> is on at startup – the printer entities.
    /// </summary>
    public static IServiceCollection AddPrusaPrinter(this IServiceCollection services, IConfigurationSection section)
    {
        if (!section.GetValue("Enabled", true))
            return services;

        services.Configure<PrinterOptions>(section);

        // The entity set is fixed at startup (see EntityRegistry)
        bool entities = section.GetValue("Linked", false) && section.GetValue("Entities", true);
        var printerEntities = new PrinterEntities(entities);
        services.AddSingleton(printerEntities);
        foreach (var entity in printerEntities.All)
            services.AddSingleton<Entity>(entity);

        services.AddSingleton<PrusaUsbPrinter>();
        services.AddHostedService(sp => sp.GetRequiredService<PrusaUsbPrinter>());
        services.AddSingleton<IPrinterConnection>(sp => sp.GetRequiredService<PrusaUsbPrinter>());

        services.AddSingleton<ISettingsSection>(sp => new PrinterSettingsSection(
            sp.GetRequiredService<IOptionsMonitor<PrinterOptions>>(),
            sp.GetRequiredService<IPrinterConnection>(),
            sp.GetRequiredService<ISettingsStore>(),
            section.Path,
            entities));

        return services;
    }
}
