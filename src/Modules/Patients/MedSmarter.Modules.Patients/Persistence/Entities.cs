using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Modules.Patients.Persistence;

// Persistence model of the patient layer (schema "patients"). Reads and writes go through PatientsDbContext only.
// Every clinical row belongs to ONE patient (PatientId, the internal id). The account link (UserId) lives only on Patient, so analytics can use
// PatientId without ever seeing who the person is.

public sealed class Patient
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public PatientStatus Status { get; set; } = PatientStatus.Active;
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}

public sealed class ProfileRecord
{
    public Guid PatientId { get; set; }
    public int? YearOfBirth { get; set; }
    public SexGroup? Sex { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    public string TimeZone { get; set; } = "Asia/Tehran";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ConditionRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly? OnsetDate { get; set; }
    public ConditionStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class AllergyRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public AllergenKind Kind { get; set; }
    public Guid? MedicationId { get; set; }
    public string Substance { get; set; } = string.Empty;
    public AllergySeverity Severity { get; set; }
    public string? Reaction { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class MedicationRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>Link to the medication reference (Phase 4). Null = "unregistered / needs validation": the name below is what the patient typed.</summary>
    public Guid? MedicationId { get; set; }
    public string? UnregisteredName { get; set; }
    public decimal? DoseAmount { get; set; }
    public string? DoseUnit { get; set; }
    public string? DoseText { get; set; }
    public FrequencyKind Frequency { get; set; }
    public int? FrequencyValue { get; set; }
    public string? Route { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public PrescriberSource Source { get; set; }
    public string? PrescriberNote { get; set; }
    public PatientMedicationStatus Status { get; set; }
    public string? StopReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class ScheduleEntryRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PatientMedicationId { get; set; }
    public TimeOnly TimeOfDay { get; set; }
    /// <summary>Bit 0 = Sunday ... bit 6 = Saturday (System.DayOfWeek values).</summary>
    public int DaysMask { get; set; } = 127;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class IntakeLogRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PatientMedicationId { get; set; }
    public Guid? ScheduleEntryId { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public IntakeStatus Status { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid? RecordedBy { get; set; }
}

public sealed class SymptomRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Text { get; set; } = string.Empty;
    public SymptomSeverity Severity { get; set; }
    public DateTimeOffset OnsetAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? PatientMedicationId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid? RecordedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>PatientDataSnapshotMeta: when each data category of a patient was last updated.</summary>
public sealed class DataCategoryRecord
{
    public Guid PatientId { get; set; }
    public PatientDataCategory Category { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public Guid? LastUpdatedBy { get; set; }
    public int RecordCount { get; set; }
}

/// <summary>Append-only history of a record: the full state after each change.</summary>
public sealed class RecordVersionRow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ExternalIdentifierRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public ExternalIdentifierScheme Scheme { get; set; }
    /// <summary>HMAC-SHA256 of the identifier with a server-side key (hex). The identifier itself is never stored.</summary>
    public string ValueHash { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public Guid? LinkedBy { get; set; }
}

public sealed class CareRelationshipRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PatientUserId { get; set; }
    public Guid? ProviderUserId { get; set; }
    public Guid? ProviderOrganizationId { get; set; }
    public string Kind { get; set; } = "Treating";
    public CareRelationshipStatus Status { get; set; }
    public string InitiatedBy { get; set; } = "Patient";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? PatientConsentedAt { get; set; }
    public DateTimeOffset? ProviderConsentedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? EndedBy { get; set; }
    public string? EndReason { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class CareRelationshipEventRecord
{
    public Guid Id { get; set; }
    public Guid RelationshipId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Note { get; set; }
}
