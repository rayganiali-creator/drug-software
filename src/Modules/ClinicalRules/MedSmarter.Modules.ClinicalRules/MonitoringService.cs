using System.Globalization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.ClinicalRules;

public sealed class MonitoringOptions
{
    public const string Section = "ClinicalRules:Monitoring";

    /// <summary>Run the in-process worker on a timer. Off by default; nothing runs by itself unless this is on.</summary>
    public bool WorkerEnabled { get; set; }

    public int IntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 20;
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>Failures in a row before a job stops retrying (state Exhausted). There is no endless loop.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>How soon an enrolled patient is looked at again after a successful check.</summary>
    public int CheckIntervalMinutes { get; set; } = 60;

    /// <summary>Even with nothing changed, a new assessment is stored if the latest is older than this.</summary>
    public int ReassessAtLeastEveryDays { get; set; } = 7;

    public int LeaseSeconds { get; set; } = 300;
    public int BackoffSeconds { get; set; } = 300;
    public int MaxBackoffMinutes { get; set; } = 360;
}

/// <summary>
/// Scheduled, change-aware reassessment on a durable job row. What it is: a local pass, while the API process runs, that re-checks enrolled patients when the exact
/// record versions or the rule set changed (or the latest assessment is older than the configured limit). What it is not: continuous or real-time monitoring,
/// prediction, or a notification service. Data that is not entered is not seen; a device that is offline is not heard.
/// Transaction boundaries: job claim, assessment (+ guidance), job result are three separate writes. A crash between them leaves the job "Running" with an
/// expiring lease; recovery marks it interrupted and retries, and every step is idempotent (equal inputs create no second assessment, a finding has one open message).
/// </summary>
public sealed class MonitoringService(IDbContextFactory<ClinicalRulesDbContext> factory, IClock clock, IAuditWriter audit, IPatientDirectory directory, IClinicalSafetyService safety, IPurposeConsentEvaluator consent, IOptions<MonitoringOptions> options) : IMonitoringService
{
    private static readonly string[] Categories = ["medications", "allergies", "symptoms", "profile"];

    public const string Notice = "Checks run on a local schedule only while the system is running and only on information that has been entered. This is not continuous or real-time monitoring and it does not predict anything.";

