using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Identity;

/// <summary>Phase 1 placeholder: the Identity module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class IdentityModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
