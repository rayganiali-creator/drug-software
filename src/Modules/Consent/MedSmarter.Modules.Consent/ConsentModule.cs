using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Consent.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Consent;

/// <summary>Consent: who may access which of a person's data, for what purpose, until when (docs/phase3/05-consent.md).</summary>
public sealed class ConsentModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Consent";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (PersistenceSettings.UsePostgres(configuration))
        {
            services.AddModuleDatabase<ConsentDbContext>(configuration, ConsentDbContext.Schema);
            services.AddSingleton<IConsentStore, PostgresConsentStore>();
        }
        else
        {
            services.AddSingleton<IConsentStore, InMemoryConsentStore>();
        }

        services.AddSingleton<ConsentService>();
        services.AddSingleton<IConsentService>(sp => sp.GetRequiredService<ConsentService>());
        services.AddSingleton<IConsentEvaluator>(sp => sp.GetRequiredService<ConsentService>());
        services.AddSingleton<IPurposeConsentEvaluator>(sp => sp.GetRequiredService<ConsentService>());
        services.AddSingleton<IDemoConsentSeeder>(sp => sp.GetRequiredService<ConsentService>());
    }
}
