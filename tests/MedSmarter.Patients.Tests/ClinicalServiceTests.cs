using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using F = MedSmarter.Modules.ClinicalRules.SnapshotFactory;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 7: rule life cycle, patient-specific assessments, guidance integration, consent filtering, failure handling. In memory by default, PostgreSQL when MEDSMARTER_PG_TEST is set.</summary>
public class ClinicalServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly string[] All = ["profile", "medications", "allergies", "symptoms"];
    private static readonly Dictionary<string, string?> DemoOn = new() { ["ClinicalRules:AllowDemonstrationRules"] = "true", ["ClinicalRules:SeedDemoRules"] = "true" };
    private static readonly EvidenceRef TestSource = new("test-fixture", "TEST FIXTURE (not a real source)", "1", null, EvidenceValidation.Validated, "fixture", null);

    private static RuleDefinition TestRule(string demoId, string newId) =>
        DemoRules.All(F.FixtureNow).Single(r => r.Definition.RuleId == demoId).Definition with { RuleId = newId, Evidence = [TestSource] };

    private static async Task<RuleDetailDto> Approve(PEnv env, RuleDefinition def, string author = "demo-content-manager", string reviewer = "demo-physician")
    {
        var created = await env.Rules.CreateDraftAsync(env.UserId(author), def, "test", null);
        Assert.True(created.Succeeded, created.Detail);
        var submitted = await env.Rules.SubmitForReviewAsync(env.UserId(author), def.RuleId, def.Version, "test", null);
        Assert.True(submitted.Succeeded, submitted.Detail);
        var reviewed = await env.Rules.ReviewAsync(env.UserId(reviewer), def.RuleId, def.Version, ReviewDecision.Approve, "fixture approval for a test", null, "test", null);
        Assert.True(reviewed.Succeeded, reviewed.Detail);
        Assert.Equal(ActivationState.Active, reviewed.Value!.Activation.State);
        return reviewed.Value;
    }

    private static async Task<PatientMedicationDto> Unregistered(PEnv env, Guid subject, string name = "Unknownol") =>
        (await env.Meds.AddAsync(subject, subject, new PatientMedicationInput(null, name, null, null, null, FrequencyKind.TimesPerDay, 1, null, env.Today.AddDays(-5), null, PrescriberSource.SelfReported, null), "test", null)).Value!;

    private static async Task<AssessmentDto> Assess(PEnv env, Guid subject, string[]? readable = null, Guid? actor = null, string locale = "en")
    {
        var r = await env.Safety.AssessAsync(actor ?? subject, subject, new AssessCommand(readable ?? All, locale), "test", "corr-test-0001");
        Assert.True(r.Succeeded, r.Detail);
        return r.Value!;
    }

    // ---------- rule life cycle ----------

    [Fact]
    public async Task A_rule_is_born_a_draft_and_becomes_active_only_through_a_different_reviewer()
    {
        using var env = new PEnv(settings: DemoOn);
        var def = TestRule("DEMO-DUP-001", "T-DUP-1");
        var author = env.UserId("demo-content-manager");

        var draft = (await env.Rules.CreateDraftAsync(author, def, "test", null)).Value!;
        Assert.Equal(RuleStatus.Draft, draft.Record.Lifecycle.Status);
        Assert.Equal(ActivationState.Inactive, draft.Activation.State);
        Assert.False(draft.Record.Lifecycle.IsDemo);

        Assert.Equal(RuleError.Conflict, (await env.Rules.ReviewAsync(env.UserId("demo-physician"), def.RuleId, 1, ReviewDecision.Approve, null, null, "test", null)).Error); // not submitted yet
        Assert.True((await env.Rules.SubmitForReviewAsync(author, def.RuleId, 1, "test", null)).Succeeded);

        var self = await env.Rules.ReviewAsync(author, def.RuleId, 1, ReviewDecision.Approve, null, null, "test", null);
        Assert.Equal(RuleError.Forbidden, self.Error);
        Assert.Equal("review.separation_of_duties", self.Detail);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.ClinicalRuleRefused)), e => e.ReasonCode == "review.separation_of_duties" && e.Result == AuditResult.Denied);

        var ok = await env.Rules.ReviewAsync(env.UserId("demo-physician"), def.RuleId, 1, ReviewDecision.Approve, "checked against the fixture", null, "test", null);
        Assert.True(ok.Succeeded, ok.Detail);
        Assert.Equal(RuleStatus.Approved, ok.Value!.Record.Lifecycle.Status);
        Assert.Equal(ActivationState.Active, ok.Value.Activation.State);
        Assert.Equal(env.UserId("demo-physician").ToString(), ok.Value.Record.Lifecycle.Reviewer);
        Assert.NotNull(ok.Value.Record.Lifecycle.ReviewedAt);

        // The content never changes after creation: a second creation of the same version is refused.
        Assert.Equal(RuleError.Conflict, (await env.Rules.CreateDraftAsync(author, def, "test", null)).Error);
    }

    [Fact]
    public async Task Approval_is_refused_when_evidence_is_demo_unvalidated_or_a_test_case_fails()
    {
        using var env = new PEnv(settings: DemoOn);
        var author = env.UserId("demo-content-manager");
        var reviewer = env.UserId("demo-physician");

        async Task<RuleOutcomeResult<RuleDetailDto>> TryApprove(RuleDefinition d)
        {
            Assert.True((await env.Rules.CreateDraftAsync(author, d, "test", null)).Succeeded);
            var sub = await env.Rules.SubmitForReviewAsync(author, d.RuleId, d.Version, "test", null);
            return sub.Succeeded ? await env.Rules.ReviewAsync(reviewer, d.RuleId, d.Version, ReviewDecision.Approve, null, null, "test", null) : sub;
        }

        var demoEvidence = await TryApprove(TestRule("DEMO-DUP-001", "T-NOEV-1") with { Evidence = [DemoRules.NoSource] });
        Assert.Contains("policy.demo_evidence", demoEvidence.Detail!);

        var unvalidated = await TryApprove(TestRule("DEMO-DUP-001", "T-NOEV-2") with { Evidence = [TestSource with { Validation = EvidenceValidation.Unverified }] });
        Assert.Contains("policy.no_validated_evidence", unvalidated.Detail!);

        var noEvidence = await TryApprove(TestRule("DEMO-DUP-001", "T-NOEV-3") with { Evidence = [] });
        Assert.Contains("policy.evidence_missing", noEvidence.Detail!);

        var failing = TestRule("DEMO-DUP-001", "T-FAIL-1") with { TestCases = [new RuleTestCase("wrong expectation", F.Make(meds: [F.Med(1, "A", [11])]), RuleOutcome.Matched, 1)] };
        Assert.True((await env.Rules.CreateDraftAsync(author, failing, "test", null)).Succeeded);
        var submit = await env.Rules.SubmitForReviewAsync(author, failing.RuleId, 1, "test", null);
        Assert.Equal(RuleError.Validation, submit.Error);
        Assert.Equal("tests.failed", submit.Detail);

        var noTests = await TryApprove(TestRule("DEMO-DUP-001", "T-NOTEST-1") with { TestCases = [] });
        Assert.Equal("policy.test_cases_missing", noTests.Detail);

        Assert.Equal(0, (await env.Rules.CoverageAsync()).ActiveRules);
    }

    [Fact]
    public async Task Reject_needs_a_note_and_a_rejected_rule_never_runs()
    {
        using var env = new PEnv(settings: DemoOn);
        var def = TestRule("DEMO-DQ-001", "T-REJ-1");
        var author = env.UserId("demo-content-manager");
        await env.Rules.CreateDraftAsync(author, def, "test", null);
        await env.Rules.SubmitForReviewAsync(author, def.RuleId, 1, "test", null);

        Assert.Equal("review.note_required", (await env.Rules.ReviewAsync(env.UserId("demo-physician"), def.RuleId, 1, ReviewDecision.Reject, null, null, "test", null)).Detail);
        var rejected = await env.Rules.ReviewAsync(env.UserId("demo-physician"), def.RuleId, 1, ReviewDecision.Reject, "evidence does not support the rule", null, "test", null);
        Assert.Equal(RuleStatus.Rejected, rejected.Value!.Record.Lifecycle.Status);
        Assert.Equal(ActivationState.Inactive, rejected.Value.Activation.State);
        Assert.Equal(RuleError.Conflict, (await env.Rules.ReviewAsync(env.UserId("demo-pharmacist"), def.RuleId, 1, ReviewDecision.Approve, null, null, "test", null)).Error); // final
    }

    [Fact]
    public async Task Retiring_a_rule_takes_it_out_of_force_and_review_notes_are_screened()
    {
        using var env = new PEnv(settings: DemoOn);
        var def = TestRule("DEMO-DQ-001", "T-RET-1");
        await Approve(env, def);
        Assert.Equal(1, (await env.Rules.CoverageAsync()).ActiveRules);

        var retired = await env.Rules.RetireAsync(env.UserId("demo-system-admin"), def.RuleId, 1, "test", null);
        Assert.Equal(RuleStatus.Retired, retired.Value!.Record.Lifecycle.Status);
        Assert.NotNull(retired.Value.Record.Lifecycle.RetiredAt);
        Assert.Equal(ActivationState.Inactive, retired.Value.Activation.State);
        Assert.Equal(0, (await env.Rules.CoverageAsync()).ActiveRules);

        var def2 = TestRule("DEMO-DQ-001", "T-RET-2");
        await env.Rules.CreateDraftAsync(env.UserId("demo-content-manager"), def2, "test", null);
        await env.Rules.SubmitForReviewAsync(env.UserId("demo-content-manager"), def2.RuleId, 1, "test", null);
        var bad = await env.Rules.ReviewAsync(env.UserId("demo-physician"), def2.RuleId, 1, ReviewDecision.Approve, "this is safe to take", null, "test", null);
        Assert.Equal("review.note_invalid", bad.Detail);
    }

    [Fact]
    public async Task Rules_cannot_use_the_reserved_demo_prefix_and_demonstration_rules_cannot_enter_review()
    {
        using var env = new PEnv(settings: DemoOn);
        var author = env.UserId("demo-content-manager");
        var reserved = await env.Rules.CreateDraftAsync(author, TestRule("DEMO-DQ-001", "DEMO-FAKE-1"), "test", null);
        Assert.Equal(RuleError.Validation, reserved.Error);
        Assert.Contains("rule.id_reserved", reserved.Detail!);

        await env.Get<IDemoRuleSeeder>().SeedAsync();
        await env.Get<IDemoRuleSeeder>().SeedAsync(); // idempotent
        var listed = await env.Rules.ListAsync(null, 100);
        Assert.Equal(5, listed.Count);
        Assert.All(listed, r => { Assert.True(r.IsDemo); Assert.Equal(RuleStatus.Draft, r.Status); Assert.Equal(ActivationState.DemonstrationOnly, r.Activation); });

        Assert.Equal(RuleError.Conflict, (await env.Rules.SubmitForReviewAsync(author, "DEMO-DQ-001", 1, "test", null)).Error);
        var cov = await env.Rules.CoverageAsync();
        Assert.Equal(0, cov.ActiveRules);
        Assert.Equal(5, cov.DemonstrationRules);
        Assert.Equal(5, cov.DomainsWithoutActiveCoverage.Count);
        Assert.Contains("dose_and_route", cov.UnsupportedDomains);
    }

    [Fact]
    public async Task Without_the_demonstration_switch_the_seeded_rules_are_inactive()
    {
        using var env = new PEnv(settings: new Dictionary<string, string?> { ["ClinicalRules:SeedDemoRules"] = "true" });
        await env.Get<IDemoRuleSeeder>().SeedAsync();
        Assert.All(await env.Rules.ListAsync(null, 100), r => Assert.Equal(ActivationState.Inactive, r.Activation));
        Assert.False((await env.Rules.CoverageAsync()).DemonstrationAllowed);
    }

    // ---------- assessments ----------

    [Fact]
    public async Task With_only_demonstration_rules_the_assessment_has_no_approved_coverage_and_creates_no_guidance()
    {
        using var env = new PEnv(settings: DemoOn);
        await env.Get<IDemoRuleSeeder>().SeedAsync();
        var subject = await env.Patient("demo-patient-2");
        await env.TakeMedication(subject, "demopril");
        await env.TakeMedication(subject, "nocturin");
        await Unregistered(env, subject);

        var a = await Assess(env, subject);
        Assert.Equal(AssessmentStatus.NoApprovedCoverage, a.Result.Status);
        Assert.True(a.Result.ContainsDemonstration);
        Assert.False(a.Result.SafetyClaimAllowed);
        Assert.Equal(GuidanceLinkState.NotApplicable, a.GuidanceState);
        Assert.Contains(a.Result.Findings, f => f.Domain == RuleDomain.Interaction && f.IsDemo && !f.Actionable);
        Assert.Contains(a.Result.Findings, f => f.Domain == RuleDomain.DuplicateIngredient || f.Domain == RuleDomain.DataQuality);
        Assert.All(a.Result.Findings, f => Assert.False(f.Actionable));
        Assert.StartsWith("DEMO", a.Notice, StringComparison.Ordinal);
        Assert.Empty((await env.Guidance.ListForPatientAsync(subject)).Value!);

        var stored = (await env.Safety.GetAsync(subject, a.Id, All)).Value!;
        Assert.Equal(a.Result.RuleSetVersion, stored.Result.RuleSetVersion);
        Assert.Equal(a.Result.Findings.Count, stored.Result.Findings.Count);
        Assert.Equal(ClinicalSafetyEngine.Version, stored.Result.EngineVersion);
    }

    [Fact]
    public async Task Demopril_and_Nocturin_match_the_reference_interaction_with_its_demo_provenance()
    {
        using var env = new PEnv(settings: DemoOn);
        await env.Get<IDemoRuleSeeder>().SeedAsync();
        var subject = await env.Patient("demo-patient-2");
        await env.TakeMedication(subject, "demopril");
        await env.TakeMedication(subject, "nocturin");

        var f = Assert.Single((await Assess(env, subject)).Result.Findings, x => x.Domain == RuleDomain.Interaction);
        Assert.Equal("Moderate", f.ReferenceSeverity);
        Assert.True(f.IsDemo);
        Assert.Contains(f.Evidence, e => e.Validation == EvidenceValidation.Demo && e.PublicationDate is null); // a publication date is never invented
        Assert.Contains(f.Evidence, e => e.SourceId == "demo-fixture");
    }

    [Fact]
    public async Task An_approved_rule_raises_one_guidance_message_with_its_origin_and_repeating_does_not_duplicate_it()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-1"));
        var subject = await env.Patient("demo-patient-2");
        var med = await Unregistered(env, subject);

        var first = await Assess(env, subject);
        Assert.Equal(AssessmentStatus.CompletedWithFindings, first.Result.Status);
        var finding = Assert.Single(first.Result.Findings);
        Assert.True(finding.Actionable);
        Assert.Equal(GuidanceLinkState.Created, first.GuidanceState);
        var link = Assert.Single(first.GuidanceLinks);
        Assert.Equal("created", link.State);

        var messages = (await env.Guidance.ListForPatientAsync(subject)).Value!;
        var message = Assert.Single(messages);
        Assert.Equal(link.MessageId, message.Id);
        Assert.Equal("safety.unregistered_medication", message.TemplateKey);
        Assert.Equal(GuidanceStatus.Sent, message.Status); // creating the record is not "seen"
        Assert.Null(message.SeenAt);
        Assert.Null(message.Professional);
        Assert.Equal(first.Id, message.Origin!.AssessmentId);
        Assert.Equal("T-DQ-1", message.Origin.RuleId);
        Assert.Equal(1, message.Origin.RuleVersion);
        Assert.Equal(finding.Key, message.Origin.FindingKey);
        Assert.Contains(med.DisplayName, message.Patient.Observed, StringComparison.Ordinal);

        var second = await Assess(env, subject);
        Assert.Equal("existing", Assert.Single(second.GuidanceLinks).State);
        Assert.Single((await env.Guidance.ListForPatientAsync(subject)).Value!);
    }

    [Fact]
    public async Task A_resolved_finding_is_raised_again_only_when_the_data_changed_after_it_was_resolved()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-2"));
        var subject = await env.Patient("demo-patient-2");
        await Unregistered(env, subject);
        var first = await Assess(env, subject);
        var id = first.GuidanceLinks[0].MessageId!.Value;

        env.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await env.Guidance.SetStatusAsync(subject, subject, id, GuidanceStatus.Resolved, false, "test", null)).Succeeded);

        env.Clock.Advance(TimeSpan.FromHours(1));
        await Assess(env, subject);
        Assert.Single((await env.Guidance.ListForPatientAsync(subject)).Value!); // nothing changed: the patient's decision stands

        env.Clock.Advance(TimeSpan.FromHours(1));
        await Unregistered(env, subject, "Otherol"); // the record changed
        await Assess(env, subject);
        var all = (await env.Guidance.ListForPatientAsync(subject)).Value!;
        // The record changed after the patient resolved it, so the situation may be different: the first finding is raised again (a new message) next to the new one.
        // The resolved message itself is never reopened or deleted.
        Assert.Equal(3, all.Count);
        Assert.Contains(all, m => m.Id == id && m.Status == GuidanceStatus.Resolved);
        Assert.Equal(2, all.Count(m => m.Status == GuidanceStatus.Sent));
    }

    [Fact]
    public async Task A_finding_that_no_longer_matches_is_reported_but_its_open_message_is_never_closed_automatically()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-3"));
        var subject = await env.Patient("demo-patient-2");
        var med = await Unregistered(env, subject);
        var first = await Assess(env, subject);
        var key = first.Result.Findings[0].Key;

        env.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await env.Meds.StopAsync(subject, subject, med.Id, new StopMedicationCommand("test", env.Today, med.Version), "test", null)).Succeeded);
        env.Clock.Advance(TimeSpan.FromHours(1));
        var later = await Assess(env, subject);

        Assert.Empty(later.Result.Findings);
        Assert.Contains(key, later.OpenGuidanceNoLongerMatching);
        var message = Assert.Single((await env.Guidance.ListForPatientAsync(subject)).Value!);
        Assert.Equal(GuidanceStatus.Sent, message.Status); // still open: a person decides
    }

    [Fact]
    public async Task Missing_data_is_incomplete_not_clean_and_creates_no_guidance()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DUP-001", "T-DUP-2"));
        await Approve(env, TestRule("DEMO-ALG-001", "T-ALG-2"));
        var subject = await env.Patient("demo-patient-2"); // nothing recorded at all

        var a = await Assess(env, subject);
        Assert.Equal(AssessmentStatus.Incomplete, a.Result.Status);
        Assert.False(a.Result.Complete);
        Assert.All(a.Result.Evaluations.Where(e => e.Activation == ActivationState.Active), e => Assert.Equal(RuleOutcome.MissingData, e.Outcome));
        Assert.Contains(a.Result.Inputs, i => i.Category == "medications" && i.Availability == InputAvailability.NeverRecorded);
        Assert.Empty((await env.Guidance.ListForPatientAsync(subject)).Value!);
    }

    [Fact]
    public async Task A_category_the_requester_may_not_read_is_withheld_and_treated_as_missing_never_as_empty()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-ALG-001", "T-ALG-3"));
        var subject = await env.Patient("demo-patient-2");
        var med = await env.TakeMedication(subject, "demopril");
        Assert.True((await env.Patients.AddAllergyAsync(subject, subject, new AllergyInput(AllergenKind.Medication, med.MedicationId, null, AllergySeverity.Moderate, null), "test", null)).Succeeded);

        var all = await Assess(env, subject);
        Assert.Single(all.Result.Findings); // the allergy to the same reference medicine

        var withheld = await Assess(env, subject, ["medications"], actor: env.UserId("demo-physician"));
        Assert.Contains(withheld.Result.Inputs, i => i.Category == "allergies" && i.Availability == InputAvailability.NotAuthorized);
        Assert.Empty(withheld.Result.Findings);
        Assert.Equal(AssessmentStatus.Incomplete, withheld.Result.Status);
        Assert.Contains("input.allergies.not_authorized", withheld.Result.Evaluations.Single(e => e.Activation == ActivationState.Active).Reasons);
        Assert.False(withheld.RequestedByPatient);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.SafetyInputBlocked)), e => e.ResourceId == "allergies" && e.Result == AuditResult.Denied);

        var none = await env.Safety.AssessAsync(env.UserId("demo-physician"), subject, new AssessCommand(["allergies"]), "test", null);
        Assert.Equal(SafetyError.Forbidden, none.Error); // without medications there is nothing to assess
    }

    [Fact]
    public async Task A_stored_assessment_is_shown_only_as_far_as_the_viewer_may_read_today()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-ALG-001", "T-ALG-4"));
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-4"));
        var subject = await env.Patient("demo-patient-2");
        var med = await env.TakeMedication(subject, "demopril");
        await env.Patients.AddAllergyAsync(subject, subject, new AllergyInput(AllergenKind.Medication, med.MedicationId, null, AllergySeverity.Moderate, null), "test", null);
        await Unregistered(env, subject);
        var a = await Assess(env, subject);
        Assert.Equal(2, a.Result.Findings.Count);

        var narrow = (await env.Safety.GetAsync(subject, a.Id, ["medications"])).Value!;
        Assert.Single(narrow.Result.Findings);
        Assert.Equal(RuleDomain.DataQuality, narrow.Result.Findings[0].Domain);
        Assert.Contains("view.filtered_by_consent", narrow.Result.Limitations);
        Assert.Contains(narrow.Result.Inputs, i => i.Category == "allergies" && i.Availability == InputAvailability.NotAuthorized);
        Assert.DoesNotContain(narrow.GuidanceLinks, l => l.FindingKey == a.Result.Findings.Single(f => f.Domain == RuleDomain.AllergyConflict).Key);

        var summary = Assert.Single((await env.Safety.ListAsync(subject, 10, ["medications"])).Value!);
        Assert.Equal(1, summary.FindingCount);
    }

    [Fact]
    public async Task Another_patients_assessment_is_not_found()
    {
        using var env = new PEnv(settings: DemoOn);
        var one = await env.Patient("demo-patient");
        var two = await env.Patient("demo-patient-2");
        await env.TakeMedication(one);
        var a = await Assess(env, one);
        Assert.Equal(SafetyError.NotFound, (await env.Safety.GetAsync(two, a.Id, All)).Error);
        Assert.Empty((await env.Safety.ListAsync(two, 10, All)).Value!);
        Assert.Equal(SafetyError.NotFound, (await env.Safety.LatestAsync(two, All)).Error);
    }

    [Fact]
    public async Task An_unknown_or_inactive_patient_cannot_be_assessed()
    {
        using var env = new PEnv(settings: DemoOn);
        Assert.Equal(SafetyError.NotFound, (await env.Safety.AssessAsync(Guid.NewGuid(), Guid.NewGuid(), new AssessCommand(All), "test", null)).Error);
        var subject = await env.Patient("demo-patient-2");
        await env.Patients.SetStatusAsync(env.UserId("demo-system-admin"), subject, PatientStatus.Inactive, "test", null);
        Assert.Equal(SafetyError.NotFound, (await env.Safety.AssessAsync(subject, subject, new AssessCommand(All), "test", null)).Error);
    }

    [Fact]
    public async Task The_assessment_is_flagged_outdated_when_the_data_or_the_rules_changed_since_and_nothing_reruns_by_itself()
    {
        using var env = new PEnv(settings: DemoOn);
        var subject = await env.Patient("demo-patient-2");
        await env.TakeMedication(subject);
        var a = await Assess(env, subject);
        Assert.False((await env.Safety.LatestAsync(subject, All)).Value!.Outdated);

        env.Clock.Advance(TimeSpan.FromMinutes(5));
        await env.Patients.AddAllergyAsync(subject, subject, new AllergyInput(AllergenKind.Other, null, "fictional substance", AllergySeverity.Mild, null), "test", null);
        var changed = (await env.Safety.LatestAsync(subject, All)).Value!;
        Assert.True(changed.Outdated);
        Assert.Contains("data.changed.allergies", changed.OutdatedReasons);
        Assert.Equal(a.Id, changed.Id); // still the old assessment: nothing re-ran

        // a viewer who cannot read allergies learns nothing about them from the flag
        Assert.DoesNotContain("data.changed.allergies", (await env.Safety.LatestAsync(subject, ["medications"])).Value!.OutdatedReasons);

        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-5"));
        Assert.Contains("rules.changed", (await env.Safety.LatestAsync(subject, All)).Value!.OutdatedReasons);
        Assert.Single((await env.Safety.ListAsync(subject, 10, All)).Value!);
    }

    // ---------- failure handling ----------

    private sealed class ThrowingGuidance : IGuidanceService
    {
        public Task<GuidanceOutcome<GuidanceMessageDto>> CreateAsync(Guid subjectId, GuidanceRequest request, bool isDemo, string source, string? correlationId, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<GuidanceCreated>> CreateFromOriginAsync(Guid subjectId, GuidanceRequest request, GuidanceOrigin origin, bool isDemo, string source, string? correlationId, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<IReadOnlyList<GuidanceOriginRef>>> ListOpenByOriginAsync(Guid subjectId, string originKind, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<IReadOnlyList<GuidanceMessageDto>>> ListForPatientAsync(Guid subjectId, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<GuidanceOutcome<GuidanceMessageDto>> SetStatusAsync(Guid actorUserId, Guid subjectId, Guid id, GuidanceStatus target, bool actorIsProfessional, string source, string? correlationId, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public IReadOnlyList<GuidanceComposition> Samples(string locale) => [];
    }

    private sealed class ThrowOnSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("disk full");
    }

    private sealed class FailingStoreFactory : IDbContextFactory<ClinicalRulesDbContext>
    {
        public ClinicalRulesDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ClinicalRulesDbContext>().UseInMemoryDatabase("failing-" + Guid.NewGuid().ToString("N")).AddInterceptors(new ThrowOnSave()).Options);
    }

    private static ClinicalSafetyService Service(PEnv env, IGuidanceService guidance, IDbContextFactory<ClinicalRulesDbContext>? factory = null) => new(
        factory ?? env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), env.Get<RuleStore>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), env.Patients, env.Meds, env.Reference, guidance,
        env.Get<IOptions<ClinicalRulesOptions>>());

    [Fact]
    public async Task If_guidance_cannot_be_created_the_findings_stay_visible_and_the_assessment_says_so_then_a_rerun_repairs_it_without_duplicates()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-6"));
        var subject = await env.Patient("demo-patient-2");
        await Unregistered(env, subject);

        var broken = await Service(env, new ThrowingGuidance()).AssessAsync(subject, subject, new AssessCommand(All), "test", null);
        Assert.True(broken.Succeeded);
        Assert.Equal(AssessmentStatus.CompletedWithFindings, broken.Value!.Result.Status);
        Assert.Single(broken.Value.Result.Findings); // the finding is not lost because the message could not be written
        Assert.Equal(GuidanceLinkState.Failed, broken.Value.GuidanceState);
        Assert.StartsWith("failed", Assert.Single(broken.Value.GuidanceLinks).State, StringComparison.Ordinal);
        Assert.Empty((await env.Guidance.ListForPatientAsync(subject)).Value!); // no message claims to exist
        Assert.Equal(GuidanceLinkState.Failed, (await env.Safety.LatestAsync(subject, All)).Value!.GuidanceState);

        var repaired = await Assess(env, subject);
        Assert.Equal(GuidanceLinkState.Created, repaired.GuidanceState);
        Assert.Single((await env.Guidance.ListForPatientAsync(subject)).Value!);
    }

    [Fact]
    public async Task If_the_assessment_cannot_be_stored_nothing_is_shown_and_no_guidance_is_created()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-7"));
        var subject = await env.Patient("demo-patient-2");
        await Unregistered(env, subject);

        var r = await Service(env, env.Guidance, new FailingStoreFactory()).AssessAsync(subject, subject, new AssessCommand(All), "test", null);
        Assert.False(r.Succeeded);
        Assert.Equal(SafetyError.Unavailable, r.Error);
        Assert.Equal("persistence.failed", r.Detail);
        Assert.Empty((await env.Guidance.ListForPatientAsync(subject)).Value!); // no contradictory alert state
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.SafetyAssessmentFailed)), e => e.ReasonCode == "persistence.failed");
    }

    [Fact]
    public async Task A_technical_failure_while_gathering_data_is_unavailable_not_a_partial_result()
    {
        using var env = new PEnv(settings: DemoOn);
        var subject = await env.Patient("demo-patient-2");
        await env.TakeMedication(subject);
        var service = new ClinicalSafetyService(env.Get<IDbContextFactory<ClinicalRulesDbContext>>(), env.Get<RuleStore>(), env.Clock, env.Get<IAuditWriter>(), env.Get<IPatientDirectory>(), env.Patients, env.Meds,
            new ExplodingReference(), env.Guidance, env.Get<IOptions<ClinicalRulesOptions>>());
        var r = await service.AssessAsync(subject, subject, new AssessCommand(All), "test", null);
        Assert.Equal(SafetyError.Unavailable, r.Error);
        Assert.Empty((await env.Safety.ListAsync(subject, 10, All)).Value!);
    }

    private sealed class ExplodingReference : IMedicationService
    {
        public Task<PagedResult<MedicationSummaryDto>> SearchAsync(MedicationSearchQuery query, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<OperationResult<MedicationDetailDto>> GetAsync(Guid id, bool includeNonActive = false, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<OperationResult<MedicationKnowledgeDocument>> GetKnowledgeDocumentAsync(Guid id, bool includeNonActive = false, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<OperationResult<ManufacturerDto>> GetManufacturerAsync(Guid id, CancellationToken ct = default) => throw new InvalidOperationException("boom");
    }

    [PostgresFact]
    public async Task Rules_assessments_and_guidance_origin_survive_an_application_restart_on_PostgreSQL()
    {
        using var first = new PEnv(settings: DemoOn);
        await Approve(first, TestRule("DEMO-DQ-001", "T-PG-1"));
        var subject = await first.Patient("demo-patient-2");
        await Unregistered(first, subject);
        var a = await Assess(first, subject);

        using var second = first.Restart();
        var rule = (await second.Rules.GetAsync("T-PG-1", 1)).Value!;
        Assert.Equal(RuleStatus.Approved, rule.Record.Lifecycle.Status);
        Assert.Equal(ActivationState.Active, rule.Activation.State);
        Assert.Equal(TestSource, rule.Record.Definition.Evidence[0]);

        var stored = (await second.Safety.GetAsync(subject, a.Id, All)).Value!;
        Assert.Equal(a.Result.Findings[0].Key, stored.Result.Findings[0].Key);
        Assert.Equal(AssessmentStatus.CompletedWithFindings, stored.Result.Status);
        Assert.False(stored.Outdated); // same rule set, same data

        var message = Assert.Single((await second.Guidance.ListForPatientAsync(subject)).Value!);
        Assert.Equal(a.Result.Findings[0].Key, message.Origin!.FindingKey);
        Assert.Equal("existing", Assert.Single((await Assess(second, subject)).GuidanceLinks).State);
    }

    // ---------- audit and privacy ----------

    [Fact]
    public async Task Audit_lines_carry_ids_codes_and_counts_but_never_medicine_names_or_free_text()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-8"));
        var subject = await env.Patient("demo-patient-2");
        await Unregistered(env, subject, "Zzzsecretol");
        var a = await Assess(env, subject);

        var entries = await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: subject, Action: AuditActions.SafetyAssessmentRun));
        var entry = Assert.Single(entries);
        Assert.Equal(a.Id.ToString(), entry.ResourceId);
        Assert.Equal("CompletedWithFindings", entry.ReasonCode);
        Assert.Equal("1", entry.Metadata["findings"]);
        Assert.Equal("1", entry.Metadata["actionable"]);
        Assert.Equal(a.Result.RuleSetVersion, entry.Metadata["rule_set"]);

        var everything = JsonSerializer.Serialize(await env.Audit.QueryAsync(new AuditQuery(Take: 1000)));
        Assert.DoesNotContain("Zzzsecretol", everything, StringComparison.Ordinal);
        Assert.True(await env.Audit.VerifyChainAsync());
    }

    [Fact]
    public async Task Rule_changes_are_audited_with_the_actor_and_the_rule_version()
    {
        using var env = new PEnv(settings: DemoOn);
        await Approve(env, TestRule("DEMO-DQ-001", "T-DQ-9"));
        foreach (var action in new[] { AuditActions.ClinicalRuleCreated, AuditActions.ClinicalRuleSubmitted, AuditActions.ClinicalRuleReviewed })
        {
            var e = Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: action)));
            Assert.Equal("T-DQ-9@1", e.ResourceId);
            Assert.NotNull(e.ActorUserId);
        }
    }

    // ---------- guidance templates ----------

    [Theory]
    [InlineData("safety.interaction")]
    [InlineData("safety.allergy_conflict")]
    [InlineData("safety.duplicate_ingredient")]
    [InlineData("safety.unregistered_medication")]
    [InlineData("safety.symptom_reported")]
    public void Every_safety_template_passes_the_patient_message_policy_in_both_languages_at_every_level_the_engine_uses(string key)
    {
        var composer = new MedSmarter.Modules.Guidance.GuidanceComposer();
        Assert.Contains(key, composer.TemplateKeys);
        foreach (var locale in new[] { "en", "fa" })
        {
            foreach (var level in new[] { GuidanceLevel.Information, GuidanceLevel.FollowUp, GuidanceLevel.ReviewSoon })
            {
                var p = new Dictionary<string, string> { ["medicationA"] = "Demopril", ["medicationB"] = "Nocturin", ["medication"] = "Demopril", ["allergy"] = "fictional", ["severity"] = "Moderate" };
                var c = composer.Compose(new GuidanceRequest(key, level, locale, p, GuidanceConfidence.Moderate, locale == "fa" ? "قانون ایمنی t-rule (نسخه‌ی 1)" : "safety rule t-rule (version 1) applied to your recorded information", true));
                Assert.True(c.IsValid, string.Join(',', c.Violations.Select(v => v.Code + "@" + v.Part)));
                Assert.Null(c.Patient.UrgentSigns); // no unsourced emergency text
                Assert.DoesNotContain("%", c.Patient.Observed + c.Patient.WhyItMatters + c.Patient.SuggestedAction + c.Patient.WhenToConsult, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Guidance_raised_from_a_finding_never_uses_the_urgent_level_without_sourced_emergency_signs()
    {
        var f = new Finding("f-1", "T-1", 1, RuleDomain.Interaction, FindingSeverity.Critical, FindingUrgency.Prompt, true, false, "safety.interaction", [new FindingSubject("medication", F.G(1), "A"), new FindingSubject("medication", F.G(2), "B")],
            [TestSource], "Contraindicated", false, false, [], null);
        var request = ClinicalSafetyService.GuidanceFor(f, "en");
        Assert.Equal(GuidanceLevel.ReviewSoon, request.Level);
        Assert.Equal("safety.interaction", request.TemplateKey);
        Assert.Equal("A", request.Parameters["medicationA"]);
        Assert.Equal("B", request.Parameters["medicationB"]);
        var stale = ClinicalSafetyService.GuidanceFor(f with { InputsStale = true }, "fa");
        Assert.Equal(GuidanceConfidence.Low, stale.Confidence);
    }

    [Fact]
    public void The_results_serialise_with_string_enums_and_no_patient_identifiers_inside_the_engine_result()
    {
        var r = ClinicalSafetyEngine.Evaluate(DemoRules.All(F.FixtureNow), F.Make(meds: [F.Med(1, "A", [11])]), true);
        var json = JsonSerializer.Serialize(r, JsonOptions);
        Assert.Contains("\"status\":\"NoApprovedCoverage\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("subjectId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patientId", json, StringComparison.OrdinalIgnoreCase);
    }
}
