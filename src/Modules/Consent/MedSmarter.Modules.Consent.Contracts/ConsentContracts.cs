namespace MedSmarter.Modules.Consent.Contracts;

public enum ConsentStatus
{
    Active,
    Expired,
    Revoked,
}

public sealed record ConsentDto(
    Guid Id,
    Guid SubjectUserId,
    Guid? GranteeUserId,
    Guid? GranteeOrganizationId,
    string Purpose,
    IReadOnlyList<string> Scope,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    ConsentStatus Status,
    string Version);

public sealed record GrantConsentCommand(
    Guid SubjectUserId,
    Guid? GranteeUserId,
    Guid? GranteeOrganizationId,
    string Purpose,
    IReadOnlyList<string> Scope,
    DateTimeOffset ExpiresAt,
    string Version);

public enum ConsentError
{
    None,
    NotSubject,
    InvalidPurpose,
    InvalidScope,
    InvalidGrantee,
    InvalidExpiry,
    NotFound,
}

public sealed record ConsentOutcome(ConsentDto? Consent, ConsentError Error)
{
    public bool Succeeded => Error == ConsentError.None;
}

/// <summary>What the caller wants to do with a patient's data.</summary>
public sealed record ConsentCheck(
    Guid SubjectUserId,
    Guid GranteeUserId,
    IReadOnlyCollection<Guid> GranteeOrganizationIds,
    string DataScope,
    IReadOnlyCollection<string> AcceptablePurposes);

/// <param name="Reason">Internal reason code (audit only, never sent to clients).</param>
public sealed record ConsentDecision(bool Allowed, Guid? ConsentId, string Reason);

public enum ConsentEventKind
{
    Granted,
    Revoked,
}

/// <summary>One line of a consent's history. The history is append-only: a revoked consent keeps both lines.</summary>
public sealed record ConsentEventDto(Guid ConsentId, ConsentEventKind Kind, DateTimeOffset At, Guid ActorUserId, string Purpose);

/// <summary>Asks whether the subject has an active consent for a purpose that has no grantee (manufacturer report, monitoring, AI processing...).</summary>
public sealed record PurposeConsentCheck(Guid SubjectUserId, string Purpose, IReadOnlyCollection<string> RequiredScopes);

public interface IConsentService
{
    /// <summary>A consent can only be granted by its subject: <paramref name="actorUserId"/> must equal the subject.</summary>
    Task<ConsentOutcome> GrantAsync(Guid actorUserId, GrantConsentCommand command, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Only the subject may revoke. Revocation takes effect immediately.</summary>
    Task<ConsentOutcome> RevokeAsync(Guid actorUserId, Guid consentId, string source, string? correlationId, CancellationToken ct = default);

    Task<IReadOnlyList<ConsentDto>> ListGivenAsync(Guid subjectUserId, CancellationToken ct = default);

    /// <summary>The subject's consent history (granted and revoked events), newest first.</summary>
    Task<IReadOnlyList<ConsentEventDto>> HistoryAsync(Guid subjectUserId, CancellationToken ct = default);
}

/// <summary>Consent evaluation for purposes that are not "give person X access" (the data goes nowhere on its own account).</summary>
public interface IPurposeConsentEvaluator
{
    Task<ConsentDecision> EvaluateAsync(PurposeConsentCheck check, CancellationToken ct = default);
}

/// <summary>Consent evaluation is separate from authentication and from role checks.</summary>
public interface IConsentEvaluator
{
    Task<ConsentDecision> EvaluateAsync(ConsentCheck check, CancellationToken ct = default);
}

/// <summary>Development seeding only. Imports fictional consents without the actor==subject rule.</summary>
public interface IDemoConsentSeeder
{
    Task SeedAsync(IEnumerable<ConsentDto> consents, CancellationToken ct = default);
}
