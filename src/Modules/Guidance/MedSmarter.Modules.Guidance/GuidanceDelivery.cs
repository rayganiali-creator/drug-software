using System.Globalization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Guidance;

public sealed class GuidanceDeliveryOptions
{
    public const string Section = "Guidance:Delivery";

    /// <summary>Whether a delivery is scheduled for new engine-raised messages. Off: messages exist only in the app.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the in-process worker runs delivery passes on a timer.</summary>
    public bool WorkerEnabled { get; set; }

    public string Channel { get; set; } = LocalSimulatedChannel.ChannelName;
    public int IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 3;
    public int BackoffSeconds { get; set; } = 60;
    public int LeaseSeconds { get; set; } = 120;
}

/// <summary>
/// The only channel that exists: a local stand-in. It sends nothing anywhere (no push, SMS, e-mail or device notification exists in this system), so its
/// confirmation means "the simulation accepted it" and every screen and audit line says SIMULATED. Real channels are a later, separately approved phase.
/// </summary>
public sealed class LocalSimulatedChannel : IGuidanceDeliveryChannel
{
    public const string ChannelName = "local-simulated";
    public string Name => ChannelName;
    public DeliveryMode Mode => DeliveryMode.Simulated;
    public Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct) => Task.FromResult(ChannelOutcome.Confirmed);
}

/// <summary>
/// Delivery passes. State machine per (message, channel): Scheduled -> Attempted -> Confirmed | Scheduled (retry with backoff) | Failed.
/// "Attempted" is written BEFORE the channel is called, so a crash leaves a truthful "attempted, outcome unknown"; recovery never marks it confirmed:
/// an interrupted attempt goes back to Scheduled (counted) and is attempted again, up to MaxAttempts, after which it is Failed. A channel only ever
/// receives ids (message, patient, level, locale).
/// </summary>
public sealed class GuidanceDeliveryService(IDbContextFactory<GuidanceDbContext> factory, IClock clock, IAuditWriter audit, IPatientDirectory patients, IEnumerable<IGuidanceDeliveryChannel> channels, IOptions<GuidanceDeliveryOptions> options) : IGuidanceDeliveryService
{
    public bool Enabled => options.Value.Enabled;

    public async Task<DeliveryRunResult> RunDueAsync(int batchSize, CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.UtcNow;
        var take = Math.Clamp(batchSize, 1, 100);
        int considered = 0, confirmed = 0, retrying = 0, failed = 0, recovered = 0, skipped = 0, unrecorded = 0;

        // 1) recovery: an attempt whose lease ran out has an unknown outcome. It is never promoted to Confirmed.
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var stuck = await db.Deliveries.Where(d => d.State == DeliveryState.Attempted && d.LeaseUntil != null && d.LeaseUntil < now).OrderBy(d => d.LastAttemptAt).Take(take).ToListAsync(ct);
            foreach (var d in stuck)
            {
                recovered++;
                if (d.Attempts >= o.MaxAttempts)
                {
                    d.State = DeliveryState.Failed;
                    d.FailedAt = now;
                    d.ErrorCode = "interrupted";
                }
                else
                {
                    d.State = DeliveryState.Scheduled;
                    d.NextAttemptAt = now;
                    d.ErrorCode = "interrupted";
                }

                d.LeaseUntil = null;
                d.Version++;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                stuck.Clear(); // another instance recovered them first; nothing more to do here
                recovered = 0;
            }

            foreach (var d in stuck)
            {
                await AuditAsync(AuditActions.GuidanceDeliveryRecovered, AuditResult.Success, d, d.State.ToString(), ct);
            }
        }

        // 2) one bounded pass over due deliveries
        List<GuidanceDeliveryRow> due;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            due = await db.Deliveries.AsNoTracking().Where(d => d.State == DeliveryState.Scheduled && d.NextAttemptAt <= now).OrderBy(d => d.NextAttemptAt).Take(take).ToListAsync(ct);
        }

        foreach (var snapshot in due)
        {
            ct.ThrowIfCancellationRequested();
            considered++;
            var channel = channels.FirstOrDefault(c => c.Name == snapshot.Channel);
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await db.Deliveries.FirstAsync(d => d.Id == snapshot.Id, ct);
            if (row.State != DeliveryState.Scheduled)
            {
                continue; // another pass got it first
            }

            var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == row.MessageId, ct);
            if (channel is null || message is null)
            {
                row.State = DeliveryState.Failed;
                row.FailedAt = now;
                row.ErrorCode = channel is null ? "channel.unavailable" : "message.missing";
                row.Version++;
                if (!await TrySaveAsync(db, ct))
                {
                    continue;
                }

                failed++;
                await AuditAsync(AuditActions.GuidanceDeliveryFailed, AuditResult.Failure, row, row.ErrorCode, ct);
                continue;
            }

            // Send-time eligibility, from facts the system already holds (not a consent rule: no notification consent exists, and none is invented here).
            // Message resolved: it can never be reopened (no allowed move leaves Resolved), so the delivery ends. Patient record missing: ends too.
            // Patient inactive: this can be undone (status moves both ways), so the delivery is deferred, not ended; it is neither attempted nor counted as an attempt.
            var ineligible = message.Status == GuidanceStatus.Resolved ? "message.resolved" : null;
            PatientRecordRef? patient = null;
            if (ineligible is null)
            {
                patient = await patients.FindByPatientIdAsync(row.PatientId, ct);
                ineligible = patient is null ? "patient.missing" : patient.Status != PatientStatus.Active ? "patient.inactive" : null;
            }

            if (ineligible is not null)
            {
                var deferral = ineligible == "patient.inactive";
                var firstTime = row.ErrorCode != ineligible; // one audit line per reason, not one per pass
                if (deferral)
                {
                    row.NextAttemptAt = clock.UtcNow.AddSeconds(o.BackoffSeconds);
                }
                else
                {
                    row.State = DeliveryState.Failed;
                    row.FailedAt = clock.UtcNow;
                    failed++;
                }

                row.ErrorCode = ineligible;
                row.Version++;
                if (!await TrySaveAsync(db, ct))
                {
                    if (!deferral)
                    {
                        failed--;
                    }

                    continue;
                }

                skipped++;
                if (firstTime)
                {
                    await audit.WriteAsync(new AuditEvent(AuditActions.GuidanceDeliverySkipped, AuditResult.Success, null, "guidance-delivery", row.MessageId.ToString(), patient?.SubjectId, "worker", null, ineligible,
                        new Dictionary<string, string> { ["channel"] = row.Channel, ["mode"] = row.Mode.ToString(), ["outcome"] = deferral ? "deferred" : "ended", ["sent"] = "false" }), ct);
                }

                continue;
            }

            // Record the attempt first. If the process dies now, the row says "attempted", not "delivered".
            row.State = DeliveryState.Attempted;
            row.Attempts++;
            row.LastAttemptAt = now;
            row.LeaseUntil = now.AddSeconds(o.LeaseSeconds);
            row.Version++;
            if (!await TrySaveAsync(db, ct))
            {
                continue; // another instance claimed it between our read and our write: it is theirs, we do not attempt it
            }

            ChannelOutcome outcome;
            try
            {
                outcome = await channel.DeliverAsync(new DeliveryRequest(message.Id, message.PatientId, message.Level, message.Locale, row.Id.ToString("N", CultureInfo.InvariantCulture)), ct);
            }
            catch (OperationCanceledException)
            {
                // Shutdown during the call: outcome unknown. Leave "Attempted"; the lease expires and recovery handles it.
                throw;
            }
