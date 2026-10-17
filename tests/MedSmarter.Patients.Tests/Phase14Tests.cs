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
