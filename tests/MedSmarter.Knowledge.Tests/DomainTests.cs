using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Knowledge.Tests;

public class ModelTests
{
    [Fact]
    public async Task Single_ingredient_medication_has_brand_ingredient_form_route_and_strength()
    {
        using var env = new KEnv();
        var d = (await env.Meds.GetAsync((await env.One("nocturin")).Id)).Value!;
        Assert.Equal("Nocturin", d.Brand!.Name.En);
        Assert.Equal("Tablet", d.DosageForm.En);
        Assert.Equal("Oral", Assert.Single(d.Routes).En);
        Assert.Equal("5 mg", d.StrengthSummary);
        var ing = Assert.Single(d.Ingredients);
        Assert.Equal(5, ing.StrengthValue);
        Assert.Contains("nocturamide", ing.Name.En);
        Assert.NotNull(d.Manufacturer);
    }

    [Fact]
    public async Task Multi_ingredient_medication_keeps_each_ingredient_and_strength()
    {
        using var env = new KEnv();
        var d = (await env.Meds.GetAsync((await env.One("duodemo")).Id)).Value!;
        Assert.Equal(2, d.Ingredients.Count);
        Assert.Equal("10 mg / 5 mg", d.StrengthSummary);
        Assert.Equal([0, 1], d.Ingredients.Select(i => i.Order));
    }

    [Fact]
    public async Task Brand_and_generic_products_share_an_ingredient_but_are_distinct_records()
    {
        using var env = new KEnv();
        var brand = (await env.Meds.GetAsync((await env.One("demopril")).Id)).Value!;
        var generic = (await env.Meds.GetAsync((await env.One("generic demoprilate")).Id)).Value!;
        Assert.NotEqual(brand.Id, generic.Id);
        Assert.NotNull(brand.Brand);
        Assert.Null(generic.Brand);
        Assert.Equal(brand.Ingredients[0].IngredientId, generic.Ingredients[0].IngredientId);
    }

    [Fact]
    public async Task Absent_information_is_reported_as_missing_never_guessed()
    {
        using var env = new KEnv();
        var d = (await env.Meds.GetAsync((await env.One("duodemo")).Id)).Value!;
        Assert.Empty(d.Statements);
        Assert.Equal(Enum.GetValues<StatementKind>().Length, d.MissingKinds.Count);
        var nocturin = (await env.Meds.GetAsync((await env.One("nocturin")).Id)).Value!;
        Assert.DoesNotContain(StatementKind.Warning, nocturin.MissingKinds);
        Assert.Contains(StatementKind.Contraindication, nocturin.MissingKinds); // nothing invented
        Assert.Contains(StatementKind.Indication, nocturin.MissingKinds);
    }

    [Fact]
    public async Task Demo_records_are_labelled_and_carry_no_official_identifier()
    {
        using var env = new KEnv();
        foreach (var s in (await env.Meds.SearchAsync(new MedicationSearchQuery(null, 50, 0))).Items)
        {
            var d = (await env.Meds.GetAsync(s.Id)).Value!;
            Assert.True(d.IsDemo);
            Assert.Equal(ValidationStatus.Demo, d.Validation);
            Assert.Contains("NOT FOR CLINICAL USE", d.Notice, StringComparison.Ordinal);
            Assert.Empty(d.Identifiers);
            Assert.All(d.Statements, st => Assert.Equal(ValidationStatus.Demo, st.Validation));
        }
    }

    [Fact]
    public async Task Interactions_connect_ingredients_with_a_source_in_either_direction()
    {
        using var env = new KEnv();
        var demopril = (await env.Meds.GetAsync((await env.One("demopril")).Id)).Value!;
        var nocturin = (await env.Meds.GetAsync((await env.One("nocturin")).Id)).Value!;
        var a = Assert.Single(demopril.Interactions);
        var b = Assert.Single(nocturin.Interactions);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(InteractionSeverity.Moderate, a.Severity);
        Assert.NotEqual(Guid.Empty, a.SourceId);
        Assert.Contains("nocturamide", a.OtherIngredientName.En);
    }

