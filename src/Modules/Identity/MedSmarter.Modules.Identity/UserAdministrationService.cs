using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Identity;

/// <summary>Role administration. Callers must already hold role.manage; this service enforces the invariants and audits.</summary>
public sealed class UserAdministrationService(IIdentityStore identity, AccessCatalog catalog, IAuditWriter audit, IClock clock) : IUserAdministrationService
{
    public async Task<AdminError> AssignRoleAsync(Guid actorUserId, Guid userId, string role, Guid? organizationId, RequestContext context, CancellationToken ct = default)
    {
        if (await identity.FindUserAsync(userId, ct) is null)
        {
            return AdminError.UserNotFound;
        }

        if (!catalog.IsKnownRole(role))
        {
            return AdminError.UnknownRole;
        }

        if ((await identity.RolesOfAsync(userId, ct)).Any(r => r.IsActive && r.RoleName == role && r.OrganizationId == organizationId))
        {
            return AdminError.AlreadyAssigned;
        }

        await identity.AddRoleAsync(new UserRole { UserId = userId, RoleName = role, OrganizationId = organizationId, AssignedAt = clock.UtcNow, AssignedBy = actorUserId }, ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.RoleAssigned, AuditResult.Success, actorUserId, "user", userId.ToString(), userId, context.Source, context.CorrelationId, null,
            new Dictionary<string, string> { ["role"] = role }), ct);
        return AdminError.None;
    }

    public async Task<AdminError> RevokeRoleAsync(Guid actorUserId, Guid userId, string role, RequestContext context, CancellationToken ct = default)
    {
        if (await identity.FindUserAsync(userId, ct) is null)
        {
            return AdminError.UserNotFound;
        }

        var active = (await identity.RolesOfAsync(userId, ct)).Where(r => r.IsActive && r.RoleName == role).ToList();
        if (active.Count == 0)
        {
            return AdminError.NotAssigned;
        }

        if (role == RoleNames.SystemAdmin && await identity.CountActiveHoldersAsync(role, ct) <= 1)
        {
            return AdminError.LastSystemAdmin; // never lock the platform out of administration
        }

        var now = clock.UtcNow;
        foreach (var r in active)
        {
            r.RevokedAt = now;
            r.RevokedBy = actorUserId;
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.RoleRevoked, AuditResult.Success, actorUserId, "user", userId.ToString(), userId, context.Source, context.CorrelationId, null,
            new Dictionary<string, string> { ["role"] = role }), ct);
        return AdminError.None;
    }
}
