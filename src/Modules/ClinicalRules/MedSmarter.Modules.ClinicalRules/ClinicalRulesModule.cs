using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>Phase 1 placeholder: the ClinicalRules module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class ClinicalRulesModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "ClinicalRules";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
