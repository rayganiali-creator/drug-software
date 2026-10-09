using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Audit;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Integrations;
using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Medications.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MedSmarter.Knowledge.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
}

/// <summary>
/// A throw-away LOCAL PostgreSQL database per test, cloned from a migrated template. Active only when MEDSMARTER_PG_TEST holds a
/// connection string to a local scratch server; otherwise every test runs on the in-memory stores (no database, no network).
/// </summary>
internal static class PgTemplate
{
    private static readonly Lazy<string?> Template = new(Build);

    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEDSMARTER_PG_TEST"));

    private static NpgsqlConnectionStringBuilder Admin() => new(Environment.GetEnvironmentVariable("MEDSMARTER_PG_TEST"));

    private static void Execute(string sql)
    {
        using var conn = new NpgsqlConnection(Admin().ConnectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    private static string? Build()
    {
        if (!Enabled)
        {
            return null;
        }

        var name = "tpl_" + Guid.NewGuid().ToString("N")[..12];
        Execute($"CREATE DATABASE {name}");
        var cs = Admin();
        cs.Database = name;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Persistence:Provider"] = "Postgres", ["ConnectionStrings:Postgres"] = cs.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        KEnv.RegisterModules(services, config);
        using (var sp = services.BuildServiceProvider())
        {
            foreach (var migrator in sp.GetServices<MedSmarter.BuildingBlocks.IDatabaseMigrator>())
            {
                migrator.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        NpgsqlConnection.ClearAllPools(); // a template must have no open sessions
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { NpgsqlConnection.ClearAllPools(); Execute($"DROP DATABASE IF EXISTS {name} WITH (FORCE)"); } catch (Exception) { /* best effort */ } };
        return name;
    }

    /// <summary>Returns the connection string of a fresh copy and the action that drops it.</summary>
    public static (string ConnectionString, Action Drop) NewDatabase()
    {
        var name = "t_" + Guid.NewGuid().ToString("N")[..16];
        Execute($"CREATE DATABASE {name} TEMPLATE {Template.Value}");
        var cs = Admin();
        cs.Database = name;
        return (cs.ConnectionString, () => Execute($"DROP DATABASE IF EXISTS {name} WITH (FORCE)"));
    }
}

/// <summary>Medications + AI + Integrations + Audit wired with no network and no keys: in memory, or on a scratch PostgreSQL when MEDSMARTER_PG_TEST is set.</summary>
public sealed class KEnv : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly Action? _drop;
    private readonly Dictionary<string, string?>? _settings;

    internal static void RegisterModules(IServiceCollection services, IConfiguration config)
    {
        new AuditModule().Register(services, config);
        new MedicationsModule().Register(services, config);
        new AIModule().Register(services, config);
        new IntegrationsModule().Register(services, config);
    }

    /// <summary>The scratch database of this environment (null when running in memory).</summary>
    public string? ConnectionString { get; }

    public KEnv(bool seed = true, Dictionary<string, string?>? settings = null, string? reuseDatabase = null)
    {
        _settings = settings;
        var map = new Dictionary<string, string?> { ["Medications:SeedDemoData"] = seed ? "true" : "false", ["Ai:Provider"] = "Mock", ["Persistence:Provider"] = "InMemory" };
        if (PgTemplate.Enabled)
        {
            var (cs, drop) = reuseDatabase is null ? PgTemplate.NewDatabase() : (reuseDatabase, (Action?)null);
            _drop = drop;
            ConnectionString = cs;
            map["Persistence:Provider"] = "Postgres";
            map["ConnectionStrings:Postgres"] = cs;
        }

        foreach (var (k, v) in settings ?? [])
        {
            map[k] = v;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(map).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        RegisterModules(services, config);
        _sp = services.BuildServiceProvider();
        if (seed)
        {
            _sp.GetRequiredService<IDemoMedicationSeeder>().SeedAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>A second application instance over the same database (a restart): only possible on PostgreSQL.</summary>
    public KEnv Restart() => ConnectionString is null
        ? throw new InvalidOperationException("Restart needs PostgreSQL (MEDSMARTER_PG_TEST).")
        : new KEnv(false, _settings, ConnectionString);

    public FakeClock Clock { get; } = new();
    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();
    public IMedicationService Meds => Get<IMedicationService>();
    public IMedicationAdminService Admin => Get<IMedicationAdminService>();
    public IKnowledgeSourceService Sources => Get<IKnowledgeSourceService>();
    public IAuditReader Audit => Get<IAuditReader>();
    public static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    /// <summary>A second person: whoever edited a medication may not validate it, so validation tests use this one.</summary>
    public static readonly Guid Reviewer = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task<MedicationSummaryDto> One(string q) => (await Meds.SearchAsync(new MedicationSearchQuery(q))).Items[0];
    public async Task<IReadOnlyList<string>> Names(string q, int limit = 20, int offset = 0) =>
        [.. (await Meds.SearchAsync(new MedicationSearchQuery(q, limit, offset))).Items.Select(i => i.Name.En ?? i.Name.Fa ?? "")];

    /// <summary>A real (non-demo) source + revision for tests of the "verified" path. FICTIONAL content only.</summary>
    public async Task<(KnowledgeSourceDto Source, KnowledgeRevisionDto Revision)> RealSource(bool redistribution = true, bool validatedRevision = true)
    {
        var s = (await Sources.RegisterSourceAsync(Actor, new NewKnowledgeSource("Test formulary (fictional) " + Guid.NewGuid().ToString("N")[..8], "Test publisher", SourceType.Publication, "https://example.invalid/formulary", "t-1", "Test licence", redistribution, null), "test", null)).Value!;
        var r = (await Sources.AddRevisionAsync(Actor, new NewRevision(s.Id, "r1", null), "test", null)).Value!;
        if (validatedRevision)
        {
            r = (await Sources.SetRevisionStatusAsync(Actor, r.Id, RevisionStatus.Validated, "test", null)).Value!;
        }

        return (s, r);
    }

    public async Task<Guid> Ingredient(string en, string? fa = null) =>
        (await Admin.CreateIngredientAsync(Actor, new NewIngredient(new LocalizedText(en, fa), null, []))).Value!.Id;

    public MedicationDraft Draft(Guid ingredient, Guid revision, bool demo = false, string name = "Testomed", IReadOnlyList<NewIdentifier>? ids = null, IReadOnlyList<NewStatement>? statements = null) =>
        new(new LocalizedText(name, null), null, null, "tablet", ["oral"], [], [new NewIngredientRef(ingredient, 5, "mg", null)], [], ids ?? [], statements ?? [], demo);

    public void Dispose()
    {
        _sp.Dispose();
        _drop?.Invoke();
    }
}
