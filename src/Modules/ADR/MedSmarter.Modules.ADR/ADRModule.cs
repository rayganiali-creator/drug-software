using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.ADR.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.ADR;

/// <summary>
/// Phase 9: internal adverse-event records and their human review. Not a regulatory report, not a causality or seriousness assessment, and nothing here
/// contacts any external party (no outbound adapter exists in this module).
/// </summary>
public sealed class ADRModule : IModule
{
    public string Name => "ADR";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddModuleDatabase<AdrDbContext>(configuration, AdrDbContext.Schema);
        services.AddSingleton<IAdverseEventService, AdverseEventService>();
    }
}
