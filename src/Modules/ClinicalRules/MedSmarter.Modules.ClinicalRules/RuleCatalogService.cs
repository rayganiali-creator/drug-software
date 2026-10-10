using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Guidance.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.ClinicalRules;

internal static class RuleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

/// <summary>Loads rules from the store (bounded) and turns rows into records. Used by the catalog and by the assessment service.</summary>
public sealed class RuleStore(IDbContextFactory<ClinicalRulesDbContext> factory)
{
    public const int MaxRules = 500;

    public async Task<IReadOnlyList<RuleRecord>> LoadAllAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Rules.AsNoTracking().OrderBy(r => r.RuleId).ThenBy(r => r.Version).Take(MaxRules).ToListAsync(ct);
        return [.. rows.Select(ToRecord)];
    }

    internal static RuleRecord ToRecord(RuleRow r) => new(
        JsonSerializer.Deserialize<RuleDefinition>(r.DefinitionJson, RuleJson.Options)!,
        new RuleLifecycle(r.Status, r.IsDemo, r.Author, r.AuthoredAt, r.Reviewer, r.ReviewedAt, r.ReviewNote, r.EffectiveFrom, r.RetiredAt));
}

/// <summary>
/// Rule management with a two-person review. A new rule is always a Draft; a version's content never changes after creation (a change is a new version).
/// Approval needs a different person from the author, a passing run of the rule's own test cases and the evidence policy. Every step is audited.
/// </summary>
public sealed class RuleCatalogService(IDbContextFactory<ClinicalRulesDbContext> factory, RuleStore store, IClock clock, IAuditWriter audit, IGuidanceComposer composer, IOptions<ClinicalRulesOptions> options) : IClinicalRuleCatalog
{
    private const string ReservedPrefix = "DEMO-";

    public async Task<IReadOnlyList<RuleSummaryDto>> ListAsync(RuleStatus? status, int take, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Rules.AsNoTracking().AsQueryable();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var rows = await query.OrderBy(r => r.RuleId).ThenByDescending(r => r.Version).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        return [.. rows.Select(RuleStore.ToRecord).Select(r =>
        {
            var a = ActivationPolicy.Evaluate(r, now, options.Value.AllowDemonstrationRules);
            return new RuleSummaryDto(r.Definition.RuleId, r.Definition.Version, r.Definition.Title, r.Definition.Domain, r.Lifecycle.Status, r.Lifecycle.IsDemo, a.State, a.Reasons, r.Lifecycle.AuthoredAt);
        })];
    }

