using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Modules.AI.Contracts;

public enum AiProviderKind
{
    Disabled,
    Mock,
    External,
    Local,
}

public enum AiErrorCode
{
    None,
    /// <summary>No provider is enabled (the default outside development).</summary>
    Disabled,
    /// <summary>The selected provider lacks settings (URL, key, approval) or is not installed.</summary>
    NotConfigured,
    Timeout,
    Unavailable,
    InvalidResponse,
    Rejected,
}

/// <summary>
/// Everything a provider may see: a question, source-bearing medication documents and, only when the patient consented, the minimised
/// <see cref="PatientContext"/> (no name, no account id, no contact data). A provider never reads a database.
/// </summary>
/// <param name="Evidence">The retrieved, provenance-labelled evidence items (untrusted DATA, quarantined items removed). Preferred over <paramref name="Context"/> by providers that support it.</param>
/// <param name="ExternalGrant">Set ONLY by the backend authorization gate after every external-processing condition was verified. An external provider refuses to send anything without it.</param>
public sealed record AiRequest(string Purpose, string Question, string Locale, IReadOnlyList<MedicationKnowledgeDocument> Context, int MaxTokens, PatientContext? Patient = null,
    IReadOnlyList<EvidenceItem>? Evidence = null, ExternalProcessingGrant? ExternalGrant = null);

/// <summary>Proof that the external-processing gate passed for this caller and this request (server-side only; never read from a client).</summary>
public sealed record ExternalProcessingGrant(Guid UserId, DateTimeOffset VerifiedAt, bool PatientContextAllowed);

public sealed record AiCompletion(string Text, string Provider, string Model, bool IsMock);

/// <param name="SafeMessage">Never contains URLs, keys, headers or exception text.</param>
public sealed record AiResult(AiCompletion? Completion, AiErrorCode Error, string? SafeMessage = null)
{
    public bool Succeeded => Completion is not null && Error == AiErrorCode.None;
    public static AiResult Ok(AiCompletion c) => new(c, AiErrorCode.None);
    public static AiResult Fail(AiErrorCode error, string? message = null) => new(null, error, message);
}

/// <summary>Swappable text-generation backend. Implementations are adapters; the assistant logic never changes with the vendor.</summary>
public interface IAIProvider
{
    string Name { get; }
    AiProviderKind Kind { get; }
    Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct = default);
}

/// <summary>Public status (never includes the key itself).</summary>
/// <param name="ExternalBlockers">Reason codes that currently prevent external processing (empty when none, or when the provider is not External). Never contains values.</param>
public sealed record AiProviderStatus(string Provider, AiProviderKind Kind, bool Configured, bool IsMock, string? Model, bool ApiKeyPresent, bool ExternalTransferApproved, IReadOnlyList<string>? ExternalBlockers = null);

/// <param name="IncludePatientContext">Ask the assistant to use the caller's own consented patient context. Never a way to read someone else's data.</param>
public sealed record AssistantQuestion(string Question, string Locale, IReadOnlyList<Guid>? MedicationIds, bool IncludePatientContext = false);

// ---------- evidence ----------

/// <summary>Where a piece of evidence comes from. <see cref="ReceivedAt"/> is when THIS system received the source; publication/effective dates are not recorded.</summary>
public sealed record EvidenceSource(Guid SourceId, string Name, string Version, string Publisher, DateTimeOffset? ReceivedAt, string Validation);

/// <param name="Id">Stable within one answer (E1, E2, ...). Generated text may cite only these ids.</param>
/// <param name="Kind">A statement kind (Warning, Administration, ...) or <c>Interaction</c>.</param>
/// <param name="Qualifier">For interactions: the other ingredient and the severity. Otherwise null.</param>
/// <param name="Stale">The source was received longer ago than the configured limit.</param>
/// <param name="SourceDateUnknown">The source has no recorded receipt date, so its age cannot be judged.</param>
public sealed record EvidenceItem(string Id, Guid MedicationId, string MedicationName, string Kind, string Text, string? Qualifier, EvidenceSource Source, string Validation, bool IsDemo, bool Stale, bool SourceDateUnknown);

public sealed record EvidenceMedication(Guid Id, string Name, string DosageForm, string Strength, int RecordVersion, DateTimeOffset UpdatedAt, bool IsDemo, string Validation);

