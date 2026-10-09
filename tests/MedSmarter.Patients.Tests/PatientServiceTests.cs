using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Patients.Tests;

public class PatientServiceTests
{
    [Fact]
    public async Task A_patient_creates_their_own_record_once()
    {
        using var env = new PEnv();
        var id = env.UserId("demo-patient");
        var first = await env.Patients.EnsureOwnAsync(id, "test", null);
        var second = await env.Patients.EnsureOwnAsync(id, "test", null);
        Assert.True(first.Succeeded && second.Succeeded);
        Assert.Equal(first.Value!.CreatedAt, second.Value!.CreatedAt);
        Assert.Equal(PatientStatus.Active, first.Value.Status);
        Assert.False((await env.Patients.GetAsync(env.UserId("demo-patient-2"))).Succeeded); // nobody else got one
    }

    [Fact]
    public async Task Profile_is_versioned_and_stale_writes_are_rejected()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var empty = (await env.Patients.GetProfileAsync(id)).Value!;
        Assert.Equal(0, empty.Version); // nothing saved yet: shown as empty, not invented

        var saved = await env.Patients.UpdateProfileAsync(id, id, new UpdateProfileCommand(1990, SexGroup.Female, 62.5m, 168, "Asia/Tehran", null), "test", null);
        Assert.True(saved.Succeeded, saved.Detail);
        Assert.Equal(1, saved.Value!.Version);
        Assert.Equal(36, saved.Value.AgeYears);

