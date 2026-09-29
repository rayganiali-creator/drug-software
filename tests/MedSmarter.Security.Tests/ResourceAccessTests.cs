using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Security.Tests;

public class ResourceAccessTests
{
    [Fact]
    public async Task Patient_can_read_own_data()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-patient", Permissions.PatientMedicationsRead, AccessResource.Patient(env.UserId("demo-patient")));
        Assert.True(d.Allowed);
        Assert.Equal(AccessLayer.Ownership, d.Layer);
    }

    [Fact]
    public async Task Patient_cannot_read_another_patients_data()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-patient", Permissions.PatientMedicationsRead, AccessResource.Patient(env.UserId("demo-patient-2")));
        Assert.False(d.Allowed);
        Assert.Equal(AccessLayer.Relationship, d.Layer);
    }

    [Fact]
    public async Task Physician_with_relationship_and_consent_can_read()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-physician", Permissions.PatientMedicationsRead, AccessResource.Patient(env.UserId("demo-patient")));
        Assert.True(d.Allowed);
        Assert.Equal(AccessLayer.Consent, d.Layer);
    }

    [Fact]
    public async Task Physician_without_relationship_is_denied_even_though_the_role_allows_it()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-physician-b", Permissions.PatientMedicationsRead, AccessResource.Patient(env.UserId("demo-patient")));
        Assert.False(d.Allowed);
        Assert.Equal(AccessLayer.Relationship, d.Layer);
        Assert.Equal("no_care_relationship", d.ReasonCode);
    }

    [Fact]
    public async Task Expired_consent_denies_access()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-physician", Permissions.PatientMedicationsRead, AccessResource.Patient(env.UserId("demo-patient-2")));
        Assert.False(d.Allowed);
        Assert.Equal(AccessLayer.Consent, d.Layer);
        Assert.Equal("consent_expired", d.ReasonCode);
    }

    [Fact]
    public async Task Consent_that_expires_later_stops_working_when_time_passes()
    {
        using var env = new Env();
        var pid = AccessResource.Patient(env.UserId("demo-patient"));
        Assert.True((await env.CanAsync("demo-physician", Permissions.PatientProfileRead, pid)).Allowed);
        env.Clock.Advance(TimeSpan.FromDays(336));
        Assert.False((await env.CanAsync("demo-physician", Permissions.PatientProfileRead, pid)).Allowed);
    }

    [Fact]
    public async Task Consent_scope_limits_which_data_a_pharmacist_sees()
    {
        using var env = new Env();
        var sara = AccessResource.Patient(env.UserId("demo-patient"));
        Assert.True((await env.CanAsync("demo-pharmacist", Permissions.PatientPrescriptionsRead, sara)).Allowed);
        var adherence = await env.CanAsync("demo-pharmacist", Permissions.PatientAdherenceRead, sara);
        Assert.False(adherence.Allowed);
        Assert.Equal("scope_mismatch", adherence.ReasonCode);
    }

    [Fact]
    public async Task Pharmacist_without_relationship_is_denied()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-pharmacist", Permissions.PatientMedicationsRead, AccessResource.Patient(env.UserId("subject-pt-4")));
        Assert.False(d.Allowed);
        Assert.Equal("no_care_relationship", d.ReasonCode);
    }

    [Fact]
    public async Task Revoking_a_consent_denies_access_immediately()
    {
        using var env = new Env();
        var sara = env.UserId("demo-patient");
        var res = AccessResource.Patient(sara);
        Assert.True((await env.CanAsync("demo-physician", Permissions.PatientProfileRead, res)).Allowed);
        var consent = (await env.Consents.ListGivenAsync(sara)).Single(c => c.GranteeUserId == env.UserId("demo-physician"));
        Assert.True((await env.Consents.RevokeAsync(sara, consent.Id, "test", null)).Succeeded);
        var d = await env.CanAsync("demo-physician", Permissions.PatientProfileRead, res);
        Assert.False(d.Allowed);
        Assert.Equal("consent_revoked", d.ReasonCode);
    }

    [Fact]
    public async Task Granting_a_new_consent_enables_access()
    {
        using var env = new Env();
        var p2 = env.UserId("demo-patient-2");
        var physician = env.UserId("demo-physician");
        var res = AccessResource.Patient(p2);
        Assert.False((await env.CanAsync("demo-physician", Permissions.PatientProfileRead, res)).Allowed);
        var outcome = await env.Consents.GrantAsync(p2, new GrantConsentCommand(p2, physician, null, ConsentPurposes.Treatment, [DataScopes.Profile], env.Clock.UtcNow.AddDays(30), "v1"), "test", null);
        Assert.True(outcome.Succeeded);
        Assert.True((await env.CanAsync("demo-physician", Permissions.PatientProfileRead, res)).Allowed);
        // scope is still limited to what was granted
        Assert.False((await env.CanAsync("demo-physician", Permissions.PatientAdrRead, res)).Allowed);
    }

    [Fact]
    public async Task Organization_consent_applies_to_members_only()
    {
        using var env = new Env();
        var sara = AccessResource.Patient(env.UserId("demo-patient"));
        // pharmacist is a member of Pharmacy A (org consent: prescriptions) and also has a personal consent
        Assert.True((await env.CanAsync("demo-pharmacist", Permissions.PatientPrescriptionsRead, sara)).Allowed);
        // pharmacy admin belongs to the org but holds no patient-data permission at all
        Assert.Equal(AccessLayer.Rbac, (await env.CanAsync("demo-pharmacy-admin", Permissions.PatientPrescriptionsRead, sara)).Layer);
    }

    [Fact]
    public async Task Organization_scoped_permissions_require_membership()
    {
        using var env = new Env();
        var a = env.OrgId("org-demo-pharmacy-a");
        var b = env.OrgId("org-demo-pharmacy-b");
        Assert.True((await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyInventoryRead, AccessResource.Organization(a))).Allowed);
        var other = await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyInventoryRead, AccessResource.Organization(b));
        Assert.False(other.Allowed);
        Assert.Equal(AccessLayer.Organization, other.Layer);
        Assert.False((await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyInventoryRead, AccessResource.Organization(Guid.NewGuid()))).Allowed);
        Assert.False((await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyInventoryRead, AccessResource.None("organization"))).Allowed);
    }

    [Fact]
    public async Task Own_kind_permissions_cannot_target_someone_else()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-patient", Permissions.ConsentRead, AccessResource.Patient(env.UserId("demo-patient-2")));
        Assert.False(d.Allowed);
        Assert.Equal(AccessLayer.Ownership, d.Layer);
    }

    [Fact]
    public async Task Subject_permission_without_a_subject_is_denied()
    {
        using var env = new Env();
        Assert.False((await env.CanAsync("demo-physician", Permissions.PatientProfileRead, AccessResource.None("patient"))).Allowed);
    }

    [Fact]
    public async Task Ended_care_relationship_no_longer_grants_access()
    {
        using var env = new Env();
        var pid = env.UserId("subject-pt-6");
        var physician = await env.ActorAsync("demo-physician");
        Assert.True((await env.Authz.AuthorizeAsync(physician, Permissions.PatientProfileRead, AccessResource.Patient(pid), Env.Rc)).Allowed);
        foreach (var r in await env.Get<MedSmarter.Modules.Identity.IIdentityStore>().RelationshipsForPatientAsync(pid, default))
        {
            r.EndedAt = env.Clock.UtcNow;
        }

        Assert.False((await env.Authz.AuthorizeAsync(physician, Permissions.PatientProfileRead, AccessResource.Patient(pid), Env.Rc)).Allowed);
    }

    [Fact]
    public async Task Related_patient_listing_shows_only_own_relationships()
    {
        using var env = new Env();
        var physician = await env.ActorAsync("demo-physician");
        Assert.Equal(8, (await env.Users.ListRelatedPatientsAsync(physician)).Count);
        var b = await env.ActorAsync("demo-physician-b");
        Assert.Empty(await env.Users.ListRelatedPatientsAsync(b));
    }

    [Fact]
    public async Task Pharmacy_admin_reaches_a_patients_prescriptions_only_through_the_organization_consent()
    {
        using var env = new Env();
        var pharmacyA = env.OrgId("org-demo-pharmacy-a");
        var sara = env.UserId("demo-patient");
        var ali = env.UserId("demo-patient-2");
        Assert.True((await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyPrescriptionsRead, AccessResource.OrgPatient(pharmacyA, sara, "prescriptions"))).Allowed);
        var noRelationship = await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyPrescriptionsRead, AccessResource.OrgPatient(pharmacyA, ali, "prescriptions"));
        Assert.False(noRelationship.Allowed);
        Assert.Equal("no_care_relationship", noRelationship.ReasonCode);
        var otherOrg = await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyPrescriptionsRead, AccessResource.OrgPatient(env.OrgId("org-demo-pharmacy-b"), sara, "prescriptions"));
        Assert.Equal(AccessLayer.Organization, otherOrg.Layer);
        Assert.False(otherOrg.Allowed);
    }

    [Fact]
    public async Task Revoking_the_organization_consent_stops_pharmacy_access_immediately()
    {
        using var env = new Env();
        var sara = env.UserId("demo-patient");
        var pharmacyA = env.OrgId("org-demo-pharmacy-a");
        var res = AccessResource.OrgPatient(pharmacyA, sara, "prescriptions");
        Assert.True((await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyPrescriptionsRead, res)).Allowed);
        var consent = (await env.Consents.ListGivenAsync(sara)).Single(c => c.GranteeOrganizationId == pharmacyA);
        Assert.True((await env.Consents.RevokeAsync(sara, consent.Id, "test", null)).Succeeded);
        var d = await env.CanAsync("demo-pharmacy-admin", Permissions.PharmacyPrescriptionsRead, res);
        Assert.False(d.Allowed);
        Assert.Equal("consent_revoked", d.ReasonCode);
    }
}
