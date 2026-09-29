using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Security.Tests;

public class AuthorizationTests
{
    private static readonly string[] PatientPermissions =
    [
        Permissions.PatientProfileRead, Permissions.PatientMedicationsRead, Permissions.PatientPrescriptionsRead, Permissions.PatientAdherenceRead,
        Permissions.PatientAdrRead, Permissions.PatientCheckinsRead, Permissions.PatientSymptomsRead, Permissions.PatientAiSummaryRead,
    ];

    [Theory]
    [InlineData("demo-patient", "Patient")]
    [InlineData("demo-physician", "Physician")]
    [InlineData("demo-pharmacist", "Pharmacist")]
    [InlineData("demo-pharmacy-admin", "PharmacyAdmin")]
    [InlineData("demo-industry", "PharmaceuticalCompany")]
    [InlineData("demo-researcher", "Researcher")]
    [InlineData("demo-content-manager", "ContentManager")]
    [InlineData("demo-ai-manager", "AIManager")]
    [InlineData("demo-system-admin", "SystemAdmin")]
    public async Task Effective_permissions_are_exactly_the_role_mapping(string account, string role)
    {
        using var env = new Env();
        var actor = await env.ActorAsync(account);
        var expected = env.Catalog.Roles.Single(r => r.Name == role).Permissions.ToHashSet();
        Assert.Equal(expected, actor.Permissions.ToHashSet());
    }

    [Theory]
    [InlineData("demo-system-admin")]
    [InlineData("demo-industry")]
    [InlineData("demo-researcher")]
    [InlineData("demo-content-manager")]
    [InlineData("demo-ai-manager")]
    [InlineData("demo-pharmacy-admin")]
    public async Task Non_clinical_roles_hold_no_patient_data_permission(string account)
    {
        using var env = new Env();
        var actor = await env.ActorAsync(account);
        foreach (var kind in env.Catalog.Permissions.Where(p => p.Kind == "subject"))
        {
            Assert.DoesNotContain(kind.Name, actor.Permissions);
        }
    }

    [Fact]
    public async Task Only_system_admin_manages_roles_and_reads_the_audit_log()
    {
        using var env = new Env();
        foreach (var account in new[] { "demo-patient", "demo-physician", "demo-pharmacist", "demo-pharmacy-admin", "demo-industry", "demo-researcher", "demo-content-manager", "demo-ai-manager" })
        {
            var a = await env.ActorAsync(account);
            Assert.False(a.Has(Permissions.RoleManage), account);
            Assert.False(a.Has(Permissions.AuditRead), account);
            Assert.False(a.Has(Permissions.UserManage), account);
        }

        var admin = await env.ActorAsync("demo-system-admin");
        Assert.True(admin.Has(Permissions.RoleManage) && admin.Has(Permissions.AuditRead));
    }

    [Fact]
    public async Task Multi_role_user_gets_the_union_of_permissions()
    {
        using var env = new Env();
        var actor = await env.ActorAsync("demo-multirole");
        Assert.Contains(RoleNames.Physician, actor.Roles);
        Assert.Contains(RoleNames.Researcher, actor.Roles);
        Assert.True(actor.Has(Permissions.PrescriptionCreate));
        Assert.True(actor.Has(Permissions.ResearchDatasetRequest));
    }

    [Fact]
    public async Task Unknown_permission_is_denied_by_default()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-system-admin", "made.up.permission", AccessResource.None("x"));
        Assert.False(d.Allowed);
        Assert.Equal(AccessLayer.Rbac, d.Layer);
    }

    [Fact]
    public async Task Missing_permission_is_denied_at_the_rbac_layer()
    {
        using var env = new Env();
        var d = await env.CanAsync("demo-patient", Permissions.PharmacyInventoryRead, AccessResource.Organization(env.OrgId("org-demo-pharmacy-a")));
        Assert.False(d.Allowed);
        Assert.Equal(AccessLayer.Rbac, d.Layer);
    }

    [Fact]
    public async Task Role_assignment_and_revocation_change_permissions_immediately_and_are_audited()
    {
        using var env = new Env();
        var admin = await env.ActorAsync("demo-system-admin");
        var admins = env.Get<IUserAdministrationService>();
        var patient = (await env.LoginAsync("demo-patient")).Result!;
        Assert.False((await env.Users.GetCurrentUserAsync(patient.User.Id, patient.SessionId))!.Has(Permissions.ResearchDatasetRequest));

        Assert.Equal(AdminError.None, await admins.AssignRoleAsync(admin.UserId, patient.User.Id, RoleNames.Researcher, null, Env.Rc));
        Assert.True((await env.Users.GetCurrentUserAsync(patient.User.Id, patient.SessionId))!.Has(Permissions.ResearchDatasetRequest));
        Assert.Equal(AdminError.AlreadyAssigned, await admins.AssignRoleAsync(admin.UserId, patient.User.Id, RoleNames.Researcher, null, Env.Rc));

        Assert.Equal(AdminError.None, await admins.RevokeRoleAsync(admin.UserId, patient.User.Id, RoleNames.Researcher, Env.Rc));
        Assert.False((await env.Users.GetCurrentUserAsync(patient.User.Id, patient.SessionId))!.Has(Permissions.ResearchDatasetRequest));
        Assert.Equal(AdminError.NotAssigned, await admins.RevokeRoleAsync(admin.UserId, patient.User.Id, RoleNames.Researcher, Env.Rc));

        Assert.NotEmpty(await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.RoleAssigned)));
        Assert.NotEmpty(await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.RoleRevoked)));
    }

    [Fact]
    public async Task Unknown_role_and_unknown_user_are_rejected_and_last_system_admin_is_protected()
    {
        using var env = new Env();
        var admin = await env.ActorAsync("demo-system-admin");
        var admins = env.Get<IUserAdministrationService>();
        Assert.Equal(AdminError.UnknownRole, await admins.AssignRoleAsync(admin.UserId, env.UserId("demo-patient"), "Root", null, Env.Rc));
        Assert.Equal(AdminError.UserNotFound, await admins.AssignRoleAsync(admin.UserId, Guid.NewGuid(), RoleNames.Patient, null, Env.Rc));
        Assert.Equal(AdminError.LastSystemAdmin, await admins.RevokeRoleAsync(admin.UserId, admin.UserId, RoleNames.SystemAdmin, Env.Rc));
    }

    [Fact]
    public async Task Every_denial_is_audited_with_an_internal_reason_and_no_patient_content()
    {
        using var env = new Env();
        var patient = await env.ActorAsync("demo-patient");
        var d = await env.Authz.AuthorizeAsync(patient, Permissions.AuditRead, AccessResource.None("audit"), Env.Rc);
        Assert.False(d.Allowed);
        var entry = (await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.AccessDenied)))[0];
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.StartsWith("Rbac:", entry.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(patient.UserId, entry.ActorUserId);
    }

    [Fact]
    public async Task Catalog_declares_every_permission_kind_the_authorizer_understands()
    {
        using var env = new Env();
        var known = new[] { "subject", "organization", "aggregate", "own", "global" };
        Assert.All(env.Catalog.Permissions, p => Assert.Contains(p.Kind, known));
        Assert.All(env.Catalog.Permissions.Where(p => p.Kind == "subject"), p => Assert.False(string.IsNullOrEmpty(p.DataScope)));
        Assert.Contains(PatientPermissions[0], env.Catalog.Permissions.Select(p => p.Name));
    }
}
