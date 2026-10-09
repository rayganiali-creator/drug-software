using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Integrations.Contracts;

// Insurance integration CONTRACTS (Phase 4: architecture only). No real insurer is connected; the only provider is a fictional mock.
// A real connection needs a contract, authorisation, technical documentation and legal/security review (see docs/phase4/09).

public enum CoverageStatus
{
    Unknown,
    Active,
    Suspended,
    Expired,
}

public enum ImportStatus
{
    Completed,
    CompletedWithIssues,
    Duplicate,
    Failed,
}

public enum ImportRecordOutcome
{
    Applied,
    Unchanged,
    Rejected,
    Conflict,
    Unmapped,
}

/// <summary>An identifier issued by another system for the same person (e.g. an insurer's member id). Not a patient record.</summary>
public sealed record ExternalPatientIdentifier(string System, string Value);

public sealed record InsuranceCoverage(string PlanCode, CoverageStatus Status, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>Minimal on purpose: no name, no national id, no diagnosis. Only what is needed to know coverage.</summary>
public sealed record InsuranceMember(string InsurerCode, string ExternalMemberId, InsuranceCoverage Coverage);

public sealed record InsuranceBatch(string ProviderId, string BatchId, DateTimeOffset ProducedAt, IReadOnlyList<InsuranceMember> Members);

public sealed record InsuranceImportRecordResult(string ExternalMemberId, ImportRecordOutcome Outcome, string? ErrorCode);

/// <summary>Trace of one received batch: who sent it, when, what happened to each record. Contains no personal data beyond opaque ids.</summary>
public sealed record InsuranceDataImport(
    Guid Id,
    string ProviderId,
    string BatchId,
    string ContentHash,
    DateTimeOffset ReceivedAt,
    ImportStatus Status,
    int Total,
    int Applied,
    int Unchanged,
    int Rejected,
    int Conflicts,
    int Unmapped,
    string? ErrorCode,
    IReadOnlyList<InsuranceImportRecordResult> Records,
    Guid ActorUserId);

public enum InsuranceError
{
    None,
    Forbidden,
    NotFound,
    InvalidBatch,
    Conflict,
    ProviderUnavailable,
}

public sealed record InsuranceResult<T>(T? Value, InsuranceError Error, string? Detail = null)
{
    public bool Succeeded => Error == InsuranceError.None;
}

public static class InsuranceResult
{
    public static InsuranceResult<T> Ok<T>(T value) => new(value, InsuranceError.None);
    public static InsuranceResult<T> Fail<T>(InsuranceError error, string? detail = null) => new(default, error, detail);
}

/// <summary>Adapter to one insurer's official channel. A pull-style provider implements this; push-style channels call the service directly.</summary>
public interface IInsuranceProvider
{
    string Id { get; }

    /// <summary>Returns the batch produced since <paramref name="since"/>. Throws on transport problems (the service maps them to a failed import).</summary>
    Task<InsuranceBatch> FetchAsync(DateTimeOffset? since, CancellationToken ct = default);
}

public interface IInsuranceIntegrationService
{
    /// <summary>Requires <c>insurance.import</c>. Validates, de-duplicates and records the batch; never creates patients.</summary>
    Task<InsuranceResult<InsuranceDataImport>> ImportAsync(CurrentUser actor, InsuranceBatch batch, string source, string? correlationId, CancellationToken ct = default);

    Task<InsuranceResult<InsuranceDataImport>> ImportFromProviderAsync(CurrentUser actor, string providerId, DateTimeOffset? since, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Re-processes a stored batch (safe: applying the same data twice changes nothing).</summary>
    Task<InsuranceResult<InsuranceDataImport>> RetryAsync(CurrentUser actor, Guid importId, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Maps an external identifier to an EXISTING internal patient. One external id ↔ one patient; one patient ↔ one id per system.</summary>
    Task<InsuranceResult<ExternalPatientIdentifier>> LinkPatientAsync(CurrentUser actor, ExternalPatientIdentifier external, Guid patientUserId, string source, string? correlationId, CancellationToken ct = default);

    Task<InsuranceResult<IReadOnlyList<InsuranceDataImport>>> ListImportsAsync(CurrentUser actor, CancellationToken ct = default);

    /// <summary>Requires <c>insurance.read</c>.</summary>
    Task<InsuranceResult<InsuranceCoverage>> GetCoverageAsync(CurrentUser actor, Guid patientUserId, string insurerCode, CancellationToken ct = default);
}