    public async Task<RuleOutcomeResult<RuleDetailDto>> GetAsync(string ruleId, int version, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.RuleId == ruleId && r.Version == version, ct);
        return row is null ? RuleResult.Fail<RuleDetailDto>(RuleError.NotFound, "rule.not_found") : RuleResult.Ok(Detail(RuleStore.ToRecord(row)));
    }

    public async Task<RuleOutcomeResult<RuleDetailDto>> CreateDraftAsync(Guid actorUserId, RuleDefinition definition, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = RuleValidator.Validate(definition, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), composer.TemplateKeys).ToList();
        if (definition.RuleId?.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            errors.Add("rule.id_reserved"); // DEMO- ids belong to the built-in demonstration rules
        }

        if (errors.Count > 0)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Validation, string.Join(';', errors));
        }

        var now = clock.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Rules.AnyAsync(r => r.RuleId == definition.RuleId && r.Version == definition.Version, ct))
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Conflict, "rule.version_exists");
        }

        var row = new RuleRow
        {
            RuleId = definition.RuleId!, Version = definition.Version, Status = RuleStatus.Draft, IsDemo = false, Author = actorUserId.ToString(), AuthoredAt = now,
            DefinitionJson = JsonSerializer.Serialize(definition, RuleJson.Options),
        };
        db.Rules.Add(row);
        db.RuleEvents.Add(Event(row, "created", RuleStatus.Draft, RuleStatus.Draft, actorUserId.ToString(), now));
        await db.SaveChangesAsync(ct);
        await Audit(AuditActions.ClinicalRuleCreated, AuditResult.Success, actorUserId, row, source, correlationId, null, ct);
        return RuleResult.Ok(Detail(RuleStore.ToRecord(row)));
    }

    public async Task<RuleOutcomeResult<RuleDetailDto>> SubmitForReviewAsync(Guid actorUserId, string ruleId, int version, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Rules.FirstOrDefaultAsync(r => r.RuleId == ruleId && r.Version == version, ct);
        if (row is null)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.NotFound, "rule.not_found");
        }

        if (row.IsDemo || row.Status != RuleStatus.Draft)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Conflict, "rule.status_transition_not_allowed");
        }

        var record = RuleStore.ToRecord(row);
        var failures = RuleValidator.RunTestCases(record.Definition);
        if (failures.Count > 0)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Validation, "tests.failed");
        }

        if (record.Definition.TestCases.Count == 0)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Validation, "policy.test_cases_missing");
        }

        return await MoveAsync(db, row, RuleStatus.UnderReview, "submitted", actorUserId, AuditActions.ClinicalRuleSubmitted, source, correlationId, ct);
    }

    public async Task<RuleOutcomeResult<RuleDetailDto>> ReviewAsync(Guid actorUserId, string ruleId, int version, ReviewDecision decision, string? note, DateTimeOffset? effectiveFrom, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Rules.FirstOrDefaultAsync(r => r.RuleId == ruleId && r.Version == version, ct);
        if (row is null)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.NotFound, "rule.not_found");
        }

        if (row.IsDemo || row.Status != RuleStatus.UnderReview)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Conflict, "rule.status_transition_not_allowed");
        }

        // Two-person rule, enforced here as well as in the permission model.
        if (string.Equals(row.Author, actorUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            await Audit(AuditActions.ClinicalRuleRefused, AuditResult.Denied, actorUserId, row, source, correlationId, "review.separation_of_duties", ct);
            return RuleResult.Fail<RuleDetailDto>(RuleError.Forbidden, "review.separation_of_duties");
        }

        var cleanNote = note?.Trim();
        if (cleanNote is { Length: > 500 } || RuleTextPolicy.Violations(cleanNote).Count > 0)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Validation, "review.note_invalid");
        }

        var now = clock.UtcNow;
        row.Reviewer = actorUserId.ToString();
        row.ReviewedAt = now;
        row.ReviewNote = cleanNote;
        if (decision == ReviewDecision.Reject)
        {
            if (string.IsNullOrWhiteSpace(cleanNote))
            {
                return RuleResult.Fail<RuleDetailDto>(RuleError.Validation, "review.note_required");
            }

            return await MoveAsync(db, row, RuleStatus.Rejected, "rejected", actorUserId, AuditActions.ClinicalRuleReviewed, source, correlationId, ct);
        }

        // Approval: the evidence policy, the date, and the rule's own test cases must all hold.
        row.EffectiveFrom = effectiveFrom ?? now;
        var candidate = RuleStore.ToRecord(row) with { Lifecycle = RuleStore.ToRecord(row).Lifecycle with { Status = RuleStatus.Approved } };
        var decisionAtEffective = ActivationPolicy.Evaluate(candidate, candidate.Lifecycle.EffectiveFrom!.Value, false);
        var reasons = decisionAtEffective.Reasons.ToList();
        var failures = RuleValidator.RunTestCases(candidate.Definition);
        if (failures.Count > 0)
        {
            reasons.Add("tests.failed");
        }

        if (reasons.Count > 0)
        {
            db.ChangeTracker.Clear();
            await Audit(AuditActions.ClinicalRuleRefused, AuditResult.Denied, actorUserId, row, source, correlationId, string.Join(',', reasons), ct);
            return RuleResult.Fail<RuleDetailDto>(RuleError.Validation, string.Join(';', reasons));
        }

        return await MoveAsync(db, row, RuleStatus.Approved, "approved", actorUserId, AuditActions.ClinicalRuleReviewed, source, correlationId, ct);
    }

    public async Task<RuleOutcomeResult<RuleDetailDto>> RetireAsync(Guid actorUserId, string ruleId, int version, string source, string? correlationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Rules.FirstOrDefaultAsync(r => r.RuleId == ruleId && r.Version == version, ct);
        if (row is null)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.NotFound, "rule.not_found");
        }

        if (row.Status != RuleStatus.Approved)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Conflict, "rule.status_transition_not_allowed");
        }

        row.RetiredAt = clock.UtcNow;
        return await MoveAsync(db, row, RuleStatus.Retired, "retired", actorUserId, AuditActions.ClinicalRuleRetired, source, correlationId, ct);
    }

    public async Task<RuleCoverageDto> CoverageAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var allow = options.Value.AllowDemonstrationRules;
        var decided = (await store.LoadAllAsync(ct)).Select(r => (Record: r, Decision: ActivationPolicy.Evaluate(r, now, allow))).ToList();
        var covered = decided.Where(d => d.Decision.State == ActivationState.Active).Select(d => d.Record.Definition.Domain).Distinct().Order().ToList();
        return new RuleCoverageDto(
            decided.Count(d => d.Decision.State == ActivationState.Active), decided.Count(d => d.Decision.State == ActivationState.DemonstrationOnly), decided.Count(d => d.Decision.State == ActivationState.Inactive),
            covered, [.. Enum.GetValues<RuleDomain>().Except(covered)], ClinicalSafetyEngine.UnsupportedDomains, ActivationPolicy.RuleSetVersion(decided, allow), allow);
    }

    private RuleDetailDto Detail(RuleRecord r) => new(r, ActivationPolicy.Evaluate(r, clock.UtcNow, options.Value.AllowDemonstrationRules));

    private static RuleEventRow Event(RuleRow row, string action, RuleStatus from, RuleStatus to, string actor, DateTimeOffset at) =>
        new() { Id = Guid.CreateVersion7(), RuleId = row.RuleId, Version = row.Version, Action = action, FromStatus = from, ToStatus = to, Actor = actor, At = at };

    private async Task<RuleOutcomeResult<RuleDetailDto>> MoveAsync(ClinicalRulesDbContext db, RuleRow row, RuleStatus to, string action, Guid actor, string auditAction, string source, string? correlationId, CancellationToken ct)
    {
        var from = row.Status;
        row.Status = to;
        row.RowVersion++;
        db.RuleEvents.Add(Event(row, action, from, to, actor.ToString(), clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RuleResult.Fail<RuleDetailDto>(RuleError.Conflict, "version.mismatch");
        }

        await Audit(auditAction, AuditResult.Success, actor, row, source, correlationId, action, ct);
        return RuleResult.Ok(Detail(RuleStore.ToRecord(row)));
    }

    private Task Audit(string action, AuditResult result, Guid actor, RuleRow row, string source, string? correlationId, string? reason, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent(action, result, actor, "clinical-rule", string.Create(CultureInfo.InvariantCulture, $"{row.RuleId}@{row.Version}"), null, source, correlationId, reason), ct);
}

/// <summary>Writes the built-in DEMONSTRATION rules to the store (Development and Testing only). They are inserted as drafts and are never approved.</summary>
public sealed class DemoRuleSeeder(IDbContextFactory<ClinicalRulesDbContext> factory, IClock clock) : IDemoRuleSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        foreach (var r in DemoRules.All(now))
        {
            if (await db.Rules.AnyAsync(x => x.RuleId == r.Definition.RuleId && x.Version == r.Definition.Version, ct))
            {
                continue;
            }

            db.Rules.Add(new RuleRow
            {
                RuleId = r.Definition.RuleId, Version = r.Definition.Version, Status = r.Lifecycle.Status, IsDemo = true, Author = DemoRules.Author, AuthoredAt = now,
                DefinitionJson = JsonSerializer.Serialize(r.Definition, RuleJson.Options),
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
