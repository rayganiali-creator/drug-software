using System.Text.Json;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Pharmacies.Contracts;
using MedSmarter.Modules.Pharmacies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Pharmacies;

/// <summary>
/// The rules of the internal pharmacy safety records: the original report is written once and never edited; later information is an append-only entry; unknown details stay
/// unknown; a near miss is kept apart from an error that reached the patient; a product concern is linked to a ProductTrace record only when the caller supplied one (never
/// guessed), otherwise it is stored as Unmatched with exactly what was typed; a proposal for a manufacturer report is a human decision note and sends nothing.
/// Audit lines carry ids and enum names only, never report text.
/// </summary>
public sealed class PharmacySafetyService(IDbContextFactory<PharmacyDbContext> factory, IClock clock, IAuditWriter audit) : IPharmacySafetyService
{
    public const int MaxText = 2000;
    public const int MaxShort = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly (SafetyStatus From, SafetyStatus To)[] Moves =
    [
        (SafetyStatus.Submitted, SafetyStatus.InReview), (SafetyStatus.Submitted, SafetyStatus.Closed),
        (SafetyStatus.InReview, SafetyStatus.FollowUpRequired), (SafetyStatus.InReview, SafetyStatus.Closed),
        (SafetyStatus.FollowUpRequired, SafetyStatus.InReview), (SafetyStatus.FollowUpRequired, SafetyStatus.Closed),
        (SafetyStatus.Closed, SafetyStatus.InReview),
    ];

    public static bool MoveAllowed(SafetyStatus from, SafetyStatus to) => Moves.Any(m => m.From == from && m.To == to);

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static string? CheckText(string? s, int max, string field, bool required)
    {
        var t = Clean(s);
        if (t is null)
        {
            return required ? $"{field}.required" : null;
        }

        return t.Length > max ? $"{field}.too_long" : t.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ? $"{field}.invalid_characters" : null;
    }

    private static List<string> Collect(params string?[] checks) => [.. checks.Where(c => c is not null).Select(c => c!)];

    // ---------------------------------------------------------------- creation

    public async Task<PharmacyOutcome<SafetyEventDetailDto>> CreateEventAsync(Guid actorUserId, PharmacyActorRole role, Guid organizationId, CreateSafetyEventInput input, string source, string? correlationId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var errors = Collect(CheckText(input.Description, MaxText, "description", true), CheckText(input.MedicineAsReported, 200, "medicine", false),
            CheckText(input.HowDetectedOrIntercepted, MaxShort, "detected", false), CheckText(input.ImmediateActionTaken, MaxShort, "action", false));
        if (!Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.ReachedPatient))
        {
            errors.Add("enum.invalid");
        }
        else if (input.Kind == SafetyEventKind.NearMiss && input.ReachedPatient == ReachedPatient.Yes)
        {
            errors.Add("near_miss.reached_patient_conflict"); // a near miss, by definition, did not reach the patient
        }
        else if (input.Kind == SafetyEventKind.DispensingError && input.ReachedPatient == ReachedPatient.No)
        {
            errors.Add("dispensing_error.not_reached_patient_use_near_miss");
        }

        var examples = (input.ErrorExamples ?? []).Distinct().ToList();
        if (examples.Count > 9 || examples.Any(x => !Enum.IsDefined(x)))
        {
            errors.Add("examples.invalid");
        }

        if (input.OccurredOn is { } d && d > DateOnly.FromDateTime(now.UtcDateTime))
        {
            errors.Add("occurred.future");
        }

        var cid = Clean(input.ClientRequestId);
        if (cid is { Length: > 64 })
        {
            errors.Add("clientRequestId.too_long");
        }

        if (errors.Count > 0)
        {
            return PharmacyOutcome.Fail<SafetyEventDetailDto>(PharmacyError.Validation, string.Join(';', errors.Distinct()));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (cid is not null)
        {
            var prior = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.ReporterUserId == actorUserId && x.ClientRequestId == cid, ct);
            if (prior is not null)
            {
                return PharmacyOutcome.Ok(await EventDetailAsync(db, prior, ct)); // a retry returns the record already made
            }
        }

