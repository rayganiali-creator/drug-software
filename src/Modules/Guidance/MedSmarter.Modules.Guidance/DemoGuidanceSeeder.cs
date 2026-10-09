using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Guidance.Persistence;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Guidance;

/// <summary>Gives the demo patient two clearly labelled FICTIONAL messages so the screens have something to show. Development/Testing only; repeatable.</summary>
public sealed class DemoGuidanceSeeder(IDbContextFactory<GuidanceDbContext> factory, IGuidanceService guidance, IPatientDirectory patients, IDemoCareData careData) : IDemoGuidanceSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var account = careData.PatientAccounts().FirstOrDefault(a => a.AccountId == "demo-patient");
        if (account is null || await patients.FindBySubjectAsync(account.UserId, ct) is not { } patient)
        {
            return;
        }

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            if (await db.Messages.AnyAsync(m => m.PatientId == patient.PatientId, ct))
            {
                return;
            }
        }

        await guidance.CreateAsync(account.UserId, new GuidanceRequest("adherence.skipped_pattern", GuidanceLevel.FollowUp, "en", new Dictionary<string, string> { ["medication"] = "Demopril", ["count"] = "1", ["days"] = "3" }, GuidanceConfidence.Moderate, "DEMO sample (fictional, not from a real engine)", true), true, "demo-seed", null, ct);
        await guidance.CreateAsync(account.UserId, new GuidanceRequest("adherence.skipped_pattern", GuidanceLevel.Information, "en", new Dictionary<string, string> { ["topic"] = "your blood-pressure notes" }, GuidanceConfidence.NotAssessable, "DEMO sample", false), true, "demo-seed", null, ct);
    }
}
