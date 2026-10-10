using System.Text.Json;
using MedSmarter.Api.Security;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.ClinicalRules.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;

namespace MedSmarter.Api.Safety;

public sealed record ReviewRuleBody(ReviewDecision Decision, string? Note, DateTimeOffset? EffectiveFrom);

/// <summary>
/// Clinical safety assessments (patient-scoped) and the management of safety rules (global, two-person review).
/// Patient routes: authenticated, the catalog permission (RBAC), then ownership or an active care relationship and consent for the medications scope; the other
/// categories the engine reads (profile, allergies, symptoms) are checked separately and, when the requester may not read them, are withheld and reported as
/// not authorized (never as empty). The patient id always comes from the route and is authorized; nothing in a body can name another patient.
/// </summary>
public static class SafetyEndpoints
{
    private static string Perm(string p) => PermissionPolicyProvider.Prefix + p;
    private static CurrentUser Actor(HttpContext ctx) => (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;
    private static string SourceOf(HttpContext ctx) => ctx.Request.Headers["X-Client"].FirstOrDefault() is { } c && c is "web" or "android" or "ios" or "windows" or "macos" or "linux" ? c : "api";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly (string Category, string Permission)[] Extras =
    [
        ("profile", Permissions.PatientProfileRead), ("allergies", Permissions.PatientAllergiesRead), ("symptoms", Permissions.PatientSymptomsRead),
    ];

    private static Dictionary<string, object?> Codes(string? detail) => new() { ["code"] = detail?.Split(';')[0] ?? "invalid", ["errors"] = detail?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [] };

    private static IResult Map<T>(SafetyOutcome<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Results.Json(r.Value, Web, statusCode: ok) : r.Error switch
    {
        SafetyError.NotFound => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound, extensions: Codes(r.Detail)),
        SafetyError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        SafetyError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        SafetyError.Unavailable => Results.Problem(title: "The assessment is unavailable", statusCode: StatusCodes.Status503ServiceUnavailable, extensions: Codes(r.Detail)),
        _ => Problem.ForbiddenResult(),
    };

    private static IResult Map<T>(RuleOutcomeResult<T> r, int ok = StatusCodes.Status200OK) => r.Succeeded ? Results.Json(r.Value, Web, statusCode: ok) : r.Error switch
    {
        RuleError.NotFound => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound, extensions: Codes(r.Detail)),
        RuleError.Validation => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes(r.Detail)),
        RuleError.Conflict => Results.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, extensions: Codes(r.Detail)),
        _ => Results.Problem(title: "Access denied", statusCode: StatusCodes.Status403Forbidden, extensions: Codes(r.Detail)),
    };

    private sealed class Call(HttpContext ctx, Guid subject, IReadOnlyList<string> readable)
    {
        public HttpContext Ctx { get; } = ctx;
        public CurrentUser Actor { get; } = SafetyEndpoints.Actor(ctx);
        public Guid Subject { get; } = subject;
        public IReadOnlyList<string> Readable { get; } = readable;
        public string Source { get; } = SourceOf(ctx);
        public string? Corr => Ctx.TraceIdentifier;
        public CancellationToken Ct => Ctx.RequestAborted;
        public string Locale => Ctx.Request.Query["locale"] == "fa" ? "fa" : "en";
        public T Svc<T>() where T : notnull => Ctx.RequestServices.GetRequiredService<T>();
    }

    private static void PatientRoute(IEndpointRouteBuilder app, string method, string pattern, string permission, bool read, Func<Call, Task<IResult>> handler)
    {
        var builder = app.MapMethods(pattern, [method], async (Guid id, HttpContext ctx) =>
        {
            var actor = Actor(ctx);
            var authz = ctx.RequestServices.GetRequiredService<IAccessAuthorizer>();
            var request = new RequestContext(SourceOf(ctx), ctx.TraceIdentifier);
            var decision = await authz.AuthorizeAsync(actor, permission, AccessResource.Patient(id, "safety"), request, ctx.RequestAborted);
            if (!decision.Allowed)
            {
                return Problem.ForbiddenResult(); // identical for "no such patient", "not your patient" and "no consent"
            }

            var readable = new List<string> { "medications" };
            if (actor.UserId != id)
            {
                var patient = await ctx.RequestServices.GetRequiredService<IPatientService>().GetAsync(id, ctx.RequestAborted);
                if (!patient.Succeeded || patient.Value!.Status != PatientStatus.Active)
                {
                    return Problem.ForbiddenResult();
                }

                // Each further category is its own permission + consent decision. A refusal withholds that category; it does not fail the request.
                foreach (var (category, perm) in Extras)
                {
                    if ((await authz.AuthorizeAsync(actor, perm, AccessResource.Patient(id, "safety-" + category), request, ctx.RequestAborted)).Allowed)
                    {
                        readable.Add(category);
                    }
                }

                if (read)
                {
                    await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.PatientDataAccessed, AuditResult.Success, actor.UserId, "safety", id.ToString(), id, SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted);
                }
            }
            else
            {
                readable.AddRange(Extras.Select(e => e.Category));
            }

            return await handler(new Call(ctx, id, readable));
        }).RequireAuthorization(Perm(permission));
        if (!read)
        {
            builder.RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        }
    }

    public static IEndpointRouteBuilder MapSafety(this IEndpointRouteBuilder app)
    {
        PatientRoute(app, "POST", "/patients/{id:guid}/safety/assessments", Permissions.SafetyAssessRun, false, async c =>
            Map(await c.Svc<IClinicalSafetyService>().AssessAsync(c.Actor.UserId, c.Subject, new AssessCommand(c.Readable, c.Locale), c.Source, c.Corr, c.Ct), StatusCodes.Status201Created));
        PatientRoute(app, "GET", "/patients/{id:guid}/safety/assessments", Permissions.SafetyAssessRead, true, async c =>
            Map(await c.Svc<IClinicalSafetyService>().ListAsync(c.Subject, int.TryParse(c.Ctx.Request.Query["take"], out var take) ? take : 20, c.Readable, c.Ct)));
        PatientRoute(app, "GET", "/patients/{id:guid}/safety/assessments/latest", Permissions.SafetyAssessRead, true, async c =>
            Map(await c.Svc<IClinicalSafetyService>().LatestAsync(c.Subject, c.Readable, c.Ct)));
        app.MapGet("/patients/{id:guid}/safety/assessments/{aid:guid}", async (Guid id, Guid aid, HttpContext ctx) =>
        {
            // Same checks as the other patient routes; kept separate only because it carries a second route value.
            var actor = Actor(ctx);
            var authz = ctx.RequestServices.GetRequiredService<IAccessAuthorizer>();
            var request = new RequestContext(SourceOf(ctx), ctx.TraceIdentifier);
            if (!(await authz.AuthorizeAsync(actor, Permissions.SafetyAssessRead, AccessResource.Patient(id, "safety"), request, ctx.RequestAborted)).Allowed)
            {
                return Problem.ForbiddenResult();
            }

            var readable = new List<string> { "medications" };
            if (actor.UserId != id)
            {
                var patient = await ctx.RequestServices.GetRequiredService<IPatientService>().GetAsync(id, ctx.RequestAborted);
                if (!patient.Succeeded || patient.Value!.Status != PatientStatus.Active)
                {
                    return Problem.ForbiddenResult();
                }

                foreach (var (category, perm) in Extras)
                {
                    if ((await authz.AuthorizeAsync(actor, perm, AccessResource.Patient(id, "safety-" + category), request, ctx.RequestAborted)).Allowed)
                    {
                        readable.Add(category);
                    }
                }

                await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.PatientDataAccessed, AuditResult.Success, actor.UserId, "safety", id.ToString(), id, SourceOf(ctx), ctx.TraceIdentifier), ctx.RequestAborted);
            }
            else
            {
                readable.AddRange(Extras.Select(e => e.Category));
            }

            return Map(await ctx.RequestServices.GetRequiredService<IClinicalSafetyService>().GetAsync(id, aid, readable, ctx.RequestAborted));
        }).RequireAuthorization(Perm(Permissions.SafetyAssessRead));

        // What the rule set can and cannot cover. Not patient data: any signed-in person who may run an assessment may see it.
        app.MapGet("/safety/coverage", async (IClinicalRuleCatalog catalog, HttpContext ctx) => Results.Json(await catalog.CoverageAsync(ctx.RequestAborted), Web))
            .RequireAuthorization(Perm(Permissions.SafetyAssessRun));

        MapRules(app);
        return app;
    }

    private static void MapRules(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/clinical-rules");
        g.MapGet("", async (IClinicalRuleCatalog catalog, HttpContext ctx, RuleStatus? status, int? take) => Results.Json(await catalog.ListAsync(status, take ?? 100, ctx.RequestAborted), Web))
            .RequireAuthorization(Perm(Permissions.ClinicalrulesRead));
        g.MapGet("{ruleId}/versions/{version:int}", async (string ruleId, int version, IClinicalRuleCatalog catalog, HttpContext ctx) => Map(await catalog.GetAsync(ruleId, version, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.ClinicalrulesRead));

        g.MapPost("", async (HttpContext ctx, IClinicalRuleCatalog catalog) =>
            await Body<RuleDefinition>(ctx) is { } body ? Map(await catalog.CreateDraftAsync(Actor(ctx).UserId, body, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted), StatusCodes.Status201Created) : Invalid())
            .RequireAuthorization(Perm(Permissions.ClinicalrulesAuthor)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        g.MapPost("{ruleId}/versions/{version:int}/submit", async (string ruleId, int version, IClinicalRuleCatalog catalog, HttpContext ctx) =>
            Map(await catalog.SubmitForReviewAsync(Actor(ctx).UserId, ruleId, version, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.ClinicalrulesAuthor)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        g.MapPost("{ruleId}/versions/{version:int}/review", async (string ruleId, int version, HttpContext ctx, IClinicalRuleCatalog catalog) =>
            await Body<ReviewRuleBody>(ctx) is { } body ? Map(await catalog.ReviewAsync(Actor(ctx).UserId, ruleId, version, body.Decision, body.Note, body.EffectiveFrom, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)) : Invalid())
            .RequireAuthorization(Perm(Permissions.ClinicalrulesReview)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
        g.MapPost("{ruleId}/versions/{version:int}/retire", async (string ruleId, int version, IClinicalRuleCatalog catalog, HttpContext ctx) =>
            Map(await catalog.RetireAsync(Actor(ctx).UserId, ruleId, version, SourceOf(ctx), ctx.TraceIdentifier, ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.ClinicalrulesRetire)).RequireRateLimiting(Knowledge.KnowledgeEndpoints.WriteLimiter);
    }

    private static IResult Invalid() => Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: Codes("body.invalid"));

    private static async Task<T?> Body<T>(HttpContext ctx) where T : class
    {
        try
        {
            return await ctx.Request.ReadFromJsonAsync<T>(Web, ctx.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
