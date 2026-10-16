using System.Reflection;
using System.Text.Json;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Pharmacies;
using MedSmarter.Modules.Pharmacies.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 10 at service level: dispensing-error / near-miss log and product-concern intake. Internal records only; nothing is sent anywhere.</summary>
public class Phase10Tests
{
    private static readonly Guid OrgA = Guid.Parse("383c0c5b-386f-5d57-a295-3b25374d9a2d");
    private static readonly Guid OrgB = Guid.Parse("578742b6-efd3-551a-9000-e250d815db67");

    private static CreateSafetyEventInput Error(string text = "Wrong strength handed over", ReachedPatient reached = ReachedPatient.Yes, SafetyEventKind kind = SafetyEventKind.DispensingError,
        IReadOnlyList<DispensingErrorExample>? ex = null, DateOnly? on = null, string? medicine = null, string? how = null, string? action = null, string? cid = null) =>
        new(kind, reached, ex, text, on, medicine, how, action, null, null, cid);

    private static CreateProductConcernInput Concern(string text = "Tablets look discoloured", ProductConcernType type = ProductConcernType.AppearanceOrPackaging, ProductRecordRef? record = null, ProductIdentifiers? typed = null, string? cid = null) =>
        new(type, text, null, record, typed, null, null, cid);

    private static (PEnv Env, IPharmacySafetyService Svc, Guid Pharmacist, Guid Admin) Setup()
    {
        var env = new PEnv();
        return (env, env.Get<IPharmacySafetyService>(), env.UserId("demo-pharmacist"), env.UserId("demo-pharmacy-admin"));
    }

