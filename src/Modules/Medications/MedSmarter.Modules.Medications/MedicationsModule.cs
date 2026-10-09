using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Medications;

public sealed class MedicationsOptions
{
    public const string Section = "Medications";

    /// <summary>Loads FICTIONAL demo medications at startup. Refused outside Development/Testing.</summary>
    public bool SeedDemoData { get; set; }
}

public static class MedicationsGuard
{
    public static void EnsureSafe(string environmentName, MedicationsOptions options)
    {
        var devLike = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        if (options.SeedDemoData && !devLike)
        {
            throw new InvalidOperationException($"Medications:SeedDemoData is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start.");
        }
    }
}

/// <summary>Medication knowledge core (see docs/phase4). The store is in memory until the PostgreSQL repository is added.</summary>
public sealed class MedicationsModule : IModule
{
    public string Name => "Medications";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MedicationsOptions>(configuration.GetSection(MedicationsOptions.Section));
        services.TryAddSingleton<IClock, SystemClock>();
        // The PostgreSQL model is registered so migrations can be applied (`--migrate-and-exit`). The API itself still serves from the
        // in-memory repository until the PostgreSQL repository is written (docs/phase4/07).
        if (configuration.GetConnectionString("Postgres") is { Length: > 0 } cs)
        {
            services.AddDbContext<MedicationsDbContext>(o => o.UseNpgsql(cs, b => b.MigrationsHistoryTable("__ef_migrations_history", MedicationsDbContext.Schema)));
        }

        services.AddSingleton<InMemoryMedicationRepository>();
        services.AddSingleton<IMedicationRepository>(sp => sp.GetRequiredService<InMemoryMedicationRepository>());
        services.AddSingleton<MedicationReader>();
        services.AddSingleton<IMedicationService, MedicationService>();
        services.AddSingleton<IMedicationAdminService, MedicationAdminService>();
        services.AddSingleton<IKnowledgeSourceService, KnowledgeSourceService>();
        if (configuration.GetSection(MedicationsOptions.Section).GetValue<bool>(nameof(MedicationsOptions.SeedDemoData)))
        {
            services.AddSingleton<IDemoMedicationSeeder, DemoMedicationSeeder>();
        }
    }
}
