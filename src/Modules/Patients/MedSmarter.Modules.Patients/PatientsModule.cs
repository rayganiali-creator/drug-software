using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Patients;

/// <summary>Phase 1 placeholder: the Patients module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class PatientsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Patients";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
