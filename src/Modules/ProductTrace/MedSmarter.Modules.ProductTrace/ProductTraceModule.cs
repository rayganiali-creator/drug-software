using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.ProductTrace;

/// <summary>Batch/lot tracking of products patients received and de-identified safety reports towards manufacturers (docs/phase5).</summary>
public sealed class ProductTraceModule : IModule
{
    public string Name => "ProductTrace";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ManufacturerReportsOptions>(configuration.GetSection(ManufacturerReportsOptions.Section));
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddModuleDatabase<ProductTraceDbContext>(configuration, ProductTraceDbContext.Schema);
        services.AddSingleton<IProductTraceService, ProductTraceService>();
        services.AddSingleton<IProductScanProvider, NotAvailableScanProvider>();
        services.AddSingleton<ReportPayloadBuilder>();
        var mock = string.Equals(configuration.GetSection(ManufacturerReportsOptions.Section)[nameof(ManufacturerReportsOptions.Provider)], "Mock", StringComparison.OrdinalIgnoreCase);
        if (mock)
        {
            services.AddSingleton<IManufacturerReportProvider, MockManufacturerReportProvider>();
            services.AddSingleton(sp => (MockManufacturerReportProvider)sp.GetRequiredService<IManufacturerReportProvider>());
        }
        else
        {
            services.AddSingleton<IManufacturerReportProvider, DisabledManufacturerReportProvider>();
        }

        services.AddSingleton<ManufacturerReportService>();
        services.AddSingleton<IManufacturerReportService>(sp => sp.GetRequiredService<ManufacturerReportService>());
        services.AddSingleton<IManufacturerReportOutbox>(sp => sp.GetRequiredService<ManufacturerReportService>());
    }
}
