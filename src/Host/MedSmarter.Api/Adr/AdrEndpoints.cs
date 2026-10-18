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
    public const int QueueReadCap = 200;
    public const string QueueTruncatedHeader = "X-Result-Truncated";

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

        // The reviewer's queue: records are filtered to the subjects this reviewer may read now (relationship + consent), so it never lists anyone else's.
        app.MapGet("/adverse-events/queue", async (HttpContext ctx) =>
        {
            var actor = Actor(ctx);
            var svc = ctx.RequestServices.GetRequiredService<IAdverseEventService>();
            var wanted = (ctx.Request.Query["status"].ToString()).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Enum.TryParse<ReviewStatus>(s, true, out var v) ? (ReviewStatus?)v : null).Where(v => v is not null).Select(v => v!.Value).ToList();
            if (wanted.Count == 0)
            {
                wanted = [ReviewStatus.Submitted, ReviewStatus.InReview, ReviewStatus.InformationRequested];
            }

            var rows = await svc.ListByStatusAsync(wanted, QueueReadCap, ctx.RequestAborted);
            // The service reads the newest QueueReadCap records of ALL patients and only then are they filtered to the ones this reviewer may read. When the read hit the cap, records that the
            // reviewer may read can be missing from the list, and a count made from it would be wrong. Say so (the body is unchanged); clients must not show an exact count in that case.
            ctx.Response.Headers[QueueTruncatedHeader] = rows.Value!.Count >= QueueReadCap ? "true" : "false";
            var allowed = new Dictionary<Guid, bool>();
            var result = new List<AdverseEventSummaryDto>();
            foreach (var r in rows.Value!)
            {
                if (!allowed.TryGetValue(r.SubjectId, out var ok))
                {
                    ok = r.SubjectId != actor.UserId && RoleFor(actor, r.SubjectId) is not null
                        && (await ctx.RequestServices.GetRequiredService<IAccessAuthorizer>().AuthorizeAsync(actor, Permissions.AdrReview, AccessResource.Patient(r.SubjectId, Scope), new RequestContext(SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted)).Allowed;
                    allowed[r.SubjectId] = ok;
                }

                if (ok)
                {
                    result.Add(r);
                }
            }

            await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdrAccessed, AuditResult.Success, actor.UserId, "adverse-event", "queue", null, SourceOf(ctx), ctx.TraceIdentifier, null, new Dictionary<string, string> { ["count"] = result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }), ctx.RequestAborted);
            return Results.Json(result, Web);
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
