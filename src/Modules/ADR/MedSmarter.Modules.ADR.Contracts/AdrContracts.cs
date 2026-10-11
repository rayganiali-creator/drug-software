namespace MedSmarter.Modules.ADR.Contracts;

// Phase 9: the INTERNAL adverse-event record. It is a record for human review inside this system. It is not a submitted regulatory report, not a national
// report form, and not proof of causality. Every field below is labelled in docs/phase9/03-field-traceability.md as source-derived, existing-system reuse or a
// design proposal. There is deliberately NO seriousness grade, NO causality score and NO regulatory submission schema (Phase 0 decision D-11 is still open).

public enum AdrError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

public sealed record AdrOutcome<T>(T? Value, AdrError Error, string? Detail = null)
{
    public bool Succeeded => Error == AdrError.None;
}

public static class AdrOutcome
{
    public static AdrOutcome<T> Ok<T>(T value) => new(value, AdrError.None);

    public static AdrOutcome<T> Fail<T>(AdrError error, string? detail = null) => new(default, error, detail);
}

/// <summary>Who wrote the record. Set by the server from the caller, never taken from a request body.</summary>
public enum ReporterRole
{
    Patient,
    Physician,
    Pharmacist,
}

/// <summary>Whether the information is the patient's own account or was entered by a professional. Derived from <see cref="ReporterRole"/>.</summary>
public enum ReportOrigin
{
    PatientReported,
    ProfessionalEntered,
}

/// <summary>
/// The only categories a human reviewer may assign. All five are terms DEFINED in the supplementary WHO and FIP documents (see <see cref="AdrDefinitions"/>); none is
/// defined in the lecture slides. Every record starts, and may be returned to, <see cref="Unclassified"/>. "Product-quality defect" and "lack of effect" are
/// deliberately NOT categories here: the supplied materials do not define them.
/// </summary>
public enum EventCategory
{
    Unclassified,
    AdverseDrugReaction,
    AdverseEvent,
    AdverseDrugEvent,
    MedicationError,
    NearMiss,
}

public enum ReviewStatus
{
    Submitted,
    InReview,
    InformationRequested,
    Assessed,
    Closed,
}

public enum EntryKind
{
    /// <summary>Added by the reporter (or a professional) after the original report. The original is never edited.</summary>
    FollowUp,

    /// <summary>A reviewer's note.</summary>
    ReviewNote,

    /// <summary>A reviewer asks the reporter for more information (patient-visible).</summary>
    InformationRequest,

    /// <summary>A reviewer's free-text professional assessment. NOT a seriousness grade and NOT a causality score.</summary>
    ProfessionalAssessment,

    /// <summary>Structured values added later for fields the original left unknown. Shown beside, never instead of, the original.</summary>
    Supplement,

    Classification,
    StatusChange,
}

public enum EntryVisibility
{
    PatientVisible,
    ProfessionalOnly,
}

public enum MedicationReferenceState
{
    /// <summary>Linked to one of the patient's own medication records, which was found when this view was built.</summary>
    Linked,

    /// <summary>Linked when reported, but that record could not be found now (removed or unavailable). The name given at the time is kept.</summary>
    LinkedUnavailable,

    /// <summary>Only a name as the reporter wrote it. Never matched or guessed against the reference.</summary>
    ReportedNameOnly,
}

/// <summary>Status of wording that a clinical reviewer should check. Nothing is "Approved" until a named, qualified reviewer actually approves it.</summary>
public enum WordingStatus
{
    Draft,
    AwaitingClinicalReview,
    Approved,
    Retired,
}

public sealed record SuspectedMedicationInput(Guid? PatientMedicationId, string? NameAsReported);

public sealed record SuspectedMedicationDto(Guid? PatientMedicationId, string? NameAsReported, string? DisplayName, MedicationReferenceState State);

public sealed record CreateAdverseEventInput(
    string EventDescription,
    DateTimeOffset? OnsetAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<SuspectedMedicationInput> SuspectedMedications,
    string? OutcomeAsStated,
    string? AdditionalDetails,
    string? ClientRequestId = null);

/// <summary>Immutable: exactly what was reported, never edited afterwards.</summary>
public sealed record AdverseEventOriginal(
    string EventDescription,
    DateTimeOffset? OnsetAt,
    DateTimeOffset? ResolvedAt,
    string? OutcomeAsStated,
    string? AdditionalDetails);

/// <summary>One value with where it came from, so a later supplement never hides the original.</summary>
public sealed record FieldValue<T>(T Value, string Provenance, Guid? EntryId);

