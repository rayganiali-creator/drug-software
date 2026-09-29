using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Medications;

/// <summary>Phase 1 placeholder: the Medications module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class MedicationsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Medications";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