    [Fact]
    public void The_dispensing_error_examples_are_exactly_the_eight_verified_FIP_examples_each_cited_and_not_called_official()
    {
        Assert.Equal(8, PharmacyDefinitions.Examples.Count);
        Assert.All(PharmacyDefinitions.Examples, x =>
        {
            Assert.Contains("Medication without harm", x.Document, StringComparison.Ordinal);
            Assert.Equal("§5.2.1 Common dispensing errors", x.Section);
            Assert.False(string.IsNullOrWhiteSpace(x.SourceWording));
        });
        Assert.Contains(PharmacyDefinitions.Examples, x => x.SourceWording == "Patients receiving an expired medicine" && x.Example == DispensingErrorExample.ExpiredMedicine);
        Assert.Contains(PharmacyDefinitions.Examples, x => x.SourceWording.Contains("someone else's prescription medicine", StringComparison.Ordinal));
        // the enum has those eight plus MedSmarter's own "other", which is not presented as FIP's
        Assert.Equal(9, Enum.GetValues<DispensingErrorExample>().Length);
        Assert.DoesNotContain(PharmacyDefinitions.Examples, x => x.Example == DispensingErrorExample.OtherNotListed);
        Assert.Contains("example", PharmacyNotices.Taxonomy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a complete or official list", PharmacyNotices.Taxonomy, StringComparison.Ordinal);
        Assert.Contains("intercepted and corrected", PharmacyDefinitions.NearMissDefinition, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_concern_types_are_labelled_provisional_and_no_category_claims_a_source_definition()
    {
        Assert.Contains("do not define", PharmacyNotices.ConcernTypes, StringComparison.Ordinal);
        Assert.Contains("does not show that a product is defective", PharmacyNotices.Concern, StringComparison.Ordinal);
        var names = typeof(IPharmacySafetyService).Assembly.GetTypes().SelectMany(t => t.GetProperties().Select(p => p.Name).Concat(t.IsEnum ? Enum.GetNames(t) : [])).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Serious", StringComparison.OrdinalIgnoreCase) || n.Contains("Causal", StringComparison.OrdinalIgnoreCase) || n.Contains("Confirmed", StringComparison.OrdinalIgnoreCase) || n.Contains("Defective", StringComparison.OrdinalIgnoreCase) || n.Contains("E2B", StringComparison.OrdinalIgnoreCase) || n.Contains("Deadline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_pharmacy_module_has_no_outbound_path_no_ProductTrace_integration_or_http_reference()
    {
        var refs = typeof(PharmacySafetyService).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(refs, n => n.Contains("ProductTrace", StringComparison.Ordinal) || n.Contains("Integrations", StringComparison.Ordinal) || n.Contains("System.Net.Http", StringComparison.Ordinal) || n.EndsWith(".AI", StringComparison.Ordinal) || n.Contains("Consent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_near_miss_is_kept_apart_from_an_error_that_reached_the_patient()
    {
        var (env, svc, pharmacist, _) = Setup();
        using var _env = env;
        Assert.Equal("near_miss.reached_patient_conflict", (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(kind: SafetyEventKind.NearMiss, reached: ReachedPatient.Yes), "t", null)).Detail);
        Assert.Equal("dispensing_error.not_reached_patient_use_near_miss", (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(reached: ReachedPatient.No), "t", null)).Detail);
        var near = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error("Strength noticed at final check", ReachedPatient.No, SafetyEventKind.NearMiss, how: "caught at the final check"), "t", null)).Value!.Event;
        var err = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(), "t", null)).Value!.Event;
        Assert.Equal(SafetyEventKind.NearMiss, near.Kind);
        Assert.Equal(ReachedPatient.No, near.ReachedPatient);
        Assert.Equal(SafetyEventKind.DispensingError, err.Kind);
        Assert.Equal(ReachedPatient.Yes, err.ReachedPatient);
        // unknown is allowed for either and stays unknown
        var unknown = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(reached: ReachedPatient.Unknown), "t", null)).Value!.Event;
        Assert.Contains("reached_patient.unknown", unknown.MissingInformation);
    }

    [Fact]
    public async Task Unknown_details_stay_unknown_and_nothing_is_guessed()
    {
        var (env, svc, pharmacist, _) = Setup();
        using var _env = env;
        var e = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(), "t", null)).Value!.Event;
        Assert.Null(e.OccurredOn);
        Assert.Null(e.MedicineAsReported);
        Assert.Empty(e.ErrorExamples);
        foreach (var code in new[] { "occurred_on.unknown", "error_examples.not_stated", "medicine.not_stated", "how_detected.not_stated", "immediate_action.not_stated" })
        {
            Assert.Contains(code, e.MissingInformation);
        }

        var full = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error("Wrong box", on: env.Today, ex: [DispensingErrorExample.ExpiredMedicine, DispensingErrorExample.ExpiredMedicine, DispensingErrorExample.OtherNotListed], medicine: "Demopril 5 mg", how: "the patient called", action: "replaced it"), "t", null)).Value!.Event;
        Assert.Equal(2, full.ErrorExamples.Count); // duplicates collapsed
        Assert.Equal(["Wrong box"], [full.Description]);
        Assert.DoesNotContain("occurred_on.unknown", full.MissingInformation);
        Assert.Equal("NotSubmitted", full.ExternalStatus);
        Assert.Equal("AwaitingClinicalReview", full.PersianWordingStatus);
        Assert.Contains("not a regulatory report", full.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validation_codes_and_idempotent_retries()
    {
        var (env, svc, pharmacist, _) = Setup();
        using var _env = env;
        async Task<string?> Code(CreateSafetyEventInput i) => (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, i, "t", null)).Detail;
        Assert.Contains("description.required", await Code(Error(" ")) ?? "");
        Assert.Contains("description.too_long", await Code(Error(new string('x', 2001))) ?? "");
        Assert.Contains("description.invalid_characters", await Code(Error("bad\u0001text")) ?? "");
        Assert.Contains("occurred.future", await Code(Error(on: env.Today.AddDays(2))) ?? "");
        Assert.Equal("link.concern_unavailable", (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error() with { LinkedProductConcernId = Guid.NewGuid() }, "t", null)).Detail);
        var a = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(cid: "r-1"), "t", null)).Value!.Event;
        var b = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(cid: "r-1"), "t", null)).Value!.Event;
        Assert.Equal(a.Id, b.Id);
        Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.PharmacySafetyEventCreated)));
        Assert.Single((await svc.ListAsync(OrgA, SafetyRecordKind.SafetyEvent, null, 50)).Value!);
    }

    [Fact]
    public async Task Review_keeps_the_original_adds_entries_and_closing_or_reopening_needs_a_reason()
    {
        var (env, svc, pharmacist, admin) = Setup();
        using var _env = env;
        var e = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error(), "t", null)).Value!.Event;
        var original = (e.Description, e.Kind, e.ReachedPatient, e.ReportedAt);
        Assert.Equal(PharmacyError.Validation, (await svc.AddEntryAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyEntryKind.StatusChange, "x", "t", null)).Error);
        Assert.True((await svc.AddEntryAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyEntryKind.ReviewNote, "Reviewed the shelf labels", "t", null)).Succeeded);
        Assert.Equal(PharmacyError.Conflict, (await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyStatus.FollowUpRequired, null, 99, "t", null)).Error); // stale
        var v = (await svc.GetEventAsync(e.Id)).Value!.Event.Version;
        Assert.Equal(PharmacyError.Conflict, (await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyStatus.FollowUpRequired, null, v, "t", null)).Error); // Submitted -> FollowUp is not a move
        Assert.True((await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyStatus.InReview, null, v, "t", null)).Succeeded);
        v = (await svc.GetEventAsync(e.Id)).Value!.Event.Version;
        Assert.Equal("note.required", (await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyStatus.Closed, " ", v, "t", null)).Detail);
        Assert.True((await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyStatus.Closed, "Labels re-checked; shelf moved", v, "t", null)).Succeeded);
        v = (await svc.GetEventAsync(e.Id)).Value!.Event.Version;
        Assert.Equal("note.required", (await svc.SetStatusAsync(pharmacist, PharmacyActorRole.Pharmacist, SafetyRecordKind.SafetyEvent, e.Id, SafetyStatus.InReview, null, v, "t", null)).Detail);
        var after = (await svc.GetEventAsync(e.Id)).Value!;
        Assert.Equal(original, (after.Event.Description, after.Event.Kind, after.Event.ReachedPatient, after.Event.ReportedAt));
        Assert.Equal([SafetyEntryKind.ReviewNote, SafetyEntryKind.StatusChange, SafetyEntryKind.StatusChange], after.Entries.Select(x => x.Kind).ToArray());
        Assert.Equal(SafetyStatus.Closed, after.Entries[^1].ToStatus);
    }

    [Fact]
    public async Task An_unmatched_product_concern_keeps_exactly_what_was_typed_and_flags_what_is_missing()
    {
        var (env, svc, pharmacist, _) = Setup();
        using var _env = env;
        var empty = (await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern(), null, "t", null)).Value!.Concern;
        Assert.Equal(ProductLinkState.Unmatched, empty.LinkState);
        Assert.Null(empty.Linked);
        Assert.Null(empty.AsReported);
        foreach (var f in new[] { "product_record.not_linked", "product.name_missing", "batch.missing", "expiry.missing", "manufacturer.missing" })
        {
            Assert.Contains(f, empty.Flags);
        }

        Assert.Equal("ProvisionalNotSourceDefined", empty.TypeBasis);
        Assert.Contains("does not show that a product is defective", empty.Notice, StringComparison.Ordinal);
        Assert.False(empty.ManufacturerReportProposed);
        var typed = (await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern(typed: new ProductIdentifiers("Demopril", "LOT-X", null, null, null)), null, "t", null)).Value!.Concern;
        Assert.Equal("LOT-X", typed.AsReported!.BatchNumber);
        Assert.DoesNotContain("batch.missing", typed.Flags);
        Assert.Contains("expiry.missing", typed.Flags);
        // a link that was asked for but could not be verified is refused; no other record is substituted
        var asked = await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern(record: new ProductRecordRef(Guid.NewGuid(), Guid.NewGuid())), null, "t", null);
        Assert.Contains("product_record.unavailable", asked.Detail ?? "");
        Assert.Equal(2, (await svc.ListAsync(OrgA, SafetyRecordKind.ProductConcern, null, 50)).Value!.Count);
    }

    [Fact]
    public async Task A_linked_concern_copies_identifiers_and_shows_when_the_typed_ones_differ_without_choosing()
    {
        var (env, svc, pharmacist, _) = Setup();
        using var _env = env;
        var sara = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        var rec = (await env.Products.RecordAsync(sara, ProductRecorder.Patient, sara, new ProductRecordInput(null, med, null, null, null, null, "LOT-2026-A1", env.Today.AddMonths(-2), env.Today.AddMonths(12), null, null, env.Today.AddDays(-3)), "t", null)).Value!;
        var snap = new ProductSnapshot(sara, rec.Id, new ProductIdentifiers(rec.ProductName, rec.BatchNumber, rec.ExpiryDate, rec.ManufacturerName, rec.Gtin), rec.Version, rec.Verification.ToString());
        var linked = (await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern(record: new ProductRecordRef(sara, rec.Id), typed: new ProductIdentifiers(null, "LOT-OTHER", null, null, null)), snap, "t", null)).Value!.Concern;
        Assert.Equal(ProductLinkState.LinkedToProductRecord, linked.LinkState);
        Assert.Equal("LOT-2026-A1", linked.Linked!.BatchNumber);
        Assert.Equal("LOT-OTHER", linked.AsReported!.BatchNumber); // both kept
        Assert.Contains("batch.differs_from_linked_record", linked.Flags);
        Assert.Equal(rec.Version, linked.LinkedRecordVersion);
        // the response never carries the patient's id
        Assert.DoesNotContain(sara.ToString(), JsonSerializer.Serialize(linked), StringComparison.OrdinalIgnoreCase);
        // a later change of the patient's record does not rewrite the snapshot
        await env.Products.UpdateAsync(sara, ProductRecorder.Patient, sara, rec.Id, new ProductRecordInput(null, med, null, null, null, null, "LOT-CHANGED", env.Today.AddMonths(-2), env.Today.AddMonths(12), null, null, env.Today.AddDays(-3), ExpectedVersion: rec.Version), "typo", "t", null);
        Assert.Equal("LOT-2026-A1", (await svc.GetConcernAsync(linked.Id)).Value!.Concern.Linked!.BatchNumber);
    }

    [Fact]
    public async Task A_manufacturer_report_proposal_is_a_human_note_that_creates_and_sends_nothing()
    {
        var (env, svc, pharmacist, admin) = Setup();
        using var _env = env;
        var c = (await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern(), null, "t", null)).Value!.Concern;
        Assert.Equal("proposal.needs_review", (await svc.ProposeManufacturerReportAsync(admin, PharmacyActorRole.PharmacyAdmin, c.Id, "worth reporting", c.Version, "t", null)).Detail); // not on a first report
        Assert.True((await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.ProductConcern, c.Id, SafetyStatus.InReview, null, c.Version, "t", null)).Succeeded);
        var v = (await svc.GetConcernAsync(c.Id)).Value!.Concern.Version;
        Assert.Equal(PharmacyError.Validation, (await svc.ProposeManufacturerReportAsync(admin, PharmacyActorRole.PharmacyAdmin, c.Id, " ", v, "t", null)).Error);
        Assert.True((await svc.ProposeManufacturerReportAsync(admin, PharmacyActorRole.PharmacyAdmin, c.Id, "Appearance differs across two patients; worth a report if the patients agree", v, "t", null)).Succeeded);
        var after = (await svc.GetConcernAsync(c.Id)).Value!;
        Assert.True(after.Concern.ManufacturerReportProposed);
        Assert.Equal("NotSubmitted", after.Concern.ExternalStatus);
        Assert.Contains(after.Entries, x => x.Kind == SafetyEntryKind.ManufacturerReportProposal);
        Assert.Equal("proposal.already_recorded", (await svc.ProposeManufacturerReportAsync(admin, PharmacyActorRole.PharmacyAdmin, c.Id, "again", after.Concern.Version, "t", null)).Detail);
        // nothing was queued in the existing mock manufacturer-report outbox
        Assert.All((await env.Outbox.GetQueueAsync()).OutboxByState.Values, n => Assert.Equal(0, n));
    }

    [Fact]
    public async Task Audit_holds_ids_and_names_only_never_the_report_text()
    {
        var (env, svc, pharmacist, admin) = Setup();
        using var _env = env;
        var e = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error("Zzzsecretdescription", medicine: "Zzzsecretmed", how: "Zzzsecrethow"), "t", "corr")).Value!.Event;
        await svc.AddEntryAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.SafetyEvent, e.Id, SafetyEntryKind.ReviewNote, "Zzzsecretnote", "t", null);
        var c = (await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern("Zzzsecretconcern", typed: new ProductIdentifiers("Zzzsecretproduct", "ZZZ-LOT", null, null, null)), null, "t", null)).Value!.Concern;
        await svc.SetStatusAsync(admin, PharmacyActorRole.PharmacyAdmin, SafetyRecordKind.ProductConcern, c.Id, SafetyStatus.InReview, "Zzzsecretreason", c.Version, "t", null);
        var all = JsonSerializer.Serialize(await env.Audit.QueryAsync(new AuditQuery(Take: 1000)));
        Assert.DoesNotContain("Zzzsecret", all, StringComparison.Ordinal);
        Assert.DoesNotContain("ZZZ-LOT", all, StringComparison.Ordinal);
        foreach (var a in new[] { AuditActions.PharmacySafetyEventCreated, AuditActions.PharmacySafetyEntryAdded, AuditActions.ProductConcernCreated, AuditActions.PharmacySafetyStatusChanged })
        {
            Assert.NotEmpty(await env.Audit.QueryAsync(new AuditQuery(Action: a)));
        }
    }

    [Fact]
    public async Task Lists_are_scoped_to_one_organization()
    {
        var (env, svc, pharmacist, _) = Setup();
        using var _env = env;
        await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error("A record"), "t", null);
        await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgB, Error("B record"), "t", null);
        Assert.Equal(["A record"], (await svc.ListAsync(OrgA, SafetyRecordKind.SafetyEvent, null, 50)).Value!.Select(x => x.Excerpt).ToArray());
        Assert.Equal(["B record"], (await svc.ListAsync(OrgB, SafetyRecordKind.SafetyEvent, null, 50)).Value!.Select(x => x.Excerpt).ToArray());
        Assert.Null(await svc.OrganizationOfAsync(SafetyRecordKind.ProductConcern, Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task On_PostgreSQL_records_and_history_survive_a_restart()
    {
        using var first = new PEnv();
        var svc = first.Get<IPharmacySafetyService>();
        var pharmacist = first.UserId("demo-pharmacist");
        var e = (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error("Persisted", cid: "pg-1", ex: [DispensingErrorExample.IncorrectQuantity]), "t", null)).Value!.Event;
        Assert.Equal(e.Id, (await svc.CreateEventAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Error("Persisted", cid: "pg-1"), "t", null)).Value!.Event.Id);
        await svc.AddEntryAsync(pharmacist, PharmacyActorRole.Pharmacist, SafetyRecordKind.SafetyEvent, e.Id, SafetyEntryKind.FollowUp, "kept", "t", null);
        var c = (await svc.CreateConcernAsync(pharmacist, PharmacyActorRole.Pharmacist, OrgA, Concern(typed: new ProductIdentifiers("P", "B", null, null, null), cid: "pg-2"), null, "t", null)).Value!.Concern;
        using var second = first.Restart();
        var s2 = second.Get<IPharmacySafetyService>();
        var back = (await s2.GetEventAsync(e.Id)).Value!;
        Assert.Equal([DispensingErrorExample.IncorrectQuantity], back.Event.ErrorExamples.ToArray());
        Assert.Single(back.Entries);
        Assert.Equal("B", (await s2.GetConcernAsync(c.Id)).Value!.Concern.AsReported!.BatchNumber);
        Assert.Equal(OrgA, await s2.OrganizationOfAsync(SafetyRecordKind.SafetyEvent, e.Id));
    }
}
