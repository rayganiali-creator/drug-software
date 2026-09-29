using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.AI;

/// <summary>Phase 1 placeholder: the AI module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class AIModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "AI";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
