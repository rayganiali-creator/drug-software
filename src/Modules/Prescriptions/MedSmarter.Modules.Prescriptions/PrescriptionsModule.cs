using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Prescriptions;

/// <summary>Phase 1 placeholder: the Prescriptions module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class PrescriptionsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Prescriptions";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
