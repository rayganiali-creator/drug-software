using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Medications.Domain;

namespace MedSmarter.Modules.Medications;

public static class SearchLimits
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 64;
    public const int MaxTokens = 8;
    public const int MaxLimit = 50;
    public const int MaxOffset = 1000;
    /// <summary>Upper bound of candidate rows examined per query (keeps one request from scanning everything).</summary>
    public const int MaxCandidates = 2000;
}

public sealed record ValidatedQuery(string Normalized, int Limit, int Offset, bool IncludeInactive);

public static class SearchQueryValidator
{
    /// <summary>Returns the normalized query, or a machine-readable error code. An empty query means "browse".</summary>
    public static (ValidatedQuery? Query, string? Error) Validate(MedicationSearchQuery q)
    {
        if (q.Limit is < 1 or > SearchLimits.MaxLimit)
        {
            return (null, "limit.range");
        }

        if (q.Offset is < 0 or > SearchLimits.MaxOffset)
        {
            return (null, "offset.range");
        }

        var raw = q.Q ?? string.Empty;
        if (raw.Length > SearchLimits.MaxQueryLength * 2)
        {
            return (null, "q.too_long");
        }

        if (raw.Any(char.IsControl))
        {
            return (null, "q.invalid_characters");
        }

        var normalized = TextNormalizer.Normalize(raw);
        if (raw.Trim().Length > 0 && normalized.Length == 0)
        {
            return (null, "q.invalid");
        }

        if (normalized.Length > SearchLimits.MaxQueryLength)
        {
            return (null, "q.too_long");
        }

        if (normalized.Length is > 0 and < SearchLimits.MinQueryLength)
        {
            return (null, "q.too_short");
        }

        if (normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > SearchLimits.MaxTokens)
        {
            return (null, "q.too_many_terms");
        }

        return (new ValidatedQuery(normalized, q.Limit, q.Offset, q.IncludeInactive), null);
    }
}

public sealed record RankedHit(Guid MedicationId, int Score, string MatchedOn);

/// <summary>Relevance ranking shared by every repository implementation: exact &gt; prefix &gt; word-prefix &gt; substring.</summary>
public static class SearchRanker
{
    public static IReadOnlyList<RankedHit> Rank(IEnumerable<MedicationSearchTerm> terms, string normalizedQuery)
    {
        var best = new Dictionary<Guid, RankedHit>();
        foreach (var t in terms)
        {
            var score = ScoreTerm(t, normalizedQuery);
            if (score <= 0)
            {
                continue;
            }

            if (!best.TryGetValue(t.MedicationId, out var current) || score > current.Score)
            {
                best[t.MedicationId] = new RankedHit(t.MedicationId, score, t.Display);
            }
        }

        return [.. best.Values];
    }

    private static int ScoreTerm(MedicationSearchTerm t, string q)
    {
        var n = t.Normalized;
        int basis;
        if (n == q)
        {
            basis = 1000;
        }
        else if (n.StartsWith(q, StringComparison.Ordinal))
        {
            basis = 800;
        }
        else if (n.Contains(' ' + q, StringComparison.Ordinal))
        {
            basis = 600;
        }
        else if (n.Contains(q, StringComparison.Ordinal))
        {
            basis = 300;
        }
        else
        {
            return 0;
        }

        // Codes (GTIN, ATC, ...) only count as an exact match: partial digit strings are noise.
        if (t.Kind == NameKind.Code && basis != 1000)
        {
            return 0;
        }

        var bonus = t.Kind switch
        {
            NameKind.Generic => 60,
            NameKind.Brand => 60,
            NameKind.Ingredient => 40,
            NameKind.Code => 60,
            _ => 0,
        };
        return basis + bonus;
    }
}
