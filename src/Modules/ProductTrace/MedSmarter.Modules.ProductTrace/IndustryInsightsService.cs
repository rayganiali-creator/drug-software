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

    /// <summary>A calendar quarter is published only this many days after its last day. Reports are placed in the month they were SENT, which cannot be back-dated, so a published quarter never changes afterwards.</summary>
    public int ClosureDelayDays { get; set; } = 7;

    /// <summary>How many closed quarters can be asked for.</summary>
    public int MaxPeriods { get; set; } = 8;
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
    /// Complementary suppression: if anything is hidden, at least two groups must be hidden and together hold at least <paramref name="k"/> reports, otherwise the smallest visible groups are hidden too.
    /// Without that, "population total (from another table) minus the visible groups" would give the exact count of a single hidden group (small, or hidden for having few reporters). Output order is by label/key, never by size.
    /// This reduces differencing within and across the tables of one response; it does not make the output anonymous (see docs/post-phase-15-stabilization).
    /// </summary>
    public static IReadOnlyList<InsightCell> Apply(IEnumerable<RawGroup> groups, int k)
    {
        var list = groups.OrderBy(g => g.Label ?? g.Key, StringComparer.Ordinal).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var hidden = list.Select(g => g.Reports < k || g.Reporters < k).ToList();
        var hiddenSum = list.Where((_, i) => hidden[i]).Sum(g => g.Reports);
        var hiddenCount = hidden.Count(h => h);
        if (hiddenCount > 0 && (hiddenSum < k || hiddenCount < 2))
        {
            foreach (var i in Enumerable.Range(0, list.Count).Where(i => !hidden[i]).OrderBy(i => list[i].Reports).ThenBy(i => list[i].Key, StringComparer.Ordinal))
            {
                hidden[i] = true;
                hiddenSum += list[i].Reports;
                hiddenCount++;
                if (hiddenSum >= k && hiddenCount >= 2)
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
    private static readonly System.Text.RegularExpressions.Regex PeriodPattern = new(@"^(\d{4})-Q([1-4])$", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);

    private static (DateOnly Start, DateOnly End) QuarterBounds(int year, int quarter)
    {
        var start = new DateOnly(year, ((quarter - 1) * 3) + 1, 1);
        return (start, start.AddMonths(3).AddDays(-1));
    }

    private bool IsClosed(DateOnly end, DateOnly today) => today > end.AddDays(options.Value.ClosureDelayDays);

    /// <summary>The newest closed quarters, newest first.</summary>
    private List<string> ClosedPeriods(DateOnly today)
    {
        var list = new List<string>();
        var year = today.Year;
        var quarter = ((today.Month - 1) / 3) + 1;
        for (var i = 0; i < 40 && list.Count < Math.Max(1, options.Value.MaxPeriods); i++)
        {
            if (IsClosed(QuarterBounds(year, quarter).End, today))
            {
                list.Add($"{year:0000}-Q{quarter}");
            }

            if (--quarter == 0)
            {
                quarter = 4;
                year--;
            }
        }

        return list;
    }

    public async Task<TraceOutcome<IndustryInsightsDto>> GetAsync(Guid organizationId, string? period, InsightBasis basis, CancellationToken ct = default)
    {
        var o = options.Value;
        var k = Math.Max(o.MinCellSize, IndustryInsightsOptions.FloorMinCellSize);
        if (!Enum.IsDefined(basis))
        {
            return Support.Invalid<IndustryInsightsDto>(["basis.invalid"]);
        }

        // Fixed, non-overlapping reporting periods (calendar quarters), published only after they are closed. No caller-chosen range exists, so two answers can never be subtracted
        // to isolate a month or a day, and the same request always gives the same answer.
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var available = ClosedPeriods(today);
        var chosen = period ?? available.FirstOrDefault();
        if (chosen is null)
        {
            return Support.Invalid<IndustryInsightsDto>(["period.not_closed"]);
        }

        var match = PeriodPattern.Match(chosen);
        if (!match.Success)
        {
            return Support.Invalid<IndustryInsightsDto>(["period.invalid"]);
        }

        if (!available.Contains(chosen, StringComparer.Ordinal))
        {
            return Support.Invalid<IndustryInsightsDto>([IsClosed(QuarterBounds(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)).End, today) ? "period.too_old" : "period.not_closed"]);
        }

        var (start, end) = QuarterBounds(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        var linked = LinkedManufacturers(o, organizationId);
        if (linked.Count == 0)
        {
            return TraceOutcome.Ok(new IndustryInsightsDto(organizationId, start, end, basis, false, k, false, [], [], [], [], chosen, available));
        }

        var startUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await using var db = await factory.CreateDbContextAsync(ct);
        var real = basis == InsightBasis.Real;
        var rows =
            from r in db.Reports.AsNoTracking()
            join p in db.Products.AsNoTracking() on r.ProductRecordId equals p.Id
            where linked.Contains(p.ManufacturerId)
                && (r.Status == ReportStatus.Sent || r.Status == ReportStatus.Acknowledged)
                && r.SentAt >= startUtc && r.SentAt < endUtc
                && (real ? !r.IsMockDelivery && !r.IsDemo && !p.IsDemo : r.IsMockDelivery || r.IsDemo || p.IsDemo)
            select new { r.PatientId, r.IssueType, r.Severity, r.SentAt, p.MedicationId, p.ProductName };

        var byProduct = await rows.Where(x => x.MedicationId != null).GroupBy(x => x.MedicationId)
            .Select(g => new { Id = g.Key, Name = g.Max(x => x.ProductName), Reports = g.Count(), Reporters = g.Select(x => x.PatientId).Distinct().Count() })
            .OrderByDescending(g => g.Reports).ThenBy(g => g.Id).Take(o.MaxProducts + 1).ToListAsync(ct); // ties broken by id: the same request must always list the same products
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

        // the three months of the quarter (month = month the report was sent), one range query each: no provider-specific date functions
        var months = new List<RawGroup>();
        for (var m = 0; m < 3; m++)
        {
            var from = new DateTimeOffset(start.AddMonths(m).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var to = new DateTimeOffset(start.AddMonths(m + 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var inMonth = rows.Where(x => x.SentAt >= from && x.SentAt < to);
            var n = await inMonth.CountAsync(ct);
            if (n > 0)
            {
                months.Add(new RawGroup($"{from.Year:0000}-{from.Month:00}", null, n, await inMonth.Select(x => x.PatientId).Distinct().CountAsync(ct)));
            }
        }

        return TraceOutcome.Ok(new IndustryInsightsDto(
            organizationId, start, end, basis, true, k, truncated,
            DisclosureRule.Apply(productGroups, k),
            DisclosureRule.Apply(byIssue.Select(g => new RawGroup(g.Key.ToString(), null, g.Reports, g.Reporters)), k),
            DisclosureRule.Apply(bySeverity.Select(g => new RawGroup(g.Key.ToString(), null, g.Reports, g.Reporters)), k),
            DisclosureRule.Apply(months, k),
            chosen, available));
    }

    private static List<Guid?> LinkedManufacturers(IndustryInsightsOptions o, Guid organizationId) =>
        o.Organizations.TryGetValue(organizationId.ToString(), out var ids)
            ? [.. ids.Select(s => Guid.TryParse(s, out var g) ? (Guid?)g : null).Where(g => g is not null)]
            : [];
}
