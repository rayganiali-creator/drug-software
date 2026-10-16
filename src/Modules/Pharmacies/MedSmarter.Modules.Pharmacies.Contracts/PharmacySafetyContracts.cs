namespace MedSmarter.Modules.Pharmacies.Contracts;

// Phase 10: INTERNAL pharmacy safety records. They are for a pharmacy's own review and learning. They are not regulatory reports, not a nationally mandated
// classification, and not proof of anything. Nothing here is sent to a manufacturer, regulator or insurer. Each field and list is labelled in
// docs/phase10/03-source-traceability.md as source-derived, existing-system reuse or a design proposal.

public enum PharmacyError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

public sealed record PharmacyOutcome<T>(T? Value, PharmacyError Error, string? Detail = null)
{
    public bool Succeeded => Error == PharmacyError.None;
}

public static class PharmacyOutcome
{
    public static PharmacyOutcome<T> Ok<T>(T value) => new(value, PharmacyError.None);

    public static PharmacyOutcome<T> Fail<T>(PharmacyError error, string? detail = null) => new(default, error, detail);
}

public enum PharmacyActorRole
{
    Pharmacist,
    PharmacyAdmin,
}

public enum SafetyStatus
{
    Submitted,
    InReview,
    FollowUpRequired,
    Closed,
}

public enum SafetyRecordKind
{
    SafetyEvent,
    ProductConcern,
}

public enum SafetyEntryKind
{
    ReviewNote,
    FollowUp,
    ManufacturerReportProposal,
    StatusChange,
}

// ---------------------------------------------------------------- dispensing errors and near misses

/// <summary>"Near miss" and "dispensing error" are kept apart by whether the problem reached the patient. A near miss never did (it was intercepted or did not reach them).</summary>
public enum SafetyEventKind
{
    DispensingError,
    NearMiss,
}

public enum ReachedPatient
{
    Yes,
    No,
    Unknown,
}

/// <summary>
/// The EXAMPLES listed in the FIP report (Patient safety: Pharmacists' role in "Medication without harm", 2020), §5.2.1 "Common dispensing errors", list on printed p. 29 (page numbers to be re-checked against the original).
/// The report introduces them with "For example:", so they are examples, not a complete or official list. <see cref="OtherNotListed"/> is MedSmarter's own and is not from FIP.
/// </summary>
public enum DispensingErrorExample
{
    IncorrectMedicineFormOrStrength,
    LabelIncorrectOrInconsistentWithPrescription,
    IncorrectQuantity,
    ExpiredMedicine,
    IncorrectlyCompounded,
    InteractionNotDetectedOrAllergyNotDocumented,
    SomeoneElsesPrescription,
    DifferentBrandLeadingToDuplication,
    OtherNotListed,
}

public sealed record CreateSafetyEventInput(
    SafetyEventKind Kind,
    ReachedPatient ReachedPatient,
    IReadOnlyList<DispensingErrorExample>? ErrorExamples,
    string Description,
    DateOnly? OccurredOn,
    string? MedicineAsReported,
    string? HowDetectedOrIntercepted,
    string? ImmediateActionTaken,
    Guid? LinkedAdverseEventId = null,
    Guid? LinkedProductConcernId = null,
    string? ClientRequestId = null);

public sealed record SafetyEventDto(
    Guid Id,
    Guid OrganizationId,
    SafetyEventKind Kind,
    ReachedPatient ReachedPatient,
    IReadOnlyList<DispensingErrorExample> ErrorExamples,
    string Description,
    DateOnly? OccurredOn,
    string? MedicineAsReported,
    string? HowDetectedOrIntercepted,
    string? ImmediateActionTaken,
    PharmacyActorRole ReporterRole,
    DateTimeOffset ReportedAt,
    SafetyStatus Status,
    IReadOnlyList<string> MissingInformation,
    Guid? LinkedAdverseEventId,
    Guid? LinkedProductConcernId,
    DateTimeOffset LastActivityAt,
    int Version,
    string Notice,
    string ExternalStatus,
    string PersianWordingStatus);

// ---------------------------------------------------------------- product concerns

/// <summary>
/// PROVISIONAL, MedSmarter-own types. The supplied materials do not define "product-quality defect" or "lack of effect"; these are labels for intake only,
/// pending an authoritative taxonomy. A concern is a suspicion, never a finding.
/// </summary>
public enum ProductConcernType
{
    AppearanceOrPackaging,
    SuspectedQualityProblem,
    LabellingOrNaming,
    ApparentLackOfEffect,
    Other,
}