public sealed record AdverseEventEffective(FieldValue<DateTimeOffset?> OnsetAt, FieldValue<DateTimeOffset?> ResolvedAt, FieldValue<string?> OutcomeAsStated);

public sealed record SupplementInput(DateTimeOffset? OnsetAt, DateTimeOffset? ResolvedAt, string? OutcomeAsStated, string? Note);

public sealed record AdverseEventEntryDto(
    Guid Id,
    EntryKind Kind,
    ReporterRole AuthorRole,
    /// <summary>Null in the patient view.</summary>
    Guid? AuthorUserId,
    DateTimeOffset At,
    string? Text,
    EntryVisibility Visibility,
    /// <summary>Who this content comes from: "PatientReported" or "ProfessionalReview". Never an automated source.</summary>
    string Provenance,
    ReviewStatus? FromStatus,
    ReviewStatus? ToStatus,
    EventCategory? FromCategory,
    EventCategory? ToCategory,
    DateTimeOffset? SupplementOnsetAt,
    DateTimeOffset? SupplementResolvedAt,
    string? SupplementOutcomeAsStated);

public sealed record AdverseEventSummaryDto(
    Guid Id,
    Guid SubjectId,
    ReporterRole ReporterRole,
    ReportOrigin Origin,
    DateTimeOffset ReportedAt,
    /// <summary>Null when the viewer is the patient: the category is a professional label and is not shown to the patient.</summary>
    EventCategory? Category,
    ReviewStatus Status,
    DateTimeOffset LastActivityAt,
    string Excerpt);

public sealed record AdverseEventDto(
    Guid Id,
    Guid SubjectId,
    ReporterRole ReporterRole,
    ReportOrigin Origin,
    DateTimeOffset ReportedAt,
    AdverseEventOriginal Original,
    AdverseEventEffective Effective,
    IReadOnlyList<SuspectedMedicationDto> SuspectedMedications,
    /// <summary>Null when the viewer is the patient: the category is a professional label and is not shown to the patient.</summary>
    EventCategory? Category,
    ReviewStatus Status,
    /// <summary>Codes for what is not known. Unknown stays unknown: nothing is guessed to fill a gap.</summary>
    IReadOnlyList<string> MissingInformation,
    DateTimeOffset LastActivityAt,
    int Version,
    string SignalNotice,
    string InternalRecordNotice,
    /// <summary>Always "NotSubmitted": nothing in this system sends an adverse-event record anywhere.</summary>
    string ExternalStatus,
    WordingStatus PersianWordingStatus);

public sealed record AdverseEventDetailDto(AdverseEventDto Event, IReadOnlyList<AdverseEventEntryDto> Entries);

public sealed record AssistanceLine(string Text, string FromSection);

/// <summary>
/// A navigation aid built from what is already recorded. Today it is produced by a fixed template: NO language model is called. It is never stored in the record
/// and never a conclusion; <see cref="Origin"/> says so.
/// </summary>
public sealed record AdverseEventAssistanceDto(
    string Origin,
    string Label,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AssistanceLine> Summary,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> Uncertainty,
    string Notice,
    WordingStatus PersianWordingStatus);

public sealed record DefinitionSource(string Document, string Section, string Page, string Kind, bool IsLectureSlide);

/// <summary>A definition quoted from a supplementary source. The Persian rendering has not been reviewed by anyone.</summary>
public sealed record CategoryDefinition(EventCategory Category, string Term, string Definition, DefinitionSource Source, string Note, WordingStatus PersianWordingStatus);

public static class AdrDefinitions
{
    private const string Who = "WHO — The global smart pharmacovigilance strategy (2025)";
    private const string Fip = "FIP — Patient safety: Pharmacists' role in \"Medication without harm\" (2020)";

