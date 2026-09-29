using MedSmarter.Modules.Consent.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Consent;

/// <summary>Consent: who may access which of a person's data, for what purpose, until when (docs/phase3/05-consent.md).</summary>
public sealed class ConsentModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Consent";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConsentStore, InMemoryConsentStore>();
        services.AddSingleton<ConsentService>();
        services.AddSingleton<IConsentService>(sp => sp.GetRequiredService<ConsentService>());
        services.AddSingleton<IConsentEvaluator>(sp => sp.GetRequiredService<ConsentService>());
        services.AddSingleton<IDemoConsentSeeder>(sp => sp.GetRequiredService<ConsentService>());
    }
}
