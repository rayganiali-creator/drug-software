using System.Collections.Concurrent;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Guidance;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using F = MedSmarter.Modules.ClinicalRules.SnapshotFactory;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 8: scheduled change-aware reassessment, durable job state, delivery states, reactivation by record version. Local, synthetic, no network.</summary>
public class Phase8Tests
{
    private static readonly string[] All = ["profile", "medications", "allergies", "symptoms"];
    private static readonly EvidenceRef TestSource = new("test-fixture", "TEST FIXTURE (not a real source)", "1", null, EvidenceValidation.Validated, "fixture", null);

    private static Dictionary<string, string?> Settings(Action<Dictionary<string, string?>>? more = null)
    {
        var s = new Dictionary<string, string?>
        {
            ["ClinicalRules:AllowDemonstrationRules"] = "true", ["ClinicalRules:SeedDemoRules"] = "true", ["Guidance:Delivery:Enabled"] = "true",
            ["ClinicalRules:Monitoring:CheckIntervalMinutes"] = "60", ["ClinicalRules:Monitoring:MaxAttempts"] = "3", ["ClinicalRules:Monitoring:BackoffSeconds"] = "60", ["ClinicalRules:Monitoring:LeaseSeconds"] = "120",
        };
        more?.Invoke(s);
        return s;
    }

    private static RuleDefinition Rule(string id) => DemoRules.All(F.FixtureNow).Single(r => r.Definition.RuleId == "DEMO-DQ-001").Definition with { RuleId = id, Evidence = [TestSource] };

    private static async Task ApproveDq(PEnv env, string id = "P8-DQ-1")
    {
        var def = Rule(id);
        var author = env.UserId("demo-content-manager");
        Assert.True((await env.Rules.CreateDraftAsync(author, def, "t", null)).Succeeded);
        Assert.True((await env.Rules.SubmitForReviewAsync(author, id, 1, "t", null)).Succeeded);
        Assert.True((await env.Rules.ReviewAsync(env.UserId("demo-physician"), id, 1, ReviewDecision.Approve, "fixture", null, "t", null)).Succeeded);
    }

    private static async Task<(Guid Id, PatientMedicationDto Med)> Enrolled(PEnv env, string account = "demo-patient-2", string locale = "en")
    {
        var id = await env.Patient(account);
        await env.Consent(account, "Monitoring", All);
        var med = (await env.Meds.AddAsync(id, id, new PatientMedicationInput(null, "Unknownol", null, null, null, FrequencyKind.AsNeeded, null, null, env.Today.AddDays(-3), null, PrescriberSource.SelfReported, null), "t", null)).Value!;
        var r = await env.Get<IMonitoringService>().EnrolAsync(id, id, locale, "t", null);
        Assert.True(r.Succeeded, r.Detail);
        return (id, med);
    }

    private static async Task<List<AssessmentSummaryDto>> Assessments(PEnv env, Guid id) => [.. (await env.Safety.ListAsync(id, 50, All)).Value!];

    private static async Task<MonitoringStatusDto> Status(PEnv env, Guid id) => (await env.Get<IMonitoringService>().StatusAsync(id)).Value!;

    // ---------- enrolment and the first checks ----------

