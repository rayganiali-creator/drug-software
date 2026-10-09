using MedSmarter.Modules.Integrations.Contracts;

namespace MedSmarter.Modules.Integrations;

/// <summary>Fictional insurer for development and tests. Identifiers are obviously invented ("DEMO-INS-...") and map to no real person.</summary>
public sealed class MockInsuranceProvider(MockInsuranceOptions? options = null) : IInsuranceProvider
{
    public const string ProviderId = "mock-insurance";
    private readonly MockInsuranceOptions _options = options ?? new();

    public string Id => ProviderId;

    public Task<InsuranceBatch> FetchAsync(DateTimeOffset? since, CancellationToken ct = default)
    {
        if (_options.SimulateFailure)
        {
            throw new HttpRequestException("simulated provider outage");
        }

        var from = new DateOnly(2026, 1, 1);
        return Task.FromResult(new InsuranceBatch(ProviderId, _options.BatchId, _options.ProducedAt,
        [
            new InsuranceMember("DEMO-INS", "DEMO-INS-0001", new InsuranceCoverage("DEMO-BASIC", CoverageStatus.Active, from, new DateOnly(2026, 12, 31))),
            new InsuranceMember("DEMO-INS", "DEMO-INS-0002", new InsuranceCoverage("DEMO-PLUS", CoverageStatus.Suspended, from, null)),
            new InsuranceMember("DEMO-INS", "DEMO-INS-0003", new InsuranceCoverage("DEMO-BASIC", CoverageStatus.Expired, new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31))),
        ]));
    }
}

public sealed class MockInsuranceOptions
{
    public bool SimulateFailure { get; set; }
    public string BatchId { get; set; } = "DEMO-BATCH-001";
    public DateTimeOffset ProducedAt { get; set; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
}
