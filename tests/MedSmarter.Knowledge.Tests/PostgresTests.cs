using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;
using MedSmarter.Modules.Medications.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedSmarter.Knowledge.Tests;

/// <summary>Runs only when MEDSMARTER_PG_TEST holds a connection string to a throw-away LOCAL PostgreSQL database (see docs/phase4/07).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEDSMARTER_PG_TEST")))
        {
            Skip = "Set MEDSMARTER_PG_TEST to a local PostgreSQL connection string to run the schema tests (no server is required otherwise).";
        }
    }
}

public class PostgresSchemaTests
{
    private static MedicationsDbContext Db()
    {
        var options = new DbContextOptionsBuilder<MedicationsDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("MEDSMARTER_PG_TEST"), o => o.MigrationsHistoryTable("__ef_migrations_history", MedicationsDbContext.Schema))
            .Options;
        return new MedicationsDbContext(options);
    }

    private sealed record Graph(ReferenceTerm Form, ActiveIngredient Ingredient, KnowledgeRevision Revision, Medication Med);

    private static async Task<Graph> Persist(MedicationsDbContext db, bool demo = false)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var form = new ReferenceTerm { Id = Guid.NewGuid(), Kind = ReferenceKind.DosageForm, Code = "t-" + tag, NameEn = "Test form" };
        var ing = new ActiveIngredient { Id = Guid.NewGuid(), NameEn = "pgtestium " + tag };
        var src = new KnowledgeSource { Id = Guid.NewGuid(), Name = "pg test " + tag, Publisher = "t", Type = demo ? SourceType.Demo : SourceType.Publication, Version = "1", LicenseName = "t", ReceivedAt = DateTimeOffset.UtcNow };
        var rev = new KnowledgeRevision { Id = Guid.NewGuid(), SourceId = src.Id, Label = "r1", ReceivedAt = DateTimeOffset.UtcNow };
        var med = new Medication { Id = Guid.NewGuid(), NameEn = "Pgtestomed " + tag, DosageFormId = form.Id, IsDemo = demo, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Lifecycle = LifecycleStatus.Active };
        med.Ingredients.Add(new MedicationIngredient { MedicationId = med.Id, IngredientId = ing.Id, StrengthValue = 5, StrengthUnit = "mg" });
        med.Statements.Add(new MedicationStatement { Id = Guid.NewGuid(), MedicationId = med.Id, Kind = StatementKind.Warning, TextEn = "t", RevisionId = rev.Id });
        db.AddRange(form, ing, src, rev, med);
        db.Add(new MedicationSearchTerm { Id = Guid.NewGuid(), MedicationId = med.Id, Normalized = TextNormalizer.Normalize(med.NameEn), Display = med.NameEn!, Kind = NameKind.Generic });
        await db.SaveChangesAsync();
        return new Graph(form, ing, rev, med);
    }

    private static async Task<PostgresException> Violation(Func<Task> act)
    {
        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(act);
        return Assert.IsType<PostgresException>(ex.InnerException);
    }

    [PostgresFact]
    public async Task Migration_applies_and_leaves_nothing_pending()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("InitialMedicationsSchema", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task A_medication_graph_round_trips()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var g = await Persist(db);
        await using var read = Db();
        var m = await read.Medications.Include(x => x.Ingredients).Include(x => x.Statements).SingleAsync(x => x.Id == g.Med.Id);
        Assert.Equal(g.Med.NameEn, m.NameEn);
        Assert.Equal(5m, Assert.Single(m.Ingredients).StrengthValue);
        Assert.Equal(StatementKind.Warning, Assert.Single(m.Statements).Kind);
        Assert.Equal(LifecycleStatus.Active, m.Lifecycle);
    }

    [PostgresFact]
    public async Task Normalized_names_are_searchable_by_substring()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var g = await Persist(db);
        var needle = TextNormalizer.Normalize(g.Med.NameEn)[2..8];
        var hits = await db.MedicationSearchTerms.Where(t => EF.Functions.Like(t.Normalized, $"%{needle}%")).Select(t => t.MedicationId).ToListAsync();
        Assert.Contains(g.Med.Id, hits);
    }

    [PostgresFact]
    public async Task Identifier_uniqueness_is_enforced_by_the_database()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var a = await Persist(db);
        var b = await Persist(db);
        var code = "PG-" + Guid.NewGuid().ToString("N")[..10];
        db.Add(new MedicationIdentifier { Id = Guid.NewGuid(), MedicationId = a.Med.Id, Scheme = IdentifierScheme.ExternalDrugId, Value = code, SourceRevisionId = a.Revision.Id });
        await db.SaveChangesAsync();
        db.Add(new MedicationIdentifier { Id = Guid.NewGuid(), MedicationId = b.Med.Id, Scheme = IdentifierScheme.ExternalDrugId, Value = code, SourceRevisionId = b.Revision.Id });
        Assert.Equal("23505", (await Violation(() => db.SaveChangesAsync())).SqlState);
    }

    [PostgresFact]
    public async Task A_statement_without_a_real_revision_is_rejected()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var g = await Persist(db);
        db.Add(new MedicationStatement { Id = Guid.NewGuid(), MedicationId = g.Med.Id, Kind = StatementKind.Warning, TextEn = "orphan", RevisionId = Guid.NewGuid() });
        Assert.Equal("23503", (await Violation(() => db.SaveChangesAsync())).SqlState);
    }

    [PostgresFact]
    public async Task Interactions_are_stored_once_in_canonical_order_and_need_a_source()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var a = await Persist(db);
        var b = await Persist(db);
        var (low, high) = a.Ingredient.Id.CompareTo(b.Ingredient.Id) < 0 ? (a.Ingredient.Id, b.Ingredient.Id) : (b.Ingredient.Id, a.Ingredient.Id);
        db.Add(new DrugInteraction { Id = Guid.NewGuid(), IngredientAId = high, IngredientBId = low, Severity = InteractionSeverity.Minor, RevisionId = a.Revision.Id });
        Assert.Equal("23514", (await Violation(() => db.SaveChangesAsync())).SqlState); // check constraint: A < B
        db.ChangeTracker.Clear();
        db.Add(new DrugInteraction { Id = Guid.NewGuid(), IngredientAId = low, IngredientBId = high, Severity = InteractionSeverity.Minor, RevisionId = a.Revision.Id });
        await db.SaveChangesAsync();
        db.Add(new DrugInteraction { Id = Guid.NewGuid(), IngredientAId = low, IngredientBId = high, Severity = InteractionSeverity.Major, RevisionId = a.Revision.Id });
        Assert.Equal("23505", (await Violation(() => db.SaveChangesAsync())).SqlState);
    }

    [PostgresFact]
    public async Task Fictional_records_cannot_be_stored_as_validated()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var g = await Persist(db, demo: true);
        g.Med.Validation = ValidationStatus.Validated;
        Assert.Equal("23514", (await Violation(() => db.SaveChangesAsync())).SqlState);
    }

    [PostgresFact]
    public async Task Concurrent_edits_are_detected_with_the_version_column()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var g = await Persist(db);
        await using var one = Db();
        await using var two = Db();
        var m1 = await one.Medications.SingleAsync(x => x.Id == g.Med.Id);
        var m2 = await two.Medications.SingleAsync(x => x.Id == g.Med.Id);
        m1.NameEn = "first";
        m1.Version++;
        await one.SaveChangesAsync();
        m2.NameEn = "second";
        m2.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task A_medication_needs_a_name_and_a_strength_needs_a_unit()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var g = await Persist(db);
        db.Add(new Medication { Id = Guid.NewGuid(), DosageFormId = g.Form.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        Assert.Equal("23514", (await Violation(() => db.SaveChangesAsync())).SqlState);
        db.ChangeTracker.Clear();
        g.Med.Ingredients.Clear();
        db.Add(new MedicationIngredient { MedicationId = g.Med.Id, IngredientId = g.Ingredient.Id, StrengthValue = 5, StrengthUnit = null });
        Assert.Equal("23514", (await Violation(() => db.SaveChangesAsync())).SqlState);
    }
}
