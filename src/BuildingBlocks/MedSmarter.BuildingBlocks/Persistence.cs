using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.BuildingBlocks;

public enum PersistenceProvider
{
    /// <summary>PostgreSQL (the default and the only provider allowed outside Development/Testing).</summary>
    Postgres,

    /// <summary>Volatile stores for automated tests and a database-free developer session. Data is lost on restart.</summary>
    InMemory,
}

/// <summary>
/// One switch for every module that stores data: <c>Persistence:Provider</c>. Postgres is the default, so forgetting the setting
/// can never silently put patient data in memory. See docs/phase5.
/// </summary>
public static class PersistenceSettings
{
    public const string Section = "Persistence";

    public static PersistenceProvider ProviderOf(IConfiguration configuration) =>
        Enum.TryParse<PersistenceProvider>(configuration[$"{Section}:Provider"], true, out var p) ? p : PersistenceProvider.Postgres;

    public static bool UsePostgres(IConfiguration configuration) => ProviderOf(configuration) == PersistenceProvider.Postgres;

    /// <summary>Applying migrations at startup is a developer convenience; production applies them as a controlled deployment step.</summary>
    public static bool MigrateOnStartup(IConfiguration configuration) => configuration.GetValue<bool>($"{Section}:MigrateOnStartup");

    public static bool IsDevLike(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    /// <summary>Refuses to start with volatile storage or startup migrations outside Development/Testing.</summary>
    public static void EnsureSafe(string environmentName, IConfiguration configuration)
    {
        if (IsDevLike(environmentName))
        {
            return;
        }

        if (ProviderOf(configuration) == PersistenceProvider.InMemory)
        {
            throw new InvalidOperationException($"Persistence:Provider=InMemory is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start: patient data, consents and audit must be stored durably.");
        }

        if (MigrateOnStartup(configuration))
        {
            throw new InvalidOperationException($"Persistence:MigrateOnStartup is only allowed in Development/Testing (environment is '{environmentName}'). Apply migrations with --migrate-and-exit.");
        }
    }

    /// <summary>
    /// Registers the module's DbContext factory and its migrator. With <see cref="PersistenceProvider.InMemory"/> the EF Core in-memory
    /// provider is used (tests and database-free development only; constraints are not enforced there, PostgreSQL tests cover them).
    /// </summary>
    public static IServiceCollection AddModuleDatabase<TContext>(this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        if (!UsePostgres(configuration))
        {
            var name = $"{typeof(TContext).Name}-{Guid.NewGuid():N}"; // one isolated store per application instance
            services.AddDbContextFactory<TContext>(o => o.UseInMemoryDatabase(name).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
            return services;
        }

        var connection = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is required when Persistence:Provider is Postgres (set Persistence:Provider=InMemory only for tests or a database-free development session).");
        }

        services.AddDbContextFactory<TContext>(o => o.UseNpgsql(connection, b => b.MigrationsHistoryTable("__ef_migrations_history", schema)));
        services.AddSingleton<IDatabaseMigrator, EfMigrator<TContext>>();
        return services;
    }
}

/// <summary>Applies the migrations of one module database. The Host runs all of them for <c>--migrate-and-exit</c>.</summary>
public interface IDatabaseMigrator
{
    string Name { get; }

    Task MigrateAsync(CancellationToken ct);
}

public sealed class EfMigrator<TContext>(IDbContextFactory<TContext> factory) : IDatabaseMigrator where TContext : DbContext
{
    public string Name => typeof(TContext).Name;

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
