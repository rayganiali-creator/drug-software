using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Audit;

/// <summary>Phase 1 placeholder: the Audit module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class AuditModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Audit";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