/// <summary>Two sources disagree (today: about the severity of the same interaction). The system does not decide which is right.</summary>
public sealed record EvidenceConflict(string Code, string MedicationName, IReadOnlyList<string> ItemIds);

/// <summary>Describes the provenance status of the evidence only. It says nothing about clinical correctness and is never a number.</summary>
public enum EvidenceQuality
{
    /// <summary>Nothing relevant was found.</summary>
    None,
    /// <summary>Only fictional demonstration data.</summary>
    DemoOnly,
    /// <summary>At least one item is not source-validated.</summary>
    Unverified,
    /// <summary>All items are validated but something limits them: stale or undated sources, a conflict, or missing relevant information.</summary>
    Limited,
    /// <summary>All items are validated, current, consistent and cover the topic asked about.</summary>
    Validated,
}

public sealed record EvidenceSet(
    IReadOnlyList<EvidenceMedication> Medications,
    IReadOnlyList<EvidenceItem> Items,
    IReadOnlyList<EvidenceConflict> Conflicts,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> Limitations,
    EvidenceQuality Quality,
    int QuarantinedCount = 0,
    int ExcludedCount = 0)
{
    public static EvidenceSet Empty { get; } = new([], [], [], [], ["evidence.none_found"], EvidenceQuality.None);
}

public sealed record EvidenceQuery(string Text, string Locale, IReadOnlyList<Guid>? MedicationIds = null, int MaxMedications = 5);

/// <summary>
/// Retrieval seam. Today it is backed by the medication knowledge service (database search, no vectors). A hybrid or vector retriever can replace it
/// later without touching the answer logic. Retrieved content is DATA: it never carries instructions and never grants access.
/// </summary>
public interface IEvidenceRetriever
{
    Task<EvidenceSet> RetrieveAsync(EvidenceQuery query, CancellationToken ct = default);
}

// ---------- answer contract ----------

public enum AnswerStatus
{
    /// <summary>Generated text that passed the safety policy, with its evidence.</summary>
    Answered,
    /// <summary>No relevant evidence was found; nothing was generated.</summary>
    NoEvidence,
    /// <summary>The assistant declines this kind of request (reason code says why); no text was generated by a model.</summary>
    Refused,
    /// <summary>Possible warning signs: a fixed, backend-written message that points to urgent help. A model was not asked.</summary>
    Escalated,
    /// <summary>A generated answer failed the safety policy and was withheld. The evidence is still shown.</summary>
    Blocked,
    /// <summary>No answer could be generated (provider disabled, not authorized, failed, timed out). The evidence is still shown.</summary>
    Unavailable,
}

public enum NextStep
{
    None,
    ConsultProfessional,
    ConsultPrescriber,
    EmergencyServices,
}

/// <param name="External">True only when the text came from an external provider (data left this system under the gate's authorization).</param>
public sealed record GenerationInfo(string Provider, AiProviderKind Kind, string? Model, bool IsMock, bool External);

/// <summary>
/// The response of the medication assistant (contract <c>ai-answer-1</c>). Evidence is kept SEPARATE from generated text; the text may cite evidence ids.
/// <see cref="Reason"/>, <see cref="Limitations"/> and <see cref="MissingInformation"/> are machine codes that clients translate.
/// There is deliberately no numeric confidence.
/// </summary>
public sealed record AssistantAnswer(
    string Text,
    string Provider,
    bool IsMock,
    bool Answered,
    IReadOnlyList<KnowledgeSourceRef> Sources,
    string Notice,
    AiErrorCode Error,
    bool PatientContextUsed = false,
    string? PatientContextNote = null,
    AnswerStatus Status = AnswerStatus.Unavailable,
    string? Reason = null,
    EvidenceSet? Evidence = null,
    EvidenceQuality EvidenceQuality = EvidenceQuality.None,
    IReadOnlyList<string>? Limitations = null,
    IReadOnlyList<string>? MissingInformation = null,
    NextStep NextStep = NextStep.None,
    GenerationInfo? Generation = null,
    string ContractVersion = "ai-answer-1");

public interface IAIAssistantService
{
    /// <summary>
    /// Answers only from medication knowledge retrieved through <see cref="IMedicationService"/> (never from the database directly).
    /// When no source exists the provider is NOT called and the answer says so.
    /// </summary>
    Task<AssistantAnswer> AskAsync(Guid actorUserId, AssistantQuestion question, string source, string? correlationId, CancellationToken ct = default);

    AiProviderStatus Status();
}
