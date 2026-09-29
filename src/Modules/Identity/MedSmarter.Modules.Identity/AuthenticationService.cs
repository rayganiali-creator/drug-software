using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Identity;

/// <summary>
/// Authentication ONLY: proves who the caller is and manages the session. It does not decide what the caller may do
/// (see <see cref="AccessAuthorizer"/>) and does not evaluate consent.
/// </summary>
public sealed class AuthenticationService(
    IEnumerable<IAuthenticationProvider> providers,
    IIdentityStore identity,
    ISessionStore sessions,
    UserIdentityService users,
    ITokenService tokens,
    LoginThrottle throttle,
    IAuditWriter audit,
    IClock clock,
    IOptions<AuthOptions> options) : IAuthenticationService, IDisposable
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public void Dispose() => _refreshGate.Dispose();

    public async Task<AuthOutcome> LoginAsync(LoginRequest request, RequestContext context, CancellationToken ct = default)
    {
        var opts = options.Value;
        var provider = providers.FirstOrDefault(p => p.Id == request.Provider);
        var throttleKey = $"{request.Provider}:{request.Credentials.GetValueOrDefault("accountId") ?? request.Credentials.Values.FirstOrDefault() ?? string.Empty}".ToLowerInvariant();
        if (throttleKey.Length > 200)
        {
            throttleKey = throttleKey[..200];
        }

        if (throttle.IsLocked(throttleKey, opts.MaxFailedLogins, TimeSpan.FromMinutes(opts.LockoutMinutes)))
        {
            await Fail(null, AuthFailure.Throttled, context, ct);
            return AuthOutcome.Fail(AuthFailure.Throttled);
        }

        var external = provider is null ? null : await provider.AuthenticateAsync(request.Credentials, ct);
        var user = external is null ? null : await identity.FindUserByIdentityAsync(external.Provider, external.Subject, ct);
        if (user is null)
        {
            throttle.RecordFailure(throttleKey);
            await Fail(null, AuthFailure.InvalidCredentials, context, ct);
            return AuthOutcome.Fail(AuthFailure.InvalidCredentials);
        }

        if (user.Status != UserStatusValue.Active)
        {
            await Fail(user.Id, AuthFailure.AccountDisabled, context, ct);
            return AuthOutcome.Fail(AuthFailure.AccountDisabled);
        }

        var summary = await users.GetSummaryAsync(user.Id, ct);
        if (summary is null || summary.Roles.Count == 0)
        {
            await Fail(user.Id, AuthFailure.NoActiveRole, context, ct);
            return AuthOutcome.Fail(AuthFailure.NoActiveRole);
        }

        throttle.Reset(throttleKey);
        var now = clock.UtcNow;
        var device = await ResolveDeviceAsync(user.Id, request.Device, now, ct);
        var session = new Session { UserId = user.Id, DeviceId = device.Id, Provider = request.Provider, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(opts.SessionDays) };
        await sessions.AddSessionAsync(session, ct);
        var result = await IssueAsync(user.Id, session, device.Id, summary, ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.Login, AuditResult.Success, user.Id, "session", session.Id.ToString(), user.Id, context.Source, context.CorrelationId, null,
            new Dictionary<string, string> { ["platform"] = device.Platform }), ct);
        return AuthOutcome.Ok(result);
    }

    public async Task<AuthOutcome> RefreshAsync(string refreshToken, RequestContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 256)
        {
            return AuthOutcome.Fail(AuthFailure.RefreshInvalid);
        }

        await _refreshGate.WaitAsync(ct);
        try
        {
            var now = clock.UtcNow;
            var stored = await sessions.FindRefreshTokenByHashAsync(tokens.HashRefreshToken(refreshToken), ct);
            var session = stored is null ? null : await sessions.FindSessionAsync(stored.SessionId, ct);
            if (stored is null || session is null)
            {
                return AuthOutcome.Fail(AuthFailure.RefreshInvalid);
            }

            if (stored.ConsumedAt is not null)
            {
                // An already-used refresh token is being replayed: assume theft and kill the whole session.
                if (session.RevokedAt is null)
                {
                    session.RevokedAt = now;
                    session.RevokedReason = "refresh_token_reuse";
                }

                await audit.WriteAsync(new AuditEvent(AuditActions.RefreshTokenReuse, AuditResult.Denied, session.UserId, "session", session.Id.ToString(), session.UserId, context.Source, context.CorrelationId, "reuse"), ct);
                return AuthOutcome.Fail(AuthFailure.RefreshInvalid);
            }

            var user = await identity.FindUserAsync(session.UserId, ct);
            if (session.RevokedAt is not null || now >= session.ExpiresAt || now >= stored.ExpiresAt || user is null || user.Status != UserStatusValue.Active)
            {
                return AuthOutcome.Fail(AuthFailure.RefreshInvalid);
            }

            var summary = await users.GetSummaryAsync(user.Id, ct);
            if (summary is null || summary.Roles.Count == 0)
            {
                return AuthOutcome.Fail(AuthFailure.RefreshInvalid);
            }

            stored.ConsumedAt = now;
            var result = await IssueAsync(user.Id, session, session.DeviceId, summary, ct, stored);
            session.LastSeenAt = now;
            await audit.WriteAsync(new AuditEvent(AuditActions.TokenRefreshed, AuditResult.Success, user.Id, "session", session.Id.ToString(), user.Id, context.Source, context.CorrelationId), ct);
            return AuthOutcome.Ok(result);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, RequestContext context, CancellationToken ct = default)
    {
        var session = await sessions.FindSessionAsync(sessionId, ct);
        if (session is null || session.UserId != userId)
        {
            return;
        }

        session.RevokedAt ??= clock.UtcNow;
        session.RevokedReason ??= "logout";
        await audit.WriteAsync(new AuditEvent(AuditActions.Logout, AuditResult.Success, userId, "session", sessionId.ToString(), userId, context.Source, context.CorrelationId), ct);
    }

    public async Task LogoutAllAsync(Guid userId, RequestContext context, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        foreach (var s in await sessions.SessionsOfAsync(userId, ct))
        {
            if (s.RevokedAt is null)
            {
                s.RevokedAt = now;
                s.RevokedReason = "logout_all";
            }
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.LogoutAll, AuditResult.Success, userId, "user", userId.ToString(), userId, context.Source, context.CorrelationId), ct);
    }

    private async Task<AuthResult> IssueAsync(Guid userId, Session session, Guid deviceId, UserSummary summary, CancellationToken ct, RefreshToken? previous = null)
    {
        var now = clock.UtcNow;
        var access = tokens.IssueAccessToken(userId, session.Id);
        var (token, hash) = tokens.NewRefreshToken();
        var refresh = new RefreshToken { SessionId = session.Id, TokenHash = hash, CreatedAt = now, ExpiresAt = session.ExpiresAt };
        await sessions.AddRefreshTokenAsync(refresh, ct);
        if (previous is not null)
        {
            previous.ReplacedById = refresh.Id;
        }

        return new AuthResult(new TokenPair(access.Token, access.ExpiresAt, token, refresh.ExpiresAt), summary, session.Id, deviceId);
    }

    private async Task<Device> ResolveDeviceAsync(Guid userId, DeviceInfo info, DateTimeOffset now, CancellationToken ct)
    {
        // Devices are identified only by an app-generated random id. No hardware fingerprinting.
        if (Guid.TryParse(info.DeviceId, out var id) && await sessions.FindDeviceAsync(id, ct) is { } existing && existing.UserId == userId && existing.RevokedAt is null)
        {
            existing.LastSeenAt = now;
            existing.AppVersion = Clip(info.AppVersion, 32);
            return existing;
        }

        var platform = info.Platform is "web" or "android" or "ios" ? info.Platform : "other";
        var device = new Device { UserId = userId, Platform = platform, AppVersion = Clip(info.AppVersion, 32), DeviceName = Clip(info.DeviceName, 60) ?? "Unknown device", CreatedAt = now, LastSeenAt = now };
        await sessions.AddDeviceAsync(device, ct);
        return device;
    }

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        v = v is null ? null : new string([.. v.Where(c => !char.IsControl(c))]);
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    private Task Fail(Guid? userId, AuthFailure failure, RequestContext context, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent(AuditActions.LoginFailed, AuditResult.Failure, userId, "session", null, userId, context.Source, context.CorrelationId, failure.ToString()), ct);
}
