using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;

namespace MedSmarter.Modules.Medications;

/// <summary>
/// Persistence port of the medication knowledge core. The in-memory implementation serves local development and tests;
/// a PostgreSQL implementation over <c>MedicationsDbContext</c> is the next step (model and migration are ready).
/// </summary>
public interface IMedicationRepository
{
    // medications
    Task<Medication?> FindMedicationAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Medication>> GetMedicationsAsync(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Candidate search terms containing <paramref name="normalized"/> (all terms when empty), bounded by <paramref name="max"/>.</summary>
    Task<IReadOnlyList<MedicationSearchTerm>> FindTermsAsync(string normalized, int max, CancellationToken ct);

    /// <summary>Medication ids that are in use (not draft/inactive) unless <paramref name="includeNonActive"/>.</summary>
    Task<IReadOnlySet<Guid>> VisibleMedicationIdsAsync(bool includeNonActive, CancellationToken ct);

    /// <summary>Inserts or replaces the medication (and its search terms) and appends the version row, atomically.
    /// Returns false when <paramref name="expectedVersion"/> no longer matches (optimistic concurrency).</summary>
    Task<bool> SaveMedicationAsync(Medication medication, IReadOnlyList<MedicationSearchTerm> terms, MedicationVersion? version, int? expectedVersion, CancellationToken ct);

    Task<Medication?> FindByIdentifierAsync(IdentifierScheme scheme, string value, CancellationToken ct);
    Task<IReadOnlyList<MedicationVersion>> GetVersionsAsync(Guid medicationId, CancellationToken ct);

    // reference data
    Task<ReferenceTerm?> FindTermAsync(ReferenceKind kind, string code, CancellationToken ct);
    Task<ReferenceTerm?> FindTermByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ReferenceTerm>> GetTermsAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task AddTermAsync(ReferenceTerm term, CancellationToken ct);
    Task<ActiveIngredient?> FindIngredientAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ActiveIngredient>> GetIngredientsAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task AddIngredientAsync(ActiveIngredient ingredient, CancellationToken ct);
    Task<Manufacturer?> FindManufacturerAsync(Guid id, CancellationToken ct);
    Task AddManufacturerAsync(Manufacturer value, CancellationToken ct);
    Task<Brand?> FindBrandAsync(Guid id, CancellationToken ct);
    Task AddBrandAsync(Brand value, CancellationToken ct);

    // interactions
    Task<IReadOnlyList<DrugInteraction>> FindInteractionsAsync(IEnumerable<Guid> ingredientIds, CancellationToken ct);
    Task UpsertInteractionAsync(DrugInteraction value, CancellationToken ct);

    // sources
    Task<KnowledgeSource?> FindSourceAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct);
    Task AddSourceAsync(KnowledgeSource source, CancellationToken ct);
    Task<KnowledgeRevision?> FindRevisionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeRevision>> GetRevisionsAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeRevision>> ListRevisionsAsync(Guid sourceId, CancellationToken ct);
    Task AddRevisionAsync(KnowledgeRevision revision, CancellationToken ct);
    Task UpdateRevisionAsync(KnowledgeRevision revision, CancellationToken ct);
}

