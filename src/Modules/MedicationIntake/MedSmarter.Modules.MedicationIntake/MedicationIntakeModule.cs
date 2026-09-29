using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.MedicationIntake;

/// <summary>Phase 1 placeholder: the MedicationIntake module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class MedicationIntakeModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "MedicationIntake";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
