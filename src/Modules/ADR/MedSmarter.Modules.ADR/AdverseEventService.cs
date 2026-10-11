using System.Text.Json;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.ADR.Persistence;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.ADR;

/// <summary>
/// The rules of the internal adverse-event record:
/// the original report is written once and never edited; everything later is an append-only entry; the category starts at Unclassified and only a professional
/// moves it (with a reason, and it can go back); the review status follows a fixed set of moves; there is no seriousness grade and no causality score anywhere;
/// the assistance is a fixed template over what is recorded and is never stored. Audit entries carry ids and enum names only, never the text.
/// </summary>
public sealed class AdverseEventService(IDbContextFactory<AdrDbContext> factory, IClock clock, IAuditWriter audit, IPatientService patients, IPatientMedicationService medications) : IAdverseEventService
{
    public const int MaxText = 2000;
    public const int MaxOutcome = 500;
    public const int MaxMedications = 10;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record StoredMedication(Guid? PatientMedicationId, string? NameAsReported);

    // From, To, professional only. The one non-professional move is made by the system when the reporter answers a request for information.
    private static readonly (ReviewStatus From, ReviewStatus To)[] Moves =
    [
        (ReviewStatus.Submitted, ReviewStatus.InReview), (ReviewStatus.Submitted, ReviewStatus.Closed), (ReviewStatus.Submitted, ReviewStatus.InformationRequested),
        (ReviewStatus.InReview, ReviewStatus.InformationRequested), (ReviewStatus.InReview, ReviewStatus.Assessed), (ReviewStatus.InReview, ReviewStatus.Closed),
        (ReviewStatus.InformationRequested, ReviewStatus.InReview), (ReviewStatus.InformationRequested, ReviewStatus.Closed),
        (ReviewStatus.Assessed, ReviewStatus.InReview), (ReviewStatus.Assessed, ReviewStatus.Closed), (ReviewStatus.Assessed, ReviewStatus.InformationRequested),
        (ReviewStatus.Closed, ReviewStatus.InReview),
    ];

    public static bool MoveAllowed(ReviewStatus from, ReviewStatus to) => Moves.Any(m => m.From == from && m.To == to);

    private static bool IsProfessional(ReporterRole r) => r is ReporterRole.Physician or ReporterRole.Pharmacist;