    public static readonly IReadOnlyList<CategoryDefinition> All =
    [
        new(EventCategory.AdverseDrugReaction, "Adverse drug reaction (ADR)",
            "A response to a medicine that is noxious and unintended, and which occurs at doses normally used in humans.",
            new(Who, "Glossary", "printed p. xiii", "SupplementaryDocument", false),
            "The FIP report words it \"a response to a medicinal product which is noxious and unintended and which occurs at doses normally used in humans for the prophylaxis, diagnosis or therapy of disease or the restoration, correction or modification of physiological function\" (§1.1, printed p. 6).",
            WordingStatus.AwaitingClinicalReview),
        new(EventCategory.AdverseEvent, "Adverse event (AE)",
            "Any untoward medical occurrence that may present during treatment with a medicine or vaccine, but which does not necessarily have a causal relationship.",
            new(Who, "Glossary", "printed p. xiii", "SupplementaryDocument", false),
            "An adverse event does not imply that the medicine caused it.",
            WordingStatus.AwaitingClinicalReview),
        new(EventCategory.AdverseDrugEvent, "Adverse drug event (ADE)",
            "Any injury occurring during the patient's medication therapy resulting from either appropriate care, or from unsuitable or suboptimal care.",
            new(Fip, "§1.1 (with Figure 2)", "printed p. 7", "SupplementaryDocument", false),
            "The FIP report states that this definition includes ADRs and medication errors.",
            WordingStatus.AwaitingClinicalReview),
        new(EventCategory.MedicationError, "Medication error (ME)",
            "Unintended mistakes in the medicines-use process caused by omissions or commissions, as well as other errors.",
            new(Fip, "§1.1", "printed p. 6", "SupplementaryDocument", false),
            "The FIP report lists types of medication error in its Figure 1.",
            WordingStatus.AwaitingClinicalReview),
        new(EventCategory.NearMiss, "Near miss",
            "A medication error that has the potential to cause harm, but has not, either by luck or because it was intercepted and corrected.",
            new(Fip, "§1.1", "printed p. 6", "SupplementaryDocument", false),
            "The FIP report also calls it a \"close call\" or a \"potential adverse drug event\".",
            WordingStatus.AwaitingClinicalReview),
    ];
}

public static class AdrNotices
{
    public const string Signal = "This record is a signal for human review. It is not proof that a medicine caused anything.";
    public const string InternalRecord = "This is an internal record. It has not been submitted to any regulator, manufacturer or insurer, and nothing here sends it anywhere.";
    public const string Assistance = "This summary was put together by a fixed template from what is already written in the record. It is not a conclusion and it was not checked by a professional.";
    public const string ExternalNotSubmitted = "NotSubmitted";
}

/// <summary>Management of internal adverse-event records. Authorization (role, relationship, consent) is enforced by the caller; the service enforces who may do what.</summary>
public interface IAdverseEventService
{
    Task<AdrOutcome<AdverseEventDetailDto>> CreateAsync(Guid actorUserId, ReporterRole role, Guid subjectId, CreateAdverseEventInput input, string source, string? correlationId, CancellationToken ct = default);

    Task<AdrOutcome<IReadOnlyList<AdverseEventSummaryDto>>> ListForSubjectAsync(Guid subjectId, ReporterRole viewerRole, int take, CancellationToken ct = default);

    /// <summary>Open records across subjects, for a reviewer's queue. The caller MUST filter the result to the subjects the reviewer may read.</summary>
    Task<AdrOutcome<IReadOnlyList<AdverseEventSummaryDto>>> ListByStatusAsync(IReadOnlyCollection<ReviewStatus> statuses, int take, CancellationToken ct = default);

    /// <summary>The subject a record belongs to, so the caller can authorize against that subject before reading it. Null when there is no such record.</summary>
    Task<Guid?> SubjectOfAsync(Guid id, CancellationToken ct = default);

    Task<AdrOutcome<AdverseEventDetailDto>> GetAsync(Guid id, ReporterRole viewerRole, CancellationToken ct = default);

    /// <summary>Adds information after the original report (reporter or professional). Never edits the original.</summary>
    Task<AdrOutcome<AdverseEventDetailDto>> AddFollowUpAsync(Guid actorUserId, ReporterRole role, Guid id, string text, string source, string? correlationId, CancellationToken ct = default);

    Task<AdrOutcome<AdverseEventDetailDto>> AddReviewEntryAsync(Guid actorUserId, ReporterRole role, Guid id, EntryKind kind, string text, string source, string? correlationId, CancellationToken ct = default);

    Task<AdrOutcome<AdverseEventDetailDto>> AddSupplementAsync(Guid actorUserId, ReporterRole role, Guid id, SupplementInput input, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Professional only. The reason is required; returning to Unclassified is allowed. The history keeps every earlier value.</summary>
    Task<AdrOutcome<AdverseEventDetailDto>> ClassifyAsync(Guid actorUserId, ReporterRole role, Guid id, EventCategory category, string reason, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);

    Task<AdrOutcome<AdverseEventDetailDto>> SetStatusAsync(Guid actorUserId, ReporterRole role, Guid id, ReviewStatus status, string? note, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);

    Task<AdrOutcome<AdverseEventAssistanceDto>> AssistAsync(Guid actorUserId, ReporterRole role, Guid id, string source, string? correlationId, CancellationToken ct = default);
}