    [Fact]
    public async Task Interaction_needs_known_ingredients_a_source_and_two_different_ingredients()
    {
        using var env = new KEnv();
        var (_, rev) = await env.RealSource();
        var i1 = await env.Ingredient("alphium");
        var i2 = await env.Ingredient("betium");
        var ok = await env.Admin.UpsertInteractionAsync(KEnv.Actor, new NewInteraction(i2, i1, InteractionSeverity.Major, new LocalizedText("m", null), new LocalizedText("x", null), rev.Id), "test", null);
        Assert.True(ok.Succeeded);
        Assert.Equal((i1.CompareTo(i2) < 0 ? i2 : i1), ok.Value!.OtherIngredientId); // stored in canonical order
        Assert.Equal(MedicationError.Validation, (await env.Admin.UpsertInteractionAsync(KEnv.Actor, new NewInteraction(i1, i1, InteractionSeverity.Minor, new LocalizedText("m", null), new LocalizedText("x", null), rev.Id), "test", null)).Error);
        Assert.Equal(MedicationError.Validation, (await env.Admin.UpsertInteractionAsync(KEnv.Actor, new NewInteraction(i1, Guid.NewGuid(), InteractionSeverity.Minor, new LocalizedText("m", null), new LocalizedText("x", null), rev.Id), "test", null)).Error);
        Assert.Equal(MedicationError.Validation, (await env.Admin.UpsertInteractionAsync(KEnv.Actor, new NewInteraction(i1, i2, InteractionSeverity.Minor, new LocalizedText("m", null), new LocalizedText("x", null), Guid.NewGuid()), "test", null)).Error);
    }
}

public class ValidationAndProvenanceTests
{
    private static async Task<(KEnv Env, KnowledgeRevisionDto Rev, Guid Ing)> Setup()
    {
        var env = new KEnv(seed: false);
        foreach (var t in new[] { (ReferenceKind.DosageForm, "tablet"), (ReferenceKind.Route, "oral") })
        {
            await env.Admin.CreateReferenceTermAsync(KEnv.Actor, new NewReferenceTerm(t.Item1, t.Item2, new LocalizedText(t.Item2, null)));
        }

        var (_, rev) = await env.RealSource();
        return (env, rev, await env.Ingredient("testium"));
    }

    [Theory]
    [InlineData("4006381333931", true)]   // valid EAN-13 check digit (checksum arithmetic only; no product is implied)
    [InlineData("4006381333932", false)]
    [InlineData("12345", false)]
    [InlineData("abcdefghijklm", false)]
    public void Gtin_check_digit_is_verified(string gtin, bool valid) => Assert.Equal(valid, MedicationValidator.IsValidGtin(gtin));

    [Theory]
    [InlineData("N02BE01", true)]
    [InlineData("N02", true)]
    [InlineData("N", true)]
    [InlineData("n02be01", false)]
    [InlineData("N02BE0", false)]
    [InlineData("1234567", false)]
    public void Atc_format_is_verified(string atc, bool valid) => Assert.Equal(valid, MedicationValidator.IsValidAtc(atc));

