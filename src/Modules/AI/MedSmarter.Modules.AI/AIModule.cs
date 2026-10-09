using MedSmarter.Modules.AI.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.AI;

/// <summary>AI provider abstraction and the source-grounded assistant (see docs/phase4/08-ai-provider-architecture.md).</summary>
public sealed class AIModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "AI";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.AddSingleton<MockAIProvider>();
        services.AddSingleton<DisabledAIProvider>();
        services.AddSingleton<LocalAIProvider>(sp => new LocalAIProvider(sp.GetService<ILocalModelRuntime>()));
        services.AddHttpClient<ExternalAIProvider>();
        services.AddSingleton<ConfiguredAIProvider>();
        services.AddSingleton<IAIProvider>(sp => sp.GetRequiredService<ConfiguredAIProvider>());
        services.AddSingleton<IAIAssistantService, AssistantService>();
    }
}
