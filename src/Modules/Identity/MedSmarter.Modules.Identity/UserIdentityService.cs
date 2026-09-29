using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Identity;

public sealed class UserIdentityService(IIdentityStore identity, ISessionService sessions, AccessCatalog catalog) : IUserIdentityService
{
    /// <summary>
    /// Resolved on EVERY request: session must still be valid, user active, roles current. Because nothing is cached in
    /// the token, a revoked session or role takes effect on the very next request.
    /// </summary>
    public async Task<CurrentUser?> GetCurrentUserAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var (state, owner) = await sessions.ValidateAsync(sessionId, ct);
        if (state != SessionState.Valid || owner != userId)
        {
            return null;
        }

        var user = await identity.FindUserAsync(userId, ct);
        if (user is null || user.Status != UserStatusValue.Active)
        {
            return null;
        }

        var roles = await ActiveRolesAsync(userId, ct);
        if (roles.Count == 0)
        {
            return null;
        }

        var profile = await identity.FindProfileAsync(userId, ct);
        var orgs = await identity.ActiveOrganizationIdsAsync(userId, ct);
        return new CurrentUser(userId, sessionId, profile?.DisplayName ?? "User", roles, catalog.PermissionsOf(roles), orgs.ToHashSet());
    }

    public async Task<UserSummary?> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await identity.FindUserAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        var profile = await identity.FindProfileAsync(userId, ct);
        var roles = await ActiveRolesAsync(userId, ct);
        var orgs = await identity.ActiveOrganizationIdsAsync(userId, ct);
        return new UserSummary(userId, profile?.DisplayName ?? "User", profile?.Email, [.. roles.Order()], [.. catalog.PermissionsOf(roles).Order()], orgs, user.Status.ToString());
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        var list = new List<UserSummary>();
        foreach (var u in await identity.AllUsersAsync(ct))
        {
            if (await GetSummaryAsync(u.Id, ct) is { } s)
            {
                list.Add(s);
            }
        }

        return [.. list.OrderBy(s => s.DisplayName, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<PatientRef>> ListRelatedPatientsAsync(CurrentUser actor, CancellationToken ct = default)
    {
        var result = new List<PatientRef>();
        foreach (var u in await identity.AllUsersAsync(ct))
        {
            if (u.Id == actor.UserId || u.Status != UserStatusValue.Active)
            {
                continue;
            }

            var related = (await identity.RelationshipsForPatientAsync(u.Id, ct))
                .Any(r => r.IsActive && (r.ProviderUserId == actor.UserId || (r.ProviderOrganizationId is { } o && actor.OrganizationIds.Contains(o))));
            if (related)
            {
                result.Add(new PatientRef(u.Id, (await identity.FindProfileAsync(u.Id, ct))?.DisplayName ?? "Patient"));
            }
        }

        return [.. result.OrderBy(p => p.DisplayName, StringComparer.Ordinal)];
    }

    private async Task<HashSet<string>> ActiveRolesAsync(Guid userId, CancellationToken ct) =>
        [.. (await identity.RolesOfAsync(userId, ct)).Where(r => r.IsActive && catalog.IsKnownRole(r.RoleName)).Select(r => r.RoleName)];
}
