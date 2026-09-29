using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Adherence;

/// <summary>Phase 1 placeholder: the Adherence module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class AdherenceModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Adherence";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
