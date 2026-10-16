using System.Text.Json;
using MedSmarter.Api.Security;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Pharmacies.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Api.Pharmacy;

public sealed record SafetyEntryBody(SafetyEntryKind Kind, string? Text);

public sealed record SafetyStatusBody(SafetyStatus Status, string? Note, int ExpectedVersion);

public sealed record ReportProposalBody(string? Note, int ExpectedVersion);

/// <summary>
/// Internal pharmacy safety records (dispensing errors, near misses, product concerns). Every route is default-deny: authenticated, the catalog permission, then membership of the
/// pharmacy organization that OWNS the record (never an organization named in a body). "No such record" and "another organization's record" answer with the same 403.
/// A link to a patient's ProductTrace record or to an adverse-event record is made only after the caller passes that record's own relationship + consent checks; nothing here
/// sends a record to any external party.
/// </summary>
public static class PharmacySafetyEndpoints
{
    private static string Perm(string p) => PermissionPolicyProvider.Prefix + p;
    private static CurrentUser Actor(HttpContext ctx) => (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;
    private static string SourceOf(HttpContext ctx) => ctx.Request.Headers["X-Client"].FirstOrDefault() is { } c && c is "web" or "android" or "ios" or "windows" or "macos" or "linux" ? c : "api";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static Dictionary<string, object?> Codes(string? detail) => new() { ["code"] = detail?.Split(';')[0] ?? "invalid", ["errors"] = detail?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [] };

    private static IResult Map<T>(PharmacyOutcome<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Results.Json(r.Value, Web, statusCode: ok) : r.Error switch
    {
        PharmacyError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        PharmacyError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(), // not found and forbidden look the same
    };

    internal static PharmacyActorRole? RoleOf(CurrentUser actor) =>
        actor.Roles.Contains("Pharmacist") ? PharmacyActorRole.Pharmacist : actor.Roles.Contains("PharmacyAdmin") ? PharmacyActorRole.PharmacyAdmin : null;

    private static async Task<bool> AllowedAsync(HttpContext ctx, string permission, AccessResource resource) =>
        (await ctx.RequestServices.GetRequiredService<IAccessAuthorizer>().AuthorizeAsync(Actor(ctx), permission, resource, new RequestContext(SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted)).Allowed;

    private sealed class Call(HttpContext ctx, Guid org, PharmacyActorRole role, Guid? id)
    {
        public HttpContext Ctx { get; } = ctx;
        public CurrentUser Actor { get; } = PharmacySafetyEndpoints.Actor(ctx);
        public Guid Org { get; } = org;
        public PharmacyActorRole Role { get; } = role;
        public Guid? Id { get; } = id;
        public string Source { get; } = SourceOf(ctx);
        public string? Corr => Ctx.TraceIdentifier;
        public CancellationToken Ct => Ctx.RequestAborted;
        public IPharmacySafetyService Svc => Ctx.RequestServices.GetRequiredService<IPharmacySafetyService>();
    }

    private static async Task<Call?> AuthorizeOrgAsync(HttpContext ctx, Guid org, string permission, Guid? id)
    {
        var role = RoleOf(Actor(ctx));
        return role is null || !await AllowedAsync(ctx, permission, AccessResource.Organization(org)) ? null : new Call(ctx, org, role.Value, id);
    }

    private static void OrgRoute(IEndpointRouteBuilder app, string method, string pattern, string permission, bool write, Func<Call, Task<IResult>> handler)
    {
        var b = app.MapMethods(pattern, [method], async (Guid org, HttpContext ctx) =>
        {
            var call = await AuthorizeOrgAsync(ctx, org, permission, null);
            return call is null ? Problem.ForbiddenResult() : await handler(call);
        }).RequireAuthorization(Perm(permission));
        if (write)
        {
            b.RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        }
    }

    private static void RecordRoute(IEndpointRouteBuilder app, string method, string pattern, SafetyRecordKind kind, string permission, bool write, Func<Call, Task<IResult>> handler)
    {
        var b = app.MapMethods(pattern, [method], async (Guid rid, HttpContext ctx) =>
        {
            var org = await ctx.RequestServices.GetRequiredService<IPharmacySafetyService>().OrganizationOfAsync(kind, rid, ctx.RequestAborted);
            if (org is null)
            {
                return Problem.ForbiddenResult();
            }

            var call = await AuthorizeOrgAsync(ctx, org.Value, permission, rid);
            if (call is null)
            {
                return Problem.ForbiddenResult();
            }

            if (!write)
            {
                await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.PharmacySafetyAccessed, AuditResult.Success, call.Actor.UserId, kind == SafetyRecordKind.SafetyEvent ? "pharmacy-safety-event" : "product-concern", rid.ToString(), null, call.Source, call.Corr, null, new Dictionary<string, string> { ["org"] = org.Value.ToString() }), ctx.RequestAborted);
            }

            return await handler(call);
        }).RequireAuthorization(Perm(permission));
        if (write)
        {
            b.RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        }
    }

    private static IReadOnlyCollection<SafetyStatus>? Statuses(HttpContext ctx) =>
        [.. ctx.Request.Query["status"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Enum.TryParse<SafetyStatus>(s, true, out var v) ? (SafetyStatus?)v : null).Where(v => v is not null).Select(v => v!.Value)];

    /// <summary>A link to an adverse-event record is allowed only if the caller may review that record (its own relationship and consent checks). Missing and hidden look the same.</summary>
    private static async Task<bool> AdrLinkAllowedAsync(HttpContext ctx, Guid? adrId)
    {
        if (adrId is not { } id)
        {
            return true;
        }

        var subject = await ctx.RequestServices.GetRequiredService<IAdverseEventService>().SubjectOfAsync(id, ctx.RequestAborted);
        return subject is { } s && await AllowedAsync(ctx, Permissions.AdrReview, AccessResource.Patient(s, "adr"));
    }

    public static IEndpointRouteBuilder MapPharmacySafety(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pharmacy-safety/definitions", () => Results.Json(new
        {
            nearMiss = new { definition = PharmacyDefinitions.NearMissDefinition, document = PharmacyDefinitions.NearMissDocument, section = PharmacyDefinitions.NearMissSection, page = PharmacyDefinitions.NearMissPage, isLectureSlide = false },
            dispensingErrorExamples = PharmacyDefinitions.Examples,
            taxonomyNotice = PharmacyNotices.Taxonomy,
            concernTypesNotice = PharmacyNotices.ConcernTypes,
            eventNotice = PharmacyNotices.Event,
            concernNotice = PharmacyNotices.Concern,
            persianWordingStatus = PharmacyNotices.PersianAwaitingReview,
        }, Web)).RequireAuthorization(Perm(Permissions.PharmacySafetyRead));

