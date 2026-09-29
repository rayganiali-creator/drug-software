using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.KnowledgeBase;

/// <summary>Phase 1 placeholder: the KnowledgeBase module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class KnowledgeBaseModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "KnowledgeBase";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
