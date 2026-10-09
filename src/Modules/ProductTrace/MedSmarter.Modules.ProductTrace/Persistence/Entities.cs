using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Modules.ProductTrace.Persistence;

// Schema "product_trace". PatientId is the patient's internal id (Patients module); there is no foreign key across module schemas.

public sealed class ProductRecordRow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? PatientMedicationId { get; set; }
    public Guid? MedicationId { get; set; }
    /// <summary>Snapshot of the name at entry time (the reference can change later).</summary>
    public string ProductName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public Guid? ManufacturerId { get; set; }
    public string? ManufacturerName { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public string BatchNormalized { get; set; } = string.Empty;
    /// <summary>The reference medication id, or the lower-cased name when the product is not in the reference: the duplicate rule uses it.</summary>
    public string ProductKey { get; set; } = string.Empty;
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string? Gtin { get; set; }
    public string? PharmacyNote { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public EntryMethod Method { get; set; }
    public ProductVerification Verification { get; set; }
    public ProductConsistency Consistency { get; set; }
    public string FindingsJson { get; set; } = "[]";
    public ProductRecorder RecordedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsDemo { get; set; }
}

public sealed class ProductVersionRow
{
    public Guid Id { get; set; }
    public Guid ProductRecordId { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReportRow
{
    public Guid Id { get; set; }
    /// <summary>Random public reference sent to the manufacturer. Not derived from any identifier of the patient.</summary>
    public string Reference { get; set; } = string.Empty;
    public Guid PatientId { get; set; }
    public Guid ProductRecordId { get; set; }
    public ReportStatus Status { get; set; }
    public ReportIssueType IssueType { get; set; }
    public ReportSeverity Severity { get; set; }
    public DateOnly OccurredOn { get; set; }
    public int? DurationOfUseDays { get; set; }
    public string? Description { get; set; }
    public bool IncludeConcomitant { get; set; }
    public bool ReviewRequired { get; set; }
    public string? ReviewerNote { get; set; }
    public ReviewDecision? ReviewDecision { get; set; }
    public Guid? ReviewedBy { get; set; }
    public Guid? ConsentId { get; set; }
    public string? FailureCode { get; set; }
    public int Attempts { get; set; }
    public string? ExternalReference { get; set; }
    public bool IsMockDelivery { get; set; }
    /// <summary>The de-identified payload frozen when the report became ready to send; what is sent is exactly this.</summary>
    public string? PayloadJson { get; set; }
    public string? PayloadHash { get; set; }
    public string? ClientRequestId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public int Version { get; set; } = 1;
    public bool IsDemo { get; set; }
}

public sealed class OutboxRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public OutboxState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ReportEventRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Note { get; set; }
}
