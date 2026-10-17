namespace MedSmarter.Modules.Integrations.Contracts;

// Phase 11: the common vocabulary of every external integration. NO real external system is connected by anything in this file or its implementations.
// Rules every adapter follows:
//  * an interface with one responsibility and a versioned contract;
//  * a Disabled implementation (the default: NotConfigured, no side effect) and a local Mock implementation (labelled simulated, no network);
//  * a REAL implementation does not exist; asking for one is fail-closed (the registry shows "RealBlocked" and the Disabled implementation stays in place);
//  * every result says whether it is simulated; a simulated result can never be mistaken for a real one.

public enum IntegrationKind
{
    MedicationReference,
    ProviderVerification,
    ElectronicPrescription,
    NationalPharmacovigilanceReporting,
    ManufacturerReporting,
    InsuranceCoverage,
    AiProvider,
    Identity,
}

/// <summary>What the configuration asks for.</summary>
public enum IntegrationMode
{
    Disabled,
    Mock,
    Real,
}

/// <summary>What is actually in force.</summary>
public enum IntegrationState
{
    /// <summary>Nothing is connected and nothing is simulated.</summary>
    Inactive,

    /// <summary>A local mock answers. Results are labelled simulated. No network is used.</summary>
    MockActive,

    /// <summary>"Real" was requested but no approved adapter exists, so the integration stays inactive (fail-closed).</summary>
    RealBlocked,

    /// <summary>A pre-existing, separately gated capability (AI external processing) is configured. Its own gate and consent rules still apply; this registry does not enable it.</summary>
    RealGatedElsewhere,
}

public sealed record IntegrationDescriptor(
    string Id,
    IntegrationKind Kind,
    string Title,
    string Responsibility,
    IntegrationMode ConfiguredMode,
    IntegrationState State,
    bool ResultsAreSimulated,
    bool UsesNetwork,
    string ContractVersion,
    string? Blocker,
    IReadOnlyList<string> RequiredBeforeReal);

public interface IIntegrationRegistry
{
    IReadOnlyList<IntegrationDescriptor> Describe();
}

public enum AdapterOutcome
{
    /// <summary>The adapter is disabled. Nothing was done.</summary>
    NotConfigured,

    /// <summary>A local mock produced the value. It is NOT a real answer.</summary>
    Simulated,

    /// <summary>The request was refused by the adapter's own checks (for example a missing human review).</summary>
    Rejected,

    TransientFailure,
}

public sealed record AdapterResult<T>(AdapterOutcome Outcome, T? Value, string? ErrorCode, string ContractVersion)
{
    public bool IsSimulated => Outcome == AdapterOutcome.Simulated;
}

public static class AdapterResults
{
    public static AdapterResult<T> NotConfigured<T>(string version) => new(AdapterOutcome.NotConfigured, default, "adapter_not_configured", version);
}

// ---------------------------------------------------------------- authoritative medication / product information

public sealed record MedicationReferenceQuery(string Identifier, string IdentifierSystem);

public sealed record MedicationReferenceInfo(string Identifier, string Name, string Source, bool IsFictional);

public interface IMedicationReferenceAdapter
{
    const string Version = "medication-reference/v1-draft";

    Task<AdapterResult<MedicationReferenceInfo>> LookupAsync(MedicationReferenceQuery query, CancellationToken ct = default);
}

// ---------------------------------------------------------------- identity / healthcare-provider verification

public enum ProviderVerificationStatus
{
    Unverified,
    SimulatedVerified,
}

public sealed record ProviderVerificationRequest(string LicenceNumberAsGiven, string ProfessionAsGiven);

public sealed record ProviderVerificationInfo(ProviderVerificationStatus Status, string Source);

public interface IProviderVerificationAdapter
{
    const string Version = "provider-verification/v1-draft";

    Task<AdapterResult<ProviderVerificationInfo>> VerifyAsync(ProviderVerificationRequest request, CancellationToken ct = default);
}

// ---------------------------------------------------------------- e-prescription / pharmacy systems

public sealed record PrescriptionLookup(string PrescriptionReference);

public sealed record ExternalPrescriptionInfo(string Reference, string Summary, string Source, bool IsFictional);

public interface IElectronicPrescriptionAdapter
{
    const string Version = "e-prescription/v1-draft";

    Task<AdapterResult<ExternalPrescriptionInfo>> FetchAsync(PrescriptionLookup lookup, CancellationToken ct = default);
}

// ---------------------------------------------------------------- national pharmacovigilance reporting

/// <summary>
/// A deliberately THIN envelope. No national form, field list, deadline or schema is supplied in the project's sources, so none is invented here. The two flags are guard
/// rails that any future real adapter must also enforce; the mock refuses a submission without both.
/// </summary>
public sealed record NationalReportEnvelope(string ContractVersion, string InternalReference, bool HumanReviewed, bool LegalBasisConfirmed);

public sealed record NationalReportReceipt(string Reference, string Source);

public interface INationalReportingAdapter
{
    const string Version = "national-pv-reporting/v0-placeholder";

    Task<AdapterResult<NationalReportReceipt>> SubmitAsync(NationalReportEnvelope envelope, string idempotencyKey, CancellationToken ct = default);
}