        // ---- dispensing errors and near misses
        app.MapPost("/organizations/{org:guid}/safety-events", async (Guid org, CreateSafetyEventInput body, HttpContext ctx) =>
        {
            var call = await AuthorizeOrgAsync(ctx, org, Permissions.PharmacySafetyCreate, null);
            if (call is null)
            {
                return Problem.ForbiddenResult();
            }

            if (!await AdrLinkAllowedAsync(ctx, body.LinkedAdverseEventId))
            {
                return Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("link.adr_unavailable"));
            }

            return Map(await call.Svc.CreateEventAsync(call.Actor.UserId, call.Role, org, body, call.Source, call.Corr, call.Ct), StatusCodes.Status201Created);
        }).RequireAuthorization(Perm(Permissions.PharmacySafetyCreate)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        OrgRoute(app, "GET", "/organizations/{org:guid}/safety-events", Permissions.PharmacySafetyRead, false, async c =>
            Map(await c.Svc.ListAsync(c.Org, SafetyRecordKind.SafetyEvent, Statuses(c.Ctx), int.TryParse(c.Ctx.Request.Query["take"], out var t) ? t : 100, c.Ct)));
        RecordRoute(app, "GET", "/safety-events/{rid:guid}", SafetyRecordKind.SafetyEvent, Permissions.PharmacySafetyRead, false, async c => Map(await c.Svc.GetEventAsync(c.Id!.Value, c.Ct)));
        RecordRoute(app, "POST", "/safety-events/{rid:guid}/entries", SafetyRecordKind.SafetyEvent, Permissions.PharmacySafetyReview, true, async c =>
            await Read<SafetyEntryBody>(c) is { } b ? await Then(c, SafetyRecordKind.SafetyEvent, await c.Svc.AddEntryAsync(c.Actor.UserId, c.Role, SafetyRecordKind.SafetyEvent, c.Id!.Value, b.Kind, b.Text ?? string.Empty, c.Source, c.Corr, c.Ct)) : Bad());
        RecordRoute(app, "POST", "/safety-events/{rid:guid}/status", SafetyRecordKind.SafetyEvent, Permissions.PharmacySafetyReview, true, async c =>
            await Read<SafetyStatusBody>(c) is { } b ? await Then(c, SafetyRecordKind.SafetyEvent, await c.Svc.SetStatusAsync(c.Actor.UserId, c.Role, SafetyRecordKind.SafetyEvent, c.Id!.Value, b.Status, b.Note, b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : Bad());

        // ---- product concerns
        app.MapPost("/organizations/{org:guid}/product-concerns", async (Guid org, CreateProductConcernInput body, HttpContext ctx) =>
        {
            var call = await AuthorizeOrgAsync(ctx, org, Permissions.PharmacySafetyCreate, null);
            if (call is null)
            {
                return Problem.ForbiddenResult();
            }

            if (!await AdrLinkAllowedAsync(ctx, body.LinkedAdverseEventId))
            {
                return Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("link.adr_unavailable"));
            }

            ProductSnapshot? snapshot = null;
            if (body.ProductRecord is { } pr)
            {
                // The link is made only if this pharmacist may read that patient's product records right now (relationship + consent). Anything else is the same "unavailable".
                if (await AllowedAsync(ctx, Permissions.PatientProductsRead, AccessResource.Patient(pr.SubjectId, "products")))
                {
                    var found = await ctx.RequestServices.GetRequiredService<IProductTraceService>().GetAsync(pr.SubjectId, pr.ProductRecordId, ctx.RequestAborted);
                    if (found.Succeeded)
                    {
                        var p = found.Value!;
                        snapshot = new ProductSnapshot(pr.SubjectId, p.Id, new ProductIdentifiers(p.ProductName, p.BatchNumber, p.ExpiryDate, p.ManufacturerName, p.Gtin), p.Version, p.Verification.ToString());
                    }
                }
            }

            return Map(await call.Svc.CreateConcernAsync(call.Actor.UserId, call.Role, org, body, snapshot, call.Source, call.Corr, call.Ct), StatusCodes.Status201Created);
        }).RequireAuthorization(Perm(Permissions.PharmacySafetyCreate)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        OrgRoute(app, "GET", "/organizations/{org:guid}/product-concerns", Permissions.PharmacySafetyRead, false, async c =>
            Map(await c.Svc.ListAsync(c.Org, SafetyRecordKind.ProductConcern, Statuses(c.Ctx), int.TryParse(c.Ctx.Request.Query["take"], out var t) ? t : 100, c.Ct)));
        RecordRoute(app, "GET", "/product-concerns/{rid:guid}", SafetyRecordKind.ProductConcern, Permissions.PharmacySafetyRead, false, async c => Map(await c.Svc.GetConcernAsync(c.Id!.Value, c.Ct)));
        RecordRoute(app, "POST", "/product-concerns/{rid:guid}/entries", SafetyRecordKind.ProductConcern, Permissions.PharmacySafetyReview, true, async c =>
            await Read<SafetyEntryBody>(c) is { } b ? await Then(c, SafetyRecordKind.ProductConcern, await c.Svc.AddEntryAsync(c.Actor.UserId, c.Role, SafetyRecordKind.ProductConcern, c.Id!.Value, b.Kind, b.Text ?? string.Empty, c.Source, c.Corr, c.Ct)) : Bad());
        RecordRoute(app, "POST", "/product-concerns/{rid:guid}/status", SafetyRecordKind.ProductConcern, Permissions.PharmacySafetyReview, true, async c =>
            await Read<SafetyStatusBody>(c) is { } b ? await Then(c, SafetyRecordKind.ProductConcern, await c.Svc.SetStatusAsync(c.Actor.UserId, c.Role, SafetyRecordKind.ProductConcern, c.Id!.Value, b.Status, b.Note, b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : Bad());
        RecordRoute(app, "POST", "/product-concerns/{rid:guid}/manufacturer-report-proposal", SafetyRecordKind.ProductConcern, Permissions.PharmacySafetyReview, true, async c =>
            await Read<ReportProposalBody>(c) is { } b ? await Then(c, SafetyRecordKind.ProductConcern, await c.Svc.ProposeManufacturerReportAsync(c.Actor.UserId, c.Role, c.Id!.Value, b.Note ?? string.Empty, b.ExpectedVersion, c.Source, c.Corr, c.Ct)) : Bad());
        return app;
    }

    private static IResult Bad() => Results.Problem(title: "Invalid request", statusCode: 400, extensions: Codes("body.invalid"));

    /// <summary>After a write, return the record as it is now.</summary>
    private static async Task<IResult> Then(Call c, SafetyRecordKind kind, PharmacyOutcome<bool> done) =>
        !done.Succeeded ? Map(done) : kind == SafetyRecordKind.SafetyEvent ? Map(await c.Svc.GetEventAsync(c.Id!.Value, c.Ct), StatusCodes.Status200OK) : Map(await c.Svc.GetConcernAsync(c.Id!.Value, c.Ct), StatusCodes.Status200OK);

    private static async Task<T?> Read<T>(Call c) where T : class
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
