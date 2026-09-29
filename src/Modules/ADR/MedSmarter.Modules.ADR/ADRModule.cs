using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.ADR;

/// <summary>Phase 1 placeholder: the ADR module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class ADRModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "ADR";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
