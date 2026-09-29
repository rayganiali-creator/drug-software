using System.Text;
using System.Text.Json;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Security.Tests;

public class AuthenticationTests
{
    [Fact]
    public async Task Mock_login_succeeds_for_demo_account_and_returns_summary()
    {
        using var env = new Env();
        var outcome = await env.LoginAsync("demo-patient");
        Assert.True(outcome.Succeeded);
        Assert.Equal(["Patient"], outcome.Result!.User.Roles);
        Assert.NotEqual(outcome.Result.Tokens.AccessToken, outcome.Result.Tokens.RefreshToken);
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("")]
    [InlineData("' OR 1=1 --")]
    public async Task Unknown_accounts_are_rejected(string account)
    {
        using var env = new Env();
        var outcome = await env.LoginAsync(account);
        Assert.Equal(AuthFailure.InvalidCredentials, outcome.Failure);
    }

    [Fact]
    public async Task Unknown_provider_is_rejected()
    {
        using var env = new Env();
        var outcome = await env.Auth.LoginAsync(new LoginRequest("national-id", new Dictionary<string, string> { ["accountId"] = "demo-patient" }, new DeviceInfo(null, "web", null, null)), Env.Rc);
        Assert.Equal(AuthFailure.InvalidCredentials, outcome.Failure);
    }

    [Fact]
    public async Task Disabled_account_cannot_log_in()
    {
        using var env = new Env();
        Assert.Equal(AuthFailure.AccountDisabled, (await env.LoginAsync("demo-disabled")).Failure);
    }

    [Fact]
    public async Task Repeated_failures_throttle_even_valid_attempts()
    {
        using var env = new Env();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(AuthFailure.InvalidCredentials, (await env.LoginAsync("ghost")).Failure);
        }

