using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Modules.ProductTrace.Contracts;

public enum TraceError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

/// <param name="Detail">Machine-readable code(s) separated by ';' (never free text, never internals).</param>
public sealed record TraceOutcome<T>(T? Value, TraceError Error, string? Detail = null)
{
    public bool Succeeded => Error == TraceError.None;
}

public static class TraceOutcome
{
    public static TraceOutcome<T> Ok<T>(T value) => new(value, TraceError.None);

    public static TraceOutcome<T> Fail<T>(TraceError error, string? detail = null) => new(default, error, detail);
}

// ---------- dispensed product records (batch / lot) ----------

/// <summary>How the record was entered. Only <see cref="Manual"/> exists today; the others are connection points for later phases.</summary>
public enum EntryMethod
{
    Manual,
    BarcodeScan,
    ExternalSystem,
}

public enum ProductVerification
{
    SelfReported,
    ProfessionalConfirmed,
}

/// <summary>How the entry compares with the medication reference. "ReferenceUnknown" means there was nothing to compare with.</summary>
public enum ProductConsistency
{
    ReferenceUnknown,
    Consistent,
    Mismatch,
}

public enum ProductRecorder
{
    Patient,
    Pharmacist,
    Physician,
}

public sealed record ProductRecordDto(
    Guid Id,
    Guid SubjectId,
    Guid? PatientMedicationId,
    Guid? MedicationId,
    string ProductName,
    LocalizedText? ReferenceName,
    string? GenericName,
    Guid? ManufacturerId,
    string? ManufacturerName,
    string BatchNumber,
    DateOnly? ManufactureDate,
    DateOnly ExpiryDate,
    bool Expired,
    string? Gtin,
    string? PharmacyNote,
    DateOnly ReceivedOn,
    EntryMethod Method,
    ProductVerification Verification,
    ProductConsistency Consistency,
    IReadOnlyList<string> Findings,
    ProductRecorder RecordedBy,
    DateTimeOffset UpdatedAt,
    int Version,
    bool IsDemo,
    string? Notice);

/// <param name="Gtin">Optional. Validated (length and check digit) but never generated, guessed or looked up.</param>
/// <param name="PharmacyNote">Placeholder: free text such as the pharmacy's name. There is no pharmacy directory yet.</param>
public sealed record ProductRecordInput(
    Guid? PatientMedicationId,
    Guid? MedicationId,
    string? ProductName,
    string? GenericName,
    Guid? ManufacturerId,
    string? ManufacturerName,
    string BatchNumber,
    DateOnly? ManufactureDate,
    DateOnly ExpiryDate,
    string? Gtin,
    string? PharmacyNote,
    DateOnly ReceivedOn,
    EntryMethod Method = EntryMethod.Manual,
    int? ExpectedVersion = null);

public interface IProductTraceService
{
    Task<TraceOutcome<IReadOnlyList<ProductRecordDto>>> ListAsync(Guid subjectId, CancellationToken ct = default);
    Task<TraceOutcome<ProductRecordDto>> GetAsync(Guid subjectId, Guid id, CancellationToken ct = default);
    Task<TraceOutcome<ProductRecordDto>> RecordAsync(Guid actorUserId, ProductRecorder recorder, Guid subjectId, ProductRecordInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<TraceOutcome<ProductRecordDto>> UpdateAsync(Guid actorUserId, ProductRecorder recorder, Guid subjectId, Guid id, ProductRecordInput input, string reason, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>A pharmacist or physician confirms a record against the physical package.</summary>
    Task<TraceOutcome<ProductRecordDto>> ConfirmAsync(Guid actorUserId, ProductRecorder recorder, Guid subjectId, Guid id, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);
    Task<TraceOutcome<bool>> RemoveAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default);
    Task<TraceOutcome<IReadOnlyList<RecordVersionInfo>>> VersionsAsync(Guid subjectId, Guid id, CancellationToken ct = default);
}

public sealed record RecordVersionInfo(int Version, DateTimeOffset At, Guid? ChangedBy, string Reason);

// ---------- barcode / QR connection point (not implemented: no camera, no external service) ----------

public sealed record ScanResult(string? Gtin, string? BatchNumber, DateOnly? ExpiryDate);

/// <summary>Where a barcode/QR reader or an external system will plug in. Today only the "not available" implementation exists.</summary>
public interface IProductScanProvider
{
    bool IsAvailable { get; }

