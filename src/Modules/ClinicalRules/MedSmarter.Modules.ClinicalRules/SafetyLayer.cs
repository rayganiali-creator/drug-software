using System.Text.RegularExpressions;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>
/// An independent check that runs AFTER the engine and never trusts it. It cannot remove or soften a finding; it can only add limitations or, when an
/// invariant is broken, mark the whole result as Failed (the findings stay visible so a serious one is never lost by the failure itself).
/// Invariants: no safety claim; no actionable finding without an Active rule; every finding is backed by an evaluation; the status agrees with the evaluations;
/// a result with findings never reports "no matches"; a result built on missing, stale or failed rules is never complete.
/// </summary>
public static class SafetyLayer
{
    public static EngineResult Apply(EngineResult r)
    {
        var broken = Check(r);
        if (broken.Count == 0)
        {
            return r;
        }

        return r with
        {
            Status = AssessmentStatus.Failed,
            Complete = false,
            Limitations = [.. r.Limitations, .. broken.Select(b => "safety.invariant_violation." + b)],
        };
    }

    public static IReadOnlyList<string> Check(EngineResult r)
    {
        var broken = new List<string>();
        if (r.SafetyClaimAllowed)
        {
            broken.Add("safety_claim");
        }

        var byRule = r.Evaluations.ToDictionary(e => (e.RuleId, e.Version));
        foreach (var f in r.Findings)
        {
            if (!byRule.TryGetValue((f.RuleId, f.RuleVersion), out var e))
            {
                broken.Add("finding_without_evaluation");
            }
            else
            {
                if (f.Actionable && e.Activation != ActivationState.Active)
                {
                    broken.Add("actionable_without_active_rule");
                }

                if (e.Outcome != RuleOutcome.Matched)
                {
                    broken.Add("finding_without_match");
                }
            }

            if (f.Evidence.Count == 0)
            {
                broken.Add("finding_without_evidence");
            }

            if (f.Subjects.Count == 0)
            {
                broken.Add("finding_without_subject");
            }
        }

        if (r.Evaluations.Where(e => e.Outcome == RuleOutcome.Matched).Any(e => r.Findings.Count(f => f.RuleId == e.RuleId && f.RuleVersion == e.Version) != e.FindingCount))
        {
            broken.Add("finding_count_mismatch");
        }

        var activeProblems = r.Evaluations.Any(e => e.Activation == ActivationState.Active && e.Outcome is RuleOutcome.MissingData or RuleOutcome.StaleData or RuleOutcome.InsufficientEvidence or RuleOutcome.Error);
        if (r.Status == AssessmentStatus.CompletedNoMatches && (r.Findings.Count > 0 || activeProblems))
        {
            broken.Add("clean_status_with_findings_or_gaps");
        }

        if (r.Complete && (activeProblems || r.Status is AssessmentStatus.NoApprovedCoverage or AssessmentStatus.Incomplete or AssessmentStatus.Failed))
        {
            broken.Add("complete_with_gaps");
        }

        return [.. broken.Distinct()];
    }
}

/// <summary>
/// Screens the free text a rule author supplies (title, purpose, limitations, emergency signs). Rules are data, not advice, and their words reach people:
/// no instruction to start, stop or change a medicine, no reassurance, no probability, no diagnosis, no triage claim, no markup.
/// </summary>
public static partial class RuleTextPolicy
{
    private static readonly string[] Forbidden =
    [
        "stop taking", "start taking", "discontinue", "do not take", "don't take", "increase the dose", "decrease the dose", "reduce the dose", "double the dose", "skip your", "switch to",
        "you are safe", "is safe", "no risk", "nothing to worry", "no need to see", "safe to take", "you have been diagnosed", "diagnosed with", "you definitely",
        "مصرف را قطع", "دارو را قطع", "قطع کنید", "مصرف نکنید", "دوز را افزایش", "دوز را کاهش", "بی‌خطر", "بدون خطر", "نگران نباشید", "تشخیص قطعی",
    ];

    [GeneratedRegex(@"\d+([.,٫]\d+)?\s*(%|٪|percent|درصد)|\b(probability|chance|odds)\b|احتمال|شانس", RegexOptions.IgnoreCase)]
    private static partial Regex Probability();

    [GeneratedRegex(@"[<>{}]|https?://|javascript:", RegexOptions.IgnoreCase)]
    private static partial Regex Markup();

    public static IReadOnlyList<string> Violations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<string>();
        var lower = text.ToLowerInvariant();
        if (Forbidden.Any(t => lower.Contains(t, StringComparison.Ordinal)))
        {
            found.Add("text.forbidden_phrase");
        }

        if (Probability().IsMatch(text))
        {
            found.Add("text.probability");
        }

        if (Markup().IsMatch(text))
        {
            found.Add("text.markup_or_link");
        }

        return found;
    }
}
