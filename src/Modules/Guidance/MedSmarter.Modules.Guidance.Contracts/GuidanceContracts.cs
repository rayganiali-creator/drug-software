namespace MedSmarter.Modules.Guidance.Contracts;

// The shared contract of every message a patient may read (warnings, AI replies, reminders, monitoring results).
// Phase 5 defines the contract, the templates, the policy and the lifecycle. The clinical engine that decides WHEN to say something is Phase 7.

public enum GuidanceLevel
{
    /// <summary>Good to know. Nothing to do now.</summary>
    Information,

    /// <summary>Worth following up at the next convenient contact.</summary>
    FollowUp,

    /// <summary>Please have a professional look at it soon.</summary>
    ReviewSoon,

    /// <summary>Needs prompt action. Written calmly and honestly, never alarmingly.</summary>
    Urgent,
}

public enum GuidanceStatus
{
    Sent,
    Seen,
    Reviewed,
    Referred,
    Resolved,
}

public enum GuidanceConfidence
{
    NotAssessable,
    Low,
    Moderate,
    High,
}

/// <summary>
/// The six parts of every patient message: what was noticed, why it matters, what is a sensible next step, when to talk to a professional,
/// which signs need urgent action (only when truly needed), and the basis and confidence. No diagnosis, no change of medicines, no scare words.
/// </summary>
public sealed record PatientGuidanceContent(
    string Observed,
    string WhyItMatters,
    string SuggestedAction,
    string WhenToConsult,
    string? UrgentSigns,
    string BasisAndConfidence);

/// <summary>The same message in the language of professionals: complete and technical, including the real severity.</summary>
public sealed record ProfessionalGuidanceContent(string Summary, string TechnicalDetail, string SeverityLabel, string Basis, GuidanceConfidence Confidence);

public sealed record GuidanceRequest(
    string TemplateKey,
    GuidanceLevel Level,
    string Locale,
    IReadOnlyDictionary<string, string> Parameters,
    GuidanceConfidence Confidence,
    string BasisLabel,
    bool HasSufficientData);

public sealed record PolicyViolation(string Code, string Part);

public sealed record GuidanceComposition(PatientGuidanceContent Patient, ProfessionalGuidanceContent Professional, GuidanceLevel Level, IReadOnlyList<PolicyViolation> Violations)
{
    public bool IsValid => Violations.Count == 0;
}

public sealed record GuidanceMessageDto(
    Guid Id,
    string TemplateKey,
    GuidanceLevel Level,
    GuidanceStatus Status,
    string Locale,
    PatientGuidanceContent Patient,
    /// <summary>Present only for professionals; patients never receive it.</summary>
    ProfessionalGuidanceContent? Professional,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SeenAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ReferredAt,
    DateTimeOffset? ResolvedAt,
    bool IsDemo,
    string? Notice,
    /// <summary>Where the message came from (for example a safety assessment). Null for messages that were not raised by an engine. Additive in Phase 7.</summary>
    GuidanceOrigin? Origin = null,
    /// <summary>Phase 8: how a notification for this message is going. Null = no delivery was ever scheduled (the message is only in the app). Record creation is NOT delivery.</summary>
    GuidanceDeliveryDto? Delivery = null,
    /// <summary>Where the content came from: AutomatedAssessment (an engine matched a rule), PatientReported, or Unspecified. A clinician's review is the separate flag below.</summary>
    string Provenance = "Unspecified",
    /// <summary>True once a professional has moved the message to Reviewed (or beyond). Automated findings are not clinician-reviewed until then.</summary>
    bool ClinicianReviewed = false);

public enum DeliveryState
{
    /// <summary>Waiting for the next delivery pass.</summary>
    Scheduled,

    /// <summary>An attempt started and its outcome is not known yet (or the worker was interrupted). Never shown as delivered.</summary>
    Attempted,

    /// <summary>The delivery mechanism confirmed. For a simulated mechanism this only means the simulation accepted it.</summary>
    Confirmed,

    /// <summary>Gave up after the allowed attempts or a permanent refusal.</summary>
    Failed,
}

public enum DeliveryMode
{
    /// <summary>A local stand-in. No real push, SMS, e-mail or device notification exists in this system.</summary>
    Simulated,
    Real,
}

public sealed record GuidanceDeliveryDto(
    DeliveryState State,
    DeliveryMode Mode,
    string Channel,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? FailedAt,
    int Attempts,
    string? ErrorCode);

/// <summary><paramref name="Skipped"/>: deliveries not sent because the patient or message was no longer eligible (deferred or ended). <paramref name="Unrecorded"/>: the channel was called but this pass could not record the outcome; the row stays "attempted" and recovery handles it (at-least-once).</summary>
public sealed record DeliveryRunResult(int Considered, int Confirmed, int Retrying, int Failed, int Recovered, int Skipped = 0, int Unrecorded = 0);