    private static ReportOrigin OriginOf(ReporterRole r) => r == ReporterRole.Patient ? ReportOrigin.PatientReported : ReportOrigin.ProfessionalEntered;

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static bool HasControlCharacters(string s) => s.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));

    private static string? CheckText(string? s, int max, string field, bool required)
    {
        var t = Clean(s);
        if (t is null)
        {
            return required ? $"{field}.required" : null;
        }

        return t.Length > max ? $"{field}.too_long" : HasControlCharacters(t) ? $"{field}.invalid_characters" : null;
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> CreateAsync(Guid actorUserId, ReporterRole role, Guid subjectId, CreateAdverseEventInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        if (role == ReporterRole.Patient && actorUserId != subjectId)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Forbidden, "role.mismatch");
        }

        var errors = new List<string>();
        foreach (var e in new[] { CheckText(input.EventDescription, MaxText, "description", true), CheckText(input.AdditionalDetails, MaxText, "details", false), CheckText(input.OutcomeAsStated, MaxOutcome, "outcome", false) })
        {
            if (e is not null)
            {
                errors.Add(e);
            }
        }

        var now = clock.UtcNow;
        if (input.OnsetAt > now)
        {
            errors.Add("onset.future");
        }

        if (input.ResolvedAt > now)
        {
            errors.Add("resolved.future");
        }

        if (input.OnsetAt is { } on && input.ResolvedAt is { } rs && rs < on)
        {
            errors.Add("resolved.before_onset");
        }

        var requested = input.SuspectedMedications ?? [];
        if (requested.Count > MaxMedications)
        {
            errors.Add("medications.too_many");
        }

        if (errors.Count > 0)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, string.Join(';', errors.Distinct()));
        }

        var patient = await patients.GetAsync(subjectId, ct);
        if (!patient.Succeeded)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "patient.not_found");
        }

        var stored = new List<StoredMedication>();
        foreach (var m in requested)
        {
            var name = Clean(m.NameAsReported);
            if (name is { Length: > 200 } || (m.PatientMedicationId is null && name is null) || (name is not null && HasControlCharacters(name)))
            {
                return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "medication.invalid");
            }

            if (m.PatientMedicationId is { } mid)
            {
                // A link is only made to the patient's own record, and only after it was found. If it cannot be checked, nothing is linked on a guess.
                PatientOutcome<PatientMedicationDto> found;
                try
                {
                    found = await medications.GetAsync(subjectId, mid, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "medication.lookup_unavailable");
                }

                if (!found.Succeeded)
                {
                    return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "medication.not_found");
                }

                stored.Add(new StoredMedication(mid, found.Value!.DisplayName));
            }
            else
            {
                stored.Add(new StoredMedication(null, name));
            }
        }

        var clientRequestId = Clean(input.ClientRequestId);
        if (clientRequestId is { Length: > 64 })
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "clientRequestId.too_long");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (clientRequestId is not null)
        {
            var prior = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.SubjectId == subjectId && x.ReporterUserId == actorUserId && x.ClientRequestId == clientRequestId, ct);
            if (prior is not null)
            {
                return await DetailAsync(db, prior, role, ct); // a retried request returns the record it already made
            }
        }

        var row = new AdverseEventRow
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            ReporterRole = role,
            ReporterUserId = actorUserId,
            Origin = OriginOf(role),
            ReportedAt = now,
            ClientRequestId = clientRequestId,
            EventDescription = Clean(input.EventDescription)!,
            OnsetAt = input.OnsetAt,
            ResolvedAt = input.ResolvedAt,
            OutcomeAsStated = Clean(input.OutcomeAsStated),
            AdditionalDetails = Clean(input.AdditionalDetails),
            SuspectedMedicationsJson = JsonSerializer.Serialize(stored, Json),
            Category = EventCategory.Unclassified,
            Status = ReviewStatus.Submitted,
            LastActivityAt = now,
            Version = 1,
        };
        db.Events.Add(row);
        await db.SaveChangesAsync(ct);
        await AuditAsync(AuditActions.AdrRecordCreated, actorUserId, row, source, correlationId, new Dictionary<string, string> { ["role"] = role.ToString(), ["medications"] = stored.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);
        return await DetailAsync(db, row, role, ct);
    }

    public async Task<AdrOutcome<IReadOnlyList<AdverseEventSummaryDto>>> ListForSubjectAsync(Guid subjectId, ReporterRole viewerRole, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Events.AsNoTracking().Where(x => x.SubjectId == subjectId).OrderByDescending(x => x.ReportedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        return AdrOutcome.Ok<IReadOnlyList<AdverseEventSummaryDto>>([.. rows.Select(r => Summary(r, IsProfessional(viewerRole)))]);
    }

    public async Task<AdrOutcome<IReadOnlyList<AdverseEventSummaryDto>>> ListByStatusAsync(IReadOnlyCollection<ReviewStatus> statuses, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Events.AsNoTracking().Where(x => statuses.Contains(x.Status)).OrderByDescending(x => x.LastActivityAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        return AdrOutcome.Ok<IReadOnlyList<AdverseEventSummaryDto>>([.. rows.Select(r => Summary(r, true))]);
    }

    public async Task<Guid?> SubjectOfAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.SubjectId).FirstOrDefaultAsync(ct);
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> GetAsync(Guid id, ReporterRole viewerRole, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "event.not_found") : await DetailAsync(db, row, viewerRole, ct);
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> AddFollowUpAsync(Guid actorUserId, ReporterRole role, Guid id, string text, string source, string? correlationId, CancellationToken ct = default)
    {
        var bad = CheckText(text, MaxText, "text", true);
        if (bad is not null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "event.not_found");
        }

        if (role == ReporterRole.Patient && actorUserId != row.SubjectId)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Forbidden, "role.mismatch");
        }

        var now = clock.UtcNow;
        var from = row.Status;
        var entry = NewEntry(row, EntryKind.FollowUp, actorUserId, role, now, Clean(text), role == ReporterRole.Patient ? EntryVisibility.PatientVisible : EntryVisibility.ProfessionalOnly);
        db.Entries.Add(entry);
        if (role == ReporterRole.Patient && from == ReviewStatus.InformationRequested)
        {
            // The reporter answered: back to review. Recorded on the entry so the history shows who moved it and why.
            row.Status = ReviewStatus.InReview;
            entry.FromStatus = from;
            entry.ToStatus = ReviewStatus.InReview;
        }

        return await SaveAsync(db, row, now, AuditActions.AdrEntryAdded, actorUserId, role, entry, source, correlationId, ct);
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> AddReviewEntryAsync(Guid actorUserId, ReporterRole role, Guid id, EntryKind kind, string text, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!IsProfessional(role))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Forbidden, "role.not_professional");
        }

        if (kind is not (EntryKind.ReviewNote or EntryKind.InformationRequest or EntryKind.ProfessionalAssessment))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "entry.kind_invalid");
        }

        var bad = CheckText(text, MaxText, "text", true);
        if (bad is not null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "event.not_found");
        }

        var now = clock.UtcNow;
        var visibility = kind == EntryKind.InformationRequest ? EntryVisibility.PatientVisible : EntryVisibility.ProfessionalOnly;
        var entry = NewEntry(row, kind, actorUserId, role, now, Clean(text), visibility);
        db.Entries.Add(entry);
        if (kind == EntryKind.InformationRequest && row.Status != ReviewStatus.InformationRequested)
        {
            entry.FromStatus = row.Status;
            entry.ToStatus = ReviewStatus.InformationRequested;
            row.Status = ReviewStatus.InformationRequested;
        }

        return await SaveAsync(db, row, now, AuditActions.AdrEntryAdded, actorUserId, role, entry, source, correlationId, ct);
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> AddSupplementAsync(Guid actorUserId, ReporterRole role, Guid id, SupplementInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var e in new[] { CheckText(input.OutcomeAsStated, MaxOutcome, "outcome", false), CheckText(input.Note, MaxText, "note", false) })
        {
            if (e is not null)
            {
                errors.Add(e);
            }
        }

        if (input.OnsetAt is null && input.ResolvedAt is null && Clean(input.OutcomeAsStated) is null)
        {
            errors.Add("supplement.empty");
        }

        if (errors.Count > 0)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, string.Join(';', errors));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "event.not_found");
        }

        if (role == ReporterRole.Patient && actorUserId != row.SubjectId)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Forbidden, "role.mismatch");
        }

        var existing = await db.Entries.AsNoTracking().Where(x => x.EventId == id && x.Kind == EntryKind.Supplement).OrderBy(x => x.At).ToListAsync(ct);
        var eff = Effective(row, existing);
        var now = clock.UtcNow;
        // A supplement only fills what is still unknown. A value that is already recorded is never replaced (a correction is written as a follow-up).
        if ((input.OnsetAt is not null && eff.OnsetAt.Value is not null) || (input.ResolvedAt is not null && eff.ResolvedAt.Value is not null) || (Clean(input.OutcomeAsStated) is not null && eff.OutcomeAsStated.Value is not null))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Conflict, "field.already_recorded");
        }

        var onset = input.OnsetAt ?? eff.OnsetAt.Value;
        var resolved = input.ResolvedAt ?? eff.ResolvedAt.Value;
        if (input.OnsetAt > now || input.ResolvedAt > now)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "date.future");
        }

        if (onset is { } o && resolved is { } r && r < o)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "resolved.before_onset");
        }

        var entry = NewEntry(row, EntryKind.Supplement, actorUserId, role, now, Clean(input.Note), EntryVisibility.PatientVisible);
        entry.SupplementOnsetAt = input.OnsetAt;
        entry.SupplementResolvedAt = input.ResolvedAt;
        entry.SupplementOutcomeAsStated = Clean(input.OutcomeAsStated);
        db.Entries.Add(entry);
        return await SaveAsync(db, row, now, AuditActions.AdrEntryAdded, actorUserId, role, entry, source, correlationId, ct);
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> ClassifyAsync(Guid actorUserId, ReporterRole role, Guid id, EventCategory category, string reason, int expectedVersion, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!IsProfessional(role))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Forbidden, "role.not_professional");
        }

        if (!Enum.IsDefined(category))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "category.invalid");
        }

        var bad = CheckText(reason, MaxText, "reason", true);
        if (bad is not null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "event.not_found");
        }

        if (row.Version != expectedVersion)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Conflict, "version.mismatch");
        }

        if (row.Category == category)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Conflict, "category.unchanged");
        }

        var now = clock.UtcNow;
        var entry = NewEntry(row, EntryKind.Classification, actorUserId, role, now, Clean(reason), EntryVisibility.ProfessionalOnly);
        entry.FromCategory = row.Category;
        entry.ToCategory = category;
        row.Category = category;
        db.Entries.Add(entry);
        return await SaveAsync(db, row, now, AuditActions.AdrClassified, actorUserId, role, entry, source, correlationId, ct);
    }

    public async Task<AdrOutcome<AdverseEventDetailDto>> SetStatusAsync(Guid actorUserId, ReporterRole role, Guid id, ReviewStatus status, string? note, int expectedVersion, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!IsProfessional(role))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Forbidden, "role.not_professional");
        }

        var bad = CheckText(note, MaxText, "note", false);
        if (bad is not null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.NotFound, "event.not_found");
        }

        if (row.Version != expectedVersion)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Conflict, "version.mismatch");
        }

        if (!Enum.IsDefined(status) || !MoveAllowed(row.Status, status))
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Conflict, "status.move_not_allowed");
        }

        // Closing, and reopening a closed record, must say why.
        if ((status == ReviewStatus.Closed || row.Status == ReviewStatus.Closed) && Clean(note) is null)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Validation, "note.required");
        }

        var now = clock.UtcNow;
        var entry = NewEntry(row, EntryKind.StatusChange, actorUserId, role, now, Clean(note), EntryVisibility.ProfessionalOnly);
        entry.FromStatus = row.Status;
        entry.ToStatus = status;
        row.Status = status;
        db.Entries.Add(entry);
        return await SaveAsync(db, row, now, AuditActions.AdrStatusChanged, actorUserId, role, entry, source, correlationId, ct);
    }

    public async Task<AdrOutcome<AdverseEventAssistanceDto>> AssistAsync(Guid actorUserId, ReporterRole role, Guid id, string source, string? correlationId, CancellationToken ct = default)
    {
        if (!IsProfessional(role))
        {
            return AdrOutcome.Fail<AdverseEventAssistanceDto>(AdrError.Forbidden, "role.not_professional");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return AdrOutcome.Fail<AdverseEventAssistanceDto>(AdrError.NotFound, "event.not_found");
        }

        var entries = await db.Entries.AsNoTracking().Where(x => x.EventId == id).OrderBy(x => x.At).ToListAsync(ct);
        var meds = await ResolveMedicationsAsync(row, ct);
        var now = clock.UtcNow;
        var dto = AdverseEventAssistance.Build(ToDto(row, entries, meds, true), entries.Count, entries.Count == 0 ? null : entries[^1].At, now);
        await AuditAsync(AuditActions.AdrAssistanceGenerated, actorUserId, row, source, correlationId, new Dictionary<string, string> { ["role"] = role.ToString(), ["origin"] = dto.Origin }, ct);
        return AdrOutcome.Ok(dto);
    }

    // ---------------------------------------------------------------- helpers

    private static AdverseEventEntryRow NewEntry(AdverseEventRow row, EntryKind kind, Guid author, ReporterRole role, DateTimeOffset at, string? text, EntryVisibility visibility) => new()
    {
        Id = Guid.NewGuid(),
        EventId = row.Id,
        Kind = kind,
        AuthorUserId = author,
        AuthorRole = role,
        At = at,
        Text = text,
        Visibility = visibility,
    };

    private async Task<AdrOutcome<AdverseEventDetailDto>> SaveAsync(AdrDbContext db, AdverseEventRow row, DateTimeOffset now, string auditAction, Guid actor, ReporterRole role, AdverseEventEntryRow entry, string source, string? correlationId, CancellationToken ct)
    {
        row.LastActivityAt = now;
        row.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdrOutcome.Fail<AdverseEventDetailDto>(AdrError.Conflict, "version.mismatch");
        }

        var meta = new Dictionary<string, string> { ["role"] = role.ToString(), ["kind"] = entry.Kind.ToString() };
        if (entry.ToStatus is { } ts)
        {
            meta["toStatus"] = ts.ToString();
        }

        if (entry.ToCategory is { } tc)
        {
            meta["toCategory"] = tc.ToString();
        }

        await AuditAsync(auditAction, actor, row, source, correlationId, meta, ct);
        return await DetailAsync(db, row, role, ct);
    }

    private async Task AuditAsync(string action, Guid actor, AdverseEventRow row, string source, string? correlationId, IReadOnlyDictionary<string, string> meta, CancellationToken ct) =>
        await audit.WriteAsync(new AuditEvent(action, AuditResult.Success, actor, "adverse-event", row.Id.ToString(), row.SubjectId, source, correlationId, null, meta), ct);

    private static AdverseEventSummaryDto Summary(AdverseEventRow r, bool professional)
    {
        var excerpt = r.EventDescription.Length <= 120 ? r.EventDescription : r.EventDescription[..120] + "…";
        return new AdverseEventSummaryDto(r.Id, r.SubjectId, r.ReporterRole, r.Origin, r.ReportedAt, professional ? r.Category : null, r.Status, r.LastActivityAt, excerpt);
    }

    private async Task<AdrOutcome<AdverseEventDetailDto>> DetailAsync(AdrDbContext db, AdverseEventRow row, ReporterRole viewer, CancellationToken ct)
    {
        var professional = IsProfessional(viewer);
        var entries = await db.Entries.AsNoTracking().Where(x => x.EventId == row.Id).OrderBy(x => x.At).ToListAsync(ct);
        var meds = await ResolveMedicationsAsync(row, ct);
        var dto = ToDto(row, entries, meds, professional);
        var visible = entries.Where(e => professional || e.Visibility == EntryVisibility.PatientVisible).Select(e => new AdverseEventEntryDto(
            e.Id, e.Kind, e.AuthorRole, professional ? e.AuthorUserId : null, e.At, e.Text, e.Visibility,
            e.AuthorRole == ReporterRole.Patient ? "PatientReported" : "ProfessionalReview",
            e.FromStatus, e.ToStatus, professional ? e.FromCategory : null, professional ? e.ToCategory : null,
            e.SupplementOnsetAt, e.SupplementResolvedAt, e.SupplementOutcomeAsStated)).ToList();
        return AdrOutcome.Ok(new AdverseEventDetailDto(dto, visible));
    }

    private async Task<List<SuspectedMedicationDto>> ResolveMedicationsAsync(AdverseEventRow row, CancellationToken ct)
    {
        var stored = JsonSerializer.Deserialize<List<StoredMedication>>(row.SuspectedMedicationsJson, Json) ?? [];
        var result = new List<SuspectedMedicationDto>();
        foreach (var m in stored)
        {
            if (m.PatientMedicationId is not { } id)
            {
                result.Add(new SuspectedMedicationDto(null, m.NameAsReported, null, MedicationReferenceState.ReportedNameOnly));
                continue;
            }

            string? display = null;
            try
            {
                var found = await medications.GetAsync(row.SubjectId, id, ct);
                display = found.Succeeded ? found.Value!.DisplayName : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                display = null; // unavailable now; the name given at the time stays
            }

            result.Add(new SuspectedMedicationDto(id, m.NameAsReported, display, display is null ? MedicationReferenceState.LinkedUnavailable : MedicationReferenceState.Linked));
        }

        return result;
    }

    internal static AdverseEventEffective Effective(AdverseEventRow row, IReadOnlyList<AdverseEventEntryRow> entries)
    {
        var sup = entries.Where(e => e.Kind == EntryKind.Supplement).OrderBy(e => e.At).ToList();
        static string By(AdverseEventEntryRow e) => e.AuthorRole == ReporterRole.Patient ? "SupplementByPatient" : "SupplementByProfessional";
        FieldValue<T?> Pick<T>(T? original, Func<AdverseEventEntryRow, T?> get)
        {
            if (original is not null)
            {
                return new FieldValue<T?>(original, "Original", null);
            }

            var s = sup.FirstOrDefault(e => get(e) is not null);
            return s is null ? new FieldValue<T?>(default, "NotRecorded", null) : new FieldValue<T?>(get(s), By(s), s.Id);
        }

        return new AdverseEventEffective(
            Pick<DateTimeOffset?>(row.OnsetAt, e => e.SupplementOnsetAt),
            Pick<DateTimeOffset?>(row.ResolvedAt, e => e.SupplementResolvedAt),
            Pick<string?>(row.OutcomeAsStated, e => e.SupplementOutcomeAsStated));
    }

    internal static IReadOnlyList<string> Missing(AdverseEventRow row, AdverseEventEffective eff, IReadOnlyList<SuspectedMedicationDto> meds)
    {
        var m = new List<string>();
        if (eff.OnsetAt.Value is null)
        {
            m.Add("onset.unknown");
        }

        if (eff.ResolvedAt.Value is null)
        {
            m.Add("resolution.unknown_or_ongoing");
        }

        if (eff.OutcomeAsStated.Value is null)
        {
            m.Add("outcome.not_stated");
        }

        if (meds.Count == 0)
        {
            m.Add("medication.none_named");
        }

        if (meds.Any(x => x.State == MedicationReferenceState.ReportedNameOnly))
        {
            m.Add("medication.not_linked");
        }

        if (meds.Any(x => x.State == MedicationReferenceState.LinkedUnavailable))
        {
            m.Add("medication.reference_unavailable");
        }

        if (row.AdditionalDetails is null)
        {
            m.Add("details.none");
        }

        return m;
    }

    private static AdverseEventDto ToDto(AdverseEventRow r, IReadOnlyList<AdverseEventEntryRow> entries, IReadOnlyList<SuspectedMedicationDto> meds, bool professional)
    {
        var eff = Effective(r, entries);
        return new AdverseEventDto(r.Id, r.SubjectId, r.ReporterRole, r.Origin, r.ReportedAt,
            new AdverseEventOriginal(r.EventDescription, r.OnsetAt, r.ResolvedAt, r.OutcomeAsStated, r.AdditionalDetails), eff, meds,
            professional ? r.Category : null, r.Status, Missing(r, eff, meds), r.LastActivityAt, r.Version,
            AdrNotices.Signal, AdrNotices.InternalRecord, AdrNotices.ExternalNotSubmitted, WordingStatus.AwaitingClinicalReview);
    }
}

