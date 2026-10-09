using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MedSmarter.Modules.ProductTrace.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.ProductTrace;

/// <summary>The default: nothing is sent anywhere. Reports stay queued until a real provider is agreed and configured.</summary>
public sealed class DisabledManufacturerReportProvider : IManufacturerReportProvider
{
    public string Name => "disabled";
    public bool IsMock => false;
    public bool IsConfigured => false;

    public Task<ReportSubmissionResult> SubmitAsync(ManufacturerReportPayload payload, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(new ReportSubmissionResult(SubmissionOutcome.NotConfigured, null, "provider_not_configured"));
}

/// <summary>
/// Fictional receiver for development and tests. It makes no network call and contacts no manufacturer; its "acknowledgements" are
/// labelled MOCK. It also remembers idempotency keys, like a real receiver must.
/// </summary>
public sealed class MockManufacturerReportProvider(IOptions<ManufacturerReportsOptions> options) : IManufacturerReportProvider
{
    public const string AckPrefix = "MOCK-ACK-";
    private readonly ConcurrentDictionary<string, int> _attempts = new();
    private readonly ConcurrentDictionary<string, string> _accepted = new();

    public string Name => "mock";
    public bool IsMock => true;
    public bool IsConfigured => true;

    /// <summary>How many payloads this mock has accepted (test hook).</summary>
    public int AcceptedCount => _accepted.Count;

    public Task<ReportSubmissionResult> SubmitAsync(ManufacturerReportPayload payload, string idempotencyKey, CancellationToken ct = default)
    {
        if (_accepted.TryGetValue(idempotencyKey, out var existing))
        {
            return Task.FromResult(new ReportSubmissionResult(SubmissionOutcome.Accepted, existing, null)); // same key: same answer, no second report
        }

        var attempt = _attempts.AddOrUpdate(idempotencyKey, 1, (_, n) => n + 1);
        if (attempt <= options.Value.Mock.FailFirstAttempts)
        {
            return Task.FromResult(new ReportSubmissionResult(SubmissionOutcome.TransientFailure, null, "mock_unavailable"));
        }

        var ack = AckPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..12];
        _accepted[idempotencyKey] = ack;
        return Task.FromResult(new ReportSubmissionResult(SubmissionOutcome.Accepted, ack, null));
    }
}

/// <summary>No camera and no scanner service yet. This is where they plug in.</summary>
public sealed class NotAvailableScanProvider : IProductScanProvider
{
    public bool IsAvailable => false;

    public Task<ScanResult?> ParseAsync(string rawCode, CancellationToken ct = default) => Task.FromResult<ScanResult?>(null);
}
