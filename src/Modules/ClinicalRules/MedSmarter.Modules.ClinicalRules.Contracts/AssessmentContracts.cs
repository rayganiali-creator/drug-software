namespace MedSmarter.Modules.ClinicalRules.Contracts;

// ---------- engine input: a frozen, minimal snapshot ----------

public enum InputAvailability
{
    Available,
    NeverRecorded,

    /// <summary>The requester is not allowed to read this category (no consent or no permission). Treated exactly like missing data, never as "nothing there".</summary>
    NotAuthorized,

    /// <summary>The category could not be read (technical error).</summary>
    Unavailable,
}

/// <summary>Where one category of input came from, when it was last updated, and whether it is too old to rely on.</summary>
public sealed record InputState(string Category, InputAvailability Availability, DateTimeOffset? LastUpdatedAt, int RecordCount, bool IsStale, int StaleAfterDays, string Source);

/// <param name="ReferenceFound">False = the linked reference medicine could not be read (inactive, removed): its ingredients are unknown.</param>
public sealed record SnapshotMedication(
    Guid Id,
    Guid? ReferenceMedicationId,
    string DisplayName,
    bool IsRegistered,
    bool IsActive,
    bool ReferenceFound,
    IReadOnlyList<Guid> IngredientIds);

/// <param name="Kind">"Medication", "ActiveIngredient" or "Other". Only a medication-linked allergy can be matched; the rest is free text and stays unmatched.</param>
public sealed record SnapshotAllergy(Guid Id, string Kind, Guid? ReferenceMedicationId, bool ReferenceFound, IReadOnlyList<Guid> IngredientIds, string Substance, string Severity);

/// <summary>The symptom text is deliberately absent: the engine never reads free text.</summary>
public sealed record SnapshotSymptom(Guid Id, string Severity, DateTimeOffset OnsetAt, DateTimeOffset? ResolvedAt);

/// <param name="Severity">Reference severity as recorded: Unknown, Minor, Moderate, Major, Contraindicated.</param>
/// <param name="Validation">Reference validation as recorded: Demo, Unverified, NeedsValidation, Validated, Rejected.</param>
public sealed record SnapshotInteraction(Guid IngredientA, Guid IngredientB, string Severity, string Validation, EvidenceRef Evidence);

/// <summary>Everything the engine may look at. Two equal snapshots and equal rules always give the same result.</summary>
public sealed record AssessmentSnapshot(
    DateTimeOffset At,
    int? AgeYears,
    IReadOnlyList<InputState> Inputs,
    IReadOnlyList<SnapshotMedication> Medications,
    IReadOnlyList<SnapshotAllergy> Allergies,
    IReadOnlyList<SnapshotSymptom> Symptoms,
    IReadOnlyList<SnapshotInteraction> Interactions);

// ---------- engine output ----------

/// <summary>What happened to one rule. Missing, stale, unavailable and "no match" are different things and are never merged.</summary>
public enum RuleOutcome
{
    Matched,
    NoMatch,
    MissingData,
    StaleData,
    InsufficientEvidence,
    NotApplicable,

    /// <summary>The rule did not run: inactive, unapproved, retired or not yet effective.</summary>
    Unavailable,

    /// <summary>The engine failed on this rule. Reported, never silenced.</summary>
    Error,
}

public sealed record RuleEvaluation(
    string RuleId,
    int Version,
    RuleDomain Domain,
    RuleOutcome Outcome,
    IReadOnlyList<string> Reasons,
    ActivationState Activation,
    bool IsDemo,
    /// <summary>True when only some of the items could be checked (for example one medicine is not in the reference).</summary>
    bool Partial,
    int FindingCount);

public sealed record Finding(
    /// <summary>Stable key of "this rule version about these exact records": the same situation always gets the same key.</summary>
    string Key,
    string RuleId,
    int RuleVersion,
    RuleDomain Domain,
    FindingSeverity Severity,
    FindingUrgency Urgency,
    /// <summary>True only for findings of Active (approved) rules. Demonstration findings are never actionable.</summary>
    bool Actionable,
    bool IsDemo,
    string GuidanceTemplateKey,
    IReadOnlyList<FindingSubject> Subjects,
    IReadOnlyList<EvidenceRef> Evidence,
    /// <summary>Reference severity, when the finding comes from a reference interaction.</summary>
    string? ReferenceSeverity,
    bool EvidenceConflict,
    bool InputsStale,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string>? EmergencySigns);

