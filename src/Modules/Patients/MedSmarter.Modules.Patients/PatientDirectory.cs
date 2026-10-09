using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Patients;

public sealed class PatientDirectory(IDbContextFactory<PatientsDbContext> factory, IClock clock, IPatientMedicationService medications) : IPatientDirectory
{
    public async Task<PatientRecordRef?> FindBySubjectAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ToRefAsync(db, db.Patients.AsNoTracking().Where(p => p.UserId == subjectId), ct);
    }

    public async Task<PatientRecordRef?> FindByPatientIdAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ToRefAsync(db, db.Patients.AsNoTracking().Where(p => p.Id == patientId), ct);
    }

    public async Task<IReadOnlyList<PatientMedicationDto>> ActiveMedicationsAsync(Guid subjectId, CancellationToken ct = default) =>
        (await medications.ListAsync(subjectId, false, ct)).Value ?? [];

    public async Task TouchCategoryAsync(Guid subjectId, PatientDataCategory category, Guid? actorUserId, int recordCount, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await Support.FindPatientAsync(db, subjectId, ct) is { } patient)
        {
            await Support.TouchAsync(db, patient.Id, category, actorUserId, clock.UtcNow, recordCount, ct);
        }
    }

    private static async Task<PatientRecordRef?> ToRefAsync(PatientsDbContext db, IQueryable<Patient> query, CancellationToken ct)
    {
        var p = await query.FirstOrDefaultAsync(ct);
        if (p is null)
        {
            return null;
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.PatientId == p.Id, ct);
        return new PatientRecordRef(p.Id, p.UserId, p.Status, profile?.YearOfBirth, profile?.Sex);
    }
}