public enum ProductLinkState
{
    /// <summary>Linked to an existing ProductTrace record; the identifiers below were copied from it at the time.</summary>
    LinkedToProductRecord,

    /// <summary>No matching record was given. Whatever identifiers the reporter typed are kept as typed; nothing is looked up or chosen.</summary>
    Unmatched,
}

public sealed record ProductRecordRef(Guid SubjectId, Guid ProductRecordId);

public sealed record ProductIdentifiers(string? ProductName, string? BatchNumber, DateOnly? ExpiryDate, string? ManufacturerName, string? Gtin);

/// <summary>What was copied from the ProductTrace record when the concern was linked (identifiers only; no patient data).</summary>
public sealed record ProductSnapshot(Guid SubjectId, Guid ProductRecordId, ProductIdentifiers Identifiers, int RecordVersion, string VerificationAtLinking);

public sealed record CreateProductConcernInput(
    ProductConcernType Type,
    string Description,
    DateOnly? ObservedOn,
    ProductRecordRef? ProductRecord,
    ProductIdentifiers? AsReported,
    Guid? LinkedAdverseEventId = null,
    Guid? LinkedSafetyEventId = null,
    string? ClientRequestId = null);

public sealed record ProductConcernDto(
    Guid Id,
    Guid OrganizationId,
    ProductConcernType Type,
    /// <summary>Always "ProvisionalNotSourceDefined".</summary>
    string TypeBasis,
    string Description,
    DateOnly? ObservedOn,
    ProductLinkState LinkState,
    ProductIdentifiers? Linked,
    int? LinkedRecordVersion,
    string? LinkedVerificationAtLinking,
    ProductIdentifiers? AsReported,
    /// <summary>Codes for what is unknown or inconsistent (for example a typed batch that differs from the linked record). Nothing is chosen on the reporter's behalf.</summary>
    IReadOnlyList<string> Flags,
    PharmacyActorRole ReporterRole,
    DateTimeOffset ReportedAt,
    SafetyStatus Status,
    Guid? LinkedAdverseEventId,
    Guid? LinkedSafetyEventId,
    bool ManufacturerReportProposed,
    DateTimeOffset? ManufacturerReportProposedAt,
    DateTimeOffset LastActivityAt,
    int Version,
    string Notice,
    string ExternalStatus,
    string PersianWordingStatus);

public sealed record SafetyEntryDto(Guid Id, SafetyEntryKind Kind, PharmacyActorRole AuthorRole, Guid AuthorUserId, DateTimeOffset At, string? Text, SafetyStatus? FromStatus, SafetyStatus? ToStatus);

public sealed record SafetyEventDetailDto(SafetyEventDto Event, IReadOnlyList<SafetyEntryDto> Entries);

public sealed record ProductConcernDetailDto(ProductConcernDto Concern, IReadOnlyList<SafetyEntryDto> Entries);

public sealed record SafetySummaryDto(Guid Id, SafetyRecordKind RecordKind, string Label, SafetyStatus Status, DateTimeOffset ReportedAt, DateTimeOffset LastActivityAt, string Excerpt);

public sealed record DispensingErrorExampleInfo(DispensingErrorExample Example, string SourceWording, string Document, string Section, string Page);

public static class PharmacyNotices
{
    public const string Event = "This is an internal pharmacy record for review and learning. It is not a regulatory report and it is not a nationally mandated classification. Do not enter patient names or other identifying details.";
    public const string Concern = "A product concern is a suspicion. It does not show that a product is defective, and nothing here has been sent to a manufacturer, regulator or insurer.";
    public const string Taxonomy = "The list of examples comes from one supplementary document (FIP, 2020) that introduces them with \"For example\". It is not a complete or official list.";
    public const string ConcernTypes = "These concern types are provisional labels of MedSmarter. The supplied materials do not define product-quality defects or lack of effect.";
    public const string ExternalNotSubmitted = "NotSubmitted";
    public const string PersianAwaitingReview = "AwaitingClinicalReview";
}

public static class PharmacyDefinitions
{
    public const string NearMissDefinition = "A medication error that has the potential to cause harm, but has not, either by luck or because it was intercepted and corrected.";
    public const string NearMissDocument = "FIP — Patient safety: Pharmacists' role in \"Medication without harm\" (2020)";
    public const string NearMissSection = "§1.1";
    public const string NearMissPage = "printed p. 6";

