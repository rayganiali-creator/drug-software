using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Pharmacies;

/// <summary>Phase 1 placeholder: the Pharmacies module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class PharmaciesModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Pharmacies";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
