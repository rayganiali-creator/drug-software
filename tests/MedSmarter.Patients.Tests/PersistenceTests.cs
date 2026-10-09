using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using MedSmarter.Modules.ProductTrace.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 5 data must survive a restart. These run only with MEDSMARTER_PG_TEST (a scratch LOCAL PostgreSQL).</summary>
public class PersistenceTests
{
    [PostgresFact]
    public async Task Patient_data_consents_reports_messages_and_the_audit_log_survive_a_restart()
    {
        using var first = new PEnv();
        var id = await first.Patient("demo-patient");
        var med = await first.TakeMedication(id);
        await first.Meds.AddScheduleEntryAsync(id, id, med.Id, new ScheduleEntryInput(new TimeOnly(8, 0), null, null, null), "t", null);
        await first.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Other, null, "pollen", AllergySeverity.Mild, null), "t", null);
        var consent = await first.Consent("demo-patient", ConsentPurposes.ManufacturerReport, [DataScopes.Products]);
        var product = (await first.Products.RecordAsync(id, ProductRecorder.Patient, id, new ProductRecordInput(null, await first.Reference_("demopril"), null, null, null, null, "PG-LOT-1", null, first.Today.AddMonths(8), null, null, first.Today.AddDays(-1)), "t", null)).Value!;
        var report = (await first.Reports.CreateDraftAsync(id, id, new ReportDraftInput(product.Id, ReportIssueType.QualityProblem, ReportSeverity.Mild, first.Today.AddDays(-1), null, "odd smell", false), "t", null)).Value!;
        await first.Reports.SubmitAsync(id, id, report.Id, report.Version, "t", null);
        var message = (await first.Guidance.CreateAsync(id, new GuidanceRequest("adherence.skipped_pattern", GuidanceLevel.FollowUp, "en", new Dictionary<string, string> { ["medication"] = "Demopril", ["count"] = "1", ["days"] = "3" }, GuidanceConfidence.Low, "DEMO", true), true, "t", null)).Value!;
        var relationship = (await first.Care.RequestAsync(id, new RequestCareRelationshipCommand(first.UserId("demo-physician-b"), "Treating"), "t", null)).Value!;

        using var second = first.Restart();
        Assert.Equal(med.Id, (await second.Meds.ListAsync(id, false)).Value!.Single().Id);
        Assert.Single((await second.Meds.ListScheduleAsync(id, med.Id)).Value!);
        Assert.Equal("pollen", (await second.Patients.ListAllergiesAsync(id)).Value!.Single().Substance);
        Assert.Equal(ConsentStatus.Active, (await second.Consents.ListGivenAsync(id)).Single(c => c.Id == consent.Id).Status);
        Assert.Equal("PG-LOT-1", (await second.Products.GetAsync(id, product.Id)).Value!.BatchNumber);
        Assert.Equal(ReportStatus.ReadyToSend, (await second.Reports.GetAsync(id, report.Id)).Value!.Status);
        Assert.Equal(1, (await second.Outbox.GetQueueAsync()).OutboxByState["Pending"]); // the queue is not lost
        Assert.Equal(message.Id, (await second.Guidance.ListForPatientAsync(id)).Value!.Single().Id);
        Assert.Equal(CareRelationshipStatus.PendingProvider, (await second.Care.ListMineAsync(id)).Single(r => r.Id == relationship.Id).Status);
        Assert.True(await second.Audit.VerifyChainAsync());
        Assert.Contains(await second.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Take: 200)), e => e.Action == AuditActions.ManufacturerReportSubmitted);

        // the restarted instance continues the work: the queued report is delivered by the new process
        Assert.Equal(1, (await second.Outbox.ProcessDueAsync(second.UserId("demo-system-admin"), 20, "t", null)).Sent);
    }

    [PostgresFact]
    public async Task The_database_itself_refuses_what_the_rules_forbid()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var factory = env.Get<IDbContextFactory<PatientsDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var patient = await db.Patients.SingleAsync();

        // a medicine needs a reference link or a name
        db.Medications.Add(new MedicationRecord { Id = Guid.NewGuid(), PatientId = patient.Id, StartDate = DateOnly.FromDateTime(DateTime.UtcNow), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ck_patient_medication_identity", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
        db.ChangeTracker.Clear();

        // a second open relationship for the same pair and kind
        var physician = env.UserId("demo-physician-b");
        Assert.True((await env.Care.RequestAsync(id, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Succeeded);
        db.CareRelationships.Add(new CareRelationshipRecord { Id = Guid.NewGuid(), PatientId = patient.Id, PatientUserId = id, ProviderUserId = physician, Kind = "Treating", Status = CareRelationshipStatus.Active, InitiatedBy = "Provider", RequestedAt = DateTimeOffset.UtcNow });
        var dup = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("care_relationship", Assert.IsType<PostgresException>(dup.InnerException).ConstraintName, StringComparison.Ordinal);
        db.ChangeTracker.Clear();

        // a relationship with oneself
        db.CareRelationships.Add(new CareRelationshipRecord { Id = Guid.NewGuid(), PatientId = patient.Id, PatientUserId = id, ProviderUserId = id, Kind = "Dispensing", Status = CareRelationshipStatus.Active, InitiatedBy = "Patient", RequestedAt = DateTimeOffset.UtcNow });
        var self = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ck_care_relationship_not_self", Assert.IsType<PostgresException>(self.InnerException).ConstraintName);
    }

    [PostgresFact]
    public async Task The_identifier_is_not_in_the_database_only_its_hash()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.Patients.LinkExternalIdentifierAsync(id, id, ExternalIdentifierScheme.Other, "DEMO-LINK-777", "t", null);
        await using var conn = new NpgsqlConnection(env.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT value_hash FROM patients.external_patient_identifier", conn);
        var stored = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(64, stored.Length);
        Assert.DoesNotContain("DEMO-LINK", stored, StringComparison.OrdinalIgnoreCase);
    }
}
