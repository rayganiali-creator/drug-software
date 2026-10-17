using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.ProductTrace;

public sealed class IndustryInsightsOptions
{
    public const string Section = "IndustryInsights";

    /// <summary>A cell is shown only when it has at least this many reports AND at least this many different reporters. Values below <see cref="FloorMinCellSize"/> are raised to it.</summary>
    public int MinCellSize { get; set; } = 5;

    public const int FloorMinCellSize = 3;
    public int MinWindowDays { get; set; } = 28;
    public int MaxWindowDays { get; set; } = 366;
    public int MaxProducts { get; set; } = 25;

    /// <summary>Organization id → manufacturer ids (from the medication reference) the organization may see. An organization without an entry sees nothing.</summary>
    public Dictionary<string, string[]> Organizations { get; set; } = [];
}

/// <summary>A group as counted in the database, before the disclosure rule is applied.</summary>
public sealed record RawGroup(string Key, string? Label, int Reports, int Reporters);

public static class DisclosureRule
{
    /// <summary>
    /// Primary suppression: a group with fewer than <paramref name="k"/> reports or fewer than <paramref name="k"/> distinct reporters has no number.
    /// Complementary suppression: if anything is hidden, the hidden groups must together hold at least <paramref name="k"/> reports, otherwise the smallest visible groups are hidden too.
    /// Without that, "population total (from another table) minus the visible groups" would isolate a single hidden small group. Output order is by label/key, never by size.
    /// This reduces differencing within and across the tables of one response; it does not make the output anonymous (see docs/post-phase-15-stabilization).
    /// </summary>
    public static IReadOnlyList<InsightCell> Apply(IEnumerable<RawGroup> groups, int k)
    {
        var list = groups.OrderBy(g => g.Label ?? g.Key, StringComparer.Ordinal).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var hidden = list.Select(g => g.Reports < k || g.Reporters < k).ToList();
        var hiddenSum = list.Where((_, i) => hidden[i]).Sum(g => g.Reports);
        if (hidden.Any(h => h) && hiddenSum < k)
        {
            foreach (var i in Enumerable.Range(0, list.Count).Where(i => !hidden[i]).OrderBy(i => list[i].Reports).ThenBy(i => list[i].Key, StringComparer.Ordinal))
            {
                hidden[i] = true;
                hiddenSum += list[i].Reports;
                if (hiddenSum >= k)
                {
                    break;
                }
            }
        }

        return [.. list.Select((g, i) => hidden[i] ? new InsightCell(g.Key, g.Label, null, true) : new InsightCell(g.Key, g.Label, g.Reports, false))];
    }
}