public sealed class InMemoryMedicationRepository : IMedicationRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Medication> _meds = [];
    private readonly Dictionary<Guid, List<MedicationSearchTerm>> _terms = [];
    private readonly Dictionary<Guid, List<MedicationVersion>> _versions = [];
    private readonly Dictionary<Guid, ReferenceTerm> _refTerms = [];
    private readonly Dictionary<Guid, ActiveIngredient> _ingredients = [];
    private readonly Dictionary<Guid, Manufacturer> _manufacturers = [];
    private readonly Dictionary<Guid, Brand> _brands = [];
    private readonly Dictionary<(Guid, Guid), DrugInteraction> _interactions = [];
    private readonly Dictionary<Guid, KnowledgeSource> _sources = [];
    private readonly Dictionary<Guid, KnowledgeRevision> _revisions = [];

    private Task<T> Run<T>(Func<T> f)
    {
        lock (_gate)
        {
            return Task.FromResult(f());
        }
    }

    public Task<Medication?> FindMedicationAsync(Guid id, CancellationToken ct) => Run(() => _meds.GetValueOrDefault(id) is { } m ? Clone(m) : null);

    public Task<IReadOnlyList<Medication>> GetMedicationsAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Run<IReadOnlyList<Medication>>(() => [.. ids.Distinct().Select(i => _meds.GetValueOrDefault(i)).Where(m => m is not null).Select(m => Clone(m!))]);

    public Task<IReadOnlyList<MedicationSearchTerm>> FindTermsAsync(string normalized, int max, CancellationToken ct) =>
        Run<IReadOnlyList<MedicationSearchTerm>>(() => [.. _terms.Values.SelectMany(x => x)
            .Where(t => normalized.Length == 0 || t.Normalized.Contains(normalized, StringComparison.Ordinal))
            .Take(max)]);

    public Task<IReadOnlySet<Guid>> VisibleMedicationIdsAsync(bool includeNonActive, CancellationToken ct) =>
        Run<IReadOnlySet<Guid>>(() => _meds.Values.Where(m => includeNonActive || m.Lifecycle == LifecycleStatus.Active).Select(m => m.Id).ToHashSet());

    public Task<bool> SaveMedicationAsync(Medication medication, IReadOnlyList<MedicationSearchTerm> terms, MedicationVersion? version, int? expectedVersion, CancellationToken ct) =>
        Run(() =>
        {
            if (_meds.TryGetValue(medication.Id, out var existing))
            {
                if (expectedVersion is null || existing.Version != expectedVersion)
                {
                    return false;
                }
            }
            else if (expectedVersion is not null)
            {
                return false;
            }

            _meds[medication.Id] = Clone(medication);
            _terms[medication.Id] = [.. terms];
            if (version is not null)
            {
                if (!_versions.TryGetValue(medication.Id, out var list))
                {
                    _versions[medication.Id] = list = [];
                }

                list.Add(version);
            }

            return true;
        });

    public Task<Medication?> FindByIdentifierAsync(IdentifierScheme scheme, string value, CancellationToken ct) =>
        Run(() => _meds.Values.FirstOrDefault(m => m.Identifiers.Any(i => i.Scheme == scheme && string.Equals(i.Value, value, StringComparison.OrdinalIgnoreCase))) is { } m ? Clone(m) : null);

    public Task<IReadOnlyList<MedicationVersion>> GetVersionsAsync(Guid medicationId, CancellationToken ct) =>
        Run<IReadOnlyList<MedicationVersion>>(() => [.. _versions.GetValueOrDefault(medicationId) ?? []]);

    public Task<ReferenceTerm?> FindTermAsync(ReferenceKind kind, string code, CancellationToken ct) =>
        Run(() => _refTerms.Values.FirstOrDefault(t => t.Kind == kind && string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<ReferenceTerm?> FindTermByIdAsync(Guid id, CancellationToken ct) => Run(() => _refTerms.GetValueOrDefault(id));
    public Task<IReadOnlyList<ReferenceTerm>> GetTermsAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Run<IReadOnlyList<ReferenceTerm>>(() => [.. ids.Distinct().Select(i => _refTerms.GetValueOrDefault(i)).Where(t => t is not null).Select(t => t!)]);
    public Task AddTermAsync(ReferenceTerm term, CancellationToken ct) => Run(() => { _refTerms[term.Id] = term; return true; });

    public Task<ActiveIngredient?> FindIngredientAsync(Guid id, CancellationToken ct) => Run(() => _ingredients.GetValueOrDefault(id));
    public Task<IReadOnlyList<ActiveIngredient>> GetIngredientsAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Run<IReadOnlyList<ActiveIngredient>>(() => [.. ids.Distinct().Select(i => _ingredients.GetValueOrDefault(i)).Where(x => x is not null).Select(x => x!)]);
    public Task AddIngredientAsync(ActiveIngredient ingredient, CancellationToken ct) => Run(() => { _ingredients[ingredient.Id] = ingredient; return true; });
    public Task<Manufacturer?> FindManufacturerAsync(Guid id, CancellationToken ct) => Run(() => _manufacturers.GetValueOrDefault(id));
    public Task AddManufacturerAsync(Manufacturer value, CancellationToken ct) => Run(() => { _manufacturers[value.Id] = value; return true; });
    public Task<Brand?> FindBrandAsync(Guid id, CancellationToken ct) => Run(() => _brands.GetValueOrDefault(id));
    public Task AddBrandAsync(Brand value, CancellationToken ct) => Run(() => { _brands[value.Id] = value; return true; });

    public Task<IReadOnlyList<DrugInteraction>> FindInteractionsAsync(IEnumerable<Guid> ingredientIds, CancellationToken ct) =>
        Run<IReadOnlyList<DrugInteraction>>(() =>
        {
            var set = ingredientIds.ToHashSet();
            return [.. _interactions.Values.Where(i => set.Contains(i.IngredientAId) || set.Contains(i.IngredientBId))];
        });

    public Task UpsertInteractionAsync(DrugInteraction value, CancellationToken ct) => Run(() =>
    {
        var key = (value.IngredientAId, value.IngredientBId);
        if (_interactions.TryGetValue(key, out var existing))
        {
            value.Id = existing.Id;
        }

        _interactions[key] = value;
        return true;
    });

    public Task<KnowledgeSource?> FindSourceAsync(Guid id, CancellationToken ct) => Run(() => _sources.GetValueOrDefault(id));
    public Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct) => Run<IReadOnlyList<KnowledgeSource>>(() => [.. _sources.Values.OrderBy(s => s.Name, StringComparer.Ordinal)]);
    public Task AddSourceAsync(KnowledgeSource source, CancellationToken ct) => Run(() => { _sources[source.Id] = source; return true; });
    public Task<KnowledgeRevision?> FindRevisionAsync(Guid id, CancellationToken ct) => Run(() => _revisions.GetValueOrDefault(id));
    public Task<IReadOnlyList<KnowledgeRevision>> GetRevisionsAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Run<IReadOnlyList<KnowledgeRevision>>(() => [.. ids.Distinct().Select(i => _revisions.GetValueOrDefault(i)).Where(r => r is not null).Select(r => r!)]);
    public Task<IReadOnlyList<KnowledgeRevision>> ListRevisionsAsync(Guid sourceId, CancellationToken ct) =>
        Run<IReadOnlyList<KnowledgeRevision>>(() => [.. _revisions.Values.Where(r => r.SourceId == sourceId).OrderBy(r => r.ReceivedAt)]);
    public Task AddRevisionAsync(KnowledgeRevision revision, CancellationToken ct) => Run(() => { _revisions[revision.Id] = revision; return true; });
    public Task UpdateRevisionAsync(KnowledgeRevision revision, CancellationToken ct) => Run(() => { _revisions[revision.Id] = revision; return true; });

    // Callers get copies, so a failed validation can never leave a half-edited object in the store.
    private static Medication Clone(Medication m) => new()
    {
        Id = m.Id, Version = m.Version, NameEn = m.NameEn, NameFa = m.NameFa, BrandId = m.BrandId, ManufacturerId = m.ManufacturerId, DosageFormId = m.DosageFormId,
        Lifecycle = m.Lifecycle, Validation = m.Validation, IsDemo = m.IsDemo, CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt, UpdatedBy = m.UpdatedBy,
        Ingredients = [.. m.Ingredients.Select(i => new MedicationIngredient { MedicationId = i.MedicationId, IngredientId = i.IngredientId, StrengthValue = i.StrengthValue, StrengthUnit = i.StrengthUnit, PerUnit = i.PerUnit, Order = i.Order })],
        Routes = [.. m.Routes.Select(r => new MedicationRoute { MedicationId = r.MedicationId, TermId = r.TermId })],
        Classifications = [.. m.Classifications.Select(r => new MedicationClassification { MedicationId = r.MedicationId, TermId = r.TermId })],
        Synonyms = [.. m.Synonyms.Select(s => new MedicationSynonym { Id = s.Id, MedicationId = s.MedicationId, Text = s.Text })],
        Identifiers = [.. m.Identifiers.Select(i => new MedicationIdentifier { Id = i.Id, MedicationId = i.MedicationId, Scheme = i.Scheme, Value = i.Value, SourceRevisionId = i.SourceRevisionId })],
        Statements = [.. m.Statements.Select(s => new MedicationStatement { Id = s.Id, MedicationId = s.MedicationId, Kind = s.Kind, TextEn = s.TextEn, TextFa = s.TextFa, Severity = s.Severity, Frequency = s.Frequency, Population = s.Population, RevisionId = s.RevisionId })],
    };
}
