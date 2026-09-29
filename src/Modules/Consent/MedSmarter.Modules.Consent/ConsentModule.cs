using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Consent;

/// <summary>Phase 1 placeholder: the Consent module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class ConsentModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Consent";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
