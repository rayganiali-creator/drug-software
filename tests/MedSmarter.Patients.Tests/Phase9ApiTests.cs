using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 9 over HTTP: default-deny access to adverse-event records (role, relationship, consent, record-bound subject), separation of professional content, no cross-patient access.</summary>
public class Phase9ApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private static readonly JsonSerializerOptions Web = ClinicalApiTests.Web;
    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);
    private static object Body(string text = "Itchy rash on both arms") => new { eventDescription = text, suspectedMedications = Array.Empty<object>() };

    private static async Task<Guid> Create(ClinicalApiTests.Session s, Guid subject, string text = "Itchy rash on both arms")
    {
        var res = await s.Client.PostAsJsonAsync($"/patients/{subject}/adverse-events", Body(text));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ClinicalApiTests.Json(res)).GetProperty("event").GetProperty("id").GetGuid();
    }

    [Theory]
    [InlineData("GET", "/adverse-events/definitions")]
    [InlineData("GET", "/adverse-events/queue")]
    [InlineData("GET", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/adverse-events")]
    [InlineData("POST", "/patients/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/adverse-events")]
    [InlineData("GET", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946")]
    [InlineData("POST", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/follow-ups")]
    [InlineData("POST", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/supplements")]
    [InlineData("POST", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/entries")]
    [InlineData("POST", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/classification")]
    [InlineData("POST", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/status")]
    [InlineData("POST", "/adverse-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/assistance")]
    public async Task Every_adverse_event_route_returns_401_without_a_token(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path))).StatusCode);
    }

    [Fact]
    public async Task Definitions_are_available_to_signed_in_users_and_name_their_supplementary_sources()
    {
        var res = await (await As("demo-patient")).Client.GetAsync("/adverse-events/definitions");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var j = await ClinicalApiTests.Json(res);
        Assert.Equal(5, j.GetProperty("definitions").GetArrayLength());
        Assert.All(j.GetProperty("definitions").EnumerateArray(), d =>
        {
            Assert.False(d.GetProperty("source").GetProperty("isLectureSlide").GetBoolean());
            Assert.Equal("AwaitingClinicalReview", d.GetProperty("persianWordingStatus").GetString());
        });
        Assert.Equal("AwaitingClinicalReview", j.GetProperty("persianWordingStatus").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-industry")).Client.GetAsync("/adverse-events/definitions")).StatusCode);
    }

    [Fact]
    public async Task A_patient_reports_for_themselves_only_and_a_professional_needs_relationship_and_consent()
    {
        var sara = await As("demo-patient");
        var id = await Create(sara, sara.UserId);

        var doctor = await As("demo-physician"); // relationship + adr consent
        var seen = await doctor.Client.GetAsync($"/adverse-events/{id}");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        var e = (await ClinicalApiTests.Json(seen)).GetProperty("event");
        Assert.Equal("Unclassified", e.GetProperty("category").GetString());
        Assert.Equal("NotSubmitted", e.GetProperty("externalStatus").GetString());
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/patients/{sara.UserId}/adverse-events")).StatusCode);

        foreach (var who in new[] { "demo-physician-b", "demo-pharmacist", "demo-patient-2", "demo-industry", "demo-researcher", "demo-system-admin" })
        {
            var other = await As(who);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"/adverse-events/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"/patients/{sara.UserId}/adverse-events")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.PostAsJsonAsync($"/patients/{sara.UserId}/adverse-events", Body())).StatusCode);
        }
    }

    [Fact]
    public async Task A_missing_record_and_a_record_you_may_not_see_are_indistinguishable()
    {
        var sara = await As("demo-patient");
        var id = await Create(sara, sara.UserId);
        var outsider = await As("demo-physician-b");
        var hidden = await outsider.Client.GetAsync($"/adverse-events/{id}");
        var missing = await outsider.Client.GetAsync($"/adverse-events/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        static string Stable(string b) => System.Text.RegularExpressions.Regex.Replace(b, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");
        Assert.Equal(Stable(await missing.Content.ReadAsStringAsync()), Stable(await hidden.Content.ReadAsStringAsync())); // only the trace id differs
    }

    [Fact]
    public async Task A_relationship_without_consent_grants_nothing()
    {
        // demo-physician has a Treating relationship with demo-patient-2 but no consent for it
        var p2 = await As("demo-patient-2");
        await factory.CreateClient().GetAsync("/health"); // keep the host warm
        var id = await Create(p2, p2.UserId);
        var doctor = await As("demo-physician");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"/adverse-events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync($"/patients/{p2.UserId}/adverse-events", Body())).StatusCode);
    }

    [Fact]
    public async Task A_professional_can_enter_a_record_for_a_consenting_patient_and_it_is_labelled_professional_entered()
    {
        var sara = await As("demo-patient");
        var doctor = await As("demo-physician");
        var res = await doctor.Client.PostAsJsonAsync($"/patients/{sara.UserId}/adverse-events", Body("Reported by the patient at the visit"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var e = (await ClinicalApiTests.Json(res)).GetProperty("event");
        Assert.Equal("ProfessionalEntered", e.GetProperty("origin").GetString());
        Assert.Equal("Physician", e.GetProperty("reporterRole").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync($"/patients/{sara.UserId}/adverse-events", Body(" "))).StatusCode);
    }

    [Fact]
    public async Task Review_actions_are_professional_only_and_the_patient_view_hides_professional_content_and_the_category()
    {
        var sara = await As("demo-patient");
        var doctor = await As("demo-physician");
        var id = await Create(sara, sara.UserId);
        var version = (await ClinicalApiTests.Json(await doctor.Client.GetAsync($"/adverse-events/{id}"))).GetProperty("event").GetProperty("version").GetInt32();

        foreach (var (path, body) in new (string, object)[]
        {
            ("entries", new { kind = "ReviewNote", text = "x" }),
            ("classification", new { category = "AdverseEvent", reason = "x", expectedVersion = version }),
            ("status", new { status = "InReview", expectedVersion = version }),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await sara.Client.PostAsJsonAsync($"/adverse-events/{id}/{path}", body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await sara.Client.PostAsync($"/adverse-events/{id}/assistance", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await doctor.Client.PostAsJsonAsync($"/adverse-events/{id}/entries", new { kind = "ReviewNote", text = "PRO-ONLY-NOTE" })).StatusCode);
        var v2 = (await ClinicalApiTests.Json(await doctor.Client.GetAsync($"/adverse-events/{id}"))).GetProperty("event").GetProperty("version").GetInt32();
        var classified = await doctor.Client.PostAsJsonAsync($"/adverse-events/{id}/classification", new { category = "AdverseEvent", reason = "Occurred during treatment; no causal link assumed", expectedVersion = v2 });
        Assert.Equal(HttpStatusCode.OK, classified.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await doctor.Client.PostAsJsonAsync($"/adverse-events/{id}/classification", new { category = "NearMiss", reason = "x", expectedVersion = v2 })).StatusCode); // stale

        var patientView = await (await sara.Client.GetAsync($"/adverse-events/{id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRO-ONLY-NOTE", patientView, StringComparison.Ordinal);
        var pj = JsonDocument.Parse(patientView).RootElement.GetProperty("event");
        Assert.Equal(JsonValueKind.Null, pj.GetProperty("category").ValueKind);
        var list = await ClinicalApiTests.Json(await sara.Client.GetAsync($"/patients/{sara.UserId}/adverse-events"));
        Assert.All(list.EnumerateArray(), x => Assert.Equal(JsonValueKind.Null, x.GetProperty("category").ValueKind));

        // the patient can answer, and only on their own record
        Assert.Equal(HttpStatusCode.Created, (await sara.Client.PostAsJsonAsync($"/adverse-events/{id}/follow-ups", new { text = "It started on Tuesday" })).StatusCode);
        var p2 = await As("demo-patient-2");
        Assert.Equal(HttpStatusCode.Forbidden, (await p2.Client.PostAsJsonAsync($"/adverse-events/{id}/follow-ups", new { text = "x" })).StatusCode);
    }

    [Fact]
    public async Task Assistance_is_available_to_a_permitted_reviewer_and_says_it_is_a_template_not_a_conclusion()
    {
        var sara = await As("demo-patient");
        var id = await Create(sara, sara.UserId, "Dizzy in the mornings");
        var res = await (await As("demo-physician")).Client.PostAsync($"/adverse-events/{id}/assistance", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var a = await ClinicalApiTests.Json(res);
        Assert.Equal("Template:no-model", a.GetProperty("origin").GetString());
        Assert.Contains("not a conclusion", a.GetProperty("notice").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-physician-b")).Client.PostAsync($"/adverse-events/{id}/assistance", null)).StatusCode);
    }

    [Fact]
    public async Task The_queue_lists_only_records_of_patients_the_reviewer_may_read_now()
    {
        var sara = await As("demo-patient");
        var p2 = await As("demo-patient-2");
        var saraRecord = await Create(sara, sara.UserId, "Queue test Sara");
        var p2Record = await Create(p2, p2.UserId, "Queue test Patient2");

        var doctor = await ClinicalApiTests.Json(await (await As("demo-physician")).Client.GetAsync("/adverse-events/queue"));
        var ids = doctor.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(saraRecord, ids);
        Assert.DoesNotContain(p2Record, ids); // relationship but no consent

        var b = await ClinicalApiTests.Json(await (await As("demo-physician-b")).Client.GetAsync("/adverse-events/queue"));
        Assert.Empty(b.EnumerateArray()); // an empty list, not an error that would reveal anything
        Assert.Equal(HttpStatusCode.Forbidden, (await sara.Client.GetAsync("/adverse-events/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-industry")).Client.GetAsync("/adverse-events/queue")).StatusCode);
    }

    [Fact]
    public async Task A_patient_cannot_use_a_subject_in_the_body_to_reach_another_patient()
    {
        var p2 = await As("demo-patient-2");
        var sara = await As("demo-patient");
        // the route subject is authorized; a body cannot name another subject (there is no such field)
        Assert.Equal(HttpStatusCode.Forbidden, (await p2.Client.PostAsJsonAsync($"/patients/{sara.UserId}/adverse-events", new { subjectId = p2.UserId, eventDescription = "x", suspectedMedications = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await p2.Client.GetAsync($"/patients/{sara.UserId}/adverse-events")).StatusCode);
    }
}