    public async Task<SafetyOutcome<MonitoringStatusDto>> EnrolAsync(Guid actorUserId, Guid subjectId, string locale, string source, string? correlationId, CancellationToken ct = default)
    {
        if (actorUserId != subjectId)
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.Forbidden, "monitoring.self_only"); // only the person themself starts scheduled checks on their own record
        }

        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null || patient.Status != PatientStatus.Active)
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.NotFound, "patient.not_found");
        }

        var cats = await ConsentedAsync(subjectId, ct);
        if (!cats.Contains("medications"))
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.Validation, "consent.monitoring_required");
        }

        var now = clock.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.MonitoringJobs.FirstOrDefaultAsync(j => j.PatientId == patient.PatientId, ct);
        if (row is null)
        {
            row = new MonitoringJobRow { PatientId = patient.PatientId, SubjectId = subjectId, EnrolledAt = now };
            db.MonitoringJobs.Add(row);
        }

        row.Locale = locale is "fa" ? "fa" : "en";
        row.State = MonitoringJobState.Idle;
        row.WithdrawnAt = null;
        row.NextCheckAt = now;
        row.ConsecutiveFailures = 0;
        row.LastErrorCode = null;
        row.LeaseUntil = null;
        row.EnrolledAt = now;
        row.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.Conflict, "version.mismatch");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.MonitoringEnrolled, AuditResult.Success, actorUserId, "monitoring-job", patient.PatientId.ToString(), subjectId, source, correlationId, "enrolled"), ct);
        return SafetyResult.Ok(ToDto(row));
    }

    public async Task<SafetyOutcome<MonitoringStatusDto>> WithdrawAsync(Guid actorUserId, Guid subjectId, string source, string? correlationId, CancellationToken ct = default)
    {
        if (actorUserId != subjectId)
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.Forbidden, "monitoring.self_only");
        }

        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.MonitoringJobs.FirstOrDefaultAsync(j => j.PatientId == patient.PatientId, ct);
        if (row is null || row.State == MonitoringJobState.Withdrawn)
        {
            return SafetyResult.Ok(ToDto(row));
        }

        row.State = MonitoringJobState.Withdrawn;
        row.WithdrawnAt = clock.UtcNow;
        row.LeaseUntil = null;
        row.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // a pass touched the row at the same moment: reload and withdraw again (withdrawal always wins)
            db.ChangeTracker.Clear();
            row = await db.MonitoringJobs.FirstAsync(j => j.PatientId == patient.PatientId, ct);
            row.State = MonitoringJobState.Withdrawn;
            row.WithdrawnAt = clock.UtcNow;
            row.LeaseUntil = null;
            row.RowVersion++;
            await db.SaveChangesAsync(ct);
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.MonitoringWithdrawn, AuditResult.Success, actorUserId, "monitoring-job", patient.PatientId.ToString(), subjectId, source, correlationId, "withdrawn"), ct);
        return SafetyResult.Ok(ToDto(row));
    }

    public async Task<SafetyOutcome<MonitoringStatusDto>> StatusAsync(Guid subjectId, CancellationToken ct = default)
    {
        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return SafetyResult.Fail<MonitoringStatusDto>(SafetyError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        return SafetyResult.Ok(ToDto(await db.MonitoringJobs.AsNoTracking().FirstOrDefaultAsync(j => j.PatientId == patient.PatientId, ct)));
    }

    public async Task<MonitoringRunResult> RunOnceAsync(CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.UtcNow;
        int recovered = 0, claimed = 0, reassessed = 0, unchanged = 0, skipped = 0, failed = 0, exhausted = 0;

        // 1) recovery: a claim whose lease ran out was interrupted. Its outcome is unknown, so it is counted as a failed attempt; it is never assumed to have succeeded.
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var stuck = await db.MonitoringJobs.Where(j => j.State == MonitoringJobState.Running && j.LeaseUntil != null && j.LeaseUntil < now).Take(Math.Clamp(o.BatchSize, 1, 200)).ToListAsync(ct);
            foreach (var j in stuck)
            {
                recovered++;
                Fail(j, "interrupted", "interrupted", now, o);
                j.RowVersion++;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // another pass recovered or finished them first
            }

            foreach (var j in stuck)
            {
                await audit.WriteAsync(new AuditEvent(AuditActions.MonitoringFailed, AuditResult.Failure, null, "monitoring-job", j.PatientId.ToString(), j.SubjectId, "worker", null, "interrupted"), ct);
            }
        }

        // 2) claim a bounded batch, one row at a time (optimistic concurrency decides the winner)
        List<Guid> candidates;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            candidates = await db.MonitoringJobs.AsNoTracking().Where(j => j.State == MonitoringJobState.Idle && j.NextCheckAt <= now).OrderBy(j => j.NextCheckAt).Select(j => j.PatientId).Take(Math.Clamp(o.BatchSize, 1, 200)).ToListAsync(ct);
        }

        var claims = new List<MonitoringJobRow>();
        foreach (var id in candidates)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await db.MonitoringJobs.FirstOrDefaultAsync(j => j.PatientId == id && j.State == MonitoringJobState.Idle, ct);
            if (row is null)
            {
                continue;
            }

            row.State = MonitoringJobState.Running;
            row.LeaseUntil = now.AddSeconds(o.LeaseSeconds);
            row.LastAttemptAt = now;
            row.RowVersion++;
            try
            {
                await db.SaveChangesAsync(ct);
                claims.Add(row);
                claimed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // someone else claimed it
            }
        }

        // 3) check each claimed job with bounded concurrency
        using var gate = new SemaphoreSlim(Math.Clamp(o.MaxConcurrency, 1, 8));
        var results = await Task.WhenAll(claims.Select(async job =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await ProcessAsync(job, ct);
            }
            finally
            {
                gate.Release();
            }
        }));
        foreach (var r in results)
        {
            switch (r)
            {
                case "reassessed": reassessed++; break;
                case "unchanged": unchanged++; break;
                case "skipped.no_consent": skipped++; break;
                case "exhausted": exhausted++; failed++; break;
                default: failed++; break;
            }
        }

        return new MonitoringRunResult(recovered, claimed, reassessed, unchanged, skipped, failed, exhausted);
    }

    private async Task<string> ProcessAsync(MonitoringJobRow claim, CancellationToken ct)
    {
        var o = options.Value;
        string outcome;
        string? error = null;
        Guid? assessmentId = null;
        try
        {
            var cats = await ConsentedAsync(claim.SubjectId, ct);
            if (!cats.Contains("medications"))
            {
                outcome = "skipped.no_consent"; // consent was withdrawn: nothing is read, nothing is assessed
            }
            else
            {
                var r = await safety.ReassessIfNeededAsync(claim.SubjectId, cats, claim.Locale, TimeSpan.FromDays(o.ReassessAtLeastEveryDays), null, ct);
                if (!r.Succeeded)
                {
                    outcome = r.Error == SafetyError.Unavailable ? "failed.transient" : "failed.permanent";
                    error = r.Detail ?? r.Error.ToString();
                }
                else
                {
                    assessmentId = r.Value!.AssessmentId;
                    if (r.Value.Guidance is GuidanceLinkState.Failed or GuidanceLinkState.Partial)
                    {
                        // The assessment exists but its guidance could not be confirmed: not a success. The retry repairs it without a second assessment.
                        outcome = "failed.transient";
                        error = "guidance.incomplete";
                    }
                    else
                    {
                        outcome = r.Value.Kind == ReassessmentKind.Unchanged ? "unchanged" : "reassessed";
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown: hand the job back at once, without counting a failure.
            await FinishAsync(claim, row => { row.State = MonitoringJobState.Idle; row.LeaseUntil = null; row.NextCheckAt = clock.UtcNow; row.LastOutcome = "cancelled"; }, CancellationToken.None);
            throw;
        }
#pragma warning disable CA1031 // any unexpected failure is a failed attempt with a code, never a crash of the pass
        catch (Exception)
#pragma warning restore CA1031
        {
            outcome = "failed.transient";
            error = "unexpected";
        }

        var now = clock.UtcNow;
        var final = outcome;
        await FinishAsync(claim, row =>
        {
            row.LastOutcome = outcome;
            if (assessmentId is not null)
            {
                row.LastAssessmentId = assessmentId;
            }

            if (outcome is "reassessed" or "unchanged")
            {
                row.State = MonitoringJobState.Idle;
                row.ConsecutiveFailures = 0;
                row.LastErrorCode = null;
                row.LastSuccessAt = now;
                row.NextCheckAt = now.AddMinutes(o.CheckIntervalMinutes);
            }
            else if (outcome == "skipped.no_consent")
            {
                row.State = MonitoringJobState.Idle;
                row.NextCheckAt = now.AddMinutes(o.CheckIntervalMinutes);
            }
            else
            {
                Fail(row, outcome, error ?? "unknown", now, o);
                if (row.State == MonitoringJobState.Exhausted)
                {
                    final = "exhausted";
                }
            }

            row.LeaseUntil = null;
        }, ct);

        var action = outcome is "reassessed" or "unchanged" ? AuditActions.MonitoringReassessed : outcome == "skipped.no_consent" ? AuditActions.MonitoringSkipped : AuditActions.MonitoringFailed;
        await audit.WriteAsync(new AuditEvent(action, action == AuditActions.MonitoringFailed ? AuditResult.Failure : AuditResult.Success, null, "monitoring-job", claim.PatientId.ToString(), claim.SubjectId, "worker", null, outcome,
            new Dictionary<string, string> { ["error"] = error ?? "none", ["assessment"] = assessmentId?.ToString() ?? "none" }), ct);
        return final;
    }

    /// <summary>Writes the job result only if this pass still owns the row (state Running); a withdrawal or a recovery in between wins.</summary>
    private async Task FinishAsync(MonitoringJobRow claim, Action<MonitoringJobRow> apply, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.MonitoringJobs.FirstOrDefaultAsync(j => j.PatientId == claim.PatientId, ct);
        if (row is null || row.State != MonitoringJobState.Running)
        {
            return;
        }

        apply(row);
        row.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // withdrawn or recovered concurrently: that decision stands
        }
    }

    private static void Fail(MonitoringJobRow row, string outcome, string error, DateTimeOffset now, MonitoringOptions o)
    {
        row.ConsecutiveFailures++;
        row.LastOutcome = outcome == "interrupted" ? "interrupted" : outcome;
        row.LastErrorCode = error.Length > 64 ? error[..64] : error;
        row.LeaseUntil = null;
        if (row.ConsecutiveFailures >= o.MaxAttempts)
        {
            row.State = MonitoringJobState.Exhausted; // stops retrying; visible in the status; re-enrolling resets it
            row.NextCheckAt = DateTimeOffset.MaxValue;
        }
        else
        {
            row.State = MonitoringJobState.Idle;
            var minutes = Math.Min(o.MaxBackoffMinutes, o.BackoffSeconds / 60.0 * Math.Pow(2, row.ConsecutiveFailures - 1));
            row.NextCheckAt = now.AddMinutes(Math.Max(minutes, 0));
        }
    }

    /// <summary>The categories the patient's Monitoring consent covers right now. Each is its own question; none is assumed.</summary>
    private async Task<IReadOnlyCollection<string>> ConsentedAsync(Guid subjectId, CancellationToken ct)
    {
        var allowed = new List<string>();
        foreach (var c in Categories)
        {
            if ((await consent.EvaluateAsync(new PurposeConsentCheck(subjectId, "Monitoring", [c]), ct)).Allowed)
            {
                allowed.Add(c);
            }
        }

        return allowed;
    }

    private MonitoringStatusDto ToDto(MonitoringJobRow? r) => r is null || r.State == MonitoringJobState.Withdrawn
        ? new MonitoringStatusDto(false, MonitoringJobState.Withdrawn, r?.EnrolledAt, r?.LastAttemptAt, r?.LastSuccessAt, r?.LastOutcome, r?.LastErrorCode, r?.ConsecutiveFailures ?? 0, null, r?.LastAssessmentId, options.Value.WorkerEnabled, Notice)
        : new MonitoringStatusDto(true, r.State, r.EnrolledAt, r.LastAttemptAt, r.LastSuccessAt, r.LastOutcome, r.LastErrorCode, r.ConsecutiveFailures, r.State == MonitoringJobState.Exhausted ? null : r.NextCheckAt, r.LastAssessmentId, options.Value.WorkerEnabled, Notice);
}
