namespace MedSmarter.Api;

/// <summary>The complete list of bounded modules composed by the Host (Phase 0, 03-modules.md).</summary>
internal static class ModuleCatalog
{
    public static IReadOnlyList<MedSmarter.BuildingBlocks.IModule> All { get; } =
    [
        new MedSmarter.Modules.ADR.ADRModule(),
        new MedSmarter.Modules.AI.AIModule(),
        new MedSmarter.Modules.ActiveIngredients.ActiveIngredientsModule(),
        new MedSmarter.Modules.Adherence.AdherenceModule(),
        new MedSmarter.Modules.Analytics.AnalyticsModule(),
        new MedSmarter.Modules.Audit.AuditModule(),
        new MedSmarter.Modules.ClinicalRules.ClinicalRulesModule(),
        new MedSmarter.Modules.Consent.ConsentModule(),
        new MedSmarter.Modules.Identity.IdentityModule(),
        new MedSmarter.Modules.Integrations.IntegrationsModule(),
        new MedSmarter.Modules.KnowledgeBase.KnowledgeBaseModule(),
        new MedSmarter.Modules.MedicationIntake.MedicationIntakeModule(),
        new MedSmarter.Modules.MedicationSchedule.MedicationScheduleModule(),
        new MedSmarter.Modules.Medications.MedicationsModule(),
        new MedSmarter.Modules.Notifications.NotificationsModule(),
        new MedSmarter.Modules.Patients.PatientsModule(),
        new MedSmarter.Modules.Pharmacies.PharmaciesModule(),
        new MedSmarter.Modules.Pharmacists.PharmacistsModule(),
        new MedSmarter.Modules.Physicians.PhysiciansModule(),
        new MedSmarter.Modules.Prescriptions.PrescriptionsModule(),
        new MedSmarter.Modules.Symptoms.SymptomsModule(),
        new MedSmarter.Modules.Users.UsersModule(),
    ];
}
