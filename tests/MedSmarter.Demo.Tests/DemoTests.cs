using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedSmarter.Api.Demo;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Patients;
using MedSmarter.Modules.ProductTrace;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MedSmarter.Demo.Tests;

internal static class Cfg
{
    public const string Code = "demo-code-for-tests-only"; // a test literal, not a real secret
    public const string Origin = "https://medsmarter-demo.example.test";
    public const string SigningKey = "unit-test-signing-key-0123456789-abcdef"; // test literal

    public static Dictionary<string, string?> ValidDemo() => new()
    {
        ["Demo:AccessCode"] = Code,
        ["Persistence:Provider"] = "InMemory",
        ["Cors:AllowedOrigins:0"] = Origin,
        ["Ai:Provider"] = "Mock",
        ["Auth:DemoAllowedAccounts:0"] = "demo-patient",
        ["Auth:DemoAllowedAccounts:1"] = "demo-physician",
    };

    public static IConfiguration Of(Dictionary<string, string?> d) => new ConfigurationBuilder().AddInMemoryCollection(d).Build();
}

/// <summary>Boots the real Host as the dedicated Demo environment (or another one) with in-memory storage and closed infrastructure ports.</summary>
public sealed class HostFactory(string environment, Dictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    static HostFactory()
    {
        foreach (var (k, v) in new Dictionary<string, string>
        {
            ["ConnectionStrings__Postgres"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=not-a-real-secret;Timeout=1;Command Timeout=1",
            ["Redis__ConnectionString"] = "127.0.0.1:1",
            ["OpenSearch__Uri"] = "http://127.0.0.1:1",
            ["Kafka__BootstrapServers"] = "127.0.0.1:1",
            ["Health__TimeoutSeconds"] = "1",
            ["Cors__AllowedOrigins__0"] = Cfg.Origin,
        })
        {
            Environment.SetEnvironmentVariable(k, v);
        }
    }

    public static HostFactory Demo(Dictionary<string, string?>? overrides = null) => new("Demo", overrides);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Auth:SigningKey", Cfg.SigningKey);
        builder.UseSetting("Demo:AccessCode", Cfg.Code);
        foreach (var (k, v) in overrides ?? [])
        {
            builder.UseSetting(k, v);
        }
    }
}

