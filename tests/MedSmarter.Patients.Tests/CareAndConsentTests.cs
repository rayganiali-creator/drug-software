using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Patients.Tests;

public class ConsentTests
{
    private static readonly string[] Products = [DataScopes.Products];

    [Fact]
    public async Task A_purpose_consent_names_nobody_and_a_person_consent_names_exactly_one()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var ok = await env.Consents.GrantAsync(id, new GrantConsentCommand(id, null, null, ConsentPurposes.ManufacturerReport, Products, env.Clock.UtcNow.AddDays(30), "v1"), "t", null);
        Assert.True(ok.Succeeded);
        var withGrantee = await env.Consents.GrantAsync(id, new GrantConsentCommand(id, env.UserId("demo-physician"), null, ConsentPurposes.ManufacturerReport, Products, env.Clock.UtcNow.AddDays(30), "v1"), "t", null);
        Assert.Equal(ConsentError.InvalidGrantee, withGrantee.Error); // a manufacturer report consent is not given to a person
        var noGrantee = await env.Consents.GrantAsync(id, new GrantConsentCommand(id, null, null, ConsentPurposes.Treatment, [DataScopes.Medications], env.Clock.UtcNow.AddDays(30), "v1"), "t", null);
        Assert.Equal(ConsentError.InvalidGrantee, noGrantee.Error);
        var self = await env.Consents.GrantAsync(id, new GrantConsentCommand(id, id, null, ConsentPurposes.Treatment, [DataScopes.Medications], env.Clock.UtcNow.AddDays(30), "v1"), "t", null);
        Assert.Equal(ConsentError.InvalidGrantee, self.Error);
    }

    [Fact]
    public async Task Only_the_subject_can_grant_and_new_scopes_and_purposes_are_validated()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = env.UserId("demo-physician");
        Assert.Equal(ConsentError.NotSubject, (await env.Consents.GrantAsync(b, new GrantConsentCommand(a, null, null, ConsentPurposes.ManufacturerReport, Products, env.Clock.UtcNow.AddDays(1), "v1"), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidScope, (await env.Consents.GrantAsync(a, new GrantConsentCommand(a, null, null, ConsentPurposes.ManufacturerReport, ["everything"], env.Clock.UtcNow.AddDays(1), "v1"), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidPurpose, (await env.Consents.GrantAsync(a, new GrantConsentCommand(a, null, null, "Marketing", Products, env.Clock.UtcNow.AddDays(1), "v1"), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidExpiry, (await env.Consents.GrantAsync(a, new GrantConsentCommand(a, null, null, ConsentPurposes.ManufacturerReport, Products, env.Clock.UtcNow.AddDays(900), "v1"), "t", null)).Error);
        Assert.True((await env.Consents.GrantAsync(a, new GrantConsentCommand(a, null, null, ConsentPurposes.AiProcessing, [DataScopes.Allergies, DataScopes.Conditions], env.Clock.UtcNow.AddDays(1), "v1"), "t", null)).Succeeded);
    }

    [Fact]
    public async Task Purpose_consent_is_checked_by_purpose_scope_expiry_and_revocation()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var eval = env.Get<IPurposeConsentEvaluator>();
        PurposeConsentCheck Check(params string[] scopes) => new(id, ConsentPurposes.AiProcessing, scopes);
        Assert.Equal("no_consent", (await eval.EvaluateAsync(Check(DataScopes.Medications))).Reason);

        var consent = await env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications, DataScopes.Allergies]);
        Assert.True((await eval.EvaluateAsync(Check(DataScopes.Medications))).Allowed);
        Assert.True((await eval.EvaluateAsync(Check(DataScopes.Medications, DataScopes.Allergies))).Allowed);
        Assert.Equal("scope_mismatch", (await eval.EvaluateAsync(Check(DataScopes.Conditions))).Reason);
        Assert.Equal("not_a_purpose_consent", (await eval.EvaluateAsync(new PurposeConsentCheck(id, ConsentPurposes.Treatment, []))).Reason);

        env.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal("consent_expired", (await eval.EvaluateAsync(Check(DataScopes.Medications))).Reason);
        env.Clock.Advance(-TimeSpan.FromDays(31));
        Assert.True((await env.Consents.RevokeAsync(id, consent.Id, "t", null)).Succeeded);
        Assert.Equal("consent_revoked", (await eval.EvaluateAsync(Check(DataScopes.Medications))).Reason); // takes effect on the next check
    }

    [Fact]
    public async Task Consent_history_keeps_both_lines_and_only_the_subject_can_revoke()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var consent = await env.Consent("demo-patient", ConsentPurposes.Monitoring, [DataScopes.Medications]);
        Assert.Equal(ConsentError.NotFound, (await env.Consents.RevokeAsync(env.UserId("demo-physician"), consent.Id, "t", null)).Error);
        env.Clock.Advance(TimeSpan.FromMinutes(5));
        var revoked = await env.Consents.RevokeAsync(id, consent.Id, "t", null);
        Assert.Equal(ConsentStatus.Revoked, revoked.Consent!.Status);
        var again = await env.Consents.RevokeAsync(id, consent.Id, "t", null); // idempotent: no second history line
        Assert.True(again.Succeeded);
        var history = await env.Consents.HistoryAsync(id);
        Assert.Equal([ConsentEventKind.Revoked, ConsentEventKind.Granted], history.Where(h => h.ConsentId == consent.Id).Select(h => h.Kind));
        Assert.All(history, h => Assert.Equal(id, h.ActorUserId));
    }

    [Fact]
    public async Task Demo_consent_seeding_is_repeatable_and_never_undoes_a_revocation()
    {
        using var env = new PEnv();
        var sara = env.UserId("demo-patient");
        var before = await env.Consents.ListGivenAsync(sara);
        Assert.NotEmpty(before);
        await env.Get<IDemoIdentitySeeder>().SeedAsync(); // a second start of the application
        Assert.Equal(before.Count, (await env.Consents.ListGivenAsync(sara)).Count);
        await env.Consents.RevokeAsync(sara, before[0].Id, "t", null);
        await env.Get<IDemoIdentitySeeder>().SeedAsync();
        Assert.Equal(ConsentStatus.Revoked, (await env.Consents.ListGivenAsync(sara)).Single(c => c.Id == before[0].Id).Status);
    }

    [Fact]
    public async Task Consent_changes_are_audited()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var c = await env.Consent("demo-patient", ConsentPurposes.ManufacturerReport, Products);
        await env.Consents.RevokeAsync(id, c.Id, "t", null);
        var actions = (await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Take: 100))).Select(e => e.Action).ToList();
        Assert.Contains(AuditActions.ConsentGranted, actions);
        Assert.Contains(AuditActions.ConsentRevoked, actions);
    }
}

