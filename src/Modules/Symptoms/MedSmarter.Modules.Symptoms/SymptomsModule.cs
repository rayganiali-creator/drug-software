using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Symptoms;

/// <summary>Phase 1 placeholder: the Symptoms module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class SymptomsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Symptoms";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
