using MedSmarter.Modules.Audit;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Security.Tests;

public class ConsentTests
{
    private static GrantConsentCommand Cmd(Env env, Guid subject, Guid? user = null, Guid? org = null, string purpose = ConsentPurposes.Treatment, string[]? scope = null, int days = 30) =>
        new(subject, user, org, purpose, scope ?? [DataScopes.Profile], env.Clock.UtcNow.AddDays(days), "v1");

    [Fact]
    public async Task Only_the_subject_can_grant_consent_on_their_own_data()
    {
        using var env = new Env();
        var victim = env.UserId("demo-patient");
        var attacker = env.UserId("demo-patient-2");
        var outcome = await env.Consents.GrantAsync(attacker, Cmd(env, victim, attacker), "test", null);
        Assert.Equal(ConsentError.NotSubject, outcome.Error);
    }

    [Theory]
    [InlineData("Marketing", ConsentError.InvalidPurpose)]
    [InlineData("", ConsentError.InvalidPurpose)]
    public async Task Invalid_purpose_is_rejected(string purpose, ConsentError expected)
    {
        using var env = new Env();
        var s = env.UserId("demo-patient");
        Assert.Equal(expected, (await env.Consents.GrantAsync(s, Cmd(env, s, env.UserId("demo-physician"), purpose: purpose), "test", null)).Error);
    }

