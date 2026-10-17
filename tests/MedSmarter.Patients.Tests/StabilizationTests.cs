using System.Collections.Concurrent;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Patients.Tests;

/// <summary>Post-Phase-15 stabilization: two API instances running the monitoring pass against one database.</summary>
public class MonitoringMultiInstanceTests
{
    private sealed class CountingSafety(int delayMs) : IClinicalSafetyService
    {
        public ConcurrentDictionary<Guid, int> Calls { get; } = new();

        public async Task<SafetyOutcome<ReassessmentOutcome>> ReassessIfNeededAsync(Guid s, IReadOnlyCollection<string> r, string l, TimeSpan? d, string? c, CancellationToken ct = default)
        {
            Calls.AddOrUpdate(s, 1, (_, n) => n + 1);
            await Task.Delay(delayMs, ct);
            return SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t"));
        }

        public Task<SafetyOutcome<AssessmentDto>> AssessAsync(Guid a, Guid s, AssessCommand c, string src, string? corr, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SafetyOutcome<IReadOnlyList<AssessmentSummaryDto>>> ListAsync(Guid s, int t, IReadOnlyCollection<string> r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SafetyOutcome<AssessmentDto>> GetAsync(Guid s, Guid a, IReadOnlyCollection<string> r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SafetyOutcome<AssessmentDto>> LatestAsync(Guid s, IReadOnlyCollection<string> r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class AllowAll : IPurposeConsentEvaluator
    {
        public Task<ConsentDecision> EvaluateAsync(PurposeConsentCheck check, CancellationToken ct = default) => Task.FromResult(new ConsentDecision(true, Guid.NewGuid(), "test"));
    }

    private static MonitoringService Instance(PEnv env, IClinicalSafetyService safety, int lease = 120) =>
        new(env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), safety, new AllowAll(),
            Options.Create(new MonitoringOptions { BatchSize = 50, MaxConcurrency = 4, MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = lease, CheckIntervalMinutes = 60 }));

    private static async Task<List<Guid>> SeedDue(PEnv env, int n)
    {
        await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        var subjects = Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToList();
        foreach (var s in subjects)
        {
            db.MonitoringJobs.Add(new MonitoringJobRow { PatientId = Guid.NewGuid(), SubjectId = s, State = MonitoringJobState.Idle, EnrolledAt = env.Clock.UtcNow.AddDays(-1), NextCheckAt = env.Clock.UtcNow.AddMinutes(-1) });
        }

        await db.SaveChangesAsync();
        return subjects;
    }

    [PostgresFact]
    public async Task Two_instances_running_at_the_same_moment_process_every_due_job_exactly_once()
    {
        using var env = new PEnv();
        var subjects = await SeedDue(env, 24);
        var safety = new CountingSafety(delayMs: 60);
        var a = Instance(env, safety);
        var b = Instance(env, safety);

        var results = await Task.WhenAll(a.RunOnceAsync(CancellationToken.None), b.RunOnceAsync(CancellationToken.None));

        Assert.Equal(24, results.Sum(r => r.Claimed)); // every job claimed once across both instances
        Assert.Equal(subjects.Count, safety.Calls.Count);
        Assert.All(subjects, s => Assert.Equal(1, safety.Calls[s])); // and checked once
        await using var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        Assert.All(await db.MonitoringJobs.AsNoTracking().ToListAsync(), j => Assert.Equal(("unchanged", MonitoringJobState.Idle), (j.LastOutcome, j.State)));
    }

    [PostgresFact]
    public async Task A_pass_whose_lease_expired_and_was_taken_over_cannot_overwrite_the_new_owners_result()
    {
        using var env = new PEnv();
        await SeedDue(env, 1);
        var gate = new TaskCompletionSource();
        var slowSafety = new GatedSafety(gate.Task);
        var slow = Instance(env, slowSafety, lease: 60);
        var first = slow.RunOnceAsync(CancellationToken.None); // claims, then waits inside the check
        await Task.Delay(400);

        env.Clock.Advance(TimeSpan.FromMinutes(5)); // its lease runs out; another instance recovers the job and claims it again
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        var fast = Instance(env, new CountingSafety(0));
        var second = await fast.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, second.Recovered);

        await using (var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync())
        {
            var row = await db.MonitoringJobs.AsNoTracking().SingleAsync();
            Assert.Equal("interrupted", row.LastOutcome); // the recovery stands
        }

        env.Clock.Advance(TimeSpan.FromHours(2));
        var third = await Instance(env, new CountingSafety(0)).RunOnceAsync(CancellationToken.None); // claims it again (new owner)
        Assert.Equal(1, third.Claimed);

        // Now the stale first pass finishes. It no longer owns the row and must change nothing.
        await using (var db = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync())
        {
            var row = await db.MonitoringJobs.SingleAsync();
            row.State = MonitoringJobState.Running; // the third pass's claim is still in flight
            row.LeaseUntil = env.Clock.UtcNow.AddMinutes(5);
            row.RowVersion++;
            row.LastOutcome = "claimed-by-new-owner";
            await db.SaveChangesAsync();
        }

        gate.SetResult();
        await first;
        await using var check = await env.Get<IDbContextFactory<ClinicalRulesDbContext>>().CreateDbContextAsync();
        var final = await check.MonitoringJobs.AsNoTracking().SingleAsync();
        Assert.Equal("claimed-by-new-owner", final.LastOutcome);
        Assert.Equal(MonitoringJobState.Running, final.State);
    }

    private sealed class GatedSafety(Task gate) : IClinicalSafetyService
    {
        public async Task<SafetyOutcome<ReassessmentOutcome>> ReassessIfNeededAsync(Guid s, IReadOnlyCollection<string> r, string l, TimeSpan? d, string? c, CancellationToken ct = default)
        {
            await gate.WaitAsync(ct);
            return SafetyResult.Ok(new ReassessmentOutcome(ReassessmentKind.Unchanged, null, null, "t"));
        }

        public Task<SafetyOutcome<AssessmentDto>> AssessAsync(Guid a, Guid s, AssessCommand c, string src, string? corr, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SafetyOutcome<IReadOnlyList<AssessmentSummaryDto>>> ListAsync(Guid s, int t, IReadOnlyCollection<string> r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SafetyOutcome<AssessmentDto>> GetAsync(Guid s, Guid a, IReadOnlyCollection<string> r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SafetyOutcome<AssessmentDto>> LatestAsync(Guid s, IReadOnlyCollection<string> r, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
