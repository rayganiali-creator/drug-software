using System.Globalization;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;

namespace MedSmarter.Modules.Medications;

/// <summary>Builds read models (DTOs) from the domain model. DTOs never expose entities.</summary>
public sealed class MedicationReader(IMedicationRepository repo)
{
    public const string DemoNotice = "DEMO DATA - NOT FOR CLINICAL USE. Fictional record created for testing.";
    public const string ValidatedNotice = "Source-validated reference information. It does not replace professional judgement.";
    public const string UnverifiedNotice = "NOT VALIDATED - this information has not been verified against a licensed source. Do not use for clinical decisions.";

    public static LocalizedText Text(string? en, string? fa) => new(en, fa);

    public static string NoticeFor(Medication m) => m.IsDemo ? DemoNotice : m.Validation == ValidationStatus.Validated ? ValidatedNotice : UnverifiedNotice;

    public static ValidationStatus StatementValidation(KnowledgeRevision? rev, KnowledgeSource? src)
    {
        if (rev is null || src is null)
        {
            return ValidationStatus.Unverified;
        }

        if (src.Type == SourceType.Demo)
        {
            return ValidationStatus.Demo;
        }

        return rev.Status switch
        {
            RevisionStatus.Validated => src.RedistributionAllowed == true ? ValidationStatus.Validated : ValidationStatus.NeedsValidation,
            RevisionStatus.InReview => ValidationStatus.NeedsValidation,
            RevisionStatus.Rejected => ValidationStatus.Rejected,
            _ => ValidationStatus.Unverified,
        };
    }

