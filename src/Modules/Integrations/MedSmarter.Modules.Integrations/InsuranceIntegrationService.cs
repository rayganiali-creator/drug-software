using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Integrations.Contracts;

namespace MedSmarter.Modules.Integrations;

/// <summary>In-memory state of the insurance integration (development). A PostgreSQL store replaces it when a real channel exists.</summary>
public sealed class InsuranceStore
{
    public Lock Gate { get; } = new();
    public Dictionary<(string System, string Value), Guid> Links { get; } = [];
    public Dictionary<(Guid Patient, string Insurer), StoredCoverage> Coverage { get; } = [];
    public Dictionary<Guid, InsuranceDataImport> Imports { get; } = [];
    public Dictionary<Guid, InsuranceBatch> Batches { get; } = [];
}

public sealed record StoredCoverage(InsuranceCoverage Coverage, string ExternalMemberId, DateTimeOffset SourceProducedAt);

public sealed partial class InsuranceIntegrationService(InsuranceStore store, IEnumerable<IInsuranceProvider> providers, IAuditWriter audit, IClock clock) : IInsuranceIntegrationService
{
    public const int MaxMembers = 5000;
    private static readonly JsonSerializerOptions Canonical = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]{1,15}$")]
    private static partial Regex Insurer();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{2,63}$")]
    private static partial Regex MemberId();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9._\-]{0,31}$")]
    private static partial Regex Plan();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{2,63}$")]
    private static partial Regex BatchIdPattern();

    private static bool Can(CurrentUser actor, string permission) => actor.Has(permission);

    public async Task<InsuranceResult<InsuranceDataImport>> ImportAsync(CurrentUser actor, InsuranceBatch batch, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!Can(actor, Permissions.InsuranceImport))
        {
            await Denied(actor, "import", source, correlationId, ct);
            return InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.Forbidden);
        }

        return await ProcessAsync(actor, batch, force: false, source, correlationId, ct);
    }

    public async Task<InsuranceResult<InsuranceDataImport>> ImportFromProviderAsync(CurrentUser actor, string providerId, DateTimeOffset? since, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!Can(actor, Permissions.InsuranceImport))
        {
            await Denied(actor, "import", source, correlationId, ct);
            return InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.Forbidden);
        }

        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null)
        {
            return InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.NotFound, "provider.unknown");
        }

        InsuranceBatch batch;
        try
        {
            batch = await provider.FetchAsync(since, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Transport problem: record a failed import (no partial state), report only a code.
            var failed = Record(actor, providerId, "unavailable-" + Guid.NewGuid().ToString("N")[..8], string.Empty, ImportStatus.Failed, [], "provider_unavailable");
            await audit.WriteAsync(new AuditEvent(AuditActions.InsuranceImport, AuditResult.Failure, actor.UserId, "insurance-import", failed.Id.ToString(), null, source, correlationId, "provider_unavailable"), ct);
            return InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.ProviderUnavailable, "provider_unavailable");
        }

        return await ProcessAsync(actor, batch, force: false, source, correlationId, ct);
    }

    public async Task<InsuranceResult<InsuranceDataImport>> RetryAsync(CurrentUser actor, Guid importId, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!Can(actor, Permissions.InsuranceImport))
        {
            await Denied(actor, "retry", source, correlationId, ct);
            return InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.Forbidden);
        }

        InsuranceBatch? batch;
        lock (store.Gate)
        {
            store.Batches.TryGetValue(importId, out batch);
        }

        return batch is null
            ? InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.NotFound)
            : await ProcessAsync(actor, batch, force: true, source, correlationId, ct);
    }

    public async Task<InsuranceResult<ExternalPatientIdentifier>> LinkPatientAsync(CurrentUser actor, ExternalPatientIdentifier external, Guid patientUserId, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!Can(actor, Permissions.InsuranceImport))
        {
            await Denied(actor, "link", source, correlationId, ct);
            return InsuranceResult.Fail<ExternalPatientIdentifier>(InsuranceError.Forbidden);
        }

        if (!Insurer().IsMatch(external.System ?? string.Empty) || !MemberId().IsMatch(external.Value ?? string.Empty) || patientUserId == Guid.Empty)
        {
            return InsuranceResult.Fail<ExternalPatientIdentifier>(InsuranceError.InvalidBatch, "identifier.invalid");
        }

        lock (store.Gate)
        {
            if (store.Links.TryGetValue((external.System!, external.Value!), out var existing))
            {
                return existing == patientUserId
                    ? InsuranceResult.Ok(external) // idempotent
                    : InsuranceResult.Fail<ExternalPatientIdentifier>(InsuranceError.Conflict, "identifier.linked_to_other_patient");
            }

            if (store.Links.Any(l => l.Key.System == external.System && l.Value == patientUserId))
            {
                return InsuranceResult.Fail<ExternalPatientIdentifier>(InsuranceError.Conflict, "patient.already_linked_in_system");
            }

            store.Links[(external.System!, external.Value!)] = patientUserId;
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.InsuranceImport, AuditResult.Success, actor.UserId, "insurance-link", null, patientUserId, source, correlationId, "linked"), ct);
        return InsuranceResult.Ok(external);
    }

    public async Task<InsuranceResult<IReadOnlyList<InsuranceDataImport>>> ListImportsAsync(CurrentUser actor, CancellationToken ct = default)
    {
        if (!Can(actor, Permissions.InsuranceImport))
        {
            return InsuranceResult.Fail<IReadOnlyList<InsuranceDataImport>>(InsuranceError.Forbidden);
        }

        await Task.CompletedTask;
        lock (store.Gate)
        {
            return InsuranceResult.Ok<IReadOnlyList<InsuranceDataImport>>([.. store.Imports.Values.OrderByDescending(i => i.ReceivedAt)]);
        }
    }

    public async Task<InsuranceResult<InsuranceCoverage>> GetCoverageAsync(CurrentUser actor, Guid patientUserId, string insurerCode, CancellationToken ct = default)
    {
        if (!Can(actor, Permissions.InsuranceRead))
        {
            return InsuranceResult.Fail<InsuranceCoverage>(InsuranceError.Forbidden);
        }

        await Task.CompletedTask;
        lock (store.Gate)
        {
            return store.Coverage.TryGetValue((patientUserId, insurerCode), out var c)
                ? InsuranceResult.Ok(c.Coverage)
                : InsuranceResult.Fail<InsuranceCoverage>(InsuranceError.NotFound);
        }
    }

    // ---------- processing ----------

    private async Task<InsuranceResult<InsuranceDataImport>> ProcessAsync(CurrentUser actor, InsuranceBatch batch, bool force, string source, string? correlationId, CancellationToken ct)
    {
        if (batch.Members is null || batch.Members.Count == 0 || batch.Members.Count > MaxMembers
            || !BatchIdPattern().IsMatch(batch.BatchId ?? string.Empty) || string.IsNullOrWhiteSpace(batch.ProviderId) || batch.ProviderId.Length > 64)
        {
            return InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.InvalidBatch, "batch.invalid");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(batch, Canonical))));
        if (!force)
        {
            lock (store.Gate)
            {
                var prior = store.Imports.Values.FirstOrDefault(i => i.ProviderId == batch.ProviderId && i.BatchId == batch.BatchId && i.Status != ImportStatus.Failed);
                if (prior is not null)
                {
                    return prior.ContentHash == hash
                        ? InsuranceResult.Ok(prior with { Status = ImportStatus.Duplicate }) // same bytes: nothing to do
                        : InsuranceResult.Fail<InsuranceDataImport>(InsuranceError.Conflict, "batch_id.reused_with_different_content");
                }
            }
        }

        var results = new List<InsuranceImportRecordResult>();
        var seen = new Dictionary<string, InsuranceMember>(StringComparer.Ordinal);
        foreach (var m in batch.Members)
        {
            results.Add(ProcessMember(batch, m, seen));
        }

        var counts = results.GroupBy(r => r.Outcome).ToDictionary(g => g.Key, g => g.Count());
        var issues = results.Any(r => r.Outcome is ImportRecordOutcome.Rejected or ImportRecordOutcome.Conflict or ImportRecordOutcome.Unmapped);
        var import = Record(actor, batch.ProviderId, batch.BatchId!, hash, issues ? ImportStatus.CompletedWithIssues : ImportStatus.Completed, results, null, counts, batch);
        await audit.WriteAsync(new AuditEvent(AuditActions.InsuranceImport, AuditResult.Success, actor.UserId, "insurance-import", import.Id.ToString(), null, source, correlationId, import.Status.ToString(),
            new Dictionary<string, string> { ["total"] = results.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), ["applied"] = import.Applied.ToString(System.Globalization.CultureInfo.InvariantCulture), ["issues"] = (import.Rejected + import.Conflicts + import.Unmapped).ToString(System.Globalization.CultureInfo.InvariantCulture) }), ct);
        return InsuranceResult.Ok(import);
    }

    private InsuranceImportRecordResult ProcessMember(InsuranceBatch batch, InsuranceMember m, Dictionary<string, InsuranceMember> seen)
    {
        var id = m.ExternalMemberId ?? string.Empty;
        var shown = MemberId().IsMatch(id) ? id : "invalid";
        var problem = Validate(m);
        if (problem is not null)
        {
            return new(shown, ImportRecordOutcome.Rejected, problem);
        }

        var key = m.InsurerCode + "|" + m.ExternalMemberId;
        if (seen.TryGetValue(key, out var earlier))
        {
            return earlier == m ? new(shown, ImportRecordOutcome.Unchanged, "duplicate_in_batch") : new(shown, ImportRecordOutcome.Conflict, "duplicate_with_different_data");
        }

        seen[key] = m;
        lock (store.Gate)
        {
            if (!store.Links.TryGetValue((m.InsurerCode, m.ExternalMemberId!), out var patient))
            {
                return new(shown, ImportRecordOutcome.Unmapped, "no_patient_link"); // never creates a patient
            }

            var k = (patient, m.InsurerCode);
            if (store.Coverage.TryGetValue(k, out var current))
            {
                if (current.Coverage == m.Coverage)
                {
                    return new(shown, ImportRecordOutcome.Unchanged, null);
                }

                if (batch.ProducedAt < current.SourceProducedAt)
                {
                    return new(shown, ImportRecordOutcome.Conflict, "older_than_stored");
                }

                if (batch.ProducedAt == current.SourceProducedAt)
                {
                    return new(shown, ImportRecordOutcome.Conflict, "same_time_different_data");
                }
            }

            store.Coverage[k] = new StoredCoverage(m.Coverage, m.ExternalMemberId!, batch.ProducedAt);
            return new(shown, ImportRecordOutcome.Applied, null);
        }
    }

    private string? Validate(InsuranceMember m)
    {
        if (!Insurer().IsMatch(m.InsurerCode ?? string.Empty)) { return "insurer.invalid"; }
        if (!MemberId().IsMatch(m.ExternalMemberId ?? string.Empty)) { return "member_id.invalid"; }
        if (m.Coverage is null || !Plan().IsMatch(m.Coverage.PlanCode ?? string.Empty)) { return "plan.invalid"; }
        if (!Enum.IsDefined(m.Coverage.Status)) { return "status.invalid"; }
        if (m.Coverage.ValidTo is { } to && to < m.Coverage.ValidFrom) { return "dates.order"; }
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return m.Coverage.ValidFrom < today.AddYears(-50) || m.Coverage.ValidFrom > today.AddYears(2) ? "dates.range" : null;
    }

    private InsuranceDataImport Record(CurrentUser actor, string provider, string batchId, string hash, ImportStatus status, List<InsuranceImportRecordResult> results, string? error, Dictionary<ImportRecordOutcome, int>? counts = null, InsuranceBatch? batch = null)
    {
        int C(ImportRecordOutcome o) => counts is not null && counts.TryGetValue(o, out var n) ? n : 0;
        var import = new InsuranceDataImport(Guid.CreateVersion7(), provider, batchId, hash, clock.UtcNow, status, results.Count, C(ImportRecordOutcome.Applied), C(ImportRecordOutcome.Unchanged),
            C(ImportRecordOutcome.Rejected), C(ImportRecordOutcome.Conflict), C(ImportRecordOutcome.Unmapped), error, results, actor.UserId);
        lock (store.Gate)
        {
            store.Imports[import.Id] = import;
            if (batch is not null)
            {
                store.Batches[import.Id] = batch;
            }
        }

        return import;
    }

    private Task Denied(CurrentUser actor, string what, string source, string? correlationId, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent(AuditActions.AccessDenied, AuditResult.Denied, actor.UserId, "insurance", null, null, source, correlationId, $"Rbac:{what}:role_lacks_permission"), ct);
}
