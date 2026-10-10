using System.Globalization;
using System.Text;

namespace MedSmarter.Modules.Medications;

/// <summary>
/// One normalization for stored search terms AND queries, so Persian/Arabic typing variants meet:
/// ي/ى→ی, ك→ک, ة/ۀ→ه, أ/إ/ٱ/آ→ا, diacritics and tatweel removed, ZWNJ→space, Persian/Arabic digits→ASCII,
/// English lower-cased, punctuation → space, whitespace collapsed.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = MedSmarter.BuildingBlocks.TextFolding.Fold(input).Normalize(NormalizationForm.FormKC);
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

            if (c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ' or 'ۖ' or 'ۭ')
            {
                continue; // harakat, superscript alef, tatweel, Quranic marks
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
                sb.Append(' '); // ZWNJ, hyphens, slashes, punctuation, control characters
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
