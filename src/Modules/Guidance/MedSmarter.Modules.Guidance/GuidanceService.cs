using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Guidance;

public sealed class GuidanceService(IDbContextFactory<GuidanceDbContext> factory, IClock clock, IAuditWriter audit, IGuidanceComposer composer, IPatientDirectory patients) : IGuidanceService
{
    public const string DemoNotice = "DEMO DATA - NOT FOR CLINICAL USE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Allowed moves and who may make them. Patients mark messages seen or resolved; reviewing and referring belong to professionals.
    private static readonly (GuidanceStatus From, GuidanceStatus To, bool Professional)[] Moves =
    [
        (GuidanceStatus.Sent, GuidanceStatus.Seen, false),
        (GuidanceStatus.Sent, GuidanceStatus.Resolved, false),
        (GuidanceStatus.Seen, GuidanceStatus.Resolved, false),
        (GuidanceStatus.Seen, GuidanceStatus.Reviewed, true),
        (GuidanceStatus.Sent, GuidanceStatus.Reviewed, true),
        (GuidanceStatus.Reviewed, GuidanceStatus.Referred, true),
        (GuidanceStatus.Reviewed, GuidanceStatus.Resolved, true),
        (GuidanceStatus.Referred, GuidanceStatus.Resolved, true),
        (GuidanceStatus.Referred, GuidanceStatus.Resolved, false),
    ];

    public static bool MoveAllowed(GuidanceStatus from, GuidanceStatus to, bool professional) => Moves.Any(m => m.From == from && m.To == to && (!m.Professional || professional));

    public async Task<GuidanceOutcome<GuidanceMessageDto>> CreateAsync(Guid subjectId, GuidanceRequest request, bool isDemo, string source, string? correlationId, CancellationToken ct = default)
    {
        var created = await CreateCoreAsync(subjectId, request, null, isDemo, ct);
        return created.Succeeded ? GuidanceOutcome.Ok(created.Value!.Message) : GuidanceOutcome.Fail<GuidanceMessageDto>(created.Error, created.Detail);
    }

    public async Task<GuidanceOutcome<GuidanceCreated>> CreateFromOriginAsync(Guid subjectId, GuidanceRequest request, GuidanceOrigin origin, bool isDemo, string source, string? correlationId, CancellationToken ct = default) =>
        await CreateCoreAsync(subjectId, request, origin, isDemo, ct);

    public async Task<GuidanceOutcome<IReadOnlyList<GuidanceOriginRef>>> ListOpenByOriginAsync(Guid subjectId, string originKind, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return GuidanceOutcome.Fail<IReadOnlyList<GuidanceOriginRef>>(GuidanceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Messages.AsNoTracking().Where(m => m.PatientId == patient.PatientId && m.OriginKind == originKind && m.Status != GuidanceStatus.Resolved)
            .OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct);
        return GuidanceOutcome.Ok<IReadOnlyList<GuidanceOriginRef>>([.. rows.Select(r => new GuidanceOriginRef(r.Id, r.Status, OriginOf(r)!))]);
    }