public class CareRelationshipTests
{
    private static readonly string[] Medications = [DataScopes.Medications];

    [Fact]
    public async Task A_patient_asks_a_physician_and_nothing_is_open_until_the_physician_accepts()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        await env.Consent("demo-patient-2", ConsentPurposes.Treatment, Medications, "demo-physician-b");
        await env.TakeMedication(patient);

        var asked = await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null);
        Assert.True(asked.Succeeded, asked.Detail);
        Assert.Equal(CareRelationshipStatus.PendingProvider, asked.Value!.Status);
        Assert.NotNull(asked.Value.PatientConsentedAt);
        Assert.Null(asked.Value.ProviderConsentedAt);
        Assert.False((await env.Authz.AuthorizeAsync(env.As("demo-physician-b"), Permissions.PatientMedicationsRead, AccessResource.Patient(patient, "medications"), PEnv.Rc)).Allowed);

        var accepted = await env.Care.AcceptAsync(physician, asked.Value.Id, "t", null);
        Assert.Equal(CareRelationshipStatus.Active, accepted.Value!.Status);
        Assert.NotNull(accepted.Value.ProviderConsentedAt);
        Assert.True((await env.Authz.AuthorizeAsync(env.As("demo-physician-b"), Permissions.PatientMedicationsRead, AccessResource.Patient(patient, "medications"), PEnv.Rc)).Allowed);
    }

    [Fact]
    public async Task Both_a_relationship_and_a_consent_are_needed_and_ending_either_stops_access()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var actor = env.As("demo-physician-b");
        var read = () => env.Authz.AuthorizeAsync(actor, Permissions.PatientMedicationsRead, AccessResource.Patient(patient, "medications"), PEnv.Rc);

        var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Value!;
        await env.Care.AcceptAsync(physician, rel.Id, "t", null);
        var withoutConsent = await read();
        Assert.False(withoutConsent.Allowed); // relationship alone is not enough
        Assert.Equal(AccessLayer.Consent, withoutConsent.Layer);

        var consent = await env.Consent("demo-patient-2", ConsentPurposes.Treatment, Medications, "demo-physician-b");
        Assert.True((await read()).Allowed);

        await env.Consents.RevokeAsync(patient, consent.Id, "t", null);
        Assert.False((await read()).Allowed); // consent withdrawn: the next request is refused
        await env.Consent("demo-patient-2", ConsentPurposes.Treatment, Medications, "demo-physician-b");
        Assert.True((await read()).Allowed);

        var ended = await env.Care.EndAsync(patient, rel.Id, "changed doctor", "t", null);
        Assert.Equal(CareRelationshipStatus.Ended, ended.Value!.Status);
        Assert.Equal("Patient", ended.Value.EndedBy);
        var denied = await read();
        Assert.False(denied.Allowed); // relationship ended: consent no longer matters
        Assert.Equal(AccessLayer.Relationship, denied.Layer);
    }

    [Fact]
    public async Task A_provider_can_end_a_relationship_too()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Value!;
        await env.Care.AcceptAsync(physician, rel.Id, "t", null);
        var ended = await env.Care.EndAsync(physician, rel.Id, null, "t", null);
        Assert.Equal("Provider", ended.Value!.EndedBy);
        Assert.Equal(PatientError.Conflict, (await env.Care.EndAsync(patient, rel.Id, null, "t", null)).Error); // already ended
    }

    [Fact]
    public async Task A_provider_can_also_start_the_request_and_then_the_patient_decides()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var asked = await env.Care.RequestAsync(physician, new RequestCareRelationshipCommand(patient, "Treating"), "t", null);
        Assert.Equal(CareRelationshipStatus.PendingPatient, asked.Value!.Status);
        Assert.Equal("Provider", asked.Value.InitiatedBy);
        Assert.Equal(PatientError.Conflict, (await env.Care.AcceptAsync(physician, asked.Value.Id, "t", null)).Error); // you cannot accept your own request
        var declined = await env.Care.DeclineAsync(patient, asked.Value.Id, "t", null);
        Assert.Equal(CareRelationshipStatus.Declined, declined.Value!.Status);
        Assert.False((await env.Authz.AuthorizeAsync(env.As("demo-physician-b"), Permissions.PatientMedicationsRead, AccessResource.Patient(patient, "medications"), PEnv.Rc)).Allowed);
    }

    [Fact]
    public async Task Requests_are_private_to_their_two_parties_and_cannot_be_duplicated()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Value!;
        var outsider = env.UserId("demo-physician");
        Assert.Equal(PatientError.NotFound, (await env.Care.AcceptAsync(outsider, rel.Id, "t", null)).Error); // same answer as a missing request
        Assert.Equal(PatientError.NotFound, (await env.Care.EndAsync(outsider, rel.Id, null, "t", null)).Error);
        Assert.Empty(await env.Care.ListMineAsync(outsider));
        Assert.Equal(PatientError.Conflict, (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Error);
        Assert.Single(await env.Care.ListMineAsync(physician));
        // the requester can withdraw a pending request
        Assert.Equal(CareRelationshipStatus.Ended, (await env.Care.EndAsync(patient, rel.Id, null, "t", null)).Value!.Status);
        Assert.True((await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Succeeded); // a new request is possible afterwards
    }

    [Fact]
    public async Task Roles_must_fit_the_kind_of_relationship()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        Assert.Equal(PatientError.Validation, (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(env.UserId("demo-pharmacist"), "Treating"), "t", null)).Error); // a pharmacist is not a treating physician
        Assert.True((await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(env.UserId("demo-pharmacist"), "Dispensing"), "t", null)).Succeeded);
        Assert.Equal(PatientError.Validation, (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(env.UserId("demo-industry"), "Treating"), "t", null)).Error);
        Assert.Equal(PatientError.Validation, (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(patient, "Treating"), "t", null)).Error);
        Assert.Equal(PatientError.Validation, (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(env.UserId("demo-physician-b"), "Friend"), "t", null)).Error);
        Assert.Equal(PatientError.NotFound, (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(Guid.NewGuid(), "Treating"), "t", null)).Error);
        // two providers cannot start a relationship with each other
        Assert.Equal(PatientError.Validation, (await env.Care.RequestAsync(env.UserId("demo-physician"), new RequestCareRelationshipCommand(env.UserId("demo-physician-b"), "Treating"), "t", null)).Error);
    }

    [Fact]
    public async Task A_provider_cannot_open_a_relationship_with_a_patient_who_has_no_record()
    {
        using var env = new PEnv();
        var r = await env.Care.RequestAsync(env.UserId("demo-physician-b"), new RequestCareRelationshipCommand(env.UserId("demo-patient-2"), "Treating"), "t", null);
        Assert.Equal(PatientError.NotFound, r.Error); // the patient creates their own record first
    }

    [Fact]
    public async Task A_deactivated_patient_record_grants_professionals_nothing()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Value!;
        await env.Care.AcceptAsync(physician, rel.Id, "t", null);
        await env.Consent("demo-patient-2", ConsentPurposes.Treatment, Medications, "demo-physician-b");
        var read = () => env.Authz.AuthorizeAsync(env.As("demo-physician-b"), Permissions.PatientMedicationsRead, AccessResource.Patient(patient, "medications"), PEnv.Rc);
        Assert.True((await read()).Allowed);
        await env.Patients.SetStatusAsync(patient, patient, PatientStatus.Inactive, "t", null);
        Assert.False((await read()).Allowed);
        await env.Patients.SetStatusAsync(patient, patient, PatientStatus.Active, "t", null);
        Assert.True((await read()).Allowed);
    }

    [Fact]
    public async Task The_providers_patient_list_follows_active_relationships()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var users = env.Get<IUserIdentityService>();
        Assert.Empty(await users.ListRelatedPatientsAsync(env.As("demo-physician-b")));
        var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Value!;
        Assert.Empty(await users.ListRelatedPatientsAsync(env.As("demo-physician-b"))); // pending is not related yet
        await env.Care.AcceptAsync(physician, rel.Id, "t", null);
        Assert.Equal(patient, Assert.Single(await users.ListRelatedPatientsAsync(env.As("demo-physician-b"))).UserId);
        await env.Care.EndAsync(physician, rel.Id, null, "t", null);
        Assert.Empty(await users.ListRelatedPatientsAsync(env.As("demo-physician-b")));
    }

    [Fact]
    public async Task Every_step_is_audited()
    {
        using var env = new PEnv();
        var patient = await env.Patient("demo-patient-2");
        var physician = env.UserId("demo-physician-b");
        var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "web", "corr-12345678")).Value!;
        await env.Care.AcceptAsync(physician, rel.Id, "web", null);
        await env.Care.EndAsync(patient, rel.Id, null, "web", null);
        var actions = (await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: patient, Take: 50))).Select(e => e.Action).ToList();
        Assert.Contains(AuditActions.CareRelationshipRequested, actions);
        Assert.Contains(AuditActions.CareRelationshipAccepted, actions);
        Assert.Contains(AuditActions.CareRelationshipEnded, actions);
    }

    [Fact]
    public async Task The_demo_relationships_are_owned_by_the_patient_module_so_a_patient_can_end_them()
    {
        using var env = new PEnv(seedPatients: true);
        var sara = env.UserId("demo-patient");
        var physician = env.As("demo-physician");
        var read = () => env.Authz.AuthorizeAsync(physician, Permissions.PatientMedicationsRead, AccessResource.Patient(sara, "medications"), PEnv.Rc);
        Assert.True((await read()).Allowed); // seeded relationship + seeded consent
        var mine = await env.Care.ListMineAsync(sara);
        var withPhysician = mine.Single(r => r.ProviderUserId == env.UserId("demo-physician"));
        Assert.Equal(CareRelationshipStatus.Active, withPhysician.Status);
        await env.Care.EndAsync(sara, withPhysician.Id, null, "t", null);
        Assert.False((await read()).Allowed);
        await env.Get<IDemoPatientSeeder>().SeedAsync(); // a restart must not bring the ended relationship back
        Assert.False((await read()).Allowed);
    }
}
