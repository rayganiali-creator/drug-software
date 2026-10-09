using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Patients.Tests;

public class MedicationServiceTests
{
    private static PatientMedicationInput Unregistered(DateOnly start, string name = "Herbal tonic (typed)") =>
        new(null, name, null, null, "1 spoon", FrequencyKind.AsNeeded, null, "oral", start, null, PrescriberSource.SelfReported, null);

    [Fact]
    public async Task A_registered_medicine_links_to_the_reference_and_shows_the_reference_name()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id, "nocturin");
        Assert.True(med.IsRegistered);
        Assert.True(med.ReferenceIsDemo);
        Assert.Equal("Nocturin", med.DisplayName);
        Assert.Equal("نوکتورین", med.ReferenceName!.Fa);
        Assert.Equal(1, med.Version);
        Assert.Single((await env.Meds.ListAsync(id, false)).Value!);
    }

    [Fact]
    public async Task A_medicine_that_is_not_in_the_reference_stays_unregistered_and_is_never_guessed()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var typed = (await env.Meds.AddAsync(id, id, Unregistered(env.Today), "t", null)).Value!;
        Assert.False(typed.IsRegistered);
        Assert.Null(typed.MedicationId);
        Assert.Null(typed.ReferenceName);
        Assert.Equal("Herbal tonic (typed)", typed.DisplayName);
        // a name that equals a reference medicine is still not auto-linked
        var lookalike = (await env.Meds.AddAsync(id, id, Unregistered(env.Today, "Nocturin"), "t", null)).Value!;
        Assert.False(lookalike.IsRegistered);
        Assert.Equal(PatientError.Validation, (await env.Meds.AddAsync(id, id, new PatientMedicationInput(Guid.NewGuid(), null, null, null, null, FrequencyKind.AsNeeded, null, null, env.Today, null, PrescriberSource.Unknown, null), "t", null)).Error);
    }

    [Fact]
    public async Task Inactive_or_draft_reference_medicines_cannot_be_linked()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var inactive = (await env.Reference.SearchAsync(new MedSmarter.Modules.Medications.Contracts.MedicationSearchQuery("oldmed", 5, 0, true))).Items[0].Id;
        var r = await env.Meds.AddAsync(id, id, new PatientMedicationInput(inactive, null, null, null, null, FrequencyKind.AsNeeded, null, null, env.Today, null, PrescriberSource.Unknown, null), "t", null);
        Assert.Contains("medication.unknown", r.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_medicine_and_dose_cannot_be_active_twice()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.TakeMedication(id);
        var again = await env.Meds.AddAsync(id, id, new PatientMedicationInput(await env.Reference_("demopril"), null, 5, "mg", null, FrequencyKind.TimesPerDay, 1, "oral", env.Today, null, PrescriberSource.Physician, null), "t", null);
        Assert.Equal(PatientError.Conflict, again.Error);
        Assert.Equal("medication.duplicate", again.Detail);
    }

    [Fact]
    public async Task Changes_keep_a_version_history_and_need_a_reason()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        var input = new PatientMedicationInput(med.MedicationId, null, 10, "mg", null, FrequencyKind.TimesPerDay, 2, "oral", med.StartDate, null, PrescriberSource.Physician, "dose changed by prescriber", med.Version);
        Assert.Contains("reason.required", (await env.Meds.UpdateAsync(id, id, med.Id, input, " ", "t", null)).Detail, StringComparison.Ordinal);
        var updated = await env.Meds.UpdateAsync(id, id, med.Id, input, "dose changed after the visit", "t", null);
        Assert.True(updated.Succeeded, updated.Detail);
        Assert.Equal(10m, updated.Value!.DoseAmount);
        Assert.Equal(2, updated.Value.Version);
        Assert.Equal(PatientError.Conflict, (await env.Meds.UpdateAsync(id, id, med.Id, input with { ExpectedVersion = 1 }, "stale", "t", null)).Error);

        var versions = (await env.Meds.VersionsAsync(id, med.Id)).Value!;
        Assert.Equal(["created", "dose changed after the visit"], versions.Select(v => v.Reason));
        Assert.All(versions, v => Assert.Equal(id, v.ChangedBy));
    }

    [Fact]
    public async Task Stopping_and_resuming_are_recorded_and_stopped_medicines_leave_the_active_list()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        var stopped = await env.Meds.StopAsync(id, id, med.Id, new StopMedicationCommand("finished the course", null, med.Version), "t", null);
        Assert.Equal(PatientMedicationStatus.Stopped, stopped.Value!.Status);
        Assert.Equal(env.Today, stopped.Value.EndDate);
        Assert.Empty((await env.Meds.ListAsync(id, false)).Value!);
        Assert.Single((await env.Meds.ListAsync(id, true)).Value!);
        Assert.Equal(PatientError.Conflict, (await env.Meds.StopAsync(id, id, med.Id, new StopMedicationCommand(null, null, stopped.Value.Version), "t", null)).Error); // already stopped

        var resumed = await env.Meds.ResumeAsync(id, id, med.Id, stopped.Value.Version, "t", null);
        Assert.Equal(PatientMedicationStatus.Active, resumed.Value!.Status);
        Assert.Null(resumed.Value.EndDate);
        Assert.Equal(["created", "stopped", "resumed"], (await env.Meds.VersionsAsync(id, med.Id)).Value!.Select(v => v.Reason));
    }

    [Fact]
    public async Task Removing_a_medicine_is_a_soft_delete_that_keeps_its_history()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        Assert.True((await env.Meds.RemoveAsync(id, id, med.Id, "t", null)).Succeeded);
        Assert.Equal(PatientError.NotFound, (await env.Meds.GetAsync(id, med.Id)).Error);
        Assert.Empty((await env.Meds.ListAsync(id, true)).Value!);
    }

    [Fact]
    public async Task Another_patients_medicine_id_is_never_reachable()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = await env.Patient("demo-patient-2");
        var med = await env.TakeMedication(a);
        Assert.Equal(PatientError.NotFound, (await env.Meds.GetAsync(b, med.Id)).Error);
        Assert.Equal(PatientError.NotFound, (await env.Meds.StopAsync(b, b, med.Id, new StopMedicationCommand(null, null, 1), "t", null)).Error);
        Assert.Equal(PatientError.NotFound, (await env.Meds.RemoveAsync(b, b, med.Id, "t", null)).Error);
        Assert.Equal(PatientError.NotFound, (await env.Meds.AddScheduleEntryAsync(b, b, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null)).Error);
        Assert.Equal(PatientError.NotFound, (await env.Meds.VersionsAsync(b, med.Id)).Error);
        Assert.Equal(PatientMedicationStatus.Active, (await env.Meds.GetAsync(a, med.Id)).Value!.Status);
    }

    [Fact]
    public async Task A_schedule_lists_the_days_planned_doses_with_what_was_logged()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        var entry = (await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, env.Today.AddDays(-3), null), "t", null)).Value!;
        Assert.Equal(7, entry.Days.Count);
        Assert.Equal(PatientError.Conflict, (await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null)).Error);
        await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(20, 30), [env.Today.DayOfWeek], null, null), "t", null);

        var slots = (await env.Meds.GetDayAsync(id, env.Today)).Value!;
        Assert.Equal(2, slots.Count);
        Assert.Equal(new TimeOnly(8, 0), slots[0].LocalTime);
        Assert.Equal("Demopril", slots[0].MedicationName);
        Assert.Null(slots[0].Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 4, 30, 0, TimeSpan.Zero), slots[0].ScheduledFor); // 08:00 Tehran = 04:30 UTC (UTC+03:30, no DST)

        var other = env.Today.AddDays(1);
        Assert.Single((await env.Meds.GetDayAsync(id, other)).Value!); // the second entry is for today's weekday only
        Assert.Empty((await env.Meds.GetDayAsync(id, env.Today.AddDays(-30))).Value!); // before the medicine started
    }

    [Fact]
    public async Task Logging_a_dose_is_idempotent_and_validated()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        var entry = (await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null)).Value!;
        env.Clock.Advance(TimeSpan.FromHours(6)); // 14:00: the 08:00 dose is in the past
        var slot = (await env.Meds.GetDayAsync(id, env.Today)).Value!.Single();

        var taken = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Taken, slot.ScheduledFor.AddMinutes(12), null), "t", null);
        Assert.True(taken.Succeeded, taken.Detail);
        var again = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Skipped, null, "felt sick"), "t", null);
        Assert.Equal(taken.Value!.Id, again.Value!.Id); // same dose, same log: updated, not duplicated
        Assert.Equal(IntakeStatus.Skipped, again.Value.Status);
        Assert.Single((await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow.AddDays(-1), env.Clock.UtcNow.AddMinutes(1))).Value!);
        Assert.Equal(IntakeStatus.Skipped, (await env.Meds.GetDayAsync(id, env.Today)).Value!.Single().Status);

        Assert.Contains("scheduled_for.not_a_planned_dose", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor.AddMinutes(7), IntakeStatus.Taken, null, null), "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("taken_at.range", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Taken, env.Clock.UtcNow.AddHours(3), null), "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("taken_at.not_allowed", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Skipped, env.Clock.UtcNow, null), "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("schedule_entry.required", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, slot.ScheduledFor, IntakeStatus.Taken, null, null), "t", null)).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_as_needed_dose_can_be_logged_without_a_schedule()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = (await env.Meds.AddAsync(id, id, Unregistered(env.Today), "t", null)).Value!;
        var log = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Taken, env.Clock.UtcNow.AddMinutes(-5), "after lunch"), "t", null);
        Assert.True(log.Succeeded);
        Assert.Null(log.Value!.ScheduleEntryId);
        var second = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Taken, env.Clock.UtcNow, null), "t", null);
        Assert.NotEqual(log.Value.Id, second.Value!.Id); // ad-hoc doses are separate events
    }

    [Fact]
    public async Task A_stopped_medicine_has_no_planned_doses_and_no_new_schedule()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.TakeMedication(id);
        await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null);
        await env.Meds.StopAsync(id, id, med.Id, new StopMedicationCommand(null, null, med.Version), "t", null);
        Assert.Empty((await env.Meds.GetDayAsync(id, env.Today)).Value!);
        Assert.Contains("medication.stopped", (await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(9, 0), null, null, null), "t", null)).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intake_ranges_are_bounded()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        Assert.Equal(PatientError.Validation, (await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow.AddDays(-400), env.Clock.UtcNow)).Error);
        Assert.Equal(PatientError.Validation, (await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow, env.Clock.UtcNow.AddDays(-1))).Error);
    }
}
