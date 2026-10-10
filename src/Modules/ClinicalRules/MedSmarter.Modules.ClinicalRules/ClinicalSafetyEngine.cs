using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>
/// The deterministic evaluator. A pure function: the same rules and the same snapshot always give the same result. It reads no clock, no database, no network
/// and no free text; it contains no probability, no model and no learned behaviour. Matching, urgency and severity are decided here and nowhere else.
/// Principles that the code enforces (and the tests pin):
///   * missing, stale, unauthorized and unavailable input is never treated as "nothing found";
///   * a rule that is not approved never produces an actionable finding;
///   * a medicine that is not in the reference is never matched by name or guessed;
///   * "nothing matched" is reported together with its coverage, and never as "safe".
/// </summary>
public static class ClinicalSafetyEngine
{
    public const string Version = "engine-1.0.0";

    /// <summary>What nothing in this phase can check, stated on every result instead of being implied by silence.</summary>
    public static readonly IReadOnlyList<string> UnsupportedDomains =
    [
        "dose_and_route", "adherence", "laboratory_values", "condition_contraindications", "ingredient_and_free_text_allergies", "pregnancy_and_lactation", "organ_function", "food_and_alcohol",
    ];

    private sealed record Candidate(
        string KeyPart,
        IReadOnlyList<FindingSubject> Subjects,
        FindingSeverity? SeverityFloor,
        string? ReferenceSeverity,
        bool EvidenceConflict,
        IReadOnlyList<EvidenceRef> DataEvidence,
        bool DataEvidenceValidated);

    private sealed class Checked
    {
        public List<Candidate> Candidates { get; } = [];
        public List<string> NotChecked { get; } = [];
        public List<string> Missing { get; } = [];
        public List<string> Insufficient { get; } = [];
    }

    public static EngineResult Evaluate(IReadOnlyList<RuleRecord> rules, AssessmentSnapshot snapshot, bool allowDemonstration)
    {
        var now = snapshot.At;
        var decided = rules.OrderBy(r => r.Definition.RuleId, StringComparer.Ordinal).ThenBy(r => r.Definition.Version)
            .Select(r => (Record: r, Decision: ActivationPolicy.Evaluate(r, now, allowDemonstration))).ToList();

        // Of several versions of one rule only the newest one that is in force runs; older ones are reported as superseded, never silently dropped.
        var newest = decided.Where(d => d.Decision.State != ActivationState.Inactive).GroupBy(d => d.Record.Definition.RuleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Record.Definition.Version), StringComparer.Ordinal);

        var evaluations = new List<RuleEvaluation>();
        var findings = new List<Finding>();
        foreach (var (record, decision) in decided)
        {
            var d = record.Definition;
            if (decision.State == ActivationState.Inactive)
            {
                evaluations.Add(Evaluation(record, decision, RuleOutcome.Unavailable, decision.Reasons, false, 0));
                continue;
            }

            if (newest[d.RuleId] != d.Version)
            {
                evaluations.Add(Evaluation(record, decision, RuleOutcome.Unavailable, ["policy.superseded_by_newer_version"], false, 0));
                continue;
            }

            try
            {
                var (evaluation, produced) = EvaluateRule(record, decision, snapshot);
                evaluations.Add(evaluation);
                findings.AddRange(produced);
            }
#pragma warning disable CA1031 // a failing rule must be reported as Error and must not stop the other rules or hide itself
            catch (Exception)
#pragma warning restore CA1031
            {
                evaluations.Add(Evaluation(record, decision, RuleOutcome.Error, ["engine.rule_error"], false, 0));
            }
        }

