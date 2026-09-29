using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MedSmarter.Security.Tests;

public sealed class SecureApiFactory : WebApplicationFactory<Program>
{
    public SecureApiFactory()
    {
        foreach (var (k, v) in new Dictionary<string, string>
        {
            ["ConnectionStrings__Postgres"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=not-a-real-secret;Timeout=1;Command Timeout=1",
            ["Redis__ConnectionString"] = "127.0.0.1:1",
            ["OpenSearch__Uri"] = "http://127.0.0.1:1",
            ["Kafka__BootstrapServers"] = "127.0.0.1:1",
            ["Health__TimeoutSeconds"] = "1",
            ["Cors__AllowedOrigins__0"] = "http://localhost:3000",
        })
        {
            Environment.SetEnvironmentVariable(k, v);
        }
    }
}

public class ApiTests(SecureApiFactory factory) : IClassFixture<SecureApiFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<(HttpClient Client, JsonElement Auth)> LoginAsync(string account)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = account }, device = new { platform = "web", deviceName = "api test" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = (await res.Content.ReadFromJsonAsync<JsonElement>(Web));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("tokens").GetProperty("accessToken").GetString());
        return (client, auth);
    }

    private static Guid Id(JsonElement auth) => auth.GetProperty("user").GetProperty("id").GetGuid();

    // ---------- 401: unauthenticated ----------

    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("GET", "/sessions")]
    [InlineData("GET", "/consents")]
    [InlineData("GET", "/audit")]
    [InlineData("GET", "/audit/me")]
    [InlineData("GET", "/admin/users")]
    [InlineData("GET", "/patients")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/profile")]
    [InlineData("GET", "/analytics/summary")]
    [InlineData("GET", "/organizations/383c0c5b-386f-5d57-a295-3b25374d9a2d/inventory")]
    [InlineData("POST", "/auth/logout")]
    [InlineData("POST", "/consents")]
    [InlineData("DELETE", "/sessions/fa1f8b92-53cc-5ad2-a0a8-c39713c37946")]
    [InlineData("GET", "/does-not-exist-anywhere")]
    public async Task Protected_endpoints_return_401_without_a_token(string method, string path)
    {
        var res = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("Bearer", res.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData("Bearer garbage")]
    [InlineData("Bearer ")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ4In0.")]
    public async Task Invalid_credentials_return_401(string header)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        req.Headers.TryAddWithoutValidation("Authorization", header);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Public_endpoints_stay_public()
    {
        var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/version")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/health/ready")).StatusCode); // reachable (not 401)
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_returns_tokens_and_a_user_without_secrets()
    {
        var (_, auth) = await LoginAsync("demo-patient");
        Assert.Equal("Patient", auth.GetProperty("user").GetProperty("roles")[0].GetString());
        Assert.False(string.IsNullOrEmpty(auth.GetProperty("tokens").GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Bad_account_gives_401_and_disabled_gives_403_with_a_code()
    {
        var c = factory.CreateClient();
        var bad = await c.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = "nobody" } });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        var disabled = await c.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = "demo-disabled" } });
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
        Assert.Contains("account_disabled", await disabled.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_accounts_list_has_no_credentials_and_is_labelled()
    {
        var text = await factory.CreateClient().GetStringAsync("/auth/demo-accounts");
        Assert.Contains("DEMO ENVIRONMENT", text, StringComparison.Ordinal);
        foreach (var w in new[] { "\"password\"", "\"secret\"", "\"token\"", "\"credentials\"" })
        {
            Assert.DoesNotContain(w, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Me_returns_the_caller()
    {
        var (c, auth) = await LoginAsync("demo-pharmacist");
        var me = await c.GetFromJsonAsync<JsonElement>("/auth/me", Web);
        Assert.Equal(Id(auth), me.GetProperty("id").GetGuid());
        Assert.Equal("Pharmacist", me.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Refresh_works_and_reuse_is_rejected()
    {
        var (_, auth) = await LoginAsync("demo-patient");
        var refresh = auth.GetProperty("tokens").GetProperty("refreshToken").GetString();
        var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh })).StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_the_access_token_immediately()
    {
        var (c, _) = await LoginAsync("demo-patient");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_session_from_another_session_kills_it()
    {
        var (a, _) = await LoginAsync("demo-patient");
        var (b, bAuth) = await LoginAsync("demo-patient");
        var sessions = await a.GetFromJsonAsync<JsonElement>("/sessions", Web);
        Assert.True(sessions.GetArrayLength() >= 2);
        var other = bAuth.GetProperty("sessionId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/sessions/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cannot_revoke_another_users_session()
    {
        var (a, _) = await LoginAsync("demo-patient");
        var (_, victim) = await LoginAsync("demo-patient-2");
        Assert.Equal(HttpStatusCode.NotFound, (await a.DeleteAsync($"/sessions/{victim.GetProperty("sessionId").GetGuid()}")).StatusCode);
    }

    // ---------- 403: authenticated but not allowed ----------

    [Theory]
    [InlineData("demo-patient", "/admin/users")]
    [InlineData("demo-patient", "/audit")]
    [InlineData("demo-patient", "/analytics/summary")]
    [InlineData("demo-physician", "/admin/users")]
    [InlineData("demo-pharmacist", "/audit")]
    [InlineData("demo-industry", "/patients")]
    [InlineData("demo-researcher", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/medications")]
    [InlineData("demo-system-admin", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/medications")]
    [InlineData("demo-patient", "/organizations/383c0c5b-386f-5d57-a295-3b25374d9a2d/inventory")]
    public async Task Missing_permission_returns_403(string account, string path)
    {
        var (c, _) = await LoginAsync(account);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Forbidden_responses_are_uniform_and_leak_nothing()
    {
        var (patient, _) = await LoginAsync("demo-patient");
        var (physicianB, _) = await LoginAsync("demo-physician-b");
        var sara = "fa1f8b92-53cc-5ad2-a0a8-c39713c37946";
        var rbac = await patient.GetAsync("/admin/users"); // role lacks permission
        var relationship = await physicianB.GetAsync($"/patients/{sara}/profile"); // no care relationship
        var unknown = await physicianB.GetAsync($"/patients/{Guid.NewGuid()}/profile"); // no such patient
        foreach (var r in new[] { relationship, unknown })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        static string Shape(string body) { var d = JsonDocument.Parse(body).RootElement; return $"{d.GetProperty("status")}|{d.GetProperty("title")}"; }
        var a = Shape(await rbac.Content.ReadAsStringAsync());
        Assert.Equal(a, Shape(await relationship.Content.ReadAsStringAsync()));
        Assert.Equal(a, Shape(await unknown.Content.ReadAsStringAsync()));
        foreach (var body in new[] { await rbac.Content.ReadAsStringAsync(), await relationship.Content.ReadAsStringAsync() })
        {
            foreach (var leak in new[] { "role_lacks_permission", "no_care_relationship", "consent", "permission", "Physician", "relationship", "Sara" })
            {
                Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------- resource-level ----------

    [Fact]
    public async Task Patient_reads_own_data_but_not_others()
    {
        var (c, auth) = await LoginAsync("demo-patient");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/patients/{Id(auth)}/medications")).StatusCode);
        var text = await c.GetStringAsync($"/patients/{Id(auth)}/profile");
        Assert.Contains("DEMO DATA", text, StringComparison.Ordinal);
        var (_, other) = await LoginAsync("demo-patient-2");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/patients/{Id(other)}/medications")).StatusCode);
    }

    [Fact]
    public async Task Physician_reads_consented_patient_and_the_access_shows_up_in_the_patients_access_log()
    {
        var (patient, pAuth) = await LoginAsync("demo-patient");
        var (physician, _) = await LoginAsync("demo-physician");
        Assert.Equal(HttpStatusCode.OK, (await physician.GetAsync($"/patients/{Id(pAuth)}/medications")).StatusCode);
        var log = await patient.GetStringAsync("/audit/me");
        Assert.Contains("PATIENT_DATA_ACCESSED", log, StringComparison.Ordinal);
        Assert.Contains("DEMO Physician", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Physician_is_denied_for_expired_consent_and_for_unrelated_patient()
    {
        var (physician, _) = await LoginAsync("demo-physician");
        var (_, p2) = await LoginAsync("demo-patient-2");
        Assert.Equal(HttpStatusCode.Forbidden, (await physician.GetAsync($"/patients/{Id(p2)}/medications")).StatusCode);
        var (physicianB, _) = await LoginAsync("demo-physician-b");
        var (_, sara) = await LoginAsync("demo-patient");
        Assert.Equal(HttpStatusCode.Forbidden, (await physicianB.GetAsync($"/patients/{Id(sara)}/medications")).StatusCode);
    }

    [Fact]
    public async Task Pharmacist_sees_only_the_consented_scope()
    {
        var (pharmacist, _) = await LoginAsync("demo-pharmacist");
        var (_, sara) = await LoginAsync("demo-patient");
        Assert.Equal(HttpStatusCode.OK, (await pharmacist.GetAsync($"/patients/{Id(sara)}/prescriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.GetAsync($"/patients/{Id(sara)}/adherence")).StatusCode);
    }

    [Fact]
    public async Task Patient_revoking_consent_blocks_the_physician_on_the_next_request()
    {
        var (patient, pAuth) = await LoginAsync("demo-patient-2");
        var (physician, _) = await LoginAsync("demo-physician");
        var grant = await patient.PostAsJsonAsync("/consents", new
        {
            granteeUserId = (await physician.GetFromJsonAsync<JsonElement>("/auth/me", Web)).GetProperty("id").GetGuid(),
            purpose = "Treatment",
            scope = new[] { "medications" },
            expiresAt = DateTimeOffset.UtcNow.AddDays(10),
            version = "consent-text-v1",
        });
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await physician.GetAsync($"/patients/{Id(pAuth)}/medications")).StatusCode);
        var id = (await grant.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await patient.DeleteAsync($"/consents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await physician.GetAsync($"/patients/{Id(pAuth)}/medications")).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_manage_their_consents()
    {
        var (sara, _) = await LoginAsync("demo-patient");
        var (other, _) = await LoginAsync("demo-patient-2");
        var list = await sara.GetFromJsonAsync<JsonElement>("/consents", Web);
        var id = list[0].GetProperty("consent").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/consents/{id}")).StatusCode);
        var (physician, _) = await LoginAsync("demo-physician");
        Assert.Equal(HttpStatusCode.Forbidden, (await physician.DeleteAsync($"/consents/{id}")).StatusCode);
    }

    [Fact]
    public async Task Physician_can_create_prescription_only_for_consented_patient_and_patient_cannot_at_all()
    {
        var (physician, _) = await LoginAsync("demo-physician");
        var (patient, sara) = await LoginAsync("demo-patient");
        var (_, p2) = await LoginAsync("demo-patient-2");
        Assert.Equal(HttpStatusCode.OK, (await physician.PostAsync($"/patients/{Id(sara)}/prescriptions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await physician.PostAsync($"/patients/{Id(p2)}/prescriptions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.PostAsync($"/patients/{Id(sara)}/prescriptions", null)).StatusCode);
    }

    [Fact]
    public async Task Pharmacy_admin_is_limited_to_their_own_organization()
    {
        var (admin, _) = await LoginAsync("demo-pharmacy-admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/organizations/383c0c5b-386f-5d57-a295-3b25374d9a2d/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/organizations/{Guid.NewGuid()}/inventory")).StatusCode);
    }

    [Fact]
    public async Task Analytics_is_aggregate_only()
    {
        var (c, _) = await LoginAsync("demo-researcher");
        var text = await c.GetStringAsync("/analytics/summary");
        Assert.Contains("No patient-level data", text, StringComparison.Ordinal);
    }

    // ---------- admin + audit ----------

    [Fact]
    public async Task System_admin_manages_roles_reads_audit_and_the_chain_verifies()
    {
        var (admin, _) = await LoginAsync("demo-system-admin");
        var users = await admin.GetFromJsonAsync<JsonElement>("/admin/users", Web);
        var target = users.EnumerateArray().First(u => u.GetProperty("displayName").GetString()!.Contains("Content", StringComparison.Ordinal)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/admin/users/{target}/roles", new { role = "Researcher" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/admin/users/{target}/roles", new { role = "Researcher" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/users/{target}/roles/Researcher")).StatusCode);
        Assert.Contains("ROLE_ASSIGNED", await admin.GetStringAsync("/audit"), StringComparison.Ordinal);
        Assert.Contains("\"intact\":true", await admin.GetStringAsync("/audit/verify"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_and_error_bodies_never_contain_tokens()
    {
        var (c, auth) = await LoginAsync("demo-patient");
        var access = auth.GetProperty("tokens").GetProperty("accessToken").GetString()!;
        var refresh = auth.GetProperty("tokens").GetProperty("refreshToken").GetString()!;
        var (admin, _) = await LoginAsync("demo-system-admin");
        _ = await c.GetAsync("/admin/users");
        var log = await admin.GetStringAsync("/audit?take=500");
        Assert.DoesNotContain(access, log, StringComparison.Ordinal);
        Assert.DoesNotContain(refresh, log, StringComparison.Ordinal);
        Assert.Contains("ACCESS_DENIED", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_a_role_takes_effect_on_the_next_request_with_the_same_token()
    {
        var (admin, _) = await LoginAsync("demo-system-admin");
        var (target, tAuth) = await LoginAsync("demo-ai-manager");
        Assert.Equal(HttpStatusCode.OK, (await target.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/users/{Id(tAuth)}/roles/AIManager")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Security_headers_and_cors_are_set()
    {
        var res = await factory.CreateClient().GetAsync("/version");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        var pre = new HttpRequestMessage(HttpMethod.Options, "/auth/login");
        pre.Headers.Add("Origin", "http://localhost:3000");
        pre.Headers.Add("Access-Control-Request-Method", "POST");
        pre.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var pf = await factory.CreateClient().SendAsync(pre);
        Assert.Equal("http://localhost:3000", pf.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var evil = new HttpRequestMessage(HttpMethod.Options, "/auth/login");
        evil.Headers.Add("Origin", "https://evil.example");
        evil.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.False((await factory.CreateClient().SendAsync(evil)).Headers.Contains("Access-Control-Allow-Origin"));
    }
}
