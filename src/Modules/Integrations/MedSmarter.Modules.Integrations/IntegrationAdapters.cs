using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MedSmarter.Modules.Integrations.Contracts;

namespace MedSmarter.Modules.Integrations;

// Disabled implementations are the default for every adapter: they do nothing and say so. Mock implementations are local, fictional, labelled simulated and never touch the network
// (this assembly references no HTTP type; a test enforces it).

public sealed class DisabledMedicationReferenceAdapter : IMedicationReferenceAdapter
{
    public Task<AdapterResult<MedicationReferenceInfo>> LookupAsync(MedicationReferenceQuery query, CancellationToken ct = default) =>
        Task.FromResult(AdapterResults.NotConfigured<MedicationReferenceInfo>(IMedicationReferenceAdapter.Version));
}

public sealed class MockMedicationReferenceAdapter : IMedicationReferenceAdapter
{
    public Task<AdapterResult<MedicationReferenceInfo>> LookupAsync(MedicationReferenceQuery query, CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(query.Identifier) || query.Identifier.Length > 64
            ? new AdapterResult<MedicationReferenceInfo>(AdapterOutcome.Rejected, null, "identifier_invalid", IMedicationReferenceAdapter.Version)
            : new AdapterResult<MedicationReferenceInfo>(AdapterOutcome.Simulated, new MedicationReferenceInfo(query.Identifier, "Fictional medicine (simulated)", "MOCK", true), null, IMedicationReferenceAdapter.Version));
}

public sealed class DisabledProviderVerificationAdapter : IProviderVerificationAdapter
{
    public Task<AdapterResult<ProviderVerificationInfo>> VerifyAsync(ProviderVerificationRequest request, CancellationToken ct = default) =>
        Task.FromResult(AdapterResults.NotConfigured<ProviderVerificationInfo>(IProviderVerificationAdapter.Version));
}

/// <summary>"Verifies" nobody: it answers SimulatedVerified for a fictional licence prefix so the flow can be exercised, and says MOCK. It is never a verification.</summary>
public sealed class MockProviderVerificationAdapter : IProviderVerificationAdapter
{
    public const string FictionalPrefix = "DEMO-";

    public Task<AdapterResult<ProviderVerificationInfo>> VerifyAsync(ProviderVerificationRequest request, CancellationToken ct = default)
    {
        var ok = request.LicenceNumberAsGiven.StartsWith(FictionalPrefix, StringComparison.Ordinal);
        return Task.FromResult(new AdapterResult<ProviderVerificationInfo>(AdapterOutcome.Simulated,
            new ProviderVerificationInfo(ok ? ProviderVerificationStatus.SimulatedVerified : ProviderVerificationStatus.Unverified, "MOCK"), null, IProviderVerificationAdapter.Version));
    }
}

public sealed class DisabledElectronicPrescriptionAdapter : IElectronicPrescriptionAdapter
{
    public Task<AdapterResult<ExternalPrescriptionInfo>> FetchAsync(PrescriptionLookup lookup, CancellationToken ct = default) =>
        Task.FromResult(AdapterResults.NotConfigured<ExternalPrescriptionInfo>(IElectronicPrescriptionAdapter.Version));
}

public sealed class MockElectronicPrescriptionAdapter : IElectronicPrescriptionAdapter
{
    public Task<AdapterResult<ExternalPrescriptionInfo>> FetchAsync(PrescriptionLookup lookup, CancellationToken ct = default) =>
        Task.FromResult(new AdapterResult<ExternalPrescriptionInfo>(AdapterOutcome.Simulated, new ExternalPrescriptionInfo(lookup.PrescriptionReference, "Fictional prescription (simulated)", "MOCK", true), null, IElectronicPrescriptionAdapter.Version));
}

public sealed class DisabledNationalReportingAdapter : INationalReportingAdapter
{
    public Task<AdapterResult<NationalReportReceipt>> SubmitAsync(NationalReportEnvelope envelope, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(AdapterResults.NotConfigured<NationalReportReceipt>(INationalReportingAdapter.Version));
}

/// <summary>Refuses anything without a human review and a confirmed legal basis; otherwise returns a receipt that says MOCK. Same key, same receipt. Nothing leaves the process.</summary>
public sealed class MockNationalReportingAdapter : INationalReportingAdapter
{
    public const string ReceiptPrefix = "MOCK-NATIONAL-";
    private readonly ConcurrentDictionary<string, string> _receipts = new();

    public int ReceiptCount => _receipts.Count;

    public Task<AdapterResult<NationalReportReceipt>> SubmitAsync(NationalReportEnvelope envelope, string idempotencyKey, CancellationToken ct = default)
    {
        if (!envelope.HumanReviewed)
        {
            return Task.FromResult(new AdapterResult<NationalReportReceipt>(AdapterOutcome.Rejected, null, "human_review_required", INationalReportingAdapter.Version));
        }

        if (!envelope.LegalBasisConfirmed)
        {
            return Task.FromResult(new AdapterResult<NationalReportReceipt>(AdapterOutcome.Rejected, null, "legal_basis_required", INationalReportingAdapter.Version));
        }

        var receipt = _receipts.GetOrAdd(idempotencyKey, k => ReceiptPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(k)))[..12]);
        return Task.FromResult(new AdapterResult<NationalReportReceipt>(AdapterOutcome.Simulated, new NationalReportReceipt(receipt, "MOCK"), null, INationalReportingAdapter.Version));
    }
}