    [Fact]
    public async Task Identifiers_need_a_source_a_valid_format_and_must_be_unique()
    {
        var (env, rev, ing) = await Setup();
        using var _ = env;
        var noSource = env.Draft(ing, rev.Id, ids: [new NewIdentifier(IdentifierScheme.ExternalDrugId, "X-1", null)]);
        Assert.Contains("identifier.source.required", (await env.Admin.CreateAsync(KEnv.Actor, noSource, "t", null)).Detail);
        var badGtin = env.Draft(ing, rev.Id, ids: [new NewIdentifier(IdentifierScheme.Gtin, "4006381333932", rev.Id)]);
        Assert.Contains("identifier.gtin.invalid", (await env.Admin.CreateAsync(KEnv.Actor, badGtin, "t", null)).Detail);
        var good = env.Draft(ing, rev.Id, ids: [new NewIdentifier(IdentifierScheme.Gtin, "4006381333931", rev.Id)]);
        Assert.True((await env.Admin.CreateAsync(KEnv.Actor, good, "t", null)).Succeeded);
        var dup = await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, name: "Other", ids: [new NewIdentifier(IdentifierScheme.Gtin, "4006381333931", rev.Id)]), "t", null);
        Assert.Equal(MedicationError.Conflict, dup.Error);
    }

    [Fact]
    public async Task Fictional_records_can_never_carry_an_official_identifier()
    {
        using var env = new KEnv();
        var demoRev = (await env.Sources.ListSourcesAsync()).Single(s => s.Type == SourceType.Demo);
        var rev = (await env.Sources.ListRevisionsAsync(demoRev.Id)).Single();
        var ing = await env.Ingredient("fictium");
        foreach (var scheme in new[] { IdentifierScheme.NationalDrugCode, IdentifierScheme.Gtin, IdentifierScheme.ManufacturerCode })
        {
            var value = scheme == IdentifierScheme.Gtin ? "4006381333931" : "ABC123";
            var r = await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, demo: true, ids: [new NewIdentifier(scheme, value, rev.Id)]), "t", null);
            Assert.Equal(MedicationError.Validation, r.Error);
            Assert.Contains("identifier.official_on_demo", r.Detail);
        }
    }

    [Fact]
    public async Task Fictional_and_real_sources_never_mix()
    {
        var (env, realRev, ing) = await Setup();
        using var _ = env;
        var demoRev = (await env.Sources.RegisterSourceAsync(KEnv.Actor, new NewKnowledgeSource("Demo", "Test", SourceType.Demo, null, "d1", "Fictional", true, null), "t", null)).Value!;
        var demoRevision = (await env.Sources.AddRevisionAsync(KEnv.Actor, new NewRevision(demoRev.Id, "d1", null), "t", null)).Value!;
        var stmt = new NewStatement(StatementKind.Warning, new LocalizedText("w", null), "info", null, null, demoRevision.Id);
        Assert.Contains("source.demo_mismatch", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, realRev.Id, demo: false, statements: [stmt]), "t", null)).Detail); // real record, demo source
        var realStmt = stmt with { RevisionId = realRev.Id };
        Assert.Contains("source.demo_mismatch", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, realRev.Id, demo: true, statements: [realStmt]), "t", null)).Detail); // demo record, real source
    }

    [Fact]
    public async Task Statements_need_text_a_known_revision_and_valid_enumerations()
    {
        var (env, rev, ing) = await Setup();
        using var _ = env;
        Assert.Contains("statement.text.required", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, statements: [new NewStatement(StatementKind.Warning, new LocalizedText(" ", null), null, null, null, rev.Id)]), "t", null)).Detail);
        Assert.Contains("revision.unknown", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, statements: [new NewStatement(StatementKind.Warning, new LocalizedText("w", null), null, null, null, Guid.NewGuid())]), "t", null)).Detail);
        Assert.Contains("statement.source.required", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, statements: [new NewStatement(StatementKind.Warning, new LocalizedText("w", null), null, null, null, Guid.Empty)]), "t", null)).Detail);
        Assert.Contains("statement.severity", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, statements: [new NewStatement(StatementKind.Warning, new LocalizedText("w", null), "catastrophic", null, null, rev.Id)]), "t", null)).Detail);
        Assert.Contains("statement.frequency", (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, statements: [new NewStatement(StatementKind.Warning, new LocalizedText("w", null), null, "common", null, rev.Id)]), "t", null)).Detail); // frequency only for ADRs
    }

    [Fact]
    public async Task Draft_validation_covers_names_ingredients_strength_and_references()
    {
        var (env, rev, ing) = await Setup();
        using var _ = env;
        var good = env.Draft(ing, rev.Id);
        Assert.Contains("name.required", (await env.Admin.CreateAsync(KEnv.Actor, good with { Name = new LocalizedText(null, " ") }, "t", null)).Detail);
        Assert.Contains("ingredients.count", (await env.Admin.CreateAsync(KEnv.Actor, good with { Ingredients = [] }, "t", null)).Detail);
        Assert.Contains("ingredients.duplicate", (await env.Admin.CreateAsync(KEnv.Actor, good with { Ingredients = [good.Ingredients[0], good.Ingredients[0]] }, "t", null)).Detail);
        Assert.Contains("strength.unit", (await env.Admin.CreateAsync(KEnv.Actor, good with { Ingredients = [new NewIngredientRef(ing, 5, "bananas", null)] }, "t", null)).Detail);
        Assert.Contains("strength.range", (await env.Admin.CreateAsync(KEnv.Actor, good with { Ingredients = [new NewIngredientRef(ing, -1, "mg", null)] }, "t", null)).Detail);
        Assert.Contains("strength.value_and_unit", (await env.Admin.CreateAsync(KEnv.Actor, good with { Ingredients = [new NewIngredientRef(ing, 5, null, null)] }, "t", null)).Detail);
        Assert.Contains("dosage_form.unknown", (await env.Admin.CreateAsync(KEnv.Actor, good with { DosageFormCode = "nope" }, "t", null)).Detail);
        Assert.Contains("route.unknown", (await env.Admin.CreateAsync(KEnv.Actor, good with { RouteCodes = ["nope"] }, "t", null)).Detail);
        Assert.Contains("ingredient.unknown", (await env.Admin.CreateAsync(KEnv.Actor, good with { Ingredients = [new NewIngredientRef(Guid.NewGuid(), 5, "mg", null)] }, "t", null)).Detail);
        Assert.Contains("brand.unknown", (await env.Admin.CreateAsync(KEnv.Actor, good with { BrandId = Guid.NewGuid() }, "t", null)).Detail);
        Assert.True((await env.Admin.CreateAsync(KEnv.Actor, good, "t", null)).Succeeded);
    }

    [Fact]
    public async Task Errors_carry_codes_only_never_the_offending_text()
    {
        var (env, rev, ing) = await Setup();
        using var _ = env;
        var r = await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id) with { Synonyms = ["<script>alert(1)</script>" + new string('x', 200)] }, "t", null);
        Assert.Equal(MedicationError.Validation, r.Error);
        Assert.DoesNotContain("script", r.Detail, StringComparison.OrdinalIgnoreCase);
    }
}

