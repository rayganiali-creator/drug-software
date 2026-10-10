using System.Globalization;
using System.Text.Json;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.ClinicalRules.Persistence;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.ClinicalRules;

/// <summary>
/// Orchestration around the pure engine. Builds a frozen snapshot from the authorized services (only the categories the requester may read; the rest is
/// marked NotAuthorized), runs the engine and the independent safety layer, stores the assessment, and raises guidance through the Guidance module for
/// ACTIONABLE findings only. A model is never involved. Nothing here runs by itself: an assessment exists because somebody asked for it.
///
/// Atomicity: the assessment is stored first, guidance is created second, the link state is updated third. These are separate stores, so a failure between
/// steps can leave an assessment whose guidance link state is "Failed" (not confirmed) while some messages exist; messages are idempotent per finding, so a re-run
/// repairs it without duplicates. An assessment is never left claiming guidance exists that was not created.
/// </summary>
public sealed class ClinicalSafetyService(
    IDbContextFactory<ClinicalRulesDbContext> factory,
    RuleStore store,
    IClock clock,
    IAuditWriter audit,
    IPatientDirectory directory,
    IPatientService patients,
    IPatientMedicationService patientMedications,
    IMedicationService medications,
    IGuidanceService guidance,
    IOptions<ClinicalRulesOptions> options) : IClinicalSafetyService
{
    public const int MaxActiveMedications = 60;
    public const int MaxReferenceReads = 120;

    private static readonly string[] Categories = ["profile", "medications", "allergies", "symptoms"];

    public async Task<SafetyOutcome<AssessmentDto>> AssessAsync(Guid actorUserId, Guid subjectId, AssessCommand command, string source, string? correlationId, CancellationToken ct = default)
    {
        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null || patient.Status != PatientStatus.Active)
        {
            return SafetyResult.Fail<AssessmentDto>(SafetyError.NotFound, "patient.not_found");
        }

        var readable = Readable(command.ReadableCategories);
        if (!readable.Contains("medications"))
        {
            return SafetyResult.Fail<AssessmentDto>(SafetyError.Forbidden, "input.medications.not_authorized"); // nothing can be assessed without them
        }

        var locale = command.Locale is "fa" ? "fa" : "en";
        AssessmentSnapshot snapshot;
        EngineResult result;
        try
        {
            var built = await BuildSnapshotAsync(subjectId, readable, ct);
            if (built.Error is { } err)
            {
                await audit.WriteAsync(new AuditEvent(AuditActions.SafetyAssessmentFailed, AuditResult.Failure, actorUserId, "safety-assessment", null, subjectId, source, correlationId, err), ct);
                return SafetyResult.Fail<AssessmentDto>(SafetyError.Validation, err);
            }

            snapshot = built.Snapshot!;
            var rules = await store.LoadAllAsync(ct);
            result = SafetyLayer.Apply(ClinicalSafetyEngine.Evaluate(rules, snapshot, options.Value.AllowDemonstrationRules));
        }
#pragma warning disable CA1031 // any technical failure must end as "unavailable", never as a partial or empty result
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.SafetyAssessmentFailed, AuditResult.Failure, actorUserId, "safety-assessment", null, subjectId, source, correlationId, "assessment.unavailable"), ct);
            return SafetyResult.Fail<AssessmentDto>(SafetyError.Unavailable, "assessment.unavailable");
        }

        foreach (var blocked in Categories.Except(readable))
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.SafetyInputBlocked, AuditResult.Denied, actorUserId, "safety-assessment", blocked, subjectId, source, correlationId, "input.not_authorized"), ct);
        }

        var id = Guid.CreateVersion7();
        var dataAsOf = result.Inputs.Where(i => i.LastUpdatedAt is not null).Select(i => i.LastUpdatedAt!.Value).DefaultIfEmpty(snapshot.At).Max();
        var actionable = result.Findings.Where(f => f.Actionable).ToList();
        var row = new AssessmentRow
        {
            Id = id, PatientId = patient.PatientId, RequestedByUserId = actorUserId, RequestedByPatient = actorUserId == subjectId, Trigger = command.Trigger is "manual" or "reassessment" ? command.Trigger : "manual",
            CreatedAt = snapshot.At, Status = result.Status, Complete = result.Complete, RuleSetVersion = result.RuleSetVersion, EngineVersion = result.EngineVersion, DataAsOf = dataAsOf,
            FindingCount = result.Findings.Count, ActionableFindingCount = actionable.Count, ContainsDemonstration = result.ContainsDemonstration,
            // Pessimistic until guidance is confirmed: the row never claims messages exist that were not created.
            GuidanceState = actionable.Count == 0 || result.Status == AssessmentStatus.Failed ? GuidanceLinkState.NotApplicable : GuidanceLinkState.Failed,
            ResultJson = JsonSerializer.Serialize(result, RuleJson.Options),
        };

        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.Assessments.Add(row);
            await db.SaveChangesAsync(ct);
        }
