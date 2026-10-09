using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Consent;

/// <summary>Persistence port. PostgreSQL by default; in memory only for tests and database-free development sessions.</summary>
public interface IConsentStore
{
    /// <summary>Stores the consent and its "granted" history line atomically.</summary>
    Task AddAsync(ConsentRecord record, ConsentEventRecord granted, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<ConsentRecord?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ConsentRecord>> ForSubjectAsync(Guid subjectUserId, CancellationToken ct);

    /// <summary>Marks the consent revoked (only if it is not revoked yet) and appends the history line, atomically. Returns the stored record.</summary>
    Task<ConsentRecord?> RevokeAsync(Guid id, DateTimeOffset at, ConsentEventRecord revoked, CancellationToken ct);
    Task<IReadOnlyList<ConsentEventRecord>> EventsAsync(Guid subjectUserId, CancellationToken ct);
}

/// <summary>Persistence record; the public shape is <see cref="ConsentDto"/>.</summary>
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

    public ConsentRecord Copy() => new()
    {
        Id = Id, SubjectUserId = SubjectUserId, GranteeUserId = GranteeUserId, GranteeOrganizationId = GranteeOrganizationId, Purpose = Purpose,
        Scope = [.. Scope], GrantedAt = GrantedAt, ExpiresAt = ExpiresAt, RevokedAt = RevokedAt, Version = Version,
    };
}

public sealed class ConsentEventRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid ConsentId { get; init; }
    public Guid SubjectUserId { get; init; }
    public ConsentEventKind Kind { get; init; }
    public DateTimeOffset At { get; init; }
    public Guid ActorUserId { get; init; }
    public string Purpose { get; init; } = string.Empty;

    public ConsentEventDto ToDto() => new(ConsentId, Kind, At, ActorUserId, Purpose);
}

public sealed class InMemoryConsentStore : IConsentStore
{
    private readonly List<ConsentRecord> _items = [];
    private readonly List<ConsentEventRecord> _events = [];
    private readonly Lock _gate = new();

    public Task AddAsync(ConsentRecord record, ConsentEventRecord granted, CancellationToken ct)
    {
        lock (_gate)
        {
            _items.Add(record.Copy());
            _events.Add(granted);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.Any(x => x.Id == id));
        }
    }

    public Task<ConsentRecord?> FindAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id)?.Copy());
        }
    }

    public Task<IReadOnlyList<ConsentRecord>> ForSubjectAsync(Guid subjectUserId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ConsentRecord>>([.. _items.Where(x => x.SubjectUserId == subjectUserId).Select(x => x.Copy())]);
        }
    }

    public Task<ConsentRecord?> RevokeAsync(Guid id, DateTimeOffset at, ConsentEventRecord revoked, CancellationToken ct)
    {
        lock (_gate)
        {
            var record = _items.FirstOrDefault(x => x.Id == id);
            if (record is not null && record.RevokedAt is null)
            {
                record.RevokedAt = at;
                _events.Add(revoked);
            }

            return Task.FromResult(record?.Copy());
        }
    }

    public Task<IReadOnlyList<ConsentEventRecord>> EventsAsync(Guid subjectUserId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ConsentEventRecord>>([.. _events.Where(x => x.SubjectUserId == subjectUserId)]);
        }
    }
}

public sealed class ConsentService(IConsentStore store, IAuditWriter audit, IClock clock) : IConsentService, IConsentEvaluator, IPurposeConsentEvaluator, IDemoConsentSeeder
{
    private static readonly string[] Purposes =
    [
        ConsentPurposes.Treatment, ConsentPurposes.MedicationReview, ConsentPurposes.Dispensing, ConsentPurposes.Research,
        ConsentPurposes.InsuranceSharing, ConsentPurposes.ManufacturerReport, ConsentPurposes.Monitoring, ConsentPurposes.AiProcessing, ConsentPurposes.AiExternalProcessing,
    ];

    private static readonly string[] Scopes =
    [
        DataScopes.Profile, DataScopes.Medications, DataScopes.Prescriptions, DataScopes.Adherence, DataScopes.Adr, DataScopes.Checkins, DataScopes.Symptoms, DataScopes.AiSummary,
        DataScopes.Conditions, DataScopes.Allergies, DataScopes.Products,
    ];

    /// <summary>A consent may not run longer than this (forces periodic re-confirmation).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(366);

