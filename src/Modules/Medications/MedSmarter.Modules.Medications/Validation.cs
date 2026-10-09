using System.Text.RegularExpressions;
using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Modules.Medications;

/// <summary>Pure (lookup-free) validation. Returns machine-readable codes; never echoes the offending input.</summary>
public static partial class MedicationValidator
{
    public const int MaxName = 200;
    public const int MaxStatement = 4000;
    public static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase) { "mg", "g", "mcg", "IU", "mL", "L", "%", "mmol", "unit", "puff", "mg/mL", "mcg/mL", "IU/mL" };
    public static readonly HashSet<string> Severities = new(StringComparer.OrdinalIgnoreCase) { "info", "mild", "moderate", "severe" };
    public static readonly HashSet<string> Frequencies = new(StringComparer.OrdinalIgnoreCase) { "very_common", "common", "uncommon", "rare", "very_rare", "unknown" };

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-/]{0,63}$")]
    private static partial Regex OpaqueId();

    [GeneratedRegex(@"^[A-Z](\d{2}([A-Z]([A-Z](\d{2})?)?)?)?$")]
    private static partial Regex Atc();

    [GeneratedRegex(@"^[A-Z]{2}$")]
    private static partial Regex Country();

    public static bool IsValidAtc(string? v) => v is not null && Atc().IsMatch(v);

    /// <summary>GS1 check digit for GTIN-8/12/13/14.</summary>
    public static bool IsValidGtin(string? v)
    {
        if (v is null || (v.Length is not (8 or 12 or 13 or 14)) || !v.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < v.Length - 1; i++)
        {
            var digit = v[v.Length - 2 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - (sum % 10)) % 10 == v[^1] - '0';
    }

    public static bool IsOfficialScheme(IdentifierScheme s) => s is IdentifierScheme.NationalDrugCode or IdentifierScheme.Gtin or IdentifierScheme.ManufacturerCode;

    public static string? TextProblem(LocalizedText t, int max, string field)
    {
        if (t.IsEmpty)
        {
            return $"{field}.required";
        }

        foreach (var s in new[] { t.En, t.Fa })
        {
            if (s is not null && (s.Length > max || s.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))))
            {
                return $"{field}.invalid";
            }
        }

        return null;
    }

    public static List<string> ValidateDraft(MedicationDraft d)
    {
        var errors = new List<string>();
        void Add(string? e)
        {
            if (e is not null && !errors.Contains(e))
            {
                errors.Add(e);
            }
        }

        Add(TextProblem(d.Name, MaxName, "name"));
        if (string.IsNullOrWhiteSpace(d.DosageFormCode))
        {
            Add("dosage_form.required");
        }

        if (d.Ingredients.Count is 0 or > 10)
        {
            Add("ingredients.count");
        }

        if (d.Ingredients.Select(i => i.IngredientId).Distinct().Count() != d.Ingredients.Count)
        {
            Add("ingredients.duplicate");
        }

        foreach (var i in d.Ingredients)
        {
            if (i.StrengthValue is { } v && (v <= 0 || v > 1_000_000m))
            {
                Add("strength.range");
            }

            if ((i.StrengthValue is null) != (string.IsNullOrWhiteSpace(i.StrengthUnit)))
            {
                Add("strength.value_and_unit");
            }

            if (!string.IsNullOrWhiteSpace(i.StrengthUnit) && !Units.Contains(i.StrengthUnit))
            {
                Add("strength.unit");
            }
        }

        if (d.Synonyms.Count > 30 || d.Synonyms.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 100 || s.Any(char.IsControl)))
        {
            Add("synonyms.invalid");
        }

        if (d.RouteCodes.Count > 8 || d.ClassCodes.Count > 12 || d.Identifiers.Count > 12 || d.Statements.Count > 200)
        {
            Add("collections.too_large");
        }

        foreach (var id in d.Identifiers)
        {
            if (id.SourceRevisionId is null || id.SourceRevisionId == Guid.Empty)
            {
                Add("identifier.source.required"); // an identifier without provenance is indistinguishable from an invented one
            }

            switch (id.Scheme)
            {
                case IdentifierScheme.Gtin when !IsValidGtin(id.Value):
                    Add("identifier.gtin.invalid");
                    break;
                case IdentifierScheme.AtcCode when !IsValidAtc(id.Value):
                    Add("identifier.atc.invalid");
                    break;
                case not IdentifierScheme.Gtin and not IdentifierScheme.AtcCode when !OpaqueId().IsMatch(id.Value ?? string.Empty):
                    Add("identifier.format");
                    break;
            }

            if (d.IsDemo && IsOfficialScheme(id.Scheme))
            {
                Add("identifier.official_on_demo"); // fictional records can never carry a national/GS1/manufacturer code
            }
        }

        if (d.Identifiers.GroupBy(i => (i.Scheme, (i.Value ?? string.Empty).ToUpperInvariant())).Any(g => g.Count() > 1))
        {
            Add("identifier.duplicate");
        }

        foreach (var s in d.Statements)
        {
            Add(TextProblem(s.Text, MaxStatement, "statement.text"));
            if (s.Severity is not null && !Severities.Contains(s.Severity))
            {
                Add("statement.severity");
            }

            if (s.Frequency is not null && (s.Kind != StatementKind.AdverseReaction || !Frequencies.Contains(s.Frequency)))
            {
                Add("statement.frequency");
            }

            if (s.Population is not null && s.Population.Length > 200)
            {
                Add("statement.population");
            }

            if (s.RevisionId == Guid.Empty)
            {
                Add("statement.source.required");
            }
        }

        return errors;
    }

    public static string? ValidateIngredient(NewIngredient v)
    {
        var p = TextProblem(v.Name, MaxName, "name");
        if (p is not null)
        {
            return p;
        }

        if (v.AtcCode is not null && !IsValidAtc(v.AtcCode))
        {
            return "atc.invalid";
        }

        return v.Synonyms.Count > 30 || v.Synonyms.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 100) ? "synonyms.invalid" : null;
    }

    public static string? ValidateManufacturer(NewManufacturer v) =>
        TextProblem(v.Name, MaxName, "name")
        ?? (v.Country is not null && !Country().IsMatch(v.Country) ? "country.invalid" : null)
        ?? (v.ManufacturerCode is not null && !OpaqueId().IsMatch(v.ManufacturerCode) ? "manufacturer_code.format" : null);

    public static string? ValidateTerm(NewReferenceTerm v) =>
        TextProblem(v.Name, MaxName, "name") ?? (OpaqueId().IsMatch(v.Code ?? string.Empty) ? null : "code.format");
}
