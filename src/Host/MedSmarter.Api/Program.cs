using MedSmarter.Api;
using MedSmarter.Api.Http;
using MedSmarter.Api.Security;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
        .WithMethods("GET", "POST", "DELETE")
        .WithHeaders("Accept", "Authorization", "Content-Type", "X-Client", CorrelationIdMiddleware.HeaderName)
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
    builder.Services.AddPlatformInfrastructure(builder.Configuration);
    foreach (var module in ModuleCatalog.All)
    {
        module.Register(builder.Services, builder.Configuration);
    }

    // ---- Phase 3: authentication / authorization ----
    var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
    AuthGuard.EnsureSafe(builder.Environment.EnvironmentName, authOptions); // DevelopmentMock can never start in production
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddAuthentication(BearerDefaults.Scheme).AddScheme<AuthenticationSchemeOptions, BearerAuthenticationHandler>(BearerDefaults.Scheme, null);
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder(BearerDefaults.Scheme).RequireAuthenticatedUser().Build()); // deny by default
    builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, UniformAuthorizationResultHandler>();

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
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        await next();
    });
    app.UseAuthentication();
    app.UseAuthorization();
    // Request logging records method/path/status/latency only (no query string, no bodies: PHI safety).
    app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information);

    if (app.Services.GetService<IDemoIdentitySeeder>() is { } seeder)
    {
        await seeder.SeedAsync(); // fictional identities only; registered solely in DevelopmentMock mode
        Log.Warning("DEV AUTH: DevelopmentMock is active with fictional demo identities. Never use outside local development.");
    }

    app.MapSecurity(app.Services.GetService<IDemoAccountDirectory>() is not null);
    app.MapPlatformHealth();
    app.MapGet("/version", () => Results.Ok(new
    {
        service = "medsmarter-api",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        modules = ModuleCatalog.All.Select(m => m.Name),
    })).AllowAnonymous();

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
