using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Guidance;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Guidance.Persistence.Migrations;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// Phase 20: worker observability (counts-only summary logs), isolation of a poisoned row, bounded and fair handling of deferred (inactive-patient) deliveries,
/// stale error codes, and a guard for the open-finding index predicate. Local, synthetic data only; channels are recording fakes; nothing leaves the process.
/// Tests marked PostgreSQL-only need MEDSMARTER_PG_TEST: the in-memory provider cannot enforce concurrency tokens and these tests do not claim it can.
/// </summary>
public class Phase20Tests
{
    // ---------- helpers ----------

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Enqueue((logLevel, formatter(state, exception)));
        public (LogLevel Level, string Message) Summary() => Lines.Single(l => l.Message.Contains(" pass ", StringComparison.Ordinal));
    }

    private sealed class RecordingAudit : IAuditWriter
    {
        public ConcurrentQueue<AuditEvent> Events { get; } = new();
        public Task WriteAsync(AuditEvent e, CancellationToken ct = default) { Events.Enqueue(e); return Task.CompletedTask; }
    }

    private sealed class RecordingChannel : IGuidanceDeliveryChannel
    {
        public ConcurrentQueue<DeliveryRequest> Requests { get; } = new();
        public Func<DeliveryRequest, Task>? OnCall;
        public string Name => LocalSimulatedChannel.ChannelName;
        public DeliveryMode Mode => DeliveryMode.Simulated;

        public async Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct)
        {
            Requests.Enqueue(request);
            if (OnCall is not null)
            {
                await OnCall(request);
            }

            return ChannelOutcome.Confirmed;
        }
    }

    /// <summary>Per-patient answers: active unless listed.</summary>
    private sealed class Directory : IPatientDirectory
    {
        public ConcurrentDictionary<Guid, PatientStatus> Status { get; } = new();
        public ConcurrentDictionary<Guid, bool> Missing { get; } = new();
        public ConcurrentDictionary<Guid, bool> Throwing { get; } = new();

        public Task<PatientRecordRef?> FindByPatientIdAsync(Guid patientId, CancellationToken ct = default)
        {
            if (Throwing.ContainsKey(patientId))
            {
                throw new InvalidOperationException("lookup-detail-must-not-be-logged");
            }

            return Task.FromResult<PatientRecordRef?>(Missing.ContainsKey(patientId) ? null : new PatientRecordRef(patientId, patientId, Status.GetValueOrDefault(patientId, PatientStatus.Active), null, null));
        }

        public Task<PatientRecordRef?> FindBySubjectAsync(Guid s, CancellationToken ct = default) => FindByPatientIdAsync(s, ct);
        public Task<IReadOnlyList<PatientMedicationDto>> ActiveMedicationsAsync(Guid s, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PatientMedicationDto>>([]);
        public Task TouchCategoryAsync(Guid s, PatientDataCategory c, Guid? a, int n, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Rejects every write that touches the given delivery row (a persistent, row-specific database failure) and counts the attempts.</summary>
    private sealed class PoisonRow : SaveChangesInterceptor
    {
        private int _rejected;
        public int Rejected => _rejected;
        public ConcurrentDictionary<Guid, bool> Ids { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<GuidanceDeliveryRow>().Any(e => e.State == EntityState.Modified && Ids.ContainsKey(e.Entity.Id)))
            {
                Interlocked.Increment(ref _rejected);
                throw new DbUpdateException("injected persistent write failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailNthSave(int n) : SaveChangesInterceptor
    {
        private int _count;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == n)
            {
                throw new DbUpdateException("injected save failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class SharedFactory(string name, IInterceptor[] interceptors) : IDbContextFactory<GuidanceDbContext>
    {
        private static readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot Root = new();
        public GuidanceDbContext CreateDbContext() => new(new DbContextOptionsBuilder<GuidanceDbContext>().UseInMemoryDatabase(name, Root).AddInterceptors(interceptors).Options);
    }

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new();
        public Directory Patients { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public CapturingLogger<GuidanceDeliveryService> Log { get; } = new();
        public SharedFactory Factory { get; }
        private readonly SharedFactory _seed;

        public Harness(params IInterceptor[] interceptors)
        {
            var name = "p20-" + Guid.NewGuid().ToString("N");
            Factory = new SharedFactory(name, interceptors);
            _seed = new SharedFactory(name, []);
        }

        public (Guid Message, Guid Delivery, Guid Patient) Add(TimeSpan? dueIn = null, string? errorCode = null, GuidanceStatus status = GuidanceStatus.Sent, TimeSpan? scheduledAgo = null)
        {
            var (m, d, p) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            using var db = _seed.CreateDbContext();
            db.Messages.Add(new GuidanceMessageRow { Id = m, PatientId = p, TemplateKey = "t", Level = GuidanceLevel.FollowUp, Status = status, Locale = "en", Confidence = GuidanceConfidence.Low, CreatedAt = Clock.UtcNow, IsDemo = true });
            db.Deliveries.Add(new GuidanceDeliveryRow
            {
                Id = d, MessageId = m, PatientId = p, Channel = LocalSimulatedChannel.ChannelName, Mode = DeliveryMode.Simulated, State = DeliveryState.Scheduled, ErrorCode = errorCode,
                ScheduledAt = Clock.UtcNow - (scheduledAgo ?? TimeSpan.Zero), NextAttemptAt = Clock.UtcNow + (dueIn ?? TimeSpan.Zero),
            });
            db.SaveChanges();
            return (m, d, p);
        }

        public GuidanceDeliveryRow Row(Guid deliveryId)
        {
            using var db = _seed.CreateDbContext();
            return db.Deliveries.AsNoTracking().Single(r => r.Id == deliveryId);
        }

        public GuidanceDeliveryService Service(IGuidanceDeliveryChannel channel, Action<GuidanceDeliveryOptions>? tune = null)
        {
            var o = new GuidanceDeliveryOptions { Enabled = true, MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120 };
            tune?.Invoke(o);
            return new GuidanceDeliveryService(Factory, Clock, Audit, Patients, [channel], Options.Create(o), Log);
        }
    }

    private static readonly Regex IdLike = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}|[0-9a-fA-F]{20,}", RegexOptions.Compiled);

    private static void AssertNoIdentifiers(IEnumerable<string> lines) => Assert.All(lines, l => Assert.DoesNotMatch(IdLike, l));

    // ---------- A: structured, counts-only pass summaries ----------

    [Fact]
    public async Task A_completed_delivery_pass_logs_one_information_summary_with_the_counts_and_no_identifiers()
    {
        var h = new Harness();
        h.Add();
        h.Add();
        var r = await h.Service(new RecordingChannel()).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(2, r.Confirmed);
        var (level, message) = h.Log.Summary();
        Assert.Equal(LogLevel.Information, level);
        Assert.Equal("Guidance delivery pass completed: considered=2 confirmed=2 retrying=0 failed=0 skipped=0 deferred=0 recovered=0 unrecorded=0 errors=0", message);
        AssertNoIdentifiers(h.Log.Lines.Select(l => l.Message));
    }

    [Fact]
    public async Task A_pass_with_a_rejected_write_is_a_warning_not_a_clean_success_and_the_error_is_in_the_result_and_the_log()
    {
        var poison = new PoisonRow();
        var h = new Harness(poison);
        poison.Ids[h.Add(TimeSpan.FromSeconds(-30)).Delivery] = true;
        h.Add();
        var r = await h.Service(new RecordingChannel()).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Errors);
        Assert.Equal(1, r.Confirmed);
        var (level, message) = h.Log.Summary();
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("completed_with_errors", message, StringComparison.Ordinal);
        Assert.Contains("errors=1", message, StringComparison.Ordinal);
        Assert.Contains(h.Log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("record-attempt", StringComparison.Ordinal) && l.Message.Contains("DbUpdateException", StringComparison.Ordinal));
        AssertNoIdentifiers(h.Log.Lines.Select(l => l.Message));
    }

    [Fact]
    public async Task An_unrecorded_outcome_makes_the_pass_a_warning_and_is_counted()
    {
        var h = new Harness(new FailNthSave(3)); // save 1 = the (empty) recovery step, save 2 = the attempt, save 3 = the outcome
        h.Add();
        var r = await h.Service(new RecordingChannel()).RunDueAsync(10, CancellationToken.None);
        Assert.Equal(1, r.Unrecorded);
        var (level, message) = h.Log.Summary();
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("completed_with_errors", message, StringComparison.Ordinal);
        Assert.Contains("unrecorded=1", message, StringComparison.Ordinal);
    }

    private sealed class ThrowingAuditOnConfirm : IAuditWriter
    {
        public Task WriteAsync(AuditEvent e, CancellationToken ct = default) =>
            e.Action == AuditActions.GuidanceDeliveryConfirmed ? throw new InvalidOperationException("audit-detail-must-not-be-logged") : Task.CompletedTask;
    }

    [Fact]
    public async Task A_pass_that_stops_part_way_logs_an_error_with_the_work_done_so_far_and_never_a_success()
    {
        var h = new Harness();
        h.Add(TimeSpan.FromSeconds(-30));
        h.Add();
        var o = Options.Create(new GuidanceDeliveryOptions { Enabled = true });
        var svc = new GuidanceDeliveryService(h.Factory, h.Clock, new ThrowingAuditOnConfirm(), h.Patients, [new RecordingChannel()], o, h.Log);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RunDueAsync(10, CancellationToken.None));
        var (level, message) = h.Log.Summary();
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("failed part-way (InvalidOperationException)", message, StringComparison.Ordinal);
        Assert.Contains("considered=1", message, StringComparison.Ordinal);
        Assert.Contains("confirmed=1", message, StringComparison.Ordinal); // the first delivery was stored before its audit write failed
        Assert.DoesNotContain("completed", message, StringComparison.Ordinal);
        Assert.All(h.Log.Lines, l => Assert.DoesNotContain("audit-detail", l.Message, StringComparison.Ordinal)); // the exception message is never logged
    }

    [Fact]
    public async Task A_cancelled_pass_logs_cancelled_with_partial_counts()
    {
        var h = new Harness();
        h.Add(TimeSpan.FromSeconds(-30));
        h.Add();
        using var cts = new CancellationTokenSource();
        var channel = new RecordingChannel { OnCall = _ => cts.CancelAsync() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Service(channel).RunDueAsync(10, cts.Token));
        var (level, message) = h.Log.Summary();
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("cancelled", message, StringComparison.Ordinal);
        Assert.Contains("considered=1", message, StringComparison.Ordinal);
    }

    // ---------- A: monitoring ----------

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

    private sealed class ThrowOnceOnReassessed(IAuditWriter inner) : IAuditWriter
    {
        private int _thrown;

        public Task WriteAsync(AuditEvent e, CancellationToken ct = default) =>
            e.Action == AuditActions.MonitoringReassessed && Interlocked.Exchange(ref _thrown, 1) == 0 ? throw new InvalidOperationException("audit-detail-must-not-be-logged") : inner.WriteAsync(e, ct);
    }

    private static SafetyOutcome<ReassessmentOutcome> Unchanged() => SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t"));

    private static async Task SeedJobs(PEnv env, int n)
    {
        await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        for (var i = 0; i < n; i++)
        {
            db.MonitoringJobs.Add(new MonitoringJobRow { PatientId = Guid.NewGuid(), SubjectId = Guid.NewGuid(), EnrolledAt = env.Clock.UtcNow, NextCheckAt = env.Clock.UtcNow.AddSeconds(-n + i) });
        }

        await db.SaveChangesAsync();
    }

    private static MonitoringService Monitoring(PEnv env, IClinicalSafetyService safety, ILogger<MonitoringService> log, IAuditWriter? audit = null, Action<MonitoringOptions>? tune = null)
    {
        var o = new MonitoringOptions { MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120, CheckIntervalMinutes = 60, BatchSize = 10 };
        tune?.Invoke(o);
        return new MonitoringService(env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), env.Clock, audit ?? env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), safety, new AllowAll(), Options.Create(o), log);
    }

    [Fact]
    public async Task A_clean_monitoring_pass_logs_an_information_summary_with_counts_only()
    {
        using var env = new PEnv();
        await SeedJobs(env, 2);
        var log = new CapturingLogger<MonitoringService>();
        var r = await Monitoring(env, new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Unchanged())), log).RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, r.Unchanged);
        var (level, message) = log.Summary();
        Assert.Equal(LogLevel.Information, level);
        Assert.Equal("Monitoring pass completed: recovered=0 claimed=2 reassessed=0 unchanged=2 skipped=0 failed=0 exhausted=0 discarded=0", message);
        AssertNoIdentifiers(log.Lines.Select(l => l.Message));
    }

    [Fact]
    public async Task A_monitoring_pass_with_failed_or_discarded_jobs_is_a_warning_with_those_counts()
    {
        using var env = new PEnv();
        await SeedJobs(env, 2);
        var log = new CapturingLogger<MonitoringService>();
        var svc = Monitoring(env, new ScriptedSafety(env.Safety, (n, _) => Task.FromResult(n == 1 ? SafetyResult.Fail<ReassessmentOutcome>(SafetyError.Unavailable, "assessment.unavailable") : Unchanged())), log, tune: o => o.MaxConcurrency = 1);
        var r = await svc.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, r.Failed);
        var (level, message) = log.Summary();
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("completed_with_errors", message, StringComparison.Ordinal);
        Assert.Contains("failed=1", message, StringComparison.Ordinal);
        Assert.Contains("unchanged=1", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_monitoring_pass_that_stops_part_way_keeps_the_failure_and_success_counts_and_logs_an_error()
    {
        using var env = new PEnv();
        await SeedJobs(env, 2);
        var log = new CapturingLogger<MonitoringService>();
        var svc = Monitoring(env, new ScriptedSafety(env.Safety, (_, _) => Task.FromResult(Unchanged())), log, new ThrowOnceOnReassessed(env.Get<IAuditWriter>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RunOnceAsync(CancellationToken.None));
        var (level, message) = log.Summary();
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("failed part-way (InvalidOperationException)", message, StringComparison.Ordinal);
        Assert.Contains("claimed=2", message, StringComparison.Ordinal);
        Assert.Contains("failed=1", message, StringComparison.Ordinal);   // the job whose processing threw is counted, not lost
        Assert.Contains("unchanged=1", message, StringComparison.Ordinal); // the job that finished is counted too
        Assert.All(log.Lines, l => Assert.DoesNotContain("audit-detail", l.Message, StringComparison.Ordinal));
    }

    // ---------- B: a poisoned row does not stop the pass ----------

    [Fact]
    public async Task One_poisoned_row_is_counted_and_the_five_healthy_rows_behind_it_are_delivered_in_the_same_pass()
    {
        var poison = new PoisonRow();
        var h = new Harness(poison);
        var bad = h.Add(TimeSpan.FromMinutes(-10)); // the oldest due row comes first
        poison.Ids[bad.Delivery] = true;
        var healthy = Enumerable.Range(0, 5).Select(i => h.Add(TimeSpan.FromSeconds(-30 + i))).ToList();
        var channel = new RecordingChannel();

        var r = await h.Service(channel).RunDueAsync(20, CancellationToken.None);

        Assert.Equal(6, r.Considered);
        Assert.Equal(1, r.Errors);
        Assert.Equal(5, r.Confirmed);
        Assert.Equal(0, r.Failed);
        Assert.Equal(5, channel.Requests.Count);
        Assert.All(healthy, x => Assert.Equal(DeliveryState.Confirmed, h.Row(x.Delivery).State));
        var row = h.Row(bad.Delivery);
        Assert.Equal(DeliveryState.Scheduled, row.State); // retryable: neither delivered nor failed
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.ErrorCode);
        Assert.Equal(1, poison.Rejected); // attempted once in this pass: no tight loop
        Assert.Equal(5, h.Audit.Events.Count(e => e.Action == AuditActions.GuidanceDeliveryConfirmed));
        Assert.DoesNotContain(h.Audit.Events, e => e.Action is AuditActions.GuidanceDeliveryFailed or AuditActions.GuidanceDeliverySkipped);
        Assert.Equal(LogLevel.Warning, h.Log.Summary().Level);
    }

    [Fact]
    public async Task A_persistently_poisoned_row_costs_one_rejected_write_per_pass_and_never_blocks_or_duplicates_the_others()
    {
        var poison = new PoisonRow();
        var h = new Harness(poison);
        var bad = h.Add(TimeSpan.FromMinutes(-10));
        poison.Ids[bad.Delivery] = true;
        var svc = h.Service(new RecordingChannel());
        var channel = new RecordingChannel();
        svc = h.Service(channel);
        for (var pass = 1; pass <= 3; pass++)
        {
            h.Add(TimeSpan.FromSeconds(-1)); // a fresh healthy delivery before every pass
            var r = await svc.RunDueAsync(20, CancellationToken.None);
            Assert.Equal(1, r.Errors);
            Assert.Equal(1, r.Confirmed);
            Assert.Equal(pass, poison.Rejected);
        }

        Assert.Equal(3, channel.Requests.Count);
        Assert.Equal(3, channel.Requests.Select(x => x.IdempotencyKey).Distinct().Count());
        Assert.Equal(3, h.Audit.Events.Count(e => e.Action == AuditActions.GuidanceDeliveryConfirmed)); // no duplicate audit
        Assert.Equal(DeliveryState.Scheduled, h.Row(bad.Delivery).State);
    }

    [Fact]
    public async Task A_rejected_write_that_would_end_or_defer_a_delivery_is_an_error_not_an_abort()
    {
        var poison = new PoisonRow();
        var h = new Harness(poison);
        var resolved = h.Add(TimeSpan.FromMinutes(-9), status: GuidanceStatus.Resolved);
        var inactive = h.Add(TimeSpan.FromMinutes(-8));
        h.Patients.Status[inactive.Patient] = PatientStatus.Inactive;
        poison.Ids[resolved.Delivery] = true;
        poison.Ids[inactive.Delivery] = true;
        var healthy = h.Add();
        var r = await h.Service(new RecordingChannel()).RunDueAsync(20, CancellationToken.None);
        Assert.Equal(2, r.Errors);
        Assert.Equal(1, r.Confirmed);
        Assert.Equal(0, r.Failed + r.Skipped); // nothing is reported as ended or deferred when it was not stored
        Assert.Equal(DeliveryState.Scheduled, h.Row(resolved.Delivery).State);
        Assert.Null(h.Row(resolved.Delivery).ErrorCode);
        Assert.Null(h.Row(inactive.Delivery).ErrorCode);
        Assert.Equal(DeliveryState.Confirmed, h.Row(healthy.Delivery).State);
        Assert.DoesNotContain(h.Audit.Events, e => e.Action is AuditActions.GuidanceDeliverySkipped or AuditActions.GuidanceDeliveryFailed);
    }

    [Fact]
    public async Task A_failing_patient_lookup_for_one_row_is_an_error_for_that_row_only_and_its_message_is_not_logged()
    {
        var h = new Harness();
        var bad = h.Add(TimeSpan.FromMinutes(-5));
        h.Patients.Throwing[bad.Patient] = true;
        h.Add();
        h.Add();
        var channel = new RecordingChannel();
        var r = await h.Service(channel).RunDueAsync(20, CancellationToken.None);
        Assert.Equal(1, r.Errors);
        Assert.Equal(2, r.Confirmed);
        Assert.Equal(2, channel.Requests.Count);
        Assert.Equal(DeliveryState.Scheduled, h.Row(bad.Delivery).State);
        Assert.Contains(h.Log.Lines, l => l.Message.Contains("patient-lookup", StringComparison.Ordinal) && l.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.All(h.Log.Lines, l => Assert.DoesNotContain("lookup-detail", l.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_concurrency_conflict_on_the_claim_is_still_quiet_and_not_counted_as_an_error()
    {
        var h = new Harness(new ConflictOnNth(2)); // save 1 = the (empty) recovery step, save 2 = the claim
        h.Add();
        var channel = new RecordingChannel();
        var r = await h.Service(channel).RunDueAsync(20, CancellationToken.None);
        Assert.Equal(0, r.Errors);
        Assert.Empty(channel.Requests);
        Assert.Equal(LogLevel.Information, h.Log.Summary().Level);
    }

    private sealed class ConflictOnNth(int n) : SaveChangesInterceptor
    {
        private int _count;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _count) == n ? throw new DbUpdateConcurrencyException("another instance changed the row") : ValueTask.FromResult(result);
    }

    // ---------- C: bounded, progressive deferral ----------

    [Fact]
    public void The_deferral_interval_starts_at_the_backoff_grows_with_the_time_waiting_and_never_exceeds_24_hours()
    {
        var o = new GuidanceDeliveryOptions { BackoffSeconds = 60 };
        var t0 = new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
        TimeSpan At(TimeSpan waiting) => GuidanceDeliveryService.DeferralDelay(o, t0, t0 + waiting);

        Assert.Equal(TimeSpan.FromSeconds(60), At(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(60), At(TimeSpan.FromSeconds(100)));
        Assert.Equal(TimeSpan.FromHours(2), At(TimeSpan.FromHours(4)));
        Assert.Equal(TimeSpan.FromHours(24), At(TimeSpan.FromHours(48)));
        Assert.Equal(TimeSpan.FromHours(24), At(TimeSpan.FromDays(365)));
        Assert.Equal(TimeSpan.FromSeconds(60), GuidanceDeliveryService.DeferralDelay(o, t0, t0 - TimeSpan.FromHours(1))); // a clock step backwards cannot shorten it below the floor

        var previous = TimeSpan.Zero;
        for (var hours = 0; hours <= 24 * 90; hours += 3)
        {
            var d = At(TimeSpan.FromHours(hours));
            Assert.True(d >= previous, "never shrinks while waiting");
            Assert.True(d <= TimeSpan.FromHours(24), "hard maximum");
            previous = d;
        }
    }

    [Theory]
    [InlineData(int.MaxValue, 86400)]
    [InlineData(86401, 86400)]
    [InlineData(3600, 3600)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void A_configured_ceiling_can_lower_the_maximum_but_never_raise_it_above_24_hours(int configured, int expectedSeconds)
    {
        var o = new GuidanceDeliveryOptions { BackoffSeconds = 1, MaxDeferralSeconds = configured };
        var t0 = DateTimeOffset.UnixEpoch;
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), GuidanceDeliveryService.DeferralDelay(o, t0, t0 + TimeSpan.FromDays(3650)));
        Assert.Equal(TimeSpan.FromHours(24), GuidanceDeliveryService.DeferralDelay(new GuidanceDeliveryOptions { BackoffSeconds = int.MaxValue, MaxDeferralSeconds = int.MaxValue }, t0, t0)); // a huge backoff cannot exceed 24 h either
    }

    [Fact]
    public async Task A_deferred_delivery_is_checked_ever_less_often_up_to_24_hours_stays_scheduled_and_is_delivered_after_reactivation()
    {
        var h = new Harness();
        var x = h.Add();
        h.Patients.Status[x.Patient] = PatientStatus.Inactive;
        var channel = new RecordingChannel();
        var svc = h.Service(channel);

        var gaps = new List<TimeSpan>();
        var passes = 0;
        var end = h.Clock.UtcNow + TimeSpan.FromDays(30);
        while (h.Clock.UtcNow < end)
        {
            var before = h.Clock.UtcNow;
            var r = await svc.RunDueAsync(20, CancellationToken.None);
            passes++;
            Assert.Equal(1, r.Deferred);
            var row = h.Row(x.Delivery);
            Assert.Equal(DeliveryState.Scheduled, row.State); // never ended because the patient is inactive
            Assert.Equal(0, row.Attempts);
            Assert.Equal(GuidanceDeliveryService.InactivePatientCode, row.ErrorCode);
            var gap = row.NextAttemptAt - before;
            Assert.InRange(gap, TimeSpan.FromSeconds(60), TimeSpan.FromHours(24));
            gaps.Add(gap);
            h.Clock.UtcNow = row.NextAttemptAt;
        }

        Assert.True(gaps.SequenceEqual(gaps.Order()), "intervals never shrink");
        Assert.Equal(TimeSpan.FromHours(24), gaps[^1]);
        Assert.True(passes < 60, $"30 days cost {passes} checks"); // bounded work: not one check a minute
        Assert.Empty(channel.Requests);
        Assert.Single(h.Audit.Events, e => e.Action == AuditActions.GuidanceDeliverySkipped); // audited once

        h.Patients.Status[x.Patient] = PatientStatus.Active; // reactivated: the next check (at most 24 h away) delivers it
        h.Clock.UtcNow = h.Row(x.Delivery).NextAttemptAt;
        var final = await svc.RunDueAsync(20, CancellationToken.None);
        Assert.Equal(1, final.Confirmed);
        Assert.Equal(DeliveryState.Confirmed, h.Row(x.Delivery).State);
    }

    [Fact]
    public async Task A_deferred_row_is_not_examined_again_in_the_same_or_the_next_pass_until_it_is_due()
    {
        var h = new Harness();
        var x = h.Add();
        h.Patients.Status[x.Patient] = PatientStatus.Inactive;
        var svc = h.Service(new RecordingChannel());
        Assert.Equal(1, (await svc.RunDueAsync(20, CancellationToken.None)).Considered);
        Assert.Equal(0, (await svc.RunDueAsync(20, CancellationToken.None)).Considered); // same instant: not due
        h.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(0, (await svc.RunDueAsync(20, CancellationToken.None)).Considered);
    }

    // ---------- C: fair scheduling ----------

    [Fact]
    public void The_scheduling_rule_reserves_a_quarter_of_the_batch_for_deferred_rows_and_none_for_a_batch_of_one()
    {
        Assert.Equal(0, GuidanceDeliveryService.DeferredReserve(1));
        Assert.Equal(1, GuidanceDeliveryService.DeferredReserve(2));
        Assert.Equal(1, GuidanceDeliveryService.DeferredReserve(3));
        Assert.Equal(5, GuidanceDeliveryService.DeferredReserve(20));
        Assert.Equal(25, GuidanceDeliveryService.DeferredReserve(100));
    }

    private static void AddDeferred(Harness h, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var d = h.Add(TimeSpan.FromHours(-1) - TimeSpan.FromSeconds(i), GuidanceDeliveryService.InactivePatientCode, scheduledAgo: TimeSpan.FromHours(2)); // older than every fresh row
            h.Patients.Status[d.Patient] = PatientStatus.Inactive;
        }
    }

    [Fact]
    public async Task With_100_overdue_inactive_deliveries_all_five_fresh_deliveries_are_attempted_in_the_first_pass()
    {
        var h = new Harness();
        AddDeferred(h, 100);
        var fresh = Enumerable.Range(0, 5).Select(i => h.Add(TimeSpan.FromSeconds(-i))).ToList();
        var channel = new RecordingChannel();
        var r = await h.Service(channel, o => o.BatchSize = 20).RunDueAsync(20, CancellationToken.None);

        Assert.Equal(20, r.Considered);          // bounded: one batch, not the whole backlog
        Assert.Equal(5, r.Confirmed);
        Assert.Equal(15, r.Deferred);            // the 5 reserved slots plus the 10 that the fresh rows did not need
        Assert.All(fresh, f => Assert.Equal(DeliveryState.Confirmed, h.Row(f.Delivery).State));
        Assert.Equal(5, channel.Requests.Count);
        Assert.DoesNotContain(h.Audit.Events, e => e.Action == AuditActions.GuidanceDeliverySkipped); // these were already audited when they were first deferred: no repeat per pass
    }

    [Fact]
    public async Task With_a_full_batch_of_fresh_rows_and_a_deferred_backlog_the_deferred_rows_still_get_their_reserved_slots()
    {
        var h = new Harness();
        AddDeferred(h, 100);
        var fresh = Enumerable.Range(0, 40).Select(i => h.Add(TimeSpan.FromSeconds(-i))).ToList();
        var r = await h.Service(new RecordingChannel()).RunDueAsync(20, CancellationToken.None);
        Assert.Equal(20, r.Considered);
        Assert.Equal(15, r.Confirmed);  // fresh rows get batch - reserve
        Assert.Equal(5, r.Deferred);    // deferred rows are not forgotten
        // the oldest fresh rows go first (the later ones in the list are the older)
        var confirmed = fresh.Where(f => h.Row(f.Delivery).State == DeliveryState.Confirmed).ToList();
        Assert.Equal(15, confirmed.Count);
        Assert.All(fresh.TakeLast(15), f => Assert.Equal(DeliveryState.Confirmed, h.Row(f.Delivery).State));
    }

    [Fact]
    public async Task Without_fresh_rows_the_whole_batch_goes_to_deferred_rows_and_per_pass_work_stays_bounded()
    {
        var h = new Harness();
        AddDeferred(h, 100);
        var svc = h.Service(new RecordingChannel());
        var r = await svc.RunDueAsync(20, CancellationToken.None);
        Assert.Equal(20, r.Considered);
        Assert.Equal(20, r.Deferred);
        var again = await svc.RunDueAsync(20, CancellationToken.None); // the 20 just deferred are not due: the next 20 are examined
        Assert.Equal(20, again.Considered);
    }

    // ---------- F: stale error codes ----------

    [Fact]
    public async Task Inactive_then_active_then_retry_clears_the_inactive_code_when_the_attempt_starts_and_the_delivery_confirms()
    {
        var h = new Harness();
        var x = h.Add();
        h.Patients.Status[x.Patient] = PatientStatus.Inactive;
        var observed = new List<(DeliveryState State, string? Code)>();
        var channel = new RecordingChannel { OnCall = _ => { var r = h.Row(x.Delivery); observed.Add((r.State, r.ErrorCode)); return Task.CompletedTask; } };
        var svc = h.Service(channel);

        await svc.RunDueAsync(20, CancellationToken.None);
        Assert.Equal(GuidanceDeliveryService.InactivePatientCode, h.Row(x.Delivery).ErrorCode);

        h.Patients.Status[x.Patient] = PatientStatus.Active;
        h.Clock.UtcNow = h.Row(x.Delivery).NextAttemptAt;
        await svc.RunDueAsync(20, CancellationToken.None);

        var seenDuringCall = Assert.Single(observed);
        Assert.Equal(DeliveryState.Attempted, seenDuringCall.State);
        Assert.Null(seenDuringCall.Code); // cleared when the attempt started, before the channel answered
        var final = h.Row(x.Delivery);
        Assert.Equal(DeliveryState.Confirmed, final.State);
        Assert.Null(final.ErrorCode);
    }

    [Fact]
    public async Task After_reactivation_a_failing_attempt_shows_the_new_reason_never_the_old_inactive_one()
    {
        var h = new Harness();
        var x = h.Add(errorCode: GuidanceDeliveryService.InactivePatientCode);
        var channel = new FailingChannel();
        await h.Service(channel).RunDueAsync(20, CancellationToken.None);
        var row = h.Row(x.Delivery);
        Assert.Equal(DeliveryState.Scheduled, row.State);
        Assert.Equal("channel.transient", row.ErrorCode);
    }

    private sealed class FailingChannel : IGuidanceDeliveryChannel
    {
        public string Name => LocalSimulatedChannel.ChannelName;
        public DeliveryMode Mode => DeliveryMode.Simulated;
        public Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct) => Task.FromResult(ChannelOutcome.TransientFailure);
    }

    [Fact]
    public async Task A_meaningful_error_code_is_kept_until_the_attempt_has_an_outcome()
    {
        var h = new Harness();
        var x = h.Add(errorCode: "interrupted"); // e.g. a recovered interrupted attempt
        string? during = null;
        var channel = new RecordingChannel { OnCall = _ => { during = h.Row(x.Delivery).ErrorCode; return Task.CompletedTask; } };
        await h.Service(channel).RunDueAsync(20, CancellationToken.None);
        Assert.Equal("interrupted", during);
        Assert.Null(h.Row(x.Delivery).ErrorCode); // cleared by the confirmed outcome
    }

    // ---------- G: guard for the open-finding index predicate ----------

    private const string ExpectedFilter = "origin_finding_key IS NOT NULL AND status <> 'Resolved'";

    private static GuidanceDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<GuidanceDbContext>().UseNpgsql("Host=127.0.0.1;Database=model_only_never_opened;Username=x;Password=x").Options); // the model is read; no connection is opened

    private static void AssertIsTheOpenFindingIndex(IReadOnlyIndex index)
    {
        Assert.True(index.IsUnique);
        Assert.Equal(["PatientId", "OriginFindingKey"], index.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(ExpectedFilter, index.GetFilter());
    }

    [Fact]
    public void The_open_finding_index_in_the_model_is_unique_and_excludes_resolved_findings()
    {
        using var db = ModelOnlyContext();
        var entity = db.Model.FindEntityType(typeof(GuidanceMessageRow))!;
        AssertIsTheOpenFindingIndex(entity.GetIndexes().Single(i => i.Name == GuidanceDbContext.OpenFindingIndex));
        var status = entity.FindProperty(nameof(GuidanceMessageRow.Status))!;
        Assert.Equal(typeof(string), status.GetProviderClrType()); // status is stored by name, which is why the predicate compares text
        Assert.Contains($"'{GuidanceStatus.Resolved}'", ExpectedFilter, StringComparison.Ordinal); // the text in the predicate is the enum member's name
        Assert.Equal("Resolved", nameof(GuidanceStatus.Resolved));
        Assert.Single(entity.GetIndexes(), i => !i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["PatientId", "OriginFindingKey"])); // the plain lookup index stays
    }

    [Fact]
    public void The_model_snapshot_and_the_migration_carry_the_same_predicate()
    {
        using var db = ModelOnlyContext();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var entity = snapshot.GetEntityTypes().Single(e => e.ClrType == typeof(GuidanceMessageRow) || e.Name.EndsWith(nameof(GuidanceMessageRow), StringComparison.Ordinal));
        AssertIsTheOpenFindingIndex(entity.GetIndexes().Single(i => i.Name == GuidanceDbContext.OpenFindingIndex));

        var up = new AddOpenFindingUniqueness().UpOperations.OfType<CreateIndexOperation>().Single();
        Assert.Equal(GuidanceDbContext.OpenFindingIndex, up.Name);
        Assert.True(up.IsUnique);
        Assert.Equal(["patient_id", "origin_finding_key"], up.Columns);
        Assert.Equal(ExpectedFilter, up.Filter);
    }

    // ---------- multi-instance on PostgreSQL ----------

    [PostgresFact]
    public async Task Two_instances_with_a_deferred_backlog_deliver_each_fresh_row_once_and_never_starve_fresh_rows_on_PostgreSQL()
    {
        using var env = new PEnv();
        var factory = env.Get<IDbContextFactory<GuidanceDbContext>>();
        var patients = new Directory();
        var now = env.Clock.UtcNow;
        var fresh = new List<Guid>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 30; i++)
            {
                var (m, d, p) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                db.Messages.Add(new GuidanceMessageRow { Id = m, PatientId = p, TemplateKey = "t", Level = GuidanceLevel.FollowUp, Status = GuidanceStatus.Sent, Locale = "en", Confidence = GuidanceConfidence.Low, CreatedAt = now, IsDemo = true });
                db.Deliveries.Add(new GuidanceDeliveryRow { Id = d, MessageId = m, PatientId = p, Channel = LocalSimulatedChannel.ChannelName, Mode = DeliveryMode.Simulated, State = DeliveryState.Scheduled, ScheduledAt = now, NextAttemptAt = now.AddSeconds(-i) });
                fresh.Add(d);
            }

            for (var i = 0; i < 60; i++)
            {
                var (m, d, p) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                patients.Status[p] = PatientStatus.Inactive;
                db.Messages.Add(new GuidanceMessageRow { Id = m, PatientId = p, TemplateKey = "t", Level = GuidanceLevel.FollowUp, Status = GuidanceStatus.Sent, Locale = "en", Confidence = GuidanceConfidence.Low, CreatedAt = now, IsDemo = true });
                db.Deliveries.Add(new GuidanceDeliveryRow { Id = d, MessageId = m, PatientId = p, Channel = LocalSimulatedChannel.ChannelName, Mode = DeliveryMode.Simulated, State = DeliveryState.Scheduled, ErrorCode = GuidanceDeliveryService.InactivePatientCode, ScheduledAt = now.AddHours(-3), NextAttemptAt = now.AddHours(-2) });
            }

            await db.SaveChangesAsync();
        }

        var channel = new RecordingChannel();
        var audit = new RecordingAudit();
        GuidanceDeliveryService Instance() => new(factory, env.Clock, audit, patients, [channel], Options.Create(new GuidanceDeliveryOptions { Enabled = true, MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120 }));

        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => Instance().RunDueAsync(20, CancellationToken.None))));
        }

        await using var check = await factory.CreateDbContextAsync();
        var states = await check.Deliveries.AsNoTracking().Where(d => fresh.Contains(d.Id)).ToListAsync();
        Assert.All(states, d => Assert.Equal(DeliveryState.Confirmed, d.State)); // every fresh row was delivered despite the backlog
        Assert.Equal(30, channel.Requests.Select(r => r.IdempotencyKey).Distinct().Count());
        Assert.Equal(30, channel.Requests.Count); // and exactly once each: two instances never sent the same row twice
        Assert.Empty(await check.Deliveries.Where(d => d.State == DeliveryState.Attempted).ToListAsync());
        Assert.Equal(30, audit.Events.Count(e => e.Action == AuditActions.GuidanceDeliveryConfirmed));
        Assert.Equal(60, await check.Deliveries.CountAsync(d => d.State == DeliveryState.Scheduled && d.ErrorCode == GuidanceDeliveryService.InactivePatientCode)); // deferred rows remain recoverable
    }
}
