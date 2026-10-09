using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Patients.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Patients;

public sealed class PatientsOptions
{
    public const string Section = "Patients";

    /// <summary>Loads FICTIONAL demo patients, records and care relationships. Refused outside Development/Testing.</summary>
    public bool SeedDemoData { get; set; }

    /// <summary>Server-side key for hashing external identifiers (national id, insurance member id...). Never committed; empty disables identifier mapping.</summary>
    public string? IdentifierHashKey { get; set; }

    /// <summary>How long soft-deleted records are kept before <see cref="IPatientService.PurgeDeletedAsync"/> may remove them.</summary>
    public int RetentionDaysAfterDelete { get; set; } = 365;

    /// <summary>After this many days without an update a category is shown as "may be out of date".</summary>
    public Dictionary<string, int> StaleAfterDays { get; set; } = [];
}

public static class PatientsGuard
{
    public static void EnsureSafe(string environmentName, PatientsOptions options)
    {
        var devLike = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        if (options.SeedDemoData && !devLike)
        {
            throw new InvalidOperationException($"Patients:SeedDemoData is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start.");
        }
    }
}

/// <summary>Shared building blocks of the patient services: audit, versions, freshness and the "writable patient" rule.</summary>
internal static class Support
{
    public const string DemoNotice = "DEMO DATA - NOT FOR CLINICAL USE";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static readonly IReadOnlyDictionary<PatientDataCategory, int> DefaultStaleDays = new Dictionary<PatientDataCategory, int>
    {
        [PatientDataCategory.Profile] = 365,
        [PatientDataCategory.Conditions] = 180,
        [PatientDataCategory.Allergies] = 365,
        [PatientDataCategory.Medications] = 30,
        [PatientDataCategory.Schedule] = 60,
        [PatientDataCategory.Intake] = 3,
        [PatientDataCategory.Symptoms] = 14,
        [PatientDataCategory.Products] = 365,
    };

    public static int StaleDays(IOptions<PatientsOptions> options, PatientDataCategory c) =>
        options.Value.StaleAfterDays.TryGetValue(c.ToString(), out var d) && d > 0 ? d : DefaultStaleDays[c];

    public static Task<Patient?> FindPatientAsync(PatientsDbContext db, Guid subjectId, CancellationToken ct) =>
        db.Patients.FirstOrDefaultAsync(p => p.UserId == subjectId, ct);

    /// <summary>NotFound when there is no patient; Forbidden when the record is deactivated (no new data is accepted).</summary>
    public static PatientOutcome<T>? WritableProblem<T>(Patient? patient) =>
        patient is null ? PatientOutcome.Fail<T>(PatientError.NotFound, "patient.not_found")
        : patient.Status != PatientStatus.Active ? PatientOutcome.Fail<T>(PatientError.Forbidden, "patient.inactive")
        : null;

    public static PatientOutcome<T> Invalid<T>(IEnumerable<string> codes) => PatientOutcome.Fail<T>(PatientError.Validation, string.Join(';', codes));

    public static void AddVersion(PatientsDbContext db, Guid patientId, string type, Guid recordId, int version, object snapshot, DateTimeOffset at, Guid? actor, string reason) =>
        db.RecordVersions.Add(new RecordVersionRow
        {
            Id = Guid.CreateVersion7(), PatientId = patientId, RecordType = type, RecordId = recordId, VersionNumber = version,
            SnapshotJson = JsonSerializer.Serialize(snapshot, Json), ChangedAt = at, ChangedBy = actor, Reason = reason,
        });

    /// <summary>Upserts the "last updated" row of a category. Call after the data change was saved so the count is current.</summary>
    public static async Task TouchAsync(PatientsDbContext db, Guid patientId, PatientDataCategory category, Guid? actor, DateTimeOffset at, int count, CancellationToken ct)
    {
        var row = await db.DataCategories.FirstOrDefaultAsync(x => x.PatientId == patientId && x.Category == category, ct);
        if (row is null)
        {
            db.DataCategories.Add(new DataCategoryRecord { PatientId = patientId, Category = category, LastUpdatedAt = at, LastUpdatedBy = actor, RecordCount = count });
        }
        else
        {
            row.LastUpdatedAt = at;
            row.LastUpdatedBy = actor;
            row.RecordCount = count;
        }

        await db.SaveChangesAsync(ct);
    }

    public static Task AuditAsync(IAuditWriter audit, string action, AuditResult result, Guid actor, string resourceType, Guid? resourceId, Guid subject, string source, string? correlationId, string? reason = null, string? detail = null) =>
        audit.WriteAsync(new AuditEvent(action, result, actor, resourceType, resourceId?.ToString(), subject, source, correlationId, reason, detail is null ? null : new Dictionary<string, string> { ["detail"] = detail }));

    public static string? Notice(Patient p) => p.IsDemo ? DemoNotice : null;
}
