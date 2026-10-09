using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Modules.ProductTrace;

internal static class Support
{
    public const string DemoNotice = "DEMO DATA - NOT FOR CLINICAL USE";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static TraceOutcome<T> Invalid<T>(IEnumerable<string> codes) => TraceOutcome.Fail<T>(TraceError.Validation, string.Join(';', codes));

    public static Task AuditAsync(IAuditWriter audit, string action, AuditResult result, Guid actor, string resourceType, Guid? resourceId, Guid? subject, string source, string? correlationId, string? reason = null, string? detail = null) =>
        audit.WriteAsync(new AuditEvent(action, result, actor, resourceType, resourceId?.ToString(), subject, source, correlationId, reason, detail is null ? null : new Dictionary<string, string> { ["detail"] = detail }));

    /// <summary>GS1 check digit for GTIN-8/12/13/14. Only the format is verified; nothing is generated or looked up.</summary>
    public static bool IsValidGtin(string gtin)
    {
        if (gtin.Length is not (8 or 12 or 13 or 14) || gtin.Any(c => !char.IsAsciiDigit(c)))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < gtin.Length - 1; i++)
        {
            var digit = gtin[gtin.Length - 2 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - (sum % 10)) % 10 == gtin[^1] - '0';
    }
}

/// <summary>The report status machine. Every status change goes through <see cref="Allowed"/>.</summary>
public static class ReportStateMachine
{
    private static readonly Dictionary<ReportStatus, ReportStatus[]> Transitions = new()
    {
        [ReportStatus.Draft] = [ReportStatus.PendingConsentOrReview, ReportStatus.ReadyToSend, ReportStatus.Cancelled],
        [ReportStatus.PendingConsentOrReview] = [ReportStatus.ReadyToSend, ReportStatus.Draft, ReportStatus.Cancelled],
        [ReportStatus.ReadyToSend] = [ReportStatus.Sent, ReportStatus.Acknowledged, ReportStatus.Failed, ReportStatus.PendingConsentOrReview, ReportStatus.Cancelled],
        [ReportStatus.Sent] = [ReportStatus.Acknowledged, ReportStatus.Failed],
        [ReportStatus.Failed] = [ReportStatus.ReadyToSend, ReportStatus.Cancelled],
        [ReportStatus.Acknowledged] = [],
        [ReportStatus.Cancelled] = [],
    };

    public static bool Allowed(ReportStatus from, ReportStatus to) => Transitions.TryGetValue(from, out var next) && next.Contains(to);
}
