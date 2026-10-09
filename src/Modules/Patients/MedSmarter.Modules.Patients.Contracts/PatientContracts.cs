using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Modules.Patients.Contracts;

// ---------- result ----------

public enum PatientError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

/// <param name="Detail">Machine-readable code(s) separated by ';' (never free text, never internals).</param>
public sealed record PatientOutcome<T>(T? Value, PatientError Error, string? Detail = null)
{
    public bool Succeeded => Error == PatientError.None;
}

public static class PatientOutcome
{
    public static PatientOutcome<T> Ok<T>(T value) => new(value, PatientError.None);

    public static PatientOutcome<T> Fail<T>(PatientError error, string? detail = null) => new(default, error, detail);
}

// ---------- vocabulary ----------

public enum PatientStatus
{
    Active,
    Inactive,
}

/// <summary>Biological-sex grouping used for dosing context only. "Undisclosed" is a valid, respected answer.</summary>
public enum SexGroup
{
    Female,
    Male,
    Other,
    Undisclosed,
}

public enum PatientDataCategory
{
    Profile,
    Conditions,
    Allergies,
    Medications,
    Schedule,
    Intake,
    Symptoms,
    Products,
}

public enum ConditionStatus
{
    Active,
    Resolved,
    Unknown,
}

public enum AllergenKind
{
    Medication,
    ActiveIngredient,
    Other,
}

public enum AllergySeverity
{
    Unknown,
    Mild,
    Moderate,
    Severe,
}

public enum FrequencyKind
{
    TimesPerDay,
    EveryNHours,
    AsNeeded,
    Other,
}

public enum PrescriberSource
{
    Unknown,
    Physician,
    Pharmacist,
    SelfReported,
}

public enum PatientMedicationStatus
{
    Active,
    Paused,
    Stopped,
}

public enum IntakeStatus
{
    Taken,
    Skipped,
}

public enum SymptomSeverity
{
    Mild,
    Moderate,
    Severe,
}

// ---------- patient + profile ----------

/// <param name="SubjectId">The platform person id (the account's user id): the key used by every patient route and by authorization.</param>
public sealed record PatientDto(Guid SubjectId, PatientStatus Status, bool IsDemo, string? Notice, DateTimeOffset CreatedAt);

public sealed record PatientProfileDto(
    Guid SubjectId,
    int? YearOfBirth,
    int? AgeYears,
    SexGroup? Sex,
    decimal? WeightKg,
    decimal? HeightCm,
    string TimeZone,
    DateTimeOffset UpdatedAt,
    int Version,
    bool IsDemo,
    string? Notice);

/// <param name="ExpectedVersion">Null for the first save; otherwise the version the client last read (optimistic locking).</param>
public sealed record UpdateProfileCommand(int? YearOfBirth, SexGroup? Sex, decimal? WeightKg, decimal? HeightCm, string? TimeZone, int? ExpectedVersion);

public sealed record DataFreshnessDto(PatientDataCategory Category, DateTimeOffset? LastUpdatedAt, int RecordCount, bool NeverRecorded, bool IsStale, int StaleAfterDays);

// ---------- conditions / allergies ----------

public sealed record ConditionDto(Guid Id, string Name, DateOnly? OnsetDate, ConditionStatus Status, string? Note, DateTimeOffset UpdatedAt, int Version);

public sealed record ConditionInput(string Name, DateOnly? OnsetDate, ConditionStatus Status, string? Note, int? ExpectedVersion = null);

public sealed record AllergyDto(Guid Id, AllergenKind Kind, Guid? MedicationId, string Substance, AllergySeverity Severity, string? Reaction, DateTimeOffset UpdatedAt, int Version);

/// <param name="MedicationId">For <see cref="AllergenKind.Medication"/>: the reference medication (checked); the name is then taken from the reference. Otherwise null.</param>
public sealed record AllergyInput(AllergenKind Kind, Guid? MedicationId, string? Substance, AllergySeverity Severity, string? Reaction, int? ExpectedVersion = null);

// ---------- medications ----------

