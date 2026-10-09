using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

public sealed class PatientsApiFactory : WebApplicationFactory<Program>
{
    private readonly Action? _drop;

    public PatientsApiFactory()
    {
        string? pg = null;
        if (PgTemplate.Enabled)
        {
            (pg, _drop) = PgTemplate.NewDatabase();
        }

        foreach (var (k, v) in new Dictionary<string, string>
        {
            ["ConnectionStrings__Postgres"] = pg ?? "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=not-a-real-secret;Timeout=1;Command Timeout=1",
            ["Redis__ConnectionString"] = "127.0.0.1:1",
            ["OpenSearch__Uri"] = "http://127.0.0.1:1",
            ["Kafka__BootstrapServers"] = "127.0.0.1:1",
            ["Health__TimeoutSeconds"] = "1",
            ["Cors__AllowedOrigins__0"] = "http://localhost:3000",
            ["RateLimits__WritePerMinute"] = "100000",
            ["RateLimits__SearchPerMinute"] = "100000",
        })
        {
            Environment.SetEnvironmentVariable(k, v);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Persistence:Provider", PgTemplate.Enabled ? "Postgres" : "InMemory");
        builder.UseSetting("Persistence:MigrateOnStartup", "false");
        builder.UseSetting("Patients:IdentifierHashKey", "api-test-hash-key-not-a-secret");
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

public class PatientsApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Session(HttpClient Client, Guid UserId);

    private async Task<Session> As(string account)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = account }, device = new { platform = "web", deviceName = "patients test" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("tokens").GetProperty("accessToken").GetString());
        return new Session(client, auth.GetProperty("user").GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Web);

    private static async Task<Guid> ReferenceId(HttpClient c, string q) => (await c.GetFromJsonAsync<JsonElement>($"/medications/search?q={q}", Web)).GetProperty("items")[0].GetProperty("id").GetGuid();

    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null) => new(m, url) { Content = body is null ? null : JsonContent.Create(body, options: Web) };

    // ---------- authentication ----------

