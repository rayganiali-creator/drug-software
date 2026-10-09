using System.Security.Cryptography;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.Audit;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Guidance;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace;
using MedSmarter.Modules.ProductTrace.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MedSmarter.Patients.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PgTemplate.Enabled)
        {
            Skip = "Set MEDSMARTER_PG_TEST to a connection string of a throw-away LOCAL PostgreSQL to run these (no database is needed otherwise).";
        }
    }
}

/// <summary>A scratch LOCAL PostgreSQL database per test, cloned from a migrated template (only when MEDSMARTER_PG_TEST is set).</summary>
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

        var name = "ptpl_" + Guid.NewGuid().ToString("N")[..12];
        Execute($"CREATE DATABASE {name}");
        var cs = Admin();
        cs.Database = name;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Persistence:Provider"] = "Postgres", ["ConnectionStrings:Postgres"] = cs.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        PEnv.RegisterModules(services, config);
        using (var sp = services.BuildServiceProvider())
        {
            foreach (var migrator in sp.GetServices<IDatabaseMigrator>())
            {
                migrator.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        NpgsqlConnection.ClearAllPools();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { NpgsqlConnection.ClearAllPools(); foreach (var left in Leftovers) { DropQuietly(left); } Execute($"DROP DATABASE IF EXISTS {name} WITH (FORCE)"); } catch (Exception) { /* best effort */ } };
        return name;
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<string> Leftovers = [];

    /// <summary>
    /// Cleanup must never fail a test. PostgreSQL can answer "permission denied to terminate process" to DROP ... WITH (FORCE) when a
    /// background worker (e.g. autovacuum, owned by another role) is attached to the scratch database at that very moment; retry, and
    /// if it still fails remember the name and drop it when the test process exits.
    /// </summary>
    private static void DropQuietly(string name)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Execute($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
                return;
            }
            catch (PostgresException)
            {
                Thread.Sleep(200);
            }
        }

        Leftovers.Add(name);
    }

    public static (string ConnectionString, Action Drop) NewDatabase()
    {
        var name = "pt_" + Guid.NewGuid().ToString("N")[..16];
        Execute($"CREATE DATABASE {name} TEMPLATE {Template.Value}");
        var cs = Admin();
        cs.Database = name;
        return (cs.ConnectionString, () => DropQuietly(name));
    }
}

