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
        bool mock = false, bool demo = false, ReportStatus status = ReportStatus.Sent, DateOnly? on = null, string product = "Reference product")
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
                OccurredOn = on ?? new DateOnly(2026, 9, 10), Description = "free text that must never be returned", IsMockDelivery = mock, IsDemo = demo, CreatedBy = pid,
            });
        }

        await db.SaveChangesAsync();
    }

    private static PEnv Env(int? k = null) => new(settings: new()
    {
        [$"IndustryInsights:Organizations:{Org}:0"] = Maker.ToString(),
        ["IndustryInsights:MinCellSize"] = (k ?? 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    private static readonly DateOnly From = new(2026, 7, 1);
    private static readonly DateOnly Until = new(2026, 9, 30);

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
        var none = (await unlinked.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), From, Until, InsightBasis.Real)).Value!;
        Assert.False(none.Linked);
        Assert.Empty(none.ByIssueType);

        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Seed(f, Maker, Med1, 6, 6);
        await Seed(f, OtherMaker, Med1, 30, 30); // another manufacturer: never visible
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), From, Until, InsightBasis.Real)).Value!;
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
        await Seed(f, Maker, Med1, 20, 20, status: ReportStatus.Draft);
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), From, Until, InsightBasis.Real)).Value!;
        var json = JsonSerializer.Serialize(r);
        Assert.DoesNotContain("herbal", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-BATCH", json, StringComparison.Ordinal);
        Assert.DoesNotContain("free text", json, StringComparison.Ordinal);
        Assert.Equal(10, r.ByProduct.Single(c => c.Key != "unregistered").Reports);
        Assert.Equal(8, r.ByProduct.Single(c => c.Key == "unregistered").Reports);
        var lack = r.ByIssueType.Single(c => c.Key == nameof(ReportIssueType.ApparentLackOfEffect));
        Assert.True(lack.Suppressed);
        Assert.Null(lack.Reports);
        Assert.Equal(15, r.ByIssueType.Single(c => c.Key == nameof(ReportIssueType.QualityProblem)).Reports); // drafts (20) are not counted
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
        Assert.Equal(6, (await svc.GetAsync(Guid.Parse(Org), From, Until, InsightBasis.Real)).Value!.ByIssueType.Single().Reports);
        var sim = (await svc.GetAsync(Guid.Parse(Org), From, Until, InsightBasis.SimulatedOrDemo)).Value!;
        Assert.Equal(20, sim.ByIssueType.Single().Reports);
        Assert.Equal(InsightBasis.SimulatedOrDemo, sim.Basis);
    }

    [Fact]
    public async Task The_window_is_bounded_and_the_threshold_cannot_be_configured_below_the_floor()
    {
        using var env = Env(k: 1);
        var svc = env.Get<IIndustryInsightsService>();
        Assert.Equal(3, (await svc.GetAsync(Guid.Parse(Org), From, Until, InsightBasis.Real)).Value!.MinCellSize);
        Assert.Equal("window.too_short", (await svc.GetAsync(Guid.Parse(Org), Until.AddDays(-6), Until, InsightBasis.Real)).Detail);
        Assert.Equal("window.too_long", (await svc.GetAsync(Guid.Parse(Org), Until.AddDays(-400), Until, InsightBasis.Real)).Detail);
        Assert.Equal("basis.invalid", (await svc.GetAsync(Guid.Parse(Org), From, Until, (InsightBasis)99)).Detail);
    }

    [Fact]
    public async Task Reports_outside_the_window_and_in_other_months_are_grouped_by_month_without_exact_dates()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Seed(f, Maker, Med1, 5, 5, on: new DateOnly(2026, 8, 3));
        await Seed(f, Maker, Med1, 5, 5, on: new DateOnly(2026, 9, 28));
        await Seed(f, Maker, Med1, 9, 9, on: new DateOnly(2026, 3, 1)); // outside
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Guid.Parse(Org), From, Until, InsightBasis.Real)).Value!;
        Assert.Equal(["2026-08", "2026-09"], r.ByMonth.Select(c => c.Key));
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
        await Phase15Tests.Seed(factory.Services.GetRequiredService<IDbContextFactory<ProductTraceDbContext>>(), Phase15Tests.Maker, null, 6, 6, on: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
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
        Assert.Equal(HttpStatusCode.BadRequest, (await industry.Client.GetAsync($"/organizations/{Phase15Tests.Org}/industry/insights?from=2026-09-01&until=2026-09-05")).StatusCode);
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
