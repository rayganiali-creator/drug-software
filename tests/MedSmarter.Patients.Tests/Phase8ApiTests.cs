using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 8 over HTTP: adherence summary access, monitoring enrolment, rule draft editing and review support. Own application instance.</summary>
public class Phase8ApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private static readonly JsonSerializerOptions Web = ClinicalApiTests.Web;
    private const string Sara = "fa1f8b92-53cc-5ad2-a0a8-c39713c37946";

    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);

    [Theory]
    [InlineData("GET", $"/patients/{Sara}/adherence/summary")]
    [InlineData("GET", $"/patients/{Sara}/safety/monitoring")]
    [InlineData("POST", $"/patients/{Sara}/safety/monitoring")]
    [InlineData("DELETE", $"/patients/{Sara}/safety/monitoring")]
    [InlineData("GET", "/clinical-rules/options")]
    [InlineData("GET", "/clinical-rules/X-1/versions/1/history")]
    [InlineData("PUT", "/clinical-rules/X-1/versions/1")]
    public async Task Every_phase8_route_returns_401_without_a_token(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path))).StatusCode);
    }

    [Fact]
    public async Task The_adherence_summary_follows_ownership_relationship_and_consent()
    {
        var sara = await As("demo-patient");
        var res = await sara.Client.GetAsync($"/patients/{sara.UserId}/adherence/summary?days=14");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await ClinicalApiTests.Json(res);
        Assert.Equal("PatientReported", s.GetProperty("provenance").GetString());
        Assert.Contains("not counted as missed", s.GetProperty("notice").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(s.GetProperty("medications").EnumerateArray());
        Assert.All(s.GetProperty("medications").EnumerateArray(), m => Assert.True(m.TryGetProperty("instructionGaps", out _)));

        Assert.Equal(HttpStatusCode.OK, (await (await As("demo-physician")).Client.GetAsync($"/patients/{sara.UserId}/adherence/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-physician-b")).Client.GetAsync($"/patients/{sara.UserId}/adherence/summary")).StatusCode); // no relationship
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-patient-2")).Client.GetAsync($"/patients/{sara.UserId}/adherence/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-industry")).Client.GetAsync($"/patients/{sara.UserId}/adherence/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await sara.Client.GetAsync($"/patients/{sara.UserId}/adherence/summary?days=400")).StatusCode);
    }

    [Fact]
    public async Task Only_the_patient_enrols_and_a_professional_may_read_the_status_but_never_start_or_stop_it()
    {
        var p = await As("demo-patient-2");
        var doctor = await As("demo-physician");

        // no Monitoring consent yet
        var refused = await p.Client.PostAsync($"/patients/{p.UserId}/safety/monitoring?locale=fa", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("consent.monitoring_required", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Created, (await p.Client.PostAsJsonAsync("/consents", new { purpose = "Monitoring", scope = new[] { "medications", "allergies" }, expiresAt = DateTimeOffset.UtcNow.AddDays(10), version = "v1" }, Web)).StatusCode);
        var ok = await p.Client.PostAsync($"/patients/{p.UserId}/safety/monitoring?locale=fa", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var s = await ClinicalApiTests.Json(ok);
        Assert.True(s.GetProperty("enrolled").GetBoolean());
        Assert.False(s.GetProperty("workerEnabled").GetBoolean()); // the worker is off by default: enrolment alone runs nothing
        Assert.Contains("not continuous or real-time monitoring", s.GetProperty("notice").GetString(), StringComparison.Ordinal);

        // a professional with relationship + consent can read the status (no clinical content) but not change it
        await p.Client.PostAsJsonAsync("/consents", new { granteeUserId = doctor.UserId, purpose = "Treatment", scope = new[] { "medications" }, expiresAt = DateTimeOffset.UtcNow.AddDays(10), version = "v1" }, Web);
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{p.UserId}/safety/monitoring")).StatusCode);
        var tryStart = await doctor.Client.PostAsync($"/patients/{p.UserId}/safety/monitoring", null);
        Assert.True(tryStart.StatusCode is HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.DeleteAsync($"/patients/{p.UserId}/safety/monitoring")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-patient")).Client.GetAsync($"/patients/{p.UserId}/safety/monitoring")).StatusCode);

        var withdrawn = await ClinicalApiTests.Json(await p.Client.DeleteAsync($"/patients/{p.UserId}/safety/monitoring"));
        Assert.False(withdrawn.GetProperty("enrolled").GetBoolean());
    }

    [Fact]
    public async Task Draft_editing_options_and_history_are_permission_checked_and_the_author_is_named()
    {
        var author = await As("demo-content-manager");
        var doctor = await As("demo-physician");
        var patient = await As("demo-patient");
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync("/clinical-rules/options")).StatusCode);
        var options = await ClinicalApiTests.Json(await doctor.Client.GetAsync("/clinical-rules/options"));
        Assert.Contains("safety.interaction", options.GetProperty("templateKeys").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(5, options.GetProperty("domains").GetArrayLength());

        var def = DemoRules.All(DateTimeOffset.UtcNow).Single(r => r.Definition.RuleId == "DEMO-DQ-001").Definition with
        {
            RuleId = "P8-API-1", Evidence = [new EvidenceRef("test-fixture", "TEST FIXTURE (not a real source)", "1", null, EvidenceValidation.Validated, "fixture", null)],
        };
        Assert.Equal(HttpStatusCode.Created, (await author.Client.PostAsJsonAsync("/clinical-rules", def, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.Client.PutAsJsonAsync("/clinical-rules/P8-API-1/versions/1", def with { Title = "Edited by the author" }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PutAsJsonAsync("/clinical-rules/P8-API-1/versions/1", def with { Title = "Edited by a reviewer" }, Web)).StatusCode); // reviewers do not author
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-content-reviewer")).Client.PutAsJsonAsync("/clinical-rules/P8-API-1/versions/1", def with { Title = "x" }, Web)).StatusCode);

        var asAuthor = await ClinicalApiTests.Json(await author.Client.GetAsync("/clinical-rules/P8-API-1/versions/1/history"));
        Assert.True(asAuthor.GetProperty("authorIsYou").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(asAuthor.GetProperty("authorName").GetString()));
        Assert.DoesNotContain(author.UserId.ToString(), asAuthor.GetProperty("authorName").GetString(), StringComparison.Ordinal);
        Assert.Contains("draft_updated", asAuthor.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("action").GetString()));
        Assert.Contains("review.separation_of_duties", asAuthor.GetProperty("approvalBlockers").EnumerateArray().Select(x => x.GetString()));

        await author.Client.PostAsync("/clinical-rules/P8-API-1/versions/1/submit", null);
        var asDoctor = await ClinicalApiTests.Json(await doctor.Client.GetAsync("/clinical-rules/P8-API-1/versions/1/history"));
        Assert.False(asDoctor.GetProperty("authorIsYou").GetBoolean());
        Assert.DoesNotContain("review.separation_of_duties", asDoctor.GetProperty("approvalBlockers").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await author.Client.PutAsJsonAsync("/clinical-rules/P8-API-1/versions/1", def, Web)).StatusCode); // frozen after submission
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.Client.GetAsync("/clinical-rules/NOPE-9/versions/1/history")).StatusCode);

        // The author cannot approve their own rule over HTTP either (the role split makes it a 403 already; the service check is covered in the service tests).
        Assert.Equal(HttpStatusCode.Forbidden, (await author.Client.PostAsJsonAsync("/clinical-rules/P8-API-1/versions/1/review", new { decision = "Approve", note = "mine" }, Web)).StatusCode);
    }
}
