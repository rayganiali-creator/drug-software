using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Consent;

/// <summary>Persistence port. In-memory for development; PostgreSQL later.</summary>
public interface IConsentStore
{
    Task AddAsync(ConsentRecord record, CancellationToken ct);
    Task<ConsentRecord?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ConsentRecord>> ForSubjectAsync(Guid subjectUserId, CancellationToken ct);
}

/// <summary>Mutable persistence record; the public shape is <see cref="ConsentDto"/>.</summary>
public sealed class ConsentRecord
{
    public Guid Id { get; init; }
    public Guid SubjectUserId { get; init; }
    public Guid? GranteeUserId { get; init; }
    public Guid? GranteeOrganizationId { get; init; }
    public string Purpose { get; init; } = string.Empty;
    public IReadOnlyList<string> Scope { get; init; } = [];
    public DateTimeOffset GrantedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string Version { get; init; } = string.Empty;

    public ConsentStatus StatusAt(DateTimeOffset now) => RevokedAt is not null ? ConsentStatus.Revoked : now >= ExpiresAt ? ConsentStatus.Expired : ConsentStatus.Active;

    public ConsentDto ToDto(DateTimeOffset now) => new(Id, SubjectUserId, GranteeUserId, GranteeOrganizationId, Purpose, Scope, GrantedAt, ExpiresAt, RevokedAt, StatusAt(now), Version);
}

public sealed class InMemoryConsentStore : IConsentStore
{
    private readonly List<ConsentRecord> _items = [];
    private readonly Lock _gate = new();

    public Task AddAsync(ConsentRecord record, CancellationToken ct)
    {
        lock (_gate)
        {
            _items.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<ConsentRecord?> FindAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
        }
    }

    public Task<IReadOnlyList<ConsentRecord>> ForSubjectAsync(Guid subjectUserId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ConsentRecord>>([.. _items.Where(x => x.SubjectUserId == subjectUserId)]);
        }
    }
}

public sealed class ConsentService(IConsentStore store, IAuditWriter audit, IClock clock) : IConsentService, IConsentEvaluator, IDemoConsentSeeder
{
    private static readonly string[] Purposes = [ConsentPurposes.Treatment, ConsentPurposes.MedicationReview, ConsentPurposes.Dispensing, ConsentPurposes.Research];
    private static readonly string[] Scopes = [DataScopes.Profile, DataScopes.Medications, DataScopes.Prescriptions, DataScopes.Adherence, DataScopes.Adr, DataScopes.Checkins, DataScopes.Symptoms, DataScopes.AiSummary];

    /// <summary>A consent may not run longer than this (forces periodic re-confirmation).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(366);

