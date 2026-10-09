using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Patients;

public sealed class PatientMedicationService(IDbContextFactory<PatientsDbContext> factory, IClock clock, IAuditWriter audit, IMedicationService reference) : IPatientMedicationService
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ---------- reading ----------

    public async Task<PatientOutcome<IReadOnlyList<PatientMedicationDto>>> ListAsync(Guid subjectId, bool includeStopped, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<PatientMedicationDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = await db.Medications.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null && (includeStopped || x.Status != PatientMedicationStatus.Stopped)).ToListAsync(ct);
        return PatientOutcome.Ok(await ToDtosAsync(rows, ct));
    }

    public async Task<PatientOutcome<PatientMedicationDto>> GetAsync(Guid subjectId, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.NotFound, "patient.not_found");
        }

        var row = await db.Medications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient.Id && x.DeletedAt == null, ct);
        return row is null ? PatientOutcome.Fail<PatientMedicationDto>(PatientError.NotFound, "medication.not_found") : PatientOutcome.Ok((await ToDtosAsync([row], ct))[0]);
    }

    public async Task<PatientOutcome<IReadOnlyList<RecordVersionDto>>> VersionsAsync(Guid subjectId, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null || !await db.Medications.AnyAsync(x => x.Id == id && x.PatientId == patient.Id, ct))
        {
            return PatientOutcome.Fail<IReadOnlyList<RecordVersionDto>>(PatientError.NotFound, "medication.not_found");
        }

        var rows = await db.RecordVersions.AsNoTracking().Where(x => x.RecordType == "medication" && x.RecordId == id && x.PatientId == patient.Id).OrderBy(x => x.VersionNumber).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<RecordVersionDto>>([.. rows.Select(r => new RecordVersionDto(r.VersionNumber, r.ChangedAt, r.ChangedBy, r.Reason))]);
    }

    // ---------- medication changes ----------

    public async Task<PatientOutcome<PatientMedicationDto>> AddAsync(Guid actorUserId, Guid subjectId, PatientMedicationInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Medication(input, Today);
        if (errors.Count > 0)
        {
            return Support.Invalid<PatientMedicationDto>(errors);
        }

        if (input.MedicationId is { } linked && !(await reference.GetAsync(linked, false, ct)).Succeeded)
        {
            return Support.Invalid<PatientMedicationDto>(["medication.unknown"]); // never guess a match: either a real reference id or an explicitly unregistered name
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<PatientMedicationDto>(patient) is { } problem)
        {
            return problem;
        }

        var duplicate = await db.Medications.AnyAsync(x => x.PatientId == patient!.Id && x.DeletedAt == null && x.Status == PatientMedicationStatus.Active && x.MedicationId == input.MedicationId && x.MedicationId != null
            && x.DoseAmount == input.DoseAmount && x.DoseUnit == input.DoseUnit, ct);
        if (duplicate)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.Conflict, "medication.duplicate");
        }

        var now = clock.UtcNow;
        var row = new MedicationRecord
        {
            Id = Guid.CreateVersion7(), PatientId = patient!.Id, CreatedAt = now, UpdatedAt = now, UpdatedBy = actorUserId, Version = 1, Status = PatientMedicationStatus.Active,
        };
        Apply(row, input);
        db.Medications.Add(row);
        Support.AddVersion(db, patient.Id, "medication", row.Id, 1, Snapshot(row), now, actorUserId, "created");
        await db.SaveChangesAsync(ct);
        await TouchMedicationsAsync(db, patient.Id, actorUserId, now, ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "patient-medication", row.Id, subjectId, source, correlationId, "created");
        return PatientOutcome.Ok((await ToDtosAsync([row], ct))[0]);
    }

    public async Task<PatientOutcome<PatientMedicationDto>> UpdateAsync(Guid actorUserId, Guid subjectId, Guid id, PatientMedicationInput input, string reason, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Medication(input, Today);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 300)
        {
            errors.Add("reason.required");
        }
        else if (FreeTextGuard.Problem(reason, "reason", 300) is { } reasonProblem)
        {
            errors.Add(reasonProblem);
        }

        if (errors.Count > 0)
        {
            return Support.Invalid<PatientMedicationDto>(errors);
        }

        if (input.MedicationId is { } linked && !(await reference.GetAsync(linked, false, ct)).Succeeded)
        {
            return Support.Invalid<PatientMedicationDto>(["medication.unknown"]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<PatientMedicationDto>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Medications.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.NotFound, "medication.not_found");
        }

        if (input.ExpectedVersion != row.Version)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.Conflict, "version.mismatch");
        }

        var now = clock.UtcNow;
        Apply(row, input);
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        Support.AddVersion(db, patient!.Id, "medication", row.Id, row.Version, Snapshot(row), now, actorUserId, FreeTextGuard.Clean(reason)!);
        if (!await TrySaveAsync(db, ct))
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.Conflict, "version.mismatch");
        }

        await TouchMedicationsAsync(db, patient.Id, actorUserId, now, ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "patient-medication", row.Id, subjectId, source, correlationId, "updated");
        return PatientOutcome.Ok((await ToDtosAsync([row], ct))[0]);
    }

    public Task<PatientOutcome<PatientMedicationDto>> StopAsync(Guid actorUserId, Guid subjectId, Guid id, StopMedicationCommand command, string source, string? correlationId, CancellationToken ct = default) =>
        ChangeStatusAsync(actorUserId, subjectId, id, command.ExpectedVersion, PatientMedicationStatus.Stopped, command.Reason, command.EndDate, source, correlationId, ct);

    public Task<PatientOutcome<PatientMedicationDto>> ResumeAsync(Guid actorUserId, Guid subjectId, Guid id, int expectedVersion, string source, string? correlationId, CancellationToken ct = default) =>
        ChangeStatusAsync(actorUserId, subjectId, id, expectedVersion, PatientMedicationStatus.Active, null, null, source, correlationId, ct);

    private async Task<PatientOutcome<PatientMedicationDto>> ChangeStatusAsync(Guid actorUserId, Guid subjectId, Guid id, int expectedVersion, PatientMedicationStatus status, string? reason, DateOnly? endDate, string source, string? correlationId, CancellationToken ct)
    {
        if (FreeTextGuard.Problem(reason, "reason", 200) is { } reasonProblem)
        {
            return Support.Invalid<PatientMedicationDto>([reasonProblem]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<PatientMedicationDto>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Medications.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.NotFound, "medication.not_found");
        }

        if (expectedVersion != row.Version)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.Conflict, "version.mismatch");
        }

        if (row.Status == status)
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.Conflict, "status.unchanged");
        }

        if (status == PatientMedicationStatus.Stopped && endDate is { } e && e < row.StartDate)
        {
            return Support.Invalid<PatientMedicationDto>(["end_date.before_start"]);
        }

        var now = clock.UtcNow;
        row.Status = status;
        row.StopReason = status == PatientMedicationStatus.Stopped ? FreeTextGuard.Clean(reason) : null;
        row.EndDate = status == PatientMedicationStatus.Stopped ? endDate ?? Today : null;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        Support.AddVersion(db, patient!.Id, "medication", row.Id, row.Version, Snapshot(row), now, actorUserId, status == PatientMedicationStatus.Stopped ? "stopped" : "resumed");
        if (!await TrySaveAsync(db, ct))
        {
            return PatientOutcome.Fail<PatientMedicationDto>(PatientError.Conflict, "version.mismatch");
        }

        await TouchMedicationsAsync(db, patient.Id, actorUserId, now, ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "patient-medication", row.Id, subjectId, source, correlationId, status == PatientMedicationStatus.Stopped ? "stopped" : "resumed");
        return PatientOutcome.Ok((await ToDtosAsync([row], ct))[0]);
    }

    public async Task<PatientOutcome<bool>> RemoveAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<bool>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Medications.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<bool>(PatientError.NotFound, "medication.not_found");
        }

        var now = clock.UtcNow;
        row.DeletedAt = now;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        foreach (var entry in await db.ScheduleEntries.Where(x => x.PatientMedicationId == id && x.DeletedAt == null).ToListAsync(ct))
        {
            entry.DeletedAt = now;
        }

        Support.AddVersion(db, patient!.Id, "medication", row.Id, row.Version, Snapshot(row), now, actorUserId, "removed");
        await db.SaveChangesAsync(ct);
        await TouchMedicationsAsync(db, patient.Id, actorUserId, now, ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "patient-medication", row.Id, subjectId, source, correlationId, "removed");
        return PatientOutcome.Ok(true);
    }

    // ---------- schedule ----------

    public async Task<PatientOutcome<IReadOnlyList<ScheduleEntryDto>>> ListScheduleAsync(Guid subjectId, Guid medicationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null || !await db.Medications.AnyAsync(x => x.Id == medicationId && x.PatientId == patient.Id && x.DeletedAt == null, ct))
        {
            return PatientOutcome.Fail<IReadOnlyList<ScheduleEntryDto>>(PatientError.NotFound, "medication.not_found");
        }

        var rows = await db.ScheduleEntries.AsNoTracking().Where(x => x.PatientMedicationId == medicationId && x.PatientId == patient.Id && x.DeletedAt == null).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<ScheduleEntryDto>>([.. rows.OrderBy(x => x.TimeOfDay).Select(ToDto)]);
    }

    public async Task<PatientOutcome<ScheduleEntryDto>> AddScheduleEntryAsync(Guid actorUserId, Guid subjectId, Guid medicationId, ScheduleEntryInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Schedule(input, Today);
        if (errors.Count > 0)
        {
            return Support.Invalid<ScheduleEntryDto>(errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<ScheduleEntryDto>(patient) is { } problem)
        {
            return problem;
        }

        var med = await db.Medications.FirstOrDefaultAsync(x => x.Id == medicationId && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (med is null)
        {
            return PatientOutcome.Fail<ScheduleEntryDto>(PatientError.NotFound, "medication.not_found");
        }

        if (med.Status == PatientMedicationStatus.Stopped)
        {
            return Support.Invalid<ScheduleEntryDto>(["medication.stopped"]);
        }

        var mask = PatientValidator.DaysToMask(input.Days);
        var minute = new TimeOnly(input.TimeOfDay.Hour, input.TimeOfDay.Minute);
        if (await db.ScheduleEntries.AnyAsync(x => x.PatientMedicationId == medicationId && x.DeletedAt == null && x.TimeOfDay == minute && x.DaysMask == mask, ct))
        {
            return PatientOutcome.Fail<ScheduleEntryDto>(PatientError.Conflict, "schedule.duplicate");
        }

        var now = clock.UtcNow;
        var row = new ScheduleEntryRecord { Id = Guid.CreateVersion7(), PatientId = patient!.Id, PatientMedicationId = medicationId, TimeOfDay = minute, DaysMask = mask, StartDate = input.StartDate ?? Today, EndDate = input.EndDate, CreatedAt = now };
        db.ScheduleEntries.Add(row);
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Schedule, actorUserId, now, await db.ScheduleEntries.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "schedule-entry", row.Id, subjectId, source, correlationId, "created");
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<bool>> RemoveScheduleEntryAsync(Guid actorUserId, Guid subjectId, Guid entryId, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<bool>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.ScheduleEntries.FirstOrDefaultAsync(x => x.Id == entryId && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<bool>(PatientError.NotFound, "schedule.not_found");
        }

        row.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient!.Id, PatientDataCategory.Schedule, actorUserId, clock.UtcNow, await db.ScheduleEntries.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "schedule-entry", row.Id, subjectId, source, correlationId, "removed");
        return PatientOutcome.Ok(true);
    }

    public async Task<PatientOutcome<IReadOnlyList<DoseSlotDto>>> GetDayAsync(Guid subjectId, DateOnly localDate, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<DoseSlotDto>>(PatientError.NotFound, "patient.not_found");
        }

        var zone = await ZoneAsync(db, patient.Id, ct);
        var meds = (await db.Medications.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null && x.Status == PatientMedicationStatus.Active).ToListAsync(ct)).ToDictionary(x => x.Id);
        var entries = await db.ScheduleEntries.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null).ToListAsync(ct);
        var names = (await ToDtosAsync([.. meds.Values], ct)).ToDictionary(d => d.Id, d => d.ReferenceName?.En ?? d.ReferenceName?.Fa ?? d.DisplayName);
        var slots = new List<(ScheduleEntryRecord Entry, DateTimeOffset At)>();
        foreach (var e in entries.Where(e => meds.ContainsKey(e.PatientMedicationId) && ActiveOn(e, meds[e.PatientMedicationId], localDate)))
        {
            slots.Add((e, ToUtc(localDate, e.TimeOfDay, zone)));
        }

        var entryIds = slots.Select(s => s.Entry.Id).ToList();
        var logs = (await db.IntakeLogs.AsNoTracking().Where(x => x.ScheduleEntryId != null && entryIds.Contains(x.ScheduleEntryId.Value)).ToListAsync(ct))
            .Where(l => l.ScheduledFor is not null).ToLookup(l => (l.ScheduleEntryId!.Value, l.ScheduledFor!.Value));
        return PatientOutcome.Ok<IReadOnlyList<DoseSlotDto>>([.. slots.OrderBy(s => s.At).Select(s =>
        {
            var log = logs[(s.Entry.Id, s.At)].FirstOrDefault();
            return new DoseSlotDto(s.Entry.Id, s.Entry.PatientMedicationId, names[s.Entry.PatientMedicationId], localDate, s.Entry.TimeOfDay, s.At, log?.Status, log?.Id);
        })]);
    }

    // ---------- intake ----------

    public async Task<PatientOutcome<IntakeLogDto>> LogIntakeAsync(Guid actorUserId, Guid subjectId, LogIntakeCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var errors = new List<string>();
        if (!Enum.IsDefined(command.Status))
        {
            errors.Add("status.invalid");
        }

        if (command.Status == IntakeStatus.Skipped && command.TakenAt is not null)
        {
            errors.Add("taken_at.not_allowed");
        }

        if (command.TakenAt is { } taken && (taken > now.AddMinutes(5) || taken < now.AddDays(-31)))
        {
            errors.Add("taken_at.range");
        }

        if (command.ScheduleEntryId is not null && command.ScheduledFor is null)
        {
            errors.Add("scheduled_for.required");
        }

        if (command.ScheduleEntryId is null && command.ScheduledFor is not null)
        {
            errors.Add("schedule_entry.required");
        }

        if (FreeTextGuard.Problem(command.Note, "note", 300) is { } noteProblem)
        {
            errors.Add(noteProblem);
        }

        if (errors.Count > 0)
        {
            return Support.Invalid<IntakeLogDto>(errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<IntakeLogDto>(patient) is { } problem)
        {
            return problem;
        }

        var med = await db.Medications.FirstOrDefaultAsync(x => x.Id == command.PatientMedicationId && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (med is null)
        {
            return PatientOutcome.Fail<IntakeLogDto>(PatientError.NotFound, "medication.not_found");
        }

        if (command.ScheduleEntryId is { } entryId)
        {
            var entry = await db.ScheduleEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entryId && x.PatientId == patient!.Id && x.PatientMedicationId == med.Id && x.DeletedAt == null, ct);
            if (entry is null)
            {
                return PatientOutcome.Fail<IntakeLogDto>(PatientError.NotFound, "schedule.not_found");
            }

            var zone = await ZoneAsync(db, patient!.Id, ct);
            var local = TimeZoneInfo.ConvertTime(command.ScheduledFor!.Value, zone);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (ToUtc(date, entry.TimeOfDay, zone) != command.ScheduledFor.Value.ToUniversalTime() || !ActiveOn(entry, med, date))
            {
                return Support.Invalid<IntakeLogDto>(["scheduled_for.not_a_planned_dose"]);
            }
        }

        var scheduledUtc = command.ScheduledFor?.ToUniversalTime();
        var existing = command.ScheduleEntryId is { } eid ? await db.IntakeLogs.FirstOrDefaultAsync(x => x.ScheduleEntryId == eid && x.ScheduledFor == scheduledUtc, ct) : null;
        var isUpdate = existing is not null;
        var log = existing ?? new IntakeLogRecord { Id = Guid.CreateVersion7(), PatientId = patient!.Id, PatientMedicationId = med.Id, ScheduleEntryId = command.ScheduleEntryId, ScheduledFor = scheduledUtc };
        log.Status = command.Status;
        log.TakenAt = command.Status == IntakeStatus.Taken ? command.TakenAt ?? now : null;
        log.Note = FreeTextGuard.Clean(command.Note);
        log.RecordedAt = now;
        log.RecordedBy = actorUserId;
        if (!isUpdate)
        {
            db.IntakeLogs.Add(log);
        }

        Support.AddVersion(db, patient!.Id, "intake", log.Id, (await db.RecordVersions.CountAsync(x => x.RecordType == "intake" && x.RecordId == log.Id, ct)) + 1, new { log.Status, log.TakenAt, log.Note }, now, actorUserId, isUpdate ? "updated" : "created");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PatientOutcome.Fail<IntakeLogDto>(PatientError.Conflict, "intake.concurrent"); // two devices logged the same dose at once: the second sees a conflict and can retry
        }

        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Intake, actorUserId, now, await db.IntakeLogs.CountAsync(x => x.PatientId == patient.Id, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "intake-log", log.Id, subjectId, source, correlationId, isUpdate ? "updated" : "created");
        return PatientOutcome.Ok(ToDto(log));
    }

    public async Task<PatientOutcome<IReadOnlyList<IntakeLogDto>>> ListIntakeAsync(Guid subjectId, DateTimeOffset fromTime, DateTimeOffset untilTime, CancellationToken ct = default)
    {
        if (untilTime < fromTime || untilTime - fromTime > TimeSpan.FromDays(92))
        {
            return Support.Invalid<IReadOnlyList<IntakeLogDto>>(["range.invalid"]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<IntakeLogDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = await db.IntakeLogs.AsNoTracking().Where(x => x.PatientId == patient.Id && x.RecordedAt >= fromTime && x.RecordedAt <= untilTime).OrderByDescending(x => x.RecordedAt).Take(1000).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<IntakeLogDto>>([.. rows.Select(ToDto)]);
    }

    // ---------- helpers ----------

    private static void Apply(MedicationRecord row, PatientMedicationInput i)
    {
        row.MedicationId = i.MedicationId;
        row.UnregisteredName = i.MedicationId is null ? FreeTextGuard.Clean(i.UnregisteredName) : null;
        row.DoseAmount = i.DoseAmount;
        row.DoseUnit = i.DoseUnit?.ToLowerInvariant();
        row.DoseText = FreeTextGuard.Clean(i.DoseText);
        row.Frequency = i.Frequency;
        row.FrequencyValue = i.Frequency is FrequencyKind.TimesPerDay or FrequencyKind.EveryNHours ? i.FrequencyValue : null;
        row.Route = i.Route?.ToLowerInvariant();
        row.StartDate = i.StartDate;
        row.EndDate = i.EndDate;
        row.Source = i.Source;
        row.PrescriberNote = FreeTextGuard.Clean(i.PrescriberNote);
    }

    private static object Snapshot(MedicationRecord r) => new
    {
        r.MedicationId, r.UnregisteredName, r.DoseAmount, r.DoseUnit, r.DoseText, r.Frequency, r.FrequencyValue, r.Route, r.StartDate, r.EndDate, r.Source, r.PrescriberNote, r.Status, r.StopReason,
    };

    private static async Task<bool> TrySaveAsync(PatientsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false; // the version token no longer matched: someone else saved first
        }
    }

    private static async Task TouchMedicationsAsync(PatientsDbContext db, Guid patientId, Guid? actor, DateTimeOffset now, CancellationToken ct)
    {
        var count = await db.Medications.CountAsync(x => x.PatientId == patientId && x.DeletedAt == null && x.Status != PatientMedicationStatus.Stopped, ct);
        await Support.TouchAsync(db, patientId, PatientDataCategory.Medications, actor, now, count, ct);
    }

    private static bool ActiveOn(ScheduleEntryRecord e, MedicationRecord m, DateOnly date)
    {
        var end = e.EndDate is { } ee && m.EndDate is { } me ? (ee < me ? ee : me) : e.EndDate ?? m.EndDate;
        return date >= e.StartDate && date >= m.StartDate && (end is null || date <= end) && (e.DaysMask & (1 << (int)date.DayOfWeek)) != 0;
    }

    private static async Task<TimeZoneInfo> ZoneAsync(PatientsDbContext db, Guid patientId, CancellationToken ct)
    {
        var name = await db.Profiles.AsNoTracking().Where(x => x.PatientId == patientId).Select(x => x.TimeZone).FirstOrDefaultAsync(ct) ?? "Asia/Tehran";
        return TimeZoneInfo.TryFindSystemTimeZoneById(name, out var zone) ? zone
            : name == "Asia/Tehran" ? TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromHours(3.5), "Tehran", "Tehran") // Iran has no daylight saving since 2022
            : TimeZoneInfo.Utc;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static ScheduleEntryDto ToDto(ScheduleEntryRecord r) => new(r.Id, r.PatientMedicationId, r.TimeOfDay, PatientValidator.MaskToDays(r.DaysMask), r.StartDate, r.EndDate, r.DeletedAt is null);

    private static IntakeLogDto ToDto(IntakeLogRecord r) => new(r.Id, r.PatientMedicationId, r.ScheduleEntryId, r.ScheduledFor, r.Status, r.TakenAt, r.Note, r.RecordedAt);

    private async Task<IReadOnlyList<PatientMedicationDto>> ToDtosAsync(IReadOnlyList<MedicationRecord> rows, CancellationToken ct)
    {
        var refs = new Dictionary<Guid, MedicationDetailDto?>();
        foreach (var id in rows.Where(r => r.MedicationId is not null).Select(r => r.MedicationId!.Value).Distinct())
        {
            var result = await reference.GetAsync(id, true, ct);
            refs[id] = result.Succeeded ? result.Value : null;
        }

        return [.. rows.OrderBy(r => r.Status).ThenBy(r => r.CreatedAt).Select(r =>
        {
            var detail = r.MedicationId is { } id ? refs[id] : null;
            var display = detail is not null ? detail.Name.En ?? detail.Name.Fa ?? string.Empty : r.UnregisteredName ?? string.Empty;
            return new PatientMedicationDto(
                r.Id, r.MedicationId, display, detail?.Name, r.MedicationId is not null, detail?.IsDemo ?? false, r.DoseAmount, r.DoseUnit, r.DoseText, r.Frequency, r.FrequencyValue, r.Route,
                r.StartDate, r.EndDate, r.Source, r.PrescriberNote, r.Status, r.StopReason, r.UpdatedAt, r.Version);
        })];
    }
}
