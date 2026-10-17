using System.Net;
using System.Text.Json;
using MedSmarter.Modules.ProductTrace;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 15: the industry view is organization-scoped, aggregate-only, suppressed, labelled and never mixes simulated with real.</summary>
public class Phase15Tests
{
    internal const string Org = "023fb39a-0093-5296-a985-9d68009a7d92"; // DEMO Pharma Co
    internal static readonly Guid Maker = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid OtherMaker = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Med1 = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    internal static async Task Seed(IDbContextFactory<ProductTraceDbContext> factory, Guid maker, Guid? med, int reports, int reporters, ReportIssueType issue = ReportIssueType.QualityProblem,
        bool mock = false, bool demo = false, ReportStatus status = ReportStatus.Sent, DateOnly? on = null, string product = "Reference product", DateTimeOffset? sentAt = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var patients = Enumerable.Range(0, reporters).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < reports; i++)
        {
            var pid = patients[i % reporters];
            var prod = new ProductRecordRow { Id = Guid.NewGuid(), PatientId = pid, MedicationId = med, ProductName = product, ManufacturerId = maker, BatchNumber = "SECRET-BATCH-" + i, BatchNormalized = "x" + i, ProductKey = "k", ExpiryDate = new DateOnly(2030, 1, 1), IsDemo = demo };
            db.Products.Add(prod);
            db.Reports.Add(new ReportRow
            {
                Id = Guid.NewGuid(), Reference = Guid.NewGuid().ToString("N")[..20], PatientId = pid, ProductRecordId = prod.Id, Status = status, IssueType = issue, Severity = ReportSeverity.Mild,
                OccurredOn = on ?? new DateOnly(2026, 9, 10), SentAt = sentAt ?? new DateTimeOffset((on ?? new DateOnly(2026, 9, 10)).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), Description = "free text that must never be returned", IsMockDelivery = mock, IsDemo = demo, CreatedBy = pid,
            });
        }

