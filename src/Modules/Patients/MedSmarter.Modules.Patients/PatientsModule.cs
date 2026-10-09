using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Patients;

/// <summary>Patient profile, medications taken, schedule and intake, symptoms, care relationships and the AI patient context (docs/phase5).</summary>
public sealed class PatientsModule : IModule
{
    public string Name => "Patients";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PatientsOptions>(configuration.GetSection(PatientsOptions.Section));
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddModuleDatabase<PatientsDbContext>(configuration, PatientsDbContext.Schema);
        services.AddSingleton<IPatientService, PatientService>();
        services.AddSingleton<IPatientMedicationService, PatientMedicationService>();
        services.AddSingleton<CareRelationshipService>();
        services.AddSingleton<ICareRelationshipService>(sp => sp.GetRequiredService<CareRelationshipService>());
        services.AddSingleton<ICareRelationshipSource>(sp => sp.GetRequiredService<CareRelationshipService>());
        services.AddSingleton<IPatientContextService, PatientContextService>();
        services.AddSingleton<IPatientDirectory, PatientDirectory>();
        if (configuration.GetSection(PatientsOptions.Section).GetValue<bool>(nameof(PatientsOptions.SeedDemoData)))
        {
            services.AddSingleton<IDemoPatientSeeder, DemoPatientSeeder>();
        }
    }
}
