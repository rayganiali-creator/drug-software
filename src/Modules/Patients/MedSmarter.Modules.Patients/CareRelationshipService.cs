using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Patients;

/// <summary>
/// Care relationships between a patient and a physician or pharmacist. A relationship starts only when BOTH sides have agreed
/// (one asks, the other accepts) and either side can end it at any time; it is the "relationship" layer of access control (docs/phase5).
/// </summary>
public sealed class CareRelationshipService(IDbContextFactory<PatientsDbContext> factory, IClock clock, IAuditWriter audit, IServiceProvider services) : ICareRelationshipService, ICareRelationshipSource
{
    // Resolved on use: the identity service itself reads care relationships from this class, so a constructor dependency would be a cycle.
    private IUserIdentityService Identity => services.GetRequiredService<IUserIdentityService>();

    private static readonly string[] Kinds = ["Treating", "Dispensing"];

    public async Task<PatientOutcome<CareRelationshipDto>> RequestAsync(Guid actorUserId, RequestCareRelationshipCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!Kinds.Contains(command.Kind))
        {
            return Support.Invalid<CareRelationshipDto>(["kind.invalid"]);
        }

        if (command.CounterpartUserId == actorUserId)
        {
            return Support.Invalid<CareRelationshipDto>(["relationship.invalid_parties"]);
        }

