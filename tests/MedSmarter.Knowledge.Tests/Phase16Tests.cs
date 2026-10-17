using System.Reflection;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Knowledge.Tests;

/// <summary>Phase 16: evidence traceability, honest handling of conflicting or weak evidence, backend-written uncertainty, and the separation of generated text from deterministic rules.</summary>
public class Phase16Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    private static KnowledgeSourceRef Src(DateTimeOffset? received = null, ValidationStatus v = ValidationStatus.Validated) =>
        new(Guid.NewGuid(), "Test formulary (fictional)", "v7", "Test publisher", received ?? Now.AddDays(-10), v);

    private static MedicationKnowledgeDocument Doc(KnowledgeSourceRef[] sources, KnowledgeStatementView[] statements, KnowledgeInteractionView[]? interactions = null) =>
        new(Guid.NewGuid(), 3, ["Testomed"], [], ["ingredient"], "tablet", "5 mg", statements, interactions ?? [], sources, ValidationStatus.Validated, false, Now, "ok");

    private static KnowledgeStatementView[] AllKinds(Guid source) =>
    [
        new(StatementKind.Indication, "Used for a test.", source, ValidationStatus.Validated),
        new(StatementKind.Contraindication, "Not for test subjects.", source, ValidationStatus.Validated),
        new(StatementKind.Warning, "Take care.", source, ValidationStatus.Validated),
        new(StatementKind.Administration, "Take with water.", source, ValidationStatus.Validated),
        new(StatementKind.AdverseReaction, "Test reaction.", source, ValidationStatus.Validated),
    ];

    private static EvidenceSet Build(MedicationKnowledgeDocument doc, string question = "tell me about Testomed") =>
        MedicationEvidenceRetriever.Build([doc], QuestionTopics.Detect(QueryNormalizer.Normalize(question)), Now, TimeSpan.FromDays(1095));

    // ---------- traceability ----------

    [Fact]
    public async Task Evidence_from_the_real_reference_carries_the_ids_of_the_stored_record_and_source_revision()
    {
        using var env = new KEnv();
        var set = await new MedicationEvidenceRetriever(env.Meds, env.Clock, Options.Create(new AiOptions())).RetrieveAsync(new EvidenceQuery("Nocturin", "en"));
        Assert.NotEmpty(set.Items);
        Assert.All(set.Items, i =>
        {
            Assert.NotNull(i.RecordId);
            Assert.NotEqual(Guid.Empty, i.RecordId!.Value);
            Assert.NotNull(i.RevisionId);
            Assert.NotEqual(Guid.Empty, i.RevisionId!.Value);
        });
        Assert.Equal(set.Items.Count, set.Items.Select(i => i.RecordId).Distinct().Count()); // each item points at a different stored record
        foreach (var item in set.Items)
        {
            var stored = (await env.Meds.GetAsync(item.MedicationId, true)).Value!; // the authoritative stored record, read independently
            var match = item.Kind == "Interaction"
                ? stored.Interactions.Any(x => x.Id == item.RecordId && x.RevisionId == item.RevisionId && x.SourceId == item.Source.SourceId)
                : stored.Statements.Any(x => x.Id == item.RecordId && x.RevisionId == item.RevisionId && x.SourceId == item.Source.SourceId);
            Assert.True(match, $"{item.Id} does not match a stored record");
        }

        Assert.All(set.Medications, m => Assert.True(m.RecordVersion > 0)); // and the record version of its medication is part of the set
    }

    [Fact]
    public void The_ids_are_carried_unchanged_from_the_document_into_the_item()
    {
        var s = Src();
        var statementId = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var set = Build(Doc([s], [new KnowledgeStatementView(StatementKind.Warning, "Take care.", s.SourceId, ValidationStatus.Validated, statementId, revision)]));
        var item = Assert.Single(set.Items);
        Assert.Equal(statementId, item.RecordId);
        Assert.Equal(revision, item.RevisionId);
        Assert.Equal(s.Version, item.Source.Version);
        Assert.Equal(s.ReceivedAt, item.Source.ReceivedAt);
    }

    // ---------- conflicting evidence ----------

    [Fact]
    public void A_source_that_contradicts_itself_about_an_interaction_is_a_conflict_too()
    {
        var s = Src();
        var set = Build(Doc([s], AllKinds(s.SourceId), [
            new KnowledgeInteractionView("otheringredient", InteractionSeverity.Minor, "Says minor.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("otheringredient", InteractionSeverity.Major, "Says major.", s.SourceId, ValidationStatus.Validated)]), "interactions of Testomed");
        var conflict = Assert.Single(set.Conflicts);
        Assert.Equal(2, conflict.ItemIds.Count);
        Assert.Contains("evidence.conflict", set.Limitations);
        Assert.Equal(EvidenceQuality.Limited, set.Quality);
        Assert.Equal(2, set.Items.Count(i => i.Kind == "Interaction")); // both stay visible; nothing is chosen
    }

    [Fact]
    public void Two_different_interactions_or_the_same_severity_twice_are_not_conflicts()
    {
        var s = Src();
        var set = Build(Doc([s], AllKinds(s.SourceId), [
            new KnowledgeInteractionView("one", InteractionSeverity.Minor, "A.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("two", InteractionSeverity.Major, "B.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("two", InteractionSeverity.Major, "B again.", s.SourceId, ValidationStatus.Validated)]), "interactions of Testomed");
        Assert.Empty(set.Conflicts);
    }

    // ---------- uncertainty is stated by the backend ----------

    private static async Task<AssistantAnswer> AskWith(Harness h, EvidenceSet set, string locale = "en")
    {
        var svc = h.Build(new FakeConsent(true), retriever: new FixedRetriever(set));
        return await h.Ask(svc, "tell me about Testomed", locale);
    }

    [Fact]
    public async Task A_conflict_in_the_evidence_is_stated_in_front_of_the_answer_even_though_the_model_said_nothing_about_it()
    {
        using var h = new Harness(response: "The sources list interactions [E1] [E2].");
        var s = Src();
        var set = Build(Doc([s], AllKinds(s.SourceId), [
            new KnowledgeInteractionView("otheringredient", InteractionSeverity.Minor, "Says minor.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("otheringredient", InteractionSeverity.Major, "Says major.", s.SourceId, ValidationStatus.Validated)]), "interactions of Testomed");
        var a = await AskWith(h, set);
        Assert.Equal(AnswerStatus.Answered, a.Status);
        Assert.StartsWith("Before you read this:", a.Text, StringComparison.Ordinal);
        Assert.Contains("disagree", a.Text, StringComparison.Ordinal);
        Assert.EndsWith("The sources list interactions [E1] [E2].", a.Text, StringComparison.Ordinal); // the generated text follows unchanged
        Assert.Contains("evidence.conflict", a.Limitations!);
        var fa = await AskWith(h, set, "fa");
        Assert.StartsWith("پیش از خواندن:", fa.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_unverified_and_stale_evidence_each_add_their_own_sentence()
    {
        using var h = new Harness(response: "The sources list a warning [E1].");
        var old = Src(received: Now.AddDays(-4000));
        var unverified = Src(v: ValidationStatus.Unverified);
        var set = MedicationEvidenceRetriever.Build([Doc([old, unverified], [
            new KnowledgeStatementView(StatementKind.Warning, "Take care.", old.SourceId, ValidationStatus.Validated),
            new KnowledgeStatementView(StatementKind.Administration, "Take with water.", unverified.SourceId, ValidationStatus.Unverified)])], QuestionTopics.Detect(QueryNormalizer.Normalize("side effects of Testomed")), Now, TimeSpan.FromDays(1095));
        var a = await AskWith(h, set);
        Assert.StartsWith("Before you read this:", a.Text, StringComparison.Ordinal);
        Assert.Contains("not found in the sources", a.Text, StringComparison.Ordinal); // incomplete
        Assert.Contains("not been verified", a.Text, StringComparison.Ordinal);
        Assert.Contains("out of date", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("disagree", a.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_validated_fresh_evidence_gets_no_caveat_and_the_text_is_untouched()
    {
        using var h = new Harness(response: "The sources list a warning [E1].");
        var s = Src();
        var set = Build(Doc([s], AllKinds(s.SourceId)));
        Assert.Equal(EvidenceQuality.Validated, set.Quality);
        var a = await AskWith(h, set);
        Assert.Equal("The sources list a warning [E1].", a.Text);
    }

    [Fact]
    public void The_demo_provider_label_stays_the_first_thing_in_the_text_when_a_caveat_is_added()
    {
        var text = AssistantService.WithCaveat(MockAIProvider.Label + " No language model was used.\nList [E1].", "Before you read this:\n- x");
        Assert.StartsWith(MockAIProvider.Label, text, StringComparison.Ordinal);
        Assert.Contains("Before you read this:", text, StringComparison.Ordinal);
        Assert.EndsWith("List [E1].", text, StringComparison.Ordinal);
        Assert.Equal("plain", AssistantService.WithCaveat("plain", null));
    }

    // ---------- generated text must rest on the evidence ----------

    [Fact]
    public async Task An_answer_that_cites_nothing_is_withheld_and_the_evidence_is_shown_instead()
    {
        using var h = new Harness(response: "Everything about this medicine is described well elsewhere.");
        var s = Src();
        var a = await AskWith(h, Build(Doc([s], AllKinds(s.SourceId))));
        Assert.Equal(AnswerStatus.Blocked, a.Status);
        Assert.DoesNotContain("elsewhere", a.Text, StringComparison.Ordinal);
        Assert.NotEmpty(a.Evidence!.Items); // the sources are still there for the person to read
    }

    [Theory]
    [InlineData("It is described [E1].", true)]
    [InlineData("It is described [E9].", false)]
    [InlineData("It is described.", false)]
    public void Citation_rules_hold_at_the_policy_level(string text, bool ok) =>
        Assert.Equal(ok, AnswerSafetyPolicy.Check(text, ["E1", "E2"]).Count == 0);

    // ---------- generated text and deterministic rules stay apart ----------

    [Fact]
    public void The_assistant_module_and_the_clinical_rule_engine_do_not_reference_each_other()
    {
        static string[] Refs(Assembly a) => [.. a.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty)];
        var ai = Refs(typeof(AssistantService).Assembly);
        Assert.DoesNotContain(ai, n => n.StartsWith("MedSmarter.Modules.ClinicalRules", StringComparison.Ordinal));
        Assert.DoesNotContain(ai, n => n.StartsWith("MedSmarter.Modules.Guidance", StringComparison.Ordinal));
        var rules = Refs(Assembly.Load("MedSmarter.Modules.ClinicalRules"));
        Assert.DoesNotContain(rules, n => n.StartsWith("MedSmarter.Modules.AI", StringComparison.Ordinal));
    }

    // ---------- the citation rule and "the sources do not cover this" ----------

    private static EvidenceSet Incomplete(out KnowledgeSourceRef src)
    {
        src = Src();
        // only a Warning exists, so Administration and AdverseReaction are recorded as missing for this question
        return MedicationEvidenceRetriever.Build([Doc([src], [new KnowledgeStatementView(StatementKind.Warning, "Take care.", src.SourceId, ValidationStatus.Validated)])],
            QuestionTopics.Detect(QueryNormalizer.Normalize("side effects and how to take Testomed")), Now, TimeSpan.FromDays(1095));
    }

    [Fact]
    public async Task A_model_that_claims_the_sources_are_insufficient_and_adds_advice_is_withheld_and_its_claim_is_not_echoed()
    {
        using var h = new Harness(response: "The sources do not cover this, but you should drink plenty of water and take it with food.");
        var set = Build(Doc([Src()], AllKinds(Guid.Empty)));
        var a = await AskWith(h, set);
        Assert.Equal(AnswerStatus.Blocked, a.Status);
        Assert.DoesNotContain("plenty of water", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("not found in the sources", a.Text, StringComparison.Ordinal); // complete evidence: the backend makes no insufficiency statement just because the model did
        Assert.Equal(SafeMessages.Withheld("en"), a.Text);
    }

    [Fact]
    public async Task When_the_evidence_really_is_incomplete_the_withheld_answer_says_so_in_backend_words()
    {
        using var h = new Harness(response: "The sources do not cover this, but you should drink plenty of water.");
        var a = await AskWith(h, Incomplete(out _));
        Assert.Equal(AnswerStatus.Blocked, a.Status);
        Assert.StartsWith("Before you read this:", a.Text, StringComparison.Ordinal);
        Assert.Contains("not found in the sources", a.Text, StringComparison.Ordinal);
        Assert.EndsWith(SafeMessages.Withheld("en"), a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("plenty of water", a.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_evidence_at_all_the_fixed_message_is_used_and_no_model_is_called()
    {
        using var h = new Harness(response: "Anything [E1].");
        var svc = h.Build(new FakeConsent(true), retriever: new FixedRetriever(EvidenceSet.Empty));
        var a = await h.Ask(svc, "tell me about Nothingomed");
        Assert.Equal(AnswerStatus.NoEvidence, a.Status);
        Assert.Equal(SafeMessages.NoEvidence("en"), a.Text);
        Assert.Equal(0, h.Handler.Calls);
    }

    [Fact]
    public async Task Known_limit_a_cited_but_unsupported_sentence_passes_the_citation_rule_and_only_the_other_filters_apply()
    {
        // The citation rule proves the text points at evidence, not that the sentence is true to it. Nothing deterministic can prove that; the other filters still catch instructions, numbers and scare language.
        using var h = new Harness(response: "The sources mention hydration [E1].");
        Assert.Equal(AnswerStatus.Answered, (await AskWith(h, Build(Doc([Src()], AllKinds(Guid.Empty))))).Status);
        using var h2 = new Harness(response: "You should stop taking the tablets [E1].");
        Assert.Equal(AnswerStatus.Blocked, (await AskWith(h2, Build(Doc([Src()], AllKinds(Guid.Empty))))).Status);
    }

    // ---------- conflict detection details ----------

    [Fact]
    public void An_ingredient_name_containing_a_bar_does_not_shift_the_severity_and_hide_a_conflict()
    {
        var s = Src();
        var set = Build(Doc([s], AllKinds(s.SourceId), [
            new KnowledgeInteractionView("alpha | beta", InteractionSeverity.Minor, "Says minor.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("alpha | beta", InteractionSeverity.Major, "Says major.", s.SourceId, ValidationStatus.Validated)]), "interactions of Testomed");
        Assert.Single(set.Conflicts);
        var same = Build(Doc([s], AllKinds(s.SourceId), [
            new KnowledgeInteractionView("alpha | beta", InteractionSeverity.Major, "Says major.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("alpha | beta", InteractionSeverity.Major, "Says major again.", s.SourceId, ValidationStatus.Validated)]), "interactions of Testomed");
        Assert.Empty(same.Conflicts);
    }

    [Fact]
    public void Conflict_detection_is_deterministic_and_never_reorders_or_drops_either_side()
    {
        var s = Src();
        var doc = Doc([s], AllKinds(s.SourceId), [
            new KnowledgeInteractionView("x", InteractionSeverity.Minor, "Says minor.", s.SourceId, ValidationStatus.Validated),
            new KnowledgeInteractionView("x", InteractionSeverity.Major, "Says major.", s.SourceId, ValidationStatus.Validated)]);
        var a = Build(doc, "interactions of Testomed");
        var b = Build(doc, "interactions of Testomed");
        Assert.Equal(a.Conflicts.Select(c => string.Join(',', c.ItemIds)), b.Conflicts.Select(c => string.Join(',', c.ItemIds)));
        Assert.Contains(a.Items, i => i.Text == "Says minor.");
        Assert.Contains(a.Items, i => i.Text == "Says major.");
    }

    [Theory]
    [InlineData("You should stop taking it [E1].", true)]
    [InlineData("Consider skipping taking them for a week [E1].", true)]
    [InlineData("Discontinuing using this is an option [E1].", true)]
    [InlineData("The sources mention taking it with water [E1].", false)]
    [InlineData("The sources describe a warning about stopping suddenly [E1].", false)]
    public void Instructions_to_stop_or_skip_are_caught_even_when_the_medicine_is_only_a_pronoun(string text, bool blocked) =>
        Assert.Equal(blocked, AnswerSafetyPolicy.Check(text, ["E1"]).Contains("medication_change_instruction"));
}
