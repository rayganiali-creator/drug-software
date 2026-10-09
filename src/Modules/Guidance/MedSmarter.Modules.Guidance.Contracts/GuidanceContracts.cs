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
    string? Notice);

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
