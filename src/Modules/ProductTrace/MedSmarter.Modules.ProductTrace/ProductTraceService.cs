using System.Text.RegularExpressions;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.ProductTrace;

public sealed partial class ProductTraceService(
    IDbContextFactory<ProductTraceDbContext> factory, IClock clock, IAuditWriter audit, IMedicationService reference, IPatientDirectory patients) : IProductTraceService
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_/. ]{0,39}$")]
    private static partial Regex BatchFormat();

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public async Task<TraceOutcome<IReadOnlyList<ProductRecordDto>>> ListAsync(Guid subjectId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<IReadOnlyList<ProductRecordDto>>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Products.AsNoTracking().Where(x => x.PatientId == patient.PatientId && x.DeletedAt == null).OrderByDescending(x => x.ReceivedOn).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
        return TraceOutcome.Ok<IReadOnlyList<ProductRecordDto>>(await ToDtosAsync(rows, subjectId, ct));
    }

    public async Task<TraceOutcome<ProductRecordDto>> GetAsync(Guid subjectId, Guid id, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient.PatientId && x.DeletedAt == null, ct);
        return row is null ? TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "product.not_found") : TraceOutcome.Ok((await ToDtosAsync([row], subjectId, ct))[0]);
    }

    public async Task<TraceOutcome<ProductRecordDto>> RecordAsync(Guid actorUserId, ProductRecorder recorder, Guid subjectId, ProductRecordInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "patient.not_found");
        }

        if (patient.Status != PatientStatus.Active)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Forbidden, "patient.inactive");
        }

        var checkedInput = await CheckAsync(subjectId, input, ct);
        if (checkedInput.Errors.Count > 0)
        {
            return Support.Invalid<ProductRecordDto>(checkedInput.Errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        var row = new ProductRecordRow { Id = Guid.CreateVersion7(), PatientId = patient.PatientId, CreatedAt = now, Version = 1 };
        Apply(row, checkedInput, input, recorder, now, actorUserId);
        if (await IsDuplicateAsync(db, row, ct))
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "product.duplicate");
        }

        db.Products.Add(row);
        db.ProductVersions.Add(Version(row, now, actorUserId, "created"));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "product.duplicate");
        }

        await patients.TouchCategoryAsync(subjectId, PatientDataCategory.Products, actorUserId, await db.Products.CountAsync(x => x.PatientId == patient.PatientId && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.ProductRecorded, AuditResult.Success, actorUserId, "product-record", row.Id, subjectId, source, correlationId, recorder.ToString());
        return TraceOutcome.Ok((await ToDtosAsync([row], subjectId, ct))[0]);
    }

    public async Task<TraceOutcome<ProductRecordDto>> UpdateAsync(Guid actorUserId, ProductRecorder recorder, Guid subjectId, Guid id, ProductRecordInput input, string reason, string source, string? correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 300 || FreeTextGuard.Problem(reason, "reason", 300) is not null)
        {
            return Support.Invalid<ProductRecordDto>(["reason.required"]);
        }

        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "patient.not_found");
        }

        if (patient.Status != PatientStatus.Active)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Forbidden, "patient.inactive");
        }

        var checkedInput = await CheckAsync(subjectId, input, ct);
        if (checkedInput.Errors.Count > 0)
        {
            return Support.Invalid<ProductRecordDto>(checkedInput.Errors);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient.PatientId && x.DeletedAt == null, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "product.not_found");
        }

        if (input.ExpectedVersion != row.Version)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "version.mismatch");
        }

        var now = clock.UtcNow;
        Apply(row, checkedInput, input, recorder, now, actorUserId);
        row.Version++;
        if (await IsDuplicateAsync(db, row, ct))
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "product.duplicate");
        }

        db.ProductVersions.Add(Version(row, now, actorUserId, FreeTextGuard.Clean(reason)!));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "version.mismatch");
        }

        await patients.TouchCategoryAsync(subjectId, PatientDataCategory.Products, actorUserId, await db.Products.CountAsync(x => x.PatientId == patient.PatientId && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.ProductUpdated, AuditResult.Success, actorUserId, "product-record", row.Id, subjectId, source, correlationId, recorder.ToString());
        return TraceOutcome.Ok((await ToDtosAsync([row], subjectId, ct))[0]);
    }

    public async Task<TraceOutcome<ProductRecordDto>> ConfirmAsync(Guid actorUserId, ProductRecorder recorder, Guid subjectId, Guid id, int expectedVersion, string source, string? correlationId, CancellationToken ct = default)
    {
        if (recorder == ProductRecorder.Patient)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Forbidden, "confirm.professional_only"); // a patient cannot confirm their own entry
        }

        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient.PatientId && x.DeletedAt == null, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.NotFound, "product.not_found");
        }

        if (expectedVersion != row.Version)
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "version.mismatch");
        }

        var now = clock.UtcNow;
        row.Verification = ProductVerification.ProfessionalConfirmed;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        db.ProductVersions.Add(Version(row, now, actorUserId, "confirmed against the package"));
        if (!await TrySaveAsync(db, ct))
        {
            return TraceOutcome.Fail<ProductRecordDto>(TraceError.Conflict, "version.mismatch");
        }

        await Support.AuditAsync(audit, AuditActions.ProductUpdated, AuditResult.Success, actorUserId, "product-record", row.Id, subjectId, source, correlationId, "confirmed");
        return TraceOutcome.Ok((await ToDtosAsync([row], subjectId, ct))[0]);
    }

    public async Task<TraceOutcome<bool>> RemoveAsync(Guid actorUserId, Guid subjectId, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return TraceOutcome.Fail<bool>(TraceError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.PatientId == patient.PatientId && x.DeletedAt == null, ct);
        if (row is null)
        {
            return TraceOutcome.Fail<bool>(TraceError.NotFound, "product.not_found");
        }

        // A record that a report refers to is kept (the report must stay explainable); only unreferenced records can be removed.
        if (await db.Reports.AnyAsync(r => r.ProductRecordId == id && r.Status != ReportStatus.Cancelled, ct))
        {
            return TraceOutcome.Fail<bool>(TraceError.Conflict, "product.has_reports");
        }

        var now = clock.UtcNow;
        row.DeletedAt = now;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        row.Version++;
        db.ProductVersions.Add(Version(row, now, actorUserId, "removed"));
        await db.SaveChangesAsync(ct);
        await patients.TouchCategoryAsync(subjectId, PatientDataCategory.Products, actorUserId, await db.Products.CountAsync(x => x.PatientId == patient.PatientId && x.DeletedAt == null, ct), ct);
        await Support.AuditAsync(audit, AuditActions.ProductRemoved, AuditResult.Success, actorUserId, "product-record", row.Id, subjectId, source, correlationId);
        return TraceOutcome.Ok(true);
    }

    public async Task<TraceOutcome<IReadOnlyList<RecordVersionInfo>>> VersionsAsync(Guid subjectId, Guid id, CancellationToken ct = default)
    {
        var patient = await patients.FindBySubjectAsync(subjectId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (patient is null || !await db.Products.AnyAsync(x => x.Id == id && x.PatientId == patient.PatientId, ct))
        {
            return TraceOutcome.Fail<IReadOnlyList<RecordVersionInfo>>(TraceError.NotFound, "product.not_found");
        }

        var rows = await db.ProductVersions.AsNoTracking().Where(x => x.ProductRecordId == id).OrderBy(x => x.VersionNumber).ToListAsync(ct);
        return TraceOutcome.Ok<IReadOnlyList<RecordVersionInfo>>([.. rows.Select(r => new RecordVersionInfo(r.VersionNumber, r.ChangedAt, r.ChangedBy, r.Reason))]);
    }

    // ---------- validation ----------

    private sealed record Checked(List<string> Errors, MedicationDetailDto? Reference, PatientMedicationDto? PatientMedication, string ProductName, Guid? ManufacturerId, string? ManufacturerName, List<string> Findings, ProductConsistency Consistency);

    private async Task<Checked> CheckAsync(Guid subjectId, ProductRecordInput i, CancellationToken ct)
    {
        var errors = new List<string>();
        var findings = new List<string>();
        var today = Today;

        if (i.Method != EntryMethod.Manual)
        {
            errors.Add("entry_method.not_available"); // scanning and external systems are connection points only
        }

        var batch = (i.BatchNumber ?? string.Empty).Trim();
        if (batch.Length == 0)
        {
            errors.Add("batch.required");
        }
        else if (!BatchFormat().IsMatch(batch))
        {
            errors.Add("batch.invalid");
        }

        if (i.ExpiryDate.Year < 1990 || i.ExpiryDate > today.AddYears(30))
        {
            errors.Add("expiry.range");
        }

        if (i.ManufactureDate is { } mfd && (mfd >= i.ExpiryDate || mfd > today || mfd.Year < 1950))
        {
            errors.Add("manufacture_date.invalid");
        }

        if (i.ReceivedOn > today || i.ReceivedOn < today.AddYears(-30))
        {
            errors.Add("received_on.range");
        }

        if (i.Gtin is { } gtin)
        {
            if (!Support.IsValidGtin(gtin.Trim()))
            {
                errors.Add("gtin.invalid");
            }
        }

        foreach (var (text, field, max) in new[] { (i.ProductName, "product_name", 200), (i.GenericName, "generic_name", 200), (i.ManufacturerName, "manufacturer_name", 200), (i.PharmacyNote, "pharmacy_note", 200) })
        {
            if (FreeTextGuard.Problem(text, field, max) is { } problem)
            {
                errors.Add(problem);
            }
        }

        if ((i.MedicationId is null) == string.IsNullOrWhiteSpace(i.ProductName))
        {
            errors.Add(i.MedicationId is null ? "product.required" : "product.ambiguous"); // either a reference medication or a typed name, never both and never neither
        }

        MedicationDetailDto? med = null;
        if (i.MedicationId is { } mid)
        {
            var found = await reference.GetAsync(mid, false, ct);
            if (!found.Succeeded)
            {
                errors.Add("medication.unknown");
            }
            else
            {
                med = found.Value;
            }
        }

        PatientMedicationDto? takenMed = null;
        if (i.PatientMedicationId is { } pmid)
        {
            takenMed = (await patients.ActiveMedicationsAsync(subjectId, ct)).FirstOrDefault(m => m.Id == pmid);
            if (takenMed is null)
            {
                errors.Add("patient_medication.not_found");
            }
            else if (med is not null && takenMed.MedicationId != med.Id)
            {
                errors.Add("patient_medication.mismatch"); // the package must be of the medicine it is attached to
            }
            else if (med is null && i.MedicationId is null && takenMed.MedicationId is { } linked)
            {
                med = (await reference.GetAsync(linked, true, ct)).Value; // default to the linked medicine
            }
        }

        // Manufacturer: an id must exist; if the medication names its manufacturer the two must agree (id mismatch is an error, name mismatch a finding).
        Guid? manufacturerId = i.ManufacturerId;
        string? manufacturerName = FreeTextGuard.Clean(i.ManufacturerName);
        if (manufacturerId is { } mf)
        {
            var m = await reference.GetManufacturerAsync(mf, ct);
            if (!m.Succeeded)
            {
                errors.Add("manufacturer.unknown");
            }
            else
            {
                manufacturerName = m.Value!.Name.En ?? m.Value.Name.Fa;
            }
        }

        var consistency = ProductConsistency.ReferenceUnknown;
        if (med is not null)
        {
            consistency = ProductConsistency.Consistent;
            if (med.Manufacturer is { } refMf)
            {
                if (manufacturerId is null && manufacturerName is null)
                {
                    manufacturerId = refMf.Id;
                    manufacturerName = refMf.Name.En ?? refMf.Name.Fa;
                }
                else if (manufacturerId is { } given && given != refMf.Id)
                {
                    errors.Add("manufacturer.incompatible");
                }
                else if (manufacturerId is null && manufacturerName is not null && !SameName(manufacturerName, refMf.Name))
                {
                    findings.Add("manufacturer.differs_from_reference");
                    consistency = ProductConsistency.Mismatch;
                }
            }

            var referenceGtin = med.Identifiers.FirstOrDefault(x => x.Scheme == IdentifierScheme.Gtin)?.Value;
            if (i.Gtin is { } entered && referenceGtin is not null && !string.Equals(entered.Trim(), referenceGtin, StringComparison.Ordinal))
            {
                findings.Add("gtin.differs_from_reference");
                consistency = ProductConsistency.Mismatch;
            }
        }

        if (i.ExpiryDate < today)
        {
            findings.Add("expiry.expired");
        }

        if (i.ReceivedOn > i.ExpiryDate)
        {
            findings.Add("received_after_expiry");
        }

        var name = med is not null ? med.Name.En ?? med.Name.Fa ?? string.Empty : FreeTextGuard.Clean(i.ProductName) ?? string.Empty;
        return new Checked(errors, med, takenMed, name, manufacturerId, manufacturerName, findings, consistency);
    }

    private static bool SameName(string text, LocalizedText name) =>
        string.Equals(text, name.En, StringComparison.OrdinalIgnoreCase) || string.Equals(text, name.Fa, StringComparison.OrdinalIgnoreCase);

    private static void Apply(ProductRecordRow row, Checked c, ProductRecordInput i, ProductRecorder recorder, DateTimeOffset now, Guid actor)
    {
        row.PatientMedicationId = i.PatientMedicationId;
        row.MedicationId = c.Reference?.Id ?? i.MedicationId;
        row.ProductName = c.ProductName;
        row.GenericName = FreeTextGuard.Clean(i.GenericName);
        row.ManufacturerId = c.ManufacturerId;
        row.ManufacturerName = c.ManufacturerName;
        row.BatchNumber = i.BatchNumber.Trim();
        row.BatchNormalized = i.BatchNumber.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        row.ProductKey = row.MedicationId is { } m ? m.ToString("N") : c.ProductName.ToLowerInvariant();
        row.ManufactureDate = i.ManufactureDate;
        row.ExpiryDate = i.ExpiryDate;
        row.Gtin = i.Gtin?.Trim();
        row.PharmacyNote = FreeTextGuard.Clean(i.PharmacyNote);
        row.ReceivedOn = i.ReceivedOn;
        row.Method = EntryMethod.Manual;
        row.Verification = recorder == ProductRecorder.Patient ? ProductVerification.SelfReported : ProductVerification.ProfessionalConfirmed;
        row.Consistency = c.Consistency;
        row.FindingsJson = System.Text.Json.JsonSerializer.Serialize(c.Findings);
        row.RecordedBy = recorder;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;
        row.IsDemo = c.Reference?.IsDemo ?? false;
    }

    private static Task<bool> IsDuplicateAsync(ProductTraceDbContext db, ProductRecordRow row, CancellationToken ct) =>
        db.Products.AnyAsync(x => x.Id != row.Id && x.PatientId == row.PatientId && x.DeletedAt == null && x.BatchNormalized == row.BatchNormalized && x.ProductKey == row.ProductKey && x.ExpiryDate == row.ExpiryDate, ct);

    private static ProductVersionRow Version(ProductRecordRow r, DateTimeOffset at, Guid actor, string reason) => new()
    {
        Id = Guid.CreateVersion7(), ProductRecordId = r.Id, VersionNumber = r.Version, ChangedAt = at, ChangedBy = actor, Reason = reason,
        SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new { r.MedicationId, r.ProductName, r.ManufacturerId, r.ManufacturerName, r.BatchNumber, r.ManufactureDate, r.ExpiryDate, r.Gtin, r.ReceivedOn, r.Verification, r.Consistency }, Support.Json),
    };

    private static async Task<bool> TrySaveAsync(ProductTraceDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<ProductRecordDto>> ToDtosAsync(IReadOnlyList<ProductRecordRow> rows, Guid subjectId, CancellationToken ct)
    {
        var today = Today;
        var names = new Dictionary<Guid, LocalizedText?>();
        foreach (var id in rows.Where(r => r.MedicationId is not null).Select(r => r.MedicationId!.Value).Distinct())
        {
            var m = await reference.GetAsync(id, true, ct);
            names[id] = m.Succeeded ? m.Value!.Name : null;
        }

        return [.. rows.Select(r => new ProductRecordDto(
            r.Id, subjectId, r.PatientMedicationId, r.MedicationId, r.ProductName, r.MedicationId is { } id ? names[id] : null, r.GenericName, r.ManufacturerId, r.ManufacturerName, r.BatchNumber, r.ManufactureDate,
            r.ExpiryDate, r.ExpiryDate < today, r.Gtin, r.PharmacyNote, r.ReceivedOn, r.Method, r.Verification, r.Consistency,
            System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.FindingsJson) ?? [], r.RecordedBy, r.UpdatedAt, r.Version, r.IsDemo, r.IsDemo ? Support.DemoNotice : null))];
    }
}