/// <summary>
/// Server-side aggregate of manufacturer reports for an industry organization. Scope is the organization's configured manufacturer links (fail-closed: no link, no data).
/// Four independent one-attribute tables, bounded window, bounded product count; the database does the counting and no row-level data leaves this class.
/// </summary>
public sealed class IndustryInsightsService(IDbContextFactory<ProductTraceDbContext> factory, IOptions<IndustryInsightsOptions> options, IClock clock) : IIndustryInsightsService
{
    public async Task<TraceOutcome<IndustryInsightsDto>> GetAsync(Guid organizationId, DateOnly windowStart, DateOnly windowEnd, InsightBasis basis, CancellationToken ct = default)
    {
        var o = options.Value;
        var k = Math.Max(o.MinCellSize, IndustryInsightsOptions.FloorMinCellSize);
        var days = windowEnd.DayNumber - windowStart.DayNumber + 1;
        if (!Enum.IsDefined(basis))
        {
            return Support.Invalid<IndustryInsightsDto>(["basis.invalid"]);
        }

        if (days < o.MinWindowDays)
        {
            return Support.Invalid<IndustryInsightsDto>(["window.too_short"]); // a very short window would single out individual reports
        }

        if (days > o.MaxWindowDays + 31)
        {
            return Support.Invalid<IndustryInsightsDto>(["window.too_long"]);
        }

        // Whole, closed calendar months only: a window that moves by a day (or a month that is still filling up) would let two queries be subtracted to isolate a day's reports.
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var lastClosed = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        windowStart = new DateOnly(windowStart.Year, windowStart.Month, 1);
        windowEnd = new DateOnly(windowEnd.Year, windowEnd.Month, 1).AddMonths(1).AddDays(-1);
        if (windowEnd > lastClosed)
        {
            windowEnd = lastClosed;
        }

        if (windowStart > windowEnd)
        {
            return Support.Invalid<IndustryInsightsDto>(["window.no_closed_month"]);
        }

        if ((windowEnd.Year - windowStart.Year) * 12 + windowEnd.Month - windowStart.Month + 1 > 12)
        {
            return Support.Invalid<IndustryInsightsDto>(["window.too_long"]);
        }

        var linked = LinkedManufacturers(o, organizationId);
        if (linked.Count == 0)
        {
            return TraceOutcome.Ok(new IndustryInsightsDto(organizationId, windowStart, windowEnd, basis, false, k, false, [], [], [], []));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var real = basis == InsightBasis.Real;
        var rows =
            from r in db.Reports.AsNoTracking()
            join p in db.Products.AsNoTracking() on r.ProductRecordId equals p.Id
            where linked.Contains(p.ManufacturerId)
                && (r.Status == ReportStatus.Sent || r.Status == ReportStatus.Acknowledged)
                && r.OccurredOn >= windowStart && r.OccurredOn <= windowEnd
                && (real ? !r.IsMockDelivery && !r.IsDemo && !p.IsDemo : r.IsMockDelivery || r.IsDemo || p.IsDemo)
            select new { r.PatientId, r.IssueType, r.Severity, r.OccurredOn, p.MedicationId, p.ProductName };

        var byProduct = await rows.Where(x => x.MedicationId != null).GroupBy(x => x.MedicationId)
            .Select(g => new { Id = g.Key, Name = g.Max(x => x.ProductName), Reports = g.Count(), Reporters = g.Select(x => x.PatientId).Distinct().Count() })
            .OrderByDescending(g => g.Reports).Take(o.MaxProducts + 1).ToListAsync(ct);
        var truncated = byProduct.Count > o.MaxProducts;
        var unregistered = await rows.Where(x => x.MedicationId == null).GroupBy(x => 1)
            .Select(g => new { Reports = g.Count(), Reporters = g.Select(x => x.PatientId).Distinct().Count() }).FirstOrDefaultAsync(ct);
        var productGroups = byProduct.Take(o.MaxProducts).Select(g => new RawGroup(g.Id!.Value.ToString(), g.Name, g.Reports, g.Reporters)).ToList();
        if (unregistered is not null)
        {
            productGroups.Add(new RawGroup("unregistered", null, unregistered.Reports, unregistered.Reporters)); // typed names are never shown
        }

        var byIssue = await rows.GroupBy(x => x.IssueType).Select(g => new { g.Key, Reports = g.Count(), Reporters = g.Select(x => x.PatientId).Distinct().Count() }).ToListAsync(ct);
        var bySeverity = await rows.GroupBy(x => x.Severity).Select(g => new { g.Key, Reports = g.Count(), Reporters = g.Select(x => x.PatientId).Distinct().Count() }).ToListAsync(ct);
        var byMonth = await rows.GroupBy(x => x.OccurredOn.Year * 100 + x.OccurredOn.Month).Select(g => new { g.Key, Reports = g.Count(), Reporters = g.Select(x => x.PatientId).Distinct().Count() }).ToListAsync(ct);

        return TraceOutcome.Ok(new IndustryInsightsDto(
            organizationId, windowStart, windowEnd, basis, true, k, truncated,
            DisclosureRule.Apply(productGroups, k),
            DisclosureRule.Apply(byIssue.Select(g => new RawGroup(g.Key.ToString(), null, g.Reports, g.Reporters)), k),
            DisclosureRule.Apply(bySeverity.Select(g => new RawGroup(g.Key.ToString(), null, g.Reports, g.Reporters)), k),
            DisclosureRule.Apply(byMonth.Select(g => new RawGroup($"{g.Key / 100:0000}-{g.Key % 100:00}", null, g.Reports, g.Reporters)), k)));
    }

    private static List<Guid?> LinkedManufacturers(IndustryInsightsOptions o, Guid organizationId) =>
        o.Organizations.TryGetValue(organizationId.ToString(), out var ids)
            ? [.. ids.Select(s => Guid.TryParse(s, out var g) ? (Guid?)g : null).Where(g => g is not null)]
            : [];
}
