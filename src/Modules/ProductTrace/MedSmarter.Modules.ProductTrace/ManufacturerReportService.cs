using System.Security.Cryptography;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.ProductTrace;

public sealed class ManufacturerReportService(
    IDbContextFactory<ProductTraceDbContext> factory, IClock clock, IAuditWriter audit, IPatientDirectory patients, IPurposeConsentEvaluator consent,
    IOptions<ManufacturerReportsOptions> options, IManufacturerReportProvider provider, ReportPayloadBuilder builder) : IManufacturerReportService, IManufacturerReportOutbox
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ---------- drafts ----------

    public async Task<TraceOutcome<ManufacturerReportDto>> CreateDraftAsync(Guid actorUserId, Guid subjectId, ReportDraftInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return Support.Invalid<ManufacturerReportDto>(errors);
        }

        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "patient.not_found");
        }

        if (patient.Status != PatientStatus.Active)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Forbidden, "patient.inactive");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (input.ClientRequestId is { Length: > 0 } rid)
        {
            var existing = await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.PatientId == patient.PatientId && r.ClientRequestId == rid, ct);
            if (existing is not null)
            {
                return TraceOutcome.Ok(await ToDtoAsync(db, existing, subjectId, ct)); // a retried request returns the report it already created
            }
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.ProductRecordId && p.PatientId == patient.PatientId && p.DeletedAt == null, ct);
        if (product is null)
        {
            return Support.Invalid<ManufacturerReportDto>(["product.not_found"]);
        }

        var now = clock.UtcNow;
        var row = new ReportRow
        {
            Id = Guid.CreateVersion7(), Reference = NewReference(), PatientId = patient.PatientId, ProductRecordId = product.Id, Status = ReportStatus.Draft, CreatedBy = actorUserId, CreatedAt = now, UpdatedAt = now,
            ClientRequestId = input.ClientRequestId, IsDemo = product.IsDemo, Version = 1,
        };
        ApplyDraft(row, input);
        db.Reports.Add(row);
        db.ReportEvents.Add(Event(row.Id, "created", now, actorUserId));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "report.duplicate_request");
        }

        await Support.AuditAsync(audit, AuditActions.ManufacturerReportCreated, AuditResult.Success, actorUserId, "manufacturer-report", row.Id, subjectId, source, correlationId);
        return TraceOutcome.Ok(await ToDtoAsync(db, row, subjectId, ct));
    }

    public async Task<TraceOutcome<ManufacturerReportDto>> UpdateDraftAsync(Guid actorUserId, Guid subjectId, Guid id, ReportDraftInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return Support.Invalid<ManufacturerReportDto>(errors);
        }

        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.PatientId, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "report.not_found");
        }

        if (row.Status != ReportStatus.Draft)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "report.not_editable"); // once submitted the content is what was consented to and reviewed
        }

        if (input.ExpectedVersion != row.Version)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.ProductRecordId && p.PatientId == patient.PatientId && p.DeletedAt == null, ct);
        if (product is null)
        {
            return Support.Invalid<ManufacturerReportDto>(["product.not_found"]);
        }

        row.ProductRecordId = product.Id;
        row.IsDemo = product.IsDemo;
        ApplyDraft(row, input);
        row.UpdatedAt = clock.UtcNow;
        row.Version++;
        db.ReportEvents.Add(Event(row.Id, "edited", row.UpdatedAt, actorUserId));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        return TraceOutcome.Ok(await ToDtoAsync(db, row, subjectId, ct));
    }

    // ---------- submit / review / cancel ----------

    public async Task<TraceOutcome<ManufacturerReportDto>> SubmitAsync(Guid actorUserId, Guid subjectId, Guid id, int expectedVersion, string source, string? correlationId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.PatientId, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "report.not_found");
        }

        if (!ReportStateMachine.Allowed(row.Status, ReportStatus.PendingConsentOrReview) || row.Status != ReportStatus.Draft)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "report.not_submittable");
        }

        if (expectedVersion != row.Version)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == row.ProductRecordId, ct);
        var built = await builder.BuildAsync(row, product, ct);
        if (built.Payload is null)
        {
            await Support.AuditAsync(audit, AuditActions.ManufacturerReportBlocked, AuditResult.Denied, actorUserId, "manufacturer-report", row.Id, subjectId, source, correlationId, built.Error);
            return built.Error == "consent.required" || built.Error == "consent.scope_medications_required"
                ? TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, built.Error)
                : Support.Invalid<ManufacturerReportDto>([built.Error!]);
        }

        var now = clock.UtcNow;
        row.ReviewRequired = ReviewNeeded(row);
        row.SubmittedAt = now;
        row.UpdatedAt = now;
        row.ReviewerNote = null;
        row.ReviewDecision = null;
        row.Version++;
        if (row.ReviewRequired)
        {
            row.Status = ReportStatus.PendingConsentOrReview;
            db.ReportEvents.Add(Event(row.Id, "submitted_for_review", now, actorUserId));
        }
        else
        {
            await MakeReadyAsync(db, row, built.Payload, now, actorUserId, ct);
        }

        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, AuditActions.ManufacturerReportSubmitted, AuditResult.Success, actorUserId, "manufacturer-report", row.Id, subjectId, source, correlationId, row.ReviewRequired ? "review_required" : "queued");
        return TraceOutcome.Ok(await ToDtoAsync(db, row, subjectId, ct));
    }

    public async Task<TraceOutcome<ManufacturerReportDto>> ReviewAsync(Guid reviewerUserId, Guid subjectId, Guid id, ReviewReportCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        if (reviewerUserId == subjectId)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Forbidden, "review.not_by_patient"); // the patient cannot review their own report
        }

        if (command.Decision == ReviewDecision.Reject && (string.IsNullOrWhiteSpace(command.Note) || FreeTextGuard.Problem(command.Note, "note", 500) is not null))
        {
            return Support.Invalid<ManufacturerReportDto>(["note.required"]);
        }

        if (FreeTextGuard.Problem(command.Note, "note", 500) is { } noteProblem)
        {
            return Support.Invalid<ManufacturerReportDto>([noteProblem]);
        }

        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.PatientId, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "report.not_found");
        }

        if (row.Status != ReportStatus.PendingConsentOrReview)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "report.not_awaiting_review");
        }

        if (command.ExpectedVersion != row.Version)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        var now = clock.UtcNow;
        if (command.Decision == ReviewDecision.Approve)
        {
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == row.ProductRecordId, ct);
            var built = await builder.BuildAsync(row, product, ct);
            if (built.Payload is null)
            {
                await Support.AuditAsync(audit, AuditActions.ManufacturerReportBlocked, AuditResult.Denied, reviewerUserId, "manufacturer-report", row.Id, subjectId, source, correlationId, built.Error);
                return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, built.Error); // e.g. the patient withdrew consent while the report waited
            }

            row.ReviewDecision = ReviewDecision.Approve;
            row.ReviewedBy = reviewerUserId;
            row.ReviewedAt = now;
            row.ReviewerNote = FreeTextGuard.Clean(command.Note);
            row.Version++;
            await MakeReadyAsync(db, row, built.Payload, now, reviewerUserId, ct);
        }
        else
        {
            row.Status = ReportStatus.Draft;
            row.ReviewDecision = ReviewDecision.Reject;
            row.ReviewedBy = reviewerUserId;
            row.ReviewedAt = now;
            row.ReviewerNote = FreeTextGuard.Clean(command.Note);
            row.SubmittedAt = null;
            row.UpdatedAt = now;
            row.Version++;
            db.ReportEvents.Add(Event(row.Id, "rejected", now, reviewerUserId));
        }

        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, AuditActions.ManufacturerReportReviewed, AuditResult.Success, reviewerUserId, "manufacturer-report", row.Id, subjectId, source, correlationId, command.Decision.ToString());
        return TraceOutcome.Ok(await ToDtoAsync(db, row, subjectId, ct));
    }

    public async Task<TraceOutcome<ManufacturerReportDto>> CancelAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.PatientId, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "report.not_found");
        }

        if (!ReportStateMachine.Allowed(row.Status, ReportStatus.Cancelled))
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "report.not_cancellable"); // already sent: it cannot be recalled from here
        }

        var now = clock.UtcNow;
        row.Status = ReportStatus.Cancelled;
        row.UpdatedAt = now;
        row.Version++;
        foreach (var o in await db.Outbox.Where(x => x.ReportId == row.Id && (x.State == OutboxState.Pending || x.State == OutboxState.InProgress)).ToListAsync(ct))
        {
            o.State = OutboxState.Cancelled;
            o.Version++;
        }

        db.ReportEvents.Add(Event(row.Id, "cancelled", now, actorUserId));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, AuditActions.ManufacturerReportCancelled, AuditResult.Success, actorUserId, "manufacturer-report", row.Id, subjectId, source, correlationId);
        return TraceOutcome.Ok(await ToDtoAsync(db, row, subjectId, ct));
    }

    // ---------- reading ----------

    public async Task<TraceOutcome<IReadOnlyList<ManufacturerReportDto>>> ListAsync(Guid subjectId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<IReadOnlyList<ManufacturerReportDto>>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Reports.AsNoTracking().Where(r => r.PatientId == patient.PatientId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var consentActive = await ConsentActiveAsync(subjectId, ct);
        var list = new List<ManufacturerReportDto>();
        foreach (var r in rows)
        {
            list.Add(await ToDtoAsync(db, r, subjectId, ct, consentActive));
        }

        return TraceOutcome.Ok<IReadOnlyList<ManufacturerReportDto>>(list);
    }

    public async Task<TraceOutcome<ManufacturerReportDto>> GetAsync(Guid subjectId, Guid id, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.PatientId, ct);
        return row is null ? TraceOutcome.Fail<ManufacturerReportDto>(TraceError.NotFound, "report.not_found") : TraceOutcome.Ok(await ToDtoAsync(db, row, subjectId, ct));
    }

    public async Task<IReadOnlyList<ManufacturerReportDto>> PendingReviewAsync(IReadOnlyCollection<Guid> subjectIds, CancellationToken ct = default)
    {
        var result = new List<ManufacturerReportDto>();
        await using var db = await factory.CreateDbContextAsync(ct);
        foreach (var subject in subjectIds.Distinct())
        {
            var patient = await patients.FindBySubjectAsync(subject, ct);
            if (patient is null)
            {
                continue;
            }

            foreach (var r in await db.Reports.AsNoTracking().Where(r => r.PatientId == patient.PatientId && r.Status == ReportStatus.PendingConsentOrReview).OrderBy(r => r.SubmittedAt).ToListAsync(ct))
            {
                result.Add(await ToDtoAsync(db, r, subject, ct));
            }
        }

        return result;
    }

    // ---------- outbox ----------

    public async Task<ReportQueueDto> GetQueueAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var byStatus = (await db.Reports.AsNoTracking().GroupBy(r => r.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key.ToString(), x => x.N);
        var byState = (await db.Outbox.AsNoTracking().GroupBy(o => o.State).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key.ToString(), x => x.N);
        foreach (var s in Enum.GetValues<ReportStatus>())
        {
            byStatus.TryAdd(s.ToString(), 0);
        }

        foreach (var s in Enum.GetValues<OutboxState>())
        {
            byState.TryAdd(s.ToString(), 0);
        }

        var entries = await db.Outbox.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync(ct);
        return new ReportQueueDto(
            byStatus, byState, [.. entries.Select(o => new OutboxEntryDto(o.Id, o.ReportId, o.State, o.Attempts, o.NextAttemptAt, o.LastErrorCode, o.CreatedAt, o.SentAt))],
            provider.Name, provider.IsMock, provider.IsConfigured,
            provider.IsConfigured
                ? (provider.IsMock ? "MOCK delivery: nothing is sent to any manufacturer or authority." : "Delivery is configured.")
                : "No delivery provider is configured. A real connection to manufacturers or authorities needs an agreement and authorization; reports wait in the queue.");
    }

    public async Task<ProcessQueueResult> ProcessDueAsync(Guid actorUserId, int max, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!provider.IsConfigured)
        {
            return new ProcessQueueResult(0, 0, 0, 0, 0, "provider_not_configured");
        }

        var o = options.Value;
        var now = clock.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var due = await db.Outbox.Where(x => (x.State == OutboxState.Pending && x.NextAttemptAt <= now) || (x.State == OutboxState.InProgress && x.LockedUntil < now))
            .OrderBy(x => x.NextAttemptAt).Take(Math.Clamp(max, 1, 100)).ToListAsync(ct);
        int sent = 0, retrying = 0, failed = 0, blocked = 0, considered = 0;
        foreach (var entry in due)
        {
            // Claim: the version token lets exactly one instance win an entry.
            entry.State = OutboxState.InProgress;
            entry.LockedUntil = now.AddSeconds(o.LeaseSeconds);
            entry.Version++;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            considered++;
            var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == entry.ReportId, ct);
            var patientRef = report is null ? null : await patients.FindByPatientIdAsync(report.PatientId, ct);
            if (report is null || patientRef is null || report.Status != ReportStatus.ReadyToSend || report.PayloadJson is null)
            {
                entry.State = OutboxState.Cancelled;
                entry.LockedUntil = null;
                entry.Version++;
                await db.SaveChangesAsync(ct);
                blocked++;
                continue;
            }

            if (ReportPayloadBuilder.Hash(report.PayloadJson) != report.PayloadHash)
            {
                await FailAsync(db, report, entry, "payload_integrity", now, actorUserId, patientRef.SubjectId, source, correlationId, ct);
                failed++;
                continue;
            }

            // The consent must still cover EVERYTHING the frozen payload contains, not only the batch: a patient may have replaced the original consent
            // by a narrower one (e.g. products only) after approval, and then age band / sex or other medicines must not leave.
            var frozen = ReportPayloadBuilder.Deserialize(report.PayloadJson)!;
            var neededScopes = new List<string> { DataScopes.Products };
            if (frozen.AgeGroup != "unknown" || frozen.SexGroup != "unknown")
            {
                neededScopes.Add(DataScopes.Profile);
            }

            if (frozen.ConcomitantMedications.Count > 0)
            {
                neededScopes.Add(DataScopes.Medications);
            }

            if (!(await consent.EvaluateAsync(new PurposeConsentCheck(patientRef.SubjectId, ConsentPurposes.ManufacturerReport, neededScopes), ct)).Allowed)
            {
                // The patient withdrew consent (or narrowed it) after approval: nothing leaves; the report waits for consent again.
                report.Status = ReportStatus.PendingConsentOrReview;
                report.FailureCode = "consent_revoked";
                report.UpdatedAt = now;
                report.Version++;
                entry.State = OutboxState.Cancelled;
                entry.LockedUntil = null;
                entry.Version++;
                db.ReportEvents.Add(Event(report.Id, "blocked_consent_revoked", now, actorUserId));
                await db.SaveChangesAsync(ct);
                await Support.AuditAsync(audit, AuditActions.ManufacturerReportBlocked, AuditResult.Denied, actorUserId, "manufacturer-report", report.Id, patientRef.SubjectId, source, correlationId, "consent_revoked");
                blocked++;
                continue;
            }

            var payload = frozen;
            ReportSubmissionResult result;
            try
            {
                result = await provider.SubmitAsync(payload, entry.IdempotencyKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new ReportSubmissionResult(SubmissionOutcome.TransientFailure, null, "provider_error"); // never expose provider internals
            }

            entry.Attempts++;
            report.Attempts = entry.Attempts;
            switch (result.Outcome)
            {
                case SubmissionOutcome.Accepted:
                    report.Status = result.ExternalReference is null ? ReportStatus.Sent : ReportStatus.Acknowledged;
                    report.SentAt = now;
                    report.AcknowledgedAt = result.ExternalReference is null ? null : now;
                    report.ExternalReference = result.ExternalReference;
                    report.IsMockDelivery = provider.IsMock;
                    report.FailureCode = null;
                    report.UpdatedAt = now;
                    report.Version++;
                    entry.State = OutboxState.Sent;
                    entry.SentAt = now;
                    entry.LockedUntil = null;
                    entry.LastErrorCode = null;
                    entry.Version++;
                    db.ReportEvents.Add(Event(report.Id, report.Status == ReportStatus.Acknowledged ? "acknowledged" : "sent", now, actorUserId));
                    await db.SaveChangesAsync(ct);
                    await Support.AuditAsync(audit, AuditActions.ManufacturerReportSent, AuditResult.Success, actorUserId, "manufacturer-report", report.Id, patientRef.SubjectId, source, correlationId, provider.IsMock ? "mock" : "real");
                    if (report.Status == ReportStatus.Acknowledged)
                    {
                        await Support.AuditAsync(audit, AuditActions.ManufacturerReportAcknowledged, AuditResult.Success, actorUserId, "manufacturer-report", report.Id, patientRef.SubjectId, source, correlationId);
                    }

                    sent++;
                    break;

                case SubmissionOutcome.TransientFailure when entry.Attempts < o.MaxAttempts:
                    var wait = o.BackoffMinutes[Math.Min(entry.Attempts - 1, o.BackoffMinutes.Length - 1)];
                    entry.State = OutboxState.Pending;
                    entry.NextAttemptAt = now.AddMinutes(wait);
                    entry.LockedUntil = null;
                    entry.LastErrorCode = result.ErrorCode ?? "transient";
                    entry.Version++;
                    report.FailureCode = entry.LastErrorCode;
                    report.UpdatedAt = now;
                    report.Version++;
                    await db.SaveChangesAsync(ct);
                    retrying++;
                    break;

                case SubmissionOutcome.NotConfigured:
                    entry.State = OutboxState.Pending;
                    entry.LockedUntil = null;
                    entry.Attempts--;
                    entry.Version++;
                    await db.SaveChangesAsync(ct);
                    break;

                default:
                    await FailAsync(db, report, entry, result.ErrorCode ?? "failed", now, actorUserId, patientRef.SubjectId, source, correlationId, ct);
                    failed++;
                    break;
            }
        }

        return new ProcessQueueResult(considered, sent, retrying, failed, blocked, null);
    }

    public async Task<TraceOutcome<bool>> RetryFailedAsync(Guid actorUserId, Guid reportId, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null)
        {
            return TraceOutcome.Fail<bool>(TraceError.NotFound, "report.not_found");
        }

        if (report.Status != ReportStatus.Failed || report.PayloadJson is null)
        {
            return TraceOutcome.Fail<bool>(TraceError.Conflict, "report.not_failed");
        }

        var now = clock.UtcNow;
        report.Status = ReportStatus.ReadyToSend;
        report.FailureCode = null;
        report.UpdatedAt = now;
        report.Version++;
        var entry = await db.Outbox.FirstOrDefaultAsync(x => x.ReportId == reportId, ct);
        if (entry is null)
        {
            db.Outbox.Add(NewOutbox(report, now));
        }
        else
        {
            entry.State = OutboxState.Pending;
            entry.Attempts = 0;
            entry.NextAttemptAt = now;
            entry.LockedUntil = null;
            entry.LastErrorCode = null;
            entry.Version++;
        }

        db.ReportEvents.Add(Event(reportId, "retry_requested", now, actorUserId));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<bool>(TraceError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, AuditActions.ManufacturerReportSubmitted, AuditResult.Success, actorUserId, "manufacturer-report", reportId, null, source, correlationId, "retry");
        return TraceOutcome.Ok(true);
    }

    // ---------- helpers ----------

    private async Task FailAsync(ProductTraceDbContext db, ReportRow report, OutboxRow entry, string code, DateTimeOffset now, Guid actor, Guid subject, string source, string? correlationId, CancellationToken ct)
    {
        report.Status = ReportStatus.Failed;
        report.FailureCode = code;
        report.UpdatedAt = now;
        report.Version++;
        entry.State = OutboxState.Failed;
        entry.LastErrorCode = code;
        entry.LockedUntil = null;
        entry.Version++;
        db.ReportEvents.Add(Event(report.Id, "failed", now, actor, code));
        await db.SaveChangesAsync(ct);
        await Support.AuditAsync(audit, AuditActions.ManufacturerReportFailed, AuditResult.Failure, actor, "manufacturer-report", report.Id, subject, source, correlationId, code);
    }

    private static async Task MakeReadyAsync(ProductTraceDbContext db, ReportRow row, ManufacturerReportPayload payload, DateTimeOffset now, Guid actor, CancellationToken ct)
    {
        var json = ReportPayloadBuilder.Serialize(payload);
        row.PayloadJson = json;
        row.PayloadHash = ReportPayloadBuilder.Hash(json);
        row.Status = ReportStatus.ReadyToSend;
        row.FailureCode = null;
        row.UpdatedAt = now;
        db.ReportEvents.Add(Event(row.Id, "ready_to_send", now, actor));
        var existing = await db.Outbox.FirstOrDefaultAsync(x => x.ReportId == row.Id, ct);
        if (existing is null)
        {
            db.Outbox.Add(NewOutbox(row, now));
        }
        else
        {
            existing.State = OutboxState.Pending;
            existing.Attempts = 0;
            existing.NextAttemptAt = now;
            existing.LockedUntil = null;
            existing.LastErrorCode = null;
            existing.Version++;
        }
    }

    private static OutboxRow NewOutbox(ReportRow report, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), ReportId = report.Id, IdempotencyKey = "msr-" + report.Reference.ToLowerInvariant(), State = OutboxState.Pending, NextAttemptAt = now, CreatedAt = now };

    private bool ReviewNeeded(ReportRow r)
    {
        var sensitive = r.IssueType == ReportIssueType.AdverseEvent || r.Severity is ReportSeverity.Severe or ReportSeverity.Unknown || r.IncludeConcomitant;
        return sensitive || string.Equals(options.Value.ReviewPolicy, "Always", StringComparison.OrdinalIgnoreCase); // "None" can only skip review for non-sensitive reports
    }

    private List<string> Validate(ReportDraftInput i)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(i.IssueType)) { errors.Add("issue_type.invalid"); }
        if (!Enum.IsDefined(i.Severity)) { errors.Add("severity.invalid"); }
        if (i.OccurredOn > Today || i.OccurredOn < Today.AddYears(-10)) { errors.Add("occurred_on.range"); }
        if (i.DurationOfUseDays is { } d && (d < 0 || d > 36500)) { errors.Add("duration.range"); }
        if (FreeTextGuard.Problem(i.Description, "description", 1000) is { } p) { errors.Add(p); }
        if (i.ClientRequestId is { } c && (c.Length > 64 || c.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))) { errors.Add("client_request_id.invalid"); }
        return errors;
    }

    private static void ApplyDraft(ReportRow row, ReportDraftInput i)
    {
        row.IssueType = i.IssueType;
        row.Severity = i.Severity;
        row.OccurredOn = i.OccurredOn;
        row.DurationOfUseDays = i.DurationOfUseDays;
        row.Description = FreeTextGuard.Clean(i.Description);
        row.IncludeConcomitant = i.IncludeConcomitantMedications;
    }

    private static string NewReference() => "MSR-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

    private static ReportEventRow Event(Guid reportId, string kind, DateTimeOffset at, Guid? actor, string? note = null) =>
        new() { Id = Guid.CreateVersion7(), ReportId = reportId, Kind = kind, At = at, ActorUserId = actor, Note = note };

    private static async Task<bool> TrySaveAsync(ProductTraceDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<bool> ConsentActiveAsync(Guid subjectId, CancellationToken ct) =>
        (await consent.EvaluateAsync(new PurposeConsentCheck(subjectId, ConsentPurposes.ManufacturerReport, [DataScopes.Products]), ct)).Allowed;

    private async Task<ManufacturerReportDto> ToDtoAsync(ProductTraceDbContext db, ReportRow r, Guid subjectId, CancellationToken ct, bool? consentActive = null)
    {
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == r.ProductRecordId, ct);
        ManufacturerReportPayload? preview = ReportPayloadBuilder.Deserialize(r.PayloadJson);
        if (preview is null && r.Status is ReportStatus.Draft or ReportStatus.PendingConsentOrReview)
        {
            preview = (await builder.BuildAsync(r, product, ct)).Payload; // best effort: null while consent is missing
        }

        return new ManufacturerReportDto(
            r.Id, subjectId, r.ProductRecordId, product.ProductName, product.BatchNumber, r.Status, r.IssueType, r.Severity, r.OccurredOn, r.DurationOfUseDays, r.Description, r.IncludeConcomitant, r.ReviewRequired,
            consentActive ?? await ConsentActiveAsync(subjectId, ct), r.ReviewerNote, r.ReviewDecision, r.FailureCode, r.Attempts, r.CreatedAt, r.UpdatedAt, r.SubmittedAt, r.ReviewedAt, r.SentAt, r.AcknowledgedAt,
            r.IsMockDelivery, r.Version, r.IsDemo, r.IsDemo ? Support.DemoNotice : null, preview);
    }
}
