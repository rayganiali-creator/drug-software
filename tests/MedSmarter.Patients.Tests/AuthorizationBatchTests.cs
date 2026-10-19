using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// The batch methods (AuthorizeManyAsync, EvaluateManyAsync, ActiveForPatientsAsync, ForSubjectsAsync) may change HOW relationships and consents are read, never what is decided or audited:
/// for every actor and every kind of patient state, one batch call gives exactly the decisions, reasons, layers and audit entries that the single calls give one after the other.
/// </summary>
public class AuthorizationBatchTests
{
    private static readonly RequestContext Rc = new("test", "corr-batch-1234");

    private static string[] PatientAccounts => ["demo-patient", "demo-patient-2", "demo-disabled", "subject-pt-2", "subject-pt-3", "subject-pt-4", "subject-pt-5", "subject-pt-6", "subject-pt-7"];
    private static string[] Actors => ["demo-physician", "demo-physician-b", "demo-pharmacist", "demo-multirole", "demo-pharmacy-admin", "demo-patient", "demo-industry", "demo-system-admin"];

    private static async Task<List<(string Action, int Result, string? Type, string? Id, Guid? Subject, string? Source, string? Corr, string? Reason, string Meta)>> Denials(PEnv env, Guid actor)
    {
        var entries = await env.Audit.QueryAsync(new AuditQuery(ActorUserId: actor, Action: AuditActions.AccessDenied, Take: 500));
        return [.. entries.OrderBy(e => e.Sequence).Select(e => (e.Action, (int)e.Result, e.ResourceType, e.ResourceId, e.SubjectUserId, (string?)e.Source, e.CorrelationId, e.ReasonCode, string.Join(",", e.Metadata.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))))];
    }

    private static List<AccessResource> Resources(PEnv env, string permission, params Guid[] extra)
    {
        var list = PatientAccounts.Select(a => AccessResource.Patient(env.UserId(a), "adr")).ToList();
        list.Add(AccessResource.Patient(Guid.NewGuid(), "adr")); // no such patient
        list.Add(AccessResource.Patient(env.UserId("demo-patient"), "adr")); // the same patient twice
        list.Add(AccessResource.None("adr")); // no subject at all
        list.Add(AccessResource.Patient(env.UserId("demo-physician-b"), "adr")); // an actor's own id for some of the runs
        list.AddRange(extra.Select(id => AccessResource.Patient(id, "adr")));
        return permission.Length == 0 ? [] : list;
    }

    private static async Task AssertParity(PEnv env, string permission, params Guid[] extra)
    {
        foreach (var account in Actors)
        {
            var actor = env.As(account);
            var resources = Resources(env, permission, extra);
            var before = (await Denials(env, actor.UserId)).Count;
            var single = new List<AccessDecision>();
            foreach (var r in resources) { single.Add(await env.Authz.AuthorizeAsync(actor, permission, r, Rc)); }
            var afterSingle = await Denials(env, actor.UserId);
            var many = await env.Authz.AuthorizeManyAsync(actor, permission, resources, Rc);
            var afterMany = await Denials(env, actor.UserId);

            Assert.Equal(single.Count, many.Count);
            for (var i = 0; i < single.Count; i++) { Assert.True(single[i] == many[i], $"{account} resource #{i}: single {single[i]} vs batch {many[i]}"); }
            var singleAudit = afterSingle.Skip(before).ToList();
            var manyAudit = afterMany.Skip(afterSingle.Count).ToList();
            Assert.Equal(single.Count(d => !d.Allowed), singleAudit.Count); // one ACCESS_DENIED per denied resource ...
            Assert.Equal(singleAudit, manyAudit);                            // ... with identical content, in the same order
        }
    }

    [Fact]
    public async Task The_batch_gives_the_decisions_and_audit_entries_of_the_single_calls_for_every_actor_in_every_patient_state()
    {
        using var env = new PEnv(seedPatients: true);
        var physB = env.UserId("demo-physician-b");
        var treating = new[] { "demo-patient-2", "subject-pt-2", "subject-pt-3", "subject-pt-4", "subject-pt-5", "subject-pt-7" };
        var relationships = new Dictionary<string, Guid>();
        foreach (var account in treating.Append("subject-pt-6"))
        {
            var patient = await env.Patient(account);
            var rel = (await env.Care.RequestAsync(patient, new RequestCareRelationshipCommand(physB, "Treating"), "t", null)).Value!;
            relationships[account] = rel.Id;
            if (account != "subject-pt-6") { Assert.True((await env.Care.AcceptAsync(physB, rel.Id, "t", null)).Succeeded); } // subject-pt-6 stays pending
        }

        await env.Consent("demo-patient-2", ConsentPurposes.Treatment, [DataScopes.Adr], "demo-physician-b");                       // allowed
        var revoked = await env.Consent("subject-pt-2", ConsentPurposes.Treatment, [DataScopes.Adr], "demo-physician-b");           // revoked below
        await env.Consent("subject-pt-3", ConsentPurposes.Treatment, [DataScopes.Adr], "demo-physician-b", days: 1);               // expires below
        await env.Consent("subject-pt-4", ConsentPurposes.Dispensing, [DataScopes.Adr], "demo-physician-b");                       // purpose does not fit a treating link
        await env.Consent("subject-pt-5", ConsentPurposes.Treatment, [DataScopes.Medications], "demo-physician-b");                // scope does not cover adr
        await env.Consent("subject-pt-6", ConsentPurposes.Treatment, [DataScopes.Adr], "demo-physician-b");                        // consent but no active relationship
        await env.Consent("subject-pt-7", ConsentPurposes.Treatment, [DataScopes.Adr], "demo-physician-b");                        // relationship ended below
        Assert.True((await env.Consents.RevokeAsync(env.UserId("subject-pt-2"), revoked.Id, "t", null)).Succeeded);
        Assert.True((await env.Care.EndAsync(physB, relationships["subject-pt-7"], null, "t", null)).Succeeded);
        env.Clock.Advance(TimeSpan.FromDays(2));

        // two patients reached only THROUGH an organization: pharmacy A (the pharmacist's organization) and pharmacy B (another one); each consented to its own organization only
        var store = env.Get<InMemoryIdentityStore>();
        var orgA = Guid.Parse("383c0c5b-386f-5d57-a295-3b25374d9a2d");
        var orgB = Guid.Parse("578742b6-efd3-551a-9000-e250d815db67");
        async Task<Guid> OrgOnlyPatient(Guid org)
        {
            var id = Guid.NewGuid();
            var at = env.Clock.UtcNow;
            await store.AddUserAsync(new User { Id = id, Status = UserStatusValue.Active, CreatedAt = at, UpdatedAt = at }, new UserProfile { UserId = id, DisplayName = "Org-only patient", UpdatedAt = at }, null, default);
            await store.AddRoleAsync(new UserRole { UserId = id, RoleName = "Patient", AssignedAt = at }, default);
            Assert.True((await env.Patients.EnsureOwnAsync(id, "t", null)).Succeeded);
            await store.AddRelationshipAsync(new CareRelationship { PatientUserId = id, ProviderOrganizationId = org, Kind = CareRelationshipKind.Dispensing, StartedAt = at }, default);
            Assert.True((await env.Consents.GrantAsync(id, new GrantConsentCommand(id, null, org, ConsentPurposes.Dispensing, [DataScopes.Adr], at.AddDays(30), "v1"), "t", null)).Succeeded);
            return id;
        }

        var viaA = await OrgOnlyPatient(orgA);
        var viaB = await OrgOnlyPatient(orgB);

        await AssertParity(env, Permissions.AdrReview, viaA, viaB); // consent states, relationship states, organizations (pharmacist, clinic physicians, multi-role), role-level denials (patient, industry, admin)
        var pharmacist = env.As("demo-pharmacist");
        var org = await env.Authz.AuthorizeManyAsync(pharmacist, Permissions.AdrReview, [AccessResource.Patient(viaA, "adr"), AccessResource.Patient(viaB, "adr")], Rc);
        Assert.Equal([true, false], org.Select(d => d.Allowed)); // through its own organization yes; another organization's patient no
        Assert.Equal("no_care_relationship", org[1].ReasonCode);
        Assert.All(await env.Authz.AuthorizeManyAsync(env.As("demo-physician-b"), Permissions.AdrReview, [AccessResource.Patient(viaA, "adr"), AccessResource.Patient(viaB, "adr")], Rc), d => Assert.Equal("no_care_relationship", d.ReasonCode));

        // the account being disabled changes neither decision nor audit; an inactive patient RECORD ends the relationship's effect in both paths alike
        (await store.FindUserAsync(env.UserId("demo-patient-2"), default))!.Status = UserStatusValue.Disabled;
        await AssertParity(env, Permissions.AdrReview, viaA, viaB);
        (await store.FindUserAsync(env.UserId("demo-patient-2"), default))!.Status = UserStatusValue.Active;
        Assert.True((await env.Patients.SetStatusAsync(env.UserId("demo-patient-2"), env.UserId("demo-patient-2"), PatientStatus.Inactive, "t", null)).Succeeded);
        await AssertParity(env, Permissions.AdrReview, viaA, viaB);

        // the concrete expectations behind the parity (so a bug common to both paths cannot hide): physician B
        var b = env.As("demo-physician-b");
        (await store.FindUserAsync(env.UserId("demo-patient-2"), default))!.Status = UserStatusValue.Active;
        Assert.True((await env.Patients.SetStatusAsync(env.UserId("demo-patient-2"), env.UserId("demo-patient-2"), PatientStatus.Active, "t", null)).Succeeded);
        var decisions = await env.Authz.AuthorizeManyAsync(b, Permissions.AdrReview, [.. new[] { "demo-patient-2", "subject-pt-2", "subject-pt-3", "subject-pt-4", "subject-pt-5", "subject-pt-6", "subject-pt-7", "demo-patient" }.Select(a => AccessResource.Patient(env.UserId(a), "adr"))], Rc);
        Assert.Equal(["allowed", "consent_revoked", "consent_expired", "purpose_mismatch", "scope_mismatch", "no_care_relationship", "no_care_relationship", "no_care_relationship"], decisions.Select(d => d.ReasonCode));
        Assert.Equal([true, false, false, false, false, false, false, false], decisions.Select(d => d.Allowed));
    }

    [Fact]
    public async Task Non_subject_permissions_and_empty_or_odd_lists_behave_exactly_like_the_single_call()
    {
        using var env = new PEnv(seedPatients: true);
        var doctor = env.As("demo-physician");
        Assert.Empty(await env.Authz.AuthorizeManyAsync(doctor, Permissions.AdrReview, [], Rc));

        // a permission of another kind (own / organization / global) and an unknown permission take the single-call path for each resource
        foreach (var permission in new[] { Permissions.PatientAdrRead, Permissions.AdrReview, "no.such.permission" })
        {
            var resources = new[] { AccessResource.Patient(env.UserId("demo-patient"), "adr"), AccessResource.None("adr"), AccessResource.Organization(Guid.NewGuid()) };
            var before = (await Denials(env, doctor.UserId)).Count;
            var single = new List<AccessDecision>();
            foreach (var r in resources) { single.Add(await env.Authz.AuthorizeAsync(doctor, permission, r, Rc)); }
            var mid = (await Denials(env, doctor.UserId)).Count;
            var many = await env.Authz.AuthorizeManyAsync(doctor, permission, resources, Rc);
            var after = (await Denials(env, doctor.UserId)).Count;
            Assert.Equal(single, many);
            Assert.Equal(mid - before, after - mid);
        }
    }

    [Fact]
    public async Task Reading_consents_and_relationships_together_returns_exactly_what_reading_them_one_by_one_returns()
    {
        using var env = new PEnv(seedPatients: true);
        var physB = env.UserId("demo-physician-b");
        var p2 = await env.Patient("demo-patient-2");
        var other = await env.Patient("subject-pt-2");
        var rel = (await env.Care.RequestAsync(p2, new RequestCareRelationshipCommand(physB, "Treating"), "t", null)).Value!;
        Assert.True((await env.Care.AcceptAsync(physB, rel.Id, "t", null)).Succeeded);
        var rel2 = (await env.Care.RequestAsync(other, new RequestCareRelationshipCommand(physB, "Treating"), "t", null)).Value!;
        Assert.True((await env.Care.AcceptAsync(physB, rel2.Id, "t", null)).Succeeded);
        Assert.True((await env.Care.EndAsync(physB, rel2.Id, null, "t", null)).Succeeded); // an ended relationship is in neither result
        await env.Consent("demo-patient-2", ConsentPurposes.Treatment, [DataScopes.Adr], "demo-physician-b");

        var source = env.Get<ICareRelationshipSource>();
        var subjects = new[] { p2, other, env.UserId("demo-patient"), Guid.NewGuid(), p2 }; // includes a repeat and an unknown id
        var together = await source.ActiveForPatientsAsync(subjects);
        var oneByOne = new List<ActiveCareLink>();
        foreach (var s in subjects.Distinct()) { oneByOne.AddRange(await source.ActiveForPatientAsync(s)); }
        Assert.Equal(oneByOne.OrderBy(l => l.PatientUserId).ThenBy(l => l.ProviderUserId).ThenBy(l => l.ProviderOrganizationId).ThenBy(l => l.Kind),
            together.OrderBy(l => l.PatientUserId).ThenBy(l => l.ProviderUserId).ThenBy(l => l.ProviderOrganizationId).ThenBy(l => l.Kind));
        Assert.Empty(await source.ActiveForPatientsAsync([]));

        var evaluator = env.Get<IConsentEvaluator>();
        var physician = env.As("demo-physician-b");
        var checks = subjects.Select(s => new ConsentCheck(s, physB, physician.OrganizationIds.ToArray(), DataScopes.Adr, [ConsentPurposes.Treatment, ConsentPurposes.MedicationReview])).ToList();
        var batch = await evaluator.EvaluateManyAsync(checks);
        var single = new List<ConsentDecision>();
        foreach (var c in checks) { single.Add(await evaluator.EvaluateAsync(c)); }
        Assert.Equal(single, batch);
        Assert.Empty(await evaluator.EvaluateManyAsync([]));
    }
}
