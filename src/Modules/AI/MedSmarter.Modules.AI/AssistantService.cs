using System.Globalization;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.AI;

/// <summary>
/// The medication assistant. Order of work (every step can stop the request, none can be skipped):
///   1. validate the input; 2. screen the question (emergency, rule-change attempt, medicine-change or diagnosis request: fixed backend answers, no model);
///   3. retrieve evidence through the authorised services (no evidence → no model call); 4. patient context (own data, consented categories only);
///   5. for an external provider: the fail-closed gate; 6. generate; 7. check the generated text against the safety policy before it is returned;
///   8. audit with codes and counts only (never the question, the answer or the evidence text).
/// The model never reads a database, calls tools or sees anything the backend did not pick for this request.
/// </summary>
public sealed class AssistantService(
    ConfiguredAIProvider provider,
    IEvidenceRetriever retriever,
    ExternalProcessingGate gate,
    IAuditWriter audit,
    IOptions<AiOptions> options,
    IPatientContextService? patientContext = null) : IAIAssistantService
{
    public const int MaxQuestion = 500;

    public AiProviderStatus Status()
    {
        var o = options.Value;
        var kind = provider.Kind;
        var blockers = kind == AiProviderKind.External ? ExternalProcessingGate.Blockers(o) : [];
        var configured = kind switch
        {
            AiProviderKind.Mock => true,
            AiProviderKind.External => blockers.Count == 0,
            _ => false,
        };
        return new AiProviderStatus(provider.Name, kind, configured, kind == AiProviderKind.Mock, o.Model, !string.IsNullOrWhiteSpace(o.ApiKey), o.AllowExternalDataTransfer, blockers);
    }

    public async Task<AssistantAnswer> AskAsync(Guid actorUserId, AssistantQuestion question, string source, string? correlationId, CancellationToken ct = default)
    {
        var text = (question.Question ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > MaxQuestion || text.Any(c => char.IsControl(c) && c is not ('\n' or '\t')) || question.Locale is not ("fa" or "en"))
        {
            throw new AssistantInputException();
        }

        var locale = question.Locale;

        // 2) Fixed, backend-written answers. A model is never asked about a possible emergency, a rule change, a medicine change or a diagnosis.
        var screened = QuestionScreen.Classify(text);
        if (screened != QuestionClass.None)
        {
            return await ScreenedAsync(screened, locale, actorUserId, source, correlationId, ct);
        }

        // 3) Evidence.
        var evidence = await retriever.RetrieveAsync(new EvidenceQuery(text, locale, question.MedicationIds), ct);
        if (evidence.Items.Count == 0)
        {
            return await FinishAsync(actorUserId, source, correlationId, evidence, null, false, null,
                Make(SafeMessages.NoEvidence(locale), AnswerStatus.NoEvidence, "no_evidence", evidence, NextStep.ConsultProfessional, null, null), ct);
        }

        // 4) Patient context: only the caller's own, only consented categories.
        var (context, contextNote) = await PatientContextAsync(actorUserId, question.IncludePatientContext, ct);

        // 5) External provider: nothing leaves unless every condition is verified, otherwise nothing is sent and the evidence is shown on its own.
        ExternalProcessingGrant? grant = null;
        if (provider.Kind == AiProviderKind.External)
        {
            var decision = await gate.EvaluateAsync(actorUserId, text, ct);
            if (!decision.Allowed)
            {
                await audit.WriteAsync(new AuditEvent(AuditActions.AiExternalBlocked, AuditResult.Denied, actorUserId, "ai-assistant", null, actorUserId, source, correlationId, decision.Reason), ct);
                return await FinishAsync(actorUserId, source, correlationId, evidence, null, false, decision.Reason,
                    Make(SafeMessages.ExternalNotAuthorized(locale), AnswerStatus.Unavailable, decision.Reason!, evidence, NextStep.ConsultProfessional, null, null), ct);
            }

            grant = decision.Grant;
        }

        // 6) Generate.
        var evidenceForModel = evidence.Items; // quarantined and rejected items were already dropped by the retriever
        var result = await provider.CompleteAsync(new AiRequest("medication-information", text, locale, [], options.Value.MaxTokens, context, evidenceForModel, grant), ct);
        var generation = new GenerationInfo(provider.Name, provider.Kind, result.Completion?.Model, provider.Kind == AiProviderKind.Mock, provider.Kind == AiProviderKind.External);
        await audit.WriteAsync(new AuditEvent(AuditActions.AiProviderCalled, result.Succeeded ? AuditResult.Success : AuditResult.Failure, actorUserId, "ai-assistant", null, null, source, correlationId,
            result.Error.ToString(), new Dictionary<string, string>
            {
                ["provider"] = provider.Name,
                ["documents"] = evidence.Medications.Count.ToString(CultureInfo.InvariantCulture),
                ["evidence"] = evidence.Items.Count.ToString(CultureInfo.InvariantCulture),
                ["patient_context"] = context is null ? "no" : "yes",
            }), ct);
        if (context is not null)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.AiPatientContextUsed, AuditResult.Success, actorUserId, "ai-assistant", null, actorUserId, source, correlationId, provider.Name), ct);
        }

        if (!result.Succeeded)
        {
            var reason = result.Error switch
            {
                AiErrorCode.Disabled => "provider.disabled",
                AiErrorCode.NotConfigured => "provider.not_configured",
                AiErrorCode.Timeout => "provider.timeout",
                AiErrorCode.InvalidResponse => "provider.invalid_response",
                AiErrorCode.Rejected => "provider.rejected",
                _ => "provider.unavailable",
            };
            return await FinishAsync(actorUserId, source, correlationId, evidence, generation, context is not null, reason,
                Make(SafeMessages.Unavailable(locale), AnswerStatus.Unavailable, reason, evidence, NextStep.ConsultProfessional, generation, null, result.Error, contextNote: contextNote), ct);
        }

        // 7) The generated text is data until it passes the policy.
        var violations = AnswerSafetyPolicy.Check(result.Completion!.Text, [.. evidence.Items.Select(i => i.Id)]);
        if (violations.Count > 0)
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.AiAnswerBlocked, AuditResult.Denied, actorUserId, "ai-assistant", null, null, source, correlationId, string.Join(',', violations)), ct);
            return await FinishAsync(actorUserId, source, correlationId, evidence, generation, context is not null, "answer.blocked",
                Make(SafeMessages.Withheld(locale), AnswerStatus.Blocked, "answer.blocked", evidence, NextStep.ConsultProfessional, generation, null, contextNote: contextNote), ct);
        }

        return await FinishAsync(actorUserId, source, correlationId, evidence, generation, context is not null, null,
            Make(result.Completion.Text, AnswerStatus.Answered, "answered", evidence, NextStep.ConsultProfessional, generation, null, contextNote: contextNote, contextUsed: context is not null), ct);
    }

    private async Task<AssistantAnswer> ScreenedAsync(QuestionClass screened, string locale, Guid actor, string source, string? correlationId, CancellationToken ct)
    {
        // No retrieval happened for these answers, so "no source found" would be a false statement: they carry no evidence limitations at all.
        var none = EvidenceSet.Empty with { Limitations = [] };
        var answer = screened switch
        {
            QuestionClass.Emergency => Make(SafeMessages.Emergency(locale), AnswerStatus.Escalated, "emergency_signs", none, NextStep.EmergencyServices, null, ["screen.keyword_based"]),
            QuestionClass.PolicyOverride => Make(SafeMessages.PolicyOverride(locale), AnswerStatus.Refused, "policy_override", none, NextStep.None, null, null),
            QuestionClass.MedicationChange => Make(SafeMessages.MedicationChange(locale), AnswerStatus.Refused, "medication_change", none, NextStep.ConsultPrescriber, null, null),
            _ => Make(SafeMessages.Diagnosis(locale), AnswerStatus.Refused, "diagnosis_request", none, NextStep.ConsultProfessional, null, null),
        };
        return await FinishAsync(actor, source, correlationId, none, null, false, answer.Reason, answer, ct);
    }

    private async Task<AssistantAnswer> FinishAsync(Guid actor, string source, string? correlationId, EvidenceSet evidence, GenerationInfo? generation, bool contextUsed, string? reason, AssistantAnswer answer, CancellationToken ct)
    {
        // One audit line per request, whatever the outcome: codes and counts only. Never the question, the answer or any evidence text.
        await audit.WriteAsync(new AuditEvent(AuditActions.AiRequestHandled, answer.Status == AnswerStatus.Answered ? AuditResult.Success : AuditResult.Failure, actor, "ai-assistant", null, actor, source, correlationId,
            reason ?? answer.Reason, new Dictionary<string, string>
            {
                ["status"] = answer.Status.ToString(),
                ["evidence_items"] = evidence.Items.Count.ToString(CultureInfo.InvariantCulture),
                ["evidence_quality"] = evidence.Quality.ToString(),
                ["provider_kind"] = (generation?.Kind ?? AiProviderKind.Disabled).ToString(),
                ["patient_context"] = contextUsed ? "yes" : "no",
            }), ct);
        return answer;
    }

    private static AssistantAnswer Make(string text, AnswerStatus status, string reason, EvidenceSet evidence, NextStep next, GenerationInfo? generation, IReadOnlyList<string>? extraLimitations,
        AiErrorCode error = AiErrorCode.None, string? contextNote = null, bool contextUsed = false)
    {
        var sources = evidence.Items.Select(i => i.Source).DistinctBy(s => s.SourceId)
            .Select(s => new KnowledgeSourceRef(s.SourceId, s.Name, s.Version, s.Publisher, s.ReceivedAt, Enum.TryParse<ValidationStatus>(s.Validation, out var v) ? v : null)).ToList();
        var notice = evidence.Items.Count == 0 ? "No sources were found."
            : evidence.Items.Any(i => i.IsDemo) ? "DEMO DATA - NOT FOR CLINICAL USE"
            : evidence.Quality == EvidenceQuality.Validated ? "Based on validated sources. Not a substitute for professional advice."
            : "Based on information that is NOT fully validated.";
        var limitations = extraLimitations is null ? evidence.Limitations : [.. evidence.Limitations, .. extraLimitations];
        if (status != AnswerStatus.Escalated)
        {
            // Phase 7: every non-emergency answer states what it is not. The assistant gives information from sources; it never sorts how urgent a person's situation is.
            limitations = [.. limitations, "triage.not_performed"];
        }

        return new AssistantAnswer(text, generation?.Provider ?? "none", generation?.IsMock ?? false, status == AnswerStatus.Answered, sources, notice, error, contextUsed, contextNote,
            status, reason, evidence, evidence.Quality, limitations, evidence.MissingInformation, next, generation);
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
        if (provider.Kind == AiProviderKind.External && !(options.Value.AllowExternalDataTransfer && context.ExternalProcessingConsented))
        {
            return (null, "patient_context.external_processing_consent_required"); // the question is still handled, without any patient data
        }

        var anyData = context.Medications.Count + context.Allergies.Count + context.Conditions.Count + context.RecentSymptoms.Count > 0 || context.AgeGroup != "unknown";
        return anyData ? (context, "patient_context.used") : (null, "patient_context.no_consented_data");
    }
}

public sealed class AssistantInputException : Exception;