#pragma warning disable CA1031 // a throwing channel is a transient failure, recorded by code only
            catch (Exception)
#pragma warning restore CA1031
            {
                outcome = ChannelOutcome.TransientFailure;
            }

            row.LeaseUntil = null;
            row.Version++;
            switch (outcome)
            {
                case ChannelOutcome.Confirmed:
                    row.State = DeliveryState.Confirmed;
                    row.ConfirmedAt = clock.UtcNow;
                    row.ErrorCode = null;
                    break;
                case ChannelOutcome.TransientFailure when row.Attempts < o.MaxAttempts:
                    row.State = DeliveryState.Scheduled;
                    row.NextAttemptAt = clock.UtcNow.AddSeconds(o.BackoffSeconds * Math.Pow(2, row.Attempts - 1));
                    row.ErrorCode = "channel.transient";
                    break;
                default:
                    row.State = DeliveryState.Failed;
                    row.FailedAt = clock.UtcNow;
                    row.ErrorCode = outcome == ChannelOutcome.PermanentFailure ? "channel.refused" : "channel.transient_exhausted";
                    break;
            }

            if (!await TrySaveOutcomeAsync(db, ct))
            {
                // The channel was called but the outcome could not be stored (the lease ran out and recovery handed the row on, or the save failed). The row stays "attempted";
                // recovery makes it due again and the channel gets the same idempotency key. Nothing is audited as delivered. This is why delivery is at-least-once.
                unrecorded++;
                continue;
            }

            switch (row.State)
            {
                case DeliveryState.Confirmed: confirmed++; break;
                case DeliveryState.Scheduled: retrying++; break;
                default: failed++; break;
            }

            if (row.State == DeliveryState.Confirmed)
            {
                await AuditAsync(AuditActions.GuidanceDeliveryConfirmed, AuditResult.Success, row, channel.Mode == DeliveryMode.Simulated ? "simulated" : "real", ct);
            }
            else if (row.State == DeliveryState.Failed)
            {
                await AuditAsync(AuditActions.GuidanceDeliveryFailed, AuditResult.Failure, row, row.ErrorCode, ct);
            }
        }

        return new DeliveryRunResult(considered, confirmed, retrying, failed, recovered, skipped, unrecorded);
    }

    /// <summary>
    /// Claim, skip and terminal-state writes happen BEFORE the channel is called. Saves, and reports false only when another instance changed the row first (optimistic
    /// concurrency: the row is theirs). Any other database failure is not hidden: it propagates out of the pass (the worker logs it), the row is unchanged, nothing was sent
    /// and nothing was audited.
    /// </summary>
    private static async Task<bool> TrySaveAsync(GuidanceDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>
    /// The write of the channel's outcome happens AFTER the channel was called, so the pass cannot undo the call. Reports false for a concurrency conflict and for a rejected
    /// write alike: the row stays "attempted", the caller counts it as unrecorded, nothing is audited as delivered, and recovery makes it due again (at-least-once).
    /// </summary>
    private static async Task<bool> TrySaveOutcomeAsync(GuidanceDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException) // includes DbUpdateConcurrencyException
        {
            return false;
        }
    }

    private async Task AuditAsync(string action, AuditResult result, GuidanceDeliveryRow row, string? reason, CancellationToken ct)
    {
        var patient = await patients.FindByPatientIdAsync(row.PatientId, ct);
        await audit.WriteAsync(new AuditEvent(action, result, null, "guidance-delivery", row.MessageId.ToString(), patient?.SubjectId, "worker", null, reason,
            new Dictionary<string, string> { ["channel"] = row.Channel, ["mode"] = row.Mode.ToString(), ["attempts"] = row.Attempts.ToString(CultureInfo.InvariantCulture) }), ct);
    }
}
