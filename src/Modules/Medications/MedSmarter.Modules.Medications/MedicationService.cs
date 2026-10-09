using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;

namespace MedSmarter.Modules.Medications;

public sealed class MedicationService(IMedicationRepository repo, MedicationReader reader) : IMedicationService
{
    public async Task<PagedResult<MedicationSummaryDto>> SearchAsync(MedicationSearchQuery query, CancellationToken ct = default)
    {
        var (valid, error) = SearchQueryValidator.Validate(query);
        if (valid is null)
        {
            throw new MedicationSearchException(error!);
        }

        var visible = await repo.VisibleMedicationIdsAsync(valid.IncludeInactive, ct);
        IReadOnlyList<RankedHit> hits;
        if (valid.Normalized.Length == 0)
        {
            // Browse: no query means "everything visible", alphabetical.
            hits = [.. visible.Select(id => new RankedHit(id, 1, string.Empty))];
        }
        else
        {
            var terms = await repo.FindTermsAsync(valid.Normalized, SearchLimits.MaxCandidates, ct);
            hits = SearchRanker.Rank(terms, valid.Normalized);
        }

        var inScope = hits.Where(h => visible.Contains(h.MedicationId)).ToList();
        var meds = (await repo.GetMedicationsAsync(inScope.Select(h => h.MedicationId), ct)).ToDictionary(m => m.Id);
        var ordered = inScope.Where(h => meds.ContainsKey(h.MedicationId))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => TextNormalizer.Normalize(meds[h.MedicationId].NameEn ?? meds[h.MedicationId].NameFa), StringComparer.Ordinal)
            .ThenBy(h => h.MedicationId)
            .ToList();
        var page = ordered.Skip(valid.Offset).Take(valid.Limit).ToList();
        var items = await reader.BuildSummariesAsync([.. page.Select(h => (meds[h.MedicationId], h.Score, (string?)(h.MatchedOn.Length == 0 ? null : h.MatchedOn)))], ct);
        return new PagedResult<MedicationSummaryDto>(items, ordered.Count, valid.Limit, valid.Offset);
    }

    public async Task<OperationResult<MedicationDetailDto>> GetAsync(Guid id, bool includeNonActive = false, CancellationToken ct = default)
    {
        var m = await repo.FindMedicationAsync(id, ct);
        if (m is null || (!includeNonActive && m.Lifecycle != LifecycleStatus.Active))
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.NotFound);
        }

        return OperationResult.Ok<MedicationDetailDto>(await reader.BuildDetailAsync(m, ct));
    }

    public async Task<OperationResult<MedicationKnowledgeDocument>> GetKnowledgeDocumentAsync(Guid id, bool includeNonActive = false, CancellationToken ct = default)
    {
        var detail = await GetAsync(id, includeNonActive, ct);
        return detail.Succeeded
            ? OperationResult.Ok<MedicationKnowledgeDocument>(MedicationReader.ToKnowledgeDocument(detail.Value!))
            : OperationResult.Fail<MedicationKnowledgeDocument>(detail.Error);
    }

    public async Task<OperationResult<ManufacturerDto>> GetManufacturerAsync(Guid id, CancellationToken ct = default) =>
        await repo.FindManufacturerAsync(id, ct) is { } m
            ? OperationResult.Ok(new ManufacturerDto(m.Id, new LocalizedText(m.NameEn, m.NameFa), m.Country, m.ManufacturerCode))
            : OperationResult.Fail<ManufacturerDto>(MedicationError.NotFound);
}

/// <summary>Raised for an invalid search query; carries only a machine-readable code.</summary>
public sealed class MedicationSearchException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
