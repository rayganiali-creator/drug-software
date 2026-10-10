using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.AI;

public sealed record ExternalGateResult(bool Allowed, string? Reason, ExternalProcessingGrant? Grant)
{
    public static ExternalGateResult Deny(string reason) => new(false, reason, null);
}

/// <summary>
/// The single place that decides whether anything may leave this system for an external AI provider. It FAILS CLOSED: every condition must be
/// verified, and a condition that cannot be verified (no consent evaluator, an error while checking) counts as not met. A global configuration
/// flag alone is never enough. Conditions, in this order:
///   1. the operator approved external transfer (<c>Ai:AllowExternalDataTransfer</c>) and configured an https (or loopback) URL, a key and a model;
///   2. the caller gave the <c>AiExternalProcessing</c> consent (consent is the person's own, revocable, and checked on every call);
///   3. the question passes privacy screening (no e-mail, link or long number, including hidden by invisible characters).
/// Screening reduces, but does not remove, the chance that a question carries personal data; it is not anonymisation.
/// </summary>
public sealed class ExternalProcessingGate(IOptions<AiOptions> options, IClock clock, IPurposeConsentEvaluator? consent = null)
{
    public async Task<ExternalGateResult> EvaluateAsync(Guid actorUserId, string question, CancellationToken ct)
    {
        var blockers = Blockers(options.Value);
        if (blockers.Count > 0)
        {
            return ExternalGateResult.Deny(blockers[0]);
        }

        if (consent is null)
        {
            return ExternalGateResult.Deny("external.consent_unverifiable");
        }

        try
        {
            var decision = await consent.EvaluateAsync(new PurposeConsentCheck(actorUserId, ConsentPurposes.AiExternalProcessing, []), ct);
            if (!decision.Allowed)
            {
                return ExternalGateResult.Deny("external.consent_required");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExternalGateResult.Deny("external.consent_unverifiable");
        }

        return FreeTextGuard.Problem(question, "question", int.MaxValue) is not null
            ? ExternalGateResult.Deny("external.question_looks_identifying")
            : new ExternalGateResult(true, null, new ExternalProcessingGrant(actorUserId, clock.UtcNow, true));
    }

    /// <summary>Configuration problems only (what an operator can fix). Codes, never values.</summary>
    public static IReadOnlyList<string> Blockers(AiOptions o)
    {
        var list = new List<string>();
        if (!o.AllowExternalDataTransfer)
        {
            list.Add("external.not_approved");
        }

        if (string.IsNullOrWhiteSpace(o.BaseUrl))
        {
            list.Add("external.missing_base_url");
        }
        else if (!AiOptions.IsAcceptableExternalUrl(o.BaseUrl))
        {
            list.Add("external.insecure_base_url");
        }

        if (string.IsNullOrWhiteSpace(o.ApiKey))
        {
            list.Add("external.missing_key");
        }

        if (string.IsNullOrWhiteSpace(o.Model))
        {
            list.Add("external.missing_model");
        }

        return list;
    }
}
