using System.Text.RegularExpressions;

namespace MedSmarter.BuildingBlocks;

/// <summary>
/// Free text entered by patients ends up in lists, reports and (minimised) AI context. It must not become a side channel for identity:
/// text that looks like an e-mail address, a link, a phone number or an official id is refused with a code, never stored.
/// </summary>
public static partial class FreeTextGuard
{
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex Email();

    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    // 8+ digits in a row, allowing common separators: phone numbers, national/insurance/card numbers. Persian digits are normalised first.
    [GeneratedRegex(@"(?:\d[\s\-().]*){8,}")]
    private static partial Regex LongNumber();

    /// <summary>Returns a code when the text must be refused, else null.</summary>
    public static string? Problem(string? text, string field, int maxLength)
    {
        if (text is null)
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            return $"{field}.too_long";
        }

        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
        {
            return $"{field}.invalid_characters";
        }

        // Invisible format characters (zero-width space/joiner, word joiner, soft hyphen, BOM...) must not hide an e-mail address, link or number.
        // They are removed for the check only; the Persian zero-width non-joiner is legitimate text and is stored unchanged.
        var normalized = NormalizeDigits(StripInvisible(text));
        return Email().IsMatch(normalized) || Link().IsMatch(normalized) || LongNumber().IsMatch(normalized) ? $"{field}.looks_identifying" : null;
    }

    private static string StripInvisible(string s) =>
        s.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) ? new string([.. s.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)]) : s;

    /// <summary>Maps Persian and Arabic-Indic digits to ASCII so number patterns cannot be hidden in another script.</summary>
    public static string NormalizeDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '۰' and <= '۹' => (char)('0' + (chars[i] - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (chars[i] - '٠')),
                _ => chars[i],
            };
        }

        return new string(chars);
    }

    /// <summary>Trims and collapses whitespace; null when empty.</summary>
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : Regex.Replace(s.Trim(), @"\s+", " ");
}