    public static string StrengthSummary(IEnumerable<MedicationIngredient> ingredients) =>
        string.Join(" / ", ingredients.OrderBy(i => i.Order)
            .Where(i => i.StrengthValue is not null)
            .Select(i => $"{i.StrengthValue!.Value.ToString("0.####", CultureInfo.InvariantCulture)} {i.StrengthUnit}{(string.IsNullOrWhiteSpace(i.PerUnit) ? string.Empty : " / " + i.PerUnit)}"));

    public async Task<MedicationDetailDto> BuildDetailAsync(Medication m, CancellationToken ct)
    {
        var terms = (await repo.GetTermsAsync(m.Routes.Select(r => r.TermId).Concat(m.Classifications.Select(c => c.TermId)).Append(m.DosageFormId), ct)).ToDictionary(t => t.Id);
        var ingredients = (await repo.GetIngredientsAsync(m.Ingredients.Select(i => i.IngredientId), ct)).ToDictionary(i => i.Id);
        var brand = m.BrandId is { } b ? await repo.FindBrandAsync(b, ct) : null;
        var manufacturer = m.ManufacturerId is { } mf ? await repo.FindManufacturerAsync(mf, ct) : null;
        var interactions = await repo.FindInteractionsAsync(m.Ingredients.Select(i => i.IngredientId), ct);

        var revisionIds = m.Statements.Select(s => s.RevisionId).Concat(m.Identifiers.Select(i => i.SourceRevisionId)).Concat(interactions.Select(i => i.RevisionId)).Distinct().ToList();
        var revisions = (await repo.GetRevisionsAsync(revisionIds, ct)).ToDictionary(r => r.Id);
        var sources = new Dictionary<Guid, KnowledgeSource>();
        foreach (var sid in revisions.Values.Select(r => r.SourceId).Distinct())
        {
            if (await repo.FindSourceAsync(sid, ct) is { } s)
            {
                sources[sid] = s;
            }
        }

        ValidationStatus ValidationOf(Guid revisionId) =>
            revisions.TryGetValue(revisionId, out var r) ? StatementValidation(r, sources.GetValueOrDefault(r.SourceId)) : ValidationStatus.Unverified;

        var otherIngredientIds = interactions.SelectMany(i => new[] { i.IngredientAId, i.IngredientBId }).Distinct().Except(ingredients.Keys);
        foreach (var extra in await repo.GetIngredientsAsync(otherIngredientIds, ct))
        {
            ingredients[extra.Id] = extra;
        }

        Guid SourceOf(Guid revisionId) => revisions.TryGetValue(revisionId, out var r) ? r.SourceId : Guid.Empty;
        LocalizedText IngName(Guid id) => ingredients.TryGetValue(id, out var i) ? Text(i.NameEn, i.NameFa) : new LocalizedText(null, null);
        var mine = m.Ingredients.Select(i => i.IngredientId).ToHashSet();

        var statements = m.Statements.OrderBy(s => s.Kind).ThenBy(s => s.TextEn ?? s.TextFa, StringComparer.Ordinal)
            .Select(s => new StatementDto(s.Id, s.Kind, Text(s.TextEn, s.TextFa), s.Severity, s.Frequency, s.Population, s.RevisionId, SourceOf(s.RevisionId), ValidationOf(s.RevisionId))).ToList();
        var present = statements.Select(s => s.Kind).ToHashSet();

        return new MedicationDetailDto(
            m.Id, m.Version, Text(m.NameEn, m.NameFa),
            brand is null ? null : new BrandDto(brand.Id, Text(brand.NameEn, brand.NameFa), brand.ManufacturerId),
            manufacturer is null ? null : new ManufacturerDto(manufacturer.Id, Text(manufacturer.NameEn, manufacturer.NameFa), manufacturer.Country, manufacturer.ManufacturerCode),
            terms.TryGetValue(m.DosageFormId, out var df) ? Text(df.NameEn, df.NameFa) : new LocalizedText(null, null),
            [.. m.Routes.Select(r => terms.GetValueOrDefault(r.TermId)).Where(t => t is not null).Select(t => Text(t!.NameEn, t.NameFa))],
            StrengthSummary(m.Ingredients),
            [.. m.Ingredients.OrderBy(i => i.Order).Select(i => new MedicationIngredientDto(i.IngredientId, IngName(i.IngredientId), i.StrengthValue, i.StrengthUnit, i.PerUnit, i.Order))],
            [.. m.Classifications.Select(c => terms.GetValueOrDefault(c.TermId)).Where(t => t is not null).Select(t => new ReferenceTermDto(t!.Id, t.Kind, t.Code, Text(t.NameEn, t.NameFa)))],
            [.. m.Synonyms.Select(s => s.Text).Order(StringComparer.Ordinal)],
            [.. m.Identifiers.OrderBy(i => i.Scheme).Select(i => new IdentifierDto(i.Scheme, i.Value, i.SourceRevisionId))],
            statements,
            [.. Enum.GetValues<StatementKind>().Where(k => !present.Contains(k))],
            [.. interactions.Select(i =>
            {
                var thisSide = mine.Contains(i.IngredientAId) ? i.IngredientAId : i.IngredientBId;
                var other = thisSide == i.IngredientAId ? i.IngredientBId : i.IngredientAId;
                return new InteractionDto(i.Id, other, IngName(other), thisSide, i.Severity, Text(i.MechanismEn, i.MechanismFa), Text(i.ManagementEn, i.ManagementFa), i.RevisionId, SourceOf(i.RevisionId), ValidationOf(i.RevisionId));
            }).OrderByDescending(i => i.Severity)],
            [.. sources.Values.OrderBy(s => s.Name, StringComparer.Ordinal).Select(ToDto)],
            m.Lifecycle, m.Validation, m.IsDemo, m.UpdatedAt, NoticeFor(m));
    }

    public async Task<IReadOnlyList<MedicationSummaryDto>> BuildSummariesAsync(IReadOnlyList<(Medication M, int Score, string? MatchedOn)> items, CancellationToken ct)
    {
        var terms = (await repo.GetTermsAsync(items.Select(i => i.M.DosageFormId).Distinct(), ct)).ToDictionary(t => t.Id);
        var ingredients = (await repo.GetIngredientsAsync(items.SelectMany(i => i.M.Ingredients.Select(x => x.IngredientId)).Distinct(), ct)).ToDictionary(i => i.Id);
        var result = new List<MedicationSummaryDto>();
        foreach (var (m, score, matched) in items)
        {
            var brand = m.BrandId is { } b ? await repo.FindBrandAsync(b, ct) : null;
            result.Add(new MedicationSummaryDto(
                m.Id, Text(m.NameEn, m.NameFa), brand is null ? null : Text(brand.NameEn, brand.NameFa),
                terms.TryGetValue(m.DosageFormId, out var df) ? Text(df.NameEn, df.NameFa) : new LocalizedText(null, null),
                StrengthSummary(m.Ingredients),
                [.. m.Ingredients.OrderBy(i => i.Order).Select(i => ingredients.TryGetValue(i.IngredientId, out var ing) ? Text(ing.NameEn, ing.NameFa) : new LocalizedText(null, null))],
                m.Lifecycle, m.Validation, m.IsDemo, matched, score));
        }

        return result;
    }

    public static KnowledgeSourceDto ToDto(KnowledgeSource s) =>
        new(s.Id, s.Name, s.Publisher, s.Type, s.Url, s.Version, s.LicenseName, s.RedistributionAllowed, s.UsageRestrictions, s.ReceivedAt, s.Validation);

    public static KnowledgeRevisionDto ToDto(KnowledgeRevision r) => new(r.Id, r.SourceId, r.Label, r.ReceivedAt, r.Status, r.Notes);

    /// <summary>The RAG-ready projection. Only facts that carry a source; nothing is inferred.</summary>
    public static MedicationKnowledgeDocument ToKnowledgeDocument(MedicationDetailDto d)
    {
        static string Pick(LocalizedText t) => t.En ?? t.Fa ?? string.Empty;
        var names = new List<string>();
        foreach (var n in new[] { d.Name.En, d.Name.Fa, d.Brand?.Name.En, d.Brand?.Name.Fa })
        {
            if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n))
            {
                names.Add(n);
            }
        }

        return new MedicationKnowledgeDocument(
            d.Id, d.Version, names, d.Synonyms,
            [.. d.Ingredients.Select(i => Pick(i.Name))], Pick(d.DosageForm), d.StrengthSummary,
            [.. d.Statements.Select(s => new KnowledgeStatementView(s.Kind, Pick(s.Text), s.SourceId, s.Validation))],
            [.. d.Interactions.Select(i => new KnowledgeInteractionView(Pick(i.OtherIngredientName), i.Severity, Pick(i.Mechanism), i.SourceId, i.Validation))],
            [.. d.Sources.Select(s => new KnowledgeSourceRef(s.Id, s.Name, s.Version, s.Publisher, s.ReceivedAt, s.Validation))],
            d.Validation, d.IsDemo, d.UpdatedAt, d.Notice);
    }
}
