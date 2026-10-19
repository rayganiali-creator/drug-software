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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// Phase 19: monitoring correctness (ownership, cancellation, leases, audit accuracy), send-time eligibility, one open message per finding, and idempotent
/// at-least-once delivery. Local, synthetic data only; channels are recording fakes; nothing leaves the process. Tests marked PostgreSQL-only need
/// MEDSMARTER_PG_TEST: the in-memory provider cannot enforce unique indexes or concurrency tokens and these tests do not claim it can.
/// </summary>
public class Phase19Tests
{
    private static readonly string[] All = ["profile", "medications", "allergies", "symptoms"];

    private static Dictionary<string, string?> Settings() => new()
    {
        ["ClinicalRules:AllowDemonstrationRules"] = "true", ["ClinicalRules:SeedDemoRules"] = "true", ["Guidance:Delivery:Enabled"] = "true",
        ["ClinicalRules:Monitoring:MaxAttempts"] = "3", ["ClinicalRules:Monitoring:BackoffSeconds"] = "60", ["ClinicalRules:Monitoring:LeaseSeconds"] = "120",
    };

    // ---------- helpers: monitoring ----------

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

    /// <summary>Every reading of the time moves it on by one second, so successive claims in one pass get different times.</summary>
    private sealed class TickingClock(IClock inner) : IClock
    {
        private int _ticks;
        public DateTimeOffset UtcNow => inner.UtcNow.AddSeconds(Interlocked.Increment(ref _ticks));
    }

    private static SafetyOutcome<ReassessmentOutcome> Unchanged() => SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t"));