    private const string Fip = NearMissDocument;
    private const string Sec = "§5.2.1 Common dispensing errors";
    private const string Pg = "printed p. 29 (list; its introduction is on the preceding page; page numbers to be re-checked against the original)";

    public static readonly IReadOnlyList<DispensingErrorExampleInfo> Examples =
    [
        new(DispensingErrorExample.IncorrectMedicineFormOrStrength, "Patients receiving the incorrect medicine, or form or strength of medicine", Fip, Sec, Pg),
        new(DispensingErrorExample.LabelIncorrectOrInconsistentWithPrescription, "Prescription labels containing incorrect information, or information inconsistent with the original prescription", Fip, Sec, Pg),
        new(DispensingErrorExample.IncorrectQuantity, "Patients receiving the incorrect quantity of medicines", Fip, Sec, Pg),
        new(DispensingErrorExample.ExpiredMedicine, "Patients receiving an expired medicine", Fip, Sec, Pg),
        new(DispensingErrorExample.IncorrectlyCompounded, "Patients receiving medicines which were incorrectly compounded", Fip, Sec, Pg),
        new(DispensingErrorExample.InteractionNotDetectedOrAllergyNotDocumented, "Pharmacists not detecting drug interactions or clearly documenting allergy information, resulting in adverse effects for the patient", Fip, Sec, Pg),
        new(DispensingErrorExample.SomeoneElsesPrescription, "Patients receiving someone else's prescription medicine", Fip, Sec, Pg),
        new(DispensingErrorExample.DifferentBrandLeadingToDuplication, "Patients receiving a different brand of a medicine that they already have (leading to duplication)", Fip, Sec, Pg),
    ];
}

/// <summary>
/// Internal pharmacy safety records. Authorization (organization membership and permission) is enforced by the caller; the service enforces what each role may do and
/// keeps the original report immutable. It has no outbound path of any kind.
/// </summary>
public interface IPharmacySafetyService
{
    Task<PharmacyOutcome<SafetyEventDetailDto>> CreateEventAsync(Guid actorUserId, PharmacyActorRole role, Guid organizationId, CreateSafetyEventInput input, string source, string? correlationId, CancellationToken ct = default);

    Task<PharmacyOutcome<ProductConcernDetailDto>> CreateConcernAsync(Guid actorUserId, PharmacyActorRole role, Guid organizationId, CreateProductConcernInput input, ProductSnapshot? snapshot, string source, string? correlationId, CancellationToken ct = default);

    Task<PharmacyOutcome<IReadOnlyList<SafetySummaryDto>>> ListAsync(Guid organizationId, SafetyRecordKind kind, IReadOnlyCollection<SafetyStatus>? statuses, int take, CancellationToken ct = default);

    /// <summary>The organization a record belongs to, so the caller authorizes against it before reading. Null when there is no such record.</summary>
    Task<Guid?> OrganizationOfAsync(SafetyRecordKind kind, Guid id, CancellationToken ct = default);

    Task<PharmacyOutcome<SafetyEventDetailDto>> GetEventAsync(Guid id, CancellationToken ct = default);

    Task<PharmacyOutcome<ProductConcernDetailDto>> GetConcernAsync(Guid id, CancellationToken ct = default);

    Task<PharmacyOutcome<bool>> AddEntryAsync(Guid actorUserId, PharmacyActorRole role, SafetyRecordKind kind, Guid id, SafetyEntryKind entryKind, string text, string source, string? correlationId, CancellationToken ct = default);

    Task<PharmacyOutcome<bool>> SetStatusAsync(Guid actorUserId, PharmacyActorRole role, SafetyRecordKind kind, Guid id, SafetyStatus status, string? note, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>
    /// A reviewer records that, in their judgement, the concern may be worth a manufacturer report. This is a HUMAN DECISION NOTE only: nothing is created, queued or sent.
    /// Any manufacturer report still has to be drafted by the patient through the existing ProductTrace pipeline (consent, human review, mock outbox).
    /// </summary>
    Task<PharmacyOutcome<bool>> ProposeManufacturerReportAsync(Guid actorUserId, PharmacyActorRole role, Guid concernId, string note, int expectedVersion, string source, string? correlationId, CancellationToken ct = default);
}
