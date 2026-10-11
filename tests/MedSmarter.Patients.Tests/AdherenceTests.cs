using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 8 adherence: patient-reported, tied to a defined schedule, idempotent, never inferred.</summary>
public class AdherenceTests
{
    private static async Task<(Guid Id, PatientMedicationDto Med, ScheduleEntryDto Entry)> Setup(PEnv env, bool withDose = true)
    {
        var id = await env.Patient("demo-patient");
        PatientMedicationDto med;
        if (withDose)
        {
            med = await env.TakeMedication(id);
        }
        else
        {
            med = (await env.Meds.AddAsync(id, id, new PatientMedicationInput(null, "Unknownol", null, null, null, FrequencyKind.TimesPerDay, 1, null, env.Today.AddDays(-10), null, PrescriberSource.SelfReported, null), "t", null)).Value!;
        }

        var entry = (await env.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null)).Value!;
        env.Clock.Advance(TimeSpan.FromHours(6));
        return (id, med, entry);
    }

    private static async Task<DoseSlotDto> Slot(PEnv env, Guid id) => (await env.Meds.GetDayAsync(id, env.Today)).Value!.Single();

    [Fact]
    public async Task A_planned_dose_can_be_reported_missed_with_source_and_provenance_and_is_not_a_taken_dose()
    {
        using var env = new PEnv();
        var (id, med, entry) = await Setup(env);
        var slot = await Slot(env, id);
        var r = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Missed, null, null), "android", null);
        Assert.True(r.Succeeded, r.Detail);
        Assert.Equal(IntakeStatus.Missed, r.Value!.Status);
        Assert.Equal("android", r.Value.Source);
        Assert.Equal("PatientReported", r.Value.Provenance);
        Assert.Null(r.Value.TakenAt);
        Assert.False(r.Value.Unplanned);
        Assert.Equal(IntakeStatus.Missed, (await Slot(env, id)).Status);
    }

    [Fact]
    public async Task Missed_and_skipped_need_a_planned_dose_that_has_already_come_and_no_taken_time()
    {
        using var env = new PEnv();
        var (id, med, entry) = await Setup(env);
        var slot = await Slot(env, id);
        Assert.Contains("schedule_entry.required", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Missed, null, null), "t", null)).Detail!, StringComparison.Ordinal);
        Assert.Contains("schedule_entry.required", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Skipped, null, null), "t", null)).Detail!, StringComparison.Ordinal);
        Assert.Contains("taken_at.not_allowed", (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Missed, env.Clock.UtcNow, null), "t", null)).Detail!, StringComparison.Ordinal);

        env.Clock.Advance(TimeSpan.FromHours(-10)); // 04:00 UTC: the 08:00 Tehran dose (04:30 UTC) has not come yet
        var early = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Missed, null, null), "t", null);
        Assert.Contains("scheduled_for.in_future", early.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retry_of_the_same_report_changes_nothing_and_writes_no_new_version_or_audit_line()
    {
        using var env = new PEnv();
        var (id, med, entry) = await Setup(env);
        var slot = await Slot(env, id);
        var cmd = new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Taken, slot.ScheduledFor.AddMinutes(5), "with water");
        var first = (await env.Meds.LogIntakeAsync(id, id, cmd, "web", null)).Value!;
        var audits = (await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Action: AuditActions.PatientDataUpdated, Take: 500))).Count;
        env.Clock.Advance(TimeSpan.FromMinutes(3));

        var retry = (await env.Meds.LogIntakeAsync(id, id, cmd, "android", null)).Value!;
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.RecordedAt, retry.RecordedAt); // the timestamp does not move
        Assert.Equal("web", retry.Source); // nor does the original source
        Assert.Equal(audits, (await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Action: AuditActions.PatientDataUpdated, Take: 500))).Count);
        Assert.Single((await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow.AddDays(-1), env.Clock.UtcNow)).Value!);

        // a real correction (taken -> missed) is accepted, versioned and audited
        var corrected = (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Missed, null, null), "web", null)).Value!;
        Assert.Equal(first.Id, corrected.Id);
        Assert.Equal(IntakeStatus.Missed, corrected.Status);
        Assert.Single((await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow.AddDays(-1), env.Clock.UtcNow)).Value!);
    }

    [Fact]
    public async Task A_dose_with_an_unknown_amount_cannot_be_recorded_and_the_gap_is_listed_not_guessed()
    {
        using var env = new PEnv();
        var (id, med, entry) = await Setup(env, withDose: false);
        var slot = await Slot(env, id);
        var r = await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Taken, null, null), "t", null);
        Assert.Equal(PatientError.Validation, r.Error);
        Assert.Contains("instructions.dose_unknown", r.Detail!, StringComparison.Ordinal);

        var summary = (await env.Meds.GetAdherenceSummaryAsync(id, 7)).Value!;
        var m = Assert.Single(summary.Medications);
        Assert.False(m.RecordingAllowed);
        Assert.Contains("dose.unknown", m.InstructionGaps);
        Assert.Contains("route.unknown", m.InstructionGaps);
        Assert.Contains("medication.not_in_reference", m.InstructionGaps);
        Assert.Contains("source.self_reported", m.InstructionGaps);
        Assert.Equal(0, m.Taken + m.Skipped + m.Missed);
    }

    [Fact]
    public async Task The_summary_counts_only_what_was_reported_and_silence_is_not_missed()
    {
        using var env = new PEnv();
        var (id, med, entry) = await Setup(env);
        var slot = await Slot(env, id);
        await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, entry.Id, slot.ScheduledFor, IntakeStatus.Taken, null, null), "t", null);
        env.Clock.Advance(TimeSpan.FromDays(2)); // two more planned doses pass with no record
        var s = (await env.Meds.GetAdherenceSummaryAsync(id, 7)).Value!;
        var m = Assert.Single(s.Medications);
        Assert.Equal("PatientReported", s.Provenance);
        Assert.Equal(1, m.Taken);
        Assert.Equal(0, m.Missed);
        Assert.Equal(2, m.NotRecorded);
        Assert.Equal(3, m.PlannedDoses);
        Assert.Contains("not counted as missed", s.Notice, StringComparison.Ordinal);
        Assert.Contains("not verified", s.Notice, StringComparison.Ordinal);
        Assert.Equal(PatientError.Validation, (await env.Meds.GetAdherenceSummaryAsync(id, 0)).Error);
        Assert.Equal(PatientError.Validation, (await env.Meds.GetAdherenceSummaryAsync(id, 99)).Error);
    }

    [Fact]
    public async Task Unplanned_intake_is_flagged_and_other_patients_see_nothing()
    {
        using var env = new PEnv();
        var (id, med, _) = await Setup(env);
        var extra = (await env.Meds.LogIntakeAsync(id, id, new LogIntakeCommand(med.Id, null, null, IntakeStatus.Taken, env.Clock.UtcNow.AddMinutes(-5), null), "ios", null)).Value!;
        Assert.True(extra.Unplanned);
        var other = await env.Patient("demo-patient-2");
        Assert.Empty((await env.Meds.GetAdherenceSummaryAsync(other, 7)).Value!.Medications);
    }

    [Fact]
    public async Task Nothing_in_the_adherence_flow_advances_a_dose_on_its_own()
    {
        // No reminder, delivery, assessment or message ever writes an intake record: only LogIntakeAsync does, and only for an explicit patient report.
        using var env = new PEnv();
        var (id, _, _) = await Setup(env);
        Assert.Null((await Slot(env, id)).Status);
        await env.Safety.AssessAsync(id, id, new MedSmarter.Modules.ClinicalRules.Contracts.AssessCommand(["medications", "allergies", "symptoms", "profile"]), "t", null);
        Assert.Null((await Slot(env, id)).Status);
        Assert.Empty((await env.Meds.ListIntakeAsync(id, env.Clock.UtcNow.AddDays(-1), env.Clock.UtcNow)).Value!);
    }
}
