using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;
using F = MedSmarter.Modules.ClinicalRules.SnapshotFactory;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 7: the pure, deterministic engine. No database, no network, no model. Synthetic data only; passing these tests is not clinical validation.</summary>
public class ClinicalEngineTests
{
    private static readonly DateTimeOffset T0 = F.FixtureNow;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly EvidenceRef TestSource = new("test-fixture", "TEST FIXTURE (not a real source)", "1", null, EvidenceValidation.Validated, "fixture", null);

    private static RuleDefinition Def(string demoId, string newId) =>
        DemoRules.All(T0).Single(r => r.Definition.RuleId == demoId).Definition with { RuleId = newId, Evidence = [TestSource] };

    private static RuleRecord Active(RuleDefinition d, string author = "author-1", string reviewer = "reviewer-1") =>
        new(d, new RuleLifecycle(RuleStatus.Approved, false, author, T0.AddDays(-30), reviewer, T0.AddDays(-20), null, T0.AddDays(-10), null));

    private static RuleRecord WithStatus(RuleRecord r, RuleStatus s) => r with { Lifecycle = r.Lifecycle with { Status = s } };

    private static RuleRecord Interaction(string min = "Moderate") => Active(Def("DEMO-INT-001", "T-INT") with { Criteria = new RuleCriteria(RuleCriteriaKind.ReferenceInteraction, min) });
    private static RuleRecord Allergy() => Active(Def("DEMO-ALG-001", "T-ALG"));
    private static RuleRecord Duplicate() => Active(Def("DEMO-DUP-001", "T-DUP"));
    private static RuleRecord Unregistered() => Active(Def("DEMO-DQ-001", "T-DQ"));
    private static RuleRecord Symptom() => Active(Def("DEMO-SYM-001", "T-SYM"));
    private static RuleRecord[] AllActive() => [Interaction(), Allergy(), Duplicate(), Unregistered(), Symptom()];

    private static EngineResult Run(IReadOnlyList<RuleRecord> rules, AssessmentSnapshot s, bool demo = false) => ClinicalSafetyEngine.Evaluate(rules, s, demo);

    private static RuleEvaluation Eval(EngineResult r, string id) => r.Evaluations.Single(e => e.RuleId == id);