    private static MonitoringService Service(PEnv env, IClinicalSafetyService safety, Action<MonitoringOptions>? tune = null, IClock? clock = null)
    {
        var o = new MonitoringOptions { MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120, CheckIntervalMinutes = 60 };
        tune?.Invoke(o);
        return new MonitoringService(env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), clock ?? env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), safety, new AllowAll(), Options.Create(o));
    }

    private static async Task<List<Guid>> SeedJobs(PEnv env, int n)
    {
        var ids = new List<Guid>();
        await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        for (var i = 0; i < n; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.MonitoringJobs.Add(new MonitoringJobRow { PatientId = id, SubjectId = Guid.NewGuid(), EnrolledAt = env.Clock.UtcNow, NextCheckAt = env.Clock.UtcNow.AddSeconds(-n + i) });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private static async Task<List<MonitoringJobRow>> Jobs(PEnv env)
    {
        await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        return await db.MonitoringJobs.AsNoTracking().OrderBy(j => j.EnrolledAt).ThenBy(j => j.NextCheckAt).ToListAsync();
    }

    // ---------- 19.1 monitoring correctness ----------

    [Fact]
    public async Task An_applied_result_is_audited_as_applied_and_never_as_discarded()
    {
        using var env = new PEnv(settings: Settings());
        await SeedJobs(env, 1);
        var r = await Service(env, new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Unchanged()))).RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, r.Unchanged);
        Assert.Equal(0, r.Discarded);
        var line = Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringReassessed)));
        Assert.Equal("applied", line.Metadata["job_result"]);
        Assert.Empty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringResultDiscarded)));
    }

    [Fact]
    public async Task A_result_that_lost_ownership_is_audited_as_discarded_with_failure_and_never_as_reassessed()
    {
        using var env = new PEnv(settings: Settings());
        var ids = await SeedJobs(env, 1);
        var svc = Service(env, new ScriptedSafety(env.Safety, async (_, ct) =>
        {
            // while the check runs, the job is taken away (withdrawn): that decision must stand
            await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync(ct);
            var row = await db.MonitoringJobs.SingleAsync(ct);
            row.State = MonitoringJobState.Withdrawn;
            row.RowVersion++;
            await db.SaveChangesAsync(ct);
            return Unchanged();
        }));

        var r = await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, r.Claimed);
        Assert.Equal(1, r.Discarded);
        Assert.Equal(0, r.Reassessed + r.Unchanged + r.Failed + r.Skipped);
        Assert.Equal(MonitoringJobState.Withdrawn, (await Jobs(env)).Single().State); // not overwritten

        var line = Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringResultDiscarded)));
        Assert.Equal(AuditResult.Failure, line.Result);
        Assert.Equal("ownership_lost", line.ReasonCode);
        Assert.Equal("not_applied", line.Metadata["job_result"]);
        Assert.Equal("may_have_run", line.Metadata["assessment_status"]); // an assessment may have run; the job row was not updated
        Assert.Equal("unchanged", line.Metadata["computed_outcome"]);
        Assert.Equal(ids[0].ToString(), line.ResourceId);
        Assert.Empty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringReassessed))); // never labelled as applied
        Assert.Empty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringFailed)));
        Assert.True(await env.Audit.VerifyChainAsync()); // the chain is intact with the new action in it
    }

    [Fact]
    public async Task Cancelling_a_pass_hands_back_jobs_that_were_claimed_but_never_started_without_counting_a_failure()
    {
        using var env = new PEnv(settings: Settings());
        await SeedJobs(env, 4);
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var svc = Service(env, new ScriptedSafety(env.Safety, async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Unchanged();
        }), o => { o.BatchSize = 4; o.MaxConcurrency = 1; }); // one runs, three wait for the gate

        var pass = svc.RunOnceAsync(cts.Token);
        await started.Task;
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pass);

        var jobs = await Jobs(env);
        Assert.Equal(4, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal(MonitoringJobState.Idle, j.State); // none is left "Running" until a lease expires
            Assert.Null(j.LeaseUntil);
            Assert.Equal("cancelled", j.LastOutcome);
            Assert.Equal(0, j.ConsecutiveFailures);
            Assert.Null(j.LastSuccessAt);
        });
        Assert.Empty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringReassessed)));
        Assert.Empty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringResultDiscarded)));

        // the released jobs are due again at once
        var again = await Service(env, new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Unchanged())), o => o.BatchSize = 4).RunOnceAsync(CancellationToken.None);
        Assert.Equal(4, again.Claimed);
        Assert.Equal(4, again.Unchanged);
    }

    [Fact]
    public async Task Each_claim_gets_its_own_lease_time()
    {
        using var env = new PEnv(settings: Settings());
        await SeedJobs(env, 3);
        var gate = new TaskCompletionSource();
        var safety = new ScriptedSafety(env.Safety, async (_, ct) => { await gate.Task.WaitAsync(ct); return Unchanged(); });
        var pass = Service(env, safety, o => { o.BatchSize = 3; o.MaxConcurrency = 3; }, new TickingClock(env.Clock)).RunOnceAsync(CancellationToken.None);
        for (var i = 0; i < 100 && safety.Calls < 3; i++)
        {
            await Task.Delay(20);
        }

        var running = (await Jobs(env)).Where(j => j.State == MonitoringJobState.Running).Select(j => j.LeaseUntil!.Value).Order().ToList();
        gate.SetResult();
        await pass;
        Assert.Equal(3, running.Count);
        Assert.Equal(3, running.Distinct().Count()); // not one shared timestamp for the whole batch
    }

    [Fact]
    public async Task Recovery_audits_each_recovered_job_once_and_only_those()
    {
        using var env = new PEnv(settings: Settings());
        await SeedJobs(env, 3);
        await using (var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync())
        {
            foreach (var j in await db.MonitoringJobs.OrderBy(j => j.NextCheckAt).Take(2).ToListAsync())
            {
                j.State = MonitoringJobState.Running; // two died mid-check; the third was never claimed
                j.LeaseUntil = env.Clock.UtcNow.AddSeconds(-1);
            }

            await db.SaveChangesAsync();
        }

        var r = await Service(env, new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Unchanged()))).RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, r.Recovered);
        Assert.Equal(2, (await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringFailed))).Count(e => e.ReasonCode == "interrupted"));
    }

    [PostgresFact]
    public async Task Two_passes_recovering_the_same_interrupted_job_recover_and_audit_it_exactly_once_on_PostgreSQL()
    {
        using var env = new PEnv(settings: Settings());
        await SeedJobs(env, 1);
        await using (var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync())
        {
            var j = await db.MonitoringJobs.SingleAsync();
            j.State = MonitoringJobState.Running;
            j.LeaseUntil = env.Clock.UtcNow.AddSeconds(-1);
            j.NextCheckAt = DateTimeOffset.MaxValue; // recovery is under test, not the claim that follows
            await db.SaveChangesAsync();
        }

        var safety = new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Unchanged()));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Service(env, safety).RunOnceAsync(CancellationToken.None))));
        Assert.Equal(1, results.Sum(r => r.Recovered));
        Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.MonitoringFailed)));
    }

    // ---------- 19.2 / 19.4 delivery: harness without the full stack ----------

    private sealed class FailNthSave(int n, Func<Exception>? make = null) : SaveChangesInterceptor
    {
        private int _count;
        public int Saves => _count;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == n)
            {
                throw make?.Invoke() ?? new DbUpdateException("injected save failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class InMemoryFactory(string name, params IInterceptor[] interceptors) : IDbContextFactory<GuidanceDbContext>
    {
        private static readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot Root = new();
        public GuidanceDbContext CreateDbContext() => new(new DbContextOptionsBuilder<GuidanceDbContext>().UseInMemoryDatabase(name, Root).AddInterceptors(interceptors).Options);
    }

    private sealed class FakeDirectory(Guid patientId, Guid subjectId) : IPatientDirectory
    {
        public PatientStatus? Status = PatientStatus.Active; // null = no such record
        public Task<PatientRecordRef?> FindBySubjectAsync(Guid s, CancellationToken ct = default) => Find();
        public Task<PatientRecordRef?> FindByPatientIdAsync(Guid p, CancellationToken ct = default) => Find();
        public Task<IReadOnlyList<PatientMedicationDto>> ActiveMedicationsAsync(Guid s, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PatientMedicationDto>>([]);
        public Task TouchCategoryAsync(Guid s, PatientDataCategory c, Guid? a, int n, CancellationToken ct = default) => Task.CompletedTask;
        private Task<PatientRecordRef?> Find() => Task.FromResult(Status is { } st ? new PatientRecordRef(patientId, subjectId, st, null, null) : null);
    }

    private sealed class RecordingAudit : IAuditWriter
    {
        public ConcurrentQueue<AuditEvent> Events { get; } = new();
        public Task WriteAsync(AuditEvent e, CancellationToken ct = default) { Events.Enqueue(e); return Task.CompletedTask; }
    }

    private sealed class RecordingChannel(params ChannelOutcome[] script) : IGuidanceDeliveryChannel
    {
        private readonly ConcurrentQueue<ChannelOutcome> _q = new(script);
        public ConcurrentQueue<DeliveryRequest> Requests { get; } = new();
        public string Name => LocalSimulatedChannel.ChannelName;
        public DeliveryMode Mode => DeliveryMode.Simulated;
        public Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct) { Requests.Enqueue(request); return Task.FromResult(_q.TryDequeue(out var o) ? o : ChannelOutcome.Confirmed); }
    }

    private sealed class Harness
    {
        public Guid PatientId = Guid.NewGuid();
        public Guid SubjectId = Guid.NewGuid();
        public Guid MessageId = Guid.NewGuid();
        public Guid DeliveryId = Guid.NewGuid();
        public FakeClock Clock = new();
        public FakeDirectory Directory;
        public RecordingAudit Audit = new();
        public InMemoryFactory Factory;

        public Harness(IInterceptor? interceptor = null, GuidanceStatus messageStatus = GuidanceStatus.Sent)
        {
            Directory = new FakeDirectory(PatientId, SubjectId);
            Factory = new InMemoryFactory("p19-" + Guid.NewGuid().ToString("N"), interceptor is null ? [] : [interceptor]);
            using var db = Factory.CreateDbContext();
            db.Messages.Add(new GuidanceMessageRow { Id = MessageId, PatientId = PatientId, TemplateKey = "t", Level = GuidanceLevel.FollowUp, Status = messageStatus, Locale = "en", Confidence = GuidanceConfidence.Low, CreatedAt = Clock.UtcNow, IsDemo = true });
            db.Deliveries.Add(new GuidanceDeliveryRow { Id = DeliveryId, MessageId = MessageId, PatientId = PatientId, Channel = LocalSimulatedChannel.ChannelName, Mode = DeliveryMode.Simulated, State = DeliveryState.Scheduled, ScheduledAt = Clock.UtcNow, NextAttemptAt = Clock.UtcNow });
            db.SaveChanges();
        }

        public GuidanceDeliveryService Service(IGuidanceDeliveryChannel channel) =>
            new(Factory, Clock, Audit, Directory, [channel], Options.Create(new GuidanceDeliveryOptions { Enabled = true, MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120 }));

        public GuidanceDeliveryRow Delivery()
        {
            using var db = Factory.CreateDbContext();
            return db.Deliveries.AsNoTracking().Single();
        }
    }

    // ---------- 19.2 send-time eligibility ----------

    [Fact]
    public async Task An_inactive_patient_is_deferred_not_attempted_and_the_skip_is_audited_once_without_content()
    {
        var h = new Harness();
        h.Directory.Status = PatientStatus.Inactive;
        var channel = new RecordingChannel();
        var svc = h.Service(channel);

        var first = await svc.RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, first.Skipped);
        Assert.Empty(channel.Requests); // nothing was sent
        var d = h.Delivery();
        Assert.Equal(DeliveryState.Scheduled, d.State); // not ended: the status can be undone
        Assert.Equal(0, d.Attempts); // not counted as an attempt
        Assert.Equal("patient.inactive", d.ErrorCode);
        Assert.Equal(h.Clock.UtcNow.AddSeconds(60), d.NextAttemptAt);

        h.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await svc.RunDueAsync(10, CancellationToken.None)).Skipped);
        Assert.Empty(channel.Requests);
        var line = Assert.Single(h.Audit.Events, e => e.Action == AuditActions.GuidanceDeliverySkipped); // once per reason, not once per pass
        Assert.Equal("patient.inactive", line.ReasonCode);
        Assert.Equal(["channel", "mode", "outcome", "sent"], line.Metadata!.Keys.Order().ToArray()); // codes only: no text, no contact data
        Assert.Equal("false", line.Metadata["sent"]);
        Assert.Equal("deferred", line.Metadata["outcome"]);

        // the patient is active again: the same delivery now goes out, with its attempt counted from here
        h.Directory.Status = PatientStatus.Active;
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await svc.RunDueAsync(10, CancellationToken.None)).Confirmed);
        var after = h.Delivery();
        Assert.Equal(DeliveryState.Confirmed, after.State);
        Assert.Equal(1, after.Attempts);
        Assert.Null(after.ErrorCode);
        Assert.Single(channel.Requests);
    }

    [Fact]
    public async Task A_resolved_message_ends_its_delivery_without_a_send_and_it_stays_ended()
    {
        var h = new Harness(messageStatus: GuidanceStatus.Resolved);
        var channel = new RecordingChannel();
        var svc = h.Service(channel);
        var r = await svc.RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Skipped);
        Assert.Equal(1, r.Failed);
        Assert.Empty(channel.Requests);
        var d = h.Delivery();
        Assert.Equal(DeliveryState.Failed, d.State);
        Assert.Equal("message.resolved", d.ErrorCode);
        Assert.Equal(0, d.Attempts);
        var line = Assert.Single(h.Audit.Events);
        Assert.Equal(AuditActions.GuidanceDeliverySkipped, line.Action);
        Assert.Equal("ended", line.Metadata!["outcome"]);

        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await svc.RunDueAsync(10, CancellationToken.None)).Considered); // terminal: not picked up again
    }

    [Fact]
    public async Task A_resolved_message_can_never_be_reopened_so_ending_its_delivery_loses_nothing()
    {
        // the reason "message.resolved" is terminal while "patient.inactive" is not: no allowed status move leaves Resolved
        foreach (var to in Enum.GetValues<GuidanceStatus>())
        {
            Assert.False(GuidanceService.MoveAllowed(GuidanceStatus.Resolved, to, true));
            Assert.False(GuidanceService.MoveAllowed(GuidanceStatus.Resolved, to, false));
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_missing_patient_record_ends_the_delivery_without_a_send()
    {
        var h = new Harness();
        h.Directory.Status = null;
        var channel = new RecordingChannel();
        var r = await h.Service(channel).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Skipped);
        Assert.Empty(channel.Requests);
        Assert.Equal("patient.missing", h.Delivery().ErrorCode);
        Assert.Equal(DeliveryState.Failed, h.Delivery().State);
    }

    [Fact]
    public async Task An_eligible_delivery_is_unchanged_by_the_check_and_carries_a_stable_idempotency_key()
    {
        var h = new Harness();
        var channel = new RecordingChannel(ChannelOutcome.TransientFailure, ChannelOutcome.Confirmed);
        var svc = h.Service(channel);
        Assert.Equal(1, (await svc.RunDueAsync(10, CancellationToken.None)).Retrying);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, (await svc.RunDueAsync(10, CancellationToken.None)).Confirmed);
        Assert.Equal(2, channel.Requests.Count);
        Assert.Equal(h.DeliveryId.ToString("N"), channel.Requests.First().IdempotencyKey);
        Assert.Equal(channel.Requests.First().IdempotencyKey, channel.Requests.Last().IdempotencyKey); // same key for every attempt of one delivery
        Assert.DoesNotContain(h.Audit.Events, e => e.Action == AuditActions.GuidanceDeliverySkipped);
    }

    // ---------- 19.4 a failed final save: at-least-once, same key, nothing audited as delivered ----------

    [Fact]
    public async Task When_the_outcome_cannot_be_saved_the_row_stays_attempted_and_the_retry_reuses_the_same_key()
    {
        var interceptor = new FailNthSave(3); // save 1 = seeding the rows; save 2 = "attempted"; save 3 = the outcome
        var h = new Harness(interceptor);
        var channel = new RecordingChannel();
        var svc = h.Service(channel);

        var first = await svc.RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, first.Unrecorded);
        Assert.Equal(0, first.Confirmed);
        Assert.Single(channel.Requests); // the channel was called...
        var d = h.Delivery();
        Assert.Equal(DeliveryState.Attempted, d.State); // ...but the row does not claim a delivery
        Assert.Null(d.ConfirmedAt);
        Assert.DoesNotContain(h.Audit.Events, e => e.Action == AuditActions.GuidanceDeliveryConfirmed);

        h.Clock.Advance(TimeSpan.FromMinutes(5)); // the lease runs out
        var second = await svc.RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, second.Recovered);
        Assert.Equal(1, second.Confirmed);
        Assert.Equal(2, channel.Requests.Count); // at-least-once: one logical delivery reached the channel twice
        Assert.Single(channel.Requests.Select(r => r.IdempotencyKey).Distinct()); // with the same key, so a channel that supports idempotency can drop the repeat
        Assert.Equal(h.DeliveryId.ToString("N"), channel.Requests.First().IdempotencyKey);
        Assert.Equal(DeliveryState.Confirmed, h.Delivery().State);
        Assert.Single(h.Audit.Events, e => e.Action == AuditActions.GuidanceDeliveryConfirmed); // confirmed once, only after it was recorded
    }

    // ---------- hardening: which database failures are handled, which are surfaced ----------
    // Save numbering in the harness: 1 = seeding the rows; then 2 = claim, skip or terminal write (before any channel call); 3 = the channel's outcome (after the call).

    private sealed class OtherChannel : IGuidanceDeliveryChannel
    {
        public string Name => "some-other-channel";
        public DeliveryMode Mode => DeliveryMode.Simulated;
        public Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct) => Task.FromResult(ChannelOutcome.Confirmed);
    }

    private static void AssertNothingReportedAsDelivered(Harness h) =>
        Assert.DoesNotContain(h.Audit.Events, e => e.Action is AuditActions.GuidanceDeliveryConfirmed or AuditActions.GuidanceDeliveryFailed or AuditActions.GuidanceDeliverySkipped);

    [Fact]
    public async Task A_rejected_claim_write_is_surfaced_not_swallowed_and_nothing_is_sent_or_audited()
    {
        var h = new Harness(new FailNthSave(2));
        var channel = new RecordingChannel();
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Service(channel).RunDueAsync(10, CancellationToken.None));
        Assert.Empty(channel.Requests); // the attempt was never recorded, so the channel was never called
        var d = h.Delivery();
        Assert.Equal(DeliveryState.Scheduled, d.State);
        Assert.Equal(0, d.Attempts);
        AssertNothingReportedAsDelivered(h);
    }

    [Fact]
    public async Task A_concurrency_conflict_on_the_claim_is_still_handled_quietly_because_the_row_belongs_to_another_instance()
    {
        var h = new Harness(new FailNthSave(2, () => new DbUpdateConcurrencyException("someone else claimed it")));
        var channel = new RecordingChannel();
        var r = await h.Service(channel).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Considered);
        Assert.Equal(0, r.Confirmed + r.Failed + r.Retrying + r.Skipped + r.Unrecorded);
        Assert.Empty(channel.Requests);
        AssertNothingReportedAsDelivered(h);
    }

    [Fact]
    public async Task A_rejected_write_that_would_end_a_delivery_is_surfaced_and_the_delivery_is_not_ended_or_audited()
    {
        var h = new Harness(new FailNthSave(2), GuidanceStatus.Resolved);
        var channel = new RecordingChannel();
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Service(channel).RunDueAsync(10, CancellationToken.None));
        var d = h.Delivery();
        Assert.Equal(DeliveryState.Scheduled, d.State); // not ended: the write failed, so it is tried again on the next pass
        Assert.Null(d.ErrorCode);
        Assert.Empty(channel.Requests);
        AssertNothingReportedAsDelivered(h);
    }

    [Fact]
    public async Task A_rejected_write_that_would_defer_an_inactive_patient_is_surfaced_and_not_audited()
    {
        var h = new Harness(new FailNthSave(2));
        h.Directory.Status = PatientStatus.Inactive;
        var channel = new RecordingChannel();
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Service(channel).RunDueAsync(10, CancellationToken.None));
        Assert.Null(h.Delivery().ErrorCode);
        Assert.Empty(channel.Requests);
        AssertNothingReportedAsDelivered(h);
    }

    [Fact]
    public async Task A_concurrency_conflict_while_ending_a_delivery_is_handled_quietly_and_counted_as_neither_skipped_nor_failed()
    {
        var h = new Harness(new FailNthSave(2, () => new DbUpdateConcurrencyException("changed by another instance")), GuidanceStatus.Resolved);
        var r = await h.Service(new RecordingChannel()).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(0, r.Skipped + r.Failed);
        Assert.Equal(DeliveryState.Scheduled, h.Delivery().State); // the other instance's decision stands
        AssertNothingReportedAsDelivered(h);
    }

    [Fact]
    public async Task A_rejected_write_of_the_no_channel_failure_is_surfaced_and_not_audited()
    {
        var h = new Harness(new FailNthSave(2));
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Service(new OtherChannel()).RunDueAsync(10, CancellationToken.None)); // the row names a channel this service does not have
        Assert.Equal(DeliveryState.Scheduled, h.Delivery().State);
        AssertNothingReportedAsDelivered(h);
    }

    [Theory]
    [InlineData(ChannelOutcome.Confirmed)]
    [InlineData(ChannelOutcome.TransientFailure)]
    [InlineData(ChannelOutcome.PermanentFailure)]
    public async Task Whatever_the_channel_answered_a_rejected_outcome_write_is_counted_as_unrecorded_and_never_audited(ChannelOutcome answer)
    {
        var h = new Harness(new FailNthSave(3));
        var channel = new RecordingChannel(answer);
        var r = await h.Service(channel).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Unrecorded);
        Assert.Equal(0, r.Confirmed + r.Failed + r.Retrying);
        Assert.Single(channel.Requests);
        var d = h.Delivery();
        Assert.Equal(DeliveryState.Attempted, d.State); // the row never claims a delivery, a retry or a failure that was not stored
        Assert.Null(d.ConfirmedAt);
        AssertNothingReportedAsDelivered(h);
    }

    [Fact]
    public async Task A_concurrency_conflict_on_the_outcome_write_is_also_counted_as_unrecorded_and_not_audited()
    {
        var h = new Harness(new FailNthSave(3, () => new DbUpdateConcurrencyException("recovery handed the row on")));
        var r = await h.Service(new RecordingChannel()).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Unrecorded);
        Assert.Equal(DeliveryState.Attempted, h.Delivery().State);
        AssertNothingReportedAsDelivered(h);
    }

    // ---------- 19.3 one open message per finding ----------

    private static GuidanceRequest Request() => new("adherence.skipped_pattern", GuidanceLevel.FollowUp, "en", new Dictionary<string, string> { ["medication"] = "Demopril", ["count"] = "3", ["days"] = "7" }, GuidanceConfidence.Low, "DEMO", true);

    private static GuidanceOrigin Origin(string key, DateTimeOffset asOf, string? fingerprint = null) => new("Assessment", Guid.NewGuid(), "DEMO-RULE", 1, key, asOf, fingerprint);

    [PostgresFact]
    public async Task Concurrent_raising_of_the_same_finding_stores_one_message_one_delivery_and_one_scheduled_event_on_PostgreSQL()
    {
        using var env = new PEnv(settings: Settings());
        var subject = await env.Patient("demo-patient");
        const int racers = 12;
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            return await env.Guidance.CreateFromOriginAsync(subject, Request(), Origin("race-key", env.Clock.UtcNow), true, "t", null);
        })).ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Succeeded, r.Detail));
        Assert.Single(results.Select(r => r.Value!.Message.Id).Distinct()); // everyone got the same message back
        Assert.Equal(1, results.Count(r => !r.Value!.Existing)); // exactly one created it
        await using var db = await env.Get<IDbContextFactory<GuidanceDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.OriginFindingKey == "race-key"));
        Assert.Single(await db.Deliveries.ToListAsync()); // no orphan delivery from a losing request
        Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.GuidanceDeliveryScheduled))); // no event for something that was not stored
    }

    [PostgresFact]
    public async Task The_database_itself_refuses_a_second_open_message_for_a_finding_but_allows_history_and_findingless_messages()
    {
        using var env = new PEnv(settings: Settings());
        var factory = env.Get<IDbContextFactory<GuidanceDbContext>>();
        var patient = Guid.NewGuid();
        static GuidanceMessageRow Row(Guid patient, string? key, GuidanceStatus status) => new() { Id = Guid.NewGuid(), PatientId = patient, TemplateKey = "t", Level = GuidanceLevel.FollowUp, Status = status, Locale = "en", Confidence = GuidanceConfidence.Low, CreatedAt = DateTimeOffset.UtcNow, IsDemo = true, OriginFindingKey = key };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Messages.AddRange(Row(patient, "k", GuidanceStatus.Resolved), Row(patient, "k", GuidanceStatus.Resolved), Row(patient, "k", GuidanceStatus.Sent), Row(patient, null, GuidanceStatus.Sent), Row(patient, null, GuidanceStatus.Sent), Row(Guid.NewGuid(), "k", GuidanceStatus.Sent));
            await db.SaveChangesAsync(); // resolved history, one open per (patient, finding), no key, another patient: all allowed
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Messages.Add(Row(patient, "k", GuidanceStatus.Seen));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pg = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
            Assert.Equal(GuidanceDbContext.OpenFindingIndex, pg.ConstraintName);
        }
    }

    [PostgresFact]
    public async Task A_resolved_finding_can_be_raised_again_when_the_data_changed_and_it_is_still_one_open_message_on_PostgreSQL()
    {
        using var env = new PEnv(settings: Settings());
        var subject = await env.Patient("demo-patient");
        var first = (await env.Guidance.CreateFromOriginAsync(subject, Request(), Origin("again-key", env.Clock.UtcNow, "fp-1"), true, "t", null)).Value!;
        Assert.False(first.Existing);
        Assert.True((await env.Guidance.SetStatusAsync(subject, subject, first.Message.Id, GuidanceStatus.Resolved, false, "t", null)).Succeeded);

        env.Clock.Advance(TimeSpan.FromHours(1));
        var unchanged = (await env.Guidance.CreateFromOriginAsync(subject, Request(), Origin("again-key", env.Clock.UtcNow, "fp-1"), true, "t", null)).Value!;
        Assert.True(unchanged.Existing); // same records: stays resolved
        Assert.Equal(first.Message.Id, unchanged.Message.Id);

        var changed = (await env.Guidance.CreateFromOriginAsync(subject, Request(), Origin("again-key", env.Clock.UtcNow, "fp-2"), true, "t", null)).Value!;
        Assert.False(changed.Existing); // the data changed: a new open message, the resolved one stays as history
        Assert.NotEqual(first.Message.Id, changed.Message.Id);
        await using var db = await env.Get<IDbContextFactory<GuidanceDbContext>>().CreateDbContextAsync();
        Assert.Equal(2, await db.Messages.CountAsync(m => m.OriginFindingKey == "again-key"));
        Assert.Equal(1, await db.Messages.CountAsync(m => m.OriginFindingKey == "again-key" && m.Status != GuidanceStatus.Resolved));
    }
}