public class AccessGateTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static HttpRequestMessage Login(string account, string? code)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = JsonContent.Create(new { credentials = new { accountId = account }, device = new { platform = "web", deviceName = "demo test" } }) };
        if (code is not null)
        {
            req.Headers.Add("X-Demo-Code", code);
        }

        return req;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<JsonElement>(Web)).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public async Task Every_anonymous_auth_entry_point_requires_the_code()
    {
        using var f = HostFactory.Demo();
        var http = f.CreateClient();

        var login = await http.SendAsync(Login("demo-patient", null));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("demo_code_required", await CodeOf(login));

        var accounts = await http.GetAsync("/auth/demo-accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, accounts.StatusCode);
        Assert.Equal("demo_code_required", await CodeOf(accounts));

        var refresh = await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("demo_code_required", await CodeOf(refresh));
    }

    [Fact]
    public async Task Wrong_code_is_refused_and_correct_code_signs_in_without_echoing_the_code()
    {
        using var f = HostFactory.Demo();
        var http = f.CreateClient();

        var wrong = await http.SendAsync(Login("demo-patient", "not-the-code"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var wrongBody = await wrong.Content.ReadAsStringAsync();
        Assert.Contains("demo_code_invalid", wrongBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Cfg.Code, wrongBody, StringComparison.Ordinal);

        var ok = await http.SendAsync(Login("demo-patient", Cfg.Code));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_codes_lock_out_even_the_correct_code()
    {
        using var f = HostFactory.Demo(new() { ["Demo:MaxFailuresPerClient"] = "3" });
        var http = f.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Login("demo-patient", $"guess-{i}"))).StatusCode);
        }

        var locked = await http.SendAsync(Login("demo-patient", Cfg.Code));
        Assert.Equal((HttpStatusCode)429, locked.StatusCode);
        Assert.Equal("demo_code_throttled", await CodeOf(locked));
        Assert.True(locked.Headers.RetryAfter is not null);
        Assert.Equal((HttpStatusCode)429, (await http.GetAsync("/auth/demo-accounts")).StatusCode);
    }

    [Fact]
    public async Task Forwarded_headers_do_not_change_the_client_identity_so_spoofing_cannot_reset_the_lockout()
    {
        using var f = HostFactory.Demo(new() { ["Demo:MaxFailuresPerClient"] = "3" });
        var http = f.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var req = Login("demo-patient", $"guess-{i}");
            req.Headers.Add("X-Forwarded-For", $"10.0.0.{i}"); // a different "client" each time
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(req)).StatusCode);
        }

        var next = Login("demo-patient", Cfg.Code);
        next.Headers.Add("X-Forwarded-For", "203.0.113.77, 198.51.100.9");
        Assert.Equal((HttpStatusCode)429, (await http.SendAsync(next)).StatusCode);
    }

    [Fact]
    public async Task Health_version_and_authenticated_routes_behave_as_before()
    {
        using var f = HostFactory.Demo();
        var http = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/version")).StatusCode);

        // The code never replaces a bearer token: authenticated routes stay closed without one.
        var req = new HttpRequestMessage(HttpMethod.Get, "/sessions");
        req.Headers.Add("X-Demo-Code", Cfg.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Discovery_lists_only_allowed_accounts_and_no_privileged_ones()
    {
        using var f = HostFactory.Demo();
        var http = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/auth/demo-accounts");
        req.Headers.Add("X-Demo-Code", Cfg.Code);
        var res = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var ids = (await res.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("id").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "demo-patient", "demo-physician", "demo-pharmacist", "demo-researcher", "demo-industry" }, ids);
        Assert.DoesNotContain("demo-system-admin", ids);
    }

    [Theory]
    [InlineData("demo-system-admin")]
    [InlineData("demo-ai-manager")]
    [InlineData("demo-content-manager")]
    [InlineData("demo-content-reviewer")]
    [InlineData("demo-pharmacy-admin")]
    [InlineData("demo-multirole")]
    [InlineData("demo-patient-2")]
    [InlineData("does-not-exist")]
    public async Task Direct_login_as_a_non_allowed_account_is_refused_even_with_the_code(string account)
    {
        using var f = HostFactory.Demo();
        var res = await f.CreateClient().SendAsync(Login(account, Cfg.Code));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("invalid_credentials", await CodeOf(res));
    }

    [Fact]
    public async Task Existing_authorization_still_applies_to_an_allowed_account()
    {
        using var f = HostFactory.Demo();
        var http = f.CreateClient();
        var login = await http.SendAsync(Login("demo-patient", Cfg.Code));
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("tokens").GetProperty("accessToken").GetString();
        var admin = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
        admin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(admin)).StatusCode);
    }

    [Fact]
    public void Throttle_unlocks_after_the_lockout_and_a_success_clears_the_client()
    {
        var clock = new FakeTime();
        var t = new DemoAccessThrottle(Options.Create(new DemoOptions { MaxFailuresPerClient = 2, MaxFailuresTotal = 10, LockoutMinutes = 5 }), clock);
        t.RecordFailure("a");
        Assert.Equal(0, t.LockedSeconds("a"));
        t.RecordFailure("a");
        Assert.True(t.LockedSeconds("a") > 0);
        Assert.Equal(0, t.LockedSeconds("b")); // other clients unaffected
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.Equal(0, t.LockedSeconds("a"));

        t.RecordFailure("c");
        t.RecordSuccess("c");
        t.RecordFailure("c");
        Assert.Equal(0, t.LockedSeconds("c"));
    }

    [Fact]
    public void Total_failures_lock_everyone_out_so_rotating_addresses_do_not_help()
    {
        var t = new DemoAccessThrottle(Options.Create(new DemoOptions { MaxFailuresPerClient = 5, MaxFailuresTotal = 6, LockoutMinutes = 5 }), new FakeTime());
        for (var i = 0; i < 6; i++)
        {
            t.RecordFailure($"client-{i}");
        }

        Assert.True(t.LockedSeconds("a-brand-new-client") > 0);
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public class DemoStartupGuardTests
{
    [Fact]
    public void A_valid_demo_configuration_passes()
    {
        DemoGuard.EnsureSafe("Demo", Cfg.Of(Cfg.ValidDemo()));
    }

    [Theory]
    [InlineData("Demo:AccessCode", null)]
    [InlineData("Demo:AccessCode", "short-code")] // 10 characters
    [InlineData("Persistence:Provider", "Postgres")]
    [InlineData("Persistence:Provider", null)]
    [InlineData("Cors:AllowedOrigins:0", "http://medsmarter-demo.example.test")]
    [InlineData("Cors:AllowedOrigins:0", "https://*.example.test")]
    [InlineData("Cors:AllowedOrigins:0", "https://medsmarter-demo.example.test/")]
    [InlineData("Cors:AllowedOrigins:0", "https://medsmarter-demo.example.test/app")]
    [InlineData("Cors:AllowedOrigins:0", null)]
    [InlineData("Ai:Provider", "External")]
    [InlineData("Ai:Provider", "Local")]
    [InlineData("Ai:AllowExternalDataTransfer", "true")]
    [InlineData("Auth:DemoAllowedAccounts:0", null)]
    [InlineData("Auth:DemoAllowedAccounts:0", "demo-system-admin")]
    [InlineData("Auth:DemoAllowedAccounts:0", "demo-pharmacy-admin")]
    [InlineData("Auth:DemoAllowedAccounts:0", "no-such-account")]
    [InlineData("Ai:BaseUrl", "https://ai.example.test")]
    [InlineData("Ai:ApiKey", "some-key")]
    [InlineData("Demo:MaxFailuresPerClient", "0")]
    public void An_unsafe_demo_configuration_refuses_to_start(string key, string? value)
    {
        var d = Cfg.ValidDemo();
        d[key] = value;
        Assert.Throws<InvalidOperationException>(() => DemoGuard.EnsureSafe("Demo", Cfg.Of(d)));
    }

    [Fact]
    public void A_missing_or_empty_allow_list_refuses_to_start()
    {
        var d = Cfg.ValidDemo();
        d.Remove("Auth:DemoAllowedAccounts:0");
        d.Remove("Auth:DemoAllowedAccounts:1");
        Assert.Throws<InvalidOperationException>(() => DemoGuard.EnsureSafe("Demo", Cfg.Of(d)));
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("DEMO")]
    [InlineData("dEmO")]
    public void A_misspelled_demo_environment_name_refuses_to_start(string environment) =>
        Assert.Throws<InvalidOperationException>(() => DemoGuard.EnsureSafe(environment, Cfg.Of(Cfg.ValidDemo())));

    [Fact]
    public void The_demo_guard_does_nothing_outside_the_demo_environment()
    {
        DemoGuard.EnsureSafe("Production", Cfg.Of([]));
        DemoGuard.EnsureSafe("Development", Cfg.Of([]));
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("DEMO")]
    public void A_host_started_with_a_misspelled_demo_environment_and_mock_settings_fails_to_start(string environment)
    {
        using var f = new HostFactory(environment, new() { ["Persistence:Provider"] = "InMemory", ["Ai:Provider"] = "Mock", ["Auth:Mode"] = "DevelopmentMock", ["Patients:SeedDemoData"] = "true" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public void A_demo_host_with_an_empty_allow_list_entry_fails_to_start()
    {
        using var f = HostFactory.Demo(new() { ["Auth:DemoAllowedAccounts:0"] = "" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public void An_external_ai_provider_in_the_demo_host_fails_startup_even_when_supplied_by_configuration()
    {
        using var f = HostFactory.Demo(new() { ["Ai:Provider"] = "External", ["Ai:BaseUrl"] = "https://ai.example.test", ["Ai:AllowExternalDataTransfer"] = "true" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public void The_demo_host_without_an_access_code_fails_startup()
    {
        using var f = HostFactory.Demo(new() { ["Demo:AccessCode"] = "tooshort" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public void The_demo_host_without_a_signing_key_fails_startup()
    {
        using var f = HostFactory.Demo(new() { ["Auth:SigningKey"] = "" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public void A_privileged_account_on_the_public_allow_list_fails_startup()
    {
        using var f = HostFactory.Demo(new() { ["Auth:DemoAllowedAccounts:0"] = "demo-system-admin" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }
}

public class ProductionIsolationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Demonstration")]
    [InlineData("demo")]
    [InlineData("DEMO")]
    [InlineData("")]
    public void Only_development_testing_and_demo_allow_demo_features(string environment) => Assert.False(EnvironmentRules.AllowsDemoFeatures(environment));

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Demo")]
    public void Development_testing_and_demo_allow_demo_features(string environment) => Assert.True(EnvironmentRules.AllowsDemoFeatures(environment));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Every_existing_demo_only_feature_is_still_refused_outside_the_allowed_environments(string environment)
    {
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe(environment, new AuthOptions { Mode = AuthModes.DevelopmentMock, SigningKey = new string('k', 40) }));
        Assert.Throws<InvalidOperationException>(() => PersistenceSettings.EnsureSafe(environment, Cfg.Of(new() { ["Persistence:Provider"] = "InMemory" })));
        Assert.Throws<InvalidOperationException>(() => MedicationsGuard.EnsureSafe(environment, new MedicationsOptions { SeedDemoData = true }));
        Assert.Throws<InvalidOperationException>(() => PatientsGuard.EnsureSafe(environment, new PatientsOptions { SeedDemoData = true }));
        Assert.Throws<InvalidOperationException>(() => ManufacturerReportsGuard.EnsureSafe(environment, new ManufacturerReportsOptions { Provider = "Mock" }));
        Assert.Throws<InvalidOperationException>(() => AiGuard.EnsureSafe(environment, new AiOptions { Provider = "Mock" }));
    }

    [Fact]
    public void Production_still_refuses_the_mock_ai_opt_in()
    {
        Assert.Throws<InvalidOperationException>(() => AiGuard.EnsureSafe("Production", new AiOptions { Provider = "Mock", AllowMockInProduction = true }));
    }

    [Fact]
    public void Demo_enables_the_mock_features_but_keeps_the_signing_key_and_no_startup_migrations()
    {
        AuthGuard.EnsureSafe("Demo", new AuthOptions { Mode = AuthModes.DevelopmentMock, SigningKey = new string('k', 40) });
        Assert.Throws<InvalidOperationException>(() => AuthGuard.EnsureSafe("Demo", new AuthOptions { Mode = AuthModes.DevelopmentMock }));
        PersistenceSettings.EnsureSafe("Demo", Cfg.Of(new() { ["Persistence:Provider"] = "InMemory" }));
        Assert.Throws<InvalidOperationException>(() => PersistenceSettings.EnsureSafe("Demo", Cfg.Of(new() { ["Persistence:Provider"] = "InMemory", ["Persistence:MigrateOnStartup"] = "true" })));
        MedicationsGuard.EnsureSafe("Demo", new MedicationsOptions { SeedDemoData = true });
        Assert.Null(AiGuard.EnsureSafe("Demo", new AiOptions { Provider = "Mock" }));
    }

    [Fact]
    public void A_production_host_refuses_the_mock_sign_in()
    {
        using var f = new HostFactory("Production", new() { ["Auth:Mode"] = "DevelopmentMock" });
        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public async Task A_production_host_has_no_access_gate_and_no_demo_endpoint()
    {
        using var f = new HostFactory("Production", new() { ["Auth:Mode"] = "Disabled", ["Persistence:Provider"] = "Postgres", ["Ai:Provider"] = "Disabled" });
        var http = f.CreateClient();
        var accounts = await http.GetAsync("/auth/demo-accounts"); // not mapped (needs the unavailable DevelopmentMock mode): the deny-by-default policy answers
        Assert.Equal(HttpStatusCode.Unauthorized, accounts.StatusCode);
        Assert.DoesNotContain("demo_code", await accounts.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var refresh = await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = "x" }); // answered by the normal handler, not the demo gate
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.DoesNotContain("demo_code", await refresh.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
    }
}

public class DemoAccountPolicyTests
{
    private static readonly AccessCatalog Catalog = AccessCatalog.LoadEmbedded();

    [Fact]
    public void The_committed_demo_allow_list_is_valid_and_contains_no_privileged_account()
    {
        var path = Path.Combine(Root(), "src", "Host", "MedSmarter.Api", "appsettings.Demo.json");
        var list = new ConfigurationBuilder().AddJsonFile(path).Build().GetSection("Auth:DemoAllowedAccounts").Get<string[]>()!;
        Assert.NotEmpty(list);
        DemoAccountPolicy.Validate(list, Catalog);
        foreach (var id in list)
        {
            Assert.DoesNotContain(Catalog.File.DemoAccounts.Single(a => a.Id == id).Roles, r => DemoAccountPolicy.PrivilegedRoles.Contains(r.Role));
        }
    }

    [Theory]
    [InlineData("demo-system-admin")]
    [InlineData("demo-ai-manager")]
    [InlineData("demo-content-manager")]
    [InlineData("demo-content-reviewer")]
    [InlineData("demo-pharmacy-admin")]
    [InlineData("not-an-account")]
    [InlineData("subject-pt-2")] // cannot sign in
    public void The_allow_list_rejects_privileged_unknown_and_non_login_accounts(string id) =>
        Assert.Throws<InvalidOperationException>(() => DemoAccountPolicy.Validate([id], Catalog));

    [Fact]
    public async Task The_provider_enforces_the_allow_list_for_login_and_discovery()
    {
        var provider = new MockAuthenticationProvider(Catalog, Options.Create(new AuthOptions { Mode = AuthModes.DevelopmentMock, DemoAllowedAccounts = ["demo-patient"] }));
        Assert.Equal(["demo-patient"], provider.List().Select(a => a.Id));
        Assert.NotNull(await provider.AuthenticateAsync(new Dictionary<string, string> { ["accountId"] = "demo-patient" }));
        Assert.Null(await provider.AuthenticateAsync(new Dictionary<string, string> { ["accountId"] = "demo-system-admin" }));
    }

    [Fact]
    public void The_provider_refuses_to_exist_with_a_privileged_account_on_the_allow_list() =>
        Assert.Throws<InvalidOperationException>(() => new MockAuthenticationProvider(Catalog, Options.Create(new AuthOptions { Mode = AuthModes.DevelopmentMock, DemoAllowedAccounts = ["demo-system-admin"] })));

    [Fact]
    public void The_headers_file_has_exactly_one_api_host_placeholder_and_it_is_in_the_csp()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), "web", "public", "_headers"));
        var hits = lines.Where(l => l.Contains("REPLACE-WITH-YOUR-API-HOST", StringComparison.Ordinal)).ToList();
        Assert.Single(hits);
        Assert.Contains("Content-Security-Policy:", hits[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Committed_demo_settings_hold_no_secret()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "src", "Host", "MedSmarter.Api", "appsettings.Demo.json"));
        foreach (var forbidden in new[] { "AccessCode", "SigningKey", "ApiKey", "Password", "ConnectionString", "BaseUrl" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MedSmarter.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