/// <summary>The fixed-template assistance. Pure: it only rearranges what is already recorded and never adds a claim.</summary>
public static class AdverseEventAssistance
{
    public const string OriginLabel = "Template:no-model";

    public static AdverseEventAssistanceDto Build(AdverseEventDto e, int entryCount, DateTimeOffset? lastEntryAt, DateTimeOffset now)
    {
        string Date(DateTimeOffset? d) => d is null ? "not recorded" : d.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<AssistanceLine>
        {
            new($"Recorded by a {e.ReporterRole} ({(e.Origin == ReportOrigin.PatientReported ? "the patient's own account" : "entered by a professional")}) on {Date(e.ReportedAt)}.", "report"),
            new($"Event, as written: \"{e.Original.EventDescription}\"", "original.eventDescription"),
            new($"Onset: {Date(e.Effective.OnsetAt.Value)} ({e.Effective.OnsetAt.Provenance}).", "effective.onsetAt"),
            new($"End or resolution: {Date(e.Effective.ResolvedAt.Value)} ({e.Effective.ResolvedAt.Provenance}).", "effective.resolvedAt"),
            new($"Outcome, as stated: {(e.Effective.OutcomeAsStated.Value is { } o ? "\"" + o + "\"" : "not stated")} ({e.Effective.OutcomeAsStated.Provenance}).", "effective.outcomeAsStated"),
        };
        if (e.SuspectedMedications.Count == 0)
        {
            lines.Add(new("No medicine was named by the reporter.", "suspectedMedications"));
        }

        foreach (var m in e.SuspectedMedications)
        {
            var name = m.DisplayName ?? m.NameAsReported ?? "unnamed";
            var how = m.State switch
            {
                MedicationReferenceState.Linked => "linked to one of the patient's own medication records",
                MedicationReferenceState.LinkedUnavailable => "was linked to a medication record that cannot be found now",
                _ => "a name as the reporter wrote it; it was not matched to anything",
            };
            lines.Add(new($"Medicine named: {name} — {how}.", "suspectedMedications"));
        }

        lines.Add(new($"Review status: {e.Status}. Category: {(e.Category is null or EventCategory.Unclassified ? "not classified" : e.Category.ToString())}.", "review"));
        lines.Add(new($"{entryCount} entr{(entryCount == 1 ? "y" : "ies")} after the original report; latest: {Date(lastEntryAt)}.", "history"));
        var uncertainty = new List<string>
        {
            "This record does not show whether any medicine caused the event.",
            "Anything listed as not recorded is unknown. It is not evidence that the thing did not happen.",
        };
        if (e.Origin == ReportOrigin.PatientReported)
        {
            uncertainty.Add("The patient's account has not been verified.");
        }

        if (e.SuspectedMedications.Any(x => x.State == MedicationReferenceState.LinkedUnavailable))
        {
            uncertainty.Add("At least one linked medication record is unavailable now, so its current details are unknown.");
        }

        return new AdverseEventAssistanceDto(OriginLabel, "Template summary of the record — not a conclusion", now, lines, e.MissingInformation, uncertainty, AdrNotices.Assistance, WordingStatus.AwaitingClinicalReview);
    }
}
