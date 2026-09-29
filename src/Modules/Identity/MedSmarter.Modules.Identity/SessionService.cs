using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Identity;

public sealed class SessionService(ISessionStore sessions, IAuditWriter audit, IClock clock) : ISessionService
{
    public async Task<(SessionState State, Guid? UserId)> ValidateAsync(Guid sessionId, CancellationToken ct = default)
    {
        var s = await sessions.FindSessionAsync(sessionId, ct);
        if (s is null)
        {
            return (SessionState.NotFound, null);
        }

        var now = clock.UtcNow;
        if (s.RevokedAt is not null)
        {
            return (SessionState.Revoked, s.UserId);
        }

        if (now >= s.ExpiresAt)
        {
            return (SessionState.Expired, s.UserId);
        }

        if (now - s.LastSeenAt > TimeSpan.FromMinutes(1))
        {
            s.LastSeenAt = now;
        }

        return (SessionState.Valid, s.UserId);
    }

    public async Task<IReadOnlyList<SessionInfo>> ListAsync(Guid userId, Guid? currentSessionId, CancellationToken ct = default)
    {
        var list = new List<SessionInfo>();
        foreach (var s in (await sessions.SessionsOfAsync(userId, ct)).OrderByDescending(x => x.CreatedAt))
        {
            var d = await sessions.FindDeviceAsync(s.DeviceId, ct);
            list.Add(new SessionInfo(s.Id, s.DeviceId, d?.DeviceName ?? "Unknown device", d?.Platform ?? "other", s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.RevokedAt, s.Id == currentSessionId));
        }

        return list;
    }

    public async Task<bool> RevokeAsync(Guid actorUserId, Guid sessionId, Guid? ownerUserId, string reason, RequestContext context, CancellationToken ct = default)
    {
        var s = await sessions.FindSessionAsync(sessionId, ct);
        if (s is null || (ownerUserId is not null && s.UserId != ownerUserId))
        {
            return false; // not found and "not yours" are indistinguishable to the caller
        }

        if (s.RevokedAt is null)
        {
            s.RevokedAt = clock.UtcNow;
            s.RevokedReason = reason;
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.SessionRevoked, AuditResult.Success, actorUserId, "session", s.Id.ToString(), s.UserId, context.Source, context.CorrelationId, reason), ct);
        return true;
    }
}
