using MedSmarter.Modules.ProductTrace;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// Industry privacy review: concrete attacks by a legitimate industry user, on synthetic data. Each test asserts the DEFENDED outcome, so a failing test means the attack succeeded.
/// </summary>
public class IndustryAttackTests
{
    private static readonly Guid Org = Guid.Parse(Phase15Tests.Org);
    private static readonly Guid Med1 = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static PEnv Env() => new(settings: new() { [$"IndustryInsights:Organizations:{Phase15Tests.Org}:0"] = Phase15Tests.Maker.ToString() });

    private static int VisibleSum(IEnumerable<InsightCell> cells) => cells.Where(c => !c.Suppressed).Sum(c => c.Reports!.Value);

    // ---- attack 1: total from one table minus the visible cells of another recovers a cell that was hidden because it has few REPORTERS
    [Fact]
    public async Task Attack1_subtracting_visible_cells_from_a_total_must_not_reveal_the_exact_count_of_a_cell_hidden_for_having_few_reporters()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 20, 2, issue: ReportIssueType.QualityProblem, on: new DateOnly(2026, 8, 10)); // 20 reports from only 2 people
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 40, 40, issue: ReportIssueType.Other, on: new DateOnly(2026, 8, 12));
        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;

        var monthTotal = VisibleSum(r.ByMonth); // the month table is fully visible: 60
        var hiddenIssueCells = r.ByIssueType.Count(c => c.Suppressed);
        var derivedHiddenSum = monthTotal - VisibleSum(r.ByIssueType);
        // the attacker knows the exact hidden quantity only if ONE cell is hidden (then it is that cell's count)
        Assert.False(hiddenIssueCells == 1 && derivedHiddenSum == 20, $"attack succeeded: one hidden cell, derived exactly {derivedHiddenSum}");
    }

    // ---- attack 2: overlapping date ranges isolate a small month (now: there is no caller-chosen range, only fixed disjoint quarters)
    [Fact]
    public async Task Attack2_there_are_no_two_overlapping_answers_so_a_month_with_fewer_than_k_reports_cannot_be_isolated_by_subtraction()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 5, 5, on: new DateOnly(2026, 1, 10));
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 4, 4, on: new DateOnly(2026, 2, 10));
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 3, 3, on: new DateOnly(2026, 3, 10)); // March: 3 reports
        var svc = env.Get<IIndustryInsightsService>();

        var q1 = (await svc.GetAsync(Org, "2026-Q1", InsightBasis.Real)).Value!;
        Assert.True(q1.ByMonth.Single(c => c.Key == "2026-03").Suppressed);
        Assert.Equal(12, VisibleSum(q1.ByProduct)); // the quarter total is visible, but no "January+February" answer exists to subtract from it
        foreach (var odd in new[] { "2026-01", "2026-Q0", "2026-Q5", "2026-1Q", "2026-Q1/2026-Q2" })
        {
            Assert.False((await svc.GetAsync(Org, odd, InsightBasis.Real)).Succeeded, odd); // months, ranges and half-quarters are not valid requests
        }

        var all = (await svc.GetAsync(Org, null, InsightBasis.Real)).Value!.AvailablePeriods;
        var ranges = new List<(DateOnly From, DateOnly Until)>();
        foreach (var p in all)
        {
            var r = (await svc.GetAsync(Org, p, InsightBasis.Real)).Value!;
            ranges.Add((r.From, r.Until));
        }

        for (var i = 0; i < ranges.Count; i++)
        {
            for (var j = i + 1; j < ranges.Count; j++)
            {
                Assert.True(ranges[i].Until < ranges[j].From || ranges[j].Until < ranges[i].From, $"{all[i]} overlaps {all[j]}");
            }
        }
    }

    // ---- attack 3: the same closed period queried before and after a late-arriving report
    [Fact]
    public async Task Attack3_a_closed_period_must_return_the_same_numbers_after_a_late_report_arrives()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 4, 4, issue: ReportIssueType.QualityProblem, on: new DateOnly(2026, 8, 10)); // 4 < k: hidden
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 30, 30, issue: ReportIssueType.Other, on: new DateOnly(2026, 8, 11));
        var svc = env.Get<IIndustryInsightsService>();
        var before = (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;

        // one report about an August problem is sent only on 5 October (the period had already closed)
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 1, 1, issue: ReportIssueType.QualityProblem, on: new DateOnly(2026, 8, 20), sentAt: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var after = (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));
    }

    // ---- attack 4: two authorized users of one organization pool their queries
    [Fact]
    public async Task Attack4_the_answer_depends_only_on_organization_period_and_basis_so_a_second_account_learns_nothing_new()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await Phase15Tests.Seed(f, Phase15Tests.Maker, Med1, 7, 7, on: new DateOnly(2026, 8, 10));
        var svc = env.Get<IIndustryInsightsService>();
        var a = System.Text.Json.JsonSerializer.Serialize((await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value);
        var b = System.Text.Json.JsonSerializer.Serialize((await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value);
        Assert.Equal(a, b); // the service has no per-caller input
    }

    // ---- attack 5: a patient edit after the fact changes a published quarter (the delta of one visible cell reveals that one report moved)
    [Fact]
    public async Task Attack5_editing_a_product_after_its_report_was_sent_must_not_change_a_published_quarter()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        var id = await env.Patient("demo-patient");
        var product = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, new ProductRecordInput(null, null, "Typed product (fictional)", null, null, "Fictional Maker", "LOT-ATK-5", env.Today.AddMonths(-2), env.Today.AddMonths(10), null, null, env.Today.AddDays(-3)), "t", null)).Value!;
        await using (var db = await f.CreateDbContextAsync())
        {
            var row = await db.Products.SingleAsync(x => x.Id == product.Id);
            row.ManufacturerId = Phase15Tests.Maker; // the patient's package is attributed to the linked manufacturer
            db.Reports.Add(new ReportRow { Id = Guid.NewGuid(), Reference = Guid.NewGuid().ToString("N")[..20], PatientId = row.PatientId, ProductRecordId = row.Id, Status = ReportStatus.Sent, IssueType = ReportIssueType.QualityProblem, Severity = ReportSeverity.Mild, OccurredOn = new DateOnly(2026, 8, 3), SentAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), CreatedBy = row.PatientId });
            await db.SaveChangesAsync();
        }

        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, issue: ReportIssueType.QualityProblem, on: new DateOnly(2026, 8, 5));
        var svc = env.Get<IIndustryInsightsService>();
        var before = (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;
        Assert.Equal(7, before.ByIssueType.Single().Reports);

        // the patient edits the product (the batch is corrected); the manufacturer name stays a typed name, so the stored manufacturer id would be cleared
        await env.Products.UpdateAsync(id, ProductRecorder.Patient, id, product.Id, new ProductRecordInput(null, null, "Typed product (fictional)", null, null, "Fictional Maker", "LOT-ATK-5B", env.Today.AddMonths(-2), env.Today.AddMonths(10), null, null, env.Today.AddDays(-3), ExpectedVersion: product.Version), "typo", "t", null);

        var after = (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;
        Assert.Equal(7, after.ByIssueType.Single().Reports);
    }

    [Fact]
    public async Task A_product_with_a_sent_report_is_locked_for_manufacturer_and_medicine_changes_but_ordinary_corrections_still_work()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        ProductRecordInput Input(string? note, Guid? medication, string? name) => new(null, medication, name, null, null, null, "LOT-LOCK-1", env.Today.AddMonths(-2), env.Today.AddMonths(10), null, note, env.Today.AddDays(-3));
        var product = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(null, med, null), "t", null)).Value!;
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Reports.Add(new ReportRow { Id = Guid.NewGuid(), Reference = Guid.NewGuid().ToString("N")[..20], PatientId = (await db.Products.SingleAsync(x => x.Id == product.Id)).PatientId, ProductRecordId = product.Id, Status = ReportStatus.Sent, IssueType = ReportIssueType.Other, Severity = ReportSeverity.Mild, OccurredOn = new DateOnly(2026, 8, 3), SentAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), CreatedBy = id });
            await db.SaveChangesAsync();
        }

        var note = await env.Products.UpdateAsync(id, ProductRecorder.Patient, id, product.Id, Input("kept in the fridge", med, null) with { ExpectedVersion = product.Version }, "note", "t", null);
        Assert.True(note.Succeeded, note.Detail);
        var change = await env.Products.UpdateAsync(id, ProductRecorder.Patient, id, product.Id, Input(null, null, "Another typed product") with { ExpectedVersion = note.Value!.Version }, "wrong product", "t", null);
        Assert.Equal(TraceError.Conflict, change.Error);
        Assert.Equal("product.locked_after_report", change.Detail);
    }

    private static async Task<List<string>> ListedProducts(IReadOnlyList<Guid> insertionOrder)
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        foreach (var m in insertionOrder)
        {
            await Phase15Tests.Seed(f, Phase15Tests.Maker, m, 5, 5, on: new DateOnly(2026, 8, 10));
        }

        var r = (await env.Get<IIndustryInsightsService>().GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;
        Assert.True(r.ProductsTruncated);
        Assert.Equal(25, r.ByProduct.Count);
        return [.. r.ByProduct.Select(c => c.Key).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task When_more_products_than_the_limit_tie_on_count_the_listed_ones_do_not_depend_on_insertion_order()
    {
        var meds = Enumerable.Range(0, 27).Select(_ => Guid.NewGuid()).ToList(); // 27 products with an exact tie across the cut-off at 25
        var a = await ListedProducts(meds);
        var b = await ListedProducts([.. Enumerable.Reverse(meds)]);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task The_delay_boundary_is_exact_and_a_report_sent_during_the_delay_is_counted_once_in_the_quarter_it_belongs_to()
    {
        using var env = Env();
        var f = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        var svc = env.Get<IIndustryInsightsService>();
        env.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero); // inside the delay: Q3 is not published
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, issue: ReportIssueType.QualityProblem, on: new DateOnly(2026, 9, 30), sentAt: new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.Zero));
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, issue: ReportIssueType.Other, on: new DateOnly(2026, 10, 3), sentAt: new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero)); // belongs to Q4
        Assert.Equal("period.not_closed", (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Detail);

        env.Clock.UtcNow = new DateTimeOffset(2026, 10, 7, 23, 59, 59, TimeSpan.Zero); // last second of day 7: still not published
        Assert.Equal("period.not_closed", (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Detail);
        env.Clock.UtcNow = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero); // first moment of day 8: published
        var q3 = (await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value!;
        Assert.Equal(6, q3.ByIssueType.Single().Reports); // the only group in Q3, visible, and the October report is not in it
        Assert.DoesNotContain(q3.ByIssueType, c => c.Key == nameof(ReportIssueType.Other)); // the October report is not in Q3
        await Phase15Tests.Seed(f, Phase15Tests.Maker, null, 6, 6, issue: ReportIssueType.Other, on: new DateOnly(2026, 10, 9)); // after publication: lands in Q4
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(q3), System.Text.Json.JsonSerializer.Serialize((await svc.GetAsync(Org, "2026-Q3", InsightBasis.Real)).Value));
    }
}
