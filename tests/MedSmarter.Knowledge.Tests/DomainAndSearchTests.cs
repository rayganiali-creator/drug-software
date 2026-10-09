using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Knowledge.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("نوكتورين", "نوکتورین")]     // Arabic ك/ي → Persian ک/ی
    [InlineData("گليكانور", "گلیکانور")]
    [InlineData("  Demo   PRIL ", "demo pril")]
    [InlineData("DEMOPRIL", "demopril")]
    [InlineData("۱۰ mg", "10 mg")]            // Persian digits
    [InlineData("١٠ mg", "10 mg")]            // Arabic-Indic digits
    [InlineData("می‌خورم", "می خورم")]         // ZWNJ → space
    [InlineData("آسپرین", "اسپرین")]          // آ → ا
    [InlineData("دَموپریل", "دموپریل")]       // diacritics removed
    [InlineData("a-b/c_d.e", "a b c d e")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("!!!", "")]
    public void Normalizes_text(string? input, string expected) => Assert.Equal(expected, TextNormalizer.Normalize(input));

    [Fact]
    public void Persian_and_Arabic_spellings_normalize_identically() =>
        Assert.Equal(TextNormalizer.Normalize("سولویتا"), TextNormalizer.Normalize("سولويتا"));
}

public class QueryValidationTests
{
    [Theory]
    [InlineData("a", "q.too_short")]
    [InlineData("!!", "q.invalid")]
    [InlineData("ab\u0000cd", "q.invalid_characters")]
    [InlineData("a b c d e f g h i", "q.too_many_terms")]
    public void Rejects_bad_queries(string q, string code) => Assert.Equal(code, SearchQueryValidator.Validate(new MedicationSearchQuery(q)).Error);

    [Fact]
    public void Rejects_very_long_queries() => Assert.Equal("q.too_long", SearchQueryValidator.Validate(new MedicationSearchQuery(new string('a', 65))).Error);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(51, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 1001)]
    public void Rejects_out_of_range_paging(int limit, int offset) =>
        Assert.NotNull(SearchQueryValidator.Validate(new MedicationSearchQuery("demo", limit, offset)).Error);

    [Fact]
    public void Empty_query_means_browse() => Assert.Null(SearchQueryValidator.Validate(new MedicationSearchQuery(null)).Error);

    [Fact]
    public async Task Service_throws_a_code_only_exception_for_invalid_queries()
    {
        using var env = new KEnv();
        var ex = await Assert.ThrowsAsync<MedicationSearchException>(() => env.Meds.SearchAsync(new MedicationSearchQuery("x")));
        Assert.Equal("q.too_short", ex.Code);
    }
}

public class SearchTests
{
    [Theory]
    [InlineData("demopril", "Demopril")]
    [InlineData("DEMOPRIL", "Demopril")]
    [InlineData("دموپریل", "Demopril")]
    [InlineData("نوكتورين", "Nocturin")]      // Arabic letters
    [InlineData("گليكانور", "Glycanor")]
    [InlineData("سولويتا", "Solvita")]
    [InlineData("nocturamide", "Nocturin")]    // by active ingredient
    [InlineData("نوکتورامید", "Nocturin")]
    [InlineData("glycanorine", "Glycanor")]    // ingredient synonym
    [InlineData("generic demoprilate", "Demoprilate 10 mg tablet")] // medication synonym
    public async Task Finds_by_name_ingredient_synonym_and_spelling_variant(string q, string expectedName)
    {
        using var env = new KEnv();
        Assert.Contains(expectedName, await env.Names(q));
    }

    [Fact]
    public async Task Partial_names_match()
    {
        using var env = new KEnv();
        Assert.Contains("Respirex", await env.Names("respi"));
        Assert.Contains("Respirex", await env.Names("spirex"));
    }

    [Fact]
    public async Task Ingredient_search_returns_every_medication_with_that_ingredient_including_combinations()
    {
        using var env = new KEnv();
        var names = await env.Names("demoprilate");
        Assert.Contains("Demopril", names);
        Assert.Contains("Demoprilate 10 mg tablet", names);
        Assert.Contains("Duodemo", names);
    }

    [Fact]
    public async Task Exact_name_outranks_prefix_and_substring_matches()
    {
        using var env = new KEnv();
        var names = await env.Names("demopril");
        Assert.Equal("Demopril", names[0]);
        Assert.True(names.Count > 1);
    }

    [Fact]
    public async Task No_match_gives_an_empty_page_not_an_error()
    {
        using var env = new KEnv();
        var page = await env.Meds.SearchAsync(new MedicationSearchQuery("zzzzzz"));
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Paging_is_stable_and_bounded()
    {
        using var env = new KEnv();
        var all = (await env.Meds.SearchAsync(new MedicationSearchQuery(null, 50, 0))).Items.Select(i => i.Id).ToList();
        var p1 = await env.Meds.SearchAsync(new MedicationSearchQuery(null, 3, 0));
        var p2 = await env.Meds.SearchAsync(new MedicationSearchQuery(null, 3, 3));
        Assert.Equal(all.Count, p1.Total);
        Assert.Equal(3, p1.Items.Count);
        Assert.Equal(all.Take(3), p1.Items.Select(i => i.Id));
        Assert.Equal(all.Skip(3).Take(3), p2.Items.Select(i => i.Id));
        Assert.Empty((await env.Meds.SearchAsync(new MedicationSearchQuery(null, 3, 1000))).Items);
    }

    [Fact]
    public async Task Inactive_medications_are_hidden_unless_an_editor_asks()
    {
        using var env = new KEnv();
        Assert.DoesNotContain("Oldmed (inactive demo)", await env.Names("oldmed"));
        var withInactive = await env.Meds.SearchAsync(new MedicationSearchQuery("oldmed", 20, 0, true));
        Assert.Contains(withInactive.Items, i => i.Lifecycle == LifecycleStatus.Inactive);
        var hidden = (await env.Meds.SearchAsync(new MedicationSearchQuery("oldmed", 20, 0, true))).Items.Single();
        Assert.Equal(MedicationError.NotFound, (await env.Meds.GetAsync(hidden.Id)).Error);
        Assert.True((await env.Meds.GetAsync(hidden.Id, includeNonActive: true)).Succeeded);
    }

    [Fact]
    public async Task Codes_match_only_exactly()
    {
        using var env = new KEnv(seed: false);
        var (_, rev) = await env.RealSource();
        await env.Admin.CreateReferenceTermAsync(KEnv.Actor, new NewReferenceTerm(ReferenceKind.DosageForm, "tablet", new LocalizedText("Tablet", null)));
        await env.Admin.CreateReferenceTermAsync(KEnv.Actor, new NewReferenceTerm(ReferenceKind.Route, "oral", new LocalizedText("Oral", null)));
        var ing = await env.Ingredient("testium");
        var created = await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id, ids: [new NewIdentifier(IdentifierScheme.AtcCode, "N02BE01", rev.Id)]), "test", null);
        Assert.True(created.Succeeded, created.Detail);
        await env.Admin.SetLifecycleAsync(KEnv.Actor, created.Value!.Id, 1, LifecycleStatus.Active, "test", null);
        Assert.Single((await env.Meds.SearchAsync(new MedicationSearchQuery("n02be01"))).Items);
        Assert.Empty((await env.Meds.SearchAsync(new MedicationSearchQuery("n02be"))).Items); // partial code = noise
    }
}
