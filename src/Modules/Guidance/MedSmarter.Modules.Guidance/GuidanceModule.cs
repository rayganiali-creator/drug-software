using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Guidance;

/// <summary>The patient-message contract: six-part structure, tone policy, templates (fa/en), levels and life cycle. The engine that triggers messages is Phase 7.</summary>
public sealed class GuidanceModule : IModule
{
    public string Name => "Guidance";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddModuleDatabase<GuidanceDbContext>(configuration, GuidanceDbContext.Schema);
        services.AddSingleton<IGuidanceComposer, GuidanceComposer>();
        services.AddSingleton<IGuidanceService, GuidanceService>();
        if (configuration.GetSection("Guidance").GetValue<bool>("SeedDemoData"))
        {
            services.AddSingleton<IDemoGuidanceSeeder, DemoGuidanceSeeder>();
        }
    }
}
