using MedSmarter.Api.Security;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Medications.Contracts;

namespace MedSmarter.Api.Knowledge;

public sealed record LifecycleBody(LifecycleStatus Status);
public sealed record ValidationBody(ValidationStatus Status, Guid RevisionId);
public sealed record RevisionStatusBody(RevisionStatus Status);
public sealed record UpdateBody(MedicationDraft Draft, string? Reason);

/// <summary>
/// Medication reference, administration, knowledge-source and AI-assistant endpoints. Every route needs a permission from the Phase 3
/// catalog (policy <c>perm:*</c>); reading the reference is separate from editing and from publishing/validating.
/// </summary>
public static class KnowledgeEndpoints
{
    public const string SearchLimiter = "search";
    public const string AiLimiter = "ai";
    public const string WriteLimiter = "knowledge-write";

    private static CurrentUser Actor(HttpContext ctx) => (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;
    private static string Perm(string p) => PermissionPolicyProvider.Prefix + p;
    private static string Source(HttpContext ctx) => ctx.Request.Headers["X-Client"].FirstOrDefault() is { } c && c is "web" or "android" or "ios" or "windows" or "macos" or "linux" ? c : "api";

    /// <summary>Maps a service failure to a uniform ProblemDetails. Only machine-readable codes are returned, never internals.</summary>
    private static IResult Fail<T>(OperationResult<T> r) => r.Error switch
    {
        MedicationError.NotFound => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound),
        MedicationError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        MedicationError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(),
    };

    private static Dictionary<string, object?> Codes(string? detail) =>
        new() { ["code"] = detail?.Split(';')[0] ?? "invalid", ["errors"] = detail?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [] };

    private static IResult Ok<T>(OperationResult<T> r) => r.Succeeded ? Results.Ok(r.Value) : Fail(r);

    private static bool TryVersion(HttpContext ctx, out int version) =>
        int.TryParse(ctx.Request.Headers.IfMatch.ToString().Trim('"', ' '), out version) && version >= 1;

