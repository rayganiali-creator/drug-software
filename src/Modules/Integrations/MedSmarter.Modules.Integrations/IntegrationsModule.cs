using MedSmarter.Modules.Integrations.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Integrations;

/// <summary>
/// External-system adapters. Every adapter has a Disabled default and a local Mock; there is NO real implementation. Configuration <c>Integrations:{Name}:Mode</c> = Disabled | Mock | Real;
/// "Real" is fail-closed (the Disabled adapter stays in place and the registry reports RealBlocked).
/// </summary>
public sealed class IntegrationsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Integrations";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<MedSmarter.BuildingBlocks.IClock, MedSmarter.BuildingBlocks.SystemClock>();
        services.AddSingleton<InsuranceStore>();
        services.AddSingleton<IInsuranceIntegrationService, InsuranceIntegrationService>();
        // Only the mock exists, and only in development configuration. A real insurer = a new IInsuranceProvider adapter.
        if (configuration.GetValue<bool>("Integrations:Insurance:EnableMock"))
        {
            services.AddSingleton<IInsuranceProvider>(new MockInsuranceProvider());
        }

        services.AddSingleton<IIntegrationRegistry>(new IntegrationRegistry(configuration));
        var mock = (string key) => IntegrationRegistry.ModeOf(configuration, key) == IntegrationMode.Mock;
        if (mock("MedicationReference")) { services.AddSingleton<IMedicationReferenceAdapter, MockMedicationReferenceAdapter>(); } else { services.AddSingleton<IMedicationReferenceAdapter, DisabledMedicationReferenceAdapter>(); }
        if (mock("ProviderVerification")) { services.AddSingleton<IProviderVerificationAdapter, MockProviderVerificationAdapter>(); } else { services.AddSingleton<IProviderVerificationAdapter, DisabledProviderVerificationAdapter>(); }
        if (mock("ElectronicPrescription")) { services.AddSingleton<IElectronicPrescriptionAdapter, MockElectronicPrescriptionAdapter>(); } else { services.AddSingleton<IElectronicPrescriptionAdapter, DisabledElectronicPrescriptionAdapter>(); }
        if (mock("NationalReporting")) { services.AddSingleton<INationalReportingAdapter, MockNationalReportingAdapter>(); } else { services.AddSingleton<INationalReportingAdapter, DisabledNationalReportingAdapter>(); }
    }
}
