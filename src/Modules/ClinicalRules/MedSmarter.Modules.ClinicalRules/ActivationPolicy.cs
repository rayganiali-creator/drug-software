using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>
/// The activation policy: the only place that decides whether a rule version may produce actionable findings. Pure and deterministic.
/// A rule is Active only when ALL of these hold: status Approved; not a demonstration rule; a reviewer who is not the author with a review time;
/// an effective date that has passed and no retirement date that has passed; at least one evidence reference, none of them a demonstration fixture,
/// and at least one validated; and at least one test case. Anything else is Inactive and says why. A stored "Approved" flag alone is never enough.
/// </summary>
public static class ActivationPolicy
{
    public static ActivationDecision Evaluate(RuleRecord record, DateTimeOffset now, bool allowDemonstration)
    {
        var l = record.Lifecycle;
        var d = record.Definition;
        if (l.IsDemo)
        {
            // A demonstration rule can never become approved knowledge, whatever its stored status says.
            if (l.Status is RuleStatus.Retired or RuleStatus.Rejected)
            {
                return new ActivationDecision(ActivationState.Inactive, [$"status.{Name(l.Status)}"]);
            }

            if (l.Status == RuleStatus.Approved)
            {
                return new ActivationDecision(ActivationState.Inactive, ["policy.demo_rule_cannot_be_approved"]);
            }

            return allowDemonstration
                ? new ActivationDecision(ActivationState.DemonstrationOnly, ["demo.not_clinically_validated"])
                : new ActivationDecision(ActivationState.Inactive, ["demo.not_allowed_here"]);
        }

        if (l.Status != RuleStatus.Approved)
        {
            return new ActivationDecision(ActivationState.Inactive, [$"status.{Name(l.Status)}"]);
        }

        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(l.Reviewer) || l.ReviewedAt is null)
        {
            reasons.Add("policy.reviewer_missing");
        }
        else if (string.Equals(l.Reviewer, l.Author, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("policy.reviewer_is_author");
        }

        if (l.EffectiveFrom is null)
        {
            reasons.Add("policy.effective_date_missing");
        }
        else if (l.EffectiveFrom > now)
        {
            reasons.Add("policy.not_yet_effective");
        }

        if (l.RetiredAt is not null && l.RetiredAt <= now)
        {
            reasons.Add("policy.retired");
        }

        if (d.Evidence.Count == 0)
        {
            reasons.Add("policy.evidence_missing");
        }
        else
        {
            if (d.Evidence.Any(e => e.Validation == EvidenceValidation.Demo))
            {
                reasons.Add("policy.demo_evidence");
            }

            if (!d.Evidence.Any(e => e.Validation == EvidenceValidation.Validated))
            {
                reasons.Add("policy.no_validated_evidence");
            }
        }

        if (d.TestCases.Count == 0)
        {
            reasons.Add("policy.test_cases_missing");
        }

        return reasons.Count == 0 ? new ActivationDecision(ActivationState.Active, []) : new ActivationDecision(ActivationState.Inactive, reasons);
    }

    /// <summary>Deterministic fingerprint of which rule versions are in force (and whether demonstration rules are included). Changes whenever the rule set changes.</summary>
    public static string RuleSetVersion(IEnumerable<(RuleRecord Record, ActivationDecision Decision)> rules, bool allowDemonstration)
    {
        var lines = rules.Where(r => r.Decision.State != ActivationState.Inactive)
            .OrderBy(r => r.Record.Definition.RuleId, StringComparer.Ordinal).ThenBy(r => r.Record.Definition.Version)
            .Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Record.Definition.RuleId}:{r.Record.Definition.Version}:{r.Decision.State}"));
        var text = string.Join('\n', lines) + (allowDemonstration ? "\ndemo=on" : "\ndemo=off");
        return "rs-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    private static string Name(RuleStatus s) => s.ToString().ToLowerInvariant();
}
