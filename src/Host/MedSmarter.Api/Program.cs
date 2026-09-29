using MedSmarter.Api;
using MedSmarter.Api.Http;
using MedSmarter.BuildingBlocks.Infrastructure;
using MedSmarter.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Bootstrap logger so configuration errors (e.g. missing env vars) are still logged as JSON.
Log.Logger = new LoggerConfiguration().WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, _, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "medsmarter-api")
        .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

    builder.Services.AddProblemDetails();

    // CORS is deny-all unless origins are configured (Cors__AllowedOrigins__0=...). Read-only for now.
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(origins)
        .WithMethods("GET")
        .WithHeaders("Accept", CorrelationIdMiddleware.HeaderName)
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
    builder.Services.AddPlatformInfrastructure(builder.Configuration);
    foreach (var module in ModuleCatalog.All)
    {
        module.Register(builder.Services, builder.Configuration);
    }

    var app = builder.Build();

    // One-shot mode for CI/CD and docker-compose: apply migrations, then exit. The API never migrates on its own.
    if (args.Contains("--migrate-and-exit"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied");
        return 0;
    }

    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseCors();
    // Request logging records method/path/status/latency only (no query string, no bodies: PHI safety).
    app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information);

    app.MapPlatformHealth();
    app.MapGet("/version", () => Results.Ok(new
    {
        service = "medsmarter-api",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        modules = ModuleCatalog.All.Select(m => m.Name),
    }));

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