/// <summary>
/// What a channel gets: ids only. Message text and health details never travel to a channel in Phase 8.
/// <paramref name="IdempotencyKey"/> is the delivery row id: the same for every attempt of one (message, channel) delivery, so a channel that supports
/// idempotency can drop a repeat. Delivery is at-least-once; without channel-side support a retry after an unrecorded outcome can reach the channel twice.
/// </summary>
public sealed record DeliveryRequest(Guid MessageId, Guid PatientId, GuidanceLevel Level, string Locale, string? IdempotencyKey = null);

public enum ChannelOutcome
{
    Confirmed,
    TransientFailure,
    PermanentFailure,
}

public interface IGuidanceDeliveryChannel
{
    string Name { get; }

    DeliveryMode Mode { get; }

    Task<ChannelOutcome> DeliverAsync(DeliveryRequest request, CancellationToken ct);
}

/// <summary>Schedules and performs delivery passes. Bounded, idempotent, and honest: a state advances only on a recorded fact.</summary>
public interface IGuidanceDeliveryService
{
    bool Enabled { get; }

    /// <summary>One bounded pass over due deliveries (and recovery of interrupted attempts).</summary>
    Task<DeliveryRunResult> RunDueAsync(int batchSize, CancellationToken ct);
}

/// <summary>Traceability of an engine-raised message: which assessment, which rule version and finding, and how fresh the data was. Creation time is the message's own CreatedAt.</summary>
/// <param name="SubjectsFingerprint">Phase 8: stable hash of the exact records (id + version) the finding is about. A resolved finding is raised again only when this changes (null = fall back to DataAsOf).</param>
public sealed record GuidanceOrigin(string Kind, Guid AssessmentId, string RuleId, int RuleVersion, string FindingKey, DateTimeOffset DataAsOf, string? SubjectsFingerprint = null);

/// <param name="Existing">True when an open message for the same finding already existed and no new one was created.</param>
public sealed record GuidanceCreated(GuidanceMessageDto Message, bool Existing);

public sealed record GuidanceOriginRef(Guid MessageId, GuidanceStatus Status, GuidanceOrigin Origin);

public enum GuidanceError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

public sealed record GuidanceOutcome<T>(T? Value, GuidanceError Error, string? Detail = null)
{
    public bool Succeeded => Error == GuidanceError.None;
}

public static class GuidanceOutcome
{
    public static GuidanceOutcome<T> Ok<T>(T value) => new(value, GuidanceError.None);

    public static GuidanceOutcome<T> Fail<T>(GuidanceError error, string? detail = null) => new(default, error, detail);
}

/// <summary>Turns a template and its parameters into a patient message and a professional message, and checks the patient message against the policy.</summary>
public interface IGuidanceComposer
{
    IReadOnlyList<string> TemplateKeys { get; }

    /// <summary>Never returns text that breaks the policy: a violating composition comes back with its violations and must not be shown.</summary>
    GuidanceComposition Compose(GuidanceRequest request);
}

public interface IGuidanceService
{
    /// <summary>Stores a composed message for a patient (used by the engines of later phases). A message that violates the policy is refused.</summary>
    Task<GuidanceOutcome<GuidanceMessageDto>> CreateAsync(Guid subjectId, GuidanceRequest request, bool isDemo, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>
    /// Stores a message raised by an engine, tied to its origin. The same finding never produces a second OPEN message, and a finding the patient already resolved is
    /// not raised again unless the patient's data changed after it was resolved. Creating the record does not mean the patient has seen it: only the patient's own action sets Seen.
    /// </summary>
    Task<GuidanceOutcome<GuidanceCreated>> CreateFromOriginAsync(Guid subjectId, GuidanceRequest request, GuidanceOrigin origin, bool isDemo, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Messages of one origin kind that are not resolved yet (bounded). Used to tell which earlier findings no longer match, without ever closing them automatically.</summary>
    Task<GuidanceOutcome<IReadOnlyList<GuidanceOriginRef>>> ListOpenByOriginAsync(Guid subjectId, string originKind, CancellationToken ct = default);

    Task<GuidanceOutcome<IReadOnlyList<GuidanceMessageDto>>> ListForPatientAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Moves a message along its life cycle. Patients can mark messages seen or resolved; reviewing and referring are for professionals.</summary>
    Task<GuidanceOutcome<GuidanceMessageDto>> SetStatusAsync(Guid actorUserId, Guid subjectId, Guid id, GuidanceStatus target, bool actorIsProfessional, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Fictional example messages in both audiences' views, built from the templates. Always labelled DEMO.</summary>
    IReadOnlyList<GuidanceComposition> Samples(string locale);
}

public interface IDemoGuidanceSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
