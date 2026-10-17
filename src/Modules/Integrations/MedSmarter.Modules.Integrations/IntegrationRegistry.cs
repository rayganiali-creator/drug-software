using MedSmarter.Modules.Integrations.Contracts;
using Microsoft.Extensions.Configuration;

namespace MedSmarter.Modules.Integrations;

/// <summary>
/// Describes what is in force for every integration, from configuration alone (it makes no call). "Real" can never be activated here: no approved real adapter exists, so a request
/// for it is reported as <see cref="IntegrationState.RealBlocked"/> and the integration stays inactive. Existing capabilities keep their own switches; this registry reads them.
/// </summary>
public sealed class IntegrationRegistry(IConfiguration configuration) : IIntegrationRegistry
{
    public const string Section = "Integrations";

    public static IntegrationMode ModeOf(IConfiguration c, string key, IntegrationMode fallback = IntegrationMode.Disabled) =>
        Enum.TryParse<IntegrationMode>(c.GetSection(Section)[$"{key}:Mode"], true, out var m) ? m : fallback;

    public IReadOnlyList<IntegrationDescriptor> Describe()
    {
        var c = configuration;
        var list = new List<IntegrationDescriptor>
        {
            New("medication-reference", IntegrationKind.MedicationReference, "Authoritative medication/product information", "Look up a medicine in an authoritative external reference", IMedicationReferenceAdapter.Version, ModeOf(c, "MedicationReference"),
                ["Authoritative source and its licence/terms", "Data-quality and update rules", "Approved field mapping"]),
            New("provider-verification", IntegrationKind.ProviderVerification, "Healthcare-provider verification", "Check a professional's licence with an authoritative registry", IProviderVerificationAdapter.Version, ModeOf(c, "ProviderVerification"),
                ["Registry specification and access agreement", "Legal basis and privacy review", "Credential management (never in source)"]),
            New("e-prescription", IntegrationKind.ElectronicPrescription, "E-prescription / pharmacy systems", "Fetch a prescription from an external system", IElectronicPrescriptionAdapter.Version, ModeOf(c, "ElectronicPrescription"),
                ["System specification and access agreement", "Consent and legal basis for exchange", "Identifier policy (no invented codes)"]),
            New("national-pv-reporting", IntegrationKind.NationalPharmacovigilanceReporting, "National pharmacovigilance reporting", "Submit an authorised, human-reviewed report to a national centre", INationalReportingAdapter.Version, ModeOf(c, "NationalReporting"),
                ["Authoritative national form, fields, pathway and deadlines (none supplied)", "Legal basis; whether MedSmarter may submit at all", "Human-review and consent re-check at disclosure", "Clinical, regulatory, legal and privacy approval"]),
        };

        var mfr = c.GetSection("ManufacturerReports")["Provider"];
        list.Add(Existing("manufacturer-reporting", IntegrationKind.ManufacturerReporting, "Manufacturer safety reporting (Phase 5 pipeline)", "Deliver a reviewed, minimised, identity-free report to a manufacturer", "manufacturer-report/v1",
            string.Equals(mfr, "Mock", StringComparison.OrdinalIgnoreCase) ? IntegrationMode.Mock : string.IsNullOrWhiteSpace(mfr) || string.Equals(mfr, "Disabled", StringComparison.OrdinalIgnoreCase) ? IntegrationMode.Disabled : IntegrationMode.Real,
            ["Agreement and technical channel with the manufacturer or a national gateway", "Legal basis for the transfer", "Privacy review of the payload"]));

        var insurance = c.GetValue<bool>("Integrations:Insurance:EnableMock") ? IntegrationMode.Mock : ModeOf(c, "Insurance");
        list.Add(Existing("insurance-coverage", IntegrationKind.InsuranceCoverage, "Insurance eligibility / coverage (administrative data only)", "Import coverage data; never pharmacovigilance records", "insurance/v1", insurance,
            ["Insurer agreement and specification", "Legal basis for exchange", "Strict separation from adverse-event and pharmacy-safety records (always)"]));

        var ai = c.GetSection("Ai")["Provider"];
        var aiMode = string.Equals(ai, "Mock", StringComparison.OrdinalIgnoreCase) ? IntegrationMode.Mock : string.IsNullOrWhiteSpace(ai) || string.Equals(ai, "Disabled", StringComparison.OrdinalIgnoreCase) ? IntegrationMode.Disabled
            : string.Equals(ai, "Local", StringComparison.OrdinalIgnoreCase) ? IntegrationMode.Disabled : IntegrationMode.Real;
        list.Add(new IntegrationDescriptor("ai-provider", IntegrationKind.AiProvider, "AI provider (Phase 6)", "Language-model assistance for information only", aiMode, aiMode == IntegrationMode.Real ? IntegrationState.RealGatedElsewhere : aiMode == IntegrationMode.Mock ? IntegrationState.MockActive : IntegrationState.Inactive,
            aiMode == IntegrationMode.Mock, false, "ai/v1", aiMode == IntegrationMode.Real ? "External processing stays behind the Phase 6 gate (explicit configuration and per-patient consent). This registry does not enable it." : null,
            ["Approved provider and data-processing terms", "Evaluation and safety validation", "Per-patient external-processing consent"]));

        var auth = c.GetSection("Auth")["Mode"];
        list.Add(new IntegrationDescriptor("identity", IntegrationKind.Identity, "Identity / authentication (Phase 3)", "Authenticate users", string.Equals(auth, "DevelopmentMock", StringComparison.OrdinalIgnoreCase) ? IntegrationMode.Mock : IntegrationMode.Disabled,
            string.Equals(auth, "DevelopmentMock", StringComparison.OrdinalIgnoreCase) ? IntegrationState.MockActive : IntegrationState.Inactive, string.Equals(auth, "DevelopmentMock", StringComparison.OrdinalIgnoreCase), false, "identity/v1", null,
            ["Real identity provider, MFA policy, credential management"]));
        return list;
    }

    private static IntegrationDescriptor New(string id, IntegrationKind kind, string title, string responsibility, string version, IntegrationMode mode, string[] required) => Build(id, kind, title, responsibility, version, mode, required);

    private static IntegrationDescriptor Existing(string id, IntegrationKind kind, string title, string responsibility, string version, IntegrationMode mode, string[] required) => Build(id, kind, title, responsibility, version, mode, required);

    private static IntegrationDescriptor Build(string id, IntegrationKind kind, string title, string responsibility, string version, IntegrationMode mode, string[] required) => new(
        id, kind, title, responsibility, mode,
        mode == IntegrationMode.Mock ? IntegrationState.MockActive : mode == IntegrationMode.Real ? IntegrationState.RealBlocked : IntegrationState.Inactive,
        mode == IntegrationMode.Mock, false, version,
        mode == IntegrationMode.Real ? "No approved real adapter exists. The request is ignored and the integration stays inactive (fail-closed)." : null, required);
}