#pragma warning disable CA1031 // a result that could not be stored must not be shown or acted on
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            await audit.WriteAsync(new AuditEvent(AuditActions.SafetyAssessmentFailed, AuditResult.Failure, actorUserId, "safety-assessment", id.ToString(), subjectId, source, correlationId, "persistence.failed"), ct);
            return SafetyResult.Fail<AssessmentDto>(SafetyError.Unavailable, "persistence.failed"); // no guidance was created: no contradictory state
        }

        var links = new List<GuidanceLink>();
        var state = row.GuidanceState;
        if (state == GuidanceLinkState.Failed)
        {
            (state, links) = await RaiseGuidanceAsync(subjectId, id, actionable, locale, dataAsOf, source, correlationId, ct);
            await SaveGuidanceStateAsync(id, state, links, ct);
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.SafetyAssessmentRun, result.Status == AssessmentStatus.Failed ? AuditResult.Failure : AuditResult.Success, actorUserId, "safety-assessment", id.ToString(), subjectId, source, correlationId,
            result.Status.ToString(), new Dictionary<string, string>
            {
                ["findings"] = result.Findings.Count.ToString(CultureInfo.InvariantCulture),
                ["actionable"] = actionable.Count.ToString(CultureInfo.InvariantCulture),
                ["rule_set"] = result.RuleSetVersion,
                ["complete"] = result.Complete ? "yes" : "no",
                ["guidance"] = state.ToString(),
            }), ct);

        var noLonger = result.Status == AssessmentStatus.Failed ? [] : await NoLongerMatchingAsync(subjectId, result, ct);
        row.GuidanceState = state;
        var dto = ToDto(subjectId, row, result, links, noLonger, [], readable);
        return SafetyResult.Ok(dto);
    }

    public async Task<SafetyOutcome<IReadOnlyList<AssessmentSummaryDto>>> ListAsync(Guid subjectId, int take, IReadOnlyCollection<string> readableCategories, CancellationToken ct = default)
    {
        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return SafetyResult.Fail<IReadOnlyList<AssessmentSummaryDto>>(SafetyError.NotFound, "patient.not_found");
        }

        var readable = Readable(readableCategories);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Assessments.AsNoTracking().Where(a => a.PatientId == patient.PatientId).OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 50)).ToListAsync(ct);
        return SafetyResult.Ok<IReadOnlyList<AssessmentSummaryDto>>([.. rows.Select(r =>
        {
            var result = Filter(Deserialize(r), readable);
            return new AssessmentSummaryDto(r.Id, r.CreatedAt, result.Status, result.Complete, result.Findings.Count, result.Findings.Count(f => f.Actionable), r.RuleSetVersion, r.Trigger, r.ContainsDemonstration);
        })]);
    }

    public async Task<SafetyOutcome<AssessmentDto>> GetAsync(Guid subjectId, Guid assessmentId, IReadOnlyCollection<string> readableCategories, CancellationToken ct = default)
    {
        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return SafetyResult.Fail<AssessmentDto>(SafetyError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Assessments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assessmentId && a.PatientId == patient.PatientId, ct); // another patient's id is simply not found
        return row is null ? SafetyResult.Fail<AssessmentDto>(SafetyError.NotFound, "assessment.not_found") : SafetyResult.Ok(await WithFreshnessAsync(subjectId, row, readableCategories, ct));
    }

    public async Task<SafetyOutcome<AssessmentDto>> LatestAsync(Guid subjectId, IReadOnlyCollection<string> readableCategories, CancellationToken ct = default)
    {
        var patient = await directory.FindBySubjectAsync(subjectId, ct);
        if (patient is null)
        {
            return SafetyResult.Fail<AssessmentDto>(SafetyError.NotFound, "patient.not_found");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Assessments.AsNoTracking().Where(a => a.PatientId == patient.PatientId).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        return row is null ? SafetyResult.Fail<AssessmentDto>(SafetyError.NotFound, "assessment.none") : SafetyResult.Ok(await WithFreshnessAsync(subjectId, row, readableCategories, ct));
    }

    // ---------- snapshot ----------

    private sealed record Built(AssessmentSnapshot? Snapshot, string? Error);

    private async Task<Built> BuildSnapshotAsync(Guid subjectId, HashSet<string> readable, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var freshness = (await patients.GetFreshnessAsync(subjectId, ct)) is { Succeeded: true } f ? f.Value! : [];
        var inputs = new List<InputState>();
        foreach (var (category, key) in new[] { ("profile", PatientDataCategory.Profile), ("medications", PatientDataCategory.Medications), ("allergies", PatientDataCategory.Allergies), ("symptoms", PatientDataCategory.Symptoms) })
        {
            if (!readable.Contains(category))
            {
                inputs.Add(new InputState(category, InputAvailability.NotAuthorized, null, 0, false, 0, "withheld"));
                continue;
            }

            var fr = freshness.FirstOrDefault(x => x.Category == key);
            inputs.Add(fr is null
                ? new InputState(category, InputAvailability.Unavailable, null, 0, false, 0, "patient-record")
                : new InputState(category, fr.NeverRecorded ? InputAvailability.NeverRecorded : InputAvailability.Available, fr.LastUpdatedAt, fr.RecordCount, fr.IsStale, fr.StaleAfterDays, "patient-record"));
        }

        int? age = null;
        if (readable.Contains("profile") && await patients.GetProfileAsync(subjectId, ct) is { Succeeded: true } profile)
        {
            age = profile.Value!.AgeYears;
        }

        var meds = new List<SnapshotMedication>();
        var interactions = new Dictionary<Guid, SnapshotInteraction>();
        var details = new Dictionary<Guid, MedicationDetailDto?>();
        var reads = 0;

        async Task<MedicationDetailDto?> Reference(Guid id)
        {
            if (details.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (reads++ >= MaxReferenceReads)
            {
                return details[id] = null; // beyond the bound the reference is treated as unreadable, never guessed
            }

            var got = await medications.GetAsync(id, false, ct);
            return details[id] = got.Succeeded ? got.Value : null;
        }

        if (readable.Contains("medications") && inputs.First(i => i.Category == "medications").Availability == InputAvailability.Available)
        {
            var listed = await patientMedications.ListAsync(subjectId, false, ct);
            if (!listed.Succeeded)
            {
                inputs[inputs.FindIndex(i => i.Category == "medications")] = inputs.First(i => i.Category == "medications") with { Availability = InputAvailability.Unavailable };
            }
            else
            {
                var active = listed.Value!.Where(m => m.Status == PatientMedicationStatus.Active).ToList();
                if (active.Count > MaxActiveMedications)
                {
                    return new Built(null, "medications.too_many");
                }

                foreach (var m in active.OrderBy(m => m.Id))
                {
                    var detail = m.IsRegistered && m.MedicationId is { } mid ? await Reference(mid) : null;
                    meds.Add(new SnapshotMedication(m.Id, m.MedicationId, m.DisplayName, m.IsRegistered, true, detail is not null, [.. detail?.Ingredients.Select(i => i.IngredientId).Distinct() ?? []]));
                    foreach (var x in detail?.Interactions ?? [])
                    {
                        interactions.TryAdd(x.Id, new SnapshotInteraction(x.ThisIngredientId, x.OtherIngredientId, x.Severity.ToString(), x.Validation.ToString(), EvidenceOf(x, detail!)));
                    }
                }
            }
        }

        var allergies = new List<SnapshotAllergy>();
        if (readable.Contains("allergies") && inputs.First(i => i.Category == "allergies").Availability == InputAvailability.Available)
        {
            var listed = await patients.ListAllergiesAsync(subjectId, ct);
            if (!listed.Succeeded)
            {
                inputs[inputs.FindIndex(i => i.Category == "allergies")] = inputs.First(i => i.Category == "allergies") with { Availability = InputAvailability.Unavailable };
            }
            else
            {
                foreach (var a in listed.Value!.OrderBy(a => a.Id))
                {
                    var detail = a.Kind == AllergenKind.Medication && a.MedicationId is { } mid ? await Reference(mid) : null;
                    allergies.Add(new SnapshotAllergy(a.Id, a.Kind.ToString(), a.MedicationId, detail is not null, [.. detail?.Ingredients.Select(i => i.IngredientId).Distinct() ?? []], a.Substance, a.Severity.ToString()));
                }
            }
        }

        var symptoms = new List<SnapshotSymptom>();
        if (readable.Contains("symptoms") && inputs.First(i => i.Category == "symptoms").Availability == InputAvailability.Available)
        {
            var listed = await patients.ListSymptomsAsync(subjectId, 100, ct);
            if (!listed.Succeeded)
            {
                inputs[inputs.FindIndex(i => i.Category == "symptoms")] = inputs.First(i => i.Category == "symptoms") with { Availability = InputAvailability.Unavailable };
            }
            else
            {
                symptoms.AddRange(listed.Value!.Select(s => new SnapshotSymptom(s.Id, s.Severity.ToString(), s.OnsetAt, s.ResolvedAt)));
            }
        }

        return new Built(new AssessmentSnapshot(now, age, inputs, meds, allergies, symptoms, [.. interactions.Values.OrderBy(i => i.IngredientA).ThenBy(i => i.IngredientB)]), null);
    }

    private static EvidenceRef EvidenceOf(InteractionDto x, MedicationDetailDto detail)
    {
        var src = detail.Sources.FirstOrDefault(s => s.Id == x.SourceId);
        var validation = x.Validation switch
        {
            ValidationStatus.Validated => EvidenceValidation.Validated,
            ValidationStatus.Demo => EvidenceValidation.Demo,
            ValidationStatus.NeedsValidation => EvidenceValidation.NeedsValidation,
            _ => EvidenceValidation.Unverified,
        };
        // The publication date is not recorded by the knowledge module, so it stays empty rather than being invented.
        return new EvidenceRef(x.SourceId.ToString(), src?.Name ?? "source not found in the reference", src?.Version ?? "unknown", null, validation, x.Validation == ValidationStatus.Validated ? "validated in the reference" : "none", null);
    }

    // ---------- guidance ----------

    private async Task<(GuidanceLinkState State, List<GuidanceLink> Links)> RaiseGuidanceAsync(Guid subjectId, Guid assessmentId, IReadOnlyList<Finding> actionable, string locale, DateTimeOffset dataAsOf, string source, string? correlationId, CancellationToken ct)
    {
        var links = new List<GuidanceLink>();
        foreach (var f in actionable)
        {
            try
            {
                var request = GuidanceFor(f, locale);
                var origin = new GuidanceOrigin("safety", assessmentId, f.RuleId, f.RuleVersion, f.Key, dataAsOf);
                var made = await guidance.CreateFromOriginAsync(subjectId, request, origin, false, source, correlationId, ct);
                links.Add(new GuidanceLink(f.Key, made.Succeeded ? made.Value!.Message.Id : null, made.Succeeded ? (made.Value!.Existing ? "existing" : "created") : "failed:" + (made.Detail ?? "unknown")));
            }
#pragma warning disable CA1031 // one failing message must not stop the others or hide the finding
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                links.Add(new GuidanceLink(f.Key, null, "failed:exception"));
            }
        }

        var ok = links.Count(l => l.MessageId is not null);
        return (ok == links.Count ? GuidanceLinkState.Created : ok == 0 ? GuidanceLinkState.Failed : GuidanceLinkState.Partial, links);
    }

    internal static GuidanceRequest GuidanceFor(Finding f, string locale)
    {
        var meds = f.Subjects.Where(s => s.Kind == "medication").Select(s => s.Label).ToList();
        var parameters = new Dictionary<string, string>
        {
            ["medicationA"] = meds.ElementAtOrDefault(0) ?? "…",
            ["medicationB"] = meds.ElementAtOrDefault(1) ?? "…",
            ["medication"] = meds.ElementAtOrDefault(0) ?? "…",
            ["allergy"] = f.Subjects.FirstOrDefault(s => s.Kind == "allergy")?.Label ?? "…",
            ["severity"] = f.ReferenceSeverity ?? "unknown",
        };
        // Phase 7 has no sourced emergency signs, so a message is never raised at the Urgent level (which requires them): the cap is stated in docs and in the assessment.
        var level = f.Urgency switch
        {
            FindingUrgency.None => GuidanceLevel.Information,
            FindingUrgency.Routine => GuidanceLevel.FollowUp,
            _ => GuidanceLevel.ReviewSoon,
        };
        var confidence = f.EvidenceConflict || f.InputsStale ? GuidanceConfidence.Low : GuidanceConfidence.Moderate;
        var rule = f.RuleId.ToLowerInvariant();
        var basis = locale == "fa"
            ? string.Create(CultureInfo.InvariantCulture, $"قانون ایمنی {rule} (نسخه‌ی {f.RuleVersion}) روی اطلاعات ثبت‌شده‌ی شما")
            : string.Create(CultureInfo.InvariantCulture, $"safety rule {rule} (version {f.RuleVersion}) applied to your recorded information");
        return new GuidanceRequest(f.GuidanceTemplateKey, level, locale, parameters, confidence, basis, true);
    }

    private async Task SaveGuidanceStateAsync(Guid assessmentId, GuidanceLinkState state, IReadOnlyList<GuidanceLink> links, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await db.Assessments.FirstAsync(a => a.Id == assessmentId, ct);
            row.GuidanceState = state;
            row.GuidanceLinksJson = JsonSerializer.Serialize(links, RuleJson.Options);
            await db.SaveChangesAsync(ct);
        }
#pragma warning disable CA1031 // the pessimistic "not confirmed" state stays; a re-run repairs it idempotently
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
        }
    }

    private async Task<IReadOnlyList<string>> NoLongerMatchingAsync(Guid subjectId, EngineResult result, CancellationToken ct)
    {
        try
        {
            var open = await guidance.ListOpenByOriginAsync(subjectId, "safety", ct);
            if (!open.Succeeded)
            {
                return [];
            }

            var current = result.Findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
            return [.. open.Value!.Where(o => !current.Contains(o.Origin.FindingKey)
                && result.Evaluations.Any(e => e.RuleId == o.Origin.RuleId && e.Version == o.Origin.RuleVersion && e.Outcome == RuleOutcome.NoMatch && e.Activation == ActivationState.Active))
                .Select(o => o.Origin.FindingKey)];
        }
#pragma warning disable CA1031 // a failed lookup only means fewer hints, never fewer findings
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    // ---------- reading stored assessments ----------

    private async Task<AssessmentDto> WithFreshnessAsync(Guid subjectId, AssessmentRow row, IReadOnlyCollection<string> readableCategories, CancellationToken ct)
    {
        var readable = Readable(readableCategories);
        var stored = Deserialize(row);
        var outdated = new List<string>();
        try
        {
            var rules = await store.LoadAllAsync(ct);
            var current = ActivationPolicy.RuleSetVersion(rules.Select(r => (r, ActivationPolicy.Evaluate(r, clock.UtcNow, options.Value.AllowDemonstrationRules))), options.Value.AllowDemonstrationRules);
            if (!string.Equals(current, row.RuleSetVersion, StringComparison.Ordinal))
            {
                outdated.Add("rules.changed");
            }

            if (await patients.GetFreshnessAsync(subjectId, ct) is { Succeeded: true } fr)
            {
                foreach (var input in stored.Inputs.Where(i => readable.Contains(i.Category)))
                {
                    var key = input.Category switch { "profile" => PatientDataCategory.Profile, "medications" => PatientDataCategory.Medications, "allergies" => PatientDataCategory.Allergies, _ => PatientDataCategory.Symptoms };
                    var now = fr.Value!.FirstOrDefault(x => x.Category == key);
                    if (now?.LastUpdatedAt is { } updated && (input.LastUpdatedAt is null || updated > input.LastUpdatedAt))
                    {
                        outdated.Add($"data.changed.{input.Category}");
                    }
                }
            }
        }
#pragma warning disable CA1031 // if freshness cannot be established, say so instead of implying the result is current
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            outdated.Add("freshness.unknown");
        }

        var links = JsonSerializer.Deserialize<List<GuidanceLink>>(row.GuidanceLinksJson, RuleJson.Options) ?? [];
        return ToDto(subjectId, row, Filter(stored, readable), links, [], outdated, readable);
    }

    private static EngineResult Deserialize(AssessmentRow row) => JsonSerializer.Deserialize<EngineResult>(row.ResultJson, RuleJson.Options)!;

    private static string[] NeededBy(RuleDomain d) => d switch
    {
        RuleDomain.AllergyConflict => ["medications", "allergies"],
        RuleDomain.ReportedSymptom => ["symptoms"],
        _ => ["medications"],
    };

    /// <summary>A stored result is shown only as far as the viewer may read today: findings that depend on a category they cannot read are removed and that category is shown as withheld.</summary>
    internal static EngineResult Filter(EngineResult r, IReadOnlySet<string> readable)
    {
        if (Categories.All(readable.Contains))
        {
            return r;
        }

        bool Visible(RuleDomain d) => NeededBy(d).All(readable.Contains);
        var keep = r.Findings.Where(f => Visible(f.Domain)).ToList();

        // A rule the viewer cannot fully see reports "not authorized" instead of its stored outcome: "no match" on a category they may not read would itself disclose it.
        var evaluations = r.Evaluations.Select(e => Visible(e.Domain) ? e
            : e with { Outcome = e.Outcome == RuleOutcome.Unavailable ? e.Outcome : RuleOutcome.MissingData, Reasons = [.. NeededBy(e.Domain).Where(c => !readable.Contains(c)).Select(c => $"input.{c}.not_authorized")], Partial = false, FindingCount = 0 }).ToList();
        var inputs = r.Inputs.Select(i => readable.Contains(i.Category) ? i : new InputState(i.Category, InputAvailability.NotAuthorized, null, 0, false, 0, "withheld")).ToList();
        var active = evaluations.Where(e => e.Activation == ActivationState.Active).ToList();
        var status = r.Status is AssessmentStatus.Failed or AssessmentStatus.NoApprovedCoverage ? r.Status
            : keep.Any(f => active.Any(e => e.RuleId == f.RuleId && e.Version == f.RuleVersion)) ? AssessmentStatus.CompletedWithFindings
            : active.Any(e => e.Outcome is RuleOutcome.MissingData or RuleOutcome.StaleData or RuleOutcome.InsufficientEvidence or RuleOutcome.Error) ? AssessmentStatus.Incomplete
            : AssessmentStatus.CompletedNoMatches;
        return r with { Status = status, Complete = r.Complete && evaluations.SequenceEqual(r.Evaluations), Findings = keep, Evaluations = evaluations, Inputs = inputs, Limitations = [.. r.Limitations, "view.filtered_by_consent"] };
    }

    private static AssessmentDto ToDto(Guid subjectId, AssessmentRow row, EngineResult result, IReadOnlyList<GuidanceLink> links, IReadOnlyList<string> noLonger, List<string> outdated, IReadOnlySet<string> readable)
    {
        var visible = result.Findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        return new AssessmentDto(row.Id, subjectId, Filter(result, readable), row.Trigger, row.RequestedByPatient, row.GuidanceState, [.. links.Where(l => visible.Contains(l.FindingKey))], noLonger, outdated.Count > 0, outdated,
            result.ContainsDemonstration ? "DEMO: includes demonstration rules that are NOT clinically validated. This is not medical advice." : "Based on the rules listed. Not a diagnosis, not a triage and not medical advice.");
    }

    private static HashSet<string> Readable(IEnumerable<string> categories) => [.. categories.Where(Categories.Contains)];
}
