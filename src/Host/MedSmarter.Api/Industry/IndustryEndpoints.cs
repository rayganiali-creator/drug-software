using System.Text.Json;
using MedSmarter.Api.Security;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;

namespace MedSmarter.Api.Industry;

/// <summary>
/// The pharmaceutical-industry workspace: one read-only, aggregate, suppressed view per organization the caller belongs to. Default-deny: authenticated, the catalog permission,
/// then membership of the organization in the path. "Not a member" and "no such organization" answer with the same 403. No patient route, no export, no report-sending route exists here.
/// </summary>
public static class IndustryEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly string[] InvalidQuery = ["query.invalid"];

    public static IEndpointRouteBuilder MapIndustry(this IEndpointRouteBuilder app)
    {
        app.MapGet("/organizations/{orgId:guid}/industry/insights", async (Guid orgId, string? from, string? until, string? basis, HttpContext ctx) =>
        {
            var actor = (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;
            var source = ctx.Request.Headers["X-Client"].FirstOrDefault() is { } c && c is "web" or "android" or "ios" or "windows" or "macos" or "linux" ? c : "api";
            var decision = await ctx.RequestServices.GetRequiredService<IAccessAuthorizer>()
                .AuthorizeAsync(actor, Permissions.IndustryInsightsRead, AccessResource.Organization(orgId), new RequestContext(source, ctx.TraceIdentifier), ctx.RequestAborted);
            if (!decision.Allowed)
            {
                return Problem.ForbiddenResult();
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var until0 = until is null ? today : DateOnly.TryParse(until, out var u) ? u : (DateOnly?)null;
            var from0 = from is null ? (until0 ?? today).AddDays(-89) : DateOnly.TryParse(from, out var f) ? f : (DateOnly?)null;
            if (until0 is null || from0 is null || !Enum.TryParse<InsightBasis>(basis ?? nameof(InsightBasis.Real), true, out var b) || !Enum.IsDefined(b))
            {
                return Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = "query.invalid", ["errors"] = InvalidQuery });
            }

            var result = await ctx.RequestServices.GetRequiredService<IIndustryInsightsService>().GetAsync(orgId, from0.Value, until0.Value, b, ctx.RequestAborted);
            if (!result.Succeeded)
            {
                var codes = result.Detail?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [];
                return Results.Problem(title: "Invalid request", statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = codes.FirstOrDefault() ?? "invalid", ["errors"] = codes });
            }

            // ids and enum names only: who looked, at which organization's view, on which basis
            await ctx.RequestServices.GetRequiredService<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.IndustryInsightsAccessed, AuditResult.Success, actor.UserId, "industry-insights", orgId.ToString(), null, source, ctx.TraceIdentifier, null,
                new Dictionary<string, string> { ["org"] = orgId.ToString(), ["basis"] = b.ToString() }), ctx.RequestAborted);
            return Results.Json(result.Value, Web);
        }).RequireAuthorization(PermissionPolicyProvider.Prefix + Permissions.IndustryInsightsRead);
        return app;
    }
}
