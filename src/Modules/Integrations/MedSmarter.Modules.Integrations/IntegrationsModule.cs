using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Integrations;

/// <summary>Phase 1 placeholder: the Integrations module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class IntegrationsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Integrations";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