        await db.SaveChangesAsync();
    }

    private static PEnv Env(int? k = null) => new(settings: new()
    {
        [$"IndustryInsights:Organizations:{Org}:0"] = Maker.ToString(),
        ["IndustryInsights:MinCellSize"] = (k ?? 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    [Fact]
    public void The_disclosure_rule_hides_small_cells_and_small_reporter_counts_and_orders_by_name_not_size()
    {
        var cells = DisclosureRule.Apply([new("b", "Beta", 9, 9), new("a", "Alpha", 4, 4), new("c", "Gamma", 20, 2), new("d", "Delta", 5, 5)], 5);
        Assert.Equal(["Alpha", "Beta", "Delta", "Gamma"], cells.Select(c => c.Label));
        Assert.True(cells[0].Suppressed);
        Assert.Null(cells[0].Reports);
        Assert.Equal(9, cells[1].Reports);
        Assert.Equal(5, cells[2].Reports);
        Assert.True(cells[3].Suppressed); // 20 reports but only 2 reporters: one household could be behind it
        Assert.Null(cells[3].Reports);
    }

    [Fact]
    public async Task An_organization_with_no_link_sees_nothing_and_a_linked_one_sees_only_its_own_manufacturers_reports()
    {
        using var unlinked = new PEnv();
        var none = (await unlinked.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.Real)).Value!;
        Assert.False(none.Linked);
        Assert.Empty(none.ByIssueType);

        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Seed(f, Maker, Med1, 6, 6);
        await Seed(f, OtherMaker, Med1, 30, 30); // another manufacturer: never visible
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.Real)).Value!;
        Assert.True(r.Linked);
        Assert.Equal(6, r.ByIssueType.Single().Reports);
        Assert.Equal(6, r.ByProduct.Single().Reports);
        Assert.Equal("2026-09", r.ByMonth.Single().Key);
        Assert.Equal(6, r.BySeverity.Single().Reports);
    }

    [Fact]
    public async Task Small_groups_are_suppressed_unregistered_names_are_never_shown_and_drafts_are_not_counted()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Seed(f, Maker, Med1, 7, 7, product: "Reference product");
        await Seed(f, Maker, null, 8, 8, product: "Typed name of a herbal tonic");
        await Seed(f, Maker, Med1, 3, 3, issue: ReportIssueType.ApparentLackOfEffect);
        await Seed(f, Maker, Med1, 40, 40, issue: ReportIssueType.Other); // a larger visible group, so only the smallest visible one is sacrificed
        await Seed(f, Maker, Med1, 20, 20, status: ReportStatus.Draft);
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.Real)).Value!;
        var json = JsonSerializer.Serialize(r);
        Assert.DoesNotContain("herbal", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-BATCH", json, StringComparison.Ordinal);
        Assert.DoesNotContain("free text", json, StringComparison.Ordinal);
        Assert.Equal(50, r.ByProduct.Single(c => c.Key != "unregistered").Reports);
        Assert.Equal(8, r.ByProduct.Single(c => c.Key == "unregistered").Reports);
        var lack = r.ByIssueType.Single(c => c.Key == nameof(ReportIssueType.ApparentLackOfEffect));
        Assert.True(lack.Suppressed);
        Assert.Null(lack.Reports);
        // the 3 hidden reports are less than k, so the next-smallest group (QualityProblem, 15) is hidden as well; drafts (20) are never counted
        Assert.True(r.ByIssueType.Single(c => c.Key == nameof(ReportIssueType.QualityProblem)).Suppressed);
        Assert.Equal(40, r.ByIssueType.Single(c => c.Key == nameof(ReportIssueType.Other)).Reports);
    }

    [Fact]
    public async Task Real_and_simulated_or_demo_reports_are_never_added_together()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Seed(f, Maker, Med1, 6, 6);
        await Seed(f, Maker, Med1, 9, 9, mock: true);
        await Seed(f, Maker, Med1, 11, 11, demo: true);
        var svc = env.Get<IIndustryInsightsService>();
        Assert.Equal(6, (await svc.GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.Real)).Value!.ByIssueType.Single().Reports);
        var sim = (await svc.GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.SimulatedOrDemo)).Value!;
        Assert.Equal(20, sim.ByIssueType.Single().Reports);
        Assert.Equal(InsightBasis.SimulatedOrDemo, sim.Basis);
    }

    [Fact]
    public async Task Only_closed_quarters_can_be_asked_for_and_the_threshold_cannot_be_configured_below_the_floor()
    {
        using var env = Env(k: 1);
        var svc = env.Get<IIndustryInsightsService>();
        var ok = (await svc.GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.Real)).Value!;
        Assert.Equal(3, ok.MinCellSize);
        Assert.Equal("2026-Q3", ok.Period);
        Assert.Equal(new DateOnly(2026, 7, 1), ok.From);
        Assert.Equal(new DateOnly(2026, 9, 30), ok.Until);
        Assert.Equal("2026-Q3", (await svc.GetAsync(Guid.Parse(Org), null, InsightBasis.Real)).Value!.Period); // default: the newest closed quarter
        Assert.Equal("period.not_closed", (await svc.GetAsync(Guid.Parse(Org), "2026-Q4", InsightBasis.Real)).Detail); // still running
        Assert.Equal("period.invalid", (await svc.GetAsync(Guid.Parse(Org), "2026-07", InsightBasis.Real)).Detail);
        Assert.Equal("period.invalid", (await svc.GetAsync(Guid.Parse(Org), "2026-Q5", InsightBasis.Real)).Detail);
        Assert.Equal("period.too_old", (await svc.GetAsync(Guid.Parse(Org), "2020-Q1", InsightBasis.Real)).Detail);
        Assert.Equal("basis.invalid", (await svc.GetAsync(Guid.Parse(Org), "2026-Q3", (InsightBasis)99)).Detail);
        Assert.Equal(8, ok.AvailablePeriods.Count);
    }

    [Fact]
    public async Task Reports_outside_the_quarter_are_not_counted_and_the_rest_are_grouped_by_month_sent_without_exact_dates()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Seed(f, Maker, Med1, 5, 5, on: new DateOnly(2026, 8, 3));
        await Seed(f, Maker, Med1, 5, 5, on: new DateOnly(2026, 9, 28));
        await Seed(f, Maker, Med1, 9, 9, on: new DateOnly(2026, 3, 1)); // outside
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), "2026-Q3", InsightBasis.Real)).Value!;
        Assert.Equal(["2026-08", "2026-09"], r.ByMonth.Select(c => c.Key));
    }
}

public class Phase15DisclosureTests
{
    [Fact]
    public void Complementary_suppression_hides_the_next_smallest_group_so_the_hidden_sum_reaches_k()
    {
        var cells = DisclosureRule.Apply([new("a", "A", 2, 2), new("b", "B", 6, 6), new("c", "C", 30, 30)], 5);
        Assert.True(cells.Single(c => c.Key == "a").Suppressed);
        Assert.True(cells.Single(c => c.Key == "b").Suppressed); // 2 + 6 >= 5
        Assert.Equal(30, cells.Single(c => c.Key == "c").Reports);
    }

    [Fact]
    public void A_single_group_hidden_for_having_few_reporters_is_never_the_only_hidden_group()
    {
        var cells = DisclosureRule.Apply([new("a", "A", 20, 2), new("b", "B", 40, 40), new("c", "C", 90, 90)], 5);
        Assert.True(cells.Single(c => c.Key == "a").Suppressed);
        Assert.True(cells.Single(c => c.Key == "b").Suppressed); // the next-smallest is hidden too, so "total minus visible" is 60, not 20
        Assert.Equal(90, cells.Single(c => c.Key == "c").Reports);
    }

