using MedSmarter.Modules.Patients.Contracts;

// Thin doors to the internal validators of the modules under test (InternalsVisibleTo).
namespace MedSmarter.Patients.Tests;

internal static class ValidatorAccess
{
    public static List<string> Profile(UpdateProfileCommand c, int year) => MedSmarter.Modules.Patients.PatientValidator.Profile(c, year);
    public static List<string> Medication(PatientMedicationInput i, DateOnly today) => MedSmarter.Modules.Patients.PatientValidator.Medication(i, today);
    public static List<string> Schedule(ScheduleEntryInput i, DateOnly today) => MedSmarter.Modules.Patients.PatientValidator.Schedule(i, today);
    public static int DaysToMask(IReadOnlyList<DayOfWeek>? days) => MedSmarter.Modules.Patients.PatientValidator.DaysToMask(days);
    public static IReadOnlyList<DayOfWeek> MaskToDays(int mask) => MedSmarter.Modules.Patients.PatientValidator.MaskToDays(mask);
}

internal static class ProductSupportAccess
{
    public static bool IsValidGtin(string gtin) => MedSmarter.Modules.ProductTrace.Support.IsValidGtin(gtin);
}
