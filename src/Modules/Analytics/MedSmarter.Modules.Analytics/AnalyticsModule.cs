using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Analytics;

/// <summary>Phase 1 placeholder: the Analytics module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class AnalyticsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Analytics";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
