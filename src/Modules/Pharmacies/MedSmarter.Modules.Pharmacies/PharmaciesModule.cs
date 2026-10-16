using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Pharmacies.Contracts;
using MedSmarter.Modules.Pharmacies.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Pharmacies;

/// <summary>
/// Phase 10: internal pharmacy safety records (dispensing errors, near misses, product concerns). No outbound adapter, no queue and no HTTP client exists in this module;
/// it cannot send anything anywhere.
/// </summary>
public sealed class PharmaciesModule : IModule
{
    public string Name => "Pharmacies";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddModuleDatabase<PharmacyDbContext>(configuration, PharmacyDbContext.Schema);
        services.AddSingleton<IPharmacySafetyService, PharmacySafetyService>();
    }
}
