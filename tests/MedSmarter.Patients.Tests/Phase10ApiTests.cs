using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedSmarter.Modules.Pharmacies.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 10 over HTTP: organization-scoped access, cross-organization refusal, links that need their own relationship and consent, no outbound effect.</summary>
public class Phase10ApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private const string OrgA = "383c0c5b-386f-5d57-a295-3b25374d9a2d";
    private const string OrgB = "578742b6-efd3-551a-9000-e250d815db67";
    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);

    private static object Event(string text = "Wrong strength handed over", string kind = "DispensingError", string reached = "Yes") =>
        new { kind, reachedPatient = reached, errorExamples = new[] { "IncorrectMedicineFormOrStrength" }, description = text, occurredOn = (string?)null, medicineAsReported = (string?)null, howDetectedOrIntercepted = (string?)null, immediateActionTaken = (string?)null };

    private static object ConcernBody(string text = "Tablets look discoloured", object? productRecord = null, object? asReported = null) =>
        new { type = "AppearanceOrPackaging", description = text, observedOn = (string?)null, productRecord, asReported };

    [Theory]
    [InlineData("GET", "/pharmacy-safety/definitions")]
    [InlineData("POST", $"/organizations/{OrgA}/safety-events")]
    [InlineData("GET", $"/organizations/{OrgA}/safety-events")]
    [InlineData("GET", "/safety-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946")]
    [InlineData("POST", "/safety-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/entries")]
    [InlineData("POST", "/safety-events/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/status")]
    [InlineData("POST", $"/organizations/{OrgA}/product-concerns")]
    [InlineData("GET", $"/organizations/{OrgA}/product-concerns")]
    [InlineData("GET", "/product-concerns/fa1f8b92-53cc-5ad2-a0a8-c39713c37946")]
    [InlineData("POST", "/product-concerns/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/entries")]
    [InlineData("POST", "/product-concerns/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/status")]
    [InlineData("POST", "/product-concerns/fa1f8b92-53cc-5ad2-a0a8-c39713c37946/manufacturer-report-proposal")]
    public async Task Every_phase10_route_returns_401_without_a_token(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path))).StatusCode);
    }

    [Fact]
    public async Task Definitions_cite_the_supplementary_source_and_say_the_list_is_examples_only()
    {
        var res = await (await As("demo-pharmacist")).Client.GetAsync("/pharmacy-safety/definitions");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var j = await ClinicalApiTests.Json(res);
        Assert.Equal(8, j.GetProperty("dispensingErrorExamples").GetArrayLength());
        Assert.False(j.GetProperty("nearMiss").GetProperty("isLectureSlide").GetBoolean());
        Assert.Contains("not a complete or official list", j.GetProperty("taxonomyNotice").GetString(), StringComparison.Ordinal);
        Assert.Equal("AwaitingClinicalReview", j.GetProperty("persianWordingStatus").GetString());
        foreach (var who in new[] { "demo-patient", "demo-physician", "demo-industry" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await (await As(who)).Client.GetAsync("/pharmacy-safety/definitions")).StatusCode);
        }
    }

    [Fact]
    public async Task A_pharmacist_logs_and_reviews_in_their_own_organization_only()
    {
        var pharmacist = await As("demo-pharmacist");
        var admin = await As("demo-pharmacy-admin");
        var res = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/safety-events", Event());
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = (await ClinicalApiTests.Json(res)).GetProperty("event");
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("NotSubmitted", created.GetProperty("externalStatus").GetString());
        Assert.Equal("Submitted", created.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"/safety-events/{id}")).StatusCode); // same organization
        var list = await ClinicalApiTests.Json(await admin.Client.GetAsync($"/organizations/{OrgA}/safety-events"));
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);

        // another organization's route, and a record that belongs to another organization
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgB}/safety-events", Event())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.GetAsync($"/organizations/{OrgB}/safety-events")).StatusCode);
        var foreign = (await factory.Services.GetRequiredService<IPharmacySafetyService>().CreateEventAsync(Guid.NewGuid(), PharmacyActorRole.Pharmacist, Guid.Parse(OrgB), new CreateSafetyEventInput(SafetyEventKind.NearMiss, ReachedPatient.No, null, "Other pharmacy", null, null, null, null), "t", null)).Value!.Event.Id;
        var hidden = await pharmacist.Client.GetAsync($"/safety-events/{foreign}");
        var missing = await pharmacist.Client.GetAsync($"/safety-events/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        static string Stable(string b) => System.Text.RegularExpressions.Regex.Replace(b, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");
        Assert.Equal(Stable(await missing.Content.ReadAsStringAsync()), Stable(await hidden.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.PostAsJsonAsync($"/safety-events/{foreign}/entries", new { kind = "ReviewNote", text = "x" })).StatusCode);

        foreach (var who in new[] { "demo-patient", "demo-physician", "demo-industry", "demo-researcher", "demo-system-admin", "demo-physician-b" })
        {
            var other = await As(who);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"/safety-events/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"/organizations/{OrgA}/safety-events")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.PostAsJsonAsync($"/organizations/{OrgA}/safety-events", Event())).StatusCode);
        }
    }

    [Fact]
    public async Task Near_miss_and_error_rules_and_the_review_flow_work_over_http()
    {
        var pharmacist = await As("demo-pharmacist");
        var admin = await As("demo-pharmacy-admin");
        var bad = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/safety-events", Event(kind: "NearMiss", reached: "Yes"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("near_miss.reached_patient_conflict", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var near = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/safety-events", Event("Caught at the final check", "NearMiss", "No"));
        var id = (await ClinicalApiTests.Json(near)).GetProperty("event").GetProperty("id").GetGuid();
        var v = (await ClinicalApiTests.Json(await admin.Client.GetAsync($"/safety-events/{id}"))).GetProperty("event").GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsJsonAsync($"/safety-events/{id}/entries", new { kind = "ReviewNote", text = "Looked at shelf layout" })).StatusCode);
        v = (await ClinicalApiTests.Json(await admin.Client.GetAsync($"/safety-events/{id}"))).GetProperty("event").GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync($"/safety-events/{id}/status", new { status = "InReview", expectedVersion = v - 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsJsonAsync($"/safety-events/{id}/status", new { status = "InReview", expectedVersion = v })).StatusCode);
        var final = await ClinicalApiTests.Json(await admin.Client.GetAsync($"/safety-events/{id}"));
        Assert.Equal("InReview", final.GetProperty("event").GetProperty("status").GetString());
        Assert.Equal(2, final.GetProperty("entries").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsync($"/safety-events/{id}/entries", new StringContent("not json", System.Text.Encoding.UTF8, "text/plain"))).StatusCode);
    }

    [Fact]
    public async Task A_link_to_an_adverse_event_needs_that_records_own_relationship_and_consent()
    {
        var sara = await As("demo-patient");
        var made = await sara.Client.PostAsJsonAsync($"/patients/{sara.UserId}/adverse-events", new { eventDescription = "Rash", suspectedMedications = Array.Empty<object>() });
        var adr = (await ClinicalApiTests.Json(made)).GetProperty("event").GetProperty("id").GetGuid();
        var pharmacist = await As("demo-pharmacist");
        var res = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/safety-events", new { kind = "DispensingError", reachedPatient = "Yes", description = "Linked", linkedAdverseEventId = adr });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode); // no ADR consent for the pharmacist
        Assert.Contains("link.adr_unavailable", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var missing = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/safety-events", new { kind = "DispensingError", reachedPatient = "Yes", description = "Linked", linkedAdverseEventId = Guid.NewGuid() });
        Assert.Contains("link.adr_unavailable", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal); // a missing record answers the same
    }

    [Fact]
    public async Task A_product_concern_links_to_a_patients_batch_record_only_with_that_patients_consent_and_otherwise_stays_unmatched()
    {
        var sara = await As("demo-patient");
        var products = factory.Services.GetRequiredService<IProductTraceService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rec = (await products.RecordAsync(sara.UserId, ProductRecorder.Patient, sara.UserId, new ProductRecordInput(null, null, "Typed product (fictional)", null, null, "Fictional Maker", "LOT-P10-1", today.AddMonths(-2), today.AddMonths(10), null, null, today.AddDays(-3)), "t", null)).Value!;
        var pharmacist = await As("demo-pharmacist");

        var linked = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/product-concerns", ConcernBody(productRecord: new { subjectId = sara.UserId, productRecordId = rec.Id }));
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var text = await linked.Content.ReadAsStringAsync();
        var c = JsonDocument.Parse(text).RootElement.GetProperty("concern");
        Assert.Equal("LinkedToProductRecord", c.GetProperty("linkState").GetString());
        Assert.Equal("LOT-P10-1", c.GetProperty("linked").GetProperty("batchNumber").GetString());
        Assert.DoesNotContain(sara.UserId.ToString(), text, StringComparison.OrdinalIgnoreCase); // the patient's id is never returned
        Assert.Equal("ProvisionalNotSourceDefined", c.GetProperty("typeBasis").GetString());
        Assert.Equal("NotSubmitted", c.GetProperty("externalStatus").GetString());

        // a patient without consent for this pharmacist: the link is refused and nothing else is substituted
        var p2 = new { UserId = Guid.Parse("02aad59a-2572-5245-b827-d311935e4573") }; // subject-pt-5: no relationship and no consent for the pharmacist
        await factory.Services.GetRequiredService<MedSmarter.Modules.Patients.Contracts.IPatientService>().EnsureOwnAsync(p2.UserId, "t", null);
        var other = (await products.RecordAsync(p2.UserId, ProductRecorder.Patient, p2.UserId, new ProductRecordInput(null, null, "Typed product (fictional)", null, null, null, "LOT-P2", today.AddMonths(-2), today.AddMonths(10), null, null, today.AddDays(-3)), "t", null)).Value!;
        var refused = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/product-concerns", ConcernBody(productRecord: new { subjectId = p2.UserId, productRecordId = other.Id }));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("product_record.unavailable", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var nonexistent = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/product-concerns", ConcernBody(productRecord: new { subjectId = sara.UserId, productRecordId = Guid.NewGuid() }));
        Assert.Contains("product_record.unavailable", await nonexistent.Content.ReadAsStringAsync(), StringComparison.Ordinal); // same answer

        var unmatched = await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/product-concerns", ConcernBody(asReported: new { productName = "Demopril", batchNumber = "LOT-TYPED" }));
        Assert.Equal(HttpStatusCode.Created, unmatched.StatusCode);
        var u = (await ClinicalApiTests.Json(unmatched)).GetProperty("concern");
        Assert.Equal("Unmatched", u.GetProperty("linkState").GetString());
        Assert.Equal(JsonValueKind.Null, u.GetProperty("linked").ValueKind);
        Assert.Contains("product_record.not_linked", u.GetProperty("flags").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("expiry.missing", u.GetProperty("flags").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task A_manufacturer_report_proposal_changes_nothing_in_the_existing_report_pipeline()
    {
        var pharmacist = await As("demo-pharmacist");
        var admin = await As("demo-pharmacy-admin");
        var queue = factory.Services.GetRequiredService<IManufacturerReportOutbox>();
        var before = (await queue.GetQueueAsync()).OutboxByState.Values.Sum();
        var made = await ClinicalApiTests.Json(await pharmacist.Client.PostAsJsonAsync($"/organizations/{OrgA}/product-concerns", ConcernBody("Packaging differs", asReported: new { productName = "Demopril", batchNumber = "LOT-7" })));
        var id = made.GetProperty("concern").GetProperty("id").GetGuid();
        var v = made.GetProperty("concern").GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync($"/product-concerns/{id}/manufacturer-report-proposal", new { note = "worth a report", expectedVersion = v })).StatusCode); // not on a first report
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsJsonAsync($"/product-concerns/{id}/status", new { status = "InReview", expectedVersion = v })).StatusCode);
        v = (await ClinicalApiTests.Json(await admin.Client.GetAsync($"/product-concerns/{id}"))).GetProperty("concern").GetProperty("version").GetInt32();
        var ok = await admin.Client.PostAsJsonAsync($"/product-concerns/{id}/manufacturer-report-proposal", new { note = "Appearance differs across batches; patients would have to agree to a report", expectedVersion = v });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var c = (await ClinicalApiTests.Json(ok)).GetProperty("concern");
        Assert.True(c.GetProperty("manufacturerReportProposed").GetBoolean());
        Assert.Equal("NotSubmitted", c.GetProperty("externalStatus").GetString());
        Assert.Equal(before, (await queue.GetQueueAsync()).OutboxByState.Values.Sum()); // nothing queued, nothing sent
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("demo-patient")).Client.PostAsJsonAsync($"/product-concerns/{id}/manufacturer-report-proposal", new { note = "x", expectedVersion = 1 })).StatusCode);
    }
}