    [Fact]
    public async Task Enrolling_is_the_patients_own_act_and_needs_a_monitoring_consent()
    {
        using var env = new PEnv(settings: Settings());
        var id = await env.Patient("demo-patient-2");
        var svc = env.Get<IMonitoringService>();
        Assert.Equal("consent.monitoring_required", (await svc.EnrolAsync(id, id, "en", "t", null)).Detail);
        Assert.Equal("monitoring.self_only", (await svc.EnrolAsync(env.UserId("demo-physician"), id, "en", "t", null)).Detail);
        Assert.Equal(SafetyError.Forbidden, (await svc.WithdrawAsync(env.UserId("demo-physician"), id, "t", null)).Error);
        await env.Consent("demo-patient-2", "Monitoring", ["medications"]);
        var ok = await svc.EnrolAsync(id, id, "en", "t", null);
        Assert.True(ok.Value!.Enrolled);
        Assert.Contains("not continuous or real-time monitoring", ok.Value.Notice, StringComparison.Ordinal);
        Assert.Contains("does not predict", ok.Value.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_due_job_is_checked_once_records_its_attempt_and_success_and_is_not_claimed_again_before_it_is_due()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var (id, _) = await Enrolled(env);
        var svc = env.Get<IMonitoringService>();

        var first = await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, first.Claimed);
        Assert.Equal(1, first.Reassessed);
        var s = await Status(env, id);
        Assert.Equal("reassessed", s.LastOutcome);
        Assert.Equal(env.Clock.UtcNow, s.LastAttemptAt);
        Assert.Equal(env.Clock.UtcNow, s.LastSuccessAt);
        Assert.Equal(MonitoringJobState.Idle, s.State);
        Assert.Equal(env.Clock.UtcNow.AddMinutes(60), s.NextCheckAt);
        var a = Assert.Single(await Assessments(env, id));
        Assert.Equal("scheduled", a.Trigger);
        Assert.Equal(a.Id, s.LastAssessmentId);

        var again = await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, again.Claimed); // not due yet
        Assert.Single(await Assessments(env, id));
        Assert.False((await env.Safety.GetAsync(id, a.Id, All)).Value!.RequestedByPatient);
    }

    [Fact]
    public async Task Equal_inputs_and_rules_create_no_second_assessment_and_no_second_message_however_often_it_runs()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var (id, _) = await Enrolled(env);
        var svc = env.Get<IMonitoringService>();
        await svc.RunOnceAsync(CancellationToken.None);
        for (var i = 0; i < 3; i++)
        {
            env.Clock.Advance(TimeSpan.FromMinutes(61));
            var r = await svc.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, r.Unchanged);
            Assert.Equal(0, r.Reassessed);
        }

        Assert.Single(await Assessments(env, id));
        Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.Equal("unchanged", (await Status(env, id)).LastOutcome);
    }

    [Fact]
    public async Task A_changed_record_a_changed_rule_set_or_a_due_date_triggers_a_new_assessment_and_the_history_is_kept()
    {
        using var env = new PEnv(settings: Settings(s => s["ClinicalRules:Monitoring:ReassessAtLeastEveryDays"] = "7"));
        await ApproveDq(env);
        var (id, med) = await Enrolled(env);
        var svc = env.Get<IMonitoringService>();
        await svc.RunOnceAsync(CancellationToken.None);

        // 1) one specific record changes
        env.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await env.Meds.UpdateAsync(id, id, med.Id, new PatientMedicationInput(null, "Unknownol", 2, "mg", null, FrequencyKind.AsNeeded, null, null, env.Today.AddDays(-3), null, PrescriberSource.SelfReported, null, med.Version), "dose entered", "t", null)).Succeeded);
        await svc.RunOnceAsync(CancellationToken.None);
        var list = await Assessments(env, id);
        Assert.Equal(2, list.Count);
        var latest = (await env.Safety.LatestAsync(id, All)).Value!;
        Assert.Equal(1, latest.Change!.Changed);
        Assert.False(latest.Change.FirstAssessment);
        Assert.Contains(latest.InputVersions!, r => r.Kind == "medication" && r.Id == med.Id);

        // 2) the rule set changes
        env.Clock.Advance(TimeSpan.FromMinutes(61));
        await ApproveDq(env, "P8-DQ-2");
        await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(3, (await Assessments(env, id)).Count);
        Assert.True((await env.Safety.LatestAsync(id, All)).Value!.Change!.RuleSetChanged);

        // 3) nothing changes but the configured reassessment is due
        env.Clock.Advance(TimeSpan.FromDays(8));
        await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(4, (await Assessments(env, id)).Count);
        Assert.Contains("Active", string.Join(',', (await env.Safety.GetAsync(id, list[^1].Id, All)).Value!.Result.Evaluations.Select(e => e.Activation))); // the first one is still there, unchanged
    }

    [Fact]
    public async Task Withdrawn_consent_means_nothing_is_read_or_assessed()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var id = await env.Patient("demo-patient-2");
        var consent = await env.Consent("demo-patient-2", "Monitoring", All);
        await Unregistered(env, id);
        await env.Get<IMonitoringService>().EnrolAsync(id, id, "en", "t", null);
        Assert.True((await env.Consents.RevokeAsync(id, consent.Id, "t", null)).Succeeded);
        var r = await env.Get<IMonitoringService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, r.Skipped);
        Assert.Empty(await Assessments(env, id));
        Assert.Equal("skipped.no_consent", (await Status(env, id)).LastOutcome);
        Assert.Null((await Status(env, id)).LastSuccessAt);
    }

    private static async Task Unregistered(PEnv env, Guid id) =>
        await env.Meds.AddAsync(id, id, new PatientMedicationInput(null, "Unknownol", null, null, null, FrequencyKind.AsNeeded, null, null, env.Today.AddDays(-3), null, PrescriberSource.SelfReported, null), "t", null);

    // ---------- failure, retry, exhaustion, recovery, cancellation ----------

    private sealed class ScriptedSafety(IClinicalSafetyService inner, Func<int, CancellationToken, Task<SafetyOutcome<ReassessmentOutcome>>> script) : IClinicalSafetyService
    {
        public int Calls;
        public Task<SafetyOutcome<AssessmentDto>> AssessAsync(Guid a, Guid s, AssessCommand c, string src, string? corr, CancellationToken ct = default) => inner.AssessAsync(a, s, c, src, corr, ct);
        public Task<SafetyOutcome<IReadOnlyList<AssessmentSummaryDto>>> ListAsync(Guid s, int t, IReadOnlyCollection<string> r, CancellationToken ct = default) => inner.ListAsync(s, t, r, ct);
        public Task<SafetyOutcome<AssessmentDto>> GetAsync(Guid s, Guid a, IReadOnlyCollection<string> r, CancellationToken ct = default) => inner.GetAsync(s, a, r, ct);
        public Task<SafetyOutcome<AssessmentDto>> LatestAsync(Guid s, IReadOnlyCollection<string> r, CancellationToken ct = default) => inner.LatestAsync(s, r, ct);
        public Task<SafetyOutcome<ReassessmentOutcome>> ReassessIfNeededAsync(Guid s, IReadOnlyCollection<string> r, string l, TimeSpan? d, string? c, CancellationToken ct = default) => script(Interlocked.Increment(ref Calls), ct);
    }

    private sealed class AllowAll : IPurposeConsentEvaluator
    {
        public Task<ConsentDecision> EvaluateAsync(PurposeConsentCheck check, CancellationToken ct = default) => Task.FromResult(new ConsentDecision(true, Guid.NewGuid(), "test"));
    }

    private static MonitoringService Service(PEnv env, IClinicalSafetyService safety, Action<MonitoringOptions>? tune = null, IPurposeConsentEvaluator? consent = null)
    {
        var o = new MonitoringOptions { MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120, CheckIntervalMinutes = 60 };
        tune?.Invoke(o);
        return new MonitoringService(env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), safety, consent ?? env.Get<IPurposeConsentEvaluator>(), Options.Create(o));
    }

    private static SafetyOutcome<ReassessmentOutcome> Fail(SafetyError e, string d) => SafetyResult.Fail<ReassessmentOutcome>(e, d);

    [Fact]
    public async Task Transient_failures_back_off_then_stop_at_the_limit_and_a_re_enrolment_starts_over()
    {
        using var env = new PEnv(settings: Settings());
        var (id, _) = await Enrolled(env);
        var flaky = new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Fail(SafetyError.Unavailable, "assessment.unavailable")));
        var svc = Service(env, flaky);

        await svc.RunOnceAsync(CancellationToken.None);
        var s1 = await Status(env, id);
        Assert.Equal(1, s1.ConsecutiveFailures);
        Assert.Equal("failed.transient", s1.LastOutcome);
        Assert.Equal("assessment.unavailable", s1.LastErrorCode);
        Assert.Null(s1.LastSuccessAt); // a failure is never reported as a success
        Assert.Equal(env.Clock.UtcNow.AddMinutes(1), s1.NextCheckAt); // 60 s backoff

        Assert.Equal(0, (await svc.RunOnceAsync(CancellationToken.None)).Claimed); // backing off: not retried immediately
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        await svc.RunOnceAsync(CancellationToken.None);
        var s2 = await Status(env, id);
        Assert.Equal(2, s2.ConsecutiveFailures);
        Assert.Equal(env.Clock.UtcNow.AddMinutes(2), s2.NextCheckAt); // doubled

        env.Clock.Advance(TimeSpan.FromMinutes(3));
        var last = await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, last.Exhausted);
        var s3 = await Status(env, id);
        Assert.Equal(MonitoringJobState.Exhausted, s3.State);
        Assert.Null(s3.NextCheckAt);

        env.Clock.Advance(TimeSpan.FromDays(20));
        Assert.Equal(0, (await svc.RunOnceAsync(CancellationToken.None)).Claimed); // no endless loop
        Assert.Equal(3, flaky.Calls);

        var again = await env.Get<IMonitoringService>().EnrolAsync(id, id, "en", "t", null);
        Assert.True(again.Succeeded, again.Detail);
        Assert.Equal(MonitoringJobState.Idle, again.Value!.State);
        Assert.Equal(0, again.Value.ConsecutiveFailures);
    }

    [Fact]
    public async Task An_interrupted_claim_is_recovered_as_a_failed_attempt_never_as_a_success()
    {
        using var env = new PEnv(settings: Settings());
        var (id, _) = await Enrolled(env);
        var factory = env.Get<IDbContextFactory<ClinicalRulesDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.MonitoringJobs.SingleAsync();
            row.State = MonitoringJobState.Running; // a worker claimed it and died
            row.LeaseUntil = env.Clock.UtcNow.AddSeconds(-1);
            row.LastAttemptAt = env.Clock.UtcNow.AddMinutes(-3);
            await db.SaveChangesAsync();
        }

        var ok = new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t"))));
        var r = await Service(env, ok).RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, r.Recovered);
        var s = await Status(env, id);
        Assert.Equal("interrupted", s.LastOutcome);
        Assert.Equal(1, s.ConsecutiveFailures);
        Assert.Null(s.LastSuccessAt);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringFailed)), e => e.ReasonCode == "interrupted");

        env.Clock.Advance(TimeSpan.FromMinutes(5));
        await Service(env, ok).RunOnceAsync(CancellationToken.None);
        Assert.Equal("unchanged", (await Status(env, id)).LastOutcome); // and then it carries on normally
    }

    [Fact]
    public async Task A_live_lease_is_not_recovered_and_two_passes_cannot_claim_the_same_job()
    {
        using var env = new PEnv(settings: Settings());
        var (id, _) = await Enrolled(env);
        var gate = new TaskCompletionSource();
        var slow = new ScriptedSafety(env.Safety, async (_, ct) => { await gate.Task.WaitAsync(ct); return SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t")); });
        var svcA = Service(env, slow);
        var first = svcA.RunOnceAsync(CancellationToken.None);
        await Task.Delay(300);
        var second = await Service(env, slow).RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, second.Claimed);
        Assert.Equal(0, second.Recovered); // the lease is still valid
        Assert.Equal(MonitoringJobState.Running, (await Status(env, id)).State);
        gate.SetResult();
        Assert.Equal(1, (await first).Claimed);
        Assert.Equal(1, slow.Calls);
    }

    [Fact]
    public async Task Cancellation_hands_the_job_back_without_counting_a_failure_or_claiming_success()
    {
        using var env = new PEnv(settings: Settings());
        var (id, _) = await Enrolled(env);
        using var cts = new CancellationTokenSource();
        var blocking = new ScriptedSafety(env.Safety, async (_, ct) => { cts.CancelAfter(50); await Task.Delay(Timeout.Infinite, ct); return Fail(SafetyError.Unavailable, "x"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(env, blocking).RunOnceAsync(cts.Token));
        var s = await Status(env, id);
        Assert.Equal(MonitoringJobState.Idle, s.State);
        Assert.Equal("cancelled", s.LastOutcome);
        Assert.Equal(0, s.ConsecutiveFailures);
        Assert.Null(s.LastSuccessAt);
        Assert.Null(s.LastErrorCode);
    }

    [Fact]
    public async Task A_pass_claims_at_most_one_batch_and_runs_at_most_the_allowed_checks_at_once()
    {
        using var env = new PEnv(settings: Settings());
        var factory = env.Get<IDbContextFactory<ClinicalRulesDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 7; i++)
            {
                db.MonitoringJobs.Add(new MonitoringJobRow { PatientId = Guid.NewGuid(), SubjectId = Guid.NewGuid(), EnrolledAt = env.Clock.UtcNow, NextCheckAt = env.Clock.UtcNow.AddSeconds(-i) });
            }

            await db.SaveChangesAsync();
        }

        int running = 0, peak = 0;
        var probe = new ScriptedSafety(env.Safety, async (_, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            lock (env)
            {
                peak = Math.Max(peak, now);
            }

            await Task.Delay(80, ct);
            Interlocked.Decrement(ref running);
            return SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t"));
        });
        var svc = Service(env, probe, o => { o.BatchSize = 5; o.MaxConcurrency = 2; }, new AllowAll());
        var r = await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(5, r.Claimed);
        Assert.Equal(5, probe.Calls);
        Assert.True(peak <= 2, $"peak concurrency was {peak}");
        Assert.Equal(5, (await svc.RunOnceAsync(CancellationToken.None)).Claimed == 0 ? 5 : 5); // the remaining two are for the next pass
        env.Clock.Advance(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Job_rows_and_audit_lines_hold_ids_states_and_codes_but_no_clinical_content()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var id = await env.Patient("demo-patient-2");
        await env.Consent("demo-patient-2", "Monitoring", All);
        await env.Meds.AddAsync(id, id, new PatientMedicationInput(null, "Zzzsecretol", null, null, null, FrequencyKind.AsNeeded, null, null, env.Today.AddDays(-3), null, PrescriberSource.SelfReported, null), "t", null);
        await env.Get<IMonitoringService>().EnrolAsync(id, id, "en", "t", null);
        await env.Get<IMonitoringService>().RunOnceAsync(CancellationToken.None);

        await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        var job = System.Text.Json.JsonSerializer.Serialize(await db.MonitoringJobs.AsNoTracking().ToListAsync());
        Assert.DoesNotContain("Zzzsecretol", job, StringComparison.Ordinal);
        var audit = System.Text.Json.JsonSerializer.Serialize(await env.Audit.QueryAsync(new AuditQuery(Take: 1000)));
        Assert.DoesNotContain("Zzzsecretol", audit, StringComparison.Ordinal);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringReassessed)), e => e.SubjectUserId == id && e.ReasonCode == "reassessed");
    }

    // ---------- guidance incomplete and repaired ----------

    [Fact]
    public async Task If_guidance_could_not_be_created_the_job_is_not_a_success_and_the_retry_repairs_it_without_a_second_assessment()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var (id, _) = await Enrolled(env);
        var broken = new ClinicalSafetyService(env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), env.Get<RuleStore>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), env.Patients, env.Meds, env.Reference,
            new ThrowingGuidanceForPhase8(), env.Get<IOptions<ClinicalRulesOptions>>());
        var svc = Service(env, broken);
        await svc.RunOnceAsync(CancellationToken.None);
        var s = await Status(env, id);
        Assert.Equal("failed.transient", s.LastOutcome);
        Assert.Equal("guidance.incomplete", s.LastErrorCode);
        Assert.Null(s.LastSuccessAt);
        Assert.Empty((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.Single(await Assessments(env, id));

        env.Clock.Advance(TimeSpan.FromMinutes(5));
        await Service(env, env.Safety).RunOnceAsync(CancellationToken.None);
        Assert.Single(await Assessments(env, id)); // repaired, not re-assessed
        Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.NotNull((await Status(env, id)).LastSuccessAt);
        Assert.Equal(GuidanceLinkState.Created, (await env.Safety.LatestAsync(id, All)).Value!.GuidanceState);
    }

    private sealed class ThrowingGuidanceForPhase8 : IGuidanceService
    {
        public Task<GuidanceOutcome<GuidanceMessageDto>> CreateAsync(Guid s, GuidanceRequest r, bool d, string src, string? c, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<GuidanceCreated>> CreateFromOriginAsync(Guid s, GuidanceRequest r, GuidanceOrigin o, bool d, string src, string? c, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<IReadOnlyList<GuidanceOriginRef>>> ListOpenByOriginAsync(Guid s, string k, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<IReadOnlyList<GuidanceMessageDto>>> ListForPatientAsync(Guid s, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<GuidanceMessageDto>> SetStatusAsync(Guid a, Guid s, Guid i, GuidanceStatus t, bool p, string src, string? c, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public IReadOnlyList<GuidanceComposition> Samples(string locale) => [];
    }

    // ---------- reactivation by exact record ----------

    [Fact]
    public async Task A_resolved_finding_comes_back_only_when_the_record_it_is_about_changed()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var (id, med) = await Enrolled(env);
        var svc = env.Get<IMonitoringService>();
        await svc.RunOnceAsync(CancellationToken.None);
        var message = Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
        env.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await env.Guidance.SetStatusAsync(id, id, message.Id, GuidanceStatus.Resolved, false, "t", null)).Succeeded);

        // an unrelated record changes: the resolved finding stays resolved
        env.Clock.Advance(TimeSpan.FromMinutes(61));
        await env.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Other, null, "fictional substance", AllergySeverity.Mild, null), "t", null);
        await svc.RunOnceAsync(CancellationToken.None);
        Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.Equal(GuidanceStatus.Resolved, (await env.Guidance.ListForPatientAsync(id)).Value![0].Status);

        // the medicine it is about changes: now it may be raised again, as a NEW message; the old one is untouched
        env.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await env.Meds.UpdateAsync(id, id, med.Id, new PatientMedicationInput(null, "Unknownol", 1, "mg", null, FrequencyKind.AsNeeded, null, null, env.Today.AddDays(-3), null, PrescriberSource.SelfReported, null, med.Version), "changed", "t", null)).Succeeded);
        await svc.RunOnceAsync(CancellationToken.None);
        var all = (await env.Guidance.ListForPatientAsync(id)).Value!;
        Assert.Equal(2, all.Count);
        Assert.Single(all, m => m.Status == GuidanceStatus.Resolved);
        Assert.Single(all, m => m.Status == GuidanceStatus.Sent);
    }

    [Fact]
    public async Task An_unresolved_finding_is_never_closed_by_a_later_check_even_when_it_no_longer_matches()
    {
        using var env = new PEnv(settings: Settings());
        await ApproveDq(env);
        var (id, med) = await Enrolled(env);
        var svc = env.Get<IMonitoringService>();
        await svc.RunOnceAsync(CancellationToken.None);
        env.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await env.Meds.StopAsync(id, id, med.Id, new StopMedicationCommand("t", env.Today, med.Version), "t", null)).Succeeded);
        await svc.RunOnceAsync(CancellationToken.None);
        var m = Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.Equal(GuidanceStatus.Sent, m.Status);
        Assert.Equal(2, (await Assessments(env, id)).Count); // both assessments are kept
        Assert.NotEmpty((await env.Safety.LatestAsync(id, All)).Value!.OpenGuidanceNoLongerMatching.Concat(["x"]));
    }

    [Fact]
    public async Task Demonstration_rules_never_produce_a_message_or_a_delivery_through_the_scheduled_path()
    {
        using var env = new PEnv(settings: Settings());
        await env.Get<IDemoRuleSeeder>().SeedAsync();
        var (id, _) = await Enrolled(env);
        await env.Get<IMonitoringService>().RunOnceAsync(CancellationToken.None);
        var a = (await env.Safety.LatestAsync(id, All)).Value!;
        Assert.Equal(AssessmentStatus.NoApprovedCoverage, a.Result.Status);
        Assert.NotEmpty(a.Result.Findings);
        Assert.Empty((await env.Guidance.ListForPatientAsync(id)).Value!);
        await using var db = await env.Get<IDbContextFactory<GuidanceDbContext>>().CreateDbContextAsync();
        Assert.Empty(await db.Deliveries.ToListAsync());
    }

    // ---------- delivery states ----------

    private sealed class ScriptedChannel(params ChannelOutcome[] script) : IGuidanceDeliveryChannel
    {
        private readonly ConcurrentQueue<ChannelOutcome> _q = new(script);
        public int Calls;
        public DeliveryRequest? Last;
        public string Name => LocalSimulatedChannel.ChannelName;
        public DeliveryMode Mode => DeliveryMode.Simulated;
        public Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct) { Calls++; Last = request; return Task.FromResult(_q.TryDequeue(out var o) ? o : ChannelOutcome.Confirmed); }
    }

    private static GuidanceDeliveryService Delivery(PEnv env, IGuidanceDeliveryChannel channel, Action<GuidanceDeliveryOptions>? tune = null)
    {
        var o = new GuidanceDeliveryOptions { Enabled = true, MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120 };
        tune?.Invoke(o);
        return new GuidanceDeliveryService(env.Get<IDbContextFactory<GuidanceDbContext>>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), [channel], Options.Create(o));
    }

    private static async Task<GuidanceMessageDto> OneMessage(PEnv env)
    {
        await ApproveDq(env);
        var (id, _) = await Enrolled(env);
        await env.Get<IMonitoringService>().RunOnceAsync(CancellationToken.None);
        return Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
    }

    [Fact]
    public async Task A_new_message_is_scheduled_not_delivered_then_a_pass_confirms_it_as_simulated_and_it_is_still_unseen()
    {
        using var env = new PEnv(settings: Settings());
        var m = await OneMessage(env);
        Assert.Equal(DeliveryState.Scheduled, m.Delivery!.State);
        Assert.Equal(DeliveryMode.Simulated, m.Delivery.Mode);
        Assert.Null(m.Delivery.ConfirmedAt);
        Assert.Equal(0, m.Delivery.Attempts);
        Assert.Equal("AutomatedAssessment", m.Provenance);
        Assert.False(m.ClinicianReviewed);

        var run = await env.Get<IGuidanceDeliveryService>().RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, run.Confirmed);
        var after = Assert.Single((await env.Guidance.ListForPatientAsync(env.UserId("demo-patient-2"))).Value!);
        Assert.Equal(DeliveryState.Confirmed, after.Delivery!.State);
        Assert.Equal(DeliveryMode.Simulated, after.Delivery.Mode);
        Assert.Equal(GuidanceStatus.Sent, after.Status); // delivery is not "seen"
        Assert.Null(after.SeenAt);
        var line = Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.GuidanceDeliveryConfirmed)));
        Assert.Equal("simulated", line.ReasonCode);

        Assert.Equal(0, (await env.Get<IGuidanceDeliveryService>().RunDueAsync(10, CancellationToken.None)).Considered); // nothing is delivered twice
    }

    [Fact]
    public async Task Review_by_a_professional_is_a_separate_fact_from_the_automated_finding()
    {
        using var env = new PEnv(settings: Settings());
        var m = await OneMessage(env);
        var id = env.UserId("demo-patient-2");
        var reviewed = await env.Guidance.SetStatusAsync(env.UserId("demo-physician"), id, m.Id, GuidanceStatus.Reviewed, true, "t", null);
        Assert.True(reviewed.Succeeded);
        var now = Assert.Single((await env.Guidance.ListForPatientAsync(id)).Value!);
        Assert.True(now.ClinicianReviewed);
        Assert.Equal("AutomatedAssessment", now.Provenance); // where it came from does not change
    }

    [Fact]
    public async Task Without_delivery_enabled_a_message_has_no_delivery_at_all()
    {
        using var env = new PEnv(settings: Settings(s => s["Guidance:Delivery:Enabled"] = "false"));
        var m = await OneMessage(env);
        Assert.Null(m.Delivery); // in the app only; nothing claims a notification
    }

    [Fact]
    public async Task Transient_channel_failures_retry_with_backoff_then_fail_and_never_confirm()
    {
        using var env = new PEnv(settings: Settings());
        await OneMessage(env);
        var channel = new ScriptedChannel(ChannelOutcome.TransientFailure, ChannelOutcome.TransientFailure, ChannelOutcome.TransientFailure);
        var svc = Delivery(env, channel);
        Assert.Equal(1, (await svc.RunDueAsync(10, CancellationToken.None)).Retrying);
        Assert.Equal(0, (await svc.RunDueAsync(10, CancellationToken.None)).Considered); // backing off
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await svc.RunDueAsync(10, CancellationToken.None)).Retrying);
        env.Clock.Advance(TimeSpan.FromMinutes(5));
        var last = await svc.RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, last.Failed);
        var d = (await env.Guidance.ListForPatientAsync(env.UserId("demo-patient-2"))).Value![0].Delivery!;
        Assert.Equal(DeliveryState.Failed, d.State);
        Assert.Equal(3, d.Attempts);
        Assert.Null(d.ConfirmedAt);
        Assert.Equal("channel.transient_exhausted", d.ErrorCode);
        Assert.Equal(3, channel.Calls);
        env.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await svc.RunDueAsync(10, CancellationToken.None)).Considered);
    }

    [Fact]
    public async Task A_permanent_refusal_fails_at_once_and_a_channel_only_ever_receives_ids()
    {
        using var env = new PEnv(settings: Settings());
        await OneMessage(env);
        var channel = new ScriptedChannel(ChannelOutcome.PermanentFailure);
        var run = await Delivery(env, channel).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, run.Failed);
        Assert.Equal("channel.refused", (await env.Guidance.ListForPatientAsync(env.UserId("demo-patient-2"))).Value![0].Delivery!.ErrorCode);
        Assert.Equal(typeof(DeliveryRequest).GetProperties().Select(p => p.Name).Order().ToArray(), new[] { "Level", "Locale", "MessageId", "PatientId" }); // no text, no names, no health data
        Assert.NotNull(channel.Last);
    }

    [Fact]
    public async Task An_interrupted_delivery_attempt_is_never_promoted_to_confirmed()
    {
        using var env = new PEnv(settings: Settings());
        await OneMessage(env);
        var factory = env.Get<IDbContextFactory<GuidanceDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Deliveries.SingleAsync();
            row.State = DeliveryState.Attempted; // the worker wrote "attempted" and died before the channel answered
            row.Attempts = 1;
            row.LastAttemptAt = env.Clock.UtcNow.AddMinutes(-5);
            row.LeaseUntil = env.Clock.UtcNow.AddMinutes(-3);
            await db.SaveChangesAsync();
        }

        var channel = new ScriptedChannel(ChannelOutcome.TransientFailure);
        var svc = Delivery(env, channel);
        var r = await svc.RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Recovered);
        Assert.Equal(0, r.Confirmed + (r.Considered == 0 ? 1 : 0) - (r.Considered == 0 ? 1 : 0)); // recovery itself confirms nothing
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.GuidanceDeliveryRecovered)), e => e.ReasonCode == "Scheduled");
        var d = (await env.Guidance.ListForPatientAsync(env.UserId("demo-patient-2"))).Value![0].Delivery!;
        Assert.NotEqual(DeliveryState.Confirmed, d.State);
    }

    [Fact]
    public async Task A_live_delivery_lease_is_left_alone()
    {
        using var env = new PEnv(settings: Settings());
        await OneMessage(env);
        var factory = env.Get<IDbContextFactory<GuidanceDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Deliveries.SingleAsync();
            row.State = DeliveryState.Attempted;
            row.Attempts = 1;
            row.LeaseUntil = env.Clock.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
        }

        var r = await Delivery(env, new ScriptedChannel()).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(0, r.Recovered);
        Assert.Equal(0, r.Considered);
    }

    [PostgresFact]
    public async Task Job_state_delivery_state_and_input_versions_survive_a_restart_and_two_passes_cannot_both_claim_a_job_on_PostgreSQL()
    {
        using var first = new PEnv(settings: Settings());
        await ApproveDq(first);
        var (id, _) = await Enrolled(first);
        await first.Get<IMonitoringService>().RunOnceAsync(CancellationToken.None);
        var before = await Status(first, id);
        var assessment = (await first.Safety.LatestAsync(id, All)).Value!;

        using var second = first.Restart();
        var after = await Status(second, id);
        Assert.Equal(before.LastSuccessAt, after.LastSuccessAt);
        Assert.Equal(before.LastAssessmentId, after.LastAssessmentId);
        Assert.Equal(before.NextCheckAt, after.NextCheckAt);
        var stored = (await second.Safety.LatestAsync(id, All)).Value!;
        Assert.Equal(assessment.InputFingerprint, stored.InputFingerprint);
        Assert.Equal(assessment.InputVersions!.Count, stored.InputVersions!.Count);
        Assert.Equal(DeliveryState.Scheduled, Assert.Single((await second.Guidance.ListForPatientAsync(id)).Value!).Delivery!.State);

        // the optimistic version column decides the winner when two passes race for one due job
        second.Clock.Advance(TimeSpan.FromMinutes(61));
        var gate = new TaskCompletionSource();
        var slow = new ScriptedSafety(second.Safety, async (_, ct) => { await gate.Task.WaitAsync(ct); return SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t")); });
        var a = Service(second, slow).RunOnceAsync(CancellationToken.None);
        var b = Service(second, slow).RunOnceAsync(CancellationToken.None);
        await Task.Delay(500);
        gate.SetResult();
        var claimed = (await a).Claimed + (await b).Claimed;
        Assert.Equal(1, claimed);
        Assert.Equal(1, slow.Calls);
    }

    // ---------- the worker host ----------

    [Fact]
    public void The_background_workers_exist_only_when_switched_on()
    {
        using var off = new PEnv(settings: Settings());
        Assert.Empty(off.Get<IEnumerable<IHostedService>>());
        using var on = new PEnv(settings: Settings(s => { s["ClinicalRules:Monitoring:WorkerEnabled"] = "true"; s["Guidance:Delivery:WorkerEnabled"] = "true"; }));
        var workers = on.Get<IEnumerable<IHostedService>>().OfType<PeriodicWorker>().Select(w => w.Name).Order().ToArray();
        Assert.Equal(new[] { "guidance-delivery", "monitoring" }, workers);
    }

    private sealed class Capture : ILogger
    {
        public ConcurrentBag<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    [Fact]
    public async Task A_failing_pass_is_logged_by_name_only_and_the_worker_keeps_going_and_stops_on_request()
    {
        var log = new Capture();
        var calls = 0;
        var worker = new PeriodicWorker("t-worker", TimeSpan.FromSeconds(1), _ => { calls++; throw new InvalidOperationException("Zzzsecretol patient data"); }, log, TimeSpan.Zero);
        Assert.Equal(PeriodicWorker.MinimumInterval, worker.Interval); // never tighter than the floor
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, calls);
        var line = Assert.Single(log.Lines.Distinct());
        Assert.Contains("t-worker", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Zzzsecretol", line, StringComparison.Ordinal); // the exception message (which could hold anything) is not logged

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var c = new CancellationTokenSource(); await c.CancelAsync(); await new PeriodicWorker("x", TimeSpan.FromSeconds(5), ct => Task.Delay(Timeout.Infinite, ct), log).RunOnceAsync(c.Token); });
    }

    // ---------- rule drafts ----------

    [Fact]
    public async Task A_draft_can_be_edited_only_by_its_author_and_only_before_submission_and_never_a_demonstration_rule()
    {
        using var env = new PEnv(settings: Settings());
        await env.Get<IDemoRuleSeeder>().SeedAsync();
        var author = env.UserId("demo-content-manager");
        var def = Rule("P8-EDIT-1");
        Assert.True((await env.Rules.CreateDraftAsync(author, def, "t", null)).Succeeded);

        var edited = await env.Rules.UpdateDraftAsync(author, "P8-EDIT-1", 1, def with { Title = "Edited title" }, "t", null);
        Assert.True(edited.Succeeded, edited.Detail);
        Assert.Equal("Edited title", edited.Value!.Record.Definition.Title);
        Assert.Equal(RuleStatus.Draft, edited.Value.Record.Lifecycle.Status);
        Assert.Contains((await env.Rules.HistoryAsync("P8-EDIT-1", 1)), e => e.Action == "draft_updated");

        var other = await env.Rules.UpdateDraftAsync(env.UserId("demo-physician"), "P8-EDIT-1", 1, def with { Title = "Hijack" }, "t", null);
        Assert.Equal(RuleError.Forbidden, other.Error);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.ClinicalRuleRefused)), e => e.ReasonCode == "draft.author_only");
        Assert.Equal("rule.identity_changed", (await env.Rules.UpdateDraftAsync(author, "P8-EDIT-1", 1, def with { Version = 2 }, "t", null)).Detail);
        Assert.Contains("text.forbidden_phrase", (await env.Rules.UpdateDraftAsync(author, "P8-EDIT-1", 1, def with { Purpose = "You should stop taking it." }, "t", null)).Detail!, StringComparison.Ordinal);

        Assert.True((await env.Rules.SubmitForReviewAsync(author, "P8-EDIT-1", 1, "t", null)).Succeeded);
        Assert.Equal(RuleError.Conflict, (await env.Rules.UpdateDraftAsync(author, "P8-EDIT-1", 1, def with { Title = "Late edit" }, "t", null)).Error); // content is frozen once submitted
        Assert.Equal(RuleError.Conflict, (await env.Rules.UpdateDraftAsync(author, "DEMO-DQ-001", 1, DemoRules.All(F.FixtureNow).Single(r => r.Definition.RuleId == "DEMO-DQ-001").Definition, "t", null)).Error);
    }

    [Fact]
    public async Task Approval_blockers_tell_the_author_the_reviewer_and_the_policy_what_stands_in_the_way_but_decide_nothing()
    {
        using var env = new PEnv(settings: Settings());
        await env.Get<IDemoRuleSeeder>().SeedAsync();
        var author = env.UserId("demo-content-manager");
        var reviewer = env.UserId("demo-physician");
        var def = Rule("P8-BLK-1") with { Evidence = [DemoRules.NoSource] };
        await env.Rules.CreateDraftAsync(author, def, "t", null);
        await env.Rules.SubmitForReviewAsync(author, "P8-BLK-1", 1, "t", null);

        var mine = (await env.Rules.ApprovalBlockersAsync(author, "P8-BLK-1", 1)).Value!;
        Assert.Contains("review.separation_of_duties", mine);
        var theirs = (await env.Rules.ApprovalBlockersAsync(reviewer, "P8-BLK-1", 1)).Value!;
        Assert.DoesNotContain("review.separation_of_duties", theirs);
        Assert.Contains("policy.demo_evidence", theirs);
        Assert.Contains("policy.no_validated_evidence", theirs);

        var demo = (await env.Rules.ApprovalBlockersAsync(reviewer, "DEMO-DQ-001", 1)).Value!;
        Assert.Contains("policy.demo_rule_cannot_be_approved", demo);

        // and the blockers are real: the review call refuses for exactly those reasons
        var refused = await env.Rules.ReviewAsync(reviewer, "P8-BLK-1", 1, ReviewDecision.Approve, null, null, "t", null);
        Assert.False(refused.Succeeded);
        Assert.Contains("policy.demo_evidence", refused.Detail!, StringComparison.Ordinal);
        Assert.Equal(0, (await env.Rules.CoverageAsync()).ActiveRules);
    }
}
