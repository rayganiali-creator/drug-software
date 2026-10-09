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

namespace MedSmarter.Knowledge.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
}

/// <summary>Medications + AI + Integrations + Audit wired in memory (no network, no database, no keys).</summary>
public sealed class KEnv : IDisposable
{
    private readonly ServiceProvider _sp;

    public KEnv(bool seed = true, Dictionary<string, string?>? settings = null)
    {
        var map = new Dictionary<string, string?> { ["Medications:SeedDemoData"] = seed ? "true" : "false", ["Ai:Provider"] = "Mock" };
        foreach (var (k, v) in settings ?? [])
        {
            map[k] = v;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(map).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        new AuditModule().Register(services, config);
        new MedicationsModule().Register(services, config);
        new AIModule().Register(services, config);
        new IntegrationsModule().Register(services, config);
        _sp = services.BuildServiceProvider();
        if (seed)
        {
            _sp.GetRequiredService<IDemoMedicationSeeder>().SeedAsync().GetAwaiter().GetResult();
        }
    }

    public FakeClock Clock { get; } = new();
    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();
    public IMedicationService Meds => Get<IMedicationService>();
    public IMedicationAdminService Admin => Get<IMedicationAdminService>();
    public IKnowledgeSourceService Sources => Get<IKnowledgeSourceService>();
    public IAuditReader Audit => Get<IAuditReader>();
    public static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task<MedicationSummaryDto> One(string q) => (await Meds.SearchAsync(new MedicationSearchQuery(q))).Items[0];
    public async Task<IReadOnlyList<string>> Names(string q, int limit = 20, int offset = 0) =>
        [.. (await Meds.SearchAsync(new MedicationSearchQuery(q, limit, offset))).Items.Select(i => i.Name.En ?? i.Name.Fa ?? "")];

    /// <summary>A real (non-demo) source + revision for tests of the "verified" path. FICTIONAL content only.</summary>
    public async Task<(KnowledgeSourceDto Source, KnowledgeRevisionDto Revision)> RealSource(bool redistribution = true, bool validatedRevision = true)
    {
        var s = (await Sources.RegisterSourceAsync(Actor, new NewKnowledgeSource("Test formulary (fictional)", "Test publisher", SourceType.Publication, "https://example.invalid/formulary", "t-1", "Test licence", redistribution, null), "test", null)).Value!;
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

    public void Dispose() => _sp.Dispose();
}
