using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;
using MedSmarter.Modules.Medications.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedSmarter.Modules.Medications;

/// <summary>
/// PostgreSQL implementation of <see cref="IMedicationRepository"/> (schema <c>medications</c>). Every call uses its own short-lived
/// context from the factory, so the singleton services stay thread-safe. Returned objects are detached copies, as with the in-memory
/// repository. Substring search runs in the database and is served by the pg_trgm GIN index.
/// </summary>
public sealed class PostgresMedicationRepository(IDbContextFactory<MedicationsDbContext> factory) : IMedicationRepository
{
    private async Task<MedicationsDbContext> Db(CancellationToken ct) => await factory.CreateDbContextAsync(ct);

    private static IQueryable<Medication> WithChildren(IQueryable<Medication> q) =>
        q.AsNoTracking().AsSplitQuery()
         .Include(m => m.Ingredients).Include(m => m.Routes).Include(m => m.Classifications)
         .Include(m => m.Synonyms).Include(m => m.Identifiers).Include(m => m.Statements);

    public async Task<Medication?> FindMedicationAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await WithChildren(db.Medications.Where(m => m.Id == id)).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Medication>> GetMedicationsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToArray();
        if (list.Length == 0)
        {
            return [];
        }

        await using var db = await Db(ct);
        return await WithChildren(db.Medications.Where(m => list.Contains(m.Id))).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MedicationSearchTerm>> FindTermsAsync(string normalized, int max, CancellationToken ct)
    {
        await using var db = await Db(ct);
        var q = db.MedicationSearchTerms.AsNoTracking();
        if (normalized.Length > 0)
        {
            // LIKE '%x%' is what the trigram index serves. Prefix hits first, then shorter names, so the candidate cap keeps the best matches.
            var like = "%" + EscapeLike(normalized) + "%";
            var prefix = EscapeLike(normalized) + "%";
            q = q.Where(t => EF.Functions.Like(t.Normalized, like))
                 .OrderBy(t => EF.Functions.Like(t.Normalized, prefix) ? 0 : 1)
                 .ThenBy(t => t.Normalized.Length)
                 .ThenBy(t => t.Id);
        }
        else
        {
            q = q.OrderBy(t => t.Normalized).ThenBy(t => t.Id);
        }

        return await q.Take(max).ToListAsync(ct);
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    public async Task<IReadOnlySet<Guid>> VisibleMedicationIdsAsync(bool includeNonActive, CancellationToken ct)
    {
        await using var db = await Db(ct);
        var q = db.Medications.AsNoTracking();
        if (!includeNonActive)
        {
            q = q.Where(m => m.Lifecycle == LifecycleStatus.Active);
        }

        return (await q.Select(m => m.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<bool> SaveMedicationAsync(Medication medication, IReadOnlyList<MedicationSearchTerm> terms, MedicationVersion? version, int? expectedVersion, CancellationToken ct)
    {
        await using var db = await Db(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (expectedVersion is null)
            {
                if (await db.Medications.AnyAsync(m => m.Id == medication.Id, ct))
                {
                    return false;
                }

                db.Medications.Add(Copy(medication));
            }
            else
            {
                // Optimistic concurrency: the row only changes when nobody saved since the caller read it.
                var rows = await db.Medications.Where(m => m.Id == medication.Id && m.Version == expectedVersion).ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Version, medication.Version)
                    .SetProperty(m => m.NameEn, medication.NameEn)
                    .SetProperty(m => m.NameFa, medication.NameFa)
                    .SetProperty(m => m.BrandId, medication.BrandId)
                    .SetProperty(m => m.ManufacturerId, medication.ManufacturerId)
                    .SetProperty(m => m.DosageFormId, medication.DosageFormId)
                    .SetProperty(m => m.Lifecycle, medication.Lifecycle)
                    .SetProperty(m => m.Validation, medication.Validation)
                    .SetProperty(m => m.IsDemo, medication.IsDemo)
                    .SetProperty(m => m.UpdatedAt, medication.UpdatedAt)
                    .SetProperty(m => m.UpdatedBy, medication.UpdatedBy), ct);
                if (rows == 0)
                {
                    return false;
                }

                var id = medication.Id;
                await db.MedicationIngredients.Where(x => x.MedicationId == id).ExecuteDeleteAsync(ct);
                await db.MedicationRoutes.Where(x => x.MedicationId == id).ExecuteDeleteAsync(ct);
                await db.MedicationClassifications.Where(x => x.MedicationId == id).ExecuteDeleteAsync(ct);
                await db.MedicationSynonyms.Where(x => x.MedicationId == id).ExecuteDeleteAsync(ct);
                await db.MedicationIdentifiers.Where(x => x.MedicationId == id).ExecuteDeleteAsync(ct);
                await db.MedicationStatements.Where(x => x.MedicationId == id).ExecuteDeleteAsync(ct);
                var copy = Copy(medication);
                db.MedicationIngredients.AddRange(copy.Ingredients);
                db.MedicationRoutes.AddRange(copy.Routes);
                db.MedicationClassifications.AddRange(copy.Classifications);
                db.MedicationSynonyms.AddRange(copy.Synonyms);
                db.MedicationIdentifiers.AddRange(copy.Identifiers);
                db.MedicationStatements.AddRange(copy.Statements);
            }

            await db.MedicationSearchTerms.Where(t => t.MedicationId == medication.Id).ExecuteDeleteAsync(ct);
            db.MedicationSearchTerms.AddRange(terms.Select(t => new MedicationSearchTerm { Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id, MedicationId = medication.Id, Normalized = t.Normalized, Display = t.Display, Kind = t.Kind }));
            if (version is not null)
            {
                db.MedicationVersions.Add(new MedicationVersion { Id = version.Id, MedicationId = version.MedicationId, VersionNumber = version.VersionNumber, SnapshotJson = version.SnapshotJson, ChangedAt = version.ChangedAt, ChangedBy = version.ChangedBy, ChangeReason = version.ChangeReason });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false; // a concurrent writer took the same id/identifier/version: reported as a conflict, never as a server error
        }
    }

    public async Task<Medication?> FindByIdentifierAsync(IdentifierScheme scheme, string value, CancellationToken ct)
    {
        await using var db = await Db(ct);
        var pattern = EscapeLike(value);
        var id = await db.MedicationIdentifiers.AsNoTracking().Where(i => i.Scheme == scheme && EF.Functions.ILike(i.Value, pattern)).Select(i => (Guid?)i.MedicationId).FirstOrDefaultAsync(ct);
        return id is null ? null : await WithChildren(db.Medications.Where(m => m.Id == id)).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<MedicationVersion>> GetVersionsAsync(Guid medicationId, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.MedicationVersions.AsNoTracking().Where(v => v.MedicationId == medicationId).OrderBy(v => v.VersionNumber).ToListAsync(ct);
    }

    // ---- reference data ----
    public async Task<ReferenceTerm?> FindTermAsync(ReferenceKind kind, string code, CancellationToken ct)
    {
        await using var db = await Db(ct);
        var pattern = EscapeLike(code);
        return await db.ReferenceTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Kind == kind && EF.Functions.ILike(t.Code, pattern), ct);
    }

    public async Task<ReferenceTerm?> FindTermByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.ReferenceTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IReadOnlyList<ReferenceTerm>> GetTermsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToArray();
        await using var db = await Db(ct);
        return await db.ReferenceTerms.AsNoTracking().Where(t => list.Contains(t.Id)).ToListAsync(ct);
    }

    public Task AddTermAsync(ReferenceTerm term, CancellationToken ct) => AddAsync(term, ct);

    public async Task<ActiveIngredient?> FindIngredientAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.ActiveIngredients.AsNoTracking().Include(i => i.Synonyms).FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<ActiveIngredient>> GetIngredientsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToArray();
        await using var db = await Db(ct);
        return await db.ActiveIngredients.AsNoTracking().Include(i => i.Synonyms).Where(i => list.Contains(i.Id)).ToListAsync(ct);
    }

    public Task AddIngredientAsync(ActiveIngredient ingredient, CancellationToken ct) => AddAsync(ingredient, ct);

    public async Task<Manufacturer?> FindManufacturerAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.Manufacturers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public Task AddManufacturerAsync(Manufacturer value, CancellationToken ct) => AddAsync(value, ct);

    public async Task<Brand?> FindBrandAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public Task AddBrandAsync(Brand value, CancellationToken ct) => AddAsync(value, ct);

    // ---- interactions ----
    public async Task<IReadOnlyList<DrugInteraction>> FindInteractionsAsync(IEnumerable<Guid> ingredientIds, CancellationToken ct)
    {
        var list = ingredientIds.Distinct().ToArray();
        await using var db = await Db(ct);
        return await db.DrugInteractions.AsNoTracking().Where(i => list.Contains(i.IngredientAId) || list.Contains(i.IngredientBId)).ToListAsync(ct);
    }

    public async Task UpsertInteractionAsync(DrugInteraction value, CancellationToken ct)
    {
        await using var db = await Db(ct);
        var existing = await db.DrugInteractions.FirstOrDefaultAsync(i => i.IngredientAId == value.IngredientAId && i.IngredientBId == value.IngredientBId, ct);
        if (existing is null)
        {
            db.DrugInteractions.Add(value);
        }
        else
        {
            value.Id = existing.Id;
            db.Entry(existing).CurrentValues.SetValues(value);
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- sources ----
    public async Task<KnowledgeSource?> FindSourceAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.KnowledgeSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.KnowledgeSources.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
    }

    public Task AddSourceAsync(KnowledgeSource source, CancellationToken ct) => AddAsync(source, ct);

    public async Task<KnowledgeRevision?> FindRevisionAsync(Guid id, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.KnowledgeRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<KnowledgeRevision>> GetRevisionsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToArray();
        await using var db = await Db(ct);
        return await db.KnowledgeRevisions.AsNoTracking().Where(r => list.Contains(r.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeRevision>> ListRevisionsAsync(Guid sourceId, CancellationToken ct)
    {
        await using var db = await Db(ct);
        return await db.KnowledgeRevisions.AsNoTracking().Where(r => r.SourceId == sourceId).OrderBy(r => r.ReceivedAt).ToListAsync(ct);
    }

    public Task AddRevisionAsync(KnowledgeRevision revision, CancellationToken ct) => AddAsync(revision, ct);

    public async Task UpdateRevisionAsync(KnowledgeRevision revision, CancellationToken ct)
    {
        await using var db = await Db(ct);
        db.KnowledgeRevisions.Update(revision);
        await db.SaveChangesAsync(ct);
    }

    private async Task AddAsync<T>(T entity, CancellationToken ct) where T : class
    {
        await using var db = await Db(ct);
        db.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DuplicateRecordException();
        }
    }

    private static Medication Copy(Medication m) => new()
    {
        Id = m.Id, Version = m.Version, NameEn = m.NameEn, NameFa = m.NameFa, BrandId = m.BrandId, ManufacturerId = m.ManufacturerId, DosageFormId = m.DosageFormId,
        Lifecycle = m.Lifecycle, Validation = m.Validation, IsDemo = m.IsDemo, CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt, UpdatedBy = m.UpdatedBy,
        Ingredients = [.. m.Ingredients.Select(i => new MedicationIngredient { MedicationId = m.Id, IngredientId = i.IngredientId, StrengthValue = i.StrengthValue, StrengthUnit = i.StrengthUnit, PerUnit = i.PerUnit, Order = i.Order })],
        Routes = [.. m.Routes.Select(r => new MedicationRoute { MedicationId = m.Id, TermId = r.TermId })],
        Classifications = [.. m.Classifications.Select(r => new MedicationClassification { MedicationId = m.Id, TermId = r.TermId })],
        Synonyms = [.. m.Synonyms.Select(s => new MedicationSynonym { Id = s.Id, MedicationId = m.Id, Text = s.Text })],
        Identifiers = [.. m.Identifiers.Select(i => new MedicationIdentifier { Id = i.Id, MedicationId = m.Id, Scheme = i.Scheme, Value = i.Value, SourceRevisionId = i.SourceRevisionId })],
        Statements = [.. m.Statements.Select(s => new MedicationStatement { Id = s.Id, MedicationId = m.Id, Kind = s.Kind, TextEn = s.TextEn, TextFa = s.TextFa, Severity = s.Severity, Frequency = s.Frequency, Population = s.Population, RevisionId = s.RevisionId })],
    };
}
