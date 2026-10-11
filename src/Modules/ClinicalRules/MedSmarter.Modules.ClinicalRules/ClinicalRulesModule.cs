using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>
/// The deterministic clinical safety engine: versioned rules with a two-person review, patient-specific medication assessments, and guidance for findings.
/// No model decides anything here. Demonstration rules exist only where the environment allows them, and are never approved knowledge.
/// </summary>
public sealed class ClinicalRulesModule : IModule
{
    public string Name => "ClinicalRules";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.Configure<ClinicalRulesOptions>(configuration.GetSection(ClinicalRulesOptions.Section));
        services.AddModuleDatabase<ClinicalRulesDbContext>(configuration, ClinicalRulesDbContext.Schema);
        services.AddSingleton<RuleStore>();
        services.AddSingleton<IClinicalRuleCatalog, RuleCatalogService>();
        services.AddSingleton<IClinicalSafetyService, ClinicalSafetyService>();
        services.Configure<MonitoringOptions>(configuration.GetSection(MonitoringOptions.Section));
        services.AddSingleton<IMonitoringService, MonitoringService>();
        var monitoring = configuration.GetSection(MonitoringOptions.Section).Get<MonitoringOptions>() ?? new MonitoringOptions();
        if (monitoring.WorkerEnabled)
        {
            // Local, in-process, bounded. Off by default; this is a scheduled re-check, not continuous monitoring.
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => new PeriodicWorker("monitoring", TimeSpan.FromSeconds(monitoring.IntervalSeconds),
                async ct => await sp.GetRequiredService<IMonitoringService>().RunOnceAsync(ct), sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("MonitoringWorker")));
        }
        if (configuration.GetSection(ClinicalRulesOptions.Section).GetValue<bool>("SeedDemoRules"))
        {
            services.AddSingleton<IDemoRuleSeeder, DemoRuleSeeder>();
        }
    }
}