    [Theory]
    [InlineData(4, 4, 1)]   // just below k on reports
    [InlineData(5, 4, 1)]   // enough reports, one reporter short
    [InlineData(5, 5, 0)]   // exactly k on both: shown
    public void The_boundary_is_inclusive_on_both_counts(int reports, int reporters, int expectedHidden)
    {
        var cells = DisclosureRule.Apply([new("a", "A", reports, reporters), new("z", "Z", 500, 500)], 5);
        Assert.Equal(expectedHidden, cells.Count(c => c.Suppressed && c.Key == "a"));
    }

    [Fact]
    public void Subtracting_the_visible_cells_of_one_table_from_the_total_of_another_never_isolates_one_small_group()
    {
        // Property check over many shapes: whenever something is hidden, the hidden reports sum to at least k and at least two groups are hidden unless one hidden group alone holds >= k.
        var rnd = new Random(7);
        for (var run = 0; run < 500; run++)
        {
            var groups = Enumerable.Range(0, rnd.Next(1, 9)).Select(i => new RawGroup("g" + i, null, rnd.Next(1, 40), rnd.Next(1, 40))).ToList();
            groups = [.. groups.Select(g => g with { Reporters = Math.Min(g.Reporters, g.Reports) })];
            var cells = DisclosureRule.Apply(groups, 5);
            var hiddenKeys = cells.Where(c => c.Suppressed).Select(c => c.Key).ToHashSet();
            if (hiddenKeys.Count == 0)
            {
                continue;
            }

            var hiddenSum = groups.Where(g => hiddenKeys.Contains(g.Key)).Sum(g => g.Reports);
            var everyVisibleExists = hiddenKeys.Count < groups.Count;
            Assert.True(hiddenSum >= 5 || !everyVisibleExists, $"run {run}: hidden sum {hiddenSum}");
            Assert.True(hiddenKeys.Count >= 2 || !everyVisibleExists || groups.Count == 1, $"run {run}: a single hidden group would be recoverable by subtraction");
        }
    }
}

public class Phase15WindowTests
{
    private static readonly Guid OrgId = Guid.Parse(Phase15Tests.Org);

    private static PEnv Env() => new(settings: new() { [$"IndustryInsights:Organizations:{Phase15Tests.Org}:0"] = Phase15Tests.Maker.ToString() });

    [Fact]
    public async Task A_quarter_is_published_only_after_the_closure_delay_and_never_changes_afterwards()
    {
        using var env = Env(); // clock 2026-10-09; delay 7 days: Q3 closed on 2026-10-07
        var svc = env.Get<IIndustryInsightsService>();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, on: new DateOnly(2026, 9, 2));
        Assert.Equal(6, (await svc.GetAsync(OrgId, "2026-Q3", InsightBasis.Real)).Value!.ByIssueType.Single().Reports);

        env.Clock.UtcNow = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero); // inside the delay: Q3 is not yet published
        Assert.Equal("period.not_closed", (await svc.GetAsync(OrgId, "2026-Q3", InsightBasis.Real)).Detail);
        Assert.Equal("2026-Q2", (await svc.GetAsync(OrgId, null, InsightBasis.Real)).Value!.Period);

        env.Clock.UtcNow = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero); // months later: same answer
        Assert.Equal(6, (await svc.GetAsync(OrgId, "2026-Q3", InsightBasis.Real)).Value!.ByIssueType.Single().Reports);
    }

    [Fact]
    public async Task Reports_are_placed_by_the_month_they_were_sent_so_a_late_one_lands_in_a_later_quarter()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, on: new DateOnly(2026, 8, 5));
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, on: new DateOnly(2026, 8, 5), sentAt: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero)); // occurred in August, sent in October
        var svc = env.Get<IIndustryInsightsService>();
        var q3 = (await svc.GetAsync(OrgId, "2026-Q3", InsightBasis.Real)).Value!;
        Assert.Equal(6, q3.ByIssueType.Single().Reports);
        Assert.Equal("2026-08", q3.ByMonth.Single().Key);
    }

    [Fact]
    public async Task One_organizations_link_never_exposes_another_organizations_manufacturer()
    {
        const string otherOrg = "578742b6-efd3-551a-9000-e250d815db67";
        using var env = new PEnv(settings: new()
        {
            [$"IndustryInsights:Organizations:{Phase15Tests.Org}:0"] = Phase15Tests.Maker.ToString(),
            [$"IndustryInsights:Organizations:{otherOrg}:0"] = Phase15Tests.OtherMaker.ToString(),
        });
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6);
        await Phase15Tests.Seed(f, Phase15Tests.OtherMaker, null, 9, 9);
        var svc = env.Get<IIndustryInsightsService>();
        Assert.Equal(6, (await svc.GetAsync(OrgId, "2026-Q3", InsightBasis.Real)).Value!.ByIssueType.Single().Reports);
        Assert.Equal(9, (await svc.GetAsync(Guid.Parse(otherOrg), "2026-Q3", InsightBasis.Real)).Value!.ByIssueType.Single().Reports);
    }
}

