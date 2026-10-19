using System.Globalization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>
    /// Longest wait between two checks of a delivery whose patient is inactive. The wait grows with the time since the delivery was scheduled (half of it, never less than
    /// <see cref="BackoffSeconds"/>) and is capped here; values above <see cref="GuidanceDeliveryService.MaxDeferralCeilingSeconds"/> (24 hours) are treated as 24 hours.
    /// A deferred delivery is never ended because the patient is inactive.
    /// </summary>
    public int MaxDeferralSeconds { get; set; } = GuidanceDeliveryService.MaxDeferralCeilingSeconds;
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
public sealed partial class GuidanceDeliveryService(IDbContextFactory<GuidanceDbContext> factory, IClock clock, IAuditWriter audit, IPatientDirectory patients, IEnumerable<IGuidanceDeliveryChannel> channels, IOptions<GuidanceDeliveryOptions> options, ILogger<GuidanceDeliveryService>? logger = null) : IGuidanceDeliveryService
{
    /// <summary>The longest allowed wait between two checks of a deferred delivery: 24 hours.</summary>
    public const int MaxDeferralCeilingSeconds = 24 * 60 * 60;

    /// <summary>The error code of a delivery that is waiting because its patient is inactive. Such a row is "deferred" for scheduling.</summary>
    public const string InactivePatientCode = "patient.inactive";

    private readonly ILogger log = logger ?? NullLogger<GuidanceDeliveryService>.Instance;

    public bool Enabled => options.Value.Enabled;

    /// <summary>
    /// Wait before the next check of a delivery whose patient is inactive: half of the time since it was scheduled, at least <c>BackoffSeconds</c>, at most
    /// <c>MaxDeferralSeconds</c> and never more than 24 hours. Uses only the delivery's own timestamps, so any instance computes the same value.
    /// </summary>
    public static TimeSpan DeferralDelay(GuidanceDeliveryOptions o, DateTimeOffset scheduledAt, DateTimeOffset now)
    {
        var ceiling = Math.Clamp(o.MaxDeferralSeconds, 1, MaxDeferralCeilingSeconds);
        var floor = Math.Min(Math.Max(1, o.BackoffSeconds), ceiling);
        var elapsed = Math.Max(0, (now - scheduledAt).TotalSeconds);
        return TimeSpan.FromSeconds(Math.Clamp(elapsed / 2, floor, ceiling));
    }

    /// <summary>Slots of one pass reserved for deferred rows (when there are any): a quarter of the batch, at least one, none for a batch of one.</summary>
    public static int DeferredReserve(int take) => take >= 2 ? Math.Max(1, take / 4) : 0;

    /// <summary>
    /// The rows of one pass, in processing order. Fresh/regular rows come first (oldest due first), deferred rows (inactive patient) after them. Deferred rows are guaranteed
    /// <see cref="DeferredReserve"/> slots so they are not forgotten, and can never take more than the batch minus the regular rows that are due, so a backlog of deferred
    /// rows cannot crowd out fresh deliveries. Unused capacity goes to the other group. At most two bounded queries and <paramref name="take"/> rows per pass.
    /// </summary>
    private async Task<List<GuidanceDeliveryRow>> SelectDueAsync(int take, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var dueRows = db.Deliveries.AsNoTracking().Where(d => d.State == DeliveryState.Scheduled && d.NextAttemptAt <= now);
        var regular = await dueRows.Where(d => d.ErrorCode == null || d.ErrorCode != InactivePatientCode).OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Take(take).ToListAsync(ct);
        var deferred = await dueRows.Where(d => d.ErrorCode == InactivePatientCode).OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Take(take).ToListAsync(ct);

        var reserved = Math.Min(DeferredReserve(take), deferred.Count);
        var chosen = new List<GuidanceDeliveryRow>(take);
        chosen.AddRange(regular.Take(take - reserved));
        chosen.AddRange(deferred.Take(reserved));
        chosen.AddRange(deferred.Skip(reserved).Take(take - chosen.Count)); // spare capacity: regular rows were exhausted
        return chosen;
    }

