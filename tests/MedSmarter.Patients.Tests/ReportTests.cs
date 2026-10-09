using System.Reflection;
using System.Text.Json;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

public class ReportTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] ProductsOnly = [DataScopes.Products];

    private sealed record Setup(PEnv Env, Guid Patient, ProductRecordDto Product);

    private static async Task<Setup> Arrange(Dictionary<string, string?>? settings = null, bool consent = true, string[]? scopes = null)
    {
        var env = new PEnv(settings: settings);
        var id = await env.Patient("demo-patient");
        await env.Patients.UpdateProfileAsync(id, id, new UpdateProfileCommand(1985, SexGroup.Female, 60, 165, "Asia/Tehran", null), "t", null);
        var product = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id,
            new ProductRecordInput(null, await env.Reference_("demopril"), null, null, null, null, "LOT-2026-A1", env.Today.AddMonths(-2), env.Today.AddMonths(10), null, null, env.Today.AddDays(-5)), "t", null)).Value!;
        if (consent)
        {
            await env.Consent("demo-patient", ConsentPurposes.ManufacturerReport, scopes ?? ProductsOnly);
        }

        return new Setup(env, id, product);
    }

    private static ReportDraftInput Draft(Setup s, ReportIssueType issue = ReportIssueType.AbnormalAppearanceOrPackaging, ReportSeverity severity = ReportSeverity.Mild, string? description = "Tablets looked crumbly", bool concomitant = false, string? requestId = null) =>
        new(s.Product.Id, issue, severity, s.Env.Today.AddDays(-2), 14, description, concomitant, requestId);

    private static async Task<ManufacturerReportDto> Created(Setup s, ReportDraftInput? input = null) =>
        (await s.Env.Reports.CreateDraftAsync(s.Patient, s.Patient, input ?? Draft(s), "t", null)).Value!;

    // ---------- de-identification ----------

    [Fact]
    public void The_payload_type_has_no_field_that_could_identify_a_person()
    {
        var names = typeof(ManufacturerReportPayload).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["AgeGroup", "BatchNumber", "ConcomitantMedications", "Description", "DurationOfUseDays", "ExpiryDate", "GenericName", "Gtin", "IsDemo", "IssueType", "ManufactureDate", "ManufacturerName", "OccurredOn", "ProductName", "ReportReference", "SchemaVersion", "Severity", "SexGroup"],
            names); // a new field needs a conscious change of this list (and of docs/phase5/04)
        foreach (var n in names)
        {
            foreach (var banned in new[] { "name", "user", "patient", "subject", "account", "birth", "address", "phone", "email", "national", "insurance", "contact" })
            {
                Assert.False(n.Contains(banned, StringComparison.OrdinalIgnoreCase) && n is not ("ProductName" or "GenericName" or "ManufacturerName"), $"{n} looks like identity data");
            }
        }
    }

    [Fact]
    public async Task What_leaves_contains_nothing_that_identifies_the_patient()
    {
        var s = await Arrange(scopes: [DataScopes.Products, DataScopes.Profile, DataScopes.Medications]);
        using var env = s.Env;
        await env.TakeMedication(s.Patient, "nocturin");
        var report = await Created(s, Draft(s, concomitant: true, severity: ReportSeverity.Moderate, issue: ReportIssueType.AdverseEvent));
        var submitted = (await env.Reports.SubmitAsync(s.Patient, s.Patient, report.Id, report.Version, "t", null)).Value!;
        var json = JsonSerializer.Serialize(submitted.PayloadPreview);
        var patientRecord = (await env.Get<IPatientDirectory>().FindBySubjectAsync(s.Patient))!;

        Assert.DoesNotContain(s.Patient.ToString(), json, StringComparison.OrdinalIgnoreCase); // account id
        Assert.DoesNotContain(patientRecord.PatientId.ToString(), json, StringComparison.OrdinalIgnoreCase); // internal patient id
        Assert.DoesNotContain(report.Id.ToString(), json, StringComparison.OrdinalIgnoreCase); // internal report id
        Assert.DoesNotContain(s.Product.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sara", json, StringComparison.OrdinalIgnoreCase); // the patient's name
        Assert.DoesNotContain("1985", json, StringComparison.Ordinal); // birth year
        // The weight must not leak. The random report reference (hex) could contain "62" by chance, so it is removed first.
        var withoutReference = System.Text.RegularExpressions.Regex.Replace(json, "MSR-[0-9A-Fa-f]+", string.Empty).Replace("2026", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("62", withoutReference, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", json, StringComparison.OrdinalIgnoreCase); // account e-mail
        var p = submitted.PayloadPreview!;
        Assert.Equal("40-64", p.AgeGroup); // bands only
        Assert.Equal("Female", p.SexGroup);
        Assert.Equal(["Nocturin"], p.ConcomitantMedications);
        Assert.StartsWith("MSR-", p.ReportReference, StringComparison.Ordinal);
        Assert.True(p.IsDemo);
    }

    [Fact]
    public async Task Demographics_and_other_medicines_are_included_only_with_the_matching_consent_scope()
    {
        var s = await Arrange(); // consent covers products only
        using var env = s.Env;
        var report = await Created(s);
        var preview = (await env.Reports.GetAsync(s.Patient, report.Id)).Value!.PayloadPreview!;
        Assert.Equal("unknown", preview.AgeGroup);
        Assert.Equal("unknown", preview.SexGroup);
        Assert.Empty(preview.ConcomitantMedications);

        var withMeds = await Created(s, Draft(s, concomitant: true));
        var submit = await env.Reports.SubmitAsync(s.Patient, s.Patient, withMeds.Id, withMeds.Version, "t", null);
        Assert.Equal(TraceError.Conflict, submit.Error); // asking to name other medicines needs the medications scope too
        Assert.Equal("consent.scope_medications_required", submit.Detail);
    }

    [Fact]
    public async Task A_description_that_could_identify_the_patient_is_refused_everywhere()
    {
        var s = await Arrange();
        using var env = s.Env;
        var r = await env.Reports.CreateDraftAsync(s.Patient, s.Patient, Draft(s, description: "my number is 09121234567"), "t", null);
        Assert.Equal(TraceError.Validation, r.Error);
        Assert.Contains("looks_identifying", r.Detail, StringComparison.Ordinal);
        Assert.Contains("looks_identifying", (await env.Reports.CreateDraftAsync(s.Patient, s.Patient, Draft(s, description: "mail me sara@example.invalid"), "t", null)).Detail, StringComparison.Ordinal);
    }

    // ---------- consent and review ----------

    [Fact]
    public async Task Without_consent_a_report_cannot_be_submitted_and_stays_a_draft()
    {
        var s = await Arrange(consent: false);
        using var env = s.Env;
        var report = await Created(s);
        Assert.False(report.ConsentActive);
        Assert.Null(report.PayloadPreview);
        var submit = await env.Reports.SubmitAsync(s.Patient, s.Patient, report.Id, report.Version, "t", null);
        Assert.Equal(TraceError.Conflict, submit.Error);
        Assert.Equal("consent.required", submit.Detail);
        Assert.Equal(ReportStatus.Draft, (await env.Reports.GetAsync(s.Patient, report.Id)).Value!.Status);
        var q = await env.Outbox.GetQueueAsync();
        Assert.Equal(0, q.OutboxByState["Pending"]);
        Assert.Contains(await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: s.Patient, Take: 50)), e => e.Action == AuditActions.ManufacturerReportBlocked);
    }

    [Fact]
    public async Task A_sensitive_report_waits_for_a_human_review_and_a_mild_package_issue_goes_straight_to_the_queue()
    {
        var s = await Arrange();
        using var env = s.Env;
        var mild = await Created(s);
        var direct = (await env.Reports.SubmitAsync(s.Patient, s.Patient, mild.Id, mild.Version, "t", null)).Value!;
        Assert.Equal(ReportStatus.ReadyToSend, direct.Status);
        Assert.False(direct.ReviewRequired);

        var severe = await Created(s, Draft(s, ReportIssueType.AdverseEvent, ReportSeverity.Severe, "felt unwell after taking it"));
        var pending = (await env.Reports.SubmitAsync(s.Patient, s.Patient, severe.Id, severe.Version, "t", null)).Value!;
        Assert.Equal(ReportStatus.PendingConsentOrReview, pending.Status);
        Assert.True(pending.ReviewRequired);
        Assert.Equal(1, (await env.Outbox.GetQueueAsync()).OutboxByState["Pending"]); // only the mild one is queued

        var unknown = await Created(s, Draft(s, ReportIssueType.QualityProblem, ReportSeverity.Unknown, "smell"));
        Assert.Equal(ReportStatus.PendingConsentOrReview, (await env.Reports.SubmitAsync(s.Patient, s.Patient, unknown.Id, unknown.Version, "t", null)).Value!.Status); // unknown severity is treated as sensitive
    }

    [Fact]
    public async Task The_review_policy_is_configurable_but_sensitive_reports_are_always_reviewed()
    {
        var always = await Arrange(new() { ["ManufacturerReports:ReviewPolicy"] = "Always" });
        using (always.Env)
        {
            var r = await Created(always);
            Assert.Equal(ReportStatus.PendingConsentOrReview, (await always.Env.Reports.SubmitAsync(always.Patient, always.Patient, r.Id, r.Version, "t", null)).Value!.Status);
        }

        var none = await Arrange(new() { ["ManufacturerReports:ReviewPolicy"] = "None" });
        using (none.Env)
        {
            var mild = await Created(none);
            Assert.Equal(ReportStatus.ReadyToSend, (await none.Env.Reports.SubmitAsync(none.Patient, none.Patient, mild.Id, mild.Version, "t", null)).Value!.Status);
            var adverse = await Created(none, Draft(none, ReportIssueType.AdverseEvent, ReportSeverity.Mild, "rash"));
            Assert.Equal(ReportStatus.PendingConsentOrReview, (await none.Env.Reports.SubmitAsync(none.Patient, none.Patient, adverse.Id, adverse.Version, "t", null)).Value!.Status); // "None" cannot waive review of an adverse event
        }
    }

    [Fact]
    public async Task A_reviewer_approves_or_rejects_and_the_patient_cannot_review_their_own_report()
    {
        var s = await Arrange();
        using var env = s.Env;
        var draft = await Created(s, Draft(s, ReportIssueType.AdverseEvent, ReportSeverity.Moderate, "dizzy"));
        var pending = (await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null)).Value!;
        var pharmacist = env.UserId("demo-pharmacist");

        Assert.Equal(TraceError.Forbidden, (await env.Reports.ReviewAsync(s.Patient, s.Patient, pending.Id, new ReviewReportCommand(ReviewDecision.Approve, null, pending.Version), "t", null)).Error);
        Assert.Equal(TraceError.Validation, (await env.Reports.ReviewAsync(pharmacist, s.Patient, pending.Id, new ReviewReportCommand(ReviewDecision.Reject, null, pending.Version), "t", null)).Error); // a rejection explains itself
        Assert.Equal(TraceError.Conflict, (await env.Reports.ReviewAsync(pharmacist, s.Patient, pending.Id, new ReviewReportCommand(ReviewDecision.Approve, null, pending.Version - 1), "t", null)).Error);

        var rejected = (await env.Reports.ReviewAsync(pharmacist, s.Patient, pending.Id, new ReviewReportCommand(ReviewDecision.Reject, "please add when you started the tablets", pending.Version), "t", null)).Value!;
        Assert.Equal(ReportStatus.Draft, rejected.Status);
        Assert.Equal(ReviewDecision.Reject, rejected.ReviewDecision);
        Assert.Equal("please add when you started the tablets", rejected.ReviewerNote);

        var edited = await env.Reports.UpdateDraftAsync(s.Patient, s.Patient, rejected.Id, Draft(s, ReportIssueType.AdverseEvent, ReportSeverity.Moderate, "dizzy since the second day") with { ExpectedVersion = rejected.Version }, "t", null);
        var again = (await env.Reports.SubmitAsync(s.Patient, s.Patient, rejected.Id, edited.Value!.Version, "t", null)).Value!;
        var approved = (await env.Reports.ReviewAsync(pharmacist, s.Patient, again.Id, new ReviewReportCommand(ReviewDecision.Approve, null, again.Version), "t", null)).Value!;
        Assert.Equal(ReportStatus.ReadyToSend, approved.Status);
        Assert.NotNull(approved.ReviewedAt);
        Assert.Equal(TraceError.Conflict, (await env.Reports.ReviewAsync(pharmacist, s.Patient, again.Id, new ReviewReportCommand(ReviewDecision.Approve, null, approved.Version), "t", null)).Error); // already decided
    }

    [Fact]
    public async Task A_submitted_report_cannot_be_edited_and_drafts_are_idempotent_per_client_request()
    {
        var s = await Arrange();
        using var env = s.Env;
        var first = await Created(s, Draft(s, requestId: "client-req-1"));
        var retry = await Created(s, Draft(s, requestId: "client-req-1"));
        Assert.Equal(first.Id, retry.Id); // a retried request does not create a second report
        Assert.Single((await env.Reports.ListAsync(s.Patient)).Value!);
        var submitted = (await env.Reports.SubmitAsync(s.Patient, s.Patient, first.Id, first.Version, "t", null)).Value!;
        Assert.Equal("report.not_editable", (await env.Reports.UpdateDraftAsync(s.Patient, s.Patient, first.Id, Draft(s) with { ExpectedVersion = submitted.Version }, "t", null)).Detail);
        Assert.Equal("report.not_submittable", (await env.Reports.SubmitAsync(s.Patient, s.Patient, first.Id, submitted.Version, "t", null)).Detail);
    }

    [Fact]
    public async Task Another_patients_report_and_product_cannot_be_used()
    {
        var s = await Arrange();
        using var env = s.Env;
        var other = await env.Patient("demo-patient-2");
        Assert.Contains("product.not_found", (await env.Reports.CreateDraftAsync(other, other, Draft(s), "t", null)).Detail, StringComparison.Ordinal); // A's product id on B's route
        var report = await Created(s);
        Assert.Equal(TraceError.NotFound, (await env.Reports.GetAsync(other, report.Id)).Error);
        Assert.Equal(TraceError.NotFound, (await env.Reports.CancelAsync(other, other, report.Id, "t", null)).Error);
        Assert.Equal(TraceError.NotFound, (await env.Reports.SubmitAsync(other, other, report.Id, 1, "t", null)).Error);
        Assert.Empty((await env.Reports.ListAsync(other)).Value!);
    }

    // ---------- sending (mock only) ----------

    [Fact]
    public async Task An_approved_report_travels_from_the_queue_to_a_mock_acknowledgement_exactly_once()
    {
        var s = await Arrange();
        using var env = s.Env;
        var mock = env.Get<MockManufacturerReportProvider>();
        var draft = await Created(s);
        var ready = (await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null)).Value!;
        Assert.Equal(ReportStatus.ReadyToSend, ready.Status);

        var queue = await env.Outbox.GetQueueAsync();
        Assert.True(queue.ProviderIsMock);
        Assert.Contains("MOCK", queue.Notice, StringComparison.Ordinal);
        Assert.Equal(1, queue.ReportsByStatus["ReadyToSend"]);
        Assert.DoesNotContain("crumbly", JsonSerializer.Serialize(queue), StringComparison.Ordinal); // the queue shows no clinical content

        var run = await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal(1, run.Sent);
        var done = (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!;
        Assert.Equal(ReportStatus.Acknowledged, done.Status);
        Assert.True(done.IsMockDelivery);
        Assert.NotNull(done.SentAt);
        Assert.NotNull(done.AcknowledgedAt);
        Assert.Equal(1, mock.AcceptedCount);

        var second = await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal(0, second.Considered); // nothing left to send
        Assert.Equal(1, mock.AcceptedCount);
        var actions = (await env.Audit.QueryAsync(new AuditQuery(Take: 200))).Select(e => e.Action).ToList();
        Assert.Contains(AuditActions.ManufacturerReportSent, actions);
        Assert.Contains(AuditActions.ManufacturerReportAcknowledged, actions);
    }

    [Fact]
    public async Task The_receiver_gets_the_same_key_every_time_so_a_resend_cannot_create_a_second_report()
    {
        var s = await Arrange();
        using var env = s.Env;
        var mock = env.Get<MockManufacturerReportProvider>();
        var draft = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        var payload = JsonSerializer.Deserialize<ManufacturerReportPayload>(JsonSerializer.Serialize((await env.Reports.GetAsync(s.Patient, draft.Id)).Value!.PayloadPreview), Web)!;
        var one = await mock.SubmitAsync(payload, "msr-key-1");
        var two = await mock.SubmitAsync(payload, "msr-key-1");
        Assert.Equal(one.ExternalReference, two.ExternalReference);
        Assert.Equal(1, mock.AcceptedCount);
    }

    [Fact]
    public async Task A_temporary_failure_is_retried_with_backoff_and_then_succeeds()
    {
        var s = await Arrange(new() { ["ManufacturerReports:Mock:FailFirstAttempts"] = "2" });
        using var env = s.Env;
        var draft = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        var admin = env.UserId("demo-system-admin");

        var first = await env.Outbox.ProcessDueAsync(admin, 20, "t", null);
        Assert.Equal(1, first.Retrying);
        Assert.Equal(ReportStatus.ReadyToSend, (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!.Status);
        Assert.Equal(0, (await env.Outbox.ProcessDueAsync(admin, 20, "t", null)).Considered); // not due yet: no hammering

        env.Clock.Advance(TimeSpan.FromMinutes(2)); // first backoff is 1 minute
        Assert.Equal(1, (await env.Outbox.ProcessDueAsync(admin, 20, "t", null)).Retrying);
        env.Clock.Advance(TimeSpan.FromMinutes(4)); // second backoff is 5 minutes: not yet
        Assert.Equal(0, (await env.Outbox.ProcessDueAsync(admin, 20, "t", null)).Considered);
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await env.Outbox.ProcessDueAsync(admin, 20, "t", null)).Sent);
        var done = (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!;
        Assert.Equal(ReportStatus.Acknowledged, done.Status);
        Assert.Equal(3, done.Attempts);
    }

    [Fact]
    public async Task After_too_many_failures_the_report_fails_and_a_person_can_retry_it()
    {
        var s = await Arrange(new() { ["ManufacturerReports:Mock:FailFirstAttempts"] = "2", ["ManufacturerReports:MaxAttempts"] = "2" });
        using var env = s.Env;
        var draft = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        var admin = env.UserId("demo-system-admin");
        await env.Outbox.ProcessDueAsync(admin, 20, "t", null);
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        var last = await env.Outbox.ProcessDueAsync(admin, 20, "t", null);
        Assert.Equal(1, last.Failed);
        var failed = (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!;
        Assert.Equal(ReportStatus.Failed, failed.Status);
        Assert.Equal("mock_unavailable", failed.FailureCode);
        Assert.Equal(TraceError.Conflict, (await env.Outbox.RetryFailedAsync(admin, Guid.NewGuid(), "t", null)).Error == TraceError.NotFound ? TraceError.Conflict : TraceError.None);

        Assert.True((await env.Outbox.RetryFailedAsync(admin, draft.Id, "t", null)).Succeeded);
        Assert.Equal(ReportStatus.ReadyToSend, (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!.Status);
        Assert.Equal(1, (await env.Outbox.ProcessDueAsync(admin, 20, "t", null)).Sent); // the mock has used up its failures
        Assert.Equal(TraceError.Conflict, (await env.Outbox.RetryFailedAsync(admin, draft.Id, "t", null)).Error); // only failed reports can be retried
    }

    [Fact]
    public async Task Withdrawing_consent_after_approval_stops_the_report_before_it_leaves()
    {
        var s = await Arrange();
        using var env = s.Env;
        var mock = env.Get<MockManufacturerReportProvider>();
        var draft = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        var consent = (await env.Consents.ListGivenAsync(s.Patient)).Single(c => c.Purpose == ConsentPurposes.ManufacturerReport);
        await env.Consents.RevokeAsync(s.Patient, consent.Id, "t", null);

        var run = await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal(1, run.Blocked);
        Assert.Equal(0, mock.AcceptedCount); // nothing was sent
        var report = (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!;
        Assert.Equal(ReportStatus.PendingConsentOrReview, report.Status);
        Assert.Equal("consent_revoked", report.FailureCode);
        Assert.False(report.ConsentActive);

        await env.Consent("demo-patient", ConsentPurposes.ManufacturerReport, ProductsOnly); // consent given again: a reviewer can approve it again
        var pharmacist = env.UserId("demo-pharmacist");
        Assert.Equal(ReportStatus.ReadyToSend, (await env.Reports.ReviewAsync(pharmacist, s.Patient, draft.Id, new ReviewReportCommand(ReviewDecision.Approve, null, report.Version), "t", null)).Value!.Status);
        Assert.Equal(1, (await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null)).Sent);
    }

    [Fact]
    public async Task Narrowing_consent_after_approval_stops_data_the_new_consent_no_longer_covers()
    {
        // Found by the repository review after Phase 5: the send-time check used to look at the batch scope only, so a patient who replaced
        // "batch + age/sex + other medicines" by "batch only" after approval still had age band, sex and other medicines sent.
        var s = await Arrange(scopes: [DataScopes.Products, DataScopes.Profile, DataScopes.Medications]);
        using var env = s.Env;
        await env.TakeMedication(s.Patient, "nocturin");
        var mock = env.Get<MockManufacturerReportProvider>();
        var draft = await Created(s, Draft(s, concomitant: true));
        var submitted = (await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null)).Value!;
        var pharmacist = env.UserId("demo-pharmacist");
        Assert.Equal(ReportStatus.ReadyToSend, (await env.Reports.ReviewAsync(pharmacist, s.Patient, draft.Id, new ReviewReportCommand(ReviewDecision.Approve, null, submitted.Version), "t", null)).Value!.Status);

        foreach (var c in (await env.Consents.ListGivenAsync(s.Patient)).Where(c => c.Purpose == ConsentPurposes.ManufacturerReport))
        {
            await env.Consents.RevokeAsync(s.Patient, c.Id, "t", null);
        }

        await env.Consent("demo-patient", ConsentPurposes.ManufacturerReport, ProductsOnly); // narrower than what the frozen payload contains

        var run = await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal(1, run.Blocked);
        Assert.Equal(0, mock.AcceptedCount);
        Assert.Equal(ReportStatus.PendingConsentOrReview, (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!.Status);
    }

    [Fact]
    public async Task A_report_that_was_changed_after_approval_is_not_sent()
    {
        var s = await Arrange();
        using var env = s.Env;
        var draft = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        var factory = env.Get<IDbContextFactory<ProductTraceDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Reports.SingleAsync();
            row.PayloadJson = row.PayloadJson!.Replace("crumbly", "tampered", StringComparison.Ordinal); // someone edits what was approved
            await db.SaveChangesAsync();
        }

        var run = await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal(1, run.Failed);
        var failed = (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!;
        Assert.Equal(ReportStatus.Failed, failed.Status);
        Assert.Equal("payload_integrity", failed.FailureCode);
        Assert.Equal(0, env.Get<MockManufacturerReportProvider>().AcceptedCount);
    }

    [Fact]
    public async Task Cancelling_removes_the_report_from_the_queue_but_a_delivered_report_cannot_be_recalled()
    {
        var s = await Arrange();
        using var env = s.Env;
        var a = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, a.Id, a.Version, "t", null);
        Assert.Equal(ReportStatus.Cancelled, (await env.Reports.CancelAsync(s.Patient, s.Patient, a.Id, "t", null)).Value!.Status);
        Assert.Equal(0, (await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null)).Sent);

        var b = await Created(s, Draft(s) with { OccurredOn = env.Today.AddDays(-3) });
        await env.Reports.SubmitAsync(s.Patient, s.Patient, b.Id, b.Version, "t", null);
        await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal("report.not_cancellable", (await env.Reports.CancelAsync(s.Patient, s.Patient, b.Id, "t", null)).Detail);
        Assert.Equal(TraceError.Conflict, (await env.Products.RemoveAsync(s.Patient, s.Patient, s.Product.Id, "t", null)).Error); // the product a report refers to stays
    }

    [Fact]
    public async Task Without_a_configured_provider_nothing_is_sent_and_the_queue_says_so()
    {
        var s = await Arrange(new() { ["ManufacturerReports:Provider"] = "Disabled" });
        using var env = s.Env;
        var draft = await Created(s);
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        var queue = await env.Outbox.GetQueueAsync();
        Assert.False(queue.ProviderConfigured);
        Assert.False(queue.ProviderIsMock);
        Assert.Contains("agreement", queue.Notice, StringComparison.Ordinal); // a real connection needs an agreement and authorization
        var run = await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "t", null);
        Assert.Equal("provider_not_configured", run.Skipped);
        Assert.Equal(ReportStatus.ReadyToSend, (await env.Reports.GetAsync(s.Patient, draft.Id)).Value!.Status);
        Assert.Null(env.Get<IServiceProvider>().GetService<MockManufacturerReportProvider>());
    }

    [Fact]
    public void The_mock_provider_cannot_be_enabled_outside_development()
    {
        Assert.Throws<InvalidOperationException>(() => ManufacturerReportsGuard.EnsureSafe("Production", new ManufacturerReportsOptions { Provider = "Mock" }));
        Assert.Throws<InvalidOperationException>(() => ManufacturerReportsGuard.EnsureSafe("Staging", new ManufacturerReportsOptions { Provider = "Mock" }));
        Assert.Throws<InvalidOperationException>(() => ManufacturerReportsGuard.EnsureSafe("Production", new ManufacturerReportsOptions { SeedDemoData = true }));
        Assert.Throws<InvalidOperationException>(() => ManufacturerReportsGuard.EnsureSafe("Development", new ManufacturerReportsOptions { Provider = "Real" }));
        Assert.Throws<InvalidOperationException>(() => ManufacturerReportsGuard.EnsureSafe("Development", new ManufacturerReportsOptions { ReviewPolicy = "Never" }));
        ManufacturerReportsGuard.EnsureSafe("Production", new ManufacturerReportsOptions());
        ManufacturerReportsGuard.EnsureSafe("Development", new ManufacturerReportsOptions { Provider = "Mock", SeedDemoData = true });
    }

    [Fact]
    public async Task The_audit_trail_of_a_report_never_contains_its_description()
    {
        var s = await Arrange();
        using var env = s.Env;
        var draft = await Created(s, Draft(s, description: "unique-description-text-123"));
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "web", "corr-1");
        await env.Outbox.ProcessDueAsync(env.UserId("demo-system-admin"), 20, "web", "corr-2");
        var all = await env.Audit.QueryAsync(new AuditQuery(Take: 500));
        Assert.Contains(all, e => e.Action == AuditActions.ManufacturerReportCreated);
        Assert.Contains(all, e => e.Action == AuditActions.ManufacturerReportSubmitted);
        Assert.DoesNotContain(all, e => e.Metadata.Values.Any(v => v.Contains("unique-description", StringComparison.Ordinal)) || (e.ReasonCode?.Contains("unique-description", StringComparison.Ordinal) ?? false));
        Assert.True(await env.Audit.VerifyChainAsync());
    }

    [Fact]
    public async Task The_reviewers_inbox_lists_only_the_requested_patients_pending_reports()
    {
        var s = await Arrange();
        using var env = s.Env;
        var draft = await Created(s, Draft(s, ReportIssueType.AdverseEvent, ReportSeverity.Severe, "x"));
        await env.Reports.SubmitAsync(s.Patient, s.Patient, draft.Id, draft.Version, "t", null);
        Assert.Single(await env.Reports.PendingReviewAsync([s.Patient]));
        Assert.Empty(await env.Reports.PendingReviewAsync([env.UserId("demo-patient-2")]));
        Assert.Empty(await env.Reports.PendingReviewAsync([]));
    }
}
