namespace MedSmarter.Modules.Identity.Contracts;

public enum UserStatus
{
    Active,
    Disabled,
}

/// <summary>The authenticated caller as resolved by the server on every request (roles/permissions are never read from the token).</summary>
public sealed record CurrentUser(
    Guid UserId,
    Guid SessionId,
    string DisplayName,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<Guid> OrganizationIds)
{
    public bool Has(string permission) => Permissions.Contains(permission);
}

public enum AccessLayer
{
    Authentication,
    Rbac,
    Ownership,
    Relationship,
    Organization,
    Consent,
    Aggregate,
    Allowed,
}

/// <param name="ReasonCode">Internal code for audit. Must never be returned to API clients.</param>
public sealed record AccessDecision(bool Allowed, AccessLayer Layer, string ReasonCode)
{
    public static AccessDecision Allow(AccessLayer layer) => new(true, layer, "allowed");
    public static AccessDecision Deny(AccessLayer layer, string reason) => new(false, layer, reason);
}

/// <summary>The thing being accessed. Only the fields relevant for the permission kind are set.</summary>
public sealed record AccessResource(string Type, string? Id, Guid? SubjectUserId, Guid? OrganizationId)
{
    public static AccessResource Patient(Guid patientUserId, string type = "patient") => new(type, patientUserId.ToString(), patientUserId, null);
    public static AccessResource Organization(Guid organizationId) => new("organization", organizationId.ToString(), null, organizationId);
    /// <summary>A patient's record reached THROUGH an organization (e.g. a pharmacy handling that patient's prescription).</summary>
    public static AccessResource OrgPatient(Guid organizationId, Guid patientUserId, string type) => new(type, patientUserId.ToString(), patientUserId, organizationId);

    public static AccessResource None(string type) => new(type, null, null, null);
}

public sealed record RequestContext(string Source, string? CorrelationId);

/// <summary>RBAC + resource scope (ownership, relationship, organization) + consent, in that order. Deny by default.</summary>
public interface IAccessAuthorizer
{
    Task<AccessDecision> AuthorizeAsync(CurrentUser actor, string permission, AccessResource resource, RequestContext context, CancellationToken ct = default);
}

// ---------- authentication ----------

public sealed record DeviceInfo(string? DeviceId, string Platform, string? AppVersion, string? DeviceName);

public sealed record LoginRequest(string Provider, IReadOnlyDictionary<string, string> Credentials, DeviceInfo Device);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record UserSummary(
    Guid Id,
    string DisplayName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> OrganizationIds,
    string Status);

public sealed record AuthResult(TokenPair Tokens, UserSummary User, Guid SessionId, Guid DeviceId);

public enum AuthFailure
{
    InvalidCredentials,
    AccountDisabled,
    NoActiveRole,
    Throttled,
    RefreshInvalid,
}

public sealed record AuthOutcome(AuthResult? Result, AuthFailure? Failure)
{
    public bool Succeeded => Result is not null;
    public static AuthOutcome Ok(AuthResult r) => new(r, null);
    public static AuthOutcome Fail(AuthFailure f) => new(null, f);
}

public sealed record ProviderIdentity(string Provider, string Subject);

/// <summary>
/// Abstraction over "who is this?". Development uses MockAuthenticationProvider. A real provider (OIDC, national ID,
/// hospital SSO...) implements this interface and is registered instead; clients and authorization do not change.
/// </summary>
public interface IAuthenticationProvider
{
    string Id { get; }
    Task<ProviderIdentity?> AuthenticateAsync(IReadOnlyDictionary<string, string> credentials, CancellationToken ct = default);
}

public interface IAuthenticationService
{
    Task<AuthOutcome> LoginAsync(LoginRequest request, RequestContext context, CancellationToken ct = default);
    Task<AuthOutcome> RefreshAsync(string refreshToken, RequestContext context, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, Guid sessionId, RequestContext context, CancellationToken ct = default);
    Task LogoutAllAsync(Guid userId, RequestContext context, CancellationToken ct = default);
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

/// <summary>The only facts an access token carries: who (sub), which session (sid), and when it expires. No roles, no PHI.</summary>
public sealed record AccessTokenClaims(Guid UserId, Guid SessionId, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessTokenResult IssueAccessToken(Guid userId, Guid sessionId);

    /// <summary>Verifies signature, issuer, audience and lifetime. Null when anything is wrong.</summary>
    AccessTokenClaims? ValidateAccessToken(string token);
    (string Token, string Hash) NewRefreshToken();
    string HashRefreshToken(string token);
}

public enum SessionState
{
    Valid,
    NotFound,
    Revoked,
    Expired,
}

public sealed record SessionInfo(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Platform,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool IsCurrent);

public interface ISessionService
{
    Task<(SessionState State, Guid? UserId)> ValidateAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionInfo>> ListAsync(Guid userId, Guid? currentSessionId, CancellationToken ct = default);

    /// <summary>Revokes one session. <paramref name="ownerUserId"/> restricts the operation to that user's sessions.</summary>
    Task<bool> RevokeAsync(Guid actorUserId, Guid sessionId, Guid? ownerUserId, string reason, RequestContext context, CancellationToken ct = default);
}

public interface IUserIdentityService
{
    Task<CurrentUser?> GetCurrentUserAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
    Task<UserSummary?> GetSummaryAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct = default);

    /// <summary>Patients the actor has an ACTIVE care relationship with (directly or through an organization). Says nothing about consent.</summary>
    Task<IReadOnlyList<PatientRef>> ListRelatedPatientsAsync(CurrentUser actor, CancellationToken ct = default);
}

public sealed record PatientRef(Guid UserId, string DisplayName);

public enum AdminError
{
    None,
    UserNotFound,
    UnknownRole,
    AlreadyAssigned,
    NotAssigned,
    LastSystemAdmin,
}

public interface IUserAdministrationService
{
    Task<AdminError> AssignRoleAsync(Guid actorUserId, Guid userId, string role, Guid? organizationId, RequestContext context, CancellationToken ct = default);
    Task<AdminError> RevokeRoleAsync(Guid actorUserId, Guid userId, string role, RequestContext context, CancellationToken ct = default);
}

/// <summary>A fictional account offered by the development login screen. Never contains credentials.</summary>
public sealed record DemoAccountInfo(
    string Id,
    IReadOnlyDictionary<string, string> DisplayName,
    IReadOnlyDictionary<string, string> Description,
    IReadOnlyList<string> Roles,
    bool Primary,
    string Status);

/// <summary>Development-only. Not registered (and therefore unreachable) unless the authentication mode is DevelopmentMock.</summary>
public interface IDemoAccountDirectory
{
    IReadOnlyList<DemoAccountInfo> List();
}

public interface IDemoIdentitySeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
