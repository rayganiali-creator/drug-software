using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Guidance;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Patients;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Patients.Tests;

public class FreeTextGuardTests
{
    [Theory]
    [InlineData("call me on 0912 345 6789", "looks_identifying")]
    [InlineData("تماس: ۰۹۱۲۳۴۵۶۷۸۹", "looks_identifying")] // Persian digits cannot hide a number
    [InlineData("national id 0012345678", "looks_identifying")]
    [InlineData("mail sara@example.invalid", "looks_identifying")]
    [InlineData("see https://example.invalid/x", "looks_identifying")]
    [InlineData("see www.example.invalid", "looks_identifying")]
    public void Text_that_could_identify_a_person_is_refused(string text, string suffix)
    {
        Assert.EndsWith(suffix, FreeTextGuard.Problem(text, "note", 500) ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mild headache after lunch")]
    [InlineData("سردرد خفیف بعد از ناهار")]
    [InlineData("took 2 tablets at 8 and 20")]
    public void Ordinary_text_passes(string text) => Assert.Null(FreeTextGuard.Problem(text, "note", 500));

    [Fact]
    public void Length_and_control_characters_are_limited()
    {
        Assert.Equal("note.too_long", FreeTextGuard.Problem(new string('a', 501), "note", 500));
        Assert.Equal("note.invalid_characters", FreeTextGuard.Problem("a\u0007b", "note", 500));
        Assert.Equal("a b", FreeTextGuard.Clean("  a   b "));
        Assert.Null(FreeTextGuard.Clean("   "));
    }
}

public class ValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    [Fact]
    public void Profile_ranges()
    {
        Assert.Empty(PatientValidator_Profile(new UpdateProfileCommand(1990, SexGroup.Female, 60, 170, "Asia/Tehran", null)));
        Assert.Contains("year_of_birth.range", PatientValidator_Profile(new UpdateProfileCommand(1850, null, null, null, null, null)));
        Assert.Contains("weight.range", PatientValidator_Profile(new UpdateProfileCommand(null, null, 0, null, null, null)));
        Assert.Contains("height.range", PatientValidator_Profile(new UpdateProfileCommand(null, null, null, 400, null, null)));
        Assert.Contains("time_zone.invalid", PatientValidator_Profile(new UpdateProfileCommand(null, null, null, null, "'; drop table", null)));
    }

    private static List<string> PatientValidator_Profile(UpdateProfileCommand c) => ValidatorAccess.Profile(c, 2026);

    [Fact]
    public void A_medication_needs_exactly_one_identity_and_a_complete_dose()
    {
        var ok = new PatientMedicationInput(Guid.NewGuid(), null, 5, "mg", null, FrequencyKind.TimesPerDay, 2, "oral", Today, null, PrescriberSource.Physician, null);
        Assert.Empty(ValidatorAccess.Medication(ok, Today));
        Assert.Contains("medication.ambiguous", ValidatorAccess.Medication(ok with { UnregisteredName = "x" }, Today));
        Assert.Contains("medication.required", ValidatorAccess.Medication(ok with { MedicationId = null }, Today));
        Assert.Contains("dose.incomplete", ValidatorAccess.Medication(ok with { DoseUnit = null }, Today));
        Assert.Contains("dose_unit.invalid", ValidatorAccess.Medication(ok with { DoseUnit = "bucket" }, Today));
        Assert.Contains("frequency_value.range", ValidatorAccess.Medication(ok with { FrequencyValue = null }, Today));
        Assert.Contains("end_date.before_start", ValidatorAccess.Medication(ok with { EndDate = Today.AddDays(-1) }, Today));
        Assert.Contains("route.invalid", ValidatorAccess.Medication(ok with { Route = "telepathy" }, Today));
    }

    [Fact]
    public void Schedule_days_round_trip_through_the_mask()
    {
        Assert.Equal(127, ValidatorAccess.DaysToMask(null));
        var mask = ValidatorAccess.DaysToMask([DayOfWeek.Monday, DayOfWeek.Friday]);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], ValidatorAccess.MaskToDays(mask));
        Assert.Contains("days.empty", ValidatorAccess.Schedule(new ScheduleEntryInput(new TimeOnly(8, 0), [], null, null), Today));
    }
}

