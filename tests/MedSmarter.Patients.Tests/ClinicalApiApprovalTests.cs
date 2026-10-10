using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedSmarter.Modules.ClinicalRules;
using MedSmarter.Modules.ClinicalRules.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>An approved rule over HTTP, end to end: finding, guidance message with origin, patient actions. Separate class = separate application instance, so the approval cannot leak into other tests.</summary>
public class ClinicalApiApprovalTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    [Fact]
    public async Task An_approved_data_quality_rule_produces_an_actionable_finding_and_a_guidance_message_the_patient_alone_can_mark_seen()
    {
        var author = await ClinicalApiTests.Login(factory, "demo-content-manager");
        var doctor = await ClinicalApiTests.Login(factory, "demo-physician");
        var patient = await ClinicalApiTests.Login(factory, "demo-patient-2");
        var web = ClinicalApiTests.Web;

        var def = DemoRules.All(DateTimeOffset.UtcNow).Single(r => r.Definition.RuleId == "DEMO-DQ-001").Definition with
        {
            RuleId = "E2E-DQ-1", Evidence = [new EvidenceRef("test-fixture", "TEST FIXTURE (not a real source)", "1", null, EvidenceValidation.Validated, "fixture", null)],
        };
        Assert.Equal(HttpStatusCode.Created, (await author.Client.PostAsJsonAsync("/clinical-rules", def, web)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.Client.PostAsync("/clinical-rules/E2E-DQ-1/versions/1/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsJsonAsync("/clinical-rules/E2E-DQ-1/versions/1/review", new { decision = "Approve", note = "fixture" }, web)).StatusCode);

        var add = await patient.Client.PostAsJsonAsync($"/patients/{patient.UserId}/medications", new { unregisteredName = "Zzunknownol", frequency = "AsNeeded", startDate = "2026-10-01", source = "SelfReported" }, web);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        var res = await patient.Client.PostAsync($"/patients/{patient.UserId}/safety/assessments?locale=fa", null);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var a = await ClinicalApiTests.Json(res);
        Assert.Equal("CompletedWithFindings", a.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("Created", a.GetProperty("guidanceState").GetString());
        var finding = a.GetProperty("result").GetProperty("findings").EnumerateArray().Single(f => f.GetProperty("ruleId").GetString() == "E2E-DQ-1");
        Assert.True(finding.GetProperty("actionable").GetBoolean());
        Assert.False(a.GetProperty("result").GetProperty("safetyClaimAllowed").GetBoolean());

        var messages = await ClinicalApiTests.Json(await patient.Client.GetAsync("/guidance/messages"));
        var message = messages.EnumerateArray().Single(m => m.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object && o.GetProperty("ruleId").GetString() == "E2E-DQ-1");
        Assert.Equal("Sent", message.GetProperty("status").GetString()); // created is not delivered or seen
        Assert.Equal("fa", message.GetProperty("locale").GetString());
        Assert.Equal(a.GetProperty("id").GetGuid(), message.GetProperty("origin").GetProperty("assessmentId").GetGuid());
        Assert.Equal(JsonValueKind.Null, message.GetProperty("professional").ValueKind);

        // The physician reading the assessment does not mark anything seen on the patient's behalf.
        var forbiddenStatus = await doctor.Client.PostAsJsonAsync($"/guidance/messages/{message.GetProperty("id").GetGuid()}/status", new { status = "Seen" }, web);
        Assert.True(forbiddenStatus.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        var seen = await patient.Client.PostAsJsonAsync($"/guidance/messages/{message.GetProperty("id").GetGuid()}/status", new { status = "Seen" }, web);
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);

        // Running it again does not create a second message.
        await patient.Client.PostAsync($"/patients/{patient.UserId}/safety/assessments", null);
        var after = await ClinicalApiTests.Json(await patient.Client.GetAsync("/guidance/messages"));
        Assert.Single(after.EnumerateArray(), m => m.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object && o.GetProperty("ruleId").GetString() == "E2E-DQ-1");
    }
}
