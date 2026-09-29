using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MedSmarter.BuildingBlocks.Infrastructure;
using System.Text.Json;

namespace MedSmarter.Api.Http;

public static class HealthEndpoints
{
    /// <summary>
    /// <c>/health/live</c>: process is up (no dependencies). <c>/health/ready</c>: all dependencies reachable.
    /// Output is intentionally minimal (names + status, never exception messages).
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = Write,
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadyTag),
            ResponseWriter = Write,
        }).AllowAnonymous();

        return app;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static Task Write(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.CacheControl = "no-store";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