        var providerRole = command.Kind == "Treating" ? RoleNames.Physician : RoleNames.Pharmacist;
        var actor = await Identity.GetSummaryAsync(actorUserId, ct);
        var other = await Identity.GetSummaryAsync(command.CounterpartUserId, ct);
        if (actor is null || other is null || other.Status != nameof(UserStatus.Active))
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.NotFound, "counterpart.not_found");
        }

        var actorIsPatient = actor.Roles.Contains(RoleNames.Patient);
        var otherIsProvider = other.Roles.Contains(providerRole);
        var actorIsProvider = actor.Roles.Contains(providerRole);
        var otherIsPatient = other.Roles.Contains(RoleNames.Patient);
        Guid patientUser, providerUser;
        string initiatedBy;
        if (actorIsPatient && otherIsProvider)
        {
            (patientUser, providerUser, initiatedBy) = (actorUserId, command.CounterpartUserId, "Patient");
        }
        else if (actorIsProvider && otherIsPatient)
        {
            (patientUser, providerUser, initiatedBy) = (command.CounterpartUserId, actorUserId, "Provider");
        }
        else
        {
            return Support.Invalid<CareRelationshipDto>(["relationship.invalid_parties"]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, patientUser, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.NotFound, "counterpart.not_found"); // the patient creates their own record first
        }

        if (patient.Status != PatientStatus.Active)
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Forbidden, "patient.inactive");
        }

        var open = new[] { CareRelationshipStatus.PendingPatient, CareRelationshipStatus.PendingProvider, CareRelationshipStatus.Active };
        if (await db.CareRelationships.AnyAsync(x => x.PatientUserId == patientUser && x.ProviderUserId == providerUser && x.Kind == command.Kind && open.Contains(x.Status), ct))
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Conflict, "relationship.exists");
        }

        var now = clock.UtcNow;
        var row = new CareRelationshipRecord
        {
            Id = Guid.CreateVersion7(), PatientId = patient.Id, PatientUserId = patientUser, ProviderUserId = providerUser, Kind = command.Kind,
            Status = initiatedBy == "Patient" ? CareRelationshipStatus.PendingProvider : CareRelationshipStatus.PendingPatient, InitiatedBy = initiatedBy, RequestedAt = now,
            PatientConsentedAt = initiatedBy == "Patient" ? now : null, ProviderConsentedAt = initiatedBy == "Provider" ? now : null,
        };
        db.CareRelationships.Add(row);
        db.CareRelationshipEvents.Add(Event(row.Id, "requested", now, actorUserId));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Conflict, "relationship.exists");
        }

        await Support.AuditAsync(audit, AuditActions.CareRelationshipRequested, AuditResult.Success, actorUserId, "care-relationship", row.Id, patientUser, source, correlationId, initiatedBy, command.Kind);
        return PatientOutcome.Ok(ToDto(row));
    }

    public Task<PatientOutcome<CareRelationshipDto>> AcceptAsync(Guid actorUserId, Guid id, string source, string? correlationId, CancellationToken ct = default) =>
        DecideAsync(actorUserId, id, accept: true, source, correlationId, ct);

    public Task<PatientOutcome<CareRelationshipDto>> DeclineAsync(Guid actorUserId, Guid id, string source, string? correlationId, CancellationToken ct = default) =>
        DecideAsync(actorUserId, id, accept: false, source, correlationId, ct);

    private async Task<PatientOutcome<CareRelationshipDto>> DecideAsync(Guid actorUserId, Guid id, bool accept, string source, string? correlationId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CareRelationships.FirstOrDefaultAsync(x => x.Id == id, ct);
        // "not found" and "not your relationship" look the same: other people's requests cannot be probed.
        var isPatient = row?.PatientUserId == actorUserId;
        var isProvider = row?.ProviderUserId == actorUserId;
        var mayDecide = row is not null && ((isPatient && row.Status == CareRelationshipStatus.PendingPatient) || (isProvider && row.Status == CareRelationshipStatus.PendingProvider));
        if (row is null || !(isPatient || isProvider))
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.NotFound, "relationship.not_found");
        }

        if (!mayDecide)
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Conflict, "relationship.not_awaiting_you"); // e.g. accepting your own request, or one that is already decided
        }

        var now = clock.UtcNow;
        if (accept)
        {
            row.Status = CareRelationshipStatus.Active;
            if (isPatient) { row.PatientConsentedAt = now; } else { row.ProviderConsentedAt = now; }
        }
        else
        {
            row.Status = CareRelationshipStatus.Declined;
            row.EndedAt = now;
            row.EndedBy = actorUserId;
            row.EndReason = "declined";
        }

        row.Version++;
        db.CareRelationshipEvents.Add(Event(row.Id, accept ? "accepted" : "declined", now, actorUserId));
        if (!await TrySaveAsync(db, ct))
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, accept ? AuditActions.CareRelationshipAccepted : AuditActions.CareRelationshipDeclined, AuditResult.Success, actorUserId, "care-relationship", row.Id, row.PatientUserId, source, correlationId);
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<CareRelationshipDto>> EndAsync(Guid actorUserId, Guid id, string? reason, string source, string? correlationId, CancellationToken ct = default)
    {
        if (FreeTextGuard.Problem(reason, "reason", 200) is { } problem)
        {
            return Support.Invalid<CareRelationshipDto>([problem]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CareRelationships.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || (row.PatientUserId != actorUserId && row.ProviderUserId != actorUserId))
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.NotFound, "relationship.not_found");
        }

        var initiator = row.InitiatedBy == "Patient" ? row.PatientUserId : row.ProviderUserId;
        var withdrawable = row.Status is CareRelationshipStatus.PendingPatient or CareRelationshipStatus.PendingProvider && initiator == actorUserId;
        if (row.Status != CareRelationshipStatus.Active && !withdrawable)
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Conflict, "relationship.not_active");
        }

        var now = clock.UtcNow;
        row.Status = CareRelationshipStatus.Ended;
        row.EndedAt = now;
        row.EndedBy = actorUserId;
        row.EndReason = FreeTextGuard.Clean(reason) ?? (withdrawable ? "withdrawn" : "ended");
        row.Version++;
        db.CareRelationshipEvents.Add(Event(row.Id, "ended", now, actorUserId));
        if (!await TrySaveAsync(db, ct))
        {
            return PatientOutcome.Fail<CareRelationshipDto>(PatientError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, AuditActions.CareRelationshipEnded, AuditResult.Success, actorUserId, "care-relationship", row.Id, row.PatientUserId, source, correlationId);
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<IReadOnlyList<CareRelationshipDto>> ListMineAsync(Guid actorUserId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CareRelationships.AsNoTracking().Where(x => x.PatientUserId == actorUserId || x.ProviderUserId == actorUserId).ToListAsync(ct);
        return [.. rows.OrderByDescending(x => x.RequestedAt).Select(ToDto)];
    }

    // ---------- ICareRelationshipSource: what the authorizer sees ----------

    public async Task<IReadOnlyList<ActiveCareLink>> ActiveForPatientAsync(Guid patientUserId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // A deactivated patient record grants professionals nothing.
        var rows = await (from r in db.CareRelationships.AsNoTracking()
                          join p in db.Patients.AsNoTracking() on r.PatientId equals p.Id
                          where r.PatientUserId == patientUserId && r.Status == CareRelationshipStatus.Active && p.Status == PatientStatus.Active
                          select r).ToListAsync(ct);
        return [.. rows.Select(r => new ActiveCareLink(r.PatientUserId, r.ProviderUserId, r.ProviderOrganizationId, r.Kind))];
    }

    public async Task<IReadOnlyList<ActiveCareLink>> ActiveForProviderAsync(Guid providerUserId, IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var orgs = organizationIds.ToArray();
        var rows = await (from r in db.CareRelationships.AsNoTracking()
                          join p in db.Patients.AsNoTracking() on r.PatientId equals p.Id
                          where r.Status == CareRelationshipStatus.Active && p.Status == PatientStatus.Active
                             && (r.ProviderUserId == providerUserId || (r.ProviderOrganizationId != null && orgs.Contains(r.ProviderOrganizationId.Value)))
                          select r).ToListAsync(ct);
        return [.. rows.Select(r => new ActiveCareLink(r.PatientUserId, r.ProviderUserId, r.ProviderOrganizationId, r.Kind))];
    }

    private static CareRelationshipEventRecord Event(Guid relationshipId, string kind, DateTimeOffset at, Guid actor) =>
        new() { Id = Guid.CreateVersion7(), RelationshipId = relationshipId, Kind = kind, At = at, ActorUserId = actor };

    private static async Task<bool> TrySaveAsync(PatientsDbContext db, CancellationToken ct)
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

    internal static CareRelationshipDto ToDto(CareRelationshipRecord r) =>
        new(r.Id, r.PatientUserId, r.ProviderUserId, r.Kind, r.Status, r.InitiatedBy, r.RequestedAt, r.PatientConsentedAt, r.ProviderConsentedAt, r.EndedAt, r.EndedBy is null ? null : r.EndedBy == r.PatientUserId ? "Patient" : "Provider", r.EndReason, r.ProviderOrganizationId);
}
