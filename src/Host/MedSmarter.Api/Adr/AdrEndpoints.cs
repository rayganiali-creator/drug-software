using System.Text.Json;
using MedSmarter.Api.Security;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Api.Adr;

public sealed record AdrTextBody(string? Text);

public sealed record AdrEntryBody(EntryKind Kind, string? Text);

public sealed record AdrClassifyBody(EventCategory Category, string? Reason, int ExpectedVersion);

public sealed record AdrStatusBody(ReviewStatus Status, string? Note, int ExpectedVersion);

/// <summary>
/// Internal adverse-event records. Every route is default-deny: the catalog permission (RBAC), then ownership or an active care relationship plus consent for the
/// "adr" scope, evaluated against the subject the RECORD belongs to (never a subject named in a body). "No such record", "not your patient" and "no consent" all
/// answer with the same 403. Nothing here sends a record to any external party.
/// </summary>
public static class AdrEndpoints
{
    public const int QueueDefaultLimit = 50;
    public const int QueueMaxLimit = 100;
    public const string QueueTruncatedHeader = "X-Result-Truncated";
    public const string QueueNextCursorHeader = "X-Next-Cursor";
    public const string QueueTotalCountHeader = "X-Total-Count";

    private static string Perm(string p) => PermissionPolicyProvider.Prefix + p;
    private static CurrentUser Actor(HttpContext ctx) => (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;
    private static string SourceOf(HttpContext ctx) => ctx.Request.Headers["X-Client"].FirstOrDefault() is { } c && c is "web" or "android" or "ios" or "windows" or "macos" or "linux" ? c : "api";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public const string Scope = "adr";

    private static Dictionary<string, object?> Codes(string? detail) => new() { ["code"] = detail?.Split(';')[0] ?? "invalid", ["errors"] = detail?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [] };

    private static IResult Map<T>(AdrOutcome<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Results.Json(r.Value, Web, statusCode: ok) : r.Error switch
    {
        AdrError.NotFound => Problem.ForbiddenResult(), // never reveals whether a record exists
        AdrError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        AdrError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(),
    };

    /// <summary>The role the caller acts in for this subject: the patient themselves, or the professional role they hold. Null when neither applies.</summary>
    internal static ReporterRole? RoleFor(CurrentUser actor, Guid subject) =>
        actor.UserId == subject ? ReporterRole.Patient
        : actor.Roles.Contains("Physician") ? ReporterRole.Physician
        : actor.Roles.Contains("Pharmacist") ? ReporterRole.Pharmacist
        : null;

    private sealed class Call(HttpContext ctx, CurrentUser actor, Guid subject, ReporterRole role, Guid? recordId)
    {
        public HttpContext Ctx { get; } = ctx;
        public CurrentUser Actor { get; } = actor;
        public Guid Subject { get; } = subject;
        public ReporterRole Role { get; } = role;
        public Guid? RecordId { get; } = recordId;
        public string Source { get; } = SourceOf(ctx);
        public string? Corr => Ctx.TraceIdentifier;
        public CancellationToken Ct => Ctx.RequestAborted;
        public IAdverseEventService Svc => Ctx.RequestServices.GetRequiredService<IAdverseEventService>();
    }

    /// <summary>Authorizes against the subject, resolves the caller's role, and (for professionals) requires an active patient. Returns null on denial.</summary>
    private static async Task<Call?> AuthorizeAsync(HttpContext ctx, Guid subject, Guid? recordId, string? ownerPermission, string? professionalPermission, bool audit)
    {
        var actor = Actor(ctx);
        var role = RoleFor(actor, subject);
        if (role is null)
        {
            return null;
        }

        var permission = role == ReporterRole.Patient ? ownerPermission : professionalPermission;
        if (permission is null)
        {
            return null;
        }

        var authz = ctx.RequestServices.GetRequiredService<IAccessAuthorizer>();
        var decision = await authz.AuthorizeAsync(actor, permission, AccessResource.Patient(subject, Scope), new RequestContext(SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted);
        if (!decision.Allowed)
        {
            return null;
        }

        if (role != ReporterRole.Patient)
        {
            var patient = await ctx.RequestServices.GetRequiredService<IPatientService>().GetAsync(subject, ctx.RequestAborted);
            if (!patient.Succeeded || patient.Value!.Status != PatientStatus.Active)
            {
                return null;
            }

            if (audit)
            {
                await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdrAccessed, AuditResult.Success, actor.UserId, "adverse-event", recordId?.ToString() ?? "list", subject, SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted);
            }
        }

        return new Call(ctx, actor, subject, role.Value, recordId);
    }

    private static void RecordRoute(IEndpointRouteBuilder app, string method, string pattern, string? ownerPermission, string? professionalPermission, bool read, Func<Call, Task<IResult>> handler)
    {
        var builder = app.MapMethods(pattern, [method], async (Guid rid, HttpContext ctx) =>
        {
            var subject = await ctx.RequestServices.GetRequiredService<IAdverseEventService>().SubjectOfAsync(rid, ctx.RequestAborted);
            if (subject is null)
            {
                return Problem.ForbiddenResult();
            }

            var call = await AuthorizeAsync(ctx, subject.Value, rid, ownerPermission, professionalPermission, read);
            return call is null ? Problem.ForbiddenResult() : await handler(call);
        }).RequireAuthorization(Perm(Permissions.PatientAdrRead));
        if (!read)
        {
            builder.RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        }
    }

    public static IEndpointRouteBuilder MapAdr(this IEndpointRouteBuilder app)
    {
        // Definitions and notices: no patient data. Quoted from supplementary documents (not lecture slides); the Persian rendering is not reviewed.
        app.MapGet("/adverse-events/definitions", () => Results.Json(new { definitions = AdrDefinitions.All, signalNotice = AdrNotices.Signal, internalRecordNotice = AdrNotices.InternalRecord, persianWordingStatus = WordingStatus.AwaitingClinicalReview }, Web))
            .RequireAuthorization(Perm(Permissions.PatientAdrRead));

        app.MapPost("/patients/{id:guid}/adverse-events", async (Guid id, CreateAdverseEventInput body, HttpContext ctx) =>
        {
            var call = await AuthorizeAsync(ctx, id, null, Permissions.PatientAdrCreate, Permissions.AdrRecordCreate, false);
            return call is null ? Problem.ForbiddenResult() : Map(await call.Svc.CreateAsync(call.Actor.UserId, call.Role, call.Subject, body, call.Source, call.Corr, call.Ct), StatusCodes.Status201Created);
        }).RequireAuthorization(Perm(Permissions.PatientAdrRead)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);

        app.MapGet("/patients/{id:guid}/adverse-events", async (Guid id, HttpContext ctx) =>
        {
            var call = await AuthorizeAsync(ctx, id, null, Permissions.PatientAdrRead, Permissions.PatientAdrRead, true);
            return call is null ? Problem.ForbiddenResult() : Map(await call.Svc.ListForSubjectAsync(call.Subject, call.Role, int.TryParse(ctx.Request.Query["take"], out var t) ? t : 50, call.Ct));
        }).RequireAuthorization(Perm(Permissions.PatientAdrRead));

        // The reviewer's queue. Which patients the reviewer may read now (active care relationship + consent, organization) is decided FIRST, one patient at a time by the same
        // authorizer as every other route, and only those patients' records are then read, counted and cut into pages. Nothing of any other patient is read.
        app.MapGet("/adverse-events/queue", async (HttpContext ctx) =>
        {
            var actor = Actor(ctx);
            ctx.Response.Headers.CacheControl = "no-store";
            var wanted = (ctx.Request.Query["status"].ToString()).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Enum.TryParse<ReviewStatus>(s, true, out var v) ? (ReviewStatus?)v : null).Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
            if (wanted.Count == 0)
            {
                wanted = [ReviewStatus.Submitted, ReviewStatus.InReview, ReviewStatus.InformationRequested];
            }

            var limitText = ctx.Request.Query["limit"];
            var limit = QueueDefaultLimit;
            if (limitText.Count > 0 && (limitText.Count > 1 || !int.TryParse(limitText[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out limit) || limit is < 1 or > QueueMaxLimit))
            {
                return Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes("limit.invalid"));
            }

            var mask = QueueCursor.MaskOf(wanted);
            var cursorText = ctx.Request.Query["cursor"];
            QueueCursor.Position? after = null;
            if (cursorText.Count > 0 && (cursorText.Count > 1 || (after = QueueCursor.Decode(cursorText[0], actor.UserId, mask)) is null))
            {
                return Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes("cursor.invalid"));
            }

            var authz = ctx.RequestServices.GetRequiredService<IAccessAuthorizer>();
            var authorized = new List<Guid>();
            foreach (var p in await ctx.RequestServices.GetRequiredService<IUserIdentityService>().ListRelatedPatientsAsync(actor, includeDisabled: true, ct: ctx.RequestAborted))
            {
                // Same test as before for each patient the reviewer is related to; an account that is disabled is not a reason to drop an open report, so those patients stay candidates.
                if (p.UserId != actor.UserId && RoleFor(actor, p.UserId) is not null
                    && (await authz.AuthorizeAsync(actor, Permissions.AdrReview, AccessResource.Patient(p.UserId, Scope), new RequestContext(SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted)).Allowed)
                {
                    authorized.Add(p.UserId);
                }
            }

            var outcome = await ctx.RequestServices.GetRequiredService<IAdverseEventService>().ListQueueAsync(new AdrQueueRequest(wanted, authorized, limit, after?.LastActivityAt, after?.Id, after is null), ctx.RequestAborted);
            if (!outcome.Succeeded)
            {
                return Map(outcome);
            }

            var page = outcome.Value!;
            var allowed = authorized.ToHashSet();
            if (page.Items.Any(r => !allowed.Contains(r.SubjectId)))
            {
                // Cannot happen while the query is scoped; if it ever did, nothing is sent.
                return Results.Problem(title: "Server error", statusCode: StatusCodes.Status500InternalServerError);
            }

            // "true" only when there is at least one more authorized record to fetch; the cursor is sent only then. The total is for the first page only.
            ctx.Response.Headers[QueueTruncatedHeader] = page.HasMore ? "true" : "false";
            if (page.HasMore)
            {
                var last = page.Items[^1];
                ctx.Response.Headers[QueueNextCursorHeader] = QueueCursor.Encode(new QueueCursor.Position(last.LastActivityAt, last.Id), actor.UserId, mask);
            }

            if (page.Total is { } total)
            {
                ctx.Response.Headers[QueueTotalCountHeader] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdrAccessed, AuditResult.Success, actor.UserId, "adverse-event", "queue", null, SourceOf(ctx), ctx.TraceIdentifier, null, new Dictionary<string, string> { ["count"] = page.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }), ctx.RequestAborted);
            return Results.Json(page.Items, Web);
        }).RequireAuthorization(Perm(Permissions.AdrReview));

        RecordRoute(app, "GET", "/adverse-events/{rid:guid}", Permissions.PatientAdrRead, Permissions.PatientAdrRead, true,
            async c => Map(await c.Svc.GetAsync(c.RecordId!.Value, c.Role, c.Ct)));
        RecordRoute(app, "POST", "/adverse-events/{rid:guid}/follow-ups", Permissions.PatientAdrCreate, Permissions.AdrReview, false, async c =>
            Map(await c.Svc.AddFollowUpAsync(c.Actor.UserId, c.Role, c.RecordId!.Value, (await ReadAsync<AdrTextBody>(c))?.Text ?? string.Empty, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created));
        RecordRoute(app, "POST", "/adverse-events/{rid:guid}/supplements", Permissions.PatientAdrCreate, Permissions.AdrReview, false, async c =>
            await ReadAsync<SupplementInput>(c) is { } b ? Map(await c.Svc.AddSupplementAsync(c.Actor.UserId, c.Role, c.RecordId!.Value, b, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("body.invalid")));
        RecordRoute(app, "POST", "/adverse-events/{rid:guid}/entries", null, Permissions.AdrReview, false, async c =>
            await ReadAsync<AdrEntryBody>(c) is { } b ? Map(await c.Svc.AddReviewEntryAsync(c.Actor.UserId, c.Role, c.RecordId!.Value, b.Kind, b.Text ?? string.Empty, c.Source, c.Corr, c.Ct), StatusCodes.Status201Created) : Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("body.invalid")));
        RecordRoute(app, "POST", "/adverse-events/{rid:guid}/classification", null, Permissions.AdrReview, false, async c =>
            await ReadAsync<AdrClassifyBody>(c) is { } b ? Map(await c.Svc.ClassifyAsync(c.Actor.UserId, c.Role, c.RecordId!.Value, b.Category, b.Reason ?? string.Empty, b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("body.invalid")));
        RecordRoute(app, "POST", "/adverse-events/{rid:guid}/status", null, Permissions.AdrReview, false, async c =>
            await ReadAsync<AdrStatusBody>(c) is { } b ? Map(await c.Svc.SetStatusAsync(c.Actor.UserId, c.Role, c.RecordId!.Value, b.Status, b.Note, b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("body.invalid")));
        RecordRoute(app, "POST", "/adverse-events/{rid:guid}/assistance", null, Permissions.AdrReview, false, async c =>
            Map(await c.Svc.AssistAsync(c.Actor.UserId, c.Role, c.RecordId!.Value, c.Source, c.Corr, c.Ct)));
        return app;
    }

    private static async Task<T?> ReadAsync<T>(Call c) where T : class
    {
        try
        {
            return await c.Ctx.Request.ReadFromJsonAsync<T>(Web, c.Ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return null;
        }
    }
}
