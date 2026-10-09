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
public sealed record AiRequest(string Purpose, string Question, string Locale, IReadOnlyList<MedicationKnowledgeDocument> Context, int MaxTokens, PatientContext? Patient = null);

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
public sealed record AiProviderStatus(string Provider, AiProviderKind Kind, bool Configured, bool IsMock, string? Model, bool ApiKeyPresent, bool ExternalTransferApproved);

/// <param name="IncludePatientContext">Ask the assistant to use the caller's own consented patient context. Never a way to read someone else's data.</param>
public sealed record AssistantQuestion(string Question, string Locale, IReadOnlyList<Guid>? MedicationIds, bool IncludePatientContext = false);

public sealed record AssistantAnswer(string Text, string Provider, bool IsMock, bool Answered, IReadOnlyList<KnowledgeSourceRef> Sources, string Notice, AiErrorCode Error, bool PatientContextUsed = false, string? PatientContextNote = null);

public interface IAIAssistantService
{
    /// <summary>
    /// Answers only from medication knowledge retrieved through <see cref="IMedicationService"/> (never from the database directly).
    /// When no source exists the provider is NOT called and the answer says so.
    /// </summary>
    Task<AssistantAnswer> AskAsync(Guid actorUserId, AssistantQuestion question, string source, string? correlationId, CancellationToken ct = default);

    AiProviderStatus Status();
}