    public async Task<ConsentOutcome> GrantAsync(Guid actorUserId, GrantConsentCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        ConsentError error = ConsentError.None;
        var granteeFree = ConsentPurposes.GranteeFree.Contains(command.Purpose);
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
        else if (granteeFree
            ? command.GranteeUserId is not null || command.GranteeOrganizationId is not null // a purpose consent names no person or organization
            : (command.GranteeUserId is null) == (command.GranteeOrganizationId is null) || command.GranteeUserId == command.SubjectUserId) // exactly one grantee, and not yourself
        {
            error = ConsentError.InvalidGrantee;
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
            Id = Guid.CreateVersion7(),
            SubjectUserId = command.SubjectUserId,
            GranteeUserId = command.GranteeUserId,
            GranteeOrganizationId = command.GranteeOrganizationId,
            Purpose = command.Purpose,
            Scope = [.. command.Scope.Distinct()],
            GrantedAt = now,
            ExpiresAt = command.ExpiresAt,
            Version = command.Version,
        };
        await store.AddAsync(record, new ConsentEventRecord { ConsentId = record.Id, SubjectUserId = record.SubjectUserId, Kind = ConsentEventKind.Granted, At = now, ActorUserId = actorUserId, Purpose = record.Purpose }, ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.ConsentGranted, AuditResult.Success, actorUserId, "consent", record.Id.ToString(), command.SubjectUserId, source, correlationId,
            Metadata: new Dictionary<string, string> { ["purpose"] = command.Purpose, ["scope"] = string.Join(',', record.Scope) }), ct);
        return new ConsentOutcome(record.ToDto(now), ConsentError.None);
    }

    public async Task<ConsentOutcome> RevokeAsync(Guid actorUserId, Guid consentId, string source, string? correlationId, CancellationToken ct = default)
    {
        var existing = await store.FindAsync(consentId, ct);
        // Same outcome for "not found" and "not yours": no probing of other people's consents.
        if (existing is null || existing.SubjectUserId != actorUserId)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.ConsentRevoked, AuditResult.Denied, actorUserId, "consent", consentId.ToString(), null, source, correlationId, "not_found_or_not_subject"), ct);
            return new ConsentOutcome(null, ConsentError.NotFound);
        }

        var now = clock.UtcNow;
        var revoked = await store.RevokeAsync(consentId, now, new ConsentEventRecord { ConsentId = consentId, SubjectUserId = actorUserId, Kind = ConsentEventKind.Revoked, At = now, ActorUserId = actorUserId, Purpose = existing.Purpose }, ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.ConsentRevoked, AuditResult.Success, actorUserId, "consent", consentId.ToString(), existing.SubjectUserId, source, correlationId), ct);
        return new ConsentOutcome(revoked!.ToDto(now), ConsentError.None);
    }

    public async Task<IReadOnlyList<ConsentDto>> ListGivenAsync(Guid subjectUserId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return [.. (await store.ForSubjectAsync(subjectUserId, ct)).OrderByDescending(x => x.GrantedAt).Select(x => x.ToDto(now))];
    }

    public async Task<IReadOnlyList<ConsentEventDto>> HistoryAsync(Guid subjectUserId, CancellationToken ct = default) =>
        [.. (await store.EventsAsync(subjectUserId, ct)).OrderByDescending(x => x.At).ThenByDescending(x => x.Kind).Select(x => x.ToDto())];

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

    public async Task<ConsentDecision> EvaluateAsync(PurposeConsentCheck check, CancellationToken ct = default)
    {
        if (!ConsentPurposes.GranteeFree.Contains(check.Purpose))
        {
            return new ConsentDecision(false, null, "not_a_purpose_consent");
        }

        var now = clock.UtcNow;
        var candidates = (await store.ForSubjectAsync(check.SubjectUserId, ct)).Where(x => x.Purpose == check.Purpose && x.GranteeUserId is null && x.GranteeOrganizationId is null).ToList();
        if (candidates.Count == 0)
        {
            return new ConsentDecision(false, null, "no_consent");
        }

        var match = candidates.Where(x => x.StatusAt(now) == ConsentStatus.Active && check.RequiredScopes.All(x.Scope.Contains)).OrderByDescending(x => x.GrantedAt).FirstOrDefault();
        if (match is not null)
        {
            return new ConsentDecision(true, match.Id, "allowed");
        }

        return new ConsentDecision(false, null, candidates.Any(x => x.StatusAt(now) == ConsentStatus.Active) ? "scope_mismatch" : candidates.Any(x => x.StatusAt(now) == ConsentStatus.Revoked) ? "consent_revoked" : "consent_expired");
    }

    public async Task SeedAsync(IEnumerable<ConsentDto> consents, CancellationToken ct = default)
    {
        foreach (var c in consents)
        {
            if (await store.ExistsAsync(c.Id, ct))
            {
                continue; // demo seeding is repeatable: ids are deterministic and a patient's later revocation is never undone
            }

            var granted = new ConsentEventRecord { ConsentId = c.Id, SubjectUserId = c.SubjectUserId, Kind = ConsentEventKind.Granted, At = c.GrantedAt, ActorUserId = c.SubjectUserId, Purpose = c.Purpose };
            await store.AddAsync(new ConsentRecord
            {
                Id = c.Id, SubjectUserId = c.SubjectUserId, GranteeUserId = c.GranteeUserId, GranteeOrganizationId = c.GranteeOrganizationId,
                Purpose = c.Purpose, Scope = c.Scope, GrantedAt = c.GrantedAt, ExpiresAt = c.ExpiresAt ?? c.GrantedAt.AddDays(1), RevokedAt = c.RevokedAt, Version = c.Version,
            }, granted, ct);
        }
    }
}
