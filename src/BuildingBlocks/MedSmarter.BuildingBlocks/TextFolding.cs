using System.Text;

namespace MedSmarter.BuildingBlocks;

/// <summary>
/// Folds look-alike "compatibility" characters to plain ASCII before text is checked or matched. The repository runs with invariant globalization
/// (no ICU), where <c>string.Normalize(NormalizationForm.FormKC)</c> does nothing, so full-width letters, digits and punctuation (e.g. "ｉｇｎｏｒｅ", "＠")
/// and mathematical bold/italic letters would otherwise slip past e-mail, link, number and instruction checks. This covers the ranges that are
/// practical to abuse; it is not a general Unicode confusables solution.
/// </summary>
public static class TextFolding
{
    public static string Fold(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var needs = false;
        foreach (var c in input)
        {
            if (c is >= '！' and <= '～' or '　' || char.IsHighSurrogate(c))
            {
                needs = true;
                break;
            }
        }

        if (!needs)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c is >= '！' and <= '～')
            {
                sb.Append((char)(c - 0xFEE0)); // full-width ASCII block
            }
            else if (c == '　')
            {
                sb.Append(' ');
            }
            else if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
            {
                var cp = char.ConvertToUtf32(c, input[i + 1]);
                i++;
                if (cp is >= 0x1D400 and <= 0x1D6A3)
                {
                    var n = (cp - 0x1D400) % 52; // each mathematical alphabet style holds A-Z then a-z
                    sb.Append((char)(n < 26 ? 'A' + n : 'a' + (n - 26)));
                }
                else if (cp is >= 0x1D7CE and <= 0x1D7FF)
                {
                    sb.Append((char)('0' + ((cp - 0x1D7CE) % 10)));
                }
                else
                {
                    sb.Append(c).Append(input[i]);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