    private static IResult MissingVersion() => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status428PreconditionRequired, extensions: Codes("if_match.required"));

    public static IEndpointRouteBuilder MapKnowledge(this IEndpointRouteBuilder app)
    {
        MapRead(app);
        MapAdmin(app);
        MapSources(app);
        MapAi(app);
        return app;
    }

    private static void MapRead(IEndpointRouteBuilder app)
    {
        app.MapGet("/medications/search", async (HttpContext ctx, IMedicationService svc, string? q, int? limit, int? offset, bool? includeInactive) =>
        {
            var inactive = includeInactive == true && Actor(ctx).Has(Permissions.KnowledgeManage); // only editors may see drafts/inactive
            try
            {
                return Results.Ok(await svc.SearchAsync(new MedicationSearchQuery(q, limit ?? 20, offset ?? 0, inactive), ctx.RequestAborted));
            }
            catch (MedicationSearchException ex)
            {
                return Results.Problem(title: "Invalid search", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(ex.Code));
            }
        }).RequireAuthorization(Perm(Permissions.MedicationRead)).RequireRateLimiting(SearchLimiter);

        app.MapGet("/medications/{id:guid}", async (Guid id, HttpContext ctx, IMedicationService svc) =>
            Ok(await svc.GetAsync(id, Actor(ctx).Has(Permissions.KnowledgeManage), ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.MedicationRead)).RequireRateLimiting(SearchLimiter);

        // RAG-ready projection. Same visibility rules as the detail view.
        app.MapGet("/medications/{id:guid}/knowledge-document", async (Guid id, HttpContext ctx, IMedicationService svc) =>
            Ok(await svc.GetKnowledgeDocumentAsync(id, Actor(ctx).Has(Permissions.KnowledgeManage), ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.MedicationRead)).RequireRateLimiting(SearchLimiter);
    }

    private static void MapAdmin(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireRateLimiting(WriteLimiter);

        admin.MapPost("/medications", async (MedicationDraft draft, HttpContext ctx, IMedicationAdminService svc) =>
        {
            var r = await svc.CreateAsync(Actor(ctx).UserId, draft, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/medications/{r.Value!.Id}", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPut("/medications/{id:guid}", async (Guid id, UpdateBody body, HttpContext ctx, IMedicationAdminService svc) =>
            !TryVersion(ctx, out var v) ? MissingVersion()
            : Ok(await svc.UpdateAsync(Actor(ctx).UserId, id, v, body.Draft, body.Reason ?? string.Empty, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPost("/medications/{id:guid}/lifecycle", async (Guid id, LifecycleBody body, HttpContext ctx, IMedicationAdminService svc) =>
            !TryVersion(ctx, out var v) ? MissingVersion()
            : Ok(await svc.SetLifecycleAsync(Actor(ctx).UserId, id, v, body.Status, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.KnowledgeManage));

        // Publishing/validation is a separate, stronger permission than editing.
        admin.MapPost("/medications/{id:guid}/validation", async (Guid id, ValidationBody body, HttpContext ctx, IMedicationAdminService svc) =>
            !TryVersion(ctx, out var v) ? MissingVersion()
            : Ok(await svc.SetValidationAsync(Actor(ctx).UserId, id, v, body.Status, body.RevisionId, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.KnowledgePublish));

        admin.MapGet("/medications/{id:guid}/versions", async (Guid id, IMedicationAdminService svc, CancellationToken ct) => Ok(await svc.ListVersionsAsync(id, ct)))
            .RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPost("/ingredients", async (NewIngredient body, HttpContext ctx, IMedicationAdminService svc) =>
        {
            var r = await svc.CreateIngredientAsync(Actor(ctx).UserId, body, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/admin/ingredients/{r.Value!.Id}", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPost("/manufacturers", async (NewManufacturer body, HttpContext ctx, IMedicationAdminService svc) =>
        {
            var r = await svc.CreateManufacturerAsync(Actor(ctx).UserId, body, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/admin/manufacturers/{r.Value!.Id}", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPost("/brands", async (NewBrand body, HttpContext ctx, IMedicationAdminService svc) =>
        {
            var r = await svc.CreateBrandAsync(Actor(ctx).UserId, body, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/admin/brands/{r.Value!.Id}", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPost("/reference-terms", async (NewReferenceTerm body, HttpContext ctx, IMedicationAdminService svc) =>
        {
            var r = await svc.CreateReferenceTermAsync(Actor(ctx).UserId, body, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/admin/reference-terms/{r.Value!.Id}", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        admin.MapPost("/interactions", async (NewInteraction body, HttpContext ctx, IMedicationAdminService svc) =>
            Ok(await svc.UpsertInteractionAsync(Actor(ctx).UserId, body, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.KnowledgeManage));
    }

    private static void MapSources(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").RequireRateLimiting(WriteLimiter);

        g.MapGet("/knowledge-sources", async (IKnowledgeSourceService svc, CancellationToken ct) => Results.Ok(await svc.ListSourcesAsync(ct)))
            .RequireAuthorization(Perm(Permissions.KnowledgeRead));

        g.MapPost("/knowledge-sources", async (NewKnowledgeSource body, HttpContext ctx, IKnowledgeSourceService svc) =>
        {
            var r = await svc.RegisterSourceAsync(Actor(ctx).UserId, body, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/admin/knowledge-sources/{r.Value!.Id}", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        g.MapGet("/knowledge-sources/{id:guid}/revisions", async (Guid id, IKnowledgeSourceService svc, CancellationToken ct) => Results.Ok(await svc.ListRevisionsAsync(id, ct)))
            .RequireAuthorization(Perm(Permissions.KnowledgeRead));

        g.MapPost("/knowledge-sources/{id:guid}/revisions", async (Guid id, NewRevision body, HttpContext ctx, IKnowledgeSourceService svc) =>
        {
            var r = await svc.AddRevisionAsync(Actor(ctx).UserId, body with { SourceId = id }, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted);
            return r.Succeeded ? Results.Created($"/admin/knowledge-sources/{id}/revisions", r.Value) : Fail(r);
        }).RequireAuthorization(Perm(Permissions.KnowledgeManage));

        g.MapPost("/knowledge-revisions/{id:guid}/status", async (Guid id, RevisionStatusBody body, HttpContext ctx, IKnowledgeSourceService svc) =>
            Ok(await svc.SetRevisionStatusAsync(Actor(ctx).UserId, id, body.Status, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.KnowledgePublish));
    }

    private static void MapAi(IEndpointRouteBuilder app)
    {
        app.MapPost("/ai/medication-assistant", async (AssistantQuestion body, HttpContext ctx, IAIAssistantService svc) =>
        {
            try
            {
                var answer = await svc.AskAsync(Actor(ctx).UserId, body, Source(ctx), ctx.TraceIdentifier, ctx.RequestAborted);
                // A provider problem is a controlled 503 with a safe message; "no information" is a normal 200.
                return answer.Error is AiErrorCode.None ? Results.Ok(answer) : Results.Json(answer, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (AssistantInputException)
            {
                return Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes("question.invalid"));
            }
        }).RequireAuthorization(Perm(Permissions.AiUse)).RequireRateLimiting(AiLimiter);

        app.MapGet("/ai/provider", (IAIAssistantService svc) => Results.Ok(svc.Status())).RequireAuthorization(Perm(Permissions.AiManage));
    }
}
