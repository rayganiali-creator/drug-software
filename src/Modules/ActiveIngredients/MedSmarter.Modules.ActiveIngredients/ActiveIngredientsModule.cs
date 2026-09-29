using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.ActiveIngredients;

/// <summary>Phase 1 placeholder: the ActiveIngredients module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class ActiveIngredientsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "ActiveIngredients";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