        if (input.LinkedProductConcernId is { } cl && !await db.Concerns.AnyAsync(x => x.Id == cl && x.OrganizationId == organizationId, ct))
        {
            return PharmacyOutcome.Fail<SafetyEventDetailDto>(PharmacyError.Validation, "link.concern_unavailable");
        }

        var row = new SafetyEventRow
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, ReporterUserId = actorUserId, ReporterRole = role, ReportedAt = now, ClientRequestId = cid,
            Kind = input.Kind, ReachedPatient = input.ReachedPatient, ErrorExamplesJson = JsonSerializer.Serialize(examples, Json), Description = Clean(input.Description)!,
            OccurredOn = input.OccurredOn, MedicineAsReported = Clean(input.MedicineAsReported), HowDetectedOrIntercepted = Clean(input.HowDetectedOrIntercepted),
            ImmediateActionTaken = Clean(input.ImmediateActionTaken), LinkedAdverseEventId = input.LinkedAdverseEventId, LinkedProductConcernId = input.LinkedProductConcernId,
            Status = SafetyStatus.Submitted, LastActivityAt = now, Version = 1,
        };
        db.Events.Add(row);
        await db.SaveChangesAsync(ct);
        await AuditAsync(AuditActions.PharmacySafetyEventCreated, actorUserId, "pharmacy-safety-event", row.Id, organizationId, source, correlationId,
            new Dictionary<string, string> { ["kind"] = row.Kind.ToString(), ["reached"] = row.ReachedPatient.ToString(), ["role"] = role.ToString(), ["org"] = organizationId.ToString() }, ct);
        return PharmacyOutcome.Ok(await EventDetailAsync(db, row, ct));
    }

    public async Task<PharmacyOutcome<ProductConcernDetailDto>> CreateConcernAsync(Guid actorUserId, PharmacyActorRole role, Guid organizationId, CreateProductConcernInput input, ProductSnapshot? snapshot, string source, string? correlationId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var a = input.AsReported;
        var errors = Collect(CheckText(input.Description, MaxText, "description", true), CheckText(a?.ProductName, 200, "product", false), CheckText(a?.ManufacturerName, 200, "manufacturer", false),
            CheckText(a?.BatchNumber, 64, "batch", false), CheckText(a?.Gtin, 64, "gtin", false));
        if (!Enum.IsDefined(input.Type))
        {
            errors.Add("type.invalid");
        }

        if (input.ObservedOn is { } d && d > DateOnly.FromDateTime(now.UtcDateTime))
        {
            errors.Add("observed.future");
        }

        var cid = Clean(input.ClientRequestId);
        if (cid is { Length: > 64 })
        {
            errors.Add("clientRequestId.too_long");
        }

        if (input.ProductRecord is not null && snapshot is null)
        {
            errors.Add("product_record.unavailable"); // a link was asked for but could not be verified: never fall back to a different record
        }

        if (errors.Count > 0)
        {
            return PharmacyOutcome.Fail<ProductConcernDetailDto>(PharmacyError.Validation, string.Join(';', errors.Distinct()));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (cid is not null)
        {
            var prior = await db.Concerns.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.ReporterUserId == actorUserId && x.ClientRequestId == cid, ct);
            if (prior is not null)
            {
                return PharmacyOutcome.Ok(await ConcernDetailAsync(db, prior, ct));
            }
        }

        if (input.LinkedSafetyEventId is { } sl && !await db.Events.AnyAsync(x => x.Id == sl && x.OrganizationId == organizationId, ct))
        {
            return PharmacyOutcome.Fail<ProductConcernDetailDto>(PharmacyError.Validation, "link.event_unavailable");
        }

        var row = new ProductConcernRow
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, ReporterUserId = actorUserId, ReporterRole = role, ReportedAt = now, ClientRequestId = cid,
            Type = input.Type, Description = Clean(input.Description)!, ObservedOn = input.ObservedOn,
            AsReportedProductName = Clean(a?.ProductName), AsReportedBatchNumber = Clean(a?.BatchNumber), AsReportedExpiryDate = a?.ExpiryDate, AsReportedManufacturerName = Clean(a?.ManufacturerName), AsReportedGtin = Clean(a?.Gtin),
            LinkedAdverseEventId = input.LinkedAdverseEventId, LinkedSafetyEventId = input.LinkedSafetyEventId, Status = SafetyStatus.Submitted, LastActivityAt = now, Version = 1,
        };
        if (snapshot is not null)
        {
            row.LinkState = ProductLinkState.LinkedToProductRecord;
            row.LinkedSubjectId = snapshot.SubjectId;
            row.LinkedProductRecordId = snapshot.ProductRecordId;
            row.LinkedRecordVersion = snapshot.RecordVersion;
            row.LinkedVerificationAtLinking = snapshot.VerificationAtLinking;
            row.LinkedProductName = snapshot.Identifiers.ProductName;
            row.LinkedBatchNumber = snapshot.Identifiers.BatchNumber;
            row.LinkedExpiryDate = snapshot.Identifiers.ExpiryDate;
            row.LinkedManufacturerName = snapshot.Identifiers.ManufacturerName;
            row.LinkedGtin = snapshot.Identifiers.Gtin;
        }
        else
        {
            row.LinkState = ProductLinkState.Unmatched;
        }

        db.Concerns.Add(row);
        await db.SaveChangesAsync(ct);
        await AuditAsync(AuditActions.ProductConcernCreated, actorUserId, "product-concern", row.Id, organizationId, source, correlationId,
            new Dictionary<string, string> { ["type"] = row.Type.ToString(), ["link"] = row.LinkState.ToString(), ["role"] = role.ToString(), ["org"] = organizationId.ToString() }, ct);
        return PharmacyOutcome.Ok(await ConcernDetailAsync(db, row, ct));
    }

    // ---------------------------------------------------------------- reads

    public async Task<PharmacyOutcome<IReadOnlyList<SafetySummaryDto>>> ListAsync(Guid organizationId, SafetyRecordKind kind, IReadOnlyCollection<SafetyStatus>? statuses, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var n = Math.Clamp(take, 1, 200);
        static string Cut(string s) => s.Length <= 120 ? s : s[..120] + "…";
        if (kind == SafetyRecordKind.SafetyEvent)
        {
            var q = db.Events.AsNoTracking().Where(x => x.OrganizationId == organizationId);
            if (statuses is { Count: > 0 })
            {
                q = q.Where(x => statuses.Contains(x.Status));
            }

            var rows = await q.OrderByDescending(x => x.LastActivityAt).Take(n).ToListAsync(ct);
            return PharmacyOutcome.Ok<IReadOnlyList<SafetySummaryDto>>([.. rows.Select(r => new SafetySummaryDto(r.Id, kind, r.Kind.ToString(), r.Status, r.ReportedAt, r.LastActivityAt, Cut(r.Description)))]);
        }

        var cq = db.Concerns.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (statuses is { Count: > 0 })
        {
            cq = cq.Where(x => statuses.Contains(x.Status));
        }

        var crows = await cq.OrderByDescending(x => x.LastActivityAt).Take(n).ToListAsync(ct);
        return PharmacyOutcome.Ok<IReadOnlyList<SafetySummaryDto>>([.. crows.Select(r => new SafetySummaryDto(r.Id, kind, r.Type.ToString(), r.Status, r.ReportedAt, r.LastActivityAt, Cut(r.Description)))]);
    }

    public async Task<Guid?> OrganizationOfAsync(SafetyRecordKind kind, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return kind == SafetyRecordKind.SafetyEvent
            ? await db.Events.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.OrganizationId).FirstOrDefaultAsync(ct)
            : await db.Concerns.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.OrganizationId).FirstOrDefaultAsync(ct);
    }

    public async Task<PharmacyOutcome<SafetyEventDetailDto>> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? PharmacyOutcome.Fail<SafetyEventDetailDto>(PharmacyError.NotFound, "record.not_found") : PharmacyOutcome.Ok(await EventDetailAsync(db, row, ct));
    }

    public async Task<PharmacyOutcome<ProductConcernDetailDto>> GetConcernAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Concerns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? PharmacyOutcome.Fail<ProductConcernDetailDto>(PharmacyError.NotFound, "record.not_found") : PharmacyOutcome.Ok(await ConcernDetailAsync(db, row, ct));
    }

    // ---------------------------------------------------------------- review actions

    public async Task<PharmacyOutcome<bool>> AddEntryAsync(Guid actorUserId, PharmacyActorRole role, SafetyRecordKind kind, Guid id, SafetyEntryKind entryKind, string text, string source, string? correlationId, CancellationToken ct = default)
    {
        if (entryKind is not (SafetyEntryKind.ReviewNote or SafetyEntryKind.FollowUp))
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Validation, "entry.kind_invalid");
        }

        var bad = CheckText(text, MaxText, "text", true);
        if (bad is not null)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var org = await TouchAsync(db, kind, id, null, ct);
        if (org.Error != PharmacyError.None)
        {
            return PharmacyOutcome.Fail<bool>(org.Error, org.Detail);
        }

        db.Entries.Add(NewEntry(kind, id, entryKind, actorUserId, role, clock.UtcNow, Clean(text), null, null));
        return await SaveAsync(db, org.Org, kind, id, AuditActions.PharmacySafetyEntryAdded, actorUserId, role, source, correlationId, new Dictionary<string, string> { ["entry"] = entryKind.ToString() }, ct);
    }

    public async Task<PharmacyOutcome<bool>> SetStatusAsync(Guid actorUserId, PharmacyActorRole role, SafetyRecordKind kind, Guid id, SafetyStatus status, string? note, int expectedVersion, string source, string? correlationId, CancellationToken ct = default)
    {
        var bad = CheckText(note, MaxText, "note", false);
        if (bad is not null)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var cur = await TouchAsync(db, kind, id, expectedVersion, ct);
        if (cur.Error != PharmacyError.None)
        {
            return PharmacyOutcome.Fail<bool>(cur.Error, cur.Detail);
        }

        if (!Enum.IsDefined(status) || !MoveAllowed(cur.Status, status))
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Conflict, "status.move_not_allowed");
        }

        if ((status == SafetyStatus.Closed || cur.Status == SafetyStatus.Closed) && Clean(note) is null)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Validation, "note.required"); // closing and reopening must say why
        }

        db.Entries.Add(NewEntry(kind, id, SafetyEntryKind.StatusChange, actorUserId, role, clock.UtcNow, Clean(note), cur.Status, status));
        cur.Apply(status);
        return await SaveAsync(db, cur.Org, kind, id, AuditActions.PharmacySafetyStatusChanged, actorUserId, role, source, correlationId, new Dictionary<string, string> { ["toStatus"] = status.ToString() }, ct);
    }

    public async Task<PharmacyOutcome<bool>> ProposeManufacturerReportAsync(Guid actorUserId, PharmacyActorRole role, Guid concernId, string note, int expectedVersion, string source, string? correlationId, CancellationToken ct = default)
    {
        var bad = CheckText(note, MaxText, "note", true);
        if (bad is not null)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Validation, bad);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Concerns.FirstOrDefaultAsync(x => x.Id == concernId, ct);
        if (row is null)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.NotFound, "record.not_found");
        }

        if (row.Version != expectedVersion)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Conflict, "version.mismatch");
        }

        if (row.Status is SafetyStatus.Closed or SafetyStatus.Submitted)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Conflict, "proposal.needs_review"); // a proposal follows a review, not a first report
        }

        if (row.ManufacturerReportProposed)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Conflict, "proposal.already_recorded");
        }

        var now = clock.UtcNow;
        row.ManufacturerReportProposed = true;
        row.ManufacturerReportProposedAt = now;
        db.Entries.Add(NewEntry(SafetyRecordKind.ProductConcern, concernId, SafetyEntryKind.ManufacturerReportProposal, actorUserId, role, now, Clean(note), null, null));
        return await SaveAsync(db, row.OrganizationId, SafetyRecordKind.ProductConcern, concernId, AuditActions.ProductConcernReportProposed, actorUserId, role, source, correlationId, new Dictionary<string, string>(), ct, row, now);
    }

    // ---------------------------------------------------------------- helpers

    private sealed class Touched(PharmacyError error, string? detail, Guid org, SafetyStatus status, Action<SafetyStatus> apply)
    {
        public PharmacyError Error { get; } = error;
        public string? Detail { get; } = detail;
        public Guid Org { get; } = org;
        public SafetyStatus Status { get; } = status;
        public void Apply(SafetyStatus s) => apply(s);
    }

    /// <summary>Loads the tracked record, checks its version when one is given, and exposes the status move. The row is saved by <see cref="SaveAsync"/>.</summary>
    private static async Task<Touched> TouchAsync(PharmacyDbContext db, SafetyRecordKind kind, Guid id, int? expectedVersion, CancellationToken ct)
    {
        if (kind == SafetyRecordKind.SafetyEvent)
        {
            var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null)
            {
                return new(PharmacyError.NotFound, "record.not_found", Guid.Empty, default, _ => { });
            }

            return expectedVersion is { } v && v != e.Version ? new(PharmacyError.Conflict, "version.mismatch", e.OrganizationId, e.Status, _ => { }) : new(PharmacyError.None, null, e.OrganizationId, e.Status, s => e.Status = s);
        }

        var c = await db.Concerns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return new(PharmacyError.NotFound, "record.not_found", Guid.Empty, default, _ => { });
        }

        return expectedVersion is { } v2 && v2 != c.Version ? new(PharmacyError.Conflict, "version.mismatch", c.OrganizationId, c.Status, _ => { }) : new(PharmacyError.None, null, c.OrganizationId, c.Status, s => c.Status = s);
    }

    private async Task<PharmacyOutcome<bool>> SaveAsync(PharmacyDbContext db, Guid org, SafetyRecordKind kind, Guid id, string action, Guid actor, PharmacyActorRole role, string source, string? correlationId, Dictionary<string, string> meta, CancellationToken ct, ProductConcernRow? concern = null, DateTimeOffset? at = null)
    {
        var now = at ?? clock.UtcNow;
        if (kind == SafetyRecordKind.SafetyEvent)
        {
            var e = await db.Events.FirstAsync(x => x.Id == id, ct);
            e.LastActivityAt = now;
            e.Version++;
        }
        else
        {
            var c = concern ?? await db.Concerns.FirstAsync(x => x.Id == id, ct);
            c.LastActivityAt = now;
            c.Version++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PharmacyOutcome.Fail<bool>(PharmacyError.Conflict, "version.mismatch");
        }

        meta["role"] = role.ToString();
        meta["org"] = org.ToString();
        await AuditAsync(action, actor, kind == SafetyRecordKind.SafetyEvent ? "pharmacy-safety-event" : "product-concern", id, org, source, correlationId, meta, ct);
        return PharmacyOutcome.Ok(true);
    }

    private static SafetyEntryRow NewEntry(SafetyRecordKind kind, Guid id, SafetyEntryKind entryKind, Guid author, PharmacyActorRole role, DateTimeOffset at, string? text, SafetyStatus? from, SafetyStatus? to) => new()
    {
        Id = Guid.NewGuid(), RecordKind = kind, RecordId = id, Kind = entryKind, AuthorUserId = author, AuthorRole = role, At = at, Text = text, FromStatus = from, ToStatus = to,
    };

    private async Task AuditAsync(string action, Guid actor, string resourceType, Guid id, Guid org, string source, string? correlationId, IReadOnlyDictionary<string, string> meta, CancellationToken ct) =>
        await audit.WriteAsync(new AuditEvent(action, AuditResult.Success, actor, resourceType, id.ToString(), null, source, correlationId, null, meta), ct);

    private static async Task<IReadOnlyList<SafetyEntryDto>> EntriesAsync(PharmacyDbContext db, SafetyRecordKind kind, Guid id, CancellationToken ct) =>
        [.. (await db.Entries.AsNoTracking().Where(x => x.RecordKind == kind && x.RecordId == id).OrderBy(x => x.At).ToListAsync(ct)).Select(e => new SafetyEntryDto(e.Id, e.Kind, e.AuthorRole, e.AuthorUserId, e.At, e.Text, e.FromStatus, e.ToStatus))];

    private static async Task<SafetyEventDetailDto> EventDetailAsync(PharmacyDbContext db, SafetyEventRow r, CancellationToken ct)
    {
        var examples = JsonSerializer.Deserialize<List<DispensingErrorExample>>(r.ErrorExamplesJson, Json) ?? [];
        var missing = new List<string>();
        if (r.OccurredOn is null)
        {
            missing.Add("occurred_on.unknown");
        }

        if (r.ReachedPatient == ReachedPatient.Unknown)
        {
            missing.Add("reached_patient.unknown");
        }

        if (examples.Count == 0)
        {
            missing.Add("error_examples.not_stated");
        }

        if (r.MedicineAsReported is null)
        {
            missing.Add("medicine.not_stated");
        }

        if (r.HowDetectedOrIntercepted is null)
        {
            missing.Add(r.Kind == SafetyEventKind.NearMiss ? "how_intercepted.not_stated" : "how_detected.not_stated");
        }

        if (r.ImmediateActionTaken is null)
        {
            missing.Add("immediate_action.not_stated");
        }

        var dto = new SafetyEventDto(r.Id, r.OrganizationId, r.Kind, r.ReachedPatient, examples, r.Description, r.OccurredOn, r.MedicineAsReported, r.HowDetectedOrIntercepted, r.ImmediateActionTaken,
            r.ReporterRole, r.ReportedAt, r.Status, missing, r.LinkedAdverseEventId, r.LinkedProductConcernId, r.LastActivityAt, r.Version, PharmacyNotices.Event, PharmacyNotices.ExternalNotSubmitted, PharmacyNotices.PersianAwaitingReview);
        return new SafetyEventDetailDto(dto, await EntriesAsync(db, SafetyRecordKind.SafetyEvent, r.Id, ct));
    }

    internal static IReadOnlyList<string> ConcernFlags(ProductConcernRow r)
    {
        var f = new List<string>();
        if (r.LinkState == ProductLinkState.Unmatched)
        {
            f.Add("product_record.not_linked");
            if (r.AsReportedProductName is null)
            {
                f.Add("product.name_missing");
            }

            if (r.AsReportedBatchNumber is null)
            {
                f.Add("batch.missing");
            }

            if (r.AsReportedExpiryDate is null)
            {
                f.Add("expiry.missing");
            }

            if (r.AsReportedManufacturerName is null)
            {
                f.Add("manufacturer.missing");
            }
        }
        else
        {
            // Both sets are kept; when they differ nothing is chosen for the reporter.
            if (r.AsReportedBatchNumber is not null && !string.Equals(r.AsReportedBatchNumber, r.LinkedBatchNumber, StringComparison.OrdinalIgnoreCase))
            {
                f.Add("batch.differs_from_linked_record");
            }

            if (r.AsReportedProductName is not null && !string.Equals(r.AsReportedProductName, r.LinkedProductName, StringComparison.OrdinalIgnoreCase))
            {
                f.Add("product.differs_from_linked_record");
            }

            if (r.AsReportedExpiryDate is not null && r.AsReportedExpiryDate != r.LinkedExpiryDate)
            {
                f.Add("expiry.differs_from_linked_record");
            }
        }

        return f;
    }

    private static async Task<ProductConcernDetailDto> ConcernDetailAsync(PharmacyDbContext db, ProductConcernRow r, CancellationToken ct)
    {
        var linked = r.LinkState == ProductLinkState.LinkedToProductRecord ? new ProductIdentifiers(r.LinkedProductName, r.LinkedBatchNumber, r.LinkedExpiryDate, r.LinkedManufacturerName, r.LinkedGtin) : null;
        var typed = r.AsReportedProductName is null && r.AsReportedBatchNumber is null && r.AsReportedExpiryDate is null && r.AsReportedManufacturerName is null && r.AsReportedGtin is null
            ? null : new ProductIdentifiers(r.AsReportedProductName, r.AsReportedBatchNumber, r.AsReportedExpiryDate, r.AsReportedManufacturerName, r.AsReportedGtin);
        var dto = new ProductConcernDto(r.Id, r.OrganizationId, r.Type, "ProvisionalNotSourceDefined", r.Description, r.ObservedOn, r.LinkState, linked, r.LinkedRecordVersion, r.LinkedVerificationAtLinking, typed,
            ConcernFlags(r), r.ReporterRole, r.ReportedAt, r.Status, r.LinkedAdverseEventId, r.LinkedSafetyEventId, r.ManufacturerReportProposed, r.ManufacturerReportProposedAt, r.LastActivityAt, r.Version,
            PharmacyNotices.Concern, PharmacyNotices.ExternalNotSubmitted, PharmacyNotices.PersianAwaitingReview);
        return new ProductConcernDetailDto(dto, await EntriesAsync(db, SafetyRecordKind.ProductConcern, r.Id, ct));
    }
}