        var stale = await env.Patients.UpdateProfileAsync(id, id, new UpdateProfileCommand(1991, null, null, null, null, 0), "test", null);
        Assert.Equal(PatientError.Conflict, stale.Error);
        var next = await env.Patients.UpdateProfileAsync(id, id, new UpdateProfileCommand(1991, SexGroup.Undisclosed, null, null, null, 1), "test", null);
        Assert.Equal(2, next.Value!.Version);
        Assert.Null(next.Value.WeightKg); // cleared values stay cleared
    }

    [Fact]
    public async Task Invalid_profile_values_are_rejected_with_codes()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var r = await env.Patients.UpdateProfileAsync(id, id, new UpdateProfileCommand(1700, null, -5, 999, null, null), "test", null);
        Assert.Equal(PatientError.Validation, r.Error);
        Assert.Contains("year_of_birth.range", r.Detail, StringComparison.Ordinal);
        Assert.Contains("weight.range", r.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_patients_are_not_found()
    {
        using var env = new PEnv();
        Assert.Equal(PatientError.NotFound, (await env.Patients.GetProfileAsync(Guid.NewGuid())).Error);
        Assert.Equal(PatientError.NotFound, (await env.Patients.AddConditionAsync(Guid.NewGuid(), Guid.NewGuid(), new ConditionInput("x", null, ConditionStatus.Active, null), "t", null)).Error);
    }

    [Fact]
    public async Task Conditions_have_history_and_are_soft_deleted()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var added = (await env.Patients.AddConditionAsync(id, id, new ConditionInput("Seasonal rhinitis", env.Today.AddYears(-2), ConditionStatus.Active, "since spring"), "t", null)).Value!;
        var updated = await env.Patients.UpdateConditionAsync(id, id, added.Id, new ConditionInput("Seasonal rhinitis", added.OnsetDate, ConditionStatus.Resolved, null, added.Version), "t", null);
        Assert.Equal(ConditionStatus.Resolved, updated.Value!.Status);
        Assert.Equal(2, updated.Value.Version);
        Assert.Equal(PatientError.Conflict, (await env.Patients.UpdateConditionAsync(id, id, added.Id, new ConditionInput("x", null, ConditionStatus.Active, null, 1), "t", null)).Error);

        Assert.True((await env.Patients.RemoveConditionAsync(id, id, added.Id, "t", null)).Succeeded);
        Assert.Empty((await env.Patients.ListConditionsAsync(id)).Value!);
        Assert.Equal(PatientError.NotFound, (await env.Patients.RemoveConditionAsync(id, id, added.Id, "t", null)).Error);
    }

    [Fact]
    public async Task One_patient_cannot_touch_another_patients_record_by_id()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = await env.Patient("demo-patient-2");
        var condition = (await env.Patients.AddConditionAsync(a, a, new ConditionInput("A's condition", null, ConditionStatus.Active, null), "t", null)).Value!;
        // B's route with A's record id: indistinguishable from a missing record
        Assert.Equal(PatientError.NotFound, (await env.Patients.UpdateConditionAsync(b, b, condition.Id, new ConditionInput("hijack", null, ConditionStatus.Active, null, 1), "t", null)).Error);
        Assert.Equal(PatientError.NotFound, (await env.Patients.RemoveConditionAsync(b, b, condition.Id, "t", null)).Error);
        Assert.Equal("A's condition", (await env.Patients.ListConditionsAsync(a)).Value!.Single().Name);
        Assert.Empty((await env.Patients.ListConditionsAsync(b)).Value!);
    }

    [Fact]
    public async Task Allergies_to_a_reference_medicine_take_the_name_from_the_reference()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("nocturin");
        var allergy = await env.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Medication, med, "typed name is ignored", AllergySeverity.Moderate, "rash"), "t", null);
        Assert.True(allergy.Succeeded, allergy.Detail);
        Assert.Equal("Nocturin", allergy.Value!.Substance);
        Assert.Equal(PatientError.Validation, (await env.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Medication, Guid.NewGuid(), null, AllergySeverity.Mild, null), "t", null)).Error); // never guessed
        Assert.Equal(PatientError.Validation, (await env.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Other, med, "pollen", AllergySeverity.Mild, null), "t", null)).Error);
        Assert.True((await env.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Other, null, "pollen", AllergySeverity.Mild, null), "t", null)).Succeeded);
    }

    [Fact]
    public async Task Free_text_that_identifies_a_person_never_gets_stored()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var r = await env.Patients.AddSymptomAsync(id, id, new SymptomInput("headache, call 09121234567", SymptomSeverity.Mild, env.Clock.UtcNow.AddHours(-2), null, null, null), "t", null);
        Assert.Equal(PatientError.Validation, r.Error);
        Assert.Contains("looks_identifying", r.Detail, StringComparison.Ordinal);
        Assert.Empty((await env.Patients.ListSymptomsAsync(id, 10)).Value!);
    }

    [Fact]
    public async Task Symptoms_validate_time_and_link_only_to_own_medicines()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = await env.Patient("demo-patient-2");
        var bMed = await env.TakeMedication(b);
        var future = await env.Patients.AddSymptomAsync(a, a, new SymptomInput("dizzy", SymptomSeverity.Mild, env.Clock.UtcNow.AddDays(2), null, null, null), "t", null);
        Assert.Contains("onset.range", future.Detail, StringComparison.Ordinal);
        var foreign = await env.Patients.AddSymptomAsync(a, a, new SymptomInput("dizzy", SymptomSeverity.Mild, env.Clock.UtcNow.AddHours(-1), null, bMed.Id, null), "t", null);
        Assert.Contains("medication.not_found", foreign.Detail, StringComparison.Ordinal);
        var ok = await env.Patients.AddSymptomAsync(a, a, new SymptomInput("dizzy", SymptomSeverity.Severe, env.Clock.UtcNow.AddHours(-1), null, null, "note"), "t", null);
        Assert.True(ok.Succeeded);
        Assert.Single((await env.Patients.ListSymptomsAsync(a, 10)).Value!);
        Assert.True((await env.Patients.RemoveSymptomAsync(a, a, ok.Value!.Id, "t", null)).Succeeded);
        Assert.Empty((await env.Patients.ListSymptomsAsync(a, 10)).Value!);
    }

    [Fact]
    public async Task Every_category_reports_when_it_was_last_updated_and_when_it_is_stale()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var before = (await env.Patients.GetFreshnessAsync(id)).Value!;
        Assert.All(before, f => { Assert.True(f.NeverRecorded); Assert.False(f.IsStale); Assert.Null(f.LastUpdatedAt); });

        await env.TakeMedication(id);
        await env.Patients.AddSymptomAsync(id, id, new SymptomInput("cough", SymptomSeverity.Mild, env.Clock.UtcNow.AddHours(-3), null, null, null), "t", null);
        var after = (await env.Patients.GetFreshnessAsync(id)).Value!.ToDictionary(f => f.Category);
        Assert.Equal(env.Clock.UtcNow, after[PatientDataCategory.Medications].LastUpdatedAt);
        Assert.Equal(1, after[PatientDataCategory.Medications].RecordCount);
        Assert.False(after[PatientDataCategory.Medications].IsStale);
        Assert.True(after[PatientDataCategory.Allergies].NeverRecorded);

        env.Clock.Advance(TimeSpan.FromDays(31));
        var later = (await env.Patients.GetFreshnessAsync(id)).Value!.ToDictionary(f => f.Category);
        Assert.True(later[PatientDataCategory.Medications].IsStale); // the screen must say "may be out of date"
        Assert.True(later[PatientDataCategory.Symptoms].IsStale);
        Assert.False(later[PatientDataCategory.Profile].IsStale || later[PatientDataCategory.Profile].LastUpdatedAt is not null && later[PatientDataCategory.Profile].RecordCount > 5);
    }

    [Fact]
    public async Task A_deactivated_patient_record_accepts_no_new_data()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.Patients.SetStatusAsync(id, id, PatientStatus.Inactive, "t", null);
        Assert.Equal(PatientError.Forbidden, (await env.Patients.AddConditionAsync(id, id, new ConditionInput("x", null, ConditionStatus.Active, null), "t", null)).Error);
        Assert.Equal(PatientStatus.Inactive, (await env.Patients.GetAsync(id)).Value!.Status);
        await env.Patients.SetStatusAsync(id, id, PatientStatus.Active, "t", null);
        Assert.True((await env.Patients.AddConditionAsync(id, id, new ConditionInput("x", null, ConditionStatus.Active, null), "t", null)).Succeeded);
    }

    [Fact]
    public async Task External_identifiers_are_stored_only_as_keyed_hashes_and_map_one_to_one()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = await env.Patient("demo-patient-2");
        const string value = "DEMO-LINK-0001"; // clearly fictional; real national or insurance numbers are never used in tests or seeds
        var linked = await env.Patients.LinkExternalIdentifierAsync(a, a, ExternalIdentifierScheme.Other, value, "t", null);
        Assert.True(linked.Succeeded, linked.Detail);
        Assert.False(linked.Value!.Verified);
        Assert.Equal(a, await env.Patients.FindSubjectByExternalIdentifierAsync(ExternalIdentifierScheme.Other, "demo-link-0001")); // case-insensitive
        Assert.Null(await env.Patients.FindSubjectByExternalIdentifierAsync(ExternalIdentifierScheme.InsuranceMemberId, value)); // a scheme is part of the key
        Assert.Equal(PatientError.Conflict, (await env.Patients.LinkExternalIdentifierAsync(b, b, ExternalIdentifierScheme.Other, value, "t", null)).Error);
        Assert.Equal(PatientError.Validation, (await env.Patients.LinkExternalIdentifierAsync(a, a, ExternalIdentifierScheme.NationalId, "12", "t", null)).Error);
        Assert.Equal(PatientError.Validation, (await env.Patients.LinkExternalIdentifierAsync(a, a, ExternalIdentifierScheme.Other, "bad value!", "t", null)).Error);
        Assert.DoesNotContain(value, System.Text.Json.JsonSerializer.Serialize((await env.Patients.ListExternalIdentifiersAsync(a)).Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Identifier_mapping_is_off_without_a_server_side_key()
    {
        using var env = new PEnv(settings: new() { ["Patients:IdentifierHashKey"] = "" });
        var a = await env.Patient("demo-patient");
        var r = await env.Patients.LinkExternalIdentifierAsync(a, a, ExternalIdentifierScheme.Other, "DEMO-LINK-0001", "t", null);
        Assert.Contains("not_configured", r.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Soft_deleted_records_are_purged_only_after_the_retention_period()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var c1 = (await env.Patients.AddConditionAsync(id, id, new ConditionInput("old", null, ConditionStatus.Active, null), "t", null)).Value!;
        await env.Patients.RemoveConditionAsync(id, id, c1.Id, "t", null);
        env.Clock.Advance(TimeSpan.FromDays(100));
        var c2 = (await env.Patients.AddConditionAsync(id, id, new ConditionInput("newer", null, ConditionStatus.Active, null), "t", null)).Value!;
        await env.Patients.RemoveConditionAsync(id, id, c2.Id, "t", null);
        var med = await env.TakeMedication(id);
        await env.Meds.RemoveAsync(id, id, med.Id, "t", null);

        Assert.Equal(0, await env.Patients.PurgeDeletedAsync(env.Clock.UtcNow.AddDays(-365)));
        var removed = await env.Patients.PurgeDeletedAsync(env.Clock.UtcNow.AddDays(-50)); // only the first condition is older than 50 days
        Assert.Equal(1, removed);
        Assert.True(await env.Patients.PurgeDeletedAsync(env.Clock.UtcNow.AddDays(1)) >= 2); // after retention everything soft-deleted goes
    }

    [Fact]
    public async Task Writes_are_audited_without_the_content()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.Patients.AddConditionAsync(id, id, new ConditionInput("secret-sounding condition name", null, ConditionStatus.Active, "private note"), "web", "corr-1");
        var entries = await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Take: 50));
        Assert.Contains(entries, e => e.Action == AuditActions.PatientDataUpdated && e.ResourceType == "condition");
        Assert.DoesNotContain(entries, e => e.Metadata.Values.Any(v => v.Contains("secret-sounding", StringComparison.Ordinal) || v.Contains("private note", StringComparison.Ordinal)));
    }
}
