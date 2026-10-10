using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 7 over HTTP: authentication, permissions, ownership/relationship/consent, uniform refusals, rule review. Each test class has its own application instance (and database).</summary>
public class ClinicalApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Sara = "fa1f8b92-53cc-5ad2-a0a8-c39713c37946";

    internal sealed record Session(HttpClient Client, Guid UserId);

    internal static async Task<Session> Login(PatientsApiFactory f, string account)
    {
        var client = f.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = account }, device = new { platform = "web", deviceName = "clinical test" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("tokens").GetProperty("accessToken").GetString());
        return new Session(client, auth.GetProperty("user").GetProperty("id").GetGuid());
    }

    private Task<Session> As(string account) => Login(factory, account);

    internal static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Web);

    [Theory]
    [InlineData("POST", $"/patients/{Sara}/safety/assessments")]
    [InlineData("GET", $"/patients/{Sara}/safety/assessments")]
    [InlineData("GET", $"/patients/{Sara}/safety/assessments/latest")]
    [InlineData("GET", $"/patients/{Sara}/safety/assessments/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/safety/coverage")]
    [InlineData("GET", "/clinical-rules")]
    [InlineData("GET", "/clinical-rules/X-1/versions/1")]
    [InlineData("POST", "/clinical-rules")]
    [InlineData("POST", "/clinical-rules/X-1/versions/1/submit")]
    [InlineData("POST", "/clinical-rules/X-1/versions/1/review")]
    [InlineData("POST", "/clinical-rules/X-1/versions/1/retire")]
    public async Task Every_new_route_returns_401_without_a_token(string method, string path)
    {
        var res = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task A_patient_assesses_themselves_and_gets_an_honest_demonstration_only_result()
    {
        var sara = await As("demo-patient");
        var res = await sara.Client.PostAsync($"/patients/{sara.UserId}/safety/assessments?locale=en", null);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var a = await Json(res);
        var result = a.GetProperty("result");
        Assert.Equal("NoApprovedCoverage", result.GetProperty("status").GetString());
        Assert.False(result.GetProperty("safetyClaimAllowed").GetBoolean());
        Assert.True(result.GetProperty("containsDemonstration").GetBoolean());
        Assert.StartsWith("DEMO", a.GetProperty("notice").GetString(), StringComparison.Ordinal);
        Assert.True(a.GetProperty("requestedByPatient").GetBoolean());
        Assert.Equal(sara.UserId, a.GetProperty("subjectId").GetGuid());
        Assert.All(result.GetProperty("findings").EnumerateArray(), f => { Assert.False(f.GetProperty("actionable").GetBoolean()); Assert.True(f.GetProperty("isDemo").GetBoolean()); });
        Assert.Equal(5, result.GetProperty("coverage").GetProperty("demonstrationRules").GetInt32());
        Assert.Contains("coverage.no_approved_rules", result.GetProperty("limitations").EnumerateArray().Select(x => x.GetString()));

        var id = a.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await sara.Client.GetAsync($"/patients/{sara.UserId}/safety/assessments/{id}")).StatusCode);
        var latest = await Json(await sara.Client.GetAsync($"/patients/{sara.UserId}/safety/assessments/latest"));
        Assert.Equal(id, latest.GetProperty("id").GetGuid());
        var list = await Json(await sara.Client.GetAsync($"/patients/{sara.UserId}/safety/assessments"));
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task One_patient_cannot_run_or_read_another_patients_assessments_and_the_refusal_is_uniform()
    {
        var sara = await As("demo-patient");
        var other = await As("demo-patient-2");
        var made = await Json(await sara.Client.PostAsync($"/patients/{sara.UserId}/safety/assessments", null));
        var id = made.GetProperty("id").GetGuid();

        var refusals = new List<(HttpStatusCode, string)>();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, $"/patients/{sara.UserId}/safety/assessments"),
            (HttpMethod.Get, $"/patients/{sara.UserId}/safety/assessments"),
            (HttpMethod.Get, $"/patients/{sara.UserId}/safety/assessments/latest"),
            (HttpMethod.Get, $"/patients/{sara.UserId}/safety/assessments/{id}"),
            (HttpMethod.Get, $"/patients/{Guid.NewGuid()}/safety/assessments"), // a patient that does not exist looks exactly the same
        })
        {
            var r = await other.Client.SendAsync(new HttpRequestMessage(method, path));
            refusals.Add((r.StatusCode, await r.Content.ReadAsStringAsync()));
        }

        Assert.All(refusals, x => Assert.Equal(HttpStatusCode.Forbidden, x.Item1));
        Assert.Single(refusals.Select(x => x.Item2).Select(s => JsonDocument.Parse(s).RootElement.GetProperty("title").GetString()).Distinct());
        Assert.All(refusals, x => { Assert.DoesNotContain("Demopril", x.Item2, StringComparison.Ordinal); Assert.DoesNotContain("consent", x.Item2, StringComparison.OrdinalIgnoreCase); });

        // A patient's own route never reaches another patient's assessment either: the id is simply not found.
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/patients/{other.UserId}/safety/assessments/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_physician_needs_a_relationship_and_consent_and_gets_only_what_the_consent_covers()
    {
        var sara = await As("demo-patient");
        var doctor = await As("demo-physician");
        var res = await doctor.Client.PostAsync($"/patients/{sara.UserId}/safety/assessments", null);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var a = await Json(res);
        Assert.False(a.GetProperty("requestedByPatient").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{sara.UserId}/safety/assessments/latest")).StatusCode);

        var doctorB = await As("demo-physician-b"); // no relationship
        Assert.Equal(HttpStatusCode.Forbidden, (await doctorB.Client.PostAsync($"/patients/{sara.UserId}/safety/assessments", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctorB.Client.GetAsync($"/patients/{sara.UserId}/safety/assessments")).StatusCode);

        var p2 = await As("demo-patient-2"); // the consent to the physician expired
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsync($"/patients/{p2.UserId}/safety/assessments", null)).StatusCode);

        // A fresh, narrower consent opens medications only: allergies and symptoms are withheld and reported as not authorized.
        var consent = await p2.Client.PostAsJsonAsync("/consents", new { granteeUserId = doctor.UserId, purpose = "Treatment", scope = new[] { "medications" }, expiresAt = DateTimeOffset.UtcNow.AddDays(10), version = "v1" }, Web);
        Assert.Equal(HttpStatusCode.Created, consent.StatusCode);
        var narrow = await Json(await doctor.Client.PostAsync($"/patients/{p2.UserId}/safety/assessments", null));
        var inputs = narrow.GetProperty("result").GetProperty("inputs").EnumerateArray().ToDictionary(i => i.GetProperty("category").GetString()!, i => i.GetProperty("availability").GetString());
        Assert.Equal("NotAuthorized", inputs["allergies"]);
        Assert.Equal("NotAuthorized", inputs["symptoms"]);
        Assert.Equal("NotAuthorized", inputs["profile"]);
        Assert.NotEqual("NotAuthorized", inputs["medications"]);
    }

    [Fact]
    public async Task Nothing_in_the_request_can_name_a_different_patient()
    {
        var sara = await As("demo-patient");
        var other = await As("demo-patient-2");
        var res = await other.Client.PostAsJsonAsync($"/patients/{other.UserId}/safety/assessments", new { subjectId = sara.UserId, patientId = sara.UserId, readableCategories = new[] { "medications", "allergies", "symptoms", "profile" } }, Web);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(other.UserId, (await Json(res)).GetProperty("subjectId").GetGuid()); // the route patient, authorized, is the only patient
    }

    [Fact]
    public async Task Coverage_is_open_to_those_who_may_assess_and_to_nobody_else()
    {
        var sara = await As("demo-patient");
        var cov = await Json(await sara.Client.GetAsync("/safety/coverage"));
        Assert.Equal(0, cov.GetProperty("activeRules").GetInt32());
        Assert.Equal(5, cov.GetProperty("demonstrationRules").GetInt32());
        Assert.True(cov.GetProperty("demonstrationAllowed").GetBoolean());
        Assert.Contains("dose_and_route", cov.GetProperty("unsupportedDomains").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-industry")).Client.GetAsync("/safety/coverage")).StatusCode);
    }

    [Fact]
    public async Task Only_the_right_roles_can_read_write_review_and_retire_rules()
    {
        var patient = await As("demo-patient");
        var doctor = await As("demo-physician");
        var author = await As("demo-content-manager");
        var admin = await As("demo-system-admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync("/clinical-rules")).StatusCode);
        var listed = await Json(await doctor.Client.GetAsync("/clinical-rules"));
        Assert.Equal(5, listed.GetArrayLength());
        Assert.All(listed.EnumerateArray(), r => { Assert.True(r.GetProperty("isDemo").GetBoolean()); Assert.Equal("Draft", r.GetProperty("status").GetString()); Assert.Equal("DemonstrationOnly", r.GetProperty("activation").GetString()); });

        var def = DemoRules.All(DateTimeOffset.UtcNow).Single(r => r.Definition.RuleId == "DEMO-DQ-001").Definition with
        {
            RuleId = "API-DQ-1", Evidence = [new EvidenceRef("test-fixture", "TEST FIXTURE (not a real source)", "1", null, EvidenceValidation.Validated, "fixture", null)],
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync("/clinical-rules", def, Web)).StatusCode); // reviewers do not author
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync("/clinical-rules", def, Web)).StatusCode);
        var reserved = await author.Client.PostAsJsonAsync("/clinical-rules", def with { RuleId = "DEMO-API-1" }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
        Assert.Contains("rule.id_reserved", await reserved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.Client.PostAsJsonAsync("/clinical-rules", def with { Purpose = "You should stop taking it." }, Web)).StatusCode);

        var created = await author.Client.PostAsJsonAsync("/clinical-rules", def, Web);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Draft", (await Json(created)).GetProperty("record").GetProperty("lifecycle").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await author.Client.PostAsJsonAsync("/clinical-rules", def, Web)).StatusCode);

        var review = new { decision = "Approve", note = "checked in a test" };
        Assert.Equal(HttpStatusCode.Conflict, (await doctor.Client.PostAsJsonAsync("/clinical-rules/API-DQ-1/versions/1/review", review, Web)).StatusCode); // not submitted
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsync("/clinical-rules/API-DQ-1/versions/1/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.Client.PostAsync("/clinical-rules/API-DQ-1/versions/1/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.Client.PostAsJsonAsync("/clinical-rules/API-DQ-1/versions/1/review", review, Web)).StatusCode); // authors do not review
        Assert.Equal(HttpStatusCode.Conflict, (await doctor.Client.PostAsJsonAsync("/clinical-rules/DEMO-DQ-001/versions/1/review", review, Web)).StatusCode); // demonstration rules are never reviewed

        var approved = await doctor.Client.PostAsJsonAsync("/clinical-rules/API-DQ-1/versions/1/review", review, Web);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Active", (await Json(approved)).GetProperty("activation").GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsync("/clinical-rules/API-DQ-1/versions/1/retire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync("/clinical-rules/API-DQ-1/versions/1/retire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.Client.GetAsync("/clinical-rules/NOPE-1/versions/1")).StatusCode);
    }

    [Fact]
    public async Task A_malformed_rule_body_is_a_400_not_a_crash()
    {
        var author = await As("demo-content-manager");
        var res = await author.Client.PostAsync("/clinical-rules", new StringContent("{ not json", new MediaTypeHeaderValue("application/json")));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var minimal = await author.Client.PostAsJsonAsync("/clinical-rules", new { ruleId = "x" }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, minimal.StatusCode);
    }
}
