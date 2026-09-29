namespace MedSmarter.Modules.Identity;

// Domain identity model (see docs/phase3/01-identity-model.md). Not UI models.

public sealed class User
{
    public Guid Id { get; init; }
    public UserStatusValue Status { get; set; } = UserStatusValue.Active;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
}

public enum UserStatusValue
{
    Active,
    Disabled,
}

public sealed class UserProfile
{
    public Guid UserId { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Locale { get; set; } = "fa";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Links an external identity (provider + subject) to a user, so providers can be swapped without touching users.</summary>
public sealed class UserIdentityLink
{
    public string Provider { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public DateTimeOffset LinkedAt { get; init; }
}

/// <summary>Assignment of a role (defined in the catalog) to a user. Revocation keeps history.</summary>
public sealed class UserRole
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid? OrganizationId { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
    public Guid? AssignedBy { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public bool IsActive => RevokedAt is null;
}

public sealed class Organization
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class OrganizationMembership
{
    public Guid UserId { get; init; }
    public Guid OrganizationId { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; set; }
    public bool IsActive => LeftAt is null;
}

public enum CareRelationshipKind
{
    Treating,
    Dispensing,
}

/// <summary>"Provider P (a user or an organization) is in a care relationship with patient X": the relationship scope.</summary>
public sealed class CareRelationship
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PatientUserId { get; init; }
    public Guid? ProviderUserId { get; init; }
    public Guid? ProviderOrganizationId { get; init; }
    public CareRelationshipKind Kind { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public bool IsActive => EndedAt is null;
}

public sealed class Device
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Platform { get; init; } = "other";
    public string? AppVersion { get; set; }
    public string DeviceName { get; set; } = "Unknown device";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool Trusted { get; set; }
}

public sealed class Session
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public Guid DeviceId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? ReplacedById { get; set; }
}