public class VersioningAndLifecycleTests
{
    private static async Task<(KEnv Env, MedicationDetailDto Med, KnowledgeRevisionDto Rev, Guid Ing)> Created()
    {
        var env = new KEnv(seed: false);
        foreach (var t in new[] { (ReferenceKind.DosageForm, "tablet"), (ReferenceKind.Route, "oral") })
        {
            await env.Admin.CreateReferenceTermAsync(KEnv.Actor, new NewReferenceTerm(t.Item1, t.Item2, new LocalizedText(t.Item2, null)));
        }

        var (_, rev) = await env.RealSource();
        var ing = await env.Ingredient("testium");
        var med = (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id), "t", "c-1")).Value!;
        return (env, med, rev, ing);
    }

    [Fact]
    public async Task New_medications_start_as_unverified_drafts_at_version_one()
    {
        var (env, med, _, _) = await Created();
        using var _1 = env;
        Assert.Equal(1, med.Version);
        Assert.Equal(LifecycleStatus.Draft, med.Lifecycle);
        Assert.Equal(ValidationStatus.Unverified, med.Validation);
        Assert.Contains("NOT VALIDATED", med.Notice, StringComparison.Ordinal);
        Assert.Equal(MedicationError.NotFound, (await env.Meds.GetAsync(med.Id)).Error); // drafts are invisible to ordinary readers
    }

    [Fact]
    public async Task Every_change_creates_a_version_and_a_stale_edit_is_a_conflict()
    {
        var (env, med, rev, ing) = await Created();
        using var _1 = env;
        var active = (await env.Admin.SetLifecycleAsync(KEnv.Actor, med.Id, 1, LifecycleStatus.Active, "t", null)).Value!;
        Assert.Equal(2, active.Version);
        var edited = await env.Admin.UpdateAsync(KEnv.Actor, med.Id, 2, env.Draft(ing, rev.Id, name: "Testomed v2"), "renamed", "t", null);
        Assert.Equal(3, edited.Value!.Version);
        var stale = await env.Admin.UpdateAsync(KEnv.Actor, med.Id, 2, env.Draft(ing, rev.Id, name: "Lost update"), "stale", "t", null);
        Assert.Equal(MedicationError.Conflict, stale.Error);
        var versions = (await env.Admin.ListVersionsAsync(med.Id)).Value!;
        Assert.Equal([1, 2, 3], versions.Select(v => v.Version));
        Assert.Equal("renamed", versions[2].ChangeReason);
        Assert.Equal("Testomed v2", (await env.Meds.GetAsync(med.Id)).Value!.Name.En);
    }

    [Fact]
    public async Task Editing_requires_a_reason()
    {
        var (env, med, rev, ing) = await Created();
        using var _1 = env;
        Assert.Contains("reason.required", (await env.Admin.UpdateAsync(KEnv.Actor, med.Id, 1, env.Draft(ing, rev.Id), " ", "t", null)).Detail);
    }

    [Fact]
    public async Task Deactivating_hides_the_medication_without_deleting_it()
    {
        var (env, med, _, _) = await Created();
        using var _1 = env;
        await env.Admin.SetLifecycleAsync(KEnv.Actor, med.Id, 1, LifecycleStatus.Active, "t", null);
        Assert.Single((await env.Meds.SearchAsync(new MedicationSearchQuery("testomed"))).Items);
        await env.Admin.SetLifecycleAsync(KEnv.Actor, med.Id, 2, LifecycleStatus.Inactive, "t", null);
        Assert.Empty((await env.Meds.SearchAsync(new MedicationSearchQuery("testomed"))).Items);
        Assert.True((await env.Meds.GetAsync(med.Id, includeNonActive: true)).Succeeded);
        await env.Admin.SetLifecycleAsync(KEnv.Actor, med.Id, 3, LifecycleStatus.Active, "t", null); // reactivation is possible
        Assert.Single((await env.Meds.SearchAsync(new MedicationSearchQuery("testomed"))).Items);
        Assert.Contains("lifecycle.transition", (await env.Admin.SetLifecycleAsync(KEnv.Actor, med.Id, 4, LifecycleStatus.Draft, "t", null)).Detail);
    }

    [Fact]
    public async Task Verified_status_requires_a_validated_revision_a_real_source_and_a_confirmed_licence()
    {
        var (env, med, rev, _) = await Created();
        using var _1 = env;
        var ok = await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 1, ValidationStatus.Validated, rev.Id, "t", null);
        Assert.True(ok.Succeeded, ok.Detail);
        Assert.Equal(ValidationStatus.Validated, ok.Value!.Validation);
        Assert.Contains("Source-validated", ok.Value.Notice, StringComparison.Ordinal);

        var (_, unvalidatedRev) = await env.RealSource(validatedRevision: false);
        Assert.Contains("revision.not_validated", (await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 2, ValidationStatus.Validated, unvalidatedRev.Id, "t", null)).Detail);
        var (_, noLicenceRev) = await env.RealSource(redistribution: false);
        Assert.Contains("license.redistribution_not_confirmed", (await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 2, ValidationStatus.Validated, noLicenceRev.Id, "t", null)).Detail);
        Assert.Contains("revision.unknown", (await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 2, ValidationStatus.Validated, Guid.NewGuid(), "t", null)).Detail);
    }

    [Fact]
    public async Task Whoever_edited_a_medication_cannot_validate_it_but_a_second_person_can()
    {
        var (env, med, rev, ing) = await Created();
        using var _1 = env;
        var self = await env.Admin.SetValidationAsync(KEnv.Actor, med.Id, 1, ValidationStatus.Validated, rev.Id, "t", null); // KEnv.Actor created it
        Assert.Equal(MedicationError.Forbidden, self.Error);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(ActorUserId: KEnv.Actor, Take: 50)), e => e.Action == AuditActions.MedicationValidationChanged && e.Result == AuditResult.Denied && e.ReasonCode == "separation_of_duties");
        Assert.Equal(1, (await env.Meds.GetAsync(med.Id, true)).Value!.Version); // nothing changed
        Assert.True((await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 1, ValidationStatus.Validated, rev.Id, "t", null)).Succeeded);
        // The reviewer then edits it: the edit withdraws validation, and now the reviewer is an editor and the original author may validate.
        var edited = await env.Admin.UpdateAsync(KEnv.Reviewer, med.Id, 2, env.Draft(ing, rev.Id, name: "Second hand"), "typo", "t", null);
        Assert.True(edited.Succeeded);
        Assert.Equal(MedicationError.Forbidden, (await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 3, ValidationStatus.Validated, rev.Id, "t", null)).Error);
        Assert.Equal(MedicationError.Forbidden, (await env.Admin.SetValidationAsync(KEnv.Actor, med.Id, 3, ValidationStatus.Validated, rev.Id, "t", null)).Error); // the author is still an editor of this record
    }

    [Fact]
    public async Task Editing_content_withdraws_validation()
    {
        var (env, med, rev, ing) = await Created();
        using var _1 = env;
        await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 1, ValidationStatus.Validated, rev.Id, "t", null);
        var edited = await env.Admin.UpdateAsync(KEnv.Actor, med.Id, 2, env.Draft(ing, rev.Id, name: "Changed"), "changed text", "t", null);
        Assert.Equal(ValidationStatus.Unverified, edited.Value!.Validation);
    }

    [Fact]
    public async Task Fictional_records_cannot_be_validated_and_demo_status_cannot_be_assigned()
    {
        using var env = new KEnv();
        var demo = await env.One("nocturin");
        var detail = (await env.Meds.GetAsync(demo.Id)).Value!;
        var (_, rev) = await env.RealSource();
        Assert.Contains("demo.cannot_change_validation", (await env.Admin.SetValidationAsync(KEnv.Reviewer, demo.Id, detail.Version, ValidationStatus.Validated, rev.Id, "t", null)).Detail);
        var (env2, med, rev2, _) = await Created();
        using var _1 = env2;
        Assert.Contains("demo.cannot_change_validation", (await env2.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 1, ValidationStatus.Demo, rev2.Id, "t", null)).Detail);
    }

    [Fact]
    public async Task A_demo_revision_can_never_be_marked_validated()
    {
        using var env = new KEnv();
        var src = (await env.Sources.ListSourcesAsync()).Single(s => s.Type == SourceType.Demo);
        var rev = (await env.Sources.ListRevisionsAsync(src.Id)).Single();
        Assert.Equal("source.demo", (await env.Sources.SetRevisionStatusAsync(KEnv.Actor, rev.Id, RevisionStatus.Validated, "t", null)).Detail);
    }

    [Fact]
    public async Task Sensitive_changes_are_audited_without_clinical_content()
    {
        var (env, med, rev, _) = await Created();
        using var _1 = env;
        await env.Admin.SetLifecycleAsync(KEnv.Actor, med.Id, 1, LifecycleStatus.Active, "web", "corr-12345678");
        await env.Admin.SetValidationAsync(KEnv.Reviewer, med.Id, 2, ValidationStatus.Validated, rev.Id, "web", "corr-12345678");
        var entries = await env.Audit.QueryAsync(new AuditQuery(ActorUserId: KEnv.Actor, Take: 100));
        Assert.Contains(entries, e => e.Action == AuditActions.MedicationCreated);
        Assert.Contains(entries, e => e.Action == AuditActions.MedicationLifecycleChanged && e.ReasonCode == "Active");
        Assert.Contains(entries, e => e.Action == AuditActions.MedicationValidationChanged && e.ReasonCode == "Validated");
        Assert.Contains(entries, e => e.Action == AuditActions.KnowledgeSourceRegistered);
        Assert.DoesNotContain(entries, e => e.Metadata.Values.Any(v => v.Contains("Testomed", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Knowledge_sources_need_publisher_version_and_a_stated_licence()
    {
        using var env = new KEnv(seed: false);
        Assert.Contains("license.required", (await env.Sources.RegisterSourceAsync(KEnv.Actor, new NewKnowledgeSource("S", "P", SourceType.Publication, null, "1", " ", null, null), "t", null)).Detail);
        Assert.Contains("publisher.required", (await env.Sources.RegisterSourceAsync(KEnv.Actor, new NewKnowledgeSource("S", "", SourceType.Publication, null, "1", "L", null, null), "t", null)).Detail);
        Assert.Contains("url.invalid", (await env.Sources.RegisterSourceAsync(KEnv.Actor, new NewKnowledgeSource("S", "P", SourceType.Publication, "javascript:alert(1)", "1", "L", null, null), "t", null)).Detail);
        var unknownLicence = await env.Sources.RegisterSourceAsync(KEnv.Actor, new NewKnowledgeSource("S", "P", SourceType.Publication, null, "1", "L", null, null), "t", null);
        Assert.Null(unknownLicence.Value!.RedistributionAllowed); // "not checked" is representable and blocks verification
    }

    [Fact]
    public async Task Knowledge_document_is_source_carrying_and_labelled()
    {
        using var env = new KEnv();
        var doc = (await env.Meds.GetKnowledgeDocumentAsync((await env.One("nocturin")).Id)).Value!;
        Assert.Contains("Nocturin", doc.Names);
        Assert.Contains(doc.Ingredients, i => i.Contains("nocturamide", StringComparison.Ordinal));
        Assert.True(doc.IsDemo);
        Assert.NotEmpty(doc.Sources);
        Assert.All(doc.Statements, s => Assert.Contains(doc.Sources, src => src.SourceId == s.SourceId));
        Assert.Contains("NOT FOR CLINICAL USE", doc.Notice, StringComparison.Ordinal);
        Assert.Equal(MedicationError.NotFound, (await env.Meds.GetKnowledgeDocumentAsync(Guid.NewGuid())).Error);
    }
}