/// <summary>
/// All Phase 5 modules wired with a fake clock and the fictional demo identities: in memory by default (EF in-memory provider, no database,
/// no network, no keys), on a scratch PostgreSQL when MEDSMARTER_PG_TEST is set.
/// </summary>
public sealed class PEnv : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly Action? _drop;
    private readonly Dictionary<string, string?>? _settings;

    internal static void RegisterModules(IServiceCollection services, IConfiguration config)
    {
        new AuditModule().Register(services, config);
        new ConsentModule().Register(services, config);
        new IdentityModule().Register(services, config);
        new MedicationsModule().Register(services, config);
        new PatientsModule().Register(services, config);
        new ProductTraceModule().Register(services, config);
        new GuidanceModule().Register(services, config);
        new AIModule().Register(services, config);
    }

    public PEnv(bool seedPatients = false, Dictionary<string, string?>? settings = null, string? reuseDatabase = null, bool seedReference = true)
    {
        _settings = settings;
        var map = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = AuthModes.DevelopmentMock,
            ["Auth:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["Medications:SeedDemoData"] = seedReference ? "true" : "false",
            ["Patients:SeedDemoData"] = seedPatients ? "true" : "false",
            ["Patients:IdentifierHashKey"] = "test-only-hash-key-not-a-secret",
            ["Guidance:SeedDemoData"] = seedPatients ? "true" : "false",
            ["ManufacturerReports:Provider"] = "Mock",
            ["Ai:Provider"] = "Mock",
            ["Persistence:Provider"] = "InMemory",
        };
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
        _sp.GetRequiredService<IDemoIdentitySeeder>().SeedAsync().GetAwaiter().GetResult();
        if (seedReference)
        {
            _sp.GetRequiredService<IDemoMedicationSeeder>().SeedAsync().GetAwaiter().GetResult();
        }

        if (seedPatients)
        {
            _sp.GetRequiredService<IDemoPatientSeeder>().SeedAsync().GetAwaiter().GetResult();
            _sp.GetRequiredService<IDemoGuidanceSeeder>().SeedAsync().GetAwaiter().GetResult();
        }
    }

    public string? ConnectionString { get; }
    public FakeClock Clock { get; } = new();
    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();

    /// <summary>A second application instance over the same database (a restart): PostgreSQL only.</summary>
    public PEnv Restart() => ConnectionString is null
        ? throw new InvalidOperationException("Restart needs PostgreSQL (MEDSMARTER_PG_TEST).")
        : new PEnv(false, _settings, ConnectionString, seedReference: false);

    public IPatientService Patients => Get<IPatientService>();
    public IPatientMedicationService Meds => Get<IPatientMedicationService>();
    public ICareRelationshipService Care => Get<ICareRelationshipService>();
    public IConsentService Consents => Get<IConsentService>();
    public IProductTraceService Products => Get<IProductTraceService>();
    public IManufacturerReportService Reports => Get<IManufacturerReportService>();
    public IManufacturerReportOutbox Outbox => Get<IManufacturerReportOutbox>();
    public IGuidanceService Guidance => Get<IGuidanceService>();
    public IAuditReader Audit => Get<IAuditReader>();
    public IAccessAuthorizer Authz => Get<IAccessAuthorizer>();
    public IMedicationService Reference => Get<IMedicationService>();
    public IPatientContextService Context => Get<IPatientContextService>();

    private AccessCatalog Catalog => Get<AccessCatalog>();

    public Guid UserId(string accountId) => Catalog.File.DemoAccounts.Single(a => a.Id == accountId).UserId;

    public CurrentUser As(string accountId)
    {
        var a = Catalog.File.DemoAccounts.Single(x => x.Id == accountId);
        var roles = a.Roles.Select(r => r.Role).ToHashSet();
        return new CurrentUser(a.UserId, Guid.NewGuid(), a.DisplayName["en"], roles, Catalog.PermissionsOf(roles), a.Organizations.ToHashSet());
    }

    public static readonly RequestContext Rc = new("test", "corr-12345678");

    /// <summary>The patient record of a demo account (created if needed), as the patient would do it.</summary>
    public async Task<Guid> Patient(string accountId)
    {
        var id = UserId(accountId);
        var r = await Patients.EnsureOwnAsync(id, "test", null);
        Assert.True(r.Succeeded);
        return id;
    }

    public async Task<ConsentDto> Consent(string subjectAccount, string purpose, string[] scopes, string? granteeAccount = null, int days = 30)
    {
        var subject = UserId(subjectAccount);
        var outcome = await Consents.GrantAsync(subject, new GrantConsentCommand(subject, granteeAccount is null ? null : UserId(granteeAccount), null, purpose, scopes, Clock.UtcNow.AddDays(days), "consent-text-v1"), "test", null);
        Assert.True(outcome.Succeeded, outcome.Error.ToString());
        return outcome.Consent!;
    }

    public async Task<Guid> Reference_(string name) => (await Reference.SearchAsync(new MedicationSearchQuery(name))).Items[0].Id;

    public async Task<PatientMedicationDto> TakeMedication(Guid subject, string referenceName = "demopril", DateOnly? start = null)
    {
        var id = await Reference_(referenceName);
        var r = await Meds.AddAsync(subject, subject, new PatientMedicationInput(id, null, 5, "mg", null, FrequencyKind.TimesPerDay, 1, "oral", start ?? Today.AddDays(-10), null, PrescriberSource.Physician, null), "test", null);
        Assert.True(r.Succeeded, r.Detail);
        return r.Value!;
    }

    public DateOnly Today => DateOnly.FromDateTime(Clock.UtcNow.UtcDateTime);

    public static string Gtin13(string twelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return twelveDigits + ((10 - (sum % 10)) % 10);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _drop?.Invoke();
    }
}
