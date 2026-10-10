namespace MedSmarter.Modules.ClinicalRules.Contracts;

// The deterministic rule contract of the clinical safety engine (Phase 7).
// Rules are DATA: structured criteria evaluated by fixed code. A rule never contains code, SQL, a shell command or a prompt, and a language model never decides
// matching, urgency or safety. Five separate questions are kept apart on purpose and never folded into one "score":
//   execution status (did the engine run), evidence provenance and quality, approval status, finding severity, data completeness and freshness.

public enum RuleDomain
{
    /// <summary>Two medicines the patient takes, both linked to the reference, that the reference lists as interacting (matched by ingredient id only).</summary>
    Interaction,

    /// <summary>A recorded allergy that is linked to a reference medicine which is, or shares an ingredient id with, a medicine the patient takes.</summary>
    AllergyConflict,

    /// <summary>Two medicines the patient takes that share an ingredient id (never matched by name).</summary>
    DuplicateIngredient,

    /// <summary>The record cannot be checked reliably (for example a medicine that is not in the reference).</summary>
    DataQuality,

    /// <summary>A symptom the patient recorded themselves as severe. The engine reports the record; it does not interpret it.</summary>
    ReportedSymptom,
}

/// <summary>Life cycle of a rule version. Only <see cref="Approved"/> rules that also meet the activation policy produce actionable findings.</summary>
public enum RuleStatus
{
    Draft,
    UnderReview,
    Approved,
    Retired,
    Rejected,
}

/// <summary>The only matching logic that exists. Each kind is implemented once, in code, and tested.</summary>
public enum RuleCriteriaKind
{
    ReferenceInteraction,
    AllergyMedicationMatch,
    DuplicateIngredient,
    UnregisteredMedication,
    SevereSymptomRecent,
}

/// <summary>How serious the thing found is in the sources. Not a probability and not a triage level.</summary>
public enum FindingSeverity
{
    Informational,
    Minor,
    Moderate,
    Major,
    Critical,
}

/// <summary>How soon a human should look at it. Separate from severity.</summary>
public enum FindingUrgency
{
    None,
    Routine,
    Soon,
    Prompt,
}

public enum EvidenceValidation
{
    /// <summary>Fictional fixture. Never clinical information.</summary>
    Demo,
    Unverified,
    NeedsValidation,
    Validated,
}

/// <summary>
/// One piece of supporting evidence. Nothing here may be invented: a publication date that is not known stays null, and a review status that does not exist is "none".
/// </summary>
public sealed record EvidenceRef(
    string SourceId,
    string SourceName,
    string Version,
    DateOnly? PublicationDate,
    EvidenceValidation Validation,
    string ReviewStatus,
    string? Locator);

/// <param name="MinAgeYears">Inclusive. Null = no lower limit.</param>
/// <param name="MaxAgeYears">Inclusive. Null = no upper limit.</param>
public sealed record RulePopulation(int? MinAgeYears, int? MaxAgeYears, IReadOnlyList<string> Unsupported);

/// <summary>Structured, closed set of parameters. Which ones apply depends on <see cref="Kind"/>; the validator rejects the rest.</summary>
public sealed record RuleCriteria(
    RuleCriteriaKind Kind,
    /// <summary>ReferenceInteraction: the lowest reference severity that matches (Minor, Moderate, Major, Contraindicated).</summary>
    string? MinReferenceSeverity = null,
    /// <summary>SevereSymptomRecent: how many days back a recorded symptom counts.</summary>
    int? WithinDays = null);

/// <summary>A rule's own example: a small synthetic input and the outcome the rule must produce. Executed by the approval step and by the test suite.</summary>
public sealed record RuleTestCase(string Name, AssessmentSnapshot Input, RuleOutcome ExpectedOutcome, int ExpectedFindings);

/// <summary>The content of one rule version. Immutable once the version leaves Draft; a change is a new version.</summary>
public sealed record RuleDefinition(
    string RuleId,
    int Version,
    string Title,
    string Purpose,
    RuleDomain Domain,
    RulePopulation Population,
    /// <summary>Inputs that must be present and fresh: any of "medications", "allergies", "symptoms", "profile".</summary>
    IReadOnlyList<string> RequiredInputs,
    RuleCriteria Criteria,
    FindingSeverity Severity,
    FindingUrgency Urgency,
    /// <summary>Key of the Guidance template used for the patient message. Must exist in the Guidance module.</summary>
    string GuidanceTemplateKey,
    /// <summary>Signs that need urgent action. Allowed only when sourced: the list is ignored unless the rule has validated evidence. Null = none.</summary>
    IReadOnlyList<string>? EmergencySigns,
    IReadOnlyList<EvidenceRef> Evidence,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RuleTestCase> TestCases);

