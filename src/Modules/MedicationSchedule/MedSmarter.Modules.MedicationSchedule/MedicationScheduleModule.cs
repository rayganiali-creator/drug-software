using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.MedicationSchedule;

/// <summary>Phase 1 placeholder: the MedicationSchedule module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class MedicationScheduleModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "MedicationSchedule";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
