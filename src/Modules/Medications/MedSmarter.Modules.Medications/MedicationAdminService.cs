using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;

namespace MedSmarter.Modules.Medications;

public sealed class MedicationAdminService(IMedicationRepository repo, MedicationReader reader, IAuditWriter audit, IClock clock) : IMedicationAdminService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static OperationResult<T> Invalid<T>(IEnumerable<string> codes) => OperationResult.Fail<T>(MedicationError.Validation, string.Join(';', codes));

    public async Task<OperationResult<MedicationDetailDto>> CreateAsync(Guid actorUserId, MedicationDraft draft, string source, string? correlationId, CancellationToken ct = default)
    {
        var med = new Medication { Id = Guid.CreateVersion7(), CreatedAt = clock.UtcNow, Lifecycle = LifecycleStatus.Draft };
        var applied = await ApplyAsync(med, draft, ct);
        if (applied is not null)
        {
            return applied;
        }

        med.Validation = draft.IsDemo ? ValidationStatus.Demo : ValidationStatus.Unverified;
        med.UpdatedAt = med.CreatedAt;
        med.UpdatedBy = actorUserId;
        var saved = await SaveAsync(med, null, "created", actorUserId, ct);
        if (!saved.Succeeded)
        {
            return saved;
        }

        await Audit(AuditActions.MedicationCreated, actorUserId, med.Id, source, correlationId, null);
        return saved;
    }

    public async Task<OperationResult<MedicationDetailDto>> UpdateAsync(Guid actorUserId, Guid id, int expectedVersion, MedicationDraft draft, string reason, string source, string? correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 300)
        {
            return Invalid<MedicationDetailDto>(["reason.required"]);
        }

        var med = await repo.FindMedicationAsync(id, ct);
        if (med is null)
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.NotFound);
        }

        if (med.Version != expectedVersion)
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.Conflict, "version.mismatch");
        }

        if (draft.IsDemo != med.IsDemo)
        {
            return Invalid<MedicationDetailDto>(["demo.immutable"]);
        }

        var applied = await ApplyAsync(med, draft, ct);
        if (applied is not null)
        {
            return applied;
        }

        // Content changed: earlier validation no longer applies.
        med.Validation = med.IsDemo ? ValidationStatus.Demo : ValidationStatus.Unverified;
        med.UpdatedBy = actorUserId;
        var saved = await SaveAsync(med, expectedVersion, reason.Trim(), actorUserId, ct);
        if (saved.Succeeded)
        {
            await Audit(AuditActions.MedicationUpdated, actorUserId, id, source, correlationId, null);
        }

        return saved;
    }

    public async Task<OperationResult<MedicationDetailDto>> SetLifecycleAsync(Guid actorUserId, Guid id, int expectedVersion, LifecycleStatus status, string source, string? correlationId, CancellationToken ct = default)
    {
        var med = await repo.FindMedicationAsync(id, ct);
        if (med is null)
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.NotFound);
        }

        if (med.Version != expectedVersion)
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.Conflict, "version.mismatch");
        }

        if (status == LifecycleStatus.Draft && med.Lifecycle != LifecycleStatus.Draft)
        {
            return Invalid<MedicationDetailDto>(["lifecycle.transition"]);
        }

        med.Lifecycle = status;
        med.UpdatedBy = actorUserId;
        var saved = await SaveAsync(med, expectedVersion, $"lifecycle:{status}", actorUserId, ct);
        if (saved.Succeeded)
        {
            await Audit(AuditActions.MedicationLifecycleChanged, actorUserId, id, source, correlationId, status.ToString());
        }

        return saved;
    }

    public async Task<OperationResult<MedicationDetailDto>> SetValidationAsync(Guid actorUserId, Guid id, int expectedVersion, ValidationStatus status, Guid revisionId, string source, string? correlationId, CancellationToken ct = default)
    {
        var med = await repo.FindMedicationAsync(id, ct);
        if (med is null)
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.NotFound);
        }

        if (med.Version != expectedVersion)
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.Conflict, "version.mismatch");
        }

        if (med.IsDemo || status == ValidationStatus.Demo)
        {
            return Invalid<MedicationDetailDto>(["demo.cannot_change_validation"]); // fictional data can never become "verified"
        }

        var rev = await repo.FindRevisionAsync(revisionId, ct);
        var src = rev is null ? null : await repo.FindSourceAsync(rev.SourceId, ct);
        if (rev is null || src is null)
        {
            return Invalid<MedicationDetailDto>(["revision.unknown"]);
        }

        if (status == ValidationStatus.Validated)
        {
            // Separation of duties: whoever created or edited the content may not be the one who declares it validated.
            var versions = await repo.GetVersionsAsync(id, ct);
            if (versions.Any(v => v.ChangedBy == actorUserId && !v.ChangeReason.StartsWith("validation:", StringComparison.Ordinal) && !v.ChangeReason.StartsWith("lifecycle:", StringComparison.Ordinal)))
            {
                await audit.WriteAsync(new AuditEvent(AuditActions.MedicationValidationChanged, AuditResult.Denied, actorUserId, "medication", id.ToString(), null, source, correlationId, "separation_of_duties"), ct);
                return OperationResult.Fail<MedicationDetailDto>(MedicationError.Forbidden, "separation_of_duties");
            }

            var problems = new List<string>();
            if (src.Type == SourceType.Demo)
            {
                problems.Add("source.demo");
            }

            if (rev.Status != RevisionStatus.Validated)
            {
                problems.Add("revision.not_validated");
            }

            if (src.RedistributionAllowed != true)
            {
                problems.Add("license.redistribution_not_confirmed");
            }

            if (problems.Count > 0)
            {
                return Invalid<MedicationDetailDto>(problems);
            }
        }

        med.Validation = status;
        med.UpdatedBy = actorUserId;
        var saved = await SaveAsync(med, expectedVersion, $"validation:{status}", actorUserId, ct);
        if (saved.Succeeded)
        {
            await Audit(AuditActions.MedicationValidationChanged, actorUserId, id, source, correlationId, status.ToString());
        }

        return saved;
    }

    public async Task<OperationResult<IReadOnlyList<MedicationVersionDto>>> ListVersionsAsync(Guid id, CancellationToken ct = default)
    {
        if (await repo.FindMedicationAsync(id, ct) is null)
        {
            return OperationResult.Fail<IReadOnlyList<MedicationVersionDto>>(MedicationError.NotFound);
        }

        var versions = await repo.GetVersionsAsync(id, ct);
        return OperationResult.Ok<IReadOnlyList<MedicationVersionDto>>([.. versions.OrderBy(v => v.VersionNumber).Select(v => new MedicationVersionDto(v.VersionNumber, v.ChangedAt, v.ChangedBy, v.ChangeReason))]);
    }

    // ---------- reference data ----------

    public async Task<OperationResult<IngredientDto>> CreateIngredientAsync(Guid actorUserId, NewIngredient value, CancellationToken ct = default)
    {
        if (MedicationValidator.ValidateIngredient(value) is { } problem)
        {
            return Invalid<IngredientDto>([problem]);
        }

        var ing = new ActiveIngredient
        {
            Id = Guid.CreateVersion7(), NameEn = value.Name.En?.Trim(), NameFa = value.Name.Fa?.Trim(), AtcCode = value.AtcCode, Lifecycle = LifecycleStatus.Active, Validation = ValidationStatus.Unverified,
        };
        ing.Synonyms = [.. value.Synonyms.Select(s => new IngredientSynonym { Id = Guid.CreateVersion7(), IngredientId = ing.Id, Text = s.Trim() })];
        await repo.AddIngredientAsync(ing, ct);
        return OperationResult.Ok<IngredientDto>(ToDto(ing));
    }

    public async Task<OperationResult<ManufacturerDto>> CreateManufacturerAsync(Guid actorUserId, NewManufacturer value, CancellationToken ct = default)
    {
        if (MedicationValidator.ValidateManufacturer(value) is { } problem)
        {
            return Invalid<ManufacturerDto>([problem]);
        }

        var m = new Manufacturer { Id = Guid.CreateVersion7(), NameEn = value.Name.En?.Trim(), NameFa = value.Name.Fa?.Trim(), Country = value.Country, ManufacturerCode = value.ManufacturerCode };
        try
        {
            await repo.AddManufacturerAsync(m, ct);
        }
        catch (DuplicateRecordException)
        {
            return OperationResult.Fail<ManufacturerDto>(MedicationError.Conflict, "manufacturer_code.duplicate");
        }

        return OperationResult.Ok<ManufacturerDto>(new ManufacturerDto(m.Id, new LocalizedText(m.NameEn, m.NameFa), m.Country, m.ManufacturerCode));
    }

    public async Task<OperationResult<BrandDto>> CreateBrandAsync(Guid actorUserId, NewBrand value, CancellationToken ct = default)
    {
        if (MedicationValidator.TextProblem(value.Name, MedicationValidator.MaxName, "name") is { } problem)
        {
            return Invalid<BrandDto>([problem]);
        }

        if (value.ManufacturerId is { } mf && await repo.FindManufacturerAsync(mf, ct) is null)
        {
            return Invalid<BrandDto>(["manufacturer.unknown"]);
        }

        var b = new Brand { Id = Guid.CreateVersion7(), NameEn = value.Name.En?.Trim(), NameFa = value.Name.Fa?.Trim(), ManufacturerId = value.ManufacturerId };
        await repo.AddBrandAsync(b, ct);
        return OperationResult.Ok<BrandDto>(new BrandDto(b.Id, new LocalizedText(b.NameEn, b.NameFa), b.ManufacturerId));
    }

    public async Task<OperationResult<ReferenceTermDto>> CreateReferenceTermAsync(Guid actorUserId, NewReferenceTerm value, CancellationToken ct = default)
    {
        if (MedicationValidator.ValidateTerm(value) is { } problem)
        {
            return Invalid<ReferenceTermDto>([problem]);
        }

        if (await repo.FindTermAsync(value.Kind, value.Code, ct) is not null)
        {
            return OperationResult.Fail<ReferenceTermDto>(MedicationError.Conflict, "code.duplicate");
        }

        var t = new ReferenceTerm { Id = Guid.CreateVersion7(), Kind = value.Kind, Code = value.Code, NameEn = value.Name.En?.Trim(), NameFa = value.Name.Fa?.Trim() };
        try
        {
            await repo.AddTermAsync(t, ct);
        }
        catch (DuplicateRecordException)
        {
            return OperationResult.Fail<ReferenceTermDto>(MedicationError.Conflict, "code.duplicate");
        }

        return OperationResult.Ok<ReferenceTermDto>(new ReferenceTermDto(t.Id, t.Kind, t.Code, new LocalizedText(t.NameEn, t.NameFa)));
    }

    public async Task<OperationResult<InteractionDto>> UpsertInteractionAsync(Guid actorUserId, NewInteraction value, string source, string? correlationId, CancellationToken ct = default)
    {
        if (value.IngredientAId == value.IngredientBId)
        {
            return Invalid<InteractionDto>(["interaction.same_ingredient"]);
        }

        var a = await repo.FindIngredientAsync(value.IngredientAId, ct);
        var b = await repo.FindIngredientAsync(value.IngredientBId, ct);
        var rev = await repo.FindRevisionAsync(value.RevisionId, ct);
        if (a is null || b is null)
        {
            return Invalid<InteractionDto>(["ingredient.unknown"]);
        }

        if (rev is null)
        {
            return Invalid<InteractionDto>(["revision.unknown"]); // an interaction without a source is not accepted
        }

        if ((!value.Mechanism.IsEmpty && MedicationValidator.TextProblem(value.Mechanism, MedicationValidator.MaxStatement, "mechanism") is not null)
            || (!value.Management.IsEmpty && MedicationValidator.TextProblem(value.Management, MedicationValidator.MaxStatement, "management") is not null))
        {
            return Invalid<InteractionDto>(["interaction.text"]);
        }

        // canonical order so (A,B) and (B,A) are the same row
        var (first, second) = value.IngredientAId.CompareTo(value.IngredientBId) < 0 ? (a, b) : (b, a);
        var row = new DrugInteraction
        {
            Id = Guid.CreateVersion7(), IngredientAId = first.Id, IngredientBId = second.Id, Severity = value.Severity, RevisionId = value.RevisionId,
            MechanismEn = value.Mechanism.En, MechanismFa = value.Mechanism.Fa, ManagementEn = value.Management.En, ManagementFa = value.Management.Fa,
        };
        await repo.UpsertInteractionAsync(row, ct);
        await Audit(AuditActions.MedicationUpdated, actorUserId, row.Id, source, correlationId, "interaction");
        var src = await repo.FindSourceAsync(rev.SourceId, ct);
        return OperationResult.Ok<InteractionDto>(new InteractionDto(row.Id, second.Id, new LocalizedText(second.NameEn, second.NameFa), first.Id, row.Severity,
            new LocalizedText(row.MechanismEn, row.MechanismFa), new LocalizedText(row.ManagementEn, row.ManagementFa), row.RevisionId, rev.SourceId, MedicationReader.StatementValidation(rev, src)));
    }

    // ---------- internals ----------

    /// <summary>Copies the draft onto the entity after resolving every reference. Returns a failure, or null on success.</summary>
    private async Task<OperationResult<MedicationDetailDto>?> ApplyAsync(Medication med, MedicationDraft d, CancellationToken ct)
    {
        var errors = MedicationValidator.ValidateDraft(d);
        if (errors.Count > 0)
        {
            return Invalid<MedicationDetailDto>(errors);
        }

        var df = await repo.FindTermAsync(ReferenceKind.DosageForm, d.DosageFormCode, ct);
        if (df is null)
        {
            errors.Add("dosage_form.unknown");
        }

        var routes = new List<ReferenceTerm>();
        foreach (var code in d.RouteCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await repo.FindTermAsync(ReferenceKind.Route, code, ct) is { } t) { routes.Add(t); } else { errors.Add("route.unknown"); }
        }

        var classes = new List<ReferenceTerm>();
        foreach (var code in d.ClassCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var t = await repo.FindTermAsync(ReferenceKind.TherapeuticClass, code, ct) ?? await repo.FindTermAsync(ReferenceKind.DrugClass, code, ct);
            if (t is not null) { classes.Add(t); } else { errors.Add("class.unknown"); }
        }

        if (d.BrandId is { } brandId && await repo.FindBrandAsync(brandId, ct) is null) { errors.Add("brand.unknown"); }
        if (d.ManufacturerId is { } mfId && await repo.FindManufacturerAsync(mfId, ct) is null) { errors.Add("manufacturer.unknown"); }

        var ingredients = await repo.GetIngredientsAsync(d.Ingredients.Select(i => i.IngredientId), ct);
        if (ingredients.Count != d.Ingredients.Count) { errors.Add("ingredient.unknown"); }

        var revisionIds = d.Identifiers.Select(i => i.SourceRevisionId!.Value).Concat(d.Statements.Select(s => s.RevisionId)).Distinct().ToList();
        var revisions = (await repo.GetRevisionsAsync(revisionIds, ct)).ToDictionary(r => r.Id);
        foreach (var rid in revisionIds)
        {
            if (!revisions.TryGetValue(rid, out var rev)) { errors.Add("revision.unknown"); continue; }
            var src = await repo.FindSourceAsync(rev.SourceId, ct);
            var isDemoSource = src?.Type == SourceType.Demo;
            if (src is null || isDemoSource != d.IsDemo) { errors.Add("source.demo_mismatch"); } // fictional and real information never mix
        }

        foreach (var id in d.Identifiers)
        {
            if (await repo.FindByIdentifierAsync(id.Scheme, id.Value, ct) is { } other && other.Id != med.Id) { errors.Add("identifier.in_use"); }
        }

        if (errors.Count > 0)
        {
            return errors.Contains("identifier.in_use") && errors.Count == 1
                ? OperationResult.Fail<MedicationDetailDto>(MedicationError.Conflict, "identifier.in_use")
                : Invalid<MedicationDetailDto>(errors.Distinct());
        }

        med.NameEn = d.Name.En?.Trim();
        med.NameFa = d.Name.Fa?.Trim();
        med.BrandId = d.BrandId;
        med.ManufacturerId = d.ManufacturerId;
        med.DosageFormId = df!.Id;
        med.IsDemo = d.IsDemo;
        med.Routes = [.. routes.Select(t => new MedicationRoute { MedicationId = med.Id, TermId = t.Id })];
        med.Classifications = [.. classes.Select(t => new MedicationClassification { MedicationId = med.Id, TermId = t.Id })];
        med.Ingredients = [.. d.Ingredients.Select((i, n) => new MedicationIngredient { MedicationId = med.Id, IngredientId = i.IngredientId, StrengthValue = i.StrengthValue, StrengthUnit = i.StrengthUnit, PerUnit = i.PerUnit, Order = n })];
        med.Synonyms = [.. d.Synonyms.Select(s => new MedicationSynonym { Id = Guid.CreateVersion7(), MedicationId = med.Id, Text = s.Trim() })];
        med.Identifiers = [.. d.Identifiers.Select(i => new MedicationIdentifier { Id = Guid.CreateVersion7(), MedicationId = med.Id, Scheme = i.Scheme, Value = i.Value.Trim(), SourceRevisionId = i.SourceRevisionId!.Value })];
        med.Statements = [.. d.Statements.Select(s => new MedicationStatement
        {
            Id = Guid.CreateVersion7(), MedicationId = med.Id, Kind = s.Kind, TextEn = s.Text.En?.Trim(), TextFa = s.Text.Fa?.Trim(), Severity = s.Severity?.ToLowerInvariant(), Frequency = s.Frequency?.ToLowerInvariant(), Population = s.Population?.Trim(), RevisionId = s.RevisionId,
        })];
        return null;
    }

    private async Task<OperationResult<MedicationDetailDto>> SaveAsync(Medication med, int? expectedVersion, string reason, Guid actorUserId, CancellationToken ct)
    {
        med.Version = (expectedVersion ?? 0) + 1;
        med.UpdatedAt = clock.UtcNow;
        var terms = await BuildSearchTermsAsync(med, ct);
        var detail = await reader.BuildDetailAsync(med, ct);
        var version = new MedicationVersion
        {
            Id = Guid.CreateVersion7(), MedicationId = med.Id, VersionNumber = med.Version, SnapshotJson = JsonSerializer.Serialize(detail, Json),
            ChangedAt = med.UpdatedAt, ChangedBy = actorUserId, ChangeReason = reason,
        };
        if (!await repo.SaveMedicationAsync(med, terms, version, expectedVersion, ct))
        {
            return OperationResult.Fail<MedicationDetailDto>(MedicationError.Conflict, "version.mismatch");
        }

        return OperationResult.Ok<MedicationDetailDto>(detail);
    }

    /// <summary>Rebuilds the derived search index of one medication from its sources of truth.</summary>
    private async Task<IReadOnlyList<MedicationSearchTerm>> BuildSearchTermsAsync(Medication med, CancellationToken ct)
    {
        var list = new List<MedicationSearchTerm>();
        void Add(string? text, NameKind kind)
        {
            var n = TextNormalizer.Normalize(text);
            if (n.Length > 0 && !list.Any(t => t.Normalized == n && t.Kind == kind))
            {
                list.Add(new MedicationSearchTerm { Id = Guid.CreateVersion7(), MedicationId = med.Id, Normalized = n, Display = text!.Trim(), Kind = kind });
            }
        }

        var brand = med.BrandId is { } b ? await repo.FindBrandAsync(b, ct) : null;
        var nameKind = brand is null ? NameKind.Generic : NameKind.Brand;
        Add(med.NameEn, nameKind);
        Add(med.NameFa, nameKind);
        if (brand is not null) { Add(brand.NameEn, NameKind.Brand); Add(brand.NameFa, NameKind.Brand); }
        foreach (var ing in await repo.GetIngredientsAsync(med.Ingredients.Select(i => i.IngredientId), ct))
        {
            Add(ing.NameEn, NameKind.Ingredient);
            Add(ing.NameFa, NameKind.Ingredient);
            foreach (var s in ing.Synonyms) { Add(s.Text, NameKind.Ingredient); }
        }

        foreach (var s in med.Synonyms) { Add(s.Text, NameKind.Synonym); }
        foreach (var i in med.Identifiers) { Add(i.Value, NameKind.Code); }
        return list;
    }

    private static IngredientDto ToDto(ActiveIngredient i) => new(i.Id, new LocalizedText(i.NameEn, i.NameFa), i.AtcCode, [.. i.Synonyms.Select(s => s.Text)], i.Lifecycle, i.Validation);

    private Task Audit(string action, Guid actor, Guid resourceId, string source, string? correlationId, string? reason) =>
        audit.WriteAsync(new AuditEvent(action, AuditResult.Success, actor, "medication", resourceId.ToString(), null, source, correlationId, reason));
}