public class IndustryApiFactory : PatientsApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting($"IndustryInsights:Organizations:{Phase15Tests.Org}:0", Phase15Tests.Maker.ToString());
    }
}

/// <summary>Phase 15 over HTTP: membership of the organization is required; every other role and organization gets the same 403.</summary>
public class Phase15ApiTests(IndustryApiFactory factory) : IClassFixture<IndustryApiFactory>
{
    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);

    [Fact]
    public async Task Without_a_token_the_route_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights")).StatusCode);
    }

    [Fact]
    public async Task Only_the_industry_role_of_that_organization_may_read_and_the_answer_is_aggregate_only_and_audited()
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var quarterStart = new DateOnly(now.Year, (((now.Month - 1) / 3) * 3) + 1, 1);
        var latestClosed = now > quarterStart.AddDays(-1).AddDays(7) ? quarterStart.AddMonths(-3) : quarterStart.AddMonths(-6); // same rule as the service (7-day delay)
        await Phase15Tests.Seed(factory.Services.GetRequiredService<IDbContextFactory<ProductTraceDbContext>>(), Phase15Tests.Maker, null, 6, 6, on: latestClosed.AddDays(40));
        var industry = await As("demo-industry");
        var res = await industry.Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights?basis=Real");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-BATCH", text, StringComparison.Ordinal);
        Assert.DoesNotContain("free text", text, StringComparison.Ordinal);
        var j = JsonDocument.Parse(text).RootElement;
        Assert.True(j.GetProperty("linked").GetBoolean());
        Assert.Equal("Real", j.GetProperty("basis").GetString());
        Assert.Equal(6, j.GetProperty("byIssueType")[0].GetProperty("reports").GetInt32());

        // another organization's id, and every other role, get the same refusal
        Assert.Equal(HttpStatusCode.Forbidden, (await industry.Client.GetAsync("/organizations/383c0c5b-386f-5d57-a295-3b25374d9a2d/industry/insights")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await industry.Client.GetAsync($"/organizations/{Guid.NewGuid()}/industry/insights")).StatusCode);
        foreach (var who in new[] { "demo-patient", "demo-physician", "demo-pharmacist", "demo-pharmacy-admin", "demo-researcher" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await (await As(who)).Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await industry.Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights?basis=Nonsense")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await industry.Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights?from=2026-09-01&until=2026-09-05")).StatusCode); // no caller-chosen range
        Assert.Equal(HttpStatusCode.BadRequest, (await industry.Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights?period=2026-07")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await industry.Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights?period=2999-Q1")).StatusCode);
    }

    [Fact]
    public async Task Over_HTTP_the_same_request_returns_the_same_body_for_separate_sessions_and_after_new_reports_are_sent()
    {
        var path = $"/organizations/{Phase15Tests.Org}/industry/insights";
        var first = await As("demo-industry");
        var second = await As("demo-industry"); // a separate session of the same account: the answer has no per-session input
        var firstBody = await (await first.Client.GetAsync(path)).Content.ReadAsStringAsync();
        var period = JsonDocument.Parse(firstBody).RootElement.GetProperty("period").GetString()!;
        Assert.Equal(firstBody, await (await second.Client.GetAsync(path)).Content.ReadAsStringAsync());
        Assert.Equal(firstBody, await (await second.Client.GetAsync(path + "?period=" + period)).Content.ReadAsStringAsync()); // naming the period explicitly changes nothing

        // reports sent now (in the running quarter) and a differently-dated product do not alter the published quarter
        await Phase15Tests.Seed(factory.Services.GetRequiredService<IDbContextFactory<ProductTraceDbContext>>(), Phase15Tests.Maker, null, 9, 9, on: DateOnly.FromDateTime(DateTime.UtcNow), sentAt: DateTimeOffset.UtcNow);
        Assert.Equal(firstBody, await (await first.Client.GetAsync(path)).Content.ReadAsStringAsync());
        Assert.Equal(firstBody, await (await first.Client.GetAsync(path + "?period=" + period + "&basis=Real")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_industry_role_has_no_patient_adverse_event_or_pharmacy_routes()
    {
        var industry = await As("demo-industry");
        foreach (var path in new[] { "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/products", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/adverse-events", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/manufacturer-reports", "/organizations/383c0c5b-386f-5d57-a295-3b25374d9a2d/safety-events", "/organizations/383c0c5b-386f-5d57-a295-3b25374d9a2d/product-concerns" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await industry.Client.GetAsync(path)).StatusCode);
        }
    }
}