public class GtinAndStateMachineTests
{
    [Fact]
    public void Gtin_check_digits_are_verified_but_never_invented()
    {
        var valid = PEnv.Gtin13("999000000001"); // a fictional number built for the test
        Assert.True(ProductSupportAccess.IsValidGtin(valid));
        Assert.False(ProductSupportAccess.IsValidGtin(valid[..12] + (char)('0' + (((valid[12] - '0') + 1) % 10))));
        Assert.False(ProductSupportAccess.IsValidGtin("123"));
        Assert.False(ProductSupportAccess.IsValidGtin("12345678901ab"));
    }

    [Fact]
    public void Report_status_machine_allows_only_the_documented_moves()
    {
        var allowed = new HashSet<(ReportStatus, ReportStatus)>
        {
            (ReportStatus.Draft, ReportStatus.PendingConsentOrReview), (ReportStatus.Draft, ReportStatus.ReadyToSend), (ReportStatus.Draft, ReportStatus.Cancelled),
            (ReportStatus.PendingConsentOrReview, ReportStatus.ReadyToSend), (ReportStatus.PendingConsentOrReview, ReportStatus.Draft), (ReportStatus.PendingConsentOrReview, ReportStatus.Cancelled),
            (ReportStatus.ReadyToSend, ReportStatus.Sent), (ReportStatus.ReadyToSend, ReportStatus.Acknowledged), (ReportStatus.ReadyToSend, ReportStatus.Failed),
            (ReportStatus.ReadyToSend, ReportStatus.PendingConsentOrReview), (ReportStatus.ReadyToSend, ReportStatus.Cancelled),
            (ReportStatus.Sent, ReportStatus.Acknowledged), (ReportStatus.Sent, ReportStatus.Failed),
            (ReportStatus.Failed, ReportStatus.ReadyToSend), (ReportStatus.Failed, ReportStatus.Cancelled),
        };
        foreach (var from in Enum.GetValues<ReportStatus>())
        {
            foreach (var to in Enum.GetValues<ReportStatus>())
            {
                Assert.Equal(allowed.Contains((from, to)), ReportStateMachine.Allowed(from, to));
            }
        }
    }

    [Fact]
    public void Acknowledged_and_cancelled_reports_are_final()
    {
        foreach (var to in Enum.GetValues<ReportStatus>())
        {
            Assert.False(ReportStateMachine.Allowed(ReportStatus.Acknowledged, to));
            Assert.False(ReportStateMachine.Allowed(ReportStatus.Cancelled, to));
        }
    }

    [Fact]
    public void Guidance_status_moves_respect_who_may_make_them()
    {
        Assert.True(GuidanceService.MoveAllowed(GuidanceStatus.Sent, GuidanceStatus.Seen, false));
        Assert.True(GuidanceService.MoveAllowed(GuidanceStatus.Seen, GuidanceStatus.Resolved, false));
        Assert.False(GuidanceService.MoveAllowed(GuidanceStatus.Seen, GuidanceStatus.Reviewed, false)); // patients do not review
        Assert.True(GuidanceService.MoveAllowed(GuidanceStatus.Seen, GuidanceStatus.Reviewed, true));
        Assert.False(GuidanceService.MoveAllowed(GuidanceStatus.Seen, GuidanceStatus.Referred, true)); // review comes first
        Assert.True(GuidanceService.MoveAllowed(GuidanceStatus.Reviewed, GuidanceStatus.Referred, true));
        Assert.False(GuidanceService.MoveAllowed(GuidanceStatus.Resolved, GuidanceStatus.Seen, true)); // resolved is final
        Assert.False(GuidanceService.MoveAllowed(GuidanceStatus.Sent, GuidanceStatus.Sent, true));
    }
}

public class AgeBandTests
{
    [Theory]
    [InlineData(2015, "0-17")]
    [InlineData(2000, "18-39")]
    [InlineData(1980, "40-64")]
    [InlineData(1950, "65+")]
    [InlineData(2040, "unknown")]
    public void Age_bands_hide_the_birth_year(int year, string band) => Assert.Equal(band, AgeBands.Of(year, 2026));

    [Fact]
    public void Unknown_year_is_unknown() => Assert.Equal("unknown", AgeBands.Of(null, 2026));
}
