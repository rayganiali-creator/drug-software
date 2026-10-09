using MedSmarter.Modules.Integrations.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Integrations;

/// <summary>External-system adapters. Phase 4 adds only the insurance CONTRACTS plus a fictional mock (no real connection).</summary>
public sealed class IntegrationsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Integrations";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<MedSmarter.BuildingBlocks.IClock, MedSmarter.BuildingBlocks.SystemClock>();
        services.AddSingleton<InsuranceStore>();
        services.AddSingleton<IInsuranceIntegrationService, InsuranceIntegrationService>();
        // Only the mock exists, and only in development configuration. A real insurer = a new IInsuranceProvider adapter.
        if (configuration.GetValue<bool>("Integrations:Insurance:EnableMock"))
        {
            services.AddSingleton<IInsuranceProvider>(new MockInsuranceProvider());
        }
    }
}
