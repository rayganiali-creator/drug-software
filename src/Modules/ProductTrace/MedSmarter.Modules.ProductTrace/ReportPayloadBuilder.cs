using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;
using MedSmarter.Modules.ProductTrace.Persistence;

namespace MedSmarter.Modules.ProductTrace;

/// <summary>
/// Builds the de-identified payload a manufacturer may receive. Starts from nothing and adds only the allowed fields: the patient's name,
/// account id, record ids, birth date and contact data are never read here. Demographics are bands, and only with the patient's consent.
/// </summary>
public sealed class ReportPayloadBuilder(IClock clock, IPurposeConsentEvaluator consent, IPatientDirectory patients)
{
    public const string SchemaVersion = "msr-1";

    public sealed record Built(ManufacturerReportPayload? Payload, string? Error)
    {
        public static Built Fail(string error) => new(null, error);
    }

    public async Task<Built> BuildAsync(ReportRow report, ProductRecordRow product, CancellationToken ct)
    {
        var patient = await patients.FindByPatientIdAsync(report.PatientId, ct);
        if (patient is null)
        {
            return Built.Fail("patient.not_found");
        }

        var subject = patient.SubjectId;
        var basic = await consent.EvaluateAsync(new PurposeConsentCheck(subject, ConsentPurposes.ManufacturerReport, [DataScopes.Products]), ct);
        if (!basic.Allowed)
        {
            return Built.Fail("consent.required");
        }

        if (FreeTextGuard.Problem(report.Description, "description", 1000) is { } problem)
        {
            return Built.Fail(problem);
        }

        var includeProfile = (await consent.EvaluateAsync(new PurposeConsentCheck(subject, ConsentPurposes.ManufacturerReport, [DataScopes.Products, DataScopes.Profile]), ct)).Allowed;
        var concomitant = new List<string>();
        if (report.IncludeConcomitant)
        {
            if (!(await consent.EvaluateAsync(new PurposeConsentCheck(subject, ConsentPurposes.ManufacturerReport, [DataScopes.Products, DataScopes.Medications]), ct)).Allowed)
            {
                return Built.Fail("consent.scope_medications_required");
            }

            concomitant = [.. (await patients.ActiveMedicationsAsync(subject, ct))
                .Where(m => m.IsRegistered && m.MedicationId != product.MedicationId) // registered names only: free text typed by the patient never leaves
                .Select(m => m.ReferenceName?.En ?? m.ReferenceName?.Fa ?? m.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        }

        var payload = new ManufacturerReportPayload(
            report.Reference, product.ProductName, product.GenericName, product.ManufacturerName, product.BatchNumber, product.ManufactureDate, product.ExpiryDate, product.Gtin,
            report.IssueType.ToString(), report.Severity.ToString(), report.OccurredOn, report.DurationOfUseDays,
            includeProfile ? AgeBands.Of(patient.YearOfBirth, clock.UtcNow.Year) : "unknown",
            includeProfile && patient.Sex is { } sex ? sex.ToString() : "unknown",
            concomitant, FreeTextGuard.Clean(report.Description), report.IsDemo, SchemaVersion);
        return new Built(payload, null);
    }

    public static string Serialize(ManufacturerReportPayload payload) => JsonSerializer.Serialize(payload, Support.Json);

    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public static ManufacturerReportPayload? Deserialize(string? json) => json is null ? null : JsonSerializer.Deserialize<ManufacturerReportPayload>(json, Support.Json);
}
