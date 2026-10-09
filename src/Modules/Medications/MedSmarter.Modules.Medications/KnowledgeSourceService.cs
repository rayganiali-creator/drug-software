using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;

namespace MedSmarter.Modules.Medications;

public sealed class KnowledgeSourceService(IMedicationRepository repo, IAuditWriter audit, IClock clock) : IKnowledgeSourceService
{
    public async Task<OperationResult<KnowledgeSourceDto>> RegisterSourceAsync(Guid actorUserId, NewKnowledgeSource value, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 200) { errors.Add("name.invalid"); }
        if (string.IsNullOrWhiteSpace(value.Publisher) || value.Publisher.Length > 200) { errors.Add("publisher.required"); }
        if (string.IsNullOrWhiteSpace(value.Version) || value.Version.Length > 64) { errors.Add("version.required"); }
        if (string.IsNullOrWhiteSpace(value.LicenseName) || value.LicenseName.Length > 200) { errors.Add("license.required"); } // provenance requires a stated licence
        if (value.Url is not null && (!Uri.TryCreate(value.Url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))) { errors.Add("url.invalid"); }
        if (value.UsageRestrictions is { Length: > 1000 }) { errors.Add("restrictions.too_long"); }
        if (errors.Count > 0)
        {
            return OperationResult.Fail<KnowledgeSourceDto>(MedicationError.Validation, string.Join(';', errors));
        }

        var s = new KnowledgeSource
        {
            Id = Guid.CreateVersion7(), Name = value.Name.Trim(), Publisher = value.Publisher.Trim(), Type = value.Type, Url = value.Url, Version = value.Version.Trim(), LicenseName = value.LicenseName.Trim(),
            RedistributionAllowed = value.RedistributionAllowed, UsageRestrictions = value.UsageRestrictions, ReceivedAt = clock.UtcNow,
            Validation = value.Type == SourceType.Demo ? ValidationStatus.Demo : ValidationStatus.Unverified,
        };
        try
        {
            await repo.AddSourceAsync(s, ct);
        }
        catch (DuplicateRecordException)
        {
            return OperationResult.Fail<KnowledgeSourceDto>(MedicationError.Conflict, "source.duplicate");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.KnowledgeSourceRegistered, AuditResult.Success, actorUserId, "knowledge-source", s.Id.ToString(), null, source, correlationId), ct);
        return OperationResult.Ok<KnowledgeSourceDto>(MedicationReader.ToDto(s));
    }

    public async Task<OperationResult<KnowledgeRevisionDto>> AddRevisionAsync(Guid actorUserId, NewRevision value, string source, string? correlationId, CancellationToken ct = default)
    {
        if (await repo.FindSourceAsync(value.SourceId, ct) is null)
        {
            return OperationResult.Fail<KnowledgeRevisionDto>(MedicationError.NotFound);
        }

        if (string.IsNullOrWhiteSpace(value.Label) || value.Label.Length > 100 || value.Notes is { Length: > 1000 })
        {
            return OperationResult.Fail<KnowledgeRevisionDto>(MedicationError.Validation, "label.invalid");
        }

        var r = new KnowledgeRevision { Id = Guid.CreateVersion7(), SourceId = value.SourceId, Label = value.Label.Trim(), Notes = value.Notes, ReceivedAt = clock.UtcNow, Status = RevisionStatus.Draft };
        try
        {
            await repo.AddRevisionAsync(r, ct);
        }
        catch (DuplicateRecordException)
        {
            return OperationResult.Fail<KnowledgeRevisionDto>(MedicationError.Conflict, "revision.duplicate");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.KnowledgeRevisionAdded, AuditResult.Success, actorUserId, "knowledge-revision", r.Id.ToString(), null, source, correlationId), ct);
        return OperationResult.Ok<KnowledgeRevisionDto>(MedicationReader.ToDto(r));
    }

    public async Task<OperationResult<KnowledgeRevisionDto>> SetRevisionStatusAsync(Guid actorUserId, Guid revisionId, RevisionStatus status, string source, string? correlationId, CancellationToken ct = default)
    {
        var r = await repo.FindRevisionAsync(revisionId, ct);
        var src = r is null ? null : await repo.FindSourceAsync(r.SourceId, ct);
        if (r is null || src is null)
        {
            return OperationResult.Fail<KnowledgeRevisionDto>(MedicationError.NotFound);
        }

        if (src.Type == SourceType.Demo && status == RevisionStatus.Validated)
        {
            return OperationResult.Fail<KnowledgeRevisionDto>(MedicationError.Validation, "source.demo");
        }

        r.Status = status;
        await repo.UpdateRevisionAsync(r, ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.MedicationValidationChanged, AuditResult.Success, actorUserId, "knowledge-revision", r.Id.ToString(), null, source, correlationId, status.ToString()), ct);
        return OperationResult.Ok<KnowledgeRevisionDto>(MedicationReader.ToDto(r));
    }

    public async Task<IReadOnlyList<KnowledgeSourceDto>> ListSourcesAsync(CancellationToken ct = default) =>
        [.. (await repo.ListSourcesAsync(ct)).Select(MedicationReader.ToDto)];

    public async Task<IReadOnlyList<KnowledgeRevisionDto>> ListRevisionsAsync(Guid sourceId, CancellationToken ct = default) =>
        [.. (await repo.ListRevisionsAsync(sourceId, ct)).Select(MedicationReader.ToDto)];
}
