using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MedSmarter.Knowledge.Tests;

public sealed class KnowledgeApiFactory : WebApplicationFactory<Program>
{
    private readonly Action? _drop;
    private readonly string? _pg;

    public KnowledgeApiFactory()
    {
        string? pg = null;
        if (PgTemplate.Enabled)
        {
            (pg, _drop) = PgTemplate.NewDatabase(); // the whole API suite also runs against a scratch PostgreSQL when MEDSMARTER_PG_TEST is set
        }

        _pg = pg;

        foreach (var (k, v) in new Dictionary<string, string>
        {
            // The scratch database is passed per host (UseSetting), never through a process-wide environment variable: two factories starting in one process would otherwise share one database.
            ["ConnectionStrings__Postgres"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=not-a-real-secret;Timeout=1;Command Timeout=1",
            ["Redis__ConnectionString"] = "127.0.0.1:1",
            ["OpenSearch__Uri"] = "http://127.0.0.1:1",
            ["Kafka__BootstrapServers"] = "127.0.0.1:1",
            ["Health__TimeoutSeconds"] = "1",
            ["Cors__AllowedOrigins__0"] = "http://localhost:3000",
        })
        {
            if (k != "ConnectionStrings__Postgres" || pg is null)
            {
                Environment.SetEnvironmentVariable(k, v);
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Persistence:Provider", PgTemplate.Enabled ? "Postgres" : "InMemory"); // tests never need a database unless MEDSMARTER_PG_TEST is set
        if (PgTemplate.Enabled)
        {
            builder.UseSetting("ConnectionStrings:Postgres", _pg!);
            builder.UseSetting("Persistence:MigrateOnStartup", "true"); // the template holds only the Knowledge schemas; the host also needs the patient-layer ones
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _drop?.Invoke();
        }
    }
}

public class KnowledgeApiTests(KnowledgeApiFactory factory) : IClassFixture<KnowledgeApiFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<HttpClient> Login(WebApplicationFactory<Program> f, string account)
    {
        var client = f.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = account }, device = new { platform = "web", deviceName = "knowledge test" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("tokens").GetProperty("accessToken").GetString());
        return client;
    }

    private Task<HttpClient> As(string account) => Login(factory, account);

    private static async Task<Guid> FindId(HttpClient c, string q)
    {
        var page = await c.GetFromJsonAsync<JsonElement>($"/medications/search?q={Uri.EscapeDataString(q)}", Web);
        return page.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    // ---------- authentication / authorization ----------

    [Theory]
    [InlineData("GET", "/medications/search?q=demo")]
    [InlineData("GET", "/medications/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/medications/00000000-0000-0000-0000-000000000001/knowledge-document")]
    [InlineData("POST", "/admin/medications")]
    [InlineData("POST", "/admin/ingredients")]
    [InlineData("GET", "/admin/knowledge-sources")]
    [InlineData("POST", "/ai/medication-assistant")]
    [InlineData("GET", "/ai/provider")]
    public async Task Every_new_endpoint_returns_401_without_a_token(string method, string path)
    {
        var res = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("demo-patient")]
    [InlineData("demo-physician")]
    [InlineData("demo-pharmacist")]
    [InlineData("demo-pharmacy-admin")]
    [InlineData("demo-industry")]
    [InlineData("demo-researcher")]
    [InlineData("demo-content-manager")]
    [InlineData("demo-ai-manager")]
    public async Task Roles_with_medication_read_can_search(string account)
    {
        var c = await As(account);
        var res = await c.GetAsync("/medications/search?q=nocturin");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task System_admin_has_no_medication_read_permission()
    {
        var c = await As("demo-system-admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/medications/search?q=nocturin")).StatusCode);
    }

    [Theory]
    [InlineData("demo-patient")]
    [InlineData("demo-physician")]
    [InlineData("demo-pharmacist")]
    [InlineData("demo-ai-manager")]
    [InlineData("demo-industry")]
    public async Task Ordinary_users_cannot_edit_or_publish_the_reference(string account)
    {
        var c = await As(account);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/admin/ingredients", new { name = new { en = "x" }, synonyms = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/admin/knowledge-sources", new { })).StatusCode);
        var id = await FindId(c, "nocturin");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/medications/{id}/validation") { Content = JsonContent.Create(new { status = "Validated", revisionId = Guid.NewGuid() }) };
        req.Headers.TryAddWithoutValidation("If-Match", "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Reading_sources_needs_knowledge_read_which_patients_do_not_have()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-patient")).GetAsync("/admin/knowledge-sources")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await As("demo-ai-manager")).GetAsync("/admin/knowledge-sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-ai-manager")).PostAsJsonAsync("/admin/knowledge-sources", new { })).StatusCode); // read is not write
    }

    // ---------- reading ----------

    [Fact]
    public async Task Search_returns_ranked_paged_results_with_labels()
    {
        var c = await As("demo-patient");
        var page = await c.GetFromJsonAsync<JsonElement>("/medications/search?q=demopril&limit=2", Web);
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("total").GetInt32() >= 3);
        var first = page.GetProperty("items")[0];
        Assert.Equal("Demopril", first.GetProperty("name").GetProperty("en").GetString());
        Assert.True(first.GetProperty("isDemo").GetBoolean());
        Assert.Equal("Demo", first.GetProperty("validation").GetString());
        Assert.Equal("Active", first.GetProperty("lifecycle").GetString());
    }

    [Fact]
    public async Task Arabic_letter_variants_find_persian_names()
    {
        var c = await As("demo-pharmacist");
        var page = await c.GetFromJsonAsync<JsonElement>($"/medications/search?q={Uri.EscapeDataString("نوكتورين")}", Web);
        Assert.Equal("Nocturin", page.GetProperty("items")[0].GetProperty("name").GetProperty("en").GetString());
    }

    [Fact]
    public async Task Detail_shows_sections_missing_information_sources_and_the_demo_notice()
    {
        var c = await As("demo-physician");
        var id = await FindId(c, "nocturin");
        var d = await c.GetFromJsonAsync<JsonElement>($"/medications/{id}", Web);
        Assert.Contains("NOT FOR CLINICAL USE", d.GetProperty("notice").GetString(), StringComparison.Ordinal);
        Assert.Equal("5 mg", d.GetProperty("strengthSummary").GetString());
        Assert.Contains(d.GetProperty("missingKinds").EnumerateArray(), k => k.GetString() == "Contraindication");
        Assert.NotEmpty(d.GetProperty("sources").EnumerateArray());
        Assert.NotEmpty(d.GetProperty("interactions").EnumerateArray());
        Assert.Empty(d.GetProperty("identifiers").EnumerateArray());
    }

    [Fact]
    public async Task Knowledge_document_is_available_for_retrieval_and_source_carrying()
    {
        var c = await As("demo-physician");
        var id = await FindId(c, "glycanor");
        var doc = await c.GetFromJsonAsync<JsonElement>($"/medications/{id}/knowledge-document", Web);
        Assert.Equal(id, doc.GetProperty("medicationId").GetGuid());
        Assert.NotEmpty(doc.GetProperty("sources").EnumerateArray());
        Assert.True(doc.GetProperty("isDemo").GetBoolean());
    }

    [Fact]
    public async Task Unknown_ids_give_a_plain_404_and_inactive_ones_are_hidden_from_readers()
    {
        var patient = await As("demo-patient");
        var missing = await patient.GetAsync($"/medications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var body = await missing.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync("/medications/not-a-guid")).StatusCode);

        var editor = await As("demo-content-manager");
        var page = await editor.GetFromJsonAsync<JsonElement>("/medications/search?q=oldmed&includeInactive=true", Web);
        var oldId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync($"/medications/{oldId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync($"/medications/{oldId}")).StatusCode);
        var patientView = await patient.GetFromJsonAsync<JsonElement>("/medications/search?q=oldmed&includeInactive=true", Web); // flag ignored for non-editors
        Assert.Equal(0, patientView.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("q=a", "q.too_short")]
    [InlineData("q=nocturin&limit=0", "limit.range")]
    [InlineData("q=nocturin&limit=1000", "limit.range")]
    [InlineData("q=nocturin&offset=-1", "offset.range")]
    [InlineData("q=nocturin&offset=100000", "offset.range")]
    [InlineData("q=%21%21", "q.invalid")]
    public async Task Invalid_search_input_is_a_400_with_a_code_only(string query, string code)
    {
        var c = await As("demo-patient");
        var res = await c.GetAsync($"/medications/search?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains(code, body, StringComparison.Ordinal);
        Assert.DoesNotContain("at MedSmarter", body, StringComparison.Ordinal); // no stack traces
    }

    [Fact]
    public async Task Non_numeric_paging_is_a_400_not_a_500()
    {
        var c = await As("demo-patient");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/medications/search?q=demo&limit=abc")).StatusCode);
    }

    [Fact]
    public async Task Search_is_rate_limited_per_caller()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimits:SearchPerMinute", "3"));
        var c = await Login(limited, "demo-patient");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/medications/search?q=demo")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/medications/search?q=demo")).StatusCode);
        var other = await Login(limited, "demo-physician"); // another caller has their own budget
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/medications/search?q=demo")).StatusCode);
    }

    // ---------- editing, versioning, validation ----------

    private static object Draft(Guid ingredient, Guid revision, string name = "Apitestomed") => new
    {
        name = new { en = name, fa = (string?)null },
        brandId = (Guid?)null,
        manufacturerId = (Guid?)null,
        dosageFormCode = "tablet",
        routeCodes = new[] { "oral" },
        classCodes = Array.Empty<string>(),
        ingredients = new[] { new { ingredientId = ingredient, strengthValue = 5m, strengthUnit = "mg", perUnit = (string?)null } },
        synonyms = Array.Empty<string>(),
        identifiers = Array.Empty<object>(),
        statements = new[] { new { kind = "Warning", text = new { en = "Test warning (fictional)", fa = (string?)null }, severity = "info", frequency = (string?)null, population = (string?)null, revisionId = revision } },
        isDemo = false,
    };

    private static HttpRequestMessage Req(HttpMethod m, string url, object? body, int? version)
    {
        var r = new HttpRequestMessage(m, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (version is not null)
        {
            r.Headers.TryAddWithoutValidation("If-Match", version.ToString());
        }

        return r;
    }

    [Fact]
    public async Task Content_manager_runs_the_full_edit_publish_flow_and_every_step_is_enforced()
    {
        var editor = await As("demo-content-manager");
        var reviewer = await As("demo-content-reviewer"); // a second person: the editor may not validate their own work
        var patient = await As("demo-patient");

        var ing = (await (await editor.PostAsJsonAsync("/admin/ingredients", new { name = new { en = "apitestium" }, synonyms = Array.Empty<string>() })).Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("id").GetGuid();
        var src = (await (await editor.PostAsJsonAsync("/admin/knowledge-sources", new { name = "API test formulary (fictional)", publisher = "Test", type = "Publication", version = "1", licenseName = "Test licence", redistributionAllowed = true })).Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("id").GetGuid();
        var rev = (await (await editor.PostAsJsonAsync($"/admin/knowledge-sources/{src}/revisions", new { sourceId = src, label = "r1" })).Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("id").GetGuid();

        var created = await editor.PostAsJsonAsync("/admin/medications", Draft(ing, rev));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var med = await created.Content.ReadFromJsonAsync<JsonElement>(Web);
        var id = med.GetProperty("id").GetGuid();
        Assert.Equal("Draft", med.GetProperty("lifecycle").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync($"/medications/{id}")).StatusCode); // drafts are not public

        Assert.Equal((HttpStatusCode)428, (await editor.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{id}/lifecycle", new { status = "Active" }, null))).StatusCode); // If-Match required
        var activated = await editor.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{id}/lifecycle", new { status = "Active" }, 1));
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await patient.GetAsync($"/medications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{id}/lifecycle", new { status = "Inactive" }, 1))).StatusCode); // stale version

        var selfValidation = await editor.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{id}/validation", new { status = "Validated", revisionId = rev }, 2));
        Assert.Equal(HttpStatusCode.Forbidden, selfValidation.StatusCode); // separation of duties: uniform 403, no hint why
        Assert.DoesNotContain("separation", await selfValidation.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var notValidated = await reviewer.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{id}/validation", new { status = "Validated", revisionId = rev }, 2));
        Assert.Equal(HttpStatusCode.BadRequest, notValidated.StatusCode); // revision is still a draft
        Assert.Contains("revision.not_validated", await notValidated.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await editor.PostAsJsonAsync($"/admin/knowledge-revisions/{rev}/status", new { status = "Validated" })).StatusCode);
        var validated = await reviewer.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{id}/validation", new { status = "Validated", revisionId = rev }, 2));
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
        var shown = await patient.GetFromJsonAsync<JsonElement>($"/medications/{id}", Web);
        Assert.Equal("Validated", shown.GetProperty("validation").GetString());

        var edited = await editor.SendAsync(Req(HttpMethod.Put, $"/admin/medications/{id}", new { draft = Draft(ing, rev, "Apitestomed 2"), reason = "rename" }, 3));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Unverified", (await edited.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("validation").GetString()); // edit withdrew validation
        var versions = await editor.GetFromJsonAsync<JsonElement>($"/admin/medications/{id}/versions", Web);
        Assert.Equal(4, versions.GetArrayLength());
    }

    [Fact]
    public async Task Invalid_edit_requests_get_codes_not_internals()
    {
        var editor = await As("demo-content-manager");
        var res = await editor.PostAsJsonAsync("/admin/medications", new
        {
            name = new { en = (string?)null, fa = (string?)null }, dosageFormCode = "nope", routeCodes = Array.Empty<string>(), classCodes = Array.Empty<string>(),
            ingredients = Array.Empty<object>(), synonyms = Array.Empty<string>(), identifiers = Array.Empty<object>(), statements = Array.Empty<object>(), isDemo = false,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("name.required", body, StringComparison.Ordinal);
        Assert.Contains("ingredients.count", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at MedSmarter", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync("/admin/medications", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.SendAsync(Req(HttpMethod.Post, $"/admin/medications/{Guid.NewGuid()}/lifecycle", new { status = "Active" }, 1))).StatusCode);
    }

    // ---------- AI ----------

    [Fact]
    public async Task Assistant_answers_from_sources_with_the_mock_provider_and_no_key()
    {
        var c = await As("demo-patient");
        var res = await c.PostAsJsonAsync("/ai/medication-assistant", new { question = "Tell me about Nocturin", locale = "en" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var a = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.True(a.GetProperty("isMock").GetBoolean());
        Assert.True(a.GetProperty("answered").GetBoolean());
        Assert.Contains("MOCK AI", a.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(a.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task Assistant_says_it_has_no_information_instead_of_guessing()
    {
        var c = await As("demo-patient");
        var a = await (await c.PostAsJsonAsync("/ai/medication-assistant", new { question = "zzzzzzqq unknownthing", locale = "en" })).Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.False(a.GetProperty("answered").GetBoolean());
        Assert.Equal("none", a.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Assistant_requires_ai_use_and_validates_input()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-pharmacy-admin")).PostAsJsonAsync("/ai/medication-assistant", new { question = "nocturin", locale = "en" })).StatusCode);
        var c = await As("demo-patient");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/ai/medication-assistant", new { question = " ", locale = "en" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/ai/medication-assistant", new { question = "nocturin", locale = "klingon" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/ai/medication-assistant", new { question = new string('a', 600), locale = "en" })).StatusCode);
    }

    [Fact]
    public async Task A_disabled_provider_is_a_controlled_503_with_a_safe_message()
    {
        using var disabled = factory.WithWebHostBuilder(b => b.UseSetting("Ai:Provider", "Disabled"));
        var c = await Login(disabled, "demo-patient");
        var res = await c.PostAsJsonAsync("/ai/medication-assistant", new { question = "Tell me about Nocturin", locale = "en" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var a = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.Equal("Disabled", a.GetProperty("error").GetString());
        Assert.DoesNotContain("Exception", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_status_is_for_ai_managers_only_and_never_shows_a_key()
    {
        using var keyed = factory.WithWebHostBuilder(b => { b.UseSetting("Ai:Provider", "External"); b.UseSetting("Ai:ApiKey", "api-test-secret-value-1234"); });
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Login(keyed, "demo-patient")).GetAsync("/ai/provider")).StatusCode);
        var text = await (await Login(keyed, "demo-ai-manager")).GetStringAsync("/ai/provider");
        Assert.Contains("\"apiKeyPresent\":true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("api-test-secret-value-1234", text, StringComparison.Ordinal);
        Assert.Contains("\"configured\":false", text, StringComparison.Ordinal);
    }

    // ---------- environment safety / CORS ----------

    [Fact]
    public void Host_refuses_demo_seed_or_mock_ai_in_production()
    {
        using var seed = factory.WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.UseSetting("Persistence:MigrateOnStartup", "false"); b.UseSetting("Persistence:Provider", "Postgres");  b.UseSetting("Auth:SigningKey", new string('k', 40)); b.UseSetting("Medications:SeedDemoData", "true"); b.UseSetting("Auth:Mode", "Disabled"); b.UseSetting("Ai:Provider", "Disabled"); });
        Assert.ThrowsAny<Exception>(() => seed.CreateClient());
        using var mock = factory.WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.UseSetting("Persistence:MigrateOnStartup", "false"); b.UseSetting("Persistence:Provider", "Postgres");  b.UseSetting("Auth:SigningKey", new string('k', 40)); b.UseSetting("Medications:SeedDemoData", "false"); b.UseSetting("Auth:Mode", "Disabled"); b.UseSetting("Ai:Provider", "Mock"); });
        Assert.ThrowsAny<Exception>(() => mock.CreateClient());
    }

    [Fact]
    public async Task A_production_style_host_has_no_demo_medications_and_a_disabled_assistant()
    {
        using var prod = factory.WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.UseSetting("Persistence:MigrateOnStartup", "false"); b.UseSetting("Persistence:Provider", "Postgres");  b.UseSetting("Auth:SigningKey", new string('k', 40)); b.UseSetting("Medications:SeedDemoData", "false"); b.UseSetting("Auth:Mode", "Disabled"); b.UseSetting("Ai:Provider", "Disabled"); b.UseSetting("Integrations:Insurance:EnableMock", "false"); });
        var c = prod.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/medications/search?q=nocturin")).StatusCode);
        var login = await c.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = "demo-patient" } });
        Assert.NotEqual(HttpStatusCode.OK, login.StatusCode); // no mock login, so no token, so no data (401, or fail-closed 500 because the durable audit store is unreachable here)
        Assert.DoesNotContain("accessToken", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preflight_allows_put_with_if_match_for_the_configured_origin_only()
    {
        var ok = new HttpRequestMessage(HttpMethod.Options, "/admin/medications/00000000-0000-0000-0000-000000000001");
        ok.Headers.Add("Origin", "http://localhost:3000");
        ok.Headers.Add("Access-Control-Request-Method", "PUT");
        ok.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,if-match");
        var res = await factory.CreateClient().SendAsync(ok);
        Assert.Equal("http://localhost:3000", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var evil = new HttpRequestMessage(HttpMethod.Options, "/medications/search");
        evil.Headers.Add("Origin", "https://evil.example");
        evil.Headers.Add("Access-Control-Request-Method", "GET");
        Assert.False((await factory.CreateClient().SendAsync(evil)).Headers.Contains("Access-Control-Allow-Origin"));
    }
}
