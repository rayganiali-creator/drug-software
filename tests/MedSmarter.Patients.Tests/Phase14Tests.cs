using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Guidance;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 14: notification reliability across more than one instance. Concurrency tokens are enforced by PostgreSQL, not by the in-memory provider, so the race test is PostgreSQL-only.</summary>
public class Phase14Tests
{
    private sealed class SlowChannel : IGuidanceDeliveryChannel
    {
        public int Calls;
        public string Name => LocalSimulatedChannel.ChannelName;
        public DeliveryMode Mode => DeliveryMode.Simulated;

        public async Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(250, ct);
            return ChannelOutcome.Confirmed;
        }
    }

    private static GuidanceDeliveryService Instance(PEnv env, IGuidanceDeliveryChannel channel) =>
        new(env.Get<IDbContextFactory<GuidanceDbContext>>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), [channel], Options.Create(new GuidanceDeliveryOptions { Enabled = true, MaxAttempts = 3, BackoffSeconds = 60, LeaseSeconds = 120 }));

    private static async Task<Guid> ScheduledDelivery(PEnv env)
    {
        var id = await env.Patient("demo-patient");
        var msg = (await env.Guidance.CreateAsync(id, new GuidanceRequest("adherence.skipped_pattern", GuidanceLevel.FollowUp, "en", new Dictionary<string, string> { ["medication"] = "Demopril", ["count"] = "3", ["days"] = "7" }, GuidanceConfidence.Low, "DEMO", true), true, "t", null)).Value!;
        await using var db = await env.Get<IDbContextFactory<GuidanceDbContext>>().CreateDbContextAsync();
        var patientId = (await db.Messages.AsNoTracking().FirstAsync(m => m.Id == msg.Id)).PatientId;
        var d = new GuidanceDeliveryRow { Id = Guid.NewGuid(), MessageId = msg.Id, PatientId = patientId, Channel = LocalSimulatedChannel.ChannelName, Mode = DeliveryMode.Simulated, State = DeliveryState.Scheduled, ScheduledAt = env.Clock.UtcNow, NextAttemptAt = env.Clock.UtcNow };
        db.Deliveries.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    [PostgresFact]
    public async Task Two_instances_running_at_the_same_time_attempt_one_due_delivery_exactly_once()
    {
        using var env = new PEnv();
        var deliveryId = await ScheduledDelivery(env);
        var channel = new SlowChannel();
        var a = Instance(env, channel);
        var b = Instance(env, channel);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => (i == 0 ? a : b).RunDueAsync(10, CancellationToken.None)));
        Assert.Equal(1, channel.Calls); // never delivered twice
        Assert.Equal(1, results.Sum(r => r.Confirmed));
        await using var db = await env.Get<IDbContextFactory<GuidanceDbContext>>().CreateDbContextAsync();
        var row = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == deliveryId);
        Assert.Equal(DeliveryState.Confirmed, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(DeliveryMode.Simulated, row.Mode); // and it stays labelled simulated
    }
}

/// <summary>Phase 14: replaying a queued offline intake never overwrites a newer server record silently.</summary>
public class Phase14SyncTests
{
    private static async Task<(Guid Id, PatientMedicationDto Med, ScheduleEntryDto Entry, DoseSlotDto Slot)> Setup(PEnv env)
    {
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        var entry = (await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null)).Value!;
        env.Clock.Advance(TimeSpan.FromHours(6));
        return (id, med, entry, (await env.Meds.GetDayAsync(id, env.Today)).Value!.Single());
    }

    [Fact]
    public async Task A_queued_report_older_than_a_different_server_record_is_a_conflict_and_changes_nothing()
    {
        using var env = new PEnv();
        var (id, med, entry, slot) = await Setup(env);
        var queuedAt = env.Clock.UtcNow;
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        var server = (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Skipped, null, null), "web", null)).Value!;

        var replay = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Taken, null, null, queuedAt), "android", null);
        Assert.Equal(PatientError.Conflict, replay.Error);
        Assert.Equal("intake.newer_record_exists", replay.Detail);
        var stored = Assert.Single((await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow.AddDays(-1), env.Clock.UtcNow)).Value!);
        Assert.Equal(IntakeStatus.Skipped, stored.Status);
        Assert.Equal(server.RecordedAt, stored.RecordedAt);
    }

    [Fact]
    public async Task A_replay_of_the_same_queued_report_is_idempotent_and_a_newer_client_report_is_applied()
    {
        using var env = new PEnv();
        var (id, med, entry, slot) = await Setup(env);
        var queuedAt = env.Clock.UtcNow;
        var cmd = new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Taken, null, null, queuedAt);
        env.Clock.Advance(TimeSpan.FromMinutes(5));
        var first = (await env.Meds.LogIntakeAsync(id, id, cmd, "android", null)).Value!;
        env.Clock.Advance(TimeSpan.FromMinutes(5));
        var again = await env.Meds.LogIntakeAsync(id, id, cmd, "android", null); // lost response, queue retries
        Assert.True(again.Succeeded);
        Assert.Equal(first.RecordedAt, again.Value!.RecordedAt);

        // a genuinely newer client decision (made after the stored record) is accepted as a versioned correction
        var later = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Skipped, null, null, env.Clock.UtcNow), "android", null);
        Assert.True(later.Succeeded);
        Assert.Equal(IntakeStatus.Skipped, later.Value!.Status);
    }
}