        var ordered = findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Urgency).ThenBy(f => f.RuleId, StringComparer.Ordinal).ThenBy(f => f.Key, StringComparer.Ordinal).ToList();
        var active = evaluations.Where(e => e.Activation == ActivationState.Active).ToList();
        var demo = evaluations.Count(e => e.Activation == ActivationState.DemonstrationOnly);
        var problems = active.Count(e => e.Outcome is RuleOutcome.MissingData or RuleOutcome.StaleData or RuleOutcome.InsufficientEvidence or RuleOutcome.Error);
        var activeFindings = ordered.Where(f => decided.Any(x => x.Record.Definition.RuleId == f.RuleId && x.Record.Definition.Version == f.RuleVersion && x.Decision.State == ActivationState.Active)).ToList();

        var status =
            active.Count == 0 ? AssessmentStatus.NoApprovedCoverage
            : activeFindings.Count > 0 ? AssessmentStatus.CompletedWithFindings
            : problems > 0 ? AssessmentStatus.Incomplete
            : AssessmentStatus.CompletedNoMatches;
        var complete = active.Count > 0 && problems == 0 && active.All(e => !e.Partial);

        var limitations = new List<string> { "engine.not_a_diagnosis", "engine.not_triage", "engine.clean_result_is_not_safety" };
        if (active.Count == 0)
        {
            limitations.Add("coverage.no_approved_rules");
        }

        if (demo > 0 || ordered.Any(f => f.IsDemo))
        {
            limitations.Add("demo.rules_not_clinically_validated");
        }

        if (!complete && active.Count > 0)
        {
            limitations.Add("coverage.incomplete_inputs_or_items");
        }

        var coverage = new AssessmentCoverage(
            active.Count, demo, evaluations.Count(e => e.Activation == ActivationState.Inactive), problems,
            [.. active.Select(e => e.Domain).Distinct().Order()], UnsupportedDomains);
        return new EngineResult(status, complete, snapshot.Inputs, evaluations, ordered, coverage, limitations, Version,
            ActivationPolicy.RuleSetVersion(decided, allowDemonstration), now, demo > 0 || ordered.Any(f => f.IsDemo), false);
    }

    /// <summary>Runs one rule definition as if it were in force (used for a rule's own test cases and by the approval step). Never used to decide what is actionable.</summary>
    public static (RuleEvaluation Evaluation, IReadOnlyList<Finding> Findings) EvaluateForTest(RuleDefinition definition, AssessmentSnapshot snapshot)
    {
        var lifecycle = new RuleLifecycle(RuleStatus.Approved, false, "test-author", snapshot.At, "test-reviewer", snapshot.At, null, snapshot.At.AddDays(-1), null);
        return EvaluateRule(new RuleRecord(definition, lifecycle), new ActivationDecision(ActivationState.Active, []), snapshot);
    }

    private static RuleEvaluation Evaluation(RuleRecord r, ActivationDecision decision, RuleOutcome outcome, IReadOnlyList<string> reasons, bool partial, int findings) =>
        new(r.Definition.RuleId, r.Definition.Version, r.Definition.Domain, outcome, reasons, decision.State, r.Lifecycle.IsDemo, partial, findings);

    private static (RuleEvaluation Evaluation, IReadOnlyList<Finding> Findings) EvaluateRule(RuleRecord record, ActivationDecision decision, AssessmentSnapshot s)
    {
        var d = record.Definition;

        // Population first: a rule outside its population says "not applicable"; an unknown age for an age-limited rule says "missing", never "fine".
        if (d.Population.MinAgeYears is not null || d.Population.MaxAgeYears is not null)
        {
            if (s.AgeYears is null)
            {
                return (Evaluation(record, decision, RuleOutcome.MissingData, ["input.age_unknown"], false, 0), []);
            }

            if (s.AgeYears < d.Population.MinAgeYears || s.AgeYears > d.Population.MaxAgeYears)
            {
                return (Evaluation(record, decision, RuleOutcome.NotApplicable, ["population.out_of_range"], false, 0), []);
            }
        }

        var missing = new List<string>();
        var stale = false;
        foreach (var category in d.RequiredInputs.Order(StringComparer.Ordinal))
        {
            var input = s.Inputs.FirstOrDefault(i => string.Equals(i.Category, category, StringComparison.Ordinal));
            if (input is null)
            {
                missing.Add($"input.{category}.not_provided");
                continue;
            }

            switch (input.Availability)
            {
                case InputAvailability.NotAuthorized: missing.Add($"input.{category}.not_authorized"); break;
                case InputAvailability.Unavailable: missing.Add($"input.{category}.unavailable"); break;
                case InputAvailability.NeverRecorded: missing.Add($"input.{category}.never_recorded"); break;
                default: stale |= input.IsStale; break;
            }
        }

        if (missing.Count > 0)
        {
            return (Evaluation(record, decision, RuleOutcome.MissingData, missing, false, 0), []);
        }

        var result = d.Criteria.Kind switch
        {
            RuleCriteriaKind.ReferenceInteraction => Interactions(d, s),
            RuleCriteriaKind.AllergyMedicationMatch => Allergies(s),
            RuleCriteriaKind.DuplicateIngredient => Duplicates(s),
            RuleCriteriaKind.UnregisteredMedication => Unregistered(s),
            RuleCriteriaKind.SevereSymptomRecent => Symptoms(d, s),
            _ => throw new InvalidOperationException("unknown criteria kind"),
        };

        var produced = result.Candidates.Select(c => ToFinding(record, decision, c, stale)).ToList();
        var reasons = new List<string>();
        reasons.AddRange(result.Missing);
        reasons.AddRange(result.Insufficient);
        if (stale && produced.Count == 0)
        {
            reasons.Add("input.stale");
        }

        reasons.AddRange(result.NotChecked);
        var partial = result.NotChecked.Count > 0;
        RuleOutcome outcome;
        if (produced.Count > 0)
        {
            outcome = RuleOutcome.Matched;
            partial |= result.Missing.Count > 0 || result.Insufficient.Count > 0;
        }
        else if (result.Insufficient.Count > 0)
        {
            outcome = RuleOutcome.InsufficientEvidence;
        }
        else if (result.Missing.Count > 0)
        {
            outcome = RuleOutcome.MissingData;
        }
        else if (stale)
        {
            outcome = RuleOutcome.StaleData;
        }
        else
        {
            outcome = RuleOutcome.NoMatch;
        }

        return (Evaluation(record, decision, outcome, reasons.Distinct().Order(StringComparer.Ordinal).ToList(), partial, produced.Count), produced);
    }

    // ---------- criteria ----------

    private static bool IsEvaluable(SnapshotMedication m) => m.IsRegistered && m.ReferenceFound && m.IngredientIds.Count > 0;

    private static int Rank(string? severity) => severity switch
    {
        "Minor" => 1,
        "Moderate" => 2,
        "Major" => 3,
        "Contraindicated" => 4,
        _ => 0,
    };

    private static FindingSeverity MapSeverity(int rank) => rank switch
    {
        1 => FindingSeverity.Minor,
        2 => FindingSeverity.Moderate,
        3 => FindingSeverity.Major,
        4 => FindingSeverity.Critical,
        _ => FindingSeverity.Informational,
    };

    private static Checked Interactions(RuleDefinition d, AssessmentSnapshot s)
    {
        var r = new Checked();
        var meds = s.Medications.Where(m => m.IsActive).OrderBy(m => m.Id).ToList();
        var evaluable = meds.Where(IsEvaluable).ToList();
        if (meds.Count >= 2 && evaluable.Count < 2)
        {
            r.Missing.Add("reference.insufficient_for_comparison");
            return r;
        }

        if (evaluable.Count < meds.Count)
        {
            r.NotChecked.Add("medication.not_in_reference");
        }

        var minRank = Math.Max(1, Rank(d.Criteria.MinReferenceSeverity));
        var byPair = s.Interactions.GroupBy(i => Pair(i.IngredientA, i.IngredientB)).ToDictionary(g => g.Key, g => g.ToList());
        for (var i = 0; i < evaluable.Count; i++)
        {
            for (var j = i + 1; j < evaluable.Count; j++)
            {
                var entries = new List<SnapshotInteraction>();
                foreach (var a in evaluable[i].IngredientIds)
                {
                    foreach (var b in evaluable[j].IngredientIds)
                    {
                        if (a != b && byPair.TryGetValue(Pair(a, b), out var found))
                        {
                            entries.AddRange(found);
                        }
                    }
                }

                var usable = entries.Where(e => e.Validation != "Rejected").ToList();
                if (usable.Count < entries.Count)
                {
                    r.NotChecked.Add("evidence.rejected_entry_ignored");
                }

                if (usable.Any(e => Rank(e.Severity) == 0))
                {
                    r.Insufficient.Add("evidence.severity_unknown");
                }

                var ranked = usable.Where(e => Rank(e.Severity) > 0).ToList();
                var matching = ranked.Where(e => Rank(e.Severity) >= minRank).ToList();
                if (matching.Count == 0)
                {
                    continue;
                }

                var top = matching.Max(e => Rank(e.Severity));
                var conflict = ranked.Select(e => Rank(e.Severity)).Distinct().Count() > 1;
                r.Candidates.Add(new Candidate(
                    $"{evaluable[i].Id:N}+{evaluable[j].Id:N}",
                    [new FindingSubject("medication", evaluable[i].Id, evaluable[i].DisplayName), new FindingSubject("medication", evaluable[j].Id, evaluable[j].DisplayName)],
                    MapSeverity(top), matching.First(e => Rank(e.Severity) == top).Severity, conflict, [.. matching.Select(e => e.Evidence)], matching.All(e => e.Validation == "Validated")));
            }
        }

        return r;
    }

    private static Checked Allergies(AssessmentSnapshot s)
    {
        var r = new Checked();
        var meds = s.Medications.Where(m => m.IsActive).OrderBy(m => m.Id).ToList();
        var checkable = s.Allergies.Where(a => a.Kind == "Medication" && a.ReferenceMedicationId is not null).OrderBy(a => a.Id).ToList();
        var free = s.Allergies.Count - checkable.Count;
        if (free > 0 && checkable.Count == 0)
        {
            r.Insufficient.Add("allergy.free_text_not_matchable");
            return r;
        }

        if (free > 0)
        {
            r.NotChecked.Add("allergy.free_text_not_matchable");
        }

        if (meds.Count > 0 && meds.All(m => !m.IsRegistered || m.ReferenceMedicationId is null) && checkable.Count > 0)
        {
            r.Missing.Add("reference.no_evaluable_medication");
            return r;
        }

        foreach (var a in checkable)
        {
            foreach (var m in meds)
            {
                var same = m.ReferenceMedicationId == a.ReferenceMedicationId;
                var shared = a.IngredientIds.Count > 0 && m.IngredientIds.Count > 0 && a.IngredientIds.Intersect(m.IngredientIds).Any();
                if (same || shared)
                {
                    r.Candidates.Add(new Candidate(
                        $"{a.Id:N}+{m.Id:N}", [new FindingSubject("allergy", a.Id, a.Substance), new FindingSubject("medication", m.Id, m.DisplayName)], null, null, false, [], true));
                }
                else if ((!a.ReferenceFound || !m.ReferenceFound) && m.IsRegistered)
                {
                    r.NotChecked.Add("allergy.reference_unreadable");
                }
            }
        }

        if (meds.Any(m => !IsEvaluable(m)))
        {
            r.NotChecked.Add("medication.not_in_reference");
        }

        return r;
    }

    private static Checked Duplicates(AssessmentSnapshot s)
    {
        var r = new Checked();
        var meds = s.Medications.Where(m => m.IsActive).OrderBy(m => m.Id).ToList();
        var evaluable = meds.Where(IsEvaluable).ToList();
        if (meds.Count >= 2 && evaluable.Count < 2)
        {
            r.Missing.Add("reference.insufficient_for_comparison");
            return r;
        }

        if (evaluable.Count < meds.Count)
        {
            r.NotChecked.Add("medication.not_in_reference");
        }

        var sets = evaluable.SelectMany(m => m.IngredientIds.Distinct().Select(ing => (Ingredient: ing, Medication: m)))
            .GroupBy(x => x.Ingredient).Where(g => g.Select(x => x.Medication.Id).Distinct().Count() > 1)
            .Select(g => g.Select(x => x.Medication).DistinctBy(m => m.Id).OrderBy(m => m.Id).ToList())
            .GroupBy(list => string.Join('+', list.Select(m => m.Id.ToString("N", CultureInfo.InvariantCulture)))).Select(g => g.First());
        foreach (var set in sets.OrderBy(x => string.Join('+', x.Select(m => m.Id)), StringComparer.Ordinal))
        {
            r.Candidates.Add(new Candidate(string.Join('+', set.Select(m => m.Id.ToString("N", CultureInfo.InvariantCulture))), [.. set.Select(m => new FindingSubject("medication", m.Id, m.DisplayName))], null, null, false, [], true));
        }

        return r;
    }

    private static Checked Unregistered(AssessmentSnapshot s)
    {
        var r = new Checked();
        foreach (var m in s.Medications.Where(m => m.IsActive && !IsEvaluable(m)).OrderBy(m => m.Id))
        {
            r.Candidates.Add(new Candidate($"{m.Id:N}", [new FindingSubject("medication", m.Id, m.DisplayName)], null, null, false, [], true));
        }

        return r;
    }

    private static Checked Symptoms(RuleDefinition d, AssessmentSnapshot s)
    {
        var r = new Checked();
        var days = d.Criteria.WithinDays ?? 7;
        var from = s.At.AddDays(-days);
        foreach (var sym in s.Symptoms.Where(x => x.Severity == "Severe" && x.ResolvedAt is null && x.OnsetAt >= from && x.OnsetAt <= s.At.AddDays(1)).OrderBy(x => x.Id))
        {
            r.Candidates.Add(new Candidate($"{sym.Id:N}", [new FindingSubject("symptom", sym.Id, "recorded symptom")], null, null, false, [], true));
        }

        return r;
    }

    // ---------- findings ----------

    private static Finding ToFinding(RuleRecord record, ActivationDecision decision, Candidate c, bool stale)
    {
        var d = record.Definition;
        var active = decision.State == ActivationState.Active;
        var dataDemo = c.DataEvidence.Any(e => e.Validation == EvidenceValidation.Demo);
        var severity = c.SeverityFloor is { } f && f > d.Severity ? f : d.Severity;
        var limitations = new List<string>(d.Limitations);
        if (!c.DataEvidenceValidated)
        {
            limitations.Add("reference.not_validated");
        }

        if (stale)
        {
            limitations.Add("input.stale");
        }

        if (c.EvidenceConflict)
        {
            limitations.Add("evidence.conflicting_entries");
        }

        var sourced = d.EmergencySigns is { Count: > 0 } && active && d.Evidence.Any(e => e.Validation == EvidenceValidation.Validated);
        var evidence = d.Evidence.Concat(c.DataEvidence).DistinctBy(e => (e.SourceId, e.Version)).ToList();
        var key = "f-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{d.RuleId}|{d.Version}|{c.KeyPart}"))))[..16];
        return new Finding(key, d.RuleId, d.Version, d.Domain, severity, d.Urgency, active && c.DataEvidenceValidated, record.Lifecycle.IsDemo || dataDemo, d.GuidanceTemplateKey,
            c.Subjects, evidence, c.ReferenceSeverity, c.EvidenceConflict, stale, limitations.Distinct().ToList(), sourced ? d.EmergencySigns : null);
    }

    private static (Guid, Guid) Pair(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
}