    private static AssessmentSnapshot Pair(params SnapshotInteraction[] interactions) =>
        F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [12])], interactions: interactions);

    // ---------- the built-in rules' own examples ----------

    [Fact]
    public void Every_demonstration_rule_passes_its_own_test_cases()
    {
        foreach (var r in DemoRules.All(T0))
        {
            Assert.NotEmpty(r.Definition.TestCases);
            Assert.Empty(RuleValidator.RunTestCases(r.Definition));
        }
    }

    [Fact]
    public void Demonstration_rules_are_labelled_and_can_never_be_active()
    {
        foreach (var r in DemoRules.All(T0))
        {
            Assert.True(r.Lifecycle.IsDemo);
            Assert.NotEqual(RuleStatus.Approved, r.Lifecycle.Status);
            Assert.Null(r.Lifecycle.Reviewer);
            Assert.StartsWith("DEMO", r.Definition.Title, StringComparison.Ordinal);
            Assert.All(r.Definition.Evidence, e => Assert.Equal(EvidenceValidation.Demo, e.Validation));
            Assert.Null(r.Definition.EmergencySigns);
            // even if someone stored "Approved" on a demonstration rule, the policy refuses it
            var forced = WithStatus(r, RuleStatus.Approved);
            Assert.Equal(ActivationState.Inactive, ActivationPolicy.Evaluate(forced, T0, true).State);
        }
    }

    // ---------- coverage and "never safe" ----------

    [Fact]
    public void Without_any_approved_rule_the_result_says_there_is_no_coverage_and_never_claims_safety()
    {
        var r = Run([], F.Make(meds: [F.Med(1, "Alpha", [11])]));
        Assert.Equal(AssessmentStatus.NoApprovedCoverage, r.Status);
        Assert.False(r.Complete);
        Assert.False(r.SafetyClaimAllowed);
        Assert.Contains("coverage.no_approved_rules", r.Limitations);
        Assert.Contains("engine.clean_result_is_not_safety", r.Limitations);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void A_clean_result_still_lists_coverage_limits_and_unsupported_domains()
    {
        var r = Run(AllActive(), F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [12])], allergies: [F.Allergy(1, "Medication", 9, [99])]));
        Assert.Equal(AssessmentStatus.CompletedNoMatches, r.Status);
        Assert.False(r.SafetyClaimAllowed);
        Assert.Contains("engine.clean_result_is_not_safety", r.Limitations);
        Assert.Contains("dose_and_route", r.Coverage.UnsupportedDomains);
        Assert.Contains("adherence", r.Coverage.UnsupportedDomains);
        Assert.Equal(5, r.Coverage.ActiveRules);
    }

    [Theory]
    [InlineData(RuleStatus.Draft)]
    [InlineData(RuleStatus.UnderReview)]
    [InlineData(RuleStatus.Rejected)]
    [InlineData(RuleStatus.Retired)]
    public void Rules_that_are_not_approved_are_unavailable_and_produce_nothing(RuleStatus status)
    {
        var rule = WithStatus(Duplicate(), status);
        var r = Run([rule], F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [11])]));
        var e = Eval(r, "T-DUP");
        Assert.Equal(RuleOutcome.Unavailable, e.Outcome);
        Assert.Contains($"status.{status.ToString().ToLowerInvariant()}", e.Reasons);
        Assert.Empty(r.Findings);
        Assert.Equal(AssessmentStatus.NoApprovedCoverage, r.Status);
    }

    [Fact]
    public void An_approved_rule_must_still_meet_the_whole_activation_policy()
    {
        var d = Duplicate();
        ActivationDecision Decide(RuleRecord x) => ActivationPolicy.Evaluate(x, T0, false);

        Assert.Equal(ActivationState.Active, Decide(d).State);
        Assert.Contains("policy.reviewer_is_author", Decide(d with { Lifecycle = d.Lifecycle with { Reviewer = d.Lifecycle.Author } }).Reasons);
        Assert.Contains("policy.reviewer_missing", Decide(d with { Lifecycle = d.Lifecycle with { Reviewer = null } }).Reasons);
        Assert.Contains("policy.reviewer_missing", Decide(d with { Lifecycle = d.Lifecycle with { ReviewedAt = null } }).Reasons);
        Assert.Contains("policy.effective_date_missing", Decide(d with { Lifecycle = d.Lifecycle with { EffectiveFrom = null } }).Reasons);
        Assert.Contains("policy.not_yet_effective", Decide(d with { Lifecycle = d.Lifecycle with { EffectiveFrom = T0.AddDays(1) } }).Reasons);
        Assert.Contains("policy.retired", Decide(d with { Lifecycle = d.Lifecycle with { RetiredAt = T0 } }).Reasons);
        Assert.Contains("policy.evidence_missing", Decide(d with { Definition = d.Definition with { Evidence = [] } }).Reasons);
        Assert.Contains("policy.demo_evidence", Decide(d with { Definition = d.Definition with { Evidence = [TestSource, DemoRules.NoSource] } }).Reasons);
        Assert.Contains("policy.no_validated_evidence", Decide(d with { Definition = d.Definition with { Evidence = [TestSource with { Validation = EvidenceValidation.Unverified }] } }).Reasons);
        Assert.Contains("policy.test_cases_missing", Decide(d with { Definition = d.Definition with { TestCases = [] } }).Reasons);
    }

    [Fact]
    public void A_rule_that_is_not_yet_effective_or_already_retired_is_not_evaluated()
    {
        var future = Duplicate() with { Lifecycle = Duplicate().Lifecycle with { EffectiveFrom = T0.AddDays(2) } };
        var r = Run([future], F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [11])]));
        Assert.Equal(RuleOutcome.Unavailable, Eval(r, "T-DUP").Outcome);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void Only_the_newest_active_version_of_a_rule_runs()
    {
        var v1 = Duplicate();
        var v2 = v1 with { Definition = v1.Definition with { Version = 2 } };
        var r = Run([v1, v2], F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [11])]));
        Assert.Equal(RuleOutcome.Unavailable, r.Evaluations.Single(e => e.Version == 1).Outcome);
        Assert.Contains("policy.superseded_by_newer_version", r.Evaluations.Single(e => e.Version == 1).Reasons);
        Assert.Equal(RuleOutcome.Matched, r.Evaluations.Single(e => e.Version == 2).Outcome);
        Assert.All(r.Findings, f => Assert.Equal(2, f.RuleVersion));
    }

    // ---------- demonstration ----------

    [Fact]
    public void Demonstration_rules_run_only_where_allowed_and_their_findings_are_never_actionable()
    {
        var rules = DemoRules.All(T0);
        var snap = F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [11])]);

        var off = Run(rules, snap, demo: false);
        Assert.All(off.Evaluations, e => Assert.Equal(RuleOutcome.Unavailable, e.Outcome));
        Assert.Empty(off.Findings);

        var on = Run(rules, snap, demo: true);
        Assert.Equal(AssessmentStatus.NoApprovedCoverage, on.Status); // nothing approved is in force
        Assert.True(on.ContainsDemonstration);
        Assert.NotEmpty(on.Findings);
        Assert.All(on.Findings, f => { Assert.False(f.Actionable); Assert.True(f.IsDemo); });
        Assert.Contains("demo.rules_not_clinically_validated", on.Limitations);
    }

    // ---------- interactions ----------

    [Fact]
    public void A_validated_reference_entry_between_two_registered_medicines_gives_an_actionable_finding_with_evidence()
    {
        var r = Run([Interaction()], Pair(F.Interaction(11, 12, "Major")));
        var f = Assert.Single(r.Findings);
        Assert.True(f.Actionable);
        Assert.False(f.IsDemo);
        Assert.Equal(FindingSeverity.Major, f.Severity); // never below what the reference says
        Assert.Equal("Major", f.ReferenceSeverity);
        Assert.Contains(f.Evidence, e => e.SourceId == "fixture-reference");
        Assert.Contains(f.Evidence, e => e.SourceId == "test-fixture");
        Assert.Equal(AssessmentStatus.CompletedWithFindings, r.Status);
        Assert.Equal(2, f.Subjects.Count);
    }

    [Fact]
    public void A_contraindicated_reference_entry_becomes_a_critical_finding_and_stays_first()
    {
        var r = Run(AllActive(), F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [12]), F.Med(3, "Gamma", registered: false)], interactions: [F.Interaction(11, 12, "Contraindicated")]));
        Assert.Equal(FindingSeverity.Critical, r.Findings[0].Severity);
        Assert.Contains(r.Findings, f => f.Domain == RuleDomain.DataQuality); // lower-severity findings do not displace it, and it does not displace them
    }

    [Fact]
    public void Reference_severity_below_the_rule_minimum_does_not_match()
    {
        var r = Run([Interaction("Major")], Pair(F.Interaction(11, 12, "Moderate")));
        Assert.Equal(RuleOutcome.NoMatch, Eval(r, "T-INT").Outcome);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void An_interaction_entry_based_on_unvalidated_reference_data_is_shown_but_not_actionable()
    {
        var r = Run([Interaction()], Pair(F.Interaction(11, 12, "Major", validation: "Demo")));
        var f = Assert.Single(r.Findings);
        Assert.False(f.Actionable);
        Assert.True(f.IsDemo);
        Assert.Contains("reference.not_validated", f.Limitations);
    }

    [Fact]
    public void Conflicting_entries_keep_the_highest_severity_and_say_so()
    {
        var r = Run([Interaction()], Pair(F.Interaction(11, 12, "Moderate"), F.Interaction(12, 11, "Major")));
        var f = Assert.Single(r.Findings);
        Assert.True(f.EvidenceConflict);
        Assert.Equal(FindingSeverity.Major, f.Severity);
        Assert.Contains("evidence.conflicting_entries", f.Limitations);
    }

    [Fact]
    public void A_rejected_reference_entry_is_ignored_and_reported()
    {
        var r = Run([Interaction()], Pair(F.Interaction(11, 12, "Major", "Rejected")));
        Assert.Empty(r.Findings);
        Assert.Contains("evidence.rejected_entry_ignored", Eval(r, "T-INT").Reasons);
        Assert.True(Eval(r, "T-INT").Partial);
        Assert.False(r.Complete);
    }

    [Fact]
    public void An_entry_with_unknown_severity_is_insufficient_evidence_not_no_match()
    {
        var r = Run([Interaction()], Pair(F.Interaction(11, 12, "Unknown")));
        Assert.Equal(RuleOutcome.InsufficientEvidence, Eval(r, "T-INT").Outcome);
        Assert.Equal(AssessmentStatus.Incomplete, r.Status);
    }

    [Fact]
    public void A_medicine_outside_the_reference_is_never_matched_by_name_and_makes_the_check_missing_data()
    {
        var snap = F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Alpha", registered: false)], interactions: [F.Interaction(11, 12, "Major")]);
        var r = Run([Interaction()], snap);
        Assert.Equal(RuleOutcome.MissingData, Eval(r, "T-INT").Outcome);
        Assert.Contains("reference.insufficient_for_comparison", Eval(r, "T-INT").Reasons);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void One_unregistered_medicine_among_checkable_ones_makes_the_result_partial_but_keeps_real_findings()
    {
        var snap = F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [12]), F.Med(3, "Unknownol", registered: false)], interactions: [F.Interaction(11, 12, "Moderate")]);
        var r = Run([Interaction()], snap);
        var e = Eval(r, "T-INT");
        Assert.Equal(RuleOutcome.Matched, e.Outcome);
        Assert.True(e.Partial);
        Assert.Contains("medication.not_in_reference", e.Reasons);
        Assert.False(r.Complete);
    }

    [Fact]
    public void Inactive_medicines_are_not_compared()
    {
        var snap = F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [12], active: false)], interactions: [F.Interaction(11, 12, "Major")]);
        var r = Run([Interaction()], snap);
        Assert.Equal(RuleOutcome.NoMatch, Eval(r, "T-INT").Outcome);
    }

    [Fact]
    public void A_single_medicine_is_a_genuine_no_match_for_interactions()
    {
        var r = Run([Interaction()], F.Make(meds: [F.Med(1, "Alpha", [11])]));
        Assert.Equal(RuleOutcome.NoMatch, Eval(r, "T-INT").Outcome);
    }

    // ---------- allergies ----------

    [Fact]
    public void A_medication_linked_allergy_matches_the_same_reference_medicine_or_a_shared_ingredient_id()
    {
        var same = Run([Allergy()], F.Make(meds: [F.Med(1, "Alpha", [11])], allergies: [F.Allergy(1, "Medication", 1)]));
        Assert.Equal(RuleOutcome.Matched, Eval(same, "T-ALG").Outcome);
        Assert.True(same.Findings[0].Actionable);
        Assert.Equal(FindingSeverity.Major, same.Findings[0].Severity);

        var shared = Run([Allergy()], F.Make(meds: [F.Med(1, "Alpha", [11])], allergies: [F.Allergy(1, "Medication", 7, [11])]));
        Assert.Equal(RuleOutcome.Matched, Eval(shared, "T-ALG").Outcome);
    }

    [Fact]
    public void Free_text_allergies_are_never_matched_and_the_rule_says_it_cannot_check_them()
    {
        var snap = F.Make(meds: [F.Med(1, "Alpha", [11])], allergies: [F.Allergy(1, "ActiveIngredient", null), F.Allergy(2, "Other", null)]);
        var r = Run([Allergy()], snap);
        Assert.Equal(RuleOutcome.InsufficientEvidence, Eval(r, "T-ALG").Outcome);
        Assert.Contains("allergy.free_text_not_matchable", Eval(r, "T-ALG").Reasons);
        Assert.Empty(r.Findings);
        Assert.Equal(AssessmentStatus.Incomplete, r.Status);
    }

    [Fact]
    public void Free_text_allergies_next_to_checkable_ones_make_a_no_match_partial()
    {
        var snap = F.Make(meds: [F.Med(1, "Alpha", [11])], allergies: [F.Allergy(1, "Medication", 9, [99]), F.Allergy(2, "Other", null)]);
        var r = Run([Allergy()], snap);
        var e = Eval(r, "T-ALG");
        Assert.Equal(RuleOutcome.NoMatch, e.Outcome);
        Assert.True(e.Partial);
        Assert.False(r.Complete);
    }

    // ---------- duplicates, data quality, symptoms ----------

    [Fact]
    public void Duplicate_ingredients_are_found_by_id_only_never_by_name()
    {
        var byId = Run([Duplicate()], F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [11, 12])]));
        Assert.Single(byId.Findings);

        var sameNameDifferentIds = Run([Duplicate()], F.Make(meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Alpha", [12])]));
        Assert.Equal(RuleOutcome.NoMatch, Eval(sameNameDifferentIds, "T-DUP").Outcome);
    }

    [Fact]
    public void Three_medicines_sharing_one_ingredient_give_one_finding_with_all_three()
    {
        var r = Run([Duplicate()], F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11]), F.Med(3, "C", [11])]));
        Assert.Equal(3, Assert.Single(r.Findings).Subjects.Count);
    }

    [Fact]
    public void Unregistered_and_incomplete_reference_medicines_are_reported_as_data_quality()
    {
        var snap = F.Make(meds: [F.Med(1, "Unknownol", registered: false), F.Med(2, "Gone", [12], referenceFound: false), F.Med(3, "Fine", [13])]);
        var r = Run([Unregistered()], snap);
        Assert.Equal(2, r.Findings.Count);
        Assert.All(r.Findings, f => Assert.Equal(FindingSeverity.Informational, f.Severity));
    }

    [Theory]
    [InlineData("Severe", 2, false, 1)]
    [InlineData("Severe", 7, false, 1)]
    [InlineData("Severe", 8, false, 0)]
    [InlineData("Severe", 2, true, 0)]
    [InlineData("Moderate", 1, false, 0)]
    public void The_symptom_rule_reports_only_recent_unresolved_severe_records(string severity, int daysAgo, bool resolved, int expected)
    {
        var r = Run([Symptom()], F.Make(symptoms: [F.Symptom(1, severity, daysAgo, resolved)]));
        Assert.Equal(expected, r.Findings.Count);
    }

    [Fact]
    public void The_symptom_finding_carries_no_free_text_and_claims_no_triage()
    {
        var r = Run([Symptom()], F.Make(symptoms: [F.Symptom(1, "Severe", 1)]));
        var f = Assert.Single(r.Findings);
        Assert.Equal("recorded symptom", f.Subjects[0].Label);
        Assert.Contains("engine.not_triage", r.Limitations);
        Assert.Null(f.EmergencySigns);
    }

    // ---------- missing, stale, unauthorized: never "nothing found" ----------

    [Theory]
    [InlineData(InputAvailability.NeverRecorded, "never_recorded")]
    [InlineData(InputAvailability.NotAuthorized, "not_authorized")]
    [InlineData(InputAvailability.Unavailable, "unavailable")]
    public void A_required_input_that_is_not_available_is_missing_data_and_the_result_is_incomplete(InputAvailability availability, string reason)
    {
        var inputs = new[] { F.Input("medications", 0, availability: availability), F.Input("allergies"), F.Input("symptoms"), F.Input("profile") };
        var r = Run(AllActive(), F.Make(inputs: inputs, meds: [F.Med(1, "Alpha", [11])]));
        Assert.Equal(RuleOutcome.MissingData, Eval(r, "T-INT").Outcome);
        Assert.Contains($"input.medications.{reason}", Eval(r, "T-INT").Reasons);
        Assert.Equal(AssessmentStatus.Incomplete, r.Status);
        Assert.False(r.Complete);
        Assert.NotEqual(AssessmentStatus.CompletedNoMatches, r.Status);
    }

    [Fact]
    public void An_input_missing_from_the_snapshot_altogether_is_missing_data()
    {
        var r = Run([Symptom()], F.Make(inputs: [F.Input("medications")]));
        Assert.Equal(RuleOutcome.MissingData, Eval(r, "T-SYM").Outcome);
        Assert.Contains("input.symptoms.not_provided", Eval(r, "T-SYM").Reasons);
    }

    [Fact]
    public void Stale_input_without_a_match_is_stale_data_and_incomplete_never_no_match()
    {
        var r = Run(AllActive(), F.Make(inputs: F.AllInputs(stale: true), meds: [F.Med(1, "Alpha", [11]), F.Med(2, "Beta", [12])]));
        Assert.All(r.Evaluations.Where(e => e.Outcome != RuleOutcome.NotApplicable), e => Assert.Equal(RuleOutcome.StaleData, e.Outcome));
        Assert.Equal(AssessmentStatus.Incomplete, r.Status);
        Assert.False(r.Complete);
    }

    [Fact]
    public void Stale_input_with_a_match_still_reports_the_finding_and_marks_it_stale()
    {
        var r = Run([Duplicate()], F.Make(inputs: F.AllInputs(stale: true), meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11])]));
        var f = Assert.Single(r.Findings);
        Assert.True(f.InputsStale);
        Assert.Contains("input.stale", f.Limitations);
    }

    [Fact]
    public void An_age_limited_rule_with_unknown_age_is_missing_data_and_outside_the_population_is_not_applicable()
    {
        var adultOnly = Active(Def("DEMO-DUP-001", "T-ADULT") with { Population = new RulePopulation(18, 65, []) });
        var snap = F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11])]);
        Assert.Equal(RuleOutcome.MissingData, Eval(Run([adultOnly], snap with { AgeYears = null }), "T-ADULT").Outcome);
        Assert.Equal(RuleOutcome.NotApplicable, Eval(Run([adultOnly], snap with { AgeYears = 9 }), "T-ADULT").Outcome);
        Assert.Equal(RuleOutcome.NotApplicable, Eval(Run([adultOnly], snap with { AgeYears = 80 }), "T-ADULT").Outcome);
        Assert.Equal(RuleOutcome.Matched, Eval(Run([adultOnly], snap with { AgeYears = 18 }), "T-ADULT").Outcome);
    }

    // ---------- determinism, failure, safety layer ----------

    [Fact]
    public void The_same_input_always_gives_the_same_result_whatever_the_rule_order()
    {
        var snap = F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11, 12]), F.Med(3, "C", registered: false)], interactions: [F.Interaction(11, 12, "Moderate")],
            symptoms: [F.Symptom(1, "Severe", 1)], allergies: [F.Allergy(1, "Medication", 2)]);
        var rules = AllActive();
        var a = JsonSerializer.Serialize(Run(rules, snap), Json);
        var b = JsonSerializer.Serialize(Run([.. rules.Reverse()], snap), Json);
        var c = JsonSerializer.Serialize(Run(rules, snap), Json);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void The_rule_set_version_changes_with_the_rules_and_is_stable_otherwise()
    {
        var snap = F.Make();
        var v1 = Run(AllActive(), snap).RuleSetVersion;
        Assert.Equal(v1, Run(AllActive(), snap).RuleSetVersion);
        Assert.NotEqual(v1, Run([.. AllActive().Take(4)], snap).RuleSetVersion);
        Assert.NotEqual(v1, Run(AllActive(), snap, demo: true).RuleSetVersion);
    }

    [Fact]
    public void A_failing_rule_is_reported_as_an_error_and_does_not_stop_the_others()
    {
        var broken = Active(Def("DEMO-DUP-001", "T-BROKEN") with { Criteria = new RuleCriteria((RuleCriteriaKind)99) });
        var r = Run([broken, Unregistered()], F.Make(meds: [F.Med(1, "Unknownol", registered: false)]));
        Assert.Equal(RuleOutcome.Error, Eval(r, "T-BROKEN").Outcome);
        Assert.Contains("engine.rule_error", Eval(r, "T-BROKEN").Reasons);
        Assert.Single(r.Findings); // the other rule still ran
        Assert.Equal(AssessmentStatus.CompletedWithFindings, r.Status);
        Assert.False(r.Complete); // a failed rule means the result is never complete
    }

    [Fact]
    public void The_safety_layer_accepts_a_consistent_engine_result()
    {
        var r = Run(AllActive(), F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11])]));
        Assert.Empty(SafetyLayer.Check(r));
        Assert.Equal(r.Status, SafetyLayer.Apply(r).Status);
    }

    [Fact]
    public void The_safety_layer_fails_closed_on_contradictions_but_keeps_every_finding()
    {
        var good = Run(AllActive(), F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11])]));
        var finding = good.Findings[0];

        var claim = SafetyLayer.Apply(good with { SafetyClaimAllowed = true });
        Assert.Equal(AssessmentStatus.Failed, claim.Status);
        Assert.Contains("safety.invariant_violation.safety_claim", claim.Limitations);
        Assert.Equal(good.Findings.Count, claim.Findings.Count); // a failure never drops a serious finding

        var clean = SafetyLayer.Apply(good with { Status = AssessmentStatus.CompletedNoMatches });
        Assert.Equal(AssessmentStatus.Failed, clean.Status);
        Assert.Contains("safety.invariant_violation.clean_status_with_findings_or_gaps", clean.Limitations);

        var fake = SafetyLayer.Apply(good with { Findings = [finding with { RuleId = "T-NOPE" }] });
        Assert.Contains("safety.invariant_violation.finding_without_evaluation", fake.Limitations);

        var noEvidence = SafetyLayer.Apply(good with { Findings = [finding with { Evidence = [] }] });
        Assert.Contains("safety.invariant_violation.finding_without_evidence", noEvidence.Limitations);

        var complete = SafetyLayer.Apply(good with { Status = AssessmentStatus.Incomplete, Complete = true });
        Assert.Contains("safety.invariant_violation.complete_with_gaps", complete.Limitations);

        var demoActionable = Run(DemoRules.All(T0), F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11])]), demo: true);
        var forged = SafetyLayer.Apply(demoActionable with { Findings = [.. demoActionable.Findings.Select(f => f with { Actionable = true })] });
        Assert.Equal(AssessmentStatus.Failed, forged.Status);
        Assert.Contains("safety.invariant_violation.actionable_without_active_rule", forged.Limitations);
    }

    [Fact]
    public void Random_inputs_never_produce_a_clean_result_while_required_input_is_missing_or_a_finding_exists()
    {
        var rng = new Random(7001);
        var all = AllActive();
        var availabilities = Enum.GetValues<InputAvailability>();
        for (var i = 0; i < 400; i++)
        {
            InputState In(string c) => rng.Next(4) == 0
                ? F.Input(c, 0, availability: availabilities[rng.Next(availabilities.Length)])
                : F.Input(c, rng.Next(0, 4), stale: rng.Next(5) == 0);
            var meds = Enumerable.Range(1, rng.Next(0, 5)).Select(n => F.Med(n, "M" + n, rng.Next(4) == 0 ? null : [10 + rng.Next(1, 4)], registered: rng.Next(5) != 0, active: rng.Next(6) != 0)).ToList();
            var syms = Enumerable.Range(1, rng.Next(0, 3)).Select(n => F.Symptom(n, rng.Next(2) == 0 ? "Severe" : "Mild", rng.Next(0, 20), rng.Next(4) == 0)).ToList();
            var inter = rng.Next(2) == 0 ? new[] { F.Interaction(11, 12, new[] { "Minor", "Moderate", "Major", "Unknown", "Contraindicated" }[rng.Next(5)], rng.Next(4) == 0 ? "Demo" : "Validated") } : [];
            var snap = F.Make(inputs: [In("medications"), In("allergies"), In("symptoms"), In("profile")], meds: meds, symptoms: syms, interactions: inter,
                allergies: rng.Next(2) == 0 ? [F.Allergy(1, "Medication", 1 + rng.Next(3), [10 + rng.Next(1, 4)])] : [], ageYears: rng.Next(5) == 0 ? null : 40);
            var r = Run(all, snap);

            Assert.False(r.SafetyClaimAllowed);
            Assert.Empty(SafetyLayer.Check(r));
            var activeEvals = r.Evaluations.Where(e => e.Activation == ActivationState.Active).ToList();
            if (r.Status == AssessmentStatus.CompletedNoMatches)
            {
                Assert.Empty(r.Findings);
                Assert.All(activeEvals, e => Assert.True(e.Outcome is RuleOutcome.NoMatch or RuleOutcome.NotApplicable));
            }

            if (activeEvals.Any(e => e.Outcome is RuleOutcome.MissingData or RuleOutcome.StaleData or RuleOutcome.InsufficientEvidence or RuleOutcome.Error))
            {
                Assert.False(r.Complete);
            }

            var rerun = Run(all, snap);
            Assert.Equal(JsonSerializer.Serialize(r, Json), JsonSerializer.Serialize(rerun, Json));
        }
    }

    // ---------- text and structure ----------

    [Fact]
    public void No_engine_output_text_contains_a_probability_or_a_medicine_change_instruction()
    {
        var r = Run(DemoRules.All(T0).Concat(AllActive()).ToList(), F.Make(meds: [F.Med(1, "A", [11]), F.Med(2, "B", [11, 12])], symptoms: [F.Symptom(1, "Severe", 1)], interactions: [F.Interaction(11, 12, "Major")]), demo: true);
        var text = string.Join('\n', r.Limitations.Concat(r.Findings.SelectMany(f => f.Limitations)).Concat(r.Evaluations.SelectMany(e => e.Reasons)));
        Assert.Empty(RuleTextPolicy.Violations(text));
    }

    [Fact]
    public void The_snapshot_has_no_field_for_free_text_diagnoses_labs_dose_or_adherence()
    {
        var names = typeof(AssessmentSnapshot).Assembly.GetTypes().Where(t => t.Namespace == typeof(AssessmentSnapshot).Namespace && t.Name.StartsWith("Snapshot", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties().Select(p => p.Name)).ToList();
        foreach (var banned in new[] { "Dose", "Route", "Adherence", "Lab", "Note", "Reaction", "Text", "Condition" })
        {
            Assert.DoesNotContain(names, n => n.Contains(banned, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------- rule validation ----------

    [Fact]
    public void A_wellformed_rule_validates_and_malformed_ones_are_refused_with_codes()
    {
        var keys = new[] { "safety.interaction", "safety.duplicate_ingredient", "safety.allergy_conflict", "safety.unregistered_medication", "safety.symptom_reported" };
        var today = DateOnly.FromDateTime(T0.UtcDateTime);
        var ok = Def("DEMO-INT-001", "T-OK") with { GuidanceTemplateKey = "safety.interaction" };
        Assert.Empty(RuleValidator.Validate(ok, today, keys));

        Assert.Contains("rule.id_invalid", RuleValidator.Validate(ok with { RuleId = "x" }, today, keys));
        Assert.Contains("rule.id_invalid", RuleValidator.Validate(ok with { RuleId = "bad id; DROP TABLE" }, today, keys));
        Assert.Contains("guidance.template_unknown", RuleValidator.Validate(ok with { GuidanceTemplateKey = "nope" }, today, keys));
        Assert.Contains("criteria.kind_domain_mismatch", RuleValidator.Validate(ok with { Domain = RuleDomain.AllergyConflict }, today, keys));
        Assert.Contains("criteria.parameters_invalid", RuleValidator.Validate(ok with { Criteria = new RuleCriteria(RuleCriteriaKind.ReferenceInteraction, "Severe!") }, today, keys));
        Assert.Contains("inputs.required_missing", RuleValidator.Validate(ok with { RequiredInputs = ["allergies"] }, today, keys));
        Assert.Contains("inputs.unknown", RuleValidator.Validate(ok with { RequiredInputs = ["medications", "labs"] }, today, keys));
        Assert.Contains("population.invalid", RuleValidator.Validate(ok with { Population = new RulePopulation(50, 10, []) }, today, keys));
        Assert.Contains("evidence.date_in_future", RuleValidator.Validate(ok with { Evidence = [TestSource with { PublicationDate = today.AddDays(3) }] }, today, keys));
        Assert.Contains("emergency_signs.unsourced", RuleValidator.Validate(ok with { EmergencySigns = ["a sign"], Evidence = [DemoRules.NoSource] }, today, keys));
        Assert.Empty(RuleValidator.Validate(ok with { EmergencySigns = ["a sign"], Evidence = [TestSource] }, today, keys));
    }

    [Theory]
    [InlineData("You should stop taking this medicine")]
    [InlineData("There is a 20% chance of harm")]
    [InlineData("This combination is safe to take")]
    [InlineData("Nothing to worry about")]
    [InlineData("see <script>alert(1)</script>")]
    [InlineData("details at https://example.test/x")]
    [InlineData("قطع کنید")]
    public void Rule_text_that_instructs_reassures_quantifies_or_carries_markup_is_refused(string text)
    {
        var keys = new[] { "safety.duplicate_ingredient" };
        var d = Def("DEMO-DUP-001", "T-TXT") with { GuidanceTemplateKey = "safety.duplicate_ingredient" };
        Assert.NotEmpty(RuleValidator.Validate(d with { Purpose = text }, DateOnly.FromDateTime(T0.UtcDateTime), keys));
        Assert.NotEmpty(RuleValidator.Validate(d with { Limitations = [text] }, DateOnly.FromDateTime(T0.UtcDateTime), keys));
    }

    [Fact]
    public void A_rule_whose_own_test_case_fails_is_reported()
    {
        var d = Def("DEMO-DUP-001", "T-BAD") with { TestCases = [new RuleTestCase("expects the wrong thing", F.Make(meds: [F.Med(1, "A", [11])]), RuleOutcome.Matched, 1)] };
        Assert.Equal(["expects the wrong thing"], RuleValidator.RunTestCases(d));
    }
}