/// <summary>Everything about a rule version except its content: who wrote and reviewed it, its dates and status.</summary>
public sealed record RuleLifecycle(
    RuleStatus Status,
    bool IsDemo,
    string Author,
    DateTimeOffset AuthoredAt,
    string? Reviewer,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? RetiredAt);

public sealed record RuleRecord(RuleDefinition Definition, RuleLifecycle Lifecycle);

public enum ActivationState
{
    /// <summary>Approved and meeting every part of the activation policy: may produce actionable findings.</summary>
    Active,

    /// <summary>A demonstration rule, evaluated only where demonstration is allowed. Findings are never actionable advice.</summary>
    DemonstrationOnly,

    /// <summary>Draft, under review, rejected, retired, not yet effective, or failing the policy: not evaluated.</summary>
    Inactive,
}

public sealed record ActivationDecision(ActivationState State, IReadOnlyList<string> Reasons);

public enum ReviewDecision
{
    Approve,
    Reject,
}

public enum RuleError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
}

public sealed record RuleOutcomeResult<T>(T? Value, RuleError Error, string? Detail = null)
{
    public bool Succeeded => Error == RuleError.None;
}

public static class RuleResult
{
    public static RuleOutcomeResult<T> Ok<T>(T value) => new(value, RuleError.None);

    public static RuleOutcomeResult<T> Fail<T>(RuleError error, string? detail = null) => new(default, error, detail);
}

public sealed record RuleSummaryDto(string RuleId, int Version, string Title, RuleDomain Domain, RuleStatus Status, bool IsDemo, ActivationState Activation, IReadOnlyList<string> ActivationReasons, DateTimeOffset AuthoredAt);

public sealed record RuleDetailDto(RuleRecord Record, ActivationDecision Activation);

public sealed record RuleCoverageDto(
    int ActiveRules,
    int DemonstrationRules,
    int InactiveRules,
    IReadOnlyList<RuleDomain> DomainsWithActiveCoverage,
    IReadOnlyList<RuleDomain> DomainsWithoutActiveCoverage,
    IReadOnlyList<string> UnsupportedDomains,
    string RuleSetVersion,
    bool DemonstrationAllowed);

/// <summary>Management of rules and their review. Authorization is enforced by the caller; the catalog enforces separation of duties itself.</summary>
public interface IClinicalRuleCatalog
{
    Task<IReadOnlyList<RuleSummaryDto>> ListAsync(RuleStatus? status, int take, CancellationToken ct = default);

    Task<RuleOutcomeResult<RuleDetailDto>> GetAsync(string ruleId, int version, CancellationToken ct = default);

    /// <summary>Creates a new DRAFT version. Never approved on creation, whatever the input says.</summary>
    Task<RuleOutcomeResult<RuleDetailDto>> CreateDraftAsync(Guid actorUserId, RuleDefinition definition, string source, string? correlationId, CancellationToken ct = default);

    Task<RuleOutcomeResult<RuleDetailDto>> SubmitForReviewAsync(Guid actorUserId, string ruleId, int version, string source, string? correlationId, CancellationToken ct = default);

    /// <summary>Two-person rule: the reviewer must not be the author. Approval also runs the rule's own test cases and the evidence policy.</summary>
    Task<RuleOutcomeResult<RuleDetailDto>> ReviewAsync(Guid actorUserId, string ruleId, int version, ReviewDecision decision, string? note, DateTimeOffset? effectiveFrom, string source, string? correlationId, CancellationToken ct = default);

    Task<RuleOutcomeResult<RuleDetailDto>> RetireAsync(Guid actorUserId, string ruleId, int version, string source, string? correlationId, CancellationToken ct = default);

    Task<RuleCoverageDto> CoverageAsync(CancellationToken ct = default);
}

public interface IDemoRuleSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