    Task<ScanResult?> ParseAsync(string rawCode, CancellationToken ct = default);
}

// ---------- manufacturer safety reports ----------

public enum ReportStatus
{
    Draft,
    PendingConsentOrReview,
    ReadyToSend,
    Sent,
    Acknowledged,
    Failed,
    Cancelled,
}

public enum ReportIssueType
{
    AdverseEvent,
    AbnormalAppearanceOrPackaging,
    ApparentLackOfEffect,
    QualityProblem,
    Other,
}

public enum ReportSeverity
{
    Unknown,
    Mild,
    Moderate,
    Severe,
}

public enum ReviewDecision
{
    Approve,
    Reject,
}

/// <summary>
/// EVERYTHING a manufacturer may receive. It has no field for a name, account id, national id, contact data, birth date, address or
/// patient/record identifier; the report reference is a random value. A test enforces that this list stays identity-free.
/// </summary>
public sealed record ManufacturerReportPayload(
    string ReportReference,
    string ProductName,
    string? GenericName,
    string? ManufacturerName,
    string BatchNumber,
    DateOnly? ManufactureDate,
    DateOnly ExpiryDate,
    string? Gtin,
    string IssueType,
    string Severity,
    DateOnly OccurredOn,
    int? DurationOfUseDays,
    string AgeGroup,
    string SexGroup,
    IReadOnlyList<string> ConcomitantMedications,
    string? Description,
    bool IsDemo,
    string SchemaVersion);

public sealed record ManufacturerReportDto(
    Guid Id,
    Guid SubjectId,
    Guid ProductRecordId,
    string ProductName,
    string BatchNumber,
    ReportStatus Status,
    ReportIssueType IssueType,
    ReportSeverity Severity,
    DateOnly OccurredOn,
    int? DurationOfUseDays,
    string? Description,
    bool IncludeConcomitantMedications,
    bool ReviewRequired,
    bool ConsentActive,
    string? ReviewerNote,
    ReviewDecision? ReviewDecision,
    string? FailureCode,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? AcknowledgedAt,
    bool IsMockDelivery,
    int Version,
    bool IsDemo,
    string? Notice,
    /// <summary>What the manufacturer would receive, shown to the patient and the reviewer. Null while a draft is incomplete.</summary>
    ManufacturerReportPayload? PayloadPreview);

public sealed record ReportDraftInput(
    Guid ProductRecordId,
    ReportIssueType IssueType,
    ReportSeverity Severity,
    DateOnly OccurredOn,
    int? DurationOfUseDays,
    string? Description,
    bool IncludeConcomitantMedications,
    string? ClientRequestId = null,
    int? ExpectedVersion = null);

public sealed record ReviewReportCommand(ReviewDecision Decision, string? Note, int ExpectedVersion);

public interface IManufacturerReportService
{
    Task<TraceOutcome<ManufacturerReportDto>> CreateDraftAsync(Guid actorUserId, Guid subjectId, ReportDraftInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<TraceOutcome<ManufacturerReportDto>> UpdateDraftAsync(Guid actorUserId, Guid subjectId, Guid id, ReportDraftInput input, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>The patient submits: consent is required; the report then waits for review or is queued for sending, as the review policy says.</summary>
    Task<TraceOutcome<ManufacturerReportDto>> SubmitAsync(Guid actorUserId, Guid subjectId, Guid id, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>A pharmacist or physician approves (queues the report) or rejects it (back to draft with a note).</summary>
    Task<TraceOutcome<ManufacturerReportDto>> ReviewAsync(Guid reviewerUserId, Guid subjectId, Guid id, ReviewReportCommand command, string source, string? correlationId, CancellationToken ct = default);
    Task<TraceOutcome<ManufacturerReportDto>> CancelAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default);
    Task<TraceOutcome<IReadOnlyList<ManufacturerReportDto>>> ListAsync(Guid subjectId, CancellationToken ct = default);
    Task<TraceOutcome<ManufacturerReportDto>> GetAsync(Guid subjectId, Guid id, CancellationToken ct = default);

    /// <summary>Reports of these patients that wait for review (used by reviewers' inboxes; authorization is done per patient by the caller).</summary>
    Task<IReadOnlyList<ManufacturerReportDto>> PendingReviewAsync(IReadOnlyCollection<Guid> subjectIds, CancellationToken ct = default);
}

// ---------- outbox + provider ----------

public enum OutboxState
{
    Pending,
    InProgress,
    Sent,
    Failed,
    Cancelled,
}

/// <summary>Queue entry without any clinical content: what an operator needs to see to run the queue.</summary>
public sealed record OutboxEntryDto(Guid Id, Guid ReportId, OutboxState State, int Attempts, DateTimeOffset NextAttemptAt, string? LastErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

public sealed record ReportQueueDto(
    IReadOnlyDictionary<string, int> ReportsByStatus,
    IReadOnlyDictionary<string, int> OutboxByState,
    IReadOnlyList<OutboxEntryDto> Entries,
    string Provider,
    bool ProviderIsMock,
    bool ProviderConfigured,
    string Notice);

public sealed record ProcessQueueResult(int Considered, int Sent, int Retrying, int Failed, int Blocked, string? Skipped);

public interface IManufacturerReportOutbox
{
    Task<ReportQueueDto> GetQueueAsync(CancellationToken ct = default);

    /// <summary>Runs one pass over the due outbox entries. Safe to call repeatedly and from several instances.</summary>
    Task<ProcessQueueResult> ProcessDueAsync(Guid actorUserId, int max, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Puts a failed report back in the queue (a person decided to retry).</summary>
    Task<TraceOutcome<bool>> RetryFailedAsync(Guid actorUserId, Guid reportId, string source, string? correlationId, CancellationToken ct = default);
}

public enum SubmissionOutcome
{
    Accepted,
    TransientFailure,
    PermanentFailure,
    NotConfigured,
}

public sealed record ReportSubmissionResult(SubmissionOutcome Outcome, string? ExternalReference, string? ErrorCode);

/// <summary>
/// Delivery to a manufacturer or authority. Today only the fictional mock exists: a real connection needs an agreement and authorization
/// with that party, and its own review of what data may be sent.
/// </summary>
public interface IManufacturerReportProvider
{
    string Name { get; }

    bool IsMock { get; }

    bool IsConfigured { get; }

    /// <param name="idempotencyKey">Stable per report: sending the same key twice must not create two reports at the receiver.</param>
    Task<ReportSubmissionResult> SubmitAsync(ManufacturerReportPayload payload, string idempotencyKey, CancellationToken ct = default);
}