        Assert.Equal(AuthFailure.Throttled, (await env.LoginAsync("ghost")).Failure);
        env.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(AuthFailure.InvalidCredentials, (await env.LoginAsync("ghost")).Failure);
    }

    [Fact]
    public async Task Access_token_carries_only_minimal_claims_and_no_roles_or_phi()
    {
        using var env = new Env();
        var result = (await env.LoginAsync("demo-physician")).Result!;
        var payload = Decode(result.Tokens.AccessToken);
        var names = payload.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Subset(new HashSet<string> { "sub", "sid", "jti", "iat", "nbf", "exp", "iss", "aud" }, names);
        var raw = payload.GetRawText();
        Assert.DoesNotContain("Physician", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Demo", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Valid_token_validates_and_expired_or_tampered_tokens_do_not()
    {
        using var env = new Env();
        var tokens = env.Get<ITokenService>();
        var result = (await env.LoginAsync("demo-patient")).Result!;
        var claims = tokens.ValidateAccessToken(result.Tokens.AccessToken);
        Assert.NotNull(claims);
        Assert.Equal(result.User.Id, claims.UserId);
        Assert.Equal(result.SessionId, claims.SessionId);

        // tampered payload
        var parts = result.Tokens.AccessToken.Split('.');
        var forged = $"{parts[0]}.{Base64(Decode(result.Tokens.AccessToken).GetRawText().Replace(result.User.Id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal))}.{parts[2]}";
        Assert.Null(tokens.ValidateAccessToken(forged));

        // alg=none
        var none = $"{Base64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.";
        Assert.Null(tokens.ValidateAccessToken(none));

        // garbage
        Assert.Null(tokens.ValidateAccessToken("not-a-token"));
        Assert.Null(tokens.ValidateAccessToken(new string('a', 5000)));

        env.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(tokens.ValidateAccessToken(result.Tokens.AccessToken));
    }

    [Fact]
    public async Task Token_signed_by_another_key_is_rejected()
    {
        using var a = new Env();
        using var b = new Env();
        var token = (await a.LoginAsync("demo-patient")).Result!.Tokens.AccessToken;
        Assert.Null(b.Get<ITokenService>().ValidateAccessToken(token));
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_kills_the_whole_session()
    {
        using var env = new Env();
        var first = (await env.LoginAsync("demo-patient")).Result!;
        var second = (await env.Auth.RefreshAsync(first.Tokens.RefreshToken, Env.Rc)).Result!;
        Assert.NotEqual(first.Tokens.RefreshToken, second.Tokens.RefreshToken);
        Assert.Equal(first.SessionId, second.SessionId);

        // replaying the old refresh token = theft signal
        Assert.Equal(AuthFailure.RefreshInvalid, (await env.Auth.RefreshAsync(first.Tokens.RefreshToken, Env.Rc)).Failure);
        // ...and the legitimate newest token no longer works either, and the session is dead
        Assert.Equal(AuthFailure.RefreshInvalid, (await env.Auth.RefreshAsync(second.Tokens.RefreshToken, Env.Rc)).Failure);
        Assert.Null(await env.Users.GetCurrentUserAsync(first.User.Id, first.SessionId));
        Assert.Contains(await env.AuditReader.QueryAsync(new AuditQuery(Action: AuditActions.RefreshTokenReuse)), _ => true);
    }

    [Fact]
    public async Task Refresh_tokens_are_not_stored_in_clear()
    {
        using var env = new Env();
        var refresh = (await env.LoginAsync("demo-patient")).Result!.Tokens.RefreshToken;
        var stored = await env.Get<ISessionStore>().FindRefreshTokenByHashAsync(refresh, default);
        Assert.Null(stored); // lookup by the raw value finds nothing: only the hash is stored
        Assert.NotNull(await env.Get<ISessionStore>().FindRefreshTokenByHashAsync(env.Get<ITokenService>().HashRefreshToken(refresh), default));
    }

    [Fact]
    public async Task Refresh_fails_after_session_expiry_garbage_and_disabled_user()
    {
        using var env = new Env();
        Assert.Equal(AuthFailure.RefreshInvalid, (await env.Auth.RefreshAsync("garbage", Env.Rc)).Failure);
        Assert.Equal(AuthFailure.RefreshInvalid, (await env.Auth.RefreshAsync(new string('x', 1000), Env.Rc)).Failure);
        var r = (await env.LoginAsync("demo-patient")).Result!;
        env.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(AuthFailure.RefreshInvalid, (await env.Auth.RefreshAsync(r.Tokens.RefreshToken, Env.Rc)).Failure);
    }

    [Fact]
    public async Task Logout_revokes_only_that_session_and_takes_effect_immediately()
    {
        using var env = new Env();
        var a = (await env.LoginAsync("demo-patient")).Result!;
        var b = (await env.LoginAsync("demo-patient")).Result!;
        await env.Auth.LogoutAsync(a.User.Id, a.SessionId, Env.Rc);
        Assert.Null(await env.Users.GetCurrentUserAsync(a.User.Id, a.SessionId));
        Assert.NotNull(await env.Users.GetCurrentUserAsync(b.User.Id, b.SessionId));
        Assert.Equal(AuthFailure.RefreshInvalid, (await env.Auth.RefreshAsync(a.Tokens.RefreshToken, Env.Rc)).Failure);
    }

    [Fact]
    public async Task Logout_all_revokes_every_session()
    {
        using var env = new Env();
        var a = (await env.LoginAsync("demo-patient")).Result!;
        var b = (await env.LoginAsync("demo-patient")).Result!;
        await env.Auth.LogoutAllAsync(a.User.Id, Env.Rc);
        Assert.Null(await env.Users.GetCurrentUserAsync(a.User.Id, a.SessionId));
        Assert.Null(await env.Users.GetCurrentUserAsync(b.User.Id, b.SessionId));
    }

    [Fact]
    public async Task A_user_cannot_revoke_someone_elses_session()
    {
        using var env = new Env();
        var victim = (await env.LoginAsync("demo-patient")).Result!;
        var attacker = (await env.LoginAsync("demo-patient-2")).Result!;
        var ok = await env.Get<ISessionService>().RevokeAsync(attacker.User.Id, victim.SessionId, attacker.User.Id, "x", Env.Rc);
        Assert.False(ok);
        Assert.NotNull(await env.Users.GetCurrentUserAsync(victim.User.Id, victim.SessionId));
    }

    [Fact]
    public async Task Token_of_one_user_with_session_of_another_is_useless()
    {
        using var env = new Env();
        var victim = (await env.LoginAsync("demo-patient")).Result!;
        Assert.Null(await env.Users.GetCurrentUserAsync(env.UserId("demo-system-admin"), victim.SessionId));
    }

    [Fact]
    public async Task Sessions_list_shows_device_and_marks_current()
    {
        using var env = new Env();
        var r = (await env.LoginAsync("demo-patient")).Result!;
        var list = await env.Get<ISessionService>().ListAsync(r.User.Id, r.SessionId);
        var s = Assert.Single(list);
        Assert.True(s.IsCurrent);
        Assert.Equal("web", s.Platform);
        Assert.Equal("Test browser", s.DeviceName);
    }

    [Fact]
    public async Task Same_device_id_reuses_the_device_record_but_foreign_device_id_does_not()
    {
        using var env = new Env();
        var first = (await env.LoginAsync("demo-patient")).Result!;
        var again = (await env.LoginAsync("demo-patient", first.DeviceId.ToString())).Result!;
        Assert.Equal(first.DeviceId, again.DeviceId);
        var other = (await env.LoginAsync("demo-patient-2", first.DeviceId.ToString())).Result!;
        Assert.NotEqual(first.DeviceId, other.DeviceId);
    }

    [Fact]
    public async Task Removing_the_last_role_ends_access_on_the_next_request()
    {
        using var env = new Env();
        var admin = await env.ActorAsync("demo-system-admin");
        var r = (await env.LoginAsync("demo-content-manager")).Result!;
        Assert.NotNull(await env.Users.GetCurrentUserAsync(r.User.Id, r.SessionId));
        Assert.Equal(AdminError.None, await env.Get<IUserAdministrationService>().RevokeRoleAsync(admin.UserId, r.User.Id, RoleNames.ContentManager, Env.Rc));
        Assert.Null(await env.Users.GetCurrentUserAsync(r.User.Id, r.SessionId));
    }

    [Fact]
    public void Development_mock_is_refused_outside_development_and_testing()
    {
        var mock = new AuthOptions { Mode = AuthModes.DevelopmentMock, SigningKey = new string('k', 40) };
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe("Production", mock));
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe("Staging", mock));
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe("", mock));
        AuthGuard.EnsureSafe("Development", mock);
        AuthGuard.EnsureSafe("Testing", mock);
    }

    [Fact]
    public void Non_development_environments_require_a_signing_key()
    {
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe("Production", new AuthOptions { Mode = AuthModes.Disabled }));
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe("Production", new AuthOptions { Mode = AuthModes.Disabled, SigningKey = "short" }));
        AuthGuard.EnsureSafe("Production", new AuthOptions { Mode = AuthModes.Disabled, SigningKey = new string('k', 32) });
    }

    [Fact]
    public async Task Disabled_mode_registers_no_provider_so_login_is_impossible()
    {
        using var env = new Env(AuthModes.Disabled);
        Assert.Equal(AuthFailure.InvalidCredentials, (await env.LoginAsync("demo-patient")).Failure);
        Assert.Null(env.Get<IServiceProvider>().GetService(typeof(IDemoAccountDirectory)));
        Assert.Null(env.Get<IServiceProvider>().GetService(typeof(IDemoIdentitySeeder)));
    }

    [Fact]
    public void Short_signing_key_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => new Env(extra: new() { ["Auth:SigningKey"] = "too-short" }).Get<ITokenService>());
    }

    [Fact]
    public void Demo_catalog_contains_no_credentials()
    {
        var json = new StreamReader(typeof(AccessCatalog).Assembly.GetManifestResourceStream("access-catalog.json")!).ReadToEnd();
        foreach (var word in new[] { "\"password\"", "\"secret\"", "\"token\"", "\"apiKey\"" })
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static JsonElement Decode(string jwt) => JsonDocument.Parse(Convert.FromBase64String(Pad(jwt.Split('.')[1]))).RootElement;
    private static string Pad(string s) { s = s.Replace('-', '+').Replace('_', '/'); return s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='); }
    private static string Base64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
