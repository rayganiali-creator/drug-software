using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.AI;

public sealed class AssistantService(ConfiguredAIProvider provider, IMedicationService medications, IAuditWriter audit, IOptions<AiOptions> options, IPatientContextService? patientContext = null) : IAIAssistantService
{
    public const int MaxQuestion = 500;
    public const int MaxDocuments = 5;
    private const string NoInfo = "No information about this is available in the medication knowledge base. Please ask a pharmacist or physician.";

    public AiProviderStatus Status()
    {
        var o = options.Value;
        var kind = provider.Kind;
        var configured = kind switch
        {
            AiProviderKind.Mock => true,
            AiProviderKind.External => o.AllowExternalDataTransfer && !string.IsNullOrWhiteSpace(o.BaseUrl) && !string.IsNullOrWhiteSpace(o.ApiKey) && !string.IsNullOrWhiteSpace(o.Model),
            _ => false,
        };
        return new AiProviderStatus(provider.Name, kind, configured, kind == AiProviderKind.Mock, o.Model, !string.IsNullOrWhiteSpace(o.ApiKey), o.AllowExternalDataTransfer);
    }

    public async Task<AssistantAnswer> AskAsync(Guid actorUserId, AssistantQuestion question, string source, string? correlationId, CancellationToken ct = default)
    {
        var text = (question.Question ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > MaxQuestion || text.Any(c => char.IsControl(c) && c is not ('\n' or '\t')) || question.Locale is not ("fa" or "en"))
        {
            throw new AssistantInputException();
        }

        // 1) Retrieval through the authorised service layer: the model never touches the database.
        var ids = question.MedicationIds is { Count: > 0 } ? [.. question.MedicationIds.Distinct().Take(MaxDocuments)] : new List<Guid>();
        if (ids.Count == 0)
        {
            ids = await RetrieveAsync(text, ct);
        }

        var documents = new List<MedicationKnowledgeDocument>();
        foreach (var id in ids)
        {
            var doc = await medications.GetKnowledgeDocumentAsync(id, false, ct);
            if (doc.Succeeded)
            {
                documents.Add(doc.Value!);
            }
        }

        // 2) No source, no answer: the provider is not called.
        if (documents.Count == 0)
        {
            return new AssistantAnswer(NoInfo, "none", false, false, [], "No sources were found.", AiErrorCode.None);
        }

        var sources = documents.SelectMany(d => d.Sources).DistinctBy(s => s.SourceId).ToList();
        var notice = documents.Any(d => d.IsDemo) ? "DEMO DATA - NOT FOR CLINICAL USE" : documents.All(d => d.Validation == ValidationStatus.Validated) ? "Based on validated sources. Not a substitute for professional advice." : "Based on information that is NOT fully validated.";

        // 2b) Patient context: only the caller's own, only consented categories, and for an external provider only with BOTH the global approval and the patient's own consent for external processing.
        var (context, contextNote) = await PatientContextAsync(actorUserId, question.IncludePatientContext, ct);
        var result = await provider.CompleteAsync(new AiRequest("medication-information", text, question.Locale, documents, options.Value.MaxTokens, context), ct);
        await audit.WriteAsync(new AuditEvent(AuditActions.AiProviderCalled, result.Succeeded ? AuditResult.Success : AuditResult.Failure, actorUserId, "ai-assistant", null, null, source, correlationId,
            result.Error.ToString(), new Dictionary<string, string> { ["provider"] = provider.Name, ["documents"] = documents.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), ["patient_context"] = context is null ? "no" : "yes" }), ct);
        if (context is not null)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.AiPatientContextUsed, AuditResult.Success, actorUserId, "ai-assistant", null, actorUserId, source, correlationId, provider.Name), ct);
        }

        return result.Succeeded
            ? new AssistantAnswer(result.Completion!.Text, result.Completion.Provider, result.Completion.IsMock, true, sources, notice, AiErrorCode.None, context is not null, contextNote)
            : new AssistantAnswer(result.SafeMessage ?? "The assistant is not available right now.", provider.Name, provider.Kind == AiProviderKind.Mock, false, sources, notice, result.Error, false, contextNote);
    }

    private async Task<(PatientContext? Context, string? Note)> PatientContextAsync(Guid actorUserId, bool requested, CancellationToken ct)
    {
        if (!requested)
        {
            return (null, null);
        }

        if (patientContext is null)
        {
            return (null, "patient_context.unavailable");
        }

        var built = await patientContext.BuildAsync(actorUserId, PatientContextPurpose.AiAssistant, ct);
        if (!built.Succeeded)
        {
            return (null, "patient_context.unavailable");
        }

        var context = built.Value!;
        var o = options.Value;
        if (provider.Kind == AiProviderKind.External && !(o.AllowExternalDataTransfer && context.ExternalProcessingConsented))
        {
            return (null, "patient_context.external_processing_consent_required"); // the question is still answered, without any patient data
        }

        var anyData = context.Medications.Count + context.Allergies.Count + context.Conditions.Count + context.RecentSymptoms.Count > 0 || context.AgeGroup != "unknown";
        return anyData ? (context, "patient_context.used") : (null, "patient_context.no_consented_data");
    }

    private const int SearchMax = 64;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "what", "which", "about", "tell", "can", "how", "does", "with", "this", "that", "from", "take", "give", "please", "info", "information",
        "من", "را", "و", "به", "در", "از", "که", "این", "آن", "برای", "با", "چیست", "چه", "است", "دارو", "درباره", "بگو",
    };

    /// <summary>Tool-style retrieval: the whole question first, then its meaningful words, each through the medication search service.</summary>
    private async Task<List<Guid>> RetrieveAsync(string text, CancellationToken ct)
    {
        var found = new List<Guid>();
        var words = text.ToLowerInvariant().Split([.. text.Where(c => !char.IsLetterOrDigit(c)).Distinct()], StringSplitOptions.RemoveEmptyEntries);
        var queries = new List<string>();
        if (text.Length is >= 2 and <= SearchMax)
        {
            queries.Add(text);
        }

        queries.AddRange(words.Where(t => t.Length >= 3 && !Stop.Contains(t)).Distinct().Take(6));
        foreach (var q in queries)
        {
            var page = await medications.SearchAsync(new MedicationSearchQuery(q, 3, 0, false), ct);
            foreach (var item in page.Items)
            {
                if (!found.Contains(item.Id))
                {
                    found.Add(item.Id);
                }
            }

            if (found.Count >= MaxDocuments)
            {
                break;
            }
        }

        return [.. found.Take(MaxDocuments)];
    }
}

public sealed class AssistantInputException : Exception;