/// <param name="Kind">"medication", "allergy" or "symptom".</param>
public sealed record FindingSubject(string Kind, Guid RecordId, string Label);

public enum AssessmentStatus
{
    /// <summary>At least one finding. Whether it is actionable is a separate flag on the finding.</summary>
    CompletedWithFindings,

    /// <summary>Approved rules ran and matched nothing. This is not "safe": see the coverage.</summary>
    CompletedNoMatches,

    /// <summary>Not enough usable data for the approved rules that apply. No conclusion.</summary>
    Incomplete,

    /// <summary>No approved rule is in force. Nothing was checked against approved knowledge.</summary>
    NoApprovedCoverage,

    /// <summary>The engine or a safety invariant failed. No result may be relied on.</summary>
    Failed,
}

public enum GuidanceLinkState
{
    NotApplicable,
    Created,
    Partial,
    Failed,
}

public sealed record AssessmentCoverage(int ActiveRules, int DemonstrationRules, int InactiveRules, int NotEvaluable, IReadOnlyList<RuleDomain> DomainsCovered, IReadOnlyList<string> UnsupportedDomains);

/// <summary>The engine's pure result. No identifiers of people and no persistence concerns.</summary>
public sealed record EngineResult(
    AssessmentStatus Status,
    bool Complete,
    IReadOnlyList<InputState> Inputs,
    IReadOnlyList<RuleEvaluation> Evaluations,
    IReadOnlyList<Finding> Findings,
    AssessmentCoverage Coverage,
    IReadOnlyList<string> Limitations,
    string EngineVersion,
    string RuleSetVersion,
    DateTimeOffset EvaluatedAt,
    bool ContainsDemonstration,
    /// <summary>Always false. A clean result is never a statement that something is safe.</summary>
    bool SafetyClaimAllowed);

public sealed record GuidanceLink(string FindingKey, Guid? MessageId, string State);

public sealed record AssessmentDto(
    Guid Id,
    Guid SubjectId,
    EngineResult Result,
    string Trigger,
    bool RequestedByPatient,
    GuidanceLinkState GuidanceState,
    IReadOnlyList<GuidanceLink> GuidanceLinks,
    /// <summary>Open guidance created by an earlier assessment whose finding no longer matches the current data. Kept open, never auto-resolved.</summary>
    IReadOnlyList<string> OpenGuidanceNoLongerMatching,
    bool Outdated,
    IReadOnlyList<string> OutdatedReasons,
    string Notice,
    /// <summary>Phase 8: stable hash of the exact record versions and availability this assessment used.</summary>
    string InputFingerprint = "",
    /// <summary>Phase 8: which record versions produced this assessment (ids and version tokens only; no names, no text).</summary>
    IReadOnlyList<InputVersionRef>? InputVersions = null,
    /// <summary>Phase 8: what changed since the previous assessment (counts only). Null when unknown.</summary>
    InputChangeSummary? Change = null);

/// <param name="Kind">medication, reference, allergy, symptom or profile.</param>
public sealed record InputVersionRef(string Kind, Guid Id, string Version);

public sealed record InputChangeSummary(bool FirstAssessment, int Added, int Removed, int Changed, bool RuleSetChanged);

public sealed record AssessmentSummaryDto(Guid Id, DateTimeOffset EvaluatedAt, AssessmentStatus Status, bool Complete, int FindingCount, int ActionableFindingCount, string RuleSetVersion, string Trigger, bool ContainsDemonstration);

public enum SafetyError
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
    Unavailable,
}

public sealed record SafetyOutcome<T>(T? Value, SafetyError Error, string? Detail = null)
{
    public bool Succeeded => Error == SafetyError.None;
}

public static class SafetyResult
{
    public static SafetyOutcome<T> Ok<T>(T value) => new(value, SafetyError.None);

    public static SafetyOutcome<T> Fail<T>(SafetyError error, string? detail = null) => new(default, error, detail);
}

/// <param name="ReadableCategories">Categories the requester may read (already decided by the caller's authorization and consent checks). Anything else is treated as missing.</param>
/// <param name="Locale">"fa" or "en": the language of the guidance messages raised for the patient.</param>
public sealed record AssessCommand(IReadOnlyCollection<string> ReadableCategories, string Locale = "en", string Trigger = "manual");

