using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MedSmarter.Modules.ADR.Contracts;

namespace MedSmarter.Api.Adr;

/// <summary>
/// Position marker of the reviewer's queue. It is only a position (the LastActivityAt and Id of the last record already returned), bound to the caller and to the statuses asked for.
/// It is NOT a permission: the authorized subjects are recomputed from relationship and consent on every request, and the position is only ever used as a comparison value
/// (the record it names is never looked up), so editing it can move the caller's own position within the records they may already read, nothing else. It is not signed on
/// purpose: the project has no key for this and nothing it protects needs one. Anything that does not parse is rejected with one code, whatever the reason.
/// </summary>
internal static class QueueCursor
{
    private const string Prefix = "v1.";
    public const int MaxLength = 200;

    public sealed record Position(DateTimeOffset LastActivityAt, Guid Id);

    /// <summary>One bit per review status, so the same set gives the same mask in any order.</summary>
    public static int MaskOf(IEnumerable<ReviewStatus> statuses) => statuses.Distinct().Aggregate(0, (m, s) => m | (1 << (int)s));

    public static string Encode(Position at, Guid actor, int statusMask) =>
        Prefix + Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{at.LastActivityAt.UtcTicks}|{at.Id:N}|{statusMask}|{actor:N}")));

    /// <summary>Null when the value is anything but a well-formed cursor issued for this caller and these statuses.</summary>
    public static Position? Decode(string? value, Guid actor, int statusMask)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || !value.StartsWith(Prefix, StringComparison.Ordinal) || !value.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.'))
        {
            return null;
        }

        string text;
        try
        {
            text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value.AsSpan(Prefix.Length)));
        }
        catch (FormatException)
        {
            return null;
        }

        var parts = text.Split('|');
        if (parts.Length != 4
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParseExact(parts[1], "N", out var id)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var mask) || mask != statusMask
            || !Guid.TryParseExact(parts[3], "N", out var owner) || owner != actor)
        {
            return null;
        }

        return new Position(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }
}