    [Theory]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/profile")]
    [InlineData("PUT", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/profile")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/conditions")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/allergies")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/medications")]
    [InlineData("POST", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/intake")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/products")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/manufacturer-reports")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/ai-context")]
    [InlineData("POST", "/patients/me")]
    [InlineData("GET", "/care-relationships")]
    [InlineData("POST", "/care-relationships")]
    [InlineData("GET", "/consents/history")]
    [InlineData("GET", "/guidance/messages")]
    [InlineData("GET", "/guidance/samples")]
    [InlineData("GET", "/manufacturer-reports/queue")]
    [InlineData("POST", "/manufacturer-reports/queue/process")]
    [InlineData("GET", "/manufacturer-reports/pending-review")]
    public async Task Every_new_route_returns_401_without_a_token(string method, string path)
    {
        var res = await factory.CreateClient().SendAsync(Req(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ---------- IDOR: one patient against another ----------

    [Fact]
    public async Task A_patient_cannot_read_or_change_another_patients_data_by_any_route()
    {
        var a = await As("demo-patient");
        var b = await As("demo-patient-2");
        var reads = new[] { "profile", "freshness", "conditions", "allergies", "medications", "symptoms", "doses?date=2026-10-09", "adherence", "products", "manufacturer-reports", "ai-context" };
        foreach (var route in reads)
        {
            var res = await a.Client.GetAsync($"/patients/{b.UserId}/{route}");
            Assert.True(res.StatusCode == HttpStatusCode.Forbidden, $"GET {route} -> {res.StatusCode}");
        }

        var writes = new (HttpMethod Method, string Route, object Body)[]
        {
            (HttpMethod.Put, "profile", new { yearOfBirth = 1990 }),
            (HttpMethod.Post, "conditions", new { name = "x", status = "Active" }),
            (HttpMethod.Post, "allergies", new { kind = "Other", substance = "x", severity = "Mild" }),
            (HttpMethod.Post, "medications", new { unregisteredName = "x", frequency = "AsNeeded", startDate = "2026-10-01", source = "Unknown" }),
            (HttpMethod.Post, "intake", new { patientMedicationId = Guid.NewGuid(), status = "Taken" }),
            (HttpMethod.Post, "symptoms", new { text = "x", severity = "Mild", onsetAt = "2026-10-08T10:00:00Z" }),
            (HttpMethod.Post, "products", new { batchNumber = "x" }),
            (HttpMethod.Post, "manufacturer-reports", new { productRecordId = Guid.NewGuid() }),
        };
        foreach (var (method, route, body) in writes)
        {
            var res = await a.Client.SendAsync(Req(method, $"/patients/{b.UserId}/{route}", body));
            Assert.True(res.StatusCode == HttpStatusCode.Forbidden, $"{method} {route} -> {res.StatusCode}");
        }
    }

    [Fact]
    public async Task A_record_id_from_one_patient_does_not_work_on_another_patients_route()
    {
        var a = await As("demo-patient");
        var b = await As("demo-patient-2");
        Assert.Equal(HttpStatusCode.OK, (await b.Client.PostAsync("/patients/me", null)).StatusCode);
        var meds = await a.Client.GetFromJsonAsync<JsonElement>($"/patients/{a.UserId}/medications", Web);
        var medicationId = meds[0].GetProperty("id").GetGuid();
        // B's own route (allowed) with A's record id: 404, exactly like an id that does not exist
        var foreign = await b.Client.GetAsync($"/patients/{b.UserId}/medications/{medicationId}");
        var missing = await b.Client.GetAsync($"/patients/{b.UserId}/medications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.DeleteAsync($"/patients/{b.UserId}/medications/{medicationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync($"/patients/{b.UserId}/medications/{medicationId}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"/patients/{a.UserId}/medications/{medicationId}")).StatusCode); // still there
    }

    [Fact]
    public async Task Refusals_are_uniform_and_reveal_nothing()
    {
        var a = await As("demo-patient");
        var physicianB = await As("demo-physician-b");
        var sara = a.UserId;
        var noRelationship = await physicianB.Client.GetAsync($"/patients/{sara}/medications");
        var noSuchPatient = await physicianB.Client.GetAsync($"/patients/{Guid.NewGuid()}/medications");
        var noPermission = await (await As("demo-industry")).Client.GetAsync($"/patients/{sara}/medications");
        static string Shape(string body) { var d = JsonDocument.Parse(body).RootElement; return $"{d.GetProperty("status")}|{d.GetProperty("title")}"; }
        var shapes = new[] { noRelationship, noSuchPatient, noPermission }.Select(r => Assert.IsType<string>(Shape(r.Content.ReadAsStringAsync().Result))).Distinct().ToList();
        Assert.Single(shapes);
        foreach (var r in new[] { noRelationship, noSuchPatient, noPermission })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync();
            foreach (var leak in new[] { "consent", "relationship", "permission", "role", "Demopril", "Sara" })
            {
                Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------- roles ----------

    [Theory]
    [InlineData("demo-industry")]
    [InlineData("demo-researcher")]
    [InlineData("demo-system-admin")]
    [InlineData("demo-ai-manager")]
    [InlineData("demo-content-manager")]
    [InlineData("demo-pharmacy-admin")]
    public async Task Roles_without_a_patient_purpose_get_nothing(string account)
    {
        var s = await As(account);
        var sara = (await As("demo-patient")).UserId;
        foreach (var route in new[] { "profile", "conditions", "allergies", "medications", "symptoms", "products", "manufacturer-reports", "ai-context" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync($"/patients/{sara}/{route}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/guidance/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/care-relationships")).StatusCode);
    }

    [Fact]
    public async Task A_physician_reads_what_the_relationship_and_consent_cover_but_cannot_write_the_patients_medicines()
    {
        var sara = (await As("demo-patient")).UserId;
        var doctor = await As("demo-physician");
        foreach (var route in new[] { "profile", "conditions", "allergies", "medications", "symptoms", "products", "adherence" })
        {
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{sara}/{route}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.SendAsync(Req(HttpMethod.Post, $"/patients/{sara}/medications", new { unregisteredName = "x", frequency = "AsNeeded", startDate = "2026-10-01", source = "Physician" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.SendAsync(Req(HttpMethod.Put, $"/patients/{sara}/profile", new { yearOfBirth = 1980 }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/patients/{sara}/ai-context")).StatusCode); // the AI view is for the patient only
    }

    [Fact]
    public async Task Expired_consent_or_no_relationship_closes_every_door_for_the_physician()
    {
        var doctor = await As("demo-physician");
        var patient2 = (await As("demo-patient-2")).UserId; // consent to the physician expired 30 days ago
        var doctorB = await As("demo-physician-b");
        var sara = (await As("demo-patient")).UserId;
        foreach (var route in new[] { "profile", "medications", "allergies", "products" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/patients/{patient2}/{route}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await doctorB.Client.GetAsync($"/patients/{sara}/{route}")).StatusCode);
        }
    }

    [Fact]
    public async Task A_pharmacist_sees_allergies_and_products_but_not_conditions()
    {
        var sara = (await As("demo-patient")).UserId;
        var pharmacist = await As("demo-pharmacist");
        Assert.Equal(HttpStatusCode.OK, (await pharmacist.Client.GetAsync($"/patients/{sara}/allergies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pharmacist.Client.GetAsync($"/patients/{sara}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.GetAsync($"/patients/{sara}/conditions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.GetAsync($"/patients/{sara}/symptoms")).StatusCode);
    }

    [Fact]
    public async Task Another_persons_access_to_the_patients_data_shows_up_in_the_patients_access_log()
    {
        var sara = await As("demo-patient");
        var doctor = await As("demo-physician");
        await doctor.Client.GetAsync($"/patients/{sara.UserId}/allergies");
        var log = await sara.Client.GetStringAsync("/audit/me");
        Assert.Contains("PATIENT_DATA_ACCESSED", log, StringComparison.Ordinal);
        Assert.Contains("DEMO Physician", log, StringComparison.Ordinal);
    }

    // ---------- the patient's own flow ----------

    [Fact]
    public async Task The_demo_patient_has_a_labelled_profile_and_a_dated_freshness_list()
    {
        var sara = await As("demo-patient");
        var profile = await sara.Client.GetFromJsonAsync<JsonElement>($"/patients/{sara.UserId}/profile", Web);
        Assert.Equal("DEMO DATA - NOT FOR CLINICAL USE", profile.GetProperty("notice").GetString());
        Assert.True(profile.GetProperty("isDemo").GetBoolean());
        var freshness = await sara.Client.GetFromJsonAsync<JsonElement>($"/patients/{sara.UserId}/freshness", Web);
        Assert.Equal(8, freshness.GetArrayLength());
        Assert.Contains(freshness.EnumerateArray(), f => f.GetProperty("category").GetString() == "Medications" && !f.GetProperty("neverRecorded").GetBoolean());
    }

    [Fact]
    public async Task A_new_patient_records_a_medicine_plans_doses_and_logs_one()
    {
        var p = await As("demo-patient-2");
        Assert.Equal(HttpStatusCode.OK, (await p.Client.PostAsync("/patients/me", null)).StatusCode);
        var referenceId = await ReferenceId(p.Client, "nocturin");
        var created = await p.Client.PostAsJsonAsync($"/patients/{p.UserId}/medications", new { medicationId = referenceId, doseAmount = 5, doseUnit = "mg", frequency = "TimesPerDay", frequencyValue = 1, route = "oral", startDate = "2026-09-01", source = "Physician" }, Web);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var med = await Json(created);
        Assert.True(med.GetProperty("isRegistered").GetBoolean());
        Assert.Equal("Nocturin", med.GetProperty("displayName").GetString());
        var mid = med.GetProperty("id").GetGuid();

        var entry = await p.Client.PostAsJsonAsync($"/patients/{p.UserId}/medications/{mid}/schedule", new { timeOfDay = "21:30:00" }, Web);
        Assert.Equal(HttpStatusCode.Created, entry.StatusCode);
        var eid = (await Json(entry)).GetProperty("id").GetGuid();
        var day = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var slots = await p.Client.GetFromJsonAsync<JsonElement>($"/patients/{p.UserId}/doses?date={day}", Web);
        Assert.Equal(1, slots.GetArrayLength());
        var scheduledFor = slots[0].GetProperty("scheduledFor").GetDateTimeOffset();
        var log = await p.Client.PostAsJsonAsync($"/patients/{p.UserId}/intake", new { patientMedicationId = mid, scheduleEntryId = eid, scheduledFor, status = "Skipped" }, Web);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode);
        Assert.Equal("Skipped", (await p.Client.GetFromJsonAsync<JsonElement>($"/patients/{p.UserId}/doses?date={day}", Web))[0].GetProperty("status").GetString());

        var stop = await p.Client.PostAsJsonAsync($"/patients/{p.UserId}/medications/{mid}/stop", new { reason = "course finished", expectedVersion = 1 }, Web);
        Assert.Equal("Stopped", (await Json(stop)).GetProperty("status").GetString());
        Assert.Equal(0, (await p.Client.GetFromJsonAsync<JsonElement>($"/patients/{p.UserId}/medications", Web)).GetArrayLength());
    }

    [Fact]
    public async Task Bad_input_gets_codes_not_internals()
    {
        var p = await As("demo-patient-2");
        await p.Client.PostAsync("/patients/me", null);
        var bad = await p.Client.PutAsJsonAsync($"/patients/{p.UserId}/profile", new { yearOfBirth = 1700, weightKg = -4 }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("year_of_birth.range", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        var broken = await p.Client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/patients/{p.UserId}/profile") { Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json") });
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await p.Client.GetAsync($"/patients/not-a-guid/profile")).StatusCode);
        var identifying = await p.Client.PostAsJsonAsync($"/patients/{p.UserId}/symptoms", new { text = "call 09121234567", severity = "Mild", onsetAt = DateTimeOffset.UtcNow.AddHours(-1) }, Web);
        Assert.Contains("looks_identifying", await identifying.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---------- care relationships and consent ----------

    [Fact]
    public async Task The_whole_care_flow_works_through_the_api_and_ending_it_closes_access_at_once()
    {
        var patient = await As("demo-patient-2");
        await patient.Client.PostAsync("/patients/me", null);
        var doctor = await As("demo-physician-b");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/patients/{patient.UserId}/medications")).StatusCode);

        var asked = await patient.Client.PostAsJsonAsync("/care-relationships", new { counterpartUserId = doctor.UserId, kind = "Treating" }, Web);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var rid = (await Json(asked)).GetProperty("id").GetGuid();
        var inbox = await doctor.Client.GetFromJsonAsync<JsonElement>("/care-relationships", Web);
        Assert.Contains(inbox.EnumerateArray(), r => r.GetProperty("relationship").GetProperty("id").GetGuid() == rid && r.GetProperty("patientName").GetString()!.Contains("Patient", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, (await (await As("demo-physician")).Client.PostAsync($"/care-relationships/{rid}/accept", null)).StatusCode); // not their request
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/care-relationships/{rid}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/patients/{patient.UserId}/medications")).StatusCode); // consent still missing

        var consent = await patient.Client.PostAsJsonAsync("/consents", new { granteeUserId = doctor.UserId, purpose = "Treatment", scope = new[] { "medications" }, expiresAt = DateTimeOffset.UtcNow.AddDays(30), version = "v1" }, Web);
        Assert.Equal(HttpStatusCode.Created, consent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{patient.UserId}/medications")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await patient.Client.PostAsJsonAsync($"/care-relationships/{rid}/end", new { reason = "moved" }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/patients/{patient.UserId}/medications")).StatusCode);
        var history = await patient.Client.GetFromJsonAsync<JsonElement>("/consents/history", Web);
        Assert.True(history.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task A_purpose_consent_has_no_grantee_and_a_person_consent_needs_one()
    {
        var p = await As("demo-patient-2");
        var ok = await p.Client.PostAsJsonAsync("/consents", new { purpose = "ManufacturerReport", scope = new[] { "products" }, expiresAt = DateTimeOffset.UtcNow.AddDays(10), version = "v1" }, Web);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var withPerson = await p.Client.PostAsJsonAsync("/consents", new { granteeUserId = (await As("demo-physician")).UserId, purpose = "ManufacturerReport", scope = new[] { "products" }, expiresAt = DateTimeOffset.UtcNow.AddDays(10), version = "v1" }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, withPerson.StatusCode);
        var list = await p.Client.GetFromJsonAsync<JsonElement>("/consents", Web);
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("consent").GetProperty("purpose").GetString() == "ManufacturerReport" && c.GetProperty("granteeName").ValueKind == JsonValueKind.Null);
    }

    // ---------- batch + manufacturer report, end to end ----------

    [Fact]
    public async Task A_report_goes_from_the_patient_through_review_to_the_mock_queue_and_the_queue_shows_no_clinical_content()
    {
        var sara = await As("demo-patient");
        var referenceId = await ReferenceId(sara.Client, "glycanor");
        var product = await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/products", new { medicationId = referenceId, batchNumber = "API-LOT-77", manufactureDate = "2026-05-01", expiryDate = "2028-05-01", receivedOn = "2026-09-20", pharmacyNote = "Corner pharmacy (placeholder)" }, Web);
        Assert.Equal(HttpStatusCode.Created, product.StatusCode);
        var pid = (await Json(product)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotImplemented, (await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/products/scan", new { code = "123" }, Web)).StatusCode);

        await sara.Client.PostAsJsonAsync("/consents", new { purpose = "ManufacturerReport", scope = new[] { "products" }, expiresAt = DateTimeOffset.UtcNow.AddDays(10), version = "v1" }, Web);
        var draft = await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/manufacturer-reports", new { productRecordId = pid, issueType = "AdverseEvent", severity = "Moderate", occurredOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2).ToString("yyyy-MM-dd"), description = "api-test-secret-description" }, Web);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var report = await Json(draft);
        var rid = report.GetProperty("id").GetGuid();
        var submitted = await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/manufacturer-reports/{rid}/submit", new { expectedVersion = report.GetProperty("version").GetInt32() }, Web);
        var s = await Json(submitted);
        Assert.Equal("PendingConsentOrReview", s.GetProperty("status").GetString());
        Assert.False(s.GetProperty("payloadPreview").TryGetProperty("patientId", out _));

        Assert.Equal(HttpStatusCode.Forbidden, (await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/manufacturer-reports/{rid}/review", new { decision = "Approve", expectedVersion = 2 }, Web)).StatusCode); // patients do not review
        var pharmacist = await As("demo-pharmacist");
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.GetAsync("/manufacturer-reports/queue")).StatusCode); // the queue is for operators
        var inbox = await (await As("demo-physician")).Client.GetFromJsonAsync<JsonElement>("/manufacturer-reports/pending-review", Web);
        Assert.Contains(inbox.EnumerateArray(), r => r.GetProperty("id").GetGuid() == rid);

        var doctor = await As("demo-physician");
        var approved = await doctor.Client.PostAsJsonAsync($"/patients/{sara.UserId}/manufacturer-reports/{rid}/review", new { decision = "Approve", expectedVersion = s.GetProperty("version").GetInt32() }, Web);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("ReadyToSend", (await Json(approved)).GetProperty("status").GetString());

        var admin = await As("demo-system-admin");
        var queue = await admin.Client.GetStringAsync("/manufacturer-reports/queue");
        Assert.DoesNotContain("api-test-secret-description", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("API-LOT-77", queue, StringComparison.Ordinal);
        var run = await Json(await admin.Client.PostAsJsonAsync("/manufacturer-reports/queue/process", new { max = 10 }, Web));
        Assert.True(run.GetProperty("sent").GetInt32() >= 1);
        var final = await sara.Client.GetFromJsonAsync<JsonElement>($"/patients/{sara.UserId}/manufacturer-reports/{rid}", Web);
        Assert.Equal("Acknowledged", final.GetProperty("status").GetString());
        Assert.True(final.GetProperty("isMockDelivery").GetBoolean());
    }

    // ---------- guidance + AI context ----------

    [Fact]
    public async Task Patient_messages_hide_the_professional_view_and_samples_show_it_only_to_professionals()
    {
        var sara = await As("demo-patient");
        var list = await sara.Client.GetFromJsonAsync<JsonElement>("/guidance/messages", Web);
        Assert.True(list.GetArrayLength() >= 1);
        Assert.All(list.EnumerateArray(), m => Assert.Equal(JsonValueKind.Null, m.GetProperty("professional").ValueKind));
        var first = list[0];
        var seen = await sara.Client.PostAsJsonAsync($"/guidance/messages/{first.GetProperty("id").GetGuid()}/status", new { status = "Seen" }, Web);
        Assert.Equal("Seen", (await Json(seen)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await sara.Client.PostAsJsonAsync($"/guidance/messages/{first.GetProperty("id").GetGuid()}/status", new { status = "Reviewed" }, Web)).StatusCode);

        var patientSamples = await sara.Client.GetFromJsonAsync<JsonElement>("/guidance/samples?locale=fa", Web);
        Assert.True(patientSamples.GetProperty("demo").GetBoolean());
        Assert.All(patientSamples.GetProperty("samples").EnumerateArray(), x => Assert.Equal(JsonValueKind.Null, x.GetProperty("professional").ValueKind));
        var proSamples = await (await As("demo-physician")).Client.GetFromJsonAsync<JsonElement>("/guidance/samples", Web);
        Assert.All(proSamples.GetProperty("samples").EnumerateArray(), x => Assert.NotEqual(JsonValueKind.Null, x.GetProperty("professional").ValueKind));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-industry")).Client.GetAsync("/guidance/samples")).StatusCode);
    }

    [Fact]
    public async Task Another_patients_message_cannot_be_changed()
    {
        var sara = await As("demo-patient");
        var id = (await sara.Client.GetFromJsonAsync<JsonElement>("/guidance/messages", Web))[0].GetProperty("id").GetGuid();
        var other = await As("demo-patient-2");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/guidance/messages/{id}/status", new { status = "Resolved" }, Web)).StatusCode);
    }

    [Fact]
    public async Task The_patient_can_see_exactly_what_an_ai_feature_would_receive()
    {
        var p = await As("demo-patient-2");
        await p.Client.PostAsync("/patients/me", null);
        var before = await p.Client.GetFromJsonAsync<JsonElement>($"/patients/{p.UserId}/ai-context", Web);
        Assert.Equal(5, before.GetProperty("excluded").GetArrayLength());
        await p.Client.PostAsJsonAsync("/consents", new { purpose = "AiProcessing", scope = new[] { "profile" }, expiresAt = DateTimeOffset.UtcNow.AddDays(5), version = "v1" }, Web);
        var after = await p.Client.GetFromJsonAsync<JsonElement>($"/patients/{p.UserId}/ai-context", Web);
        Assert.Equal(4, after.GetProperty("excluded").GetArrayLength());
        Assert.DoesNotContain(p.UserId.ToString(), after.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_deactivated_patient_record_closes_the_door_for_professionals_at_once()
    {
        var sara = await As("demo-patient");
        var doctor = await As("demo-physician");
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{sara.UserId}/allergies")).StatusCode);
        var svc = factory.Services.GetRequiredService<IPatientService>();
        await svc.SetStatusAsync(sara.UserId, sara.UserId, PatientStatus.Inactive, "test", null);
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/patients/{sara.UserId}/allergies")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await sara.Client.GetAsync($"/patients/{sara.UserId}/allergies")).StatusCode); // the patient still sees their own data
            Assert.Equal(HttpStatusCode.Forbidden, (await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/conditions", new { name = "x", status = "Active" }, Web)).StatusCode); // no new data on an inactive record
        }
        finally
        {
            await svc.SetStatusAsync(sara.UserId, sara.UserId, PatientStatus.Active, "test", null);
        }

        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{sara.UserId}/allergies")).StatusCode);
    }
}