    public async Task<DeliveryRunResult> RunDueAsync(int batchSize, CancellationToken ct)
    {
        var c = new PassCounters();
        try
        {
            await RunPassAsync(batchSize, c, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogPass(log, LogLevel.Information, "cancelled", c.Considered, c.Confirmed, c.Retrying, c.Failed, c.Skipped, c.Deferred, c.Recovered, c.Unrecorded, c.Errors);
            throw;
        }
        catch (Exception ex)
        {
            // The pass stopped part-way: the counters are what had been done, and the outcome says so. Only the exception type is logged, never its message.
            LogPassFailed(log, ex.GetType().Name, c.Considered, c.Confirmed, c.Retrying, c.Failed, c.Skipped, c.Deferred, c.Recovered, c.Unrecorded, c.Errors);
            throw;
        }

        var clean = c.Errors == 0 && c.Unrecorded == 0;
        LogPass(log, clean ? LogLevel.Information : LogLevel.Warning, clean ? "completed" : "completed_with_errors", c.Considered, c.Confirmed, c.Retrying, c.Failed, c.Skipped, c.Deferred, c.Recovered, c.Unrecorded, c.Errors);
        return c.ToResult();
    }

    [LoggerMessage(EventId = 7101, Message = "Guidance delivery pass {Outcome}: considered={Considered} confirmed={Confirmed} retrying={Retrying} failed={Failed} skipped={Skipped} deferred={Deferred} recovered={Recovered} unrecorded={Unrecorded} errors={Errors}")]
    private static partial void LogPass(ILogger logger, LogLevel level, string outcome, int considered, int confirmed, int retrying, int failed, int skipped, int deferred, int recovered, int unrecorded, int errors);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Error, Message = "Guidance delivery pass failed part-way ({ErrorType}): considered={Considered} confirmed={Confirmed} retrying={Retrying} failed={Failed} skipped={Skipped} deferred={Deferred} recovered={Recovered} unrecorded={Unrecorded} errors={Errors}")]
    private static partial void LogPassFailed(ILogger logger, string errorType, int considered, int confirmed, int retrying, int failed, int skipped, int deferred, int recovered, int unrecorded, int errors);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning, Message = "Guidance delivery row left unchanged and retryable: {Phase} could not be completed ({ErrorType})")]
    private static partial void LogRowError(ILogger logger, string phase, string errorType);

    private sealed class PassCounters
    {
        public int Considered, Confirmed, Retrying, Failed, Recovered, Skipped, Deferred, Unrecorded, Errors;

        public DeliveryRunResult ToResult() => new(Considered, Confirmed, Retrying, Failed, Recovered, Skipped, Unrecorded, Errors, Deferred);
    }

    private async Task RunPassAsync(int batchSize, PassCounters c, CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.UtcNow;
        var take = Math.Clamp(batchSize, 1, 100);

        // 1) recovery: an attempt whose lease ran out has an unknown outcome. It is never promoted to Confirmed.
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var stuck = await db.Deliveries.Where(d => d.State == DeliveryState.Attempted && d.LeaseUntil != null && d.LeaseUntil < now).OrderBy(d => d.LastAttemptAt).Take(take).ToListAsync(ct);
            var recovering = 0;
            foreach (var d in stuck)
            {
                recovering++;
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
                c.Recovered += recovering;
            }
            catch (DbUpdateConcurrencyException)
            {
                stuck.Clear(); // another instance recovered them first; nothing more to do here
            }

            foreach (var d in stuck)
            {
                await AuditAsync(AuditActions.GuidanceDeliveryRecovered, AuditResult.Success, d, d.State.ToString(), ct);
            }
        }

        // 2) one bounded pass over due deliveries (fair selection, see SelectDueAsync)
        var due = await SelectDueAsync(take, now, ct);

        foreach (var snapshot in due)
        {
            ct.ThrowIfCancellationRequested();
            c.Considered++;
            var channel = channels.FirstOrDefault(x => x.Name == snapshot.Channel);
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
                if (!await TrySaveAsync(db, c, "end-delivery", ct))
                {
                    continue;
                }

                c.Failed++;
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
                try
                {
                    patient = await patients.FindByPatientIdAsync(row.PatientId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // a failing lookup must not stop the other rows: the row is left as it is (retryable), counted and logged by type
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    c.Errors++;
                    LogRowError(log, "patient-lookup", ex.GetType().Name);
                    continue;
                }

                ineligible = patient is null ? "patient.missing" : patient.Status != PatientStatus.Active ? InactivePatientCode : null;
            }

            if (ineligible is not null)
            {
                var deferral = ineligible == InactivePatientCode;
                var firstTime = row.ErrorCode != ineligible; // one audit line per reason, not one per pass
                var stamp = clock.UtcNow;
                if (deferral)
                {
                    row.NextAttemptAt = stamp + DeferralDelay(o, row.ScheduledAt, stamp);
                }
                else
                {
                    row.State = DeliveryState.Failed;
                    row.FailedAt = stamp;
                    c.Failed++;
                }

                row.ErrorCode = ineligible;
                row.Version++;
                if (!await TrySaveAsync(db, c, deferral ? "defer-delivery" : "end-delivery", ct))
                {
                    if (!deferral)
                    {
                        c.Failed--;
                    }

                    continue;
                }

                c.Skipped++;
                if (deferral)
                {
                    c.Deferred++;
                }

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
            if (row.ErrorCode == InactivePatientCode)
            {
                row.ErrorCode = null; // the patient is active again: the old reason no longer applies. Any other code stays until this attempt has an outcome.
            }

            row.Version++;
            if (!await TrySaveAsync(db, c, "record-attempt", ct))
            {
                continue; // another instance claimed it between our read and our write (it is theirs), or the write was rejected (counted; the row is unchanged and retryable)
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
                c.Unrecorded++;
                continue;
            }

            switch (row.State)
            {
                case DeliveryState.Confirmed: c.Confirmed++; break;
                case DeliveryState.Scheduled: c.Retrying++; break;
                default: c.Failed++; break;
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
    }

    /// <summary>
    /// Claim, skip, defer and terminal-state writes happen BEFORE the channel is called. Saves, and reports false when the row is not ours to change: either another instance
    /// changed it first (optimistic concurrency, quiet) or the database rejected the write. A rejected write is never hidden and never fatal for the pass: it is counted as an
    /// error, logged by phase and exception type only, the row stays exactly as it was (retryable, never marked delivered or failed), and the pass moves on to the next row.
    /// A row is examined once per pass, so a rejected row cannot cause a tight retry loop.
    /// </summary>
    private async Task<bool> TrySaveAsync(GuidanceDbContext db, PassCounters c, string phase, CancellationToken ct)
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
        catch (DbUpdateException ex)
        {
            c.Errors++;
            LogRowError(log, phase, (ex.InnerException ?? ex).GetType().Name);
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
