using System.Security.Cryptography;
using System.Text;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Patients;

/// <summary>
/// Loads FICTIONAL patients for the demo accounts, plus the demo care relationships (so a patient can end them). Idempotent: running it again
/// changes nothing. Development/Testing only (Patients:SeedDemoData). Every record is flagged demo and labelled "DEMO DATA - NOT FOR CLINICAL USE".
/// </summary>
public sealed class DemoPatientSeeder(
    IDbContextFactory<PatientsDbContext> factory, IClock clock, IOptions<PatientsOptions> options, IDemoCareData careData,
    IPatientService patients, IPatientMedicationService medications, IMedicationService reference) : IDemoPatientSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!options.Value.SeedDemoData)
        {
            throw new InvalidOperationException("Demo patients can only be seeded when Patients:SeedDemoData is enabled.");
        }

        var now = clock.UtcNow;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            foreach (var account in careData.PatientAccounts())
            {
                if (await db.Patients.AnyAsync(p => p.UserId == account.UserId, ct))
                {
                    continue;
                }

                var patient = new Patient { Id = Guid.CreateVersion7(), UserId = account.UserId, IsDemo = true, Status = PatientStatus.Active, CreatedAt = now, UpdatedAt = now };
                db.Patients.Add(patient);
                db.Profiles.Add(new ProfileRecord { PatientId = patient.Id, YearOfBirth = 1985 + (account.AccountId.Length % 10), Sex = SexGroup.Undisclosed, TimeZone = "Asia/Tehran", UpdatedAt = now, Version = 1 });
                db.DataCategories.Add(new DataCategoryRecord { PatientId = patient.Id, Category = PatientDataCategory.Profile, LastUpdatedAt = now.AddDays(-2), RecordCount = 1 });
                if (account.AccountId == "demo-patient")
                {
                    await db.SaveChangesAsync(ct);
                    await SeedRichPatientAsync(account.UserId, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            foreach (var link in careData.CareLinks())
            {
                var patient = await db.Patients.FirstOrDefaultAsync(p => p.UserId == link.PatientUserId, ct);
                if (patient is null || await db.CareRelationships.AnyAsync(r => r.PatientUserId == link.PatientUserId && r.ProviderUserId == link.ProviderUserId && r.ProviderOrganizationId == link.ProviderOrganizationId && r.Kind == link.Kind, ct))
                {
                    continue;
                }

                var started = now.AddDays(-60);
                var row = new CareRelationshipRecord
                {
                    Id = DeterministicId($"{link.PatientUserId}|{link.ProviderUserId}|{link.ProviderOrganizationId}|{link.Kind}"), PatientId = patient.Id, PatientUserId = link.PatientUserId, ProviderUserId = link.ProviderUserId,
                    ProviderOrganizationId = link.ProviderOrganizationId, Kind = link.Kind, Status = CareRelationshipStatus.Active, InitiatedBy = "Patient", RequestedAt = started, PatientConsentedAt = started, ProviderConsentedAt = started,
                };
                db.CareRelationships.Add(row);
                db.CareRelationshipEvents.Add(new CareRelationshipEventRecord { Id = Guid.CreateVersion7(), RelationshipId = row.Id, Kind = "accepted", At = started, ActorUserId = link.ProviderUserId ?? link.PatientUserId, Note = "demo seed" });
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedRichPatientAsync(Guid subjectId, CancellationToken ct)
    {
        const string src = "demo-seed";
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        await patients.UpdateProfileAsync(subjectId, subjectId, new UpdateProfileCommand(1990, SexGroup.Female, 62m, 165m, "Asia/Tehran", 1), src, null, ct);
        await patients.AddConditionAsync(subjectId, subjectId, new ConditionInput("Seasonal rhinitis (DEMO)", today.AddYears(-3), ConditionStatus.Active, "Fictional demo entry"), src, null, ct);
        await patients.AddAllergyAsync(subjectId, subjectId, new AllergyInput(AllergenKind.Other, null, "DEMO allergen (fictional)", AllergySeverity.Mild, "Mild rash (fictional)"), src, null, ct);
        var demopril = (await reference.SearchAsync(new MedicationSearchQuery("demopril"), ct)).Items is { Count: > 0 } d ? d[0] : null;
        var nocturin = (await reference.SearchAsync(new MedicationSearchQuery("nocturin"), ct)).Items is { Count: > 0 } n ? n[0] : null;
        PatientMedicationDto? first = null;
        if (demopril is not null)
        {
            first = (await medications.AddAsync(subjectId, subjectId, new PatientMedicationInput(demopril.Id, null, 5, "mg", null, FrequencyKind.TimesPerDay, 1, "oral", today.AddDays(-40), null, PrescriberSource.Physician, "Fictional prescriber", null), src, null, ct)).Value;
        }

        if (nocturin is not null)
        {
            await medications.AddAsync(subjectId, subjectId, new PatientMedicationInput(nocturin.Id, null, 10, "mg", null, FrequencyKind.TimesPerDay, 1, "oral", today.AddDays(-20), null, PrescriberSource.Physician, null, null), src, null, ct);
        }

        await medications.AddAsync(subjectId, subjectId, new PatientMedicationInput(null, "DEMO herbal tonic (unregistered)", null, null, "1 spoon", FrequencyKind.AsNeeded, null, "oral", today.AddDays(-10), null, PrescriberSource.SelfReported, null, null), src, null, ct);
        if (first is not null)
        {
            var entry = (await medications.AddScheduleEntryAsync(subjectId, subjectId, first.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, today.AddDays(-5), null), src, null, ct)).Value;
            if (entry is not null)
            {
                for (var back = 1; back <= 3; back++)
                {
                    var slots = (await medications.GetDayAsync(subjectId, today.AddDays(-back), ct)).Value ?? [];
                    var slot = slots.FirstOrDefault(s => s.ScheduleEntryId == entry.Id);
                    if (slot is not null)
                    {
                        await medications.LogIntakeAsync(subjectId, subjectId, new LogIntakeCommand(first.Id, entry.Id, slot.ScheduledFor, back == 2 ? IntakeStatus.Skipped : IntakeStatus.Taken, back == 2 ? null : slot.ScheduledFor.AddMinutes(10), null), src, null, ct);
                    }
                }
            }
        }

        await patients.AddSymptomAsync(subjectId, subjectId, new SymptomInput("Mild headache (DEMO)", SymptomSeverity.Mild, clock.UtcNow.AddDays(-2), clock.UtcNow.AddDays(-2).AddHours(5), first?.Id, "Fictional demo entry"), src, null, ct);
    }

    private static Guid DeterministicId(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("medsmarter-demo-care|" + name)).AsSpan(0, 16));
}