/// <param name="IsRegistered">False = not in the medication reference ("unregistered, needs validation"): never guessed or matched automatically.</param>
public sealed record PatientMedicationDto(
    Guid Id,
    Guid? MedicationId,
    string DisplayName,
    LocalizedText? ReferenceName,
    bool IsRegistered,
    bool ReferenceIsDemo,
    decimal? DoseAmount,
    string? DoseUnit,
    string? DoseText,
    FrequencyKind Frequency,
    int? FrequencyValue,
    string? Route,
    DateOnly StartDate,
    DateOnly? EndDate,
    PrescriberSource Source,
    string? PrescriberNote,
    PatientMedicationStatus Status,
    string? StopReason,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record PatientMedicationInput(
    Guid? MedicationId,
    string? UnregisteredName,
    decimal? DoseAmount,
    string? DoseUnit,
    string? DoseText,
    FrequencyKind Frequency,
    int? FrequencyValue,
    string? Route,
    DateOnly StartDate,
    DateOnly? EndDate,
    PrescriberSource Source,
    string? PrescriberNote,
    int? ExpectedVersion = null);

public sealed record StopMedicationCommand(string? Reason, DateOnly? EndDate, int ExpectedVersion);

public sealed record RecordVersionDto(int Version, DateTimeOffset At, Guid? ChangedBy, string Reason);

// ---------- schedule + intake ----------

public sealed record ScheduleEntryDto(Guid Id, Guid PatientMedicationId, TimeOnly TimeOfDay, IReadOnlyList<DayOfWeek> Days, DateOnly StartDate, DateOnly? EndDate, bool Active);

public sealed record ScheduleEntryInput(TimeOnly TimeOfDay, IReadOnlyList<DayOfWeek>? Days, DateOnly? StartDate, DateOnly? EndDate);

public sealed record DoseSlotDto(
    Guid ScheduleEntryId,
    Guid PatientMedicationId,
    string MedicationName,
    DateOnly LocalDate,
    TimeOnly LocalTime,
    DateTimeOffset ScheduledFor,
    IntakeStatus? Status,
    Guid? LogId);

public sealed record IntakeLogDto(Guid Id, Guid PatientMedicationId, Guid? ScheduleEntryId, DateTimeOffset? ScheduledFor, IntakeStatus Status, DateTimeOffset? TakenAt, string? Note, DateTimeOffset RecordedAt);

public sealed record LogIntakeCommand(Guid PatientMedicationId, Guid? ScheduleEntryId, DateTimeOffset? ScheduledFor, IntakeStatus Status, DateTimeOffset? TakenAt, string? Note);

// ---------- symptoms ----------

public sealed record SymptomReportDto(Guid Id, string Text, SymptomSeverity Severity, DateTimeOffset OnsetAt, DateTimeOffset? ResolvedAt, Guid? PatientMedicationId, string? Note, DateTimeOffset RecordedAt);

public sealed record SymptomInput(string Text, SymptomSeverity Severity, DateTimeOffset OnsetAt, DateTimeOffset? ResolvedAt, Guid? PatientMedicationId, string? Note);

// ---------- external identifiers (mapping only; no value is ever shown again) ----------

public enum ExternalIdentifierScheme
{
    NationalId,
    InsuranceMemberId,
    ElectronicPrescriptionId,
    Other,
}

public sealed record ExternalIdentifierDto(Guid Id, ExternalIdentifierScheme Scheme, bool Verified, DateTimeOffset LinkedAt);

// ---------- services ----------

/// <summary>
/// Patients, profile, conditions, allergies, symptoms and data freshness. Callers must already have been authorized at the API edge
/// (RBAC + ownership/relationship + consent); every method audits. Writes are versioned and deletes are soft.
/// </summary>
public interface IPatientService
{
    Task<PatientOutcome<PatientDto>> GetAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>The patient record of the caller's own account, created on first use (patients create their own record).</summary>
    Task<PatientOutcome<PatientDto>> EnsureOwnAsync(Guid subjectId, string source, string? correlationId, CancellationToken ct = default);

    Task<PatientOutcome<PatientProfileDto>> GetProfileAsync(Guid subjectId, CancellationToken ct = default);
    Task<PatientOutcome<PatientProfileDto>> UpdateProfileAsync(Guid actorUserId, Guid subjectId, UpdateProfileCommand command, string source, string? correlationId, CancellationToken ct = default);

    Task<PatientOutcome<IReadOnlyList<ConditionDto>>> ListConditionsAsync(Guid subjectId, CancellationToken ct = default);
    Task<PatientOutcome<ConditionDto>> AddConditionAsync(Guid actorUserId, Guid subjectId, ConditionInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<ConditionDto>> UpdateConditionAsync(Guid actorUserId, Guid subjectId, Guid id, ConditionInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<bool>> RemoveConditionAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default);

    Task<PatientOutcome<IReadOnlyList<AllergyDto>>> ListAllergiesAsync(Guid subjectId, CancellationToken ct = default);
    Task<PatientOutcome<AllergyDto>> AddAllergyAsync(Guid actorUserId, Guid subjectId, AllergyInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<AllergyDto>> UpdateAllergyAsync(Guid actorUserId, Guid subjectId, Guid id, AllergyInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<bool>> RemoveAllergyAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default);

    Task<PatientOutcome<IReadOnlyList<SymptomReportDto>>> ListSymptomsAsync(Guid subjectId, int take, CancellationToken ct = default);
    Task<PatientOutcome<SymptomReportDto>> AddSymptomAsync(Guid actorUserId, Guid subjectId, SymptomInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<bool>> RemoveSymptomAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>When each category was last updated, so every screen can say how old the data is.</summary>
    Task<PatientOutcome<IReadOnlyList<DataFreshnessDto>>> GetFreshnessAsync(Guid subjectId, CancellationToken ct = default);

    Task<PatientOutcome<IReadOnlyList<ExternalIdentifierDto>>> ListExternalIdentifiersAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Maps an external identifier to the patient. Only a keyed hash of the value is stored; the value cannot be read back.</summary>
    Task<PatientOutcome<ExternalIdentifierDto>> LinkExternalIdentifierAsync(Guid actorUserId, Guid subjectId, ExternalIdentifierScheme scheme, string value, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Finds the patient mapped to an external identifier (null when none). Never matches by name.</summary>
    Task<Guid?> FindSubjectByExternalIdentifierAsync(ExternalIdentifierScheme scheme, string value, CancellationToken ct = default);

    /// <summary>Deactivation hides the record from professionals; data is kept until the retention period ends.</summary>
    Task<PatientOutcome<PatientDto>> SetStatusAsync(Guid actorUserId, Guid subjectId, PatientStatus status, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Physically removes records that were soft-deleted before <paramref name="olderThan"/> (retention policy). Returns the number of rows removed.</summary>
    Task<int> PurgeDeletedAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}

public interface IPatientMedicationService
{
    Task<PatientOutcome<IReadOnlyList<PatientMedicationDto>>> ListAsync(Guid subjectId, bool includeStopped, CancellationToken ct = default);
    Task<PatientOutcome<PatientMedicationDto>> GetAsync(Guid subjectId, Guid id, CancellationToken ct = default);
    Task<PatientOutcome<PatientMedicationDto>> AddAsync(Guid actorUserId, Guid subjectId, PatientMedicationInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<PatientMedicationDto>> UpdateAsync(Guid actorUserId, Guid subjectId, Guid id, PatientMedicationInput input, string reason, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<PatientMedicationDto>> StopAsync(Guid actorUserId, Guid subjectId, Guid id, StopMedicationCommand command, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<PatientMedicationDto>> ResumeAsync(Guid actorUserId, Guid subjectId, Guid id, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<bool>> RemoveAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<IReadOnlyList<RecordVersionDto>>> VersionsAsync(Guid subjectId, Guid id, CancellationToken ct = default);

    Task<PatientOutcome<IReadOnlyList<ScheduleEntryDto>>> ListScheduleAsync(Guid subjectId, Guid medicationId, CancellationToken ct = default);
    Task<PatientOutcome<ScheduleEntryDto>> AddScheduleEntryAsync(Guid actorUserId, Guid subjectId, Guid medicationId, ScheduleEntryInput input, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<bool>> RemoveScheduleEntryAsync(Guid actorUserId, Guid subjectId, Guid entryId, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>The doses planned for one local day with what has been logged for each.</summary>
    Task<PatientOutcome<IReadOnlyList<DoseSlotDto>>> GetDayAsync(Guid subjectId, DateOnly localDate, CancellationToken ct = default);

    /// <summary>Records a taken/skipped dose. Idempotent per (schedule entry, scheduled time): logging the same dose again updates it.</summary>
    Task<PatientOutcome<IntakeLogDto>> LogIntakeAsync(Guid actorUserId, Guid subjectId, LogIntakeCommand command, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<IReadOnlyList<IntakeLogDto>>> ListIntakeAsync(Guid subjectId, DateTimeOffset fromTime, DateTimeOffset untilTime, CancellationToken ct = default);
}

// ---------- care relationships (two-sided consent) ----------

public enum CareRelationshipStatus
{
    PendingProvider,
    PendingPatient,
    Active,
    Declined,
    Ended,
}

public sealed record CareRelationshipDto(
    Guid Id,
    Guid PatientSubjectId,
    Guid? ProviderUserId,
    string Kind,
    CareRelationshipStatus Status,
    string InitiatedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? PatientConsentedAt,
    DateTimeOffset? ProviderConsentedAt,
    DateTimeOffset? EndedAt,
    string? EndedBy,
    string? EndReason,
    Guid? ProviderOrganizationId = null);

public sealed record RequestCareRelationshipCommand(Guid CounterpartUserId, string Kind);

public interface ICareRelationshipService
{
    /// <summary>A patient asks a physician/pharmacist, or a provider asks a patient. Nothing is shared until the other side accepts.</summary>
    Task<PatientOutcome<CareRelationshipDto>> RequestAsync(Guid actorUserId, RequestCareRelationshipCommand command, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<CareRelationshipDto>> AcceptAsync(Guid actorUserId, Guid id, string source, string? correlationId, CancellationToken ct = default);
    Task<PatientOutcome<CareRelationshipDto>> DeclineAsync(Guid actorUserId, Guid id, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Either side can end an active relationship (or the requester can withdraw a pending one) at any time. Access stops on the next request.</summary>
    Task<PatientOutcome<CareRelationshipDto>> EndAsync(Guid actorUserId, Guid id, string? reason, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>The caller's relationships, as patient and as provider, newest first.</summary>
    Task<IReadOnlyList<CareRelationshipDto>> ListMineAsync(Guid actorUserId, CancellationToken ct = default);
}

// ---------- patient context for AI (contract only; Phase 6/7 consume it) ----------

public enum PatientContextPurpose
{
    AiAssistant,
    Monitoring,
}

public sealed record ContextMedication(Guid? MedicationId, string Name, string? Dose, string Frequency, string Status, bool IsRegistered);

public sealed record ContextAllergy(string Substance, string Severity, string? Reaction);

public sealed record ContextCondition(string Name, string Status);

public sealed record ContextSymptom(string Text, string Severity, DateOnly OnsetDate);

public sealed record ExcludedCategory(PatientDataCategory Category, string Reason);

/// <summary>
/// The only patient information an AI component may receive. It is built from consent-covered categories only and is minimised: an age
/// band instead of a birth year, no name, no account id, no contact data, no free-text notes, dates reduced to days. A language model
/// never reads the database; it gets this object.
/// </summary>
public sealed record PatientContext(
    string AgeGroup,
    string SexGroup,
    IReadOnlyList<ContextMedication> Medications,
    IReadOnlyList<ContextAllergy> Allergies,
    IReadOnlyList<ContextCondition> Conditions,
    IReadOnlyList<ContextSymptom> RecentSymptoms,
    IReadOnlyDictionary<string, DateTimeOffset?> LastUpdated,
    IReadOnlyList<string> ConsentedPurposes,
    IReadOnlyList<ExcludedCategory> Excluded,
    bool ExternalProcessingConsented,
    string Notice);

public interface IPatientContextService
{
    /// <summary>Consent-filtered, minimised context. Categories without consent are listed in <see cref="PatientContext.Excluded"/> and carry no data.</summary>
    Task<PatientOutcome<PatientContext>> BuildAsync(Guid subjectId, PatientContextPurpose purpose, CancellationToken ct = default);
}

// ---------- directory ----------

/// <summary>Lets other modules find out whether a subject has an active patient record, and its internal id, without seeing patient data.</summary>
public sealed record PatientRecordRef(Guid PatientId, Guid SubjectId, PatientStatus Status, int? YearOfBirth, SexGroup? Sex);

public interface IPatientDirectory
{
    Task<PatientRecordRef?> FindBySubjectAsync(Guid subjectId, CancellationToken ct = default);
    Task<PatientRecordRef?> FindByPatientIdAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>Medications the patient takes that are linked to the reference (used for batch/product consistency checks and reports).</summary>
    Task<IReadOnlyList<PatientMedicationDto>> ActiveMedicationsAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Records that a category changed in another module (products) so freshness stays complete.</summary>
    Task TouchCategoryAsync(Guid subjectId, PatientDataCategory category, Guid? actorUserId, int recordCount, CancellationToken ct = default);
}

/// <summary>Development-only seeding of FICTIONAL patients, records and care relationships. Registered only when demo seeding is enabled.</summary>
public interface IDemoPatientSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public static class AgeBands
{
    /// <summary>The age band used wherever an age is needed without a birth year: "0-17", "18-39", "40-64", "65+" or "unknown".</summary>
    public static string Of(int? yearOfBirth, int currentYear) => yearOfBirth is null ? "unknown" : (currentYear - yearOfBirth) switch
    {
        < 0 => "unknown",
        < 18 => "0-17",
        < 40 => "18-39",
        < 65 => "40-64",
        _ => "65+",
    };
}
