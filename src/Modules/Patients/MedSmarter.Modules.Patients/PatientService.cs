using System.Security.Cryptography;
using System.Text;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Patients;

public sealed class PatientService(IDbContextFactory<PatientsDbContext> factory, IClock clock, IAuditWriter audit, IMedicationService medications, IOptions<PatientsOptions> options) : IPatientService
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ---------- patient ----------

    public async Task<PatientOutcome<PatientDto>> GetAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Support.FindPatientAsync(db, subjectId, ct) is { } p ? PatientOutcome.Ok(ToDto(p)) : PatientOutcome.Fail<PatientDto>(PatientError.NotFound, "patient.not_found");
    }

    public async Task<PatientOutcome<PatientDto>> EnsureOwnAsync(Guid subjectId, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await Support.FindPatientAsync(db, subjectId, ct);
        if (existing is not null)
        {
            return PatientOutcome.Ok(ToDto(existing));
        }

        var now = clock.UtcNow;
        var patient = new Patient { Id = Guid.CreateVersion7(), UserId = subjectId, Status = PatientStatus.Active, CreatedAt = now, UpdatedAt = now };
        db.Patients.Add(patient);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced; the unique account link means exactly one record exists.
            db.ChangeTracker.Clear();
            return await Support.FindPatientAsync(db, subjectId, ct) is { } raced ? PatientOutcome.Ok(ToDto(raced)) : throw new InvalidOperationException("patient record could not be created");
        }

        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, subjectId, "patient", patient.Id, subjectId, source, correlationId, "patient_created");
        return PatientOutcome.Ok(ToDto(patient));
    }

    public async Task<PatientOutcome<PatientDto>> SetStatusAsync(Guid actorUserId, Guid subjectId, PatientStatus status, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<PatientDto>(PatientError.NotFound, "patient.not_found");
        }

        patient.Status = status;
        patient.DeactivatedAt = status == PatientStatus.Inactive ? clock.UtcNow : null;
        patient.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "patient", patient.Id, subjectId, source, correlationId, status == PatientStatus.Inactive ? "patient_deactivated" : "patient_reactivated");
        return PatientOutcome.Ok(ToDto(patient));
    }

    private static PatientDto ToDto(Patient p) => new(p.UserId, p.Status, p.IsDemo, Support.Notice(p), p.CreatedAt);

    // ---------- profile ----------

    public async Task<PatientOutcome<PatientProfileDto>> GetProfileAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<PatientProfileDto>(PatientError.NotFound, "patient.not_found");
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.PatientId == patient.Id, ct);
        return PatientOutcome.Ok(ToDto(patient, profile));
    }

    public async Task<PatientOutcome<PatientProfileDto>> UpdateProfileAsync(Guid actorUserId, Guid subjectId, UpdateProfileCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Profile(command, clock.UtcNow.Year);
        if (errors.Count > 0)
        {
            return Support.Invalid<PatientProfileDto>(errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<PatientProfileDto>(patient) is { } problem)
        {
            return problem;
        }

        var now = clock.UtcNow;
        var profile = await db.Profiles.FirstOrDefaultAsync(x => x.PatientId == patient!.Id, ct);
        if (profile is null)
        {
            if (command.ExpectedVersion is not (null or 0))
            {
                return PatientOutcome.Fail<PatientProfileDto>(PatientError.Conflict, "version.mismatch");
            }

            profile = new ProfileRecord { PatientId = patient!.Id, Version = 1 };
            db.Profiles.Add(profile);
        }
        else
        {
            if (command.ExpectedVersion != profile.Version)
            {
                return PatientOutcome.Fail<PatientProfileDto>(PatientError.Conflict, "version.mismatch");
            }

            profile.Version++;
        }

        profile.YearOfBirth = command.YearOfBirth;
        profile.Sex = command.Sex;
        profile.WeightKg = command.WeightKg;
        profile.HeightCm = command.HeightCm;
        profile.TimeZone = command.TimeZone ?? profile.TimeZone;
        profile.UpdatedAt = now;
        profile.UpdatedBy = actorUserId;
        Support.AddVersion(db, patient!.Id, "profile", patient.Id, profile.Version, new { profile.YearOfBirth, profile.Sex, profile.WeightKg, profile.HeightCm, profile.TimeZone }, now, actorUserId, "profile updated");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PatientOutcome.Fail<PatientProfileDto>(PatientError.Conflict, "version.mismatch");
        }

        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Profile, actorUserId, now, 1, ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "profile", patient.Id, subjectId, source, correlationId);
        return PatientOutcome.Ok(ToDto(patient, profile));
    }

    private PatientProfileDto ToDto(Patient p, ProfileRecord? r) => new(
        p.UserId, r?.YearOfBirth, r?.YearOfBirth is { } y ? clock.UtcNow.Year - y : null, r?.Sex, r?.WeightKg, r?.HeightCm, r?.TimeZone ?? "Asia/Tehran", r?.UpdatedAt ?? default, r?.Version ?? 0, p.IsDemo, Support.Notice(p));

    // ---------- conditions ----------

    public async Task<PatientOutcome<IReadOnlyList<ConditionDto>>> ListConditionsAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<ConditionDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = await db.Conditions.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null).OrderBy(x => x.Name).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<ConditionDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<PatientOutcome<ConditionDto>> AddConditionAsync(Guid actorUserId, Guid subjectId, ConditionInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Condition(input, Today);
        if (errors.Count > 0)
        {
            return Support.Invalid<ConditionDto>(errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<ConditionDto>(patient) is { } problem)
        {
            return problem;
        }

        var now = clock.UtcNow;
        var row = new ConditionRecord
        {
            Id = Guid.CreateVersion7(), PatientId = patient!.Id, Name = FreeTextGuard.Clean(input.Name)!, OnsetDate = input.OnsetDate, Status = input.Status, Note = FreeTextGuard.Clean(input.Note),
            CreatedAt = now, UpdatedAt = now, UpdatedBy = actorUserId, Version = 1,
        };
        db.Conditions.Add(row);
        Support.AddVersion(db, patient.Id, "condition", row.Id, 1, ToDto(row), now, actorUserId, "created");
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Conditions, actorUserId, now, await db.Conditions.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "condition", row.Id, subjectId, source, correlationId, "created");
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<ConditionDto>> UpdateConditionAsync(Guid actorUserId, Guid subjectId, Guid id, ConditionInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Condition(input, Today);
        if (errors.Count > 0)
        {
            return Support.Invalid<ConditionDto>(errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<ConditionDto>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Conditions.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct); // the patient id check is what stops another patient's record id from working
        if (row is null)
        {
            return PatientOutcome.Fail<ConditionDto>(PatientError.NotFound, "condition.not_found");
        }

        if (input.ExpectedVersion != row.Version)
        {
            return PatientOutcome.Fail<ConditionDto>(PatientError.Conflict, "version.mismatch");
        }

        var now = clock.UtcNow;
        row.Name = FreeTextGuard.Clean(input.Name)!;
        row.OnsetDate = input.OnsetDate;
        row.Status = input.Status;
        row.Note = FreeTextGuard.Clean(input.Note);
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        Support.AddVersion(db, patient!.Id, "condition", row.Id, row.Version, ToDto(row), now, actorUserId, "updated");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PatientOutcome.Fail<ConditionDto>(PatientError.Conflict, "version.mismatch");
        }

        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Conditions, actorUserId, now, await db.Conditions.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "condition", row.Id, subjectId, source, correlationId, "updated");
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<bool>> RemoveConditionAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<bool>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Conditions.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<bool>(PatientError.NotFound, "condition.not_found");
        }

        var now = clock.UtcNow;
        row.DeletedAt = now;
        row.Version++;
        row.UpdatedBy = actorUserId;
        row.UpdatedAt = now;
        Support.AddVersion(db, patient!.Id, "condition", row.Id, row.Version, ToDto(row), now, actorUserId, "removed");
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Conditions, actorUserId, now, await db.Conditions.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "condition", row.Id, subjectId, source, correlationId, "removed");
        return PatientOutcome.Ok(true);
    }

    private static ConditionDto ToDto(ConditionRecord r) => new(r.Id, r.Name, r.OnsetDate, r.Status, r.Note, r.UpdatedAt, r.Version);

    // ---------- allergies ----------

    public async Task<PatientOutcome<IReadOnlyList<AllergyDto>>> ListAllergiesAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<AllergyDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = await db.Allergies.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null).OrderBy(x => x.Substance).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<AllergyDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<PatientOutcome<AllergyDto>> AddAllergyAsync(Guid actorUserId, Guid subjectId, AllergyInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Allergy(input);
        if (errors.Count > 0)
        {
            return Support.Invalid<AllergyDto>(errors);
        }

        var substance = await ResolveSubstanceAsync(input, ct);
        if (substance is null)
        {
            return Support.Invalid<AllergyDto>(["medication.unknown"]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<AllergyDto>(patient) is { } problem)
        {
            return problem;
        }

        var now = clock.UtcNow;
        var row = new AllergyRecord
        {
            Id = Guid.CreateVersion7(), PatientId = patient!.Id, Kind = input.Kind, MedicationId = input.MedicationId, Substance = substance, Severity = input.Severity,
            Reaction = FreeTextGuard.Clean(input.Reaction), CreatedAt = now, UpdatedAt = now, UpdatedBy = actorUserId, Version = 1,
        };
        db.Allergies.Add(row);
        Support.AddVersion(db, patient.Id, "allergy", row.Id, 1, ToDto(row), now, actorUserId, "created");
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Allergies, actorUserId, now, await db.Allergies.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "allergy", row.Id, subjectId, source, correlationId, "created");
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<AllergyDto>> UpdateAllergyAsync(Guid actorUserId, Guid subjectId, Guid id, AllergyInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Allergy(input);
        if (errors.Count > 0)
        {
            return Support.Invalid<AllergyDto>(errors);
        }

        var substance = await ResolveSubstanceAsync(input, ct);
        if (substance is null)
        {
            return Support.Invalid<AllergyDto>(["medication.unknown"]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<AllergyDto>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Allergies.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<AllergyDto>(PatientError.NotFound, "allergy.not_found");
        }

        if (input.ExpectedVersion != row.Version)
        {
            return PatientOutcome.Fail<AllergyDto>(PatientError.Conflict, "version.mismatch");
        }

        var now = clock.UtcNow;
        row.Kind = input.Kind;
        row.MedicationId = input.MedicationId;
        row.Substance = substance;
        row.Severity = input.Severity;
        row.Reaction = FreeTextGuard.Clean(input.Reaction);
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        Support.AddVersion(db, patient!.Id, "allergy", row.Id, row.Version, ToDto(row), now, actorUserId, "updated");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PatientOutcome.Fail<AllergyDto>(PatientError.Conflict, "version.mismatch");
        }

        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Allergies, actorUserId, now, await db.Allergies.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "allergy", row.Id, subjectId, source, correlationId, "updated");
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<bool>> RemoveAllergyAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<bool>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Allergies.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<bool>(PatientError.NotFound, "allergy.not_found");
        }

        var now = clock.UtcNow;
        row.DeletedAt = now;
        row.Version++;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        Support.AddVersion(db, patient!.Id, "allergy", row.Id, row.Version, ToDto(row), now, actorUserId, "removed");
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Allergies, actorUserId, now, await db.Allergies.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "allergy", row.Id, subjectId, source, correlationId, "removed");
        return PatientOutcome.Ok(true);
    }

    /// <summary>For a medication allergy linked to the reference the displayed name comes from the reference (so it matches what the medication list shows).</summary>
    private async Task<string?> ResolveSubstanceAsync(AllergyInput input, CancellationToken ct)
    {
        if (input.Kind == AllergenKind.Medication && input.MedicationId is { } id)
        {
            var med = await medications.GetAsync(id, false, ct);
            return med.Succeeded ? (med.Value!.Name.En ?? med.Value.Name.Fa) : null;
        }

        return FreeTextGuard.Clean(input.Substance);
    }

    private static AllergyDto ToDto(AllergyRecord r) => new(r.Id, r.Kind, r.MedicationId, r.Substance, r.Severity, r.Reaction, r.UpdatedAt, r.Version);

    // ---------- symptoms ----------

    public async Task<PatientOutcome<IReadOnlyList<SymptomReportDto>>> ListSymptomsAsync(Guid subjectId, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<SymptomReportDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = await db.Symptoms.AsNoTracking().Where(x => x.PatientId == patient.Id && x.DeletedAt == null).OrderByDescending(x => x.OnsetAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<SymptomReportDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<PatientOutcome<SymptomReportDto>> AddSymptomAsync(Guid actorUserId, Guid subjectId, SymptomInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = PatientValidator.Symptom(input, clock.UtcNow);
        if (errors.Count > 0)
        {
            return Support.Invalid<SymptomReportDto>(errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<SymptomReportDto>(patient) is { } problem)
        {
            return problem;
        }

        if (input.PatientMedicationId is { } mid && !await db.Medications.AnyAsync(x => x.Id == mid && x.PatientId == patient!.Id && x.DeletedAt == null, ct))
        {
            return Support.Invalid<SymptomReportDto>(["medication.not_found"]); // a medication of another patient is indistinguishable from a missing one
        }

        var now = clock.UtcNow;
        var row = new SymptomRecord
        {
            Id = Guid.CreateVersion7(), PatientId = patient!.Id, Text = FreeTextGuard.Clean(input.Text)!, Severity = input.Severity, OnsetAt = input.OnsetAt, ResolvedAt = input.ResolvedAt,
            PatientMedicationId = input.PatientMedicationId, Note = FreeTextGuard.Clean(input.Note), RecordedAt = now, RecordedBy = actorUserId,
        };
        db.Symptoms.Add(row);
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient.Id, PatientDataCategory.Symptoms, actorUserId, now, await db.Symptoms.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "symptom", row.Id, subjectId, source, correlationId, "created");
        return PatientOutcome.Ok(ToDto(row));
    }

    public async Task<PatientOutcome<bool>> RemoveSymptomAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<bool>(patient) is { } problem)
        {
            return problem;
        }

        var row = await db.Symptoms.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient!.Id && x.DeletedAt == null, ct);
        if (row is null)
        {
            return PatientOutcome.Fail<bool>(PatientError.NotFound, "symptom.not_found");
        }

        row.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await Support.TouchAsync(db, patient!.Id, PatientDataCategory.Symptoms, actorUserId, clock.UtcNow, await db.Symptoms.CountAsync(x => x.PatientId == patient.Id && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "symptom", row.Id, subjectId, source, correlationId, "removed");
        return PatientOutcome.Ok(true);
    }

    private static SymptomReportDto ToDto(SymptomRecord r) => new(r.Id, r.Text, r.Severity, r.OnsetAt, r.ResolvedAt, r.PatientMedicationId, r.Note, r.RecordedAt);

    // ---------- freshness ----------

    public async Task<PatientOutcome<IReadOnlyList<DataFreshnessDto>>> GetFreshnessAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<DataFreshnessDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = (await db.DataCategories.AsNoTracking().Where(x => x.PatientId == patient.Id).ToListAsync(ct)).ToDictionary(x => x.Category);
        var now = clock.UtcNow;
        return PatientOutcome.Ok<IReadOnlyList<DataFreshnessDto>>([.. Enum.GetValues<PatientDataCategory>().Select(c =>
        {
            var days = Support.StaleDays(options, c);
            return rows.TryGetValue(c, out var r)
                ? new DataFreshnessDto(c, r.LastUpdatedAt, r.RecordCount, false, now - r.LastUpdatedAt > TimeSpan.FromDays(days), days)
                : new DataFreshnessDto(c, null, 0, true, false, days);
        })]);
    }

    // ---------- external identifiers ----------

    public async Task<PatientOutcome<IReadOnlyList<ExternalIdentifierDto>>> ListExternalIdentifiersAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (patient is null)
        {
            return PatientOutcome.Fail<IReadOnlyList<ExternalIdentifierDto>>(PatientError.NotFound, "patient.not_found");
        }

        var rows = await db.ExternalIdentifiers.AsNoTracking().Where(x => x.PatientId == patient.Id).OrderBy(x => x.LinkedAt).ToListAsync(ct);
        return PatientOutcome.Ok<IReadOnlyList<ExternalIdentifierDto>>([.. rows.Select(r => new ExternalIdentifierDto(r.Id, r.Scheme, r.Verified, r.LinkedAt))]);
    }

    public async Task<PatientOutcome<ExternalIdentifierDto>> LinkExternalIdentifierAsync(Guid actorUserId, Guid subjectId, ExternalIdentifierScheme scheme, string value, string source, string? correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.IdentifierHashKey))
        {
            return PatientOutcome.Fail<ExternalIdentifierDto>(PatientError.Validation, "identifier_hashing.not_configured");
        }

        var cleaned = FreeTextGuard.NormalizeDigits(value.Trim());
        if (!Enum.IsDefined(scheme) || cleaned.Length is < 4 or > 64 || cleaned.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '/' or '.')) || (scheme == ExternalIdentifierScheme.NationalId && (cleaned.Length != 10 || cleaned.Any(c => !char.IsAsciiDigit(c)))))
        {
            return Support.Invalid<ExternalIdentifierDto>(["identifier.invalid"]);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await Support.FindPatientAsync(db, subjectId, ct);
        if (Support.WritableProblem<ExternalIdentifierDto>(patient) is { } problem)
        {
            return problem;
        }

        var hash = Hash(cleaned);
        if (await db.ExternalIdentifiers.AnyAsync(x => x.Scheme == scheme && x.ValueHash == hash, ct))
        {
            return PatientOutcome.Fail<ExternalIdentifierDto>(PatientError.Conflict, "identifier.already_linked"); // one identifier, one patient
        }

        var row = new ExternalIdentifierRecord { Id = Guid.CreateVersion7(), PatientId = patient!.Id, Scheme = scheme, ValueHash = hash, Verified = false, LinkedAt = clock.UtcNow, LinkedBy = actorUserId };
        db.ExternalIdentifiers.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PatientOutcome.Fail<ExternalIdentifierDto>(PatientError.Conflict, "identifier.already_linked");
        }

        await Support.AuditAsync(audit, AuditActions.PatientDataUpdated, AuditResult.Success, actorUserId, "external-identifier", row.Id, subjectId, source, correlationId, "identifier_linked", scheme.ToString());
        return PatientOutcome.Ok(new ExternalIdentifierDto(row.Id, row.Scheme, row.Verified, row.LinkedAt));
    }

    public async Task<Guid?> FindSubjectByExternalIdentifierAsync(ExternalIdentifierScheme scheme, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.IdentifierHashKey))
        {
            return null;
        }

        var hash = Hash(FreeTextGuard.NormalizeDigits(value.Trim()));
        await using var db = await factory.CreateDbContextAsync(ct);
        var patientId = await db.ExternalIdentifiers.AsNoTracking().Where(x => x.Scheme == scheme && x.ValueHash == hash).Select(x => (Guid?)x.PatientId).FirstOrDefaultAsync(ct);
        return patientId is null ? null : await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => (Guid?)p.UserId).FirstOrDefaultAsync(ct);
    }

    private string Hash(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Value.IdentifierHashKey!));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value.ToUpperInvariant())));
    }

    // ---------- retention ----------

    public async Task<int> PurgeDeletedAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var removed = 0;

        var meds = await db.Medications.Where(x => x.DeletedAt != null && x.DeletedAt < olderThan).ToListAsync(ct);
        foreach (var m in meds)
        {
            var logs = await db.IntakeLogs.Where(x => x.PatientMedicationId == m.Id).ToListAsync(ct);
            var entries = await db.ScheduleEntries.Where(x => x.PatientMedicationId == m.Id).ToListAsync(ct);
            db.IntakeLogs.RemoveRange(logs);
            await db.SaveChangesAsync(ct);
            db.ScheduleEntries.RemoveRange(entries);
            foreach (var s in await db.Symptoms.Where(x => x.PatientMedicationId == m.Id).ToListAsync(ct))
            {
                s.PatientMedicationId = null;
            }

            db.RecordVersions.RemoveRange(await db.RecordVersions.Where(x => x.RecordId == m.Id).ToListAsync(ct));
            db.Medications.Remove(m);
            await db.SaveChangesAsync(ct);
            removed += 1 + logs.Count + entries.Count;
        }

        var conditions = await db.Conditions.Where(x => x.DeletedAt != null && x.DeletedAt < olderThan).ToListAsync(ct);
        db.RecordVersions.RemoveRange(await PurgeVersionsAsync(db, conditions.Select(x => x.Id), ct));
        db.Conditions.RemoveRange(conditions);
        var allergies = await db.Allergies.Where(x => x.DeletedAt != null && x.DeletedAt < olderThan).ToListAsync(ct);
        db.RecordVersions.RemoveRange(await PurgeVersionsAsync(db, allergies.Select(x => x.Id), ct));
        db.Allergies.RemoveRange(allergies);
        var symptoms = await db.Symptoms.Where(x => x.DeletedAt != null && x.DeletedAt < olderThan).ToListAsync(ct);
        db.Symptoms.RemoveRange(symptoms);
        var stale = await db.ScheduleEntries.Where(x => x.DeletedAt != null && x.DeletedAt < olderThan).ToListAsync(ct);
        foreach (var e in stale)
        {
            db.IntakeLogs.RemoveRange(await db.IntakeLogs.Where(x => x.ScheduleEntryId == e.Id).ToListAsync(ct));
        }

        await db.SaveChangesAsync(ct);
        db.ScheduleEntries.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        removed += conditions.Count + allergies.Count + symptoms.Count + stale.Count;
        return removed;
    }

    private static async Task<List<RecordVersionRow>> PurgeVersionsAsync(PatientsDbContext db, IEnumerable<Guid> recordIds, CancellationToken ct)
    {
        var ids = recordIds.ToList();
        return await db.RecordVersions.Where(x => ids.Contains(x.RecordId)).ToListAsync(ct);
    }
}