    public async Task<ConsentOutcome> GrantAsync(Guid actorUserId, GrantConsentCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        ConsentError error = ConsentError.None;
        if (actorUserId != command.SubjectUserId)
        {
            error = ConsentError.NotSubject; // only the data subject can consent to sharing their data
        }
        else if (!Purposes.Contains(command.Purpose))
        {
            error = ConsentError.InvalidPurpose;
        }
        else if (command.Scope.Count == 0 || command.Scope.Any(s => !Scopes.Contains(s)))
        {
            error = ConsentError.InvalidScope;
        }
        else if ((command.GranteeUserId is null) == (command.GranteeOrganizationId is null) || command.GranteeUserId == command.SubjectUserId)
        {
            error = ConsentError.InvalidGrantee; // exactly one grantee, and not yourself
        }
        else if (command.ExpiresAt <= now || command.ExpiresAt - now > MaxDuration)
        {
            error = ConsentError.InvalidExpiry;
        }

        if (error != ConsentError.None)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.ConsentGranted, AuditResult.Denied, actorUserId, "consent", null, command.SubjectUserId, source, correlationId, error.ToString()), ct);
            return new ConsentOutcome(null, error);
        }

        var record = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            SubjectUserId = command.SubjectUserId,
            GranteeUserId = command.GranteeUserId,
            GranteeOrganizationId = command.GranteeOrganizationId,
            Purpose = command.Purpose,
            Scope = [.. command.Scope.Distinct()],
            GrantedAt = now,
            ExpiresAt = command.ExpiresAt,
            Version = command.Version,
        };
        await store.AddAsync(record, ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.ConsentGranted, AuditResult.Success, actorUserId, "consent", record.Id.ToString(), command.SubjectUserId, source, correlationId,
            Metadata: new Dictionary<string, string> { ["purpose"] = command.Purpose, ["scope"] = string.Join(',', record.Scope) }), ct);
        return new ConsentOutcome(record.ToDto(now), ConsentError.None);
    }

    public async Task<ConsentOutcome> RevokeAsync(Guid actorUserId, Guid consentId, string source, string? correlationId, CancellationToken ct = default)
    {
        var record = await store.FindAsync(consentId, ct);
        // Same outcome for "not found" and "not yours": no probing of other people's consents.
        if (record is null || record.SubjectUserId != actorUserId)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.ConsentRevoked, AuditResult.Denied, actorUserId, "consent", consentId.ToString(), null, source, correlationId, "not_found_or_not_subject"), ct);
            return new ConsentOutcome(null, ConsentError.NotFound);
        }

        var now = clock.UtcNow;
        record.RevokedAt ??= now;
        await audit.WriteAsync(new AuditEvent(AuditActions.ConsentRevoked, AuditResult.Success, actorUserId, "consent", record.Id.ToString(), record.SubjectUserId, source, correlationId), ct);
        return new ConsentOutcome(record.ToDto(now), ConsentError.None);
    }

    public async Task<IReadOnlyList<ConsentDto>> ListGivenAsync(Guid subjectUserId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return [.. (await store.ForSubjectAsync(subjectUserId, ct)).OrderByDescending(x => x.GrantedAt).Select(x => x.ToDto(now))];
    }

    public async Task<ConsentDecision> EvaluateAsync(ConsentCheck check, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var candidates = (await store.ForSubjectAsync(check.SubjectUserId, ct))
            .Where(x => x.GranteeUserId == check.GranteeUserId || (x.GranteeOrganizationId is Guid org && check.GranteeOrganizationIds.Contains(org)))
            .ToList();
        if (candidates.Count == 0)
        {
            return new ConsentDecision(false, null, "no_consent");
        }

        var active = candidates.Where(x => x.StatusAt(now) == ConsentStatus.Active).ToList();
        if (active.Count == 0)
        {
            return new ConsentDecision(false, null, candidates.Any(x => x.StatusAt(now) == ConsentStatus.Revoked) && !candidates.Any(x => x.StatusAt(now) == ConsentStatus.Expired) ? "consent_revoked" : "consent_expired");
        }

        var purposeOk = active.Where(x => check.AcceptablePurposes.Contains(x.Purpose)).ToList();
        if (purposeOk.Count == 0)
        {
            return new ConsentDecision(false, null, "purpose_mismatch");
        }

        var match = purposeOk.FirstOrDefault(x => x.Scope.Contains(check.DataScope));
        return match is null ? new ConsentDecision(false, null, "scope_mismatch") : new ConsentDecision(true, match.Id, "allowed");
    }

    public async Task SeedAsync(IEnumerable<ConsentDto> consents, CancellationToken ct = default)
    {
        foreach (var c in consents)
        {
            await store.AddAsync(new ConsentRecord
            {
                Id = c.Id, SubjectUserId = c.SubjectUserId, GranteeUserId = c.GranteeUserId, GranteeOrganizationId = c.GranteeOrganizationId,
                Purpose = c.Purpose, Scope = c.Scope, GrantedAt = c.GrantedAt, ExpiresAt = c.ExpiresAt ?? c.GrantedAt.AddDays(1), RevokedAt = c.RevokedAt, Version = c.Version,
            }, ct);
        }
    }
}
