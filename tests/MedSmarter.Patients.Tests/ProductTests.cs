using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Patients.Tests;

public class ProductTests
{
    private static ProductRecordInput Input(PEnv env, Guid? medicationId, string batch = "LOT-2026-A1", int expiryMonths = 12) =>
        new(null, medicationId, medicationId is null ? "Typed product (fictional)" : null, null, null, null, batch, env.Today.AddMonths(-2), env.Today.AddMonths(expiryMonths), null, "Corner pharmacy (placeholder)", env.Today.AddDays(-3));

    [Fact]
    public async Task A_batch_record_links_the_medicine_and_takes_its_manufacturer_from_the_reference()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var r = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, await env.Reference_("demopril")), "t", null);
        Assert.True(r.Succeeded, r.Detail);
        var p = r.Value!;
        Assert.Equal("Demopril", p.ProductName);
        Assert.Equal("DemoPharma A (fictional)", p.ManufacturerName);
        Assert.NotNull(p.ManufacturerId);
        Assert.Equal(ProductConsistency.Consistent, p.Consistency);
        Assert.Equal(ProductVerification.SelfReported, p.Verification);
        Assert.Equal(EntryMethod.Manual, p.Method);
        Assert.False(p.Expired);
        Assert.Null(p.Gtin); // nothing is invented
        Assert.True(p.IsDemo); // fictional reference medicine => the record is labelled demo
        Assert.Equal("DEMO DATA - NOT FOR CLINICAL USE", p.Notice);
    }

    [Fact]
    public async Task A_product_that_is_not_in_the_reference_is_recorded_as_such()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var p = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, null), "t", null)).Value!;
        Assert.Equal(ProductConsistency.ReferenceUnknown, p.Consistency); // nothing to compare with: never "consistent" by default
        Assert.Null(p.MedicationId);
        Assert.False(p.IsDemo);
    }

    [Theory]
    [InlineData("", "batch.required")]
    [InlineData("  ", "batch.required")]
    [InlineData("LOT#1", "batch.invalid")]
    [InlineData("drop table;", "batch.invalid")]
    public async Task Batch_numbers_have_a_checked_format(string batch, string code)
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var r = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, null, batch), "t", null);
        Assert.Equal(TraceError.Validation, r.Error);
        Assert.Contains(code, r.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dates_must_be_possible_and_an_expired_package_is_flagged_not_hidden()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("nocturin");
        var bad = Input(env, med) with { ManufactureDate = env.Today.AddMonths(6), ExpiryDate = env.Today.AddMonths(3) };
        Assert.Contains("manufacture_date.invalid", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, bad, "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("received_on.range", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { ReceivedOn = env.Today.AddDays(2) }, "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("expiry.range", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { ManufactureDate = null, ExpiryDate = new DateOnly(1980, 1, 1) }, "t", null)).Detail, StringComparison.Ordinal);

        var expired = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "OLD-1", -1) with { ManufactureDate = env.Today.AddYears(-2) }, "t", null)).Value!;
        Assert.True(expired.Expired);
        Assert.Contains("expiry.expired", expired.Findings);
    }

    [Fact]
    public async Task A_gtin_is_optional_checksum_verified_and_compared_with_the_reference()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("nocturin");
        var good = PEnv.Gtin13("999000000001");
        var withGtin = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "G-1") with { Gtin = good }, "t", null);
        Assert.True(withGtin.Succeeded, withGtin.Detail);
        Assert.Equal(good, withGtin.Value!.Gtin);
        var wrong = good[..12] + (char)('0' + (((good[12] - '0') + 3) % 10));
        Assert.Contains("gtin.invalid", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "G-2") with { Gtin = wrong }, "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("gtin.invalid", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "G-3") with { Gtin = "12345" }, "t", null)).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_manufacturer_must_fit_the_medicine()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        var other = (await env.Get<IMedicationAdminService>().CreateManufacturerAsync(id, new NewManufacturer(new LocalizedText("Other Maker (fictional)", null), null, null))).Value!;
        var mismatch = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { ManufacturerId = other.Id }, "t", null);
        Assert.Contains("manufacturer.incompatible", mismatch.Detail, StringComparison.Ordinal);
        Assert.Contains("manufacturer.unknown", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { ManufacturerId = Guid.NewGuid() }, "t", null)).Detail, StringComparison.Ordinal);
        var text = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "TXT-1") with { ManufacturerName = "Some Other Company" }, "t", null);
        Assert.True(text.Succeeded); // a typed name that differs is allowed but flagged
        Assert.Equal(ProductConsistency.Mismatch, text.Value!.Consistency);
        Assert.Contains("manufacturer.differs_from_reference", text.Value.Findings);
        var consistent = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "TXT-2") with { ManufacturerName = "demopharma a (fictional)" }, "t", null);
        Assert.Equal(ProductConsistency.Consistent, consistent.Value!.Consistency);
    }

    [Fact]
    public async Task A_package_is_attached_only_to_the_medicine_the_patient_takes()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var taken = await env.TakeMedication(id, "demopril");
        var nocturin = await env.Reference_("nocturin");
        Assert.Contains("patient_medication.mismatch", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, nocturin) with { PatientMedicationId = taken.Id }, "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("patient_medication.not_found", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, null) with { PatientMedicationId = Guid.NewGuid() }, "t", null)).Detail, StringComparison.Ordinal);
        var linked = await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, null) with { PatientMedicationId = taken.Id }, "t", null);
        Assert.True(linked.Succeeded, linked.Detail);
        Assert.Equal(taken.MedicationId, linked.Value!.MedicationId); // defaults to the linked medicine
    }

    [Fact]
    public async Task Either_a_reference_medicine_or_a_typed_name_and_no_duplicates()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        Assert.Contains("product.required", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, null) with { ProductName = null }, "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("product.ambiguous", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { ProductName = "x" }, "t", null)).Detail, StringComparison.Ordinal);
        Assert.True((await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med), "t", null)).Succeeded);
        Assert.Equal(TraceError.Conflict, (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { BatchNumber = "lot-2026-a1" }, "t", null)).Error); // batch numbers compare case-insensitively
        Assert.True((await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med, "LOT-OTHER"), "t", null)).Succeeded);
    }

    [Fact]
    public async Task Scanning_and_external_systems_are_connection_points_not_features()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        Assert.Contains("entry_method.not_available", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { Method = EntryMethod.BarcodeScan }, "t", null)).Detail, StringComparison.Ordinal);
        Assert.Contains("entry_method.not_available", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { Method = EntryMethod.ExternalSystem }, "t", null)).Detail, StringComparison.Ordinal);
        var scan = env.Get<IProductScanProvider>();
        Assert.False(scan.IsAvailable);
        Assert.Null(await scan.ParseAsync("0123456789"));
    }

    [Fact]
    public async Task A_professional_can_record_and_confirm_but_a_patient_cannot_confirm_their_own_entry()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        var mine = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med), "t", null)).Value!;
        Assert.Equal(TraceError.Forbidden, (await env.Products.ConfirmAsync(id, ProductRecorder.Patient, id, mine.Id, mine.Version, "t", null)).Error);
        var pharmacist = env.UserId("demo-pharmacist");
        var confirmed = await env.Products.ConfirmAsync(pharmacist, ProductRecorder.Pharmacist, id, mine.Id, mine.Version, "t", null);
        Assert.Equal(ProductVerification.ProfessionalConfirmed, confirmed.Value!.Verification);
        Assert.Equal(2, confirmed.Value.Version);
        var byPharmacist = (await env.Products.RecordAsync(pharmacist, ProductRecorder.Pharmacist, id, Input(env, med, "PH-1"), "t", null)).Value!;
        Assert.Equal(ProductRecorder.Pharmacist, byPharmacist.RecordedBy);
        Assert.Equal(ProductVerification.ProfessionalConfirmed, byPharmacist.Verification);
        var versions = (await env.Products.VersionsAsync(id, mine.Id)).Value!;
        Assert.Equal(2, versions.Count);
        Assert.Equal(pharmacist, versions[1].ChangedBy);
    }

    [Fact]
    public async Task Updates_need_a_reason_and_the_current_version_and_removed_records_stay_in_history()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        var rec = (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med), "t", null)).Value!;
        var edit = Input(env, med) with { BatchNumber = "LOT-2026-A2", ExpectedVersion = rec.Version };
        Assert.Contains("reason.required", (await env.Products.UpdateAsync(id, ProductRecorder.Patient, id, rec.Id, edit, "", "t", null)).Detail, StringComparison.Ordinal);
        var updated = await env.Products.UpdateAsync(id, ProductRecorder.Patient, id, rec.Id, edit, "typo on the label", "t", null);
        Assert.Equal("LOT-2026-A2", updated.Value!.BatchNumber);
        Assert.Equal(TraceError.Conflict, (await env.Products.UpdateAsync(id, ProductRecorder.Patient, id, rec.Id, edit, "stale", "t", null)).Error);
        Assert.True((await env.Products.RemoveAsync(id, id, rec.Id, "t", null)).Succeeded);
        Assert.Empty((await env.Products.ListAsync(id)).Value!);
        Assert.Equal(TraceError.NotFound, (await env.Products.GetAsync(id, rec.Id)).Error);
        Assert.Equal(3, (await env.Products.VersionsAsync(id, rec.Id)).Value!.Count); // created, edited, removed
    }

    [Fact]
    public async Task Another_patients_record_is_unreachable()
    {
        using var env = new PEnv();
        var a = await env.Patient("demo-patient");
        var b = await env.Patient("demo-patient-2");
        var med = await env.Reference_("demopril");
        var rec = (await env.Products.RecordAsync(a, ProductRecorder.Patient, a, Input(env, med), "t", null)).Value!;
        Assert.Equal(TraceError.NotFound, (await env.Products.GetAsync(b, rec.Id)).Error);
        Assert.Equal(TraceError.NotFound, (await env.Products.RemoveAsync(b, b, rec.Id, "t", null)).Error);
        Assert.Equal(TraceError.NotFound, (await env.Products.UpdateAsync(b, ProductRecorder.Patient, b, rec.Id, Input(env, med) with { ExpectedVersion = 1 }, "x", "t", null)).Error);
        Assert.Equal(TraceError.NotFound, (await env.Products.VersionsAsync(b, rec.Id)).Error);
        Assert.Empty((await env.Products.ListAsync(b)).Value!);
    }

    [Fact]
    public async Task Free_text_fields_are_screened_and_changes_are_audited_without_the_batch_text()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        var med = await env.Reference_("demopril");
        Assert.Contains("looks_identifying", (await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med) with { PharmacyNote = "Sara, 09121234567" }, "t", null)).Detail, StringComparison.Ordinal);
        await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, med), "web", "corr-1");
        var entries = await env.Audit.QueryAsync(new AuditQuery(SubjectUserId: id, Take: 50));
        Assert.Contains(entries, e => e.Action == AuditActions.ProductRecorded);
        Assert.DoesNotContain(entries, e => e.Metadata.Values.Any(v => v.Contains("LOT-2026", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Touching_products_updates_the_freshness_of_the_category()
    {
        using var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.Products.RecordAsync(id, ProductRecorder.Patient, id, Input(env, await env.Reference_("demopril")), "t", null);
        var f = (await env.Patients.GetFreshnessAsync(id)).Value!.Single(x => x.Category == PatientDataCategory.Products);
        Assert.False(f.NeverRecorded);
        Assert.Equal(1, f.RecordCount);
    }
}
