using System.Text.RegularExpressions;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Modules.Patients;

internal static class PatientValidator
{
    private static readonly string[] DoseUnits = ["mg", "mcg", "g", "ml", "iu", "unit", "tablet", "capsule", "puff", "drop", "patch", "sachet", "ampoule"];
    private static readonly string[] Routes = ["oral", "inhalation", "topical", "injection", "sublingual", "rectal", "ophthalmic", "nasal", "other"];

    public static List<string> Profile(UpdateProfileCommand c, int currentYear)
    {
        var errors = new List<string>();
        if (c.YearOfBirth is { } y && (y < 1900 || y > currentYear))
        {
            errors.Add("year_of_birth.range");
        }

        if (c.Sex is { } sex && !Enum.IsDefined(sex))
        {
            errors.Add("sex.invalid");
        }

        if (c.WeightKg is { } w && (w <= 0 || w > 500))
        {
            errors.Add("weight.range");
        }

        if (c.HeightCm is { } h && (h <= 0 || h > 260))
        {
            errors.Add("height.range");
        }

        if (c.TimeZone is { } tz && !IsTimeZone(tz))
        {
            errors.Add("time_zone.invalid");
        }

        return errors;
    }

    public static bool IsTimeZone(string tz) => tz.Length is > 0 and <= 64 && Regex.IsMatch(tz, @"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+){0,2}$");

    public static List<string> Condition(ConditionInput i, DateOnly today)
    {
        var errors = new List<string>();
        AddIf(errors, string.IsNullOrWhiteSpace(i.Name), "name.required");
        AddIf(errors, FreeTextGuard.Problem(i.Name, "name", 200));
        AddIf(errors, FreeTextGuard.Problem(i.Note, "note", 500));
        AddIf(errors, !Enum.IsDefined(i.Status), "status.invalid");
        AddIf(errors, i.OnsetDate is { } d && (d > today || d.Year < 1900), "onset.range");
        return errors;
    }

    public static List<string> Allergy(AllergyInput i)
    {
        var errors = new List<string>();
        AddIf(errors, !Enum.IsDefined(i.Kind), "kind.invalid");
        AddIf(errors, !Enum.IsDefined(i.Severity), "severity.invalid");
        AddIf(errors, i.Kind == AllergenKind.Medication ? i.MedicationId is null && string.IsNullOrWhiteSpace(i.Substance) : string.IsNullOrWhiteSpace(i.Substance), "substance.required");
        AddIf(errors, i.Kind != AllergenKind.Medication && i.MedicationId is not null, "medication_id.not_allowed");
        AddIf(errors, FreeTextGuard.Problem(i.Substance, "substance", 200));
        AddIf(errors, FreeTextGuard.Problem(i.Reaction, "reaction", 300));
        return errors;
    }

    public static List<string> Medication(PatientMedicationInput i, DateOnly today)
    {
        var errors = new List<string>();
        AddIf(errors, i.MedicationId is null == string.IsNullOrWhiteSpace(i.UnregisteredName), i.MedicationId is null ? "medication.required" : "medication.ambiguous"); // exactly one of: reference link, typed name
        AddIf(errors, FreeTextGuard.Problem(i.UnregisteredName, "name", 120));
        AddIf(errors, (i.DoseAmount is null) != (i.DoseUnit is null), "dose.incomplete");
        AddIf(errors, i.DoseAmount is { } a && (a <= 0 || a > 100000), "dose.range");
        AddIf(errors, i.DoseUnit is { } u && !DoseUnits.Contains(u.ToLowerInvariant()), "dose_unit.invalid");
        AddIf(errors, FreeTextGuard.Problem(i.DoseText, "dose_text", 100));
        AddIf(errors, !Enum.IsDefined(i.Frequency), "frequency.invalid");
        AddIf(errors, i.Frequency is FrequencyKind.TimesPerDay && i.FrequencyValue is not (>= 1 and <= 24), "frequency_value.range");
        AddIf(errors, i.Frequency is FrequencyKind.EveryNHours && i.FrequencyValue is not (>= 1 and <= 168), "frequency_value.range");
        AddIf(errors, i.Route is { } r && !Routes.Contains(r.ToLowerInvariant()), "route.invalid");
        AddIf(errors, !Enum.IsDefined(i.Source), "source.invalid");
        AddIf(errors, i.StartDate < today.AddYears(-100) || i.StartDate > today.AddYears(1), "start_date.range");
        AddIf(errors, i.EndDate is { } e && e < i.StartDate, "end_date.before_start");
        AddIf(errors, FreeTextGuard.Problem(i.PrescriberNote, "prescriber_note", 200));
        return errors;
    }

    public static List<string> Symptom(SymptomInput i, DateTimeOffset now)
    {
        var errors = new List<string>();
        AddIf(errors, string.IsNullOrWhiteSpace(i.Text), "text.required");
        AddIf(errors, FreeTextGuard.Problem(i.Text, "text", 200));
        AddIf(errors, FreeTextGuard.Problem(i.Note, "note", 500));
        AddIf(errors, !Enum.IsDefined(i.Severity), "severity.invalid");
        AddIf(errors, i.OnsetAt > now.AddMinutes(5) || i.OnsetAt < now.AddYears(-10), "onset.range");
        AddIf(errors, i.ResolvedAt is { } r && (r < i.OnsetAt || r > now.AddMinutes(5)), "resolved.range");
        return errors;
    }

    public static List<string> Schedule(ScheduleEntryInput i, DateOnly today)
    {
        var errors = new List<string>();
        AddIf(errors, i.Days is { Count: 0 }, "days.empty");
        AddIf(errors, i.Days?.Any(d => !Enum.IsDefined(d)) == true, "days.invalid");
        AddIf(errors, i.StartDate is { } s && s < today.AddYears(-5), "start_date.range");
        AddIf(errors, i.EndDate is { } e && i.StartDate is { } st && e < st, "end_date.before_start");
        return errors;
    }

    public static int DaysToMask(IReadOnlyList<DayOfWeek>? days) => days is null ? 127 : days.Distinct().Aggregate(0, (m, d) => m | (1 << (int)d));

    public static IReadOnlyList<DayOfWeek> MaskToDays(int mask) => [.. Enum.GetValues<DayOfWeek>().Where(d => (mask & (1 << (int)d)) != 0)];

    private static void AddIf(List<string> errors, bool condition, string code)
    {
        if (condition)
        {
            errors.Add(code);
        }
    }

    private static void AddIf(List<string> errors, string? code)
    {
        if (code is not null)
        {
            errors.Add(code);
        }
    }
}
