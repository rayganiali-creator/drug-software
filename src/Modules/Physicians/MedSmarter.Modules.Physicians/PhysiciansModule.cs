using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Physicians;

/// <summary>Phase 1 placeholder: the Physicians module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class PhysiciansModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Physicians";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
