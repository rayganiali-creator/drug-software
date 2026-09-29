using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Pharmacists;

/// <summary>Phase 1 placeholder: the Pharmacists module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class PharmacistsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Pharmacists";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