    private async Task<GuidanceOutcome<GuidanceCreated>> CreateCoreAsync(Guid subjectId, GuidanceRequest request, GuidanceOrigin? origin, bool isDemo, CancellationToken ct)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return GuidanceOutcome.Fail<GuidanceCreated>(GuidanceError.NotFound, "patient.not_found");
        }

        GuidanceComposition composition;
        try
        {
            composition = composer.Compose(request);
        }
        catch (ArgumentException)
        {
            return GuidanceOutcome.Fail<GuidanceCreated>(GuidanceError.Validation, "template.unknown");
        }

        if (!composition.IsValid)
        {
            // A message that breaks the policy is never stored or shown.
            return GuidanceOutcome.Fail<GuidanceCreated>(GuidanceError.Validation, string.Join(';', composition.Violations.Select(v => $"{v.Code}@{v.Part}")));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (origin is not null)
        {
            // One open message per finding; a resolved one stays resolved unless the data changed after it was resolved.
            var existing = await db.Messages.AsNoTracking().Where(m => m.PatientId == patient.PatientId && m.OriginFindingKey == origin.FindingKey).OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
            if (existing is not null && (existing.Status != GuidanceStatus.Resolved || (existing.ResolvedAt is { } resolvedAt && origin.DataAsOf <= resolvedAt)))
            {
                return GuidanceOutcome.Ok(new GuidanceCreated(ToDto(existing, includeProfessional: false), true));
            }
        }

        var row = new GuidanceMessageRow
        {
            Id = Guid.CreateVersion7(), PatientId = patient.PatientId, TemplateKey = request.HasSufficientData ? request.TemplateKey : "data.insufficient", Level = composition.Level, Status = GuidanceStatus.Sent,
            Locale = request.Locale.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "fa" : "en", Confidence = composition.Professional.Confidence,
            PatientJson = JsonSerializer.Serialize(composition.Patient, Json), ProfessionalJson = JsonSerializer.Serialize(composition.Professional, Json), CreatedAt = clock.UtcNow, IsDemo = isDemo,
            OriginKind = origin?.Kind, OriginAssessmentId = origin?.AssessmentId, OriginRuleId = origin?.RuleId, OriginRuleVersion = origin?.RuleVersion, OriginFindingKey = origin?.FindingKey, OriginDataAsOf = origin?.DataAsOf,
        };
        db.Messages.Add(row);
        await db.SaveChangesAsync(ct);
        return GuidanceOutcome.Ok(new GuidanceCreated(ToDto(row, includeProfessional: false), false));
    }

    private static GuidanceOrigin? OriginOf(GuidanceMessageRow r) =>
        r.OriginKind is null ? null : new GuidanceOrigin(r.OriginKind, r.OriginAssessmentId ?? Guid.Empty, r.OriginRuleId ?? string.Empty, r.OriginRuleVersion ?? 0, r.OriginFindingKey ?? string.Empty, r.OriginDataAsOf ?? r.CreatedAt);

    public async Task<GuidanceOutcome<IReadOnlyList<GuidanceMessageDto>>> ListForPatientAsync(Guid subjectId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return GuidanceOutcome.Fail<IReadOnlyList<GuidanceMessageDto>>(GuidanceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Messages.AsNoTracking().Where(m => m.PatientId == patient.PatientId).OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct);
        return GuidanceOutcome.Ok<IReadOnlyList<GuidanceMessageDto>>([.. rows.Select(r => ToDto(r, includeProfessional: false))]); // patients never receive the professional view
    }

    public async Task<GuidanceOutcome<GuidanceMessageDto>> SetStatusAsync(Guid actorUserId, Guid subjectId, Guid id, GuidanceStatus target, bool actorIsProfessional, string source, string? correlationId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return GuidanceOutcome.Fail<GuidanceMessageDto>(GuidanceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Messages.FirstOrDefaultAsync(m => m.Id == id && m.PatientId == patient.PatientId, ct);
        if (row is null)
        {
            return GuidanceOutcome.Fail<GuidanceMessageDto>(GuidanceError.NotFound, "message.not_found");
        }

        if (!MoveAllowed(row.Status, target, actorIsProfessional))
        {
            return GuidanceOutcome.Fail<GuidanceMessageDto>(actorIsProfessional || row.Status == target ? GuidanceError.Conflict : GuidanceError.Forbidden, "status.transition_not_allowed");
        }

        var now = clock.UtcNow;
        db.Events.Add(new GuidanceEventRow { Id = Guid.CreateVersion7(), MessageId = row.Id, FromStatus = row.Status, ToStatus = target, At = now, ActorUserId = actorUserId, ActorIsProfessional = actorIsProfessional });
        row.Status = target;
        row.Version++;
        switch (target)
        {
            case GuidanceStatus.Seen: row.SeenAt = now; break;
            case GuidanceStatus.Reviewed: row.ReviewedAt = now; break;
            case GuidanceStatus.Referred: row.ReferredAt = now; break;
            case GuidanceStatus.Resolved: row.ResolvedAt = now; break;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return GuidanceOutcome.Fail<GuidanceMessageDto>(GuidanceError.Conflict, "version.mismatch");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.GuidanceStatusChanged, AuditResult.Success, actorUserId, "guidance-message", row.Id.ToString(), subjectId, source, correlationId, target.ToString()), ct);
        return GuidanceOutcome.Ok(ToDto(row, includeProfessional: actorIsProfessional));
    }

    public IReadOnlyList<GuidanceComposition> Samples(string locale)
    {
        var requests = new[]
        {
            new GuidanceRequest("adherence.skipped_pattern", GuidanceLevel.FollowUp, locale, new Dictionary<string, string> { ["medication"] = "Demopril", ["count"] = "3", ["days"] = "7" }, GuidanceConfidence.Moderate, "DEMO sample (fictional, not built from any real data)", true),
            new GuidanceRequest("interaction.review", GuidanceLevel.ReviewSoon, locale, new Dictionary<string, string> { ["medicationA"] = "Demopril", ["medicationB"] = "Nocturin", ["severity"] = "Moderate" }, GuidanceConfidence.Moderate, "DEMO sample (fictional reference entry)", true),
            new GuidanceRequest("symptom.followup", GuidanceLevel.FollowUp, locale, new Dictionary<string, string> { ["symptom"] = locale.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "سردرد خفیف" : "a mild headache", ["medication"] = "Demopril" }, GuidanceConfidence.Low, "DEMO sample (fictional)", true),
            new GuidanceRequest("symptom.severe_followup", GuidanceLevel.Urgent, locale, new Dictionary<string, string> { ["symptom"] = locale.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "تنگی نفس شدید" : "strong shortness of breath" }, GuidanceConfidence.NotAssessable, "DEMO sample (fictional)", true),
            new GuidanceRequest("adherence.skipped_pattern", GuidanceLevel.Information, locale, new Dictionary<string, string> { ["topic"] = "Demopril" }, GuidanceConfidence.NotAssessable, "DEMO sample (fictional)", false),
        };
        return [.. requests.Select(composer.Compose)];
    }

    private static GuidanceMessageDto ToDto(GuidanceMessageRow r, bool includeProfessional) => new(
        r.Id, r.TemplateKey, r.Level, r.Status, r.Locale, JsonSerializer.Deserialize<PatientGuidanceContent>(r.PatientJson, Json)!,
        includeProfessional ? JsonSerializer.Deserialize<ProfessionalGuidanceContent>(r.ProfessionalJson, Json) : null,
        r.CreatedAt, r.SeenAt, r.ReviewedAt, r.ReferredAt, r.ResolvedAt, r.IsDemo, r.IsDemo ? DemoNotice : null, OriginOf(r));
}
