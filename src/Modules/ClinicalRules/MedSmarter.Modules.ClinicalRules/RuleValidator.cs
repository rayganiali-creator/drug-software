using System.Text.RegularExpressions;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>Structural validation of a rule definition and the execution of a rule's own test cases. Pure; returns machine-readable codes only.</summary>
public static partial class RuleValidator
{
    public static readonly IReadOnlySet<string> Inputs = new HashSet<string>(StringComparer.Ordinal) { "medications", "allergies", "symptoms", "profile" };
    private static readonly string[] Severities = ["Minor", "Moderate", "Major", "Contraindicated"];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$")]
    private static partial Regex Id();

    private static RuleCriteriaKind KindOf(RuleDomain d) => d switch
    {
        RuleDomain.Interaction => RuleCriteriaKind.ReferenceInteraction,
        RuleDomain.AllergyConflict => RuleCriteriaKind.AllergyMedicationMatch,
        RuleDomain.DuplicateIngredient => RuleCriteriaKind.DuplicateIngredient,
        RuleDomain.DataQuality => RuleCriteriaKind.UnregisteredMedication,
        _ => RuleCriteriaKind.SevereSymptomRecent,
    };

    private static string[] RequiredFor(RuleCriteriaKind k) => k switch
    {
        RuleCriteriaKind.AllergyMedicationMatch => ["allergies", "medications"],
        RuleCriteriaKind.SevereSymptomRecent => ["symptoms"],
        _ => ["medications"],
    };

    public static IReadOnlyList<string> Validate(RuleDefinition d, DateOnly today, IReadOnlyCollection<string> guidanceTemplateKeys)
    {
        var e = new List<string>();
        if (d.RuleId is null || !Id().IsMatch(d.RuleId))
        {
            e.Add("rule.id_invalid");
        }

        if (d.Version < 1 || d.Version > 9999)
        {
            e.Add("rule.version_invalid");
        }

        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Length > 200)
        {
            e.Add("rule.title_invalid");
        }

        if (string.IsNullOrWhiteSpace(d.Purpose) || d.Purpose.Length > 500)
        {
            e.Add("rule.purpose_invalid");
        }

        if (d.Population is null || d.Criteria is null || d.RequiredInputs is null || d.Evidence is null || d.Limitations is null || d.TestCases is null)
        {
            e.Add("rule.structure_invalid");
            return e;
        }

        if (!Enum.IsDefined(d.Domain) || !Enum.IsDefined(d.Severity) || !Enum.IsDefined(d.Urgency) || !Enum.IsDefined(d.Criteria.Kind))
        {
            e.Add("rule.enum_invalid");
            return e;
        }

        if (d.Criteria.Kind != KindOf(d.Domain))
        {
            e.Add("criteria.kind_domain_mismatch");
        }

        if (d.Criteria.Kind == RuleCriteriaKind.ReferenceInteraction)
        {
            if (d.Criteria.MinReferenceSeverity is null || !Severities.Contains(d.Criteria.MinReferenceSeverity) || d.Criteria.WithinDays is not null)
            {
                e.Add("criteria.parameters_invalid");
            }
        }
        else if (d.Criteria.Kind == RuleCriteriaKind.SevereSymptomRecent)
        {
            if (d.Criteria.WithinDays is not (>= 1 and <= 90) || d.Criteria.MinReferenceSeverity is not null)
            {
                e.Add("criteria.parameters_invalid");
            }
        }
        else if (d.Criteria.MinReferenceSeverity is not null || d.Criteria.WithinDays is not null)
        {
            e.Add("criteria.parameters_invalid");
        }

        if (d.RequiredInputs.Any(i => !Inputs.Contains(i)) || d.RequiredInputs.Distinct().Count() != d.RequiredInputs.Count)
        {
            e.Add("inputs.unknown");
        }
        else if (RequiredFor(d.Criteria.Kind).Any(r => !d.RequiredInputs.Contains(r)))
        {
            e.Add("inputs.required_missing"); // a rule cannot opt out of the inputs it depends on
        }

        var p = d.Population;
        if (p.MinAgeYears is < 0 or > 130 || p.MaxAgeYears is < 0 or > 130 || (p.MinAgeYears is not null && p.MaxAgeYears is not null && p.MinAgeYears > p.MaxAgeYears) || p.Unsupported is null || p.Unsupported.Count > 20)
        {
            e.Add("population.invalid");
        }

        if (d.Evidence.Count > 20 || d.Evidence.Any(x => string.IsNullOrWhiteSpace(x.SourceId) || x.SourceId.Length > 64 || string.IsNullOrWhiteSpace(x.SourceName) || x.SourceName.Length > 200
            || string.IsNullOrWhiteSpace(x.Version) || x.Version.Length > 64 || string.IsNullOrWhiteSpace(x.ReviewStatus) || x.ReviewStatus.Length > 64 || !Enum.IsDefined(x.Validation)))
        {
            e.Add("evidence.invalid");
        }

        if (d.Evidence.Any(x => x.PublicationDate > today))
        {
            e.Add("evidence.date_in_future");
        }

        if (d.Limitations.Count > 20 || d.Limitations.Any(l => string.IsNullOrWhiteSpace(l) || l.Length > 300))
        {
            e.Add("limitations.invalid");
        }

        if (d.EmergencySigns is { Count: > 0 })
        {
            if (d.EmergencySigns.Count > 6 || d.EmergencySigns.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 200))
            {
                e.Add("emergency_signs.invalid");
            }

            if (!d.Evidence.Any(x => x.Validation == EvidenceValidation.Validated))
            {
                e.Add("emergency_signs.unsourced"); // emergency signs only with a validated source
            }
        }

        if (d.GuidanceTemplateKey is null || !guidanceTemplateKeys.Contains(d.GuidanceTemplateKey))
        {
            e.Add("guidance.template_unknown");
        }

        if (d.TestCases.Count > 20 || d.TestCases.Any(t => string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || t.Input is null || t.Input.Medications.Count > 50 || t.Input.Allergies.Count > 50 || t.Input.Symptoms.Count > 50 || t.Input.Interactions.Count > 200))
        {
            e.Add("tests.invalid");
        }

        foreach (var text in new[] { d.Title, d.Purpose }.Concat(d.Limitations).Concat(d.EmergencySigns ?? []).Concat(d.Population.Unsupported ?? []))
        {
            e.AddRange(RuleTextPolicy.Violations(text));
        }

        return [.. e.Distinct()];
    }

    /// <summary>Runs every test case of the rule through the real engine as if the rule were in force. Returns the names of failing cases.</summary>
    public static IReadOnlyList<string> RunTestCases(RuleDefinition d)
    {
        var failures = new List<string>();
        foreach (var t in d.TestCases)
        {
            try
            {
                var (evaluation, findings) = ClinicalSafetyEngine.EvaluateForTest(d, t.Input);
                if (evaluation.Outcome != t.ExpectedOutcome || findings.Count != t.ExpectedFindings)
                {
                    failures.Add(t.Name);
                }
            }
#pragma warning disable CA1031 // a crashing test case is a failing test case
            catch (Exception)
#pragma warning restore CA1031
            {
                failures.Add(t.Name);
            }
        }

        return failures;
    }
}
