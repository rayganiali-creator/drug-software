using System.Globalization;
using System.Text;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.AI;

/// <summary>
/// Normalises Persian and English input the same way the medication search does (Arabic letter variants, Persian/Arabic-Indic digits, diacritics,
/// zero-width non-joiner and punctuation), so stop words, intent words and safety patterns match whatever keyboard was used.
/// (Duplicated on purpose: AI may reference only the Medications contracts, not its implementation.)
/// </summary>
public static class QueryNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = TextFolding.Fold(input).Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text)
        {
            var c = raw switch
            {
                'ي' or 'ى' or 'ې' or 'ێ' => 'ی',
                'ك' or 'ڪ' => 'ک',
                'ة' or 'ۀ' or 'ە' => 'ه',
                'أ' or 'إ' or 'ٱ' or 'آ' => 'ا',
                'ؤ' => 'و',
                _ => raw,
            };

            if (c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ' or 'ۖ' or 'ۭ' || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue; // harakat, tatweel, Quranic marks, invisible format characters
            }

            if (c is >= '۰' and <= '۹')
            {
                sb.Append((char)('0' + (c - '۰')));
            }
            else if (c is >= '٠' and <= '٩')
            {
                sb.Append((char)('0' + (c - '٠')));
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>What the person seems to ask about; used only to say which kinds of information are missing. Not a medical classification.</summary>
public static class QuestionTopics
{
    private static readonly (StatementKind? Kind, bool Interaction, string[] Words)[] Topics =
    [
        (null, true, ["interaction", "interact", "together", "combine", "mix", "تداخل", "همزمان", "باهم", "با هم"]),
        (StatementKind.AdverseReaction, false, ["side effect", "side effects", "adverse", "reaction", "عارضه", "عوارض"]),
        (StatementKind.Administration, false, ["how to take", "how do i take", "how should i take", "when to take", "dosage", "administration", "طرز مصرف", "نحوه مصرف", "چطور مصرف", "چگونه مصرف", "زمان مصرف"]),
        (StatementKind.Storage, false, ["store", "storage", "keep it", "نگهداری"]),
        (StatementKind.Contraindication, false, ["contraindication", "contraindicated", "who should not", "should not take", "منع مصرف", "موارد منع"]),
        (StatementKind.Indication, false, ["used for", "what is it for", "indication", "what does it treat", "کاربرد", "برای چه", "مصرف دارو برای"]),
        (StatementKind.Warning, false, ["warning", "warnings", "precaution", "careful", "هشدار", "احتیاط"]),
    ];

    private static readonly StatementKind[] General = [StatementKind.Indication, StatementKind.Contraindication, StatementKind.Warning, StatementKind.Administration, StatementKind.AdverseReaction];

    public sealed record Result(IReadOnlyList<StatementKind> Kinds, bool Interactions, bool Specific);

    public static Result Detect(string normalizedQuestion)
    {
        var padded = $" {normalizedQuestion} ";
        var kinds = new List<StatementKind>();
        var interactions = false;
        foreach (var (kind, interaction, words) in Topics)
        {
            if (!words.Any(w => padded.Contains($" {QueryNormalizer.Normalize(w)} ", StringComparison.Ordinal)))
            {
                continue;
            }

            interactions |= interaction;
            if (kind is { } k)
            {
                kinds.Add(k);
                if (k == StatementKind.Contraindication || k == StatementKind.Warning)
                {
                    kinds.AddRange(new[] { StatementKind.Contraindication, StatementKind.Warning, StatementKind.Precaution }.Where(x => !kinds.Contains(x)));
                }
            }
        }

        return kinds.Count == 0 && !interactions ? new Result(General, false, false) : new Result([.. kinds.Distinct()], interactions, true);
    }
}

/// <summary>
/// Retrieval through the authorised medication service (never the database directly): normalise the question, find medications, read their
/// source-bearing knowledge documents, and turn them into provenance-labelled evidence items. Evidence stays separate from any generated text.
/// Nothing is invented: a missing source, a missing kind of information or a disagreement is reported, not filled in.
/// </summary>
public sealed class MedicationEvidenceRetriever(IMedicationService medications, IClock clock, IOptions<AiOptions> options) : IEvidenceRetriever
{
    public const int MaxItems = 20;
    private const int SearchMax = 64;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "what", "which", "about", "tell", "can", "how", "does", "with", "this", "that", "from", "take", "give", "please", "info", "information", "used", "use",
        "side", "effects", "effect", "interaction", "interactions", "together", "storage", "store", "dose", "dosage", "when", "should", "who", "not", "tablet", "tablets", "medicine", "drug",
        "من", "را", "و", "به", "در", "از", "که", "این", "آن", "برای", "با", "چیست", "چه", "است", "دارو", "درباره", "بگو", "عوارض", "عارضه", "تداخل", "مصرف", "نحوه", "طرز", "قرص", "چطور", "چگونه", "همزمان",
    };

    public async Task<EvidenceSet> RetrieveAsync(EvidenceQuery query, CancellationToken ct = default)
    {
        var normalized = QueryNormalizer.Normalize(query.Text);
        var ids = query.MedicationIds is { Count: > 0 } ? [.. query.MedicationIds.Distinct().Take(query.MaxMedications)] : await FindAsync(normalized, query.MaxMedications, ct);

        var documents = new List<MedicationKnowledgeDocument>();
        foreach (var id in ids)
        {
            var doc = await medications.GetKnowledgeDocumentAsync(id, false, ct);
            if (doc.Succeeded)
            {
                documents.Add(doc.Value!);
            }
        }

        return documents.Count == 0 ? EvidenceSet.Empty : Build(documents, QuestionTopics.Detect(normalized), clock.UtcNow, TimeSpan.FromDays(Math.Max(1, options.Value.EvidenceStaleAfterDays)));
    }

    private async Task<List<Guid>> FindAsync(string normalized, int max, CancellationToken ct)
    {
        var found = new List<Guid>();
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queries = new List<string>();
        if (normalized.Length is >= 2 and <= SearchMax)
        {
            queries.Add(normalized);
        }

        queries.AddRange(words.Where(t => t.Length >= 3 && !Stop.Contains(t)).Distinct().Take(6));
        foreach (var q in queries)
        {
            var page = await medications.SearchAsync(new MedicationSearchQuery(q, 3, 0, false), ct);
            foreach (var item in page.Items)
            {
                if (!found.Contains(item.Id))
                {
                    found.Add(item.Id);
                }
            }

            if (found.Count >= max)
            {
                break;
            }
        }

        return [.. found.Take(max)];
    }

    /// <summary>Pure function of its inputs (unit-testable without a database): documents → evidence set.</summary>
    public static EvidenceSet Build(IReadOnlyList<MedicationKnowledgeDocument> documents, QuestionTopics.Result topics, DateTimeOffset now, TimeSpan staleAfter)
    {
        var items = new List<EvidenceItem>();
        var medicationList = new List<EvidenceMedication>();
        var quarantined = 0;
        var excluded = 0;
        var truncated = false;
        var present = new HashSet<StatementKind>();
        var sourceById = documents.SelectMany(d => d.Sources).GroupBy(s => s.SourceId).ToDictionary(g => g.Key, g => g.First());

        foreach (var d in documents)
        {
            var name = d.Names.Count > 0 ? d.Names[0] : "?";
            medicationList.Add(new EvidenceMedication(d.MedicationId, name, d.DosageForm, d.Strength, d.Version, d.UpdatedAt, d.IsDemo, d.Validation.ToString()));
            var relevant = d.Statements
                .OrderByDescending(s => topics.Kinds.Contains(s.Kind))
                .ThenBy(s => s.Kind)
                .ToList();
            foreach (var s in relevant)
            {
                if (s.Validation == ValidationStatus.Rejected)
                {
                    excluded++;
                    continue;
                }

                if (InjectionPatterns.Looks(s.Text))
                {
                    quarantined++; // text that tries to instruct the assistant is never passed on as evidence
                    continue;
                }

                if (items.Count >= MaxItems)
                {
                    truncated = true;
                    continue;
                }

                present.Add(s.Kind);
                items.Add(Item(items.Count + 1, d, name, s.Kind.ToString(), s.Text, null, s.SourceId, s.Validation, sourceById, now, staleAfter));
            }

            foreach (var i in d.Interactions.OrderByDescending(x => x.Severity))
            {
                if (i.Validation == ValidationStatus.Rejected)
                {
                    excluded++;
                    continue;
                }

                if (InjectionPatterns.Looks(i.Summary) || InjectionPatterns.Looks(i.WithIngredient))
                {
                    quarantined++;
                    continue;
                }

                if (items.Count >= MaxItems)
                {
                    truncated = true;
                    continue;
                }

                items.Add(Item(items.Count + 1, d, name, "Interaction", i.Summary, $"{i.WithIngredient} | {i.Severity}", i.SourceId, i.Validation, sourceById, now, staleAfter));
            }
        }

        var conflicts = items.Where(x => x.Kind == "Interaction").GroupBy(x => (x.MedicationId, With: QueryNormalizer.Normalize(x.Qualifier!.Split('|')[0])))
            .Where(g => g.Select(x => x.Qualifier!.Split('|')[1].Trim()).Distinct(StringComparer.Ordinal).Count() > 1 && g.Select(x => x.Source.SourceId).Distinct().Count() > 1)
            .Select(g => new EvidenceConflict("interaction.severity_disagrees", g.First().MedicationName, [.. g.Select(x => x.Id)]))
            .ToList();

        var missing = new List<string>();
        foreach (var kind in topics.Kinds)
        {
            if (!present.Contains(kind) && (topics.Specific || kind is StatementKind.Indication or StatementKind.Contraindication or StatementKind.Warning or StatementKind.Administration or StatementKind.AdverseReaction))
            {
                missing.Add($"kind.{kind}");
            }
        }

        if (topics.Interactions && !items.Any(x => x.Kind == "Interaction"))
        {
            missing.Add("kind.Interaction");
        }

        var limitations = new List<string>();
        if (items.Count == 0)
        {
            limitations.Add("evidence.none_found");
        }
        else
        {
            limitations.Add("evidence.publication_date_not_recorded");
            if (items.Any(x => x.IsDemo))
            {
                limitations.Add("evidence.demo_data");
            }

            if (items.Any(x => x.Validation != nameof(ValidationStatus.Validated)))
            {
                limitations.Add("evidence.not_validated");
            }

            if (items.Any(x => x.Stale))
            {
                limitations.Add("evidence.stale_source");
            }

            if (items.Any(x => x.SourceDateUnknown))
            {
                limitations.Add("evidence.source_date_unknown");
            }

            if (conflicts.Count > 0)
            {
                limitations.Add("evidence.conflict");
            }

            if (missing.Count > 0)
            {
                limitations.Add("evidence.incomplete");
            }
        }

        if (truncated)
        {
            limitations.Add("evidence.truncated");
        }

        if (quarantined > 0)
        {
            limitations.Add("evidence.quarantined");
        }

        if (excluded > 0)
        {
            limitations.Add("evidence.excluded_rejected");
        }

        var quality = items.Count == 0 ? EvidenceQuality.None
            : items.Any(x => x.IsDemo) ? EvidenceQuality.DemoOnly
            : items.Any(x => x.Validation != nameof(ValidationStatus.Validated)) ? EvidenceQuality.Unverified
            : items.Any(x => x.Stale || x.SourceDateUnknown) || conflicts.Count > 0 || missing.Count > 0 || truncated || quarantined > 0 ? EvidenceQuality.Limited
            : EvidenceQuality.Validated;

        return new EvidenceSet(medicationList, items, conflicts, missing, limitations, quality, quarantined, excluded);
    }

    private static EvidenceItem Item(int n, MedicationKnowledgeDocument d, string name, string kind, string text, string? qualifier, Guid sourceId, ValidationStatus validation,
        Dictionary<Guid, KnowledgeSourceRef> sources, DateTimeOffset now, TimeSpan staleAfter)
    {
        var src = sources.TryGetValue(sourceId, out var s) ? s : new KnowledgeSourceRef(sourceId, "unknown source", string.Empty, string.Empty);
        var unknown = src.ReceivedAt is null;
        var stale = src.ReceivedAt is { } at && now - at > staleAfter;
        return new EvidenceItem($"E{n.ToString(CultureInfo.InvariantCulture)}", d.MedicationId, name, kind, text, qualifier,
            new EvidenceSource(src.SourceId, src.Name, src.Version, src.Publisher, src.ReceivedAt, (src.Validation ?? validation).ToString()),
            validation.ToString(), d.IsDemo, stale, unknown);
    }
}