    [Fact]
    public async Task Invalid_scope_grantee_and_expiry_are_rejected()
    {
        using var env = new Env();
        var s = env.UserId("demo-patient");
        var doc = env.UserId("demo-physician");
        Assert.Equal(ConsentError.InvalidScope, (await env.Consents.GrantAsync(s, Cmd(env, s, doc, scope: ["everything"]), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidScope, (await env.Consents.GrantAsync(s, Cmd(env, s, doc, scope: []), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidGrantee, (await env.Consents.GrantAsync(s, Cmd(env, s), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidGrantee, (await env.Consents.GrantAsync(s, Cmd(env, s, doc, env.OrgId("org-demo-clinic")), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidGrantee, (await env.Consents.GrantAsync(s, Cmd(env, s, s), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidExpiry, (await env.Consents.GrantAsync(s, Cmd(env, s, doc, days: -1), "t", null)).Error);
        Assert.Equal(ConsentError.InvalidExpiry, (await env.Consents.GrantAsync(s, Cmd(env, s, doc, days: 400), "t", null)).Error);
    }

    [Fact]
    public async Task Consent_status_reflects_active_expired_and_revoked()
    {
        using var env = new Env();
        var s = env.UserId("demo-patient-2");
        var list = await env.Consents.ListGivenAsync(s);
        Assert.Contains(list, c => c.Status == ConsentStatus.Expired);
        var granted = (await env.Consents.GrantAsync(s, Cmd(env, s, env.UserId("demo-physician-b")), "t", null)).Consent!;
        Assert.Equal(ConsentStatus.Active, granted.Status);
        var revoked = (await env.Consents.RevokeAsync(s, granted.Id, "t", null)).Consent!;
        Assert.Equal(ConsentStatus.Revoked, revoked.Status);
    }

    [Fact]
    public async Task Someone_else_cannot_revoke_a_consent_and_gets_the_same_answer_as_not_found()
    {
        using var env = new Env();
        var sara = env.UserId("demo-patient");
        var consent = (await env.Consents.ListGivenAsync(sara))[0];
        var other = await env.Consents.RevokeAsync(env.UserId("demo-patient-2"), consent.Id, "t", null);
        var missing = await env.Consents.RevokeAsync(env.UserId("demo-patient-2"), Guid.NewGuid(), "t", null);
        Assert.Equal(ConsentError.NotFound, other.Error);
        Assert.Equal(other.Error, missing.Error);
        Assert.Equal(ConsentStatus.Active, (await env.Consents.ListGivenAsync(sara)).First(c => c.Id == consent.Id).Status);
    }

    [Fact]
    public async Task Consent_grant_and_revoke_are_audited()
    {
        using var env = new Env();
        var s = env.UserId("demo-patient-2");
        var c = (await env.Consents.GrantAsync(s, Cmd(env, s, env.UserId("demo-physician-b")), "web", "corr-12345678")).Consent!;
        await env.Consents.RevokeAsync(s, c.Id, "web", "corr-12345678");
        Assert.NotEmpty(await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.ConsentGranted, SubjectUserId: s)));
        Assert.NotEmpty(await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.ConsentRevoked, SubjectUserId: s)));
    }

    [Fact]
    public async Task Evaluation_reports_precise_internal_reasons()
    {
        using var env = new Env();
        var evaluator = env.Get<IConsentEvaluator>();
        var sara = env.UserId("demo-patient");
        var none = await evaluator.EvaluateAsync(new ConsentCheck(sara, env.UserId("demo-physician-b"), [], DataScopes.Profile, [ConsentPurposes.Treatment]), default);
        Assert.Equal("no_consent", none.Reason);
        var purpose = await evaluator.EvaluateAsync(new ConsentCheck(sara, env.UserId("demo-physician"), [], DataScopes.Profile, [ConsentPurposes.Research]), default);
        Assert.Equal("purpose_mismatch", purpose.Reason);
        var ok = await evaluator.EvaluateAsync(new ConsentCheck(sara, env.UserId("demo-physician"), [], DataScopes.Profile, [ConsentPurposes.Treatment]), default);
        Assert.True(ok.Allowed);
    }
}

public class AuditTests
{
    [Fact]
    public async Task Login_and_logout_are_audited_without_tokens()
    {
        using var env = new Env();
        var r = (await env.LoginAsync("demo-patient")).Result!;
        await env.Auth.LogoutAsync(r.User.Id, r.SessionId, Env.Rc);
        var all = await env.AuditReader.QueryAsync(new AuditQuery(ActorUserId: r.User.Id));
        Assert.Contains(all, e => e.Action == AuditActions.Login && e.Result == AuditResult.Success);
        Assert.Contains(all, e => e.Action == AuditActions.Logout);
        var dump = string.Join('|', all.Select(e => $"{e.Action}{e.ResourceId}{e.ReasonCode}{string.Join(',', e.Metadata.Values)}"));
        Assert.DoesNotContain(r.Tokens.AccessToken, dump, StringComparison.Ordinal);
        Assert.DoesNotContain(r.Tokens.RefreshToken, dump, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_logins_are_audited_without_the_attempted_identifier()
    {
        using var env = new Env();
        await env.LoginAsync("attacker-guess-1");
        var e = (await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.LoginFailed))).Single();
        Assert.Equal("InvalidCredentials", e.ReasonCode);
        Assert.DoesNotContain("attacker-guess-1", string.Join(',', e.Metadata.Values) + e.ResourceId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_metadata_is_dropped_or_redacted_and_size_is_limited()
    {
        using var env = new Env();
        var writer = env.Get<IAuditWriter>();
        var jwtLike = "aaaaaaaaaaaa.bbbbbbbbbbbb.cccccccccccc";
        await writer.WriteAsync(new AuditEvent("TEST", AuditResult.Success, null, Metadata: new Dictionary<string, string>
        {
            ["password"] = "hunter2",
            ["refreshToken"] = "abc",
            ["Authorization"] = "Bearer x",
            ["note"] = jwtLike,
            ["ok"] = "fine",
            ["long"] = new string('z', 5000),
        }));
        var e = (await env.AuditReader.QueryAsync(new AuditQuery(Action: "TEST"))).Single();
        Assert.False(e.Metadata.ContainsKey("password"));
        Assert.False(e.Metadata.ContainsKey("refreshToken"));
        Assert.False(e.Metadata.ContainsKey("Authorization"));
        Assert.DoesNotContain(jwtLike, e.Metadata["note"], StringComparison.Ordinal);
        Assert.Equal("fine", e.Metadata["ok"]);
        Assert.True(e.Metadata["long"].Length <= 200);
    }

    [Fact]
    public async Task Hash_chain_is_intact_and_detects_tampering()
    {
        using var env = new Env();
        await env.LoginAsync("demo-patient");
        await env.LoginAsync("demo-physician");
        await env.LoginAsync("nobody");
        Assert.True(await env.AuditReader.VerifyChainAsync());
        env.Get<InMemoryAuditStore>().TamperForTest(1, e => e with { Result = AuditResult.Success, ActorUserId = Guid.NewGuid() });
        Assert.False(await env.AuditReader.VerifyChainAsync());
    }

    [Fact]
    public async Task Entries_are_ordered_and_sequenced()
    {
        using var env = new Env();
        await env.LoginAsync("demo-patient");
        await env.LoginAsync("demo-physician");
        var all = await env.AuditReader.QueryAsync(new AuditQuery(Take: 500));
        Assert.Equal(all.Select(e => e.Sequence).OrderByDescending(x => x), all.Select(e => e.Sequence));
        Assert.Equal(all.Count, all.Select(e => e.Sequence).Distinct().Count());
    }
}