public interface IClinicalSafetyService
{
    /// <summary>Builds the snapshot of this patient, runs the rules, applies the safety layer, stores the assessment and creates guidance for actionable findings.</summary>
    Task<SafetyOutcome<AssessmentDto>> AssessAsync(Guid actorUserId, Guid subjectId, AssessCommand command, string source, string? correlationId, CancellationToken ct = default);

    Task<SafetyOutcome<IReadOnlyList<AssessmentSummaryDto>>> ListAsync(Guid subjectId, int take, IReadOnlyCollection<string> readableCategories, CancellationToken ct = default);

    Task<SafetyOutcome<AssessmentDto>> GetAsync(Guid subjectId, Guid assessmentId, IReadOnlyCollection<string> readableCategories, CancellationToken ct = default);

    /// <summary>The newest stored assessment, with an honest statement of whether the data or the rules changed since. Nothing re-runs by itself.</summary>
    /// <summary>
    /// Change-aware re-evaluation for the scheduled worker. Runs the engine only if the exact record versions or the rule set differ from the latest assessment, or if
    /// the latest assessment is older than <paramref name="reassessAtLeastEvery"/> (a configured reassessment is due). Equal inputs never create another assessment; an earlier incomplete guidance step is repaired idempotently.
    /// </summary>
    Task<SafetyOutcome<ReassessmentOutcome>> ReassessIfNeededAsync(Guid subjectId, IReadOnlyCollection<string> readableCategories, string locale, TimeSpan? reassessAtLeastEvery, string? correlationId, CancellationToken ct = default);

    Task<SafetyOutcome<AssessmentDto>> LatestAsync(Guid subjectId, IReadOnlyCollection<string> readableCategories, CancellationToken ct = default);
}

// ---------- Phase 8: scheduled, change-aware reassessment ----------

public enum MonitoringJobState
{
    /// <summary>Enrolled and waiting for its next check.</summary>
    Idle,

    /// <summary>A pass has claimed it. A lease protects against two workers; an interrupted claim is recovered, never assumed to have succeeded.</summary>
    Running,

    /// <summary>Stopped after repeated failures. It does not retry by itself any more; re-enrolling resets it.</summary>
    Exhausted,

    Withdrawn,
}

/// <summary>Status of a patient's scheduled checks. Holds no clinical content.</summary>
public sealed record MonitoringStatusDto(
    bool Enrolled,
    MonitoringJobState State,
    DateTimeOffset? EnrolledAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    /// <summary>reassessed, unchanged, skipped.no_consent, failed.transient, failed.permanent, failed.exhausted, interrupted, cancelled.</summary>
    string? LastOutcome,
    string? LastErrorCode,
    int ConsecutiveFailures,
    DateTimeOffset? NextCheckAt,
    Guid? LastAssessmentId,
    bool WorkerEnabled,
    string Notice);

/// <summary><paramref name="Discarded"/>: jobs whose computed result was not stored because this pass no longer owned the job row (not counted in the other outcomes).</summary>
public sealed record MonitoringRunResult(int Recovered, int Claimed, int Reassessed, int Unchanged, int Skipped, int Failed, int Exhausted, int Discarded = 0);

public enum ReassessmentKind
{
    /// <summary>Inputs and rule set equal to the latest assessment, nothing due: no new assessment.</summary>
    Unchanged,

    /// <summary>A new assessment was stored.</summary>
    Reassessed,

    /// <summary>Same inputs, but an earlier run left guidance incomplete: guidance was re-raised without a new assessment.</summary>
    GuidanceRepaired,
}

public sealed record ReassessmentOutcome(ReassessmentKind Kind, Guid? AssessmentId, AssessmentStatus? Status, string Reason, GuidanceLinkState Guidance = GuidanceLinkState.NotApplicable);

public interface IMonitoringService
{
    /// <summary>Patient only (the caller authorizes). Needs an active Monitoring consent covering medications.</summary>
    Task<SafetyOutcome<MonitoringStatusDto>> EnrolAsync(Guid actorUserId, Guid subjectId, string locale, string source, string? correlationId, CancellationToken ct = default);

    Task<SafetyOutcome<MonitoringStatusDto>> WithdrawAsync(Guid actorUserId, Guid subjectId, string source, string? correlationId, CancellationToken ct = default);

    Task<SafetyOutcome<MonitoringStatusDto>> StatusAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>One bounded pass: recover interrupted claims, claim due jobs, check each. Idempotent; safe to cancel at any point.</summary>
    Task<MonitoringRunResult> RunOnceAsync(CancellationToken ct);
}
