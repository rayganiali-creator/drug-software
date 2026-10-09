using MedSmarter.Api;
using MedSmarter.BuildingBlocks;
using MedSmarter.Api.Http;
using MedSmarter.Api.Knowledge;
using MedSmarter.Api.Patients;
using MedSmarter.Api.Security;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.Medications;
using MedSmarter.Modules.Patients;
using MedSmarter.Modules.Patients.Contracts;
using MedSmarter.Modules.Guidance.Contracts;
using MedSmarter.Modules.ProductTrace;
using System.Threading.RateLimiting;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications.Contracts;
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
    // Malformed input is the caller's mistake: always a plain 400 (also in Development, where the default would throw and become a 500).
    builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

    // CORS is deny-all unless origins are configured (Cors__AllowedOrigins__0=...). Read-only for now.
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(origins)
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Accept", "Authorization", "Content-Type", "If-Match", "X-Client", CorrelationIdMiddleware.HeaderName)
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
    builder.Services.AddPlatformInfrastructure(builder.Configuration);
    foreach (var module in ModuleCatalog.All)
    {
        module.Register(builder.Services, builder.Configuration);
    }

    // ---- Phase 3: authentication / authorization ----
    var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
    if (!args.Contains("--migrate-and-exit"))
    {
        PersistenceSettings.EnsureSafe(builder.Environment.EnvironmentName, builder.Configuration); // volatile storage can never run outside Development/Testing
        AuthGuard.EnsureSafe(builder.Environment.EnvironmentName, authOptions); // DevelopmentMock can never start in production
        MedicationsGuard.EnsureSafe(builder.Environment.EnvironmentName, builder.Configuration.GetSection(MedicationsOptions.Section).Get<MedicationsOptions>() ?? new MedicationsOptions());
        if (AiGuard.EnsureSafe(builder.Environment.EnvironmentName, builder.Configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions()) is { } aiWarning)
        {
            Log.Warning("{Warning}", aiWarning); // a deliberate demo setup outside development: never silent
        }

        PatientsGuard.EnsureSafe(builder.Environment.EnvironmentName, builder.Configuration.GetSection(PatientsOptions.Section).Get<PatientsOptions>() ?? new PatientsOptions());
        ManufacturerReportsGuard.EnsureSafe(builder.Environment.EnvironmentName, builder.Configuration.GetSection(ManufacturerReportsOptions.Section).Get<ManufacturerReportsOptions>() ?? new ManufacturerReportsOptions());
        if (!PersistenceSettings.IsDevLike(builder.Environment.EnvironmentName) && builder.Configuration.GetSection("Guidance").GetValue<bool>("SeedDemoData"))
        {
            throw new InvalidOperationException("Guidance:SeedDemoData is only allowed in Development/Testing. Refusing to start.");
        }
    }

    // Per-caller rate limits (user id when signed in, otherwise client address). Limits are per instance; a shared limiter replaces them when scaling out.
    string CallerKey(HttpContext c) => c.User.FindFirst("sub")?.Value ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(KnowledgeEndpoints.SearchLimiter, c => RateLimitPartition.GetFixedWindowLimiter(CallerKey(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:SearchPerMinute", 120), Window = TimeSpan.FromMinutes(1) }));
        o.AddPolicy(KnowledgeEndpoints.AiLimiter, c => RateLimitPartition.GetFixedWindowLimiter(CallerKey(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AiPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
        o.AddPolicy(KnowledgeEndpoints.WriteLimiter, c => RateLimitPartition.GetFixedWindowLimiter(CallerKey(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:WritePerMinute", 60), Window = TimeSpan.FromMinutes(1) }));
    });

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
        foreach (var migrator in scope.ServiceProvider.GetServices<IDatabaseMigrator>())
        {
            await migrator.MigrateAsync(CancellationToken.None);
            Log.Information("Migrations applied for {Database}", migrator.Name);
        }

        Log.Information("Database migrations applied");
        return 0;
    }

    if (PersistenceSettings.MigrateOnStartup(app.Configuration) && PersistenceSettings.UsePostgres(app.Configuration))
    {
        // Developer convenience only (PersistenceSettings.EnsureSafe refuses it outside Development/Testing).
        foreach (var migrator in app.Services.GetServices<IDatabaseMigrator>())
        {
            await migrator.MigrateAsync(CancellationToken.None);
        }

        Log.Warning("DEV DATA: migrations were applied at startup (Persistence:MigrateOnStartup).");
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
    app.UseRateLimiter();
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

    if (app.Services.GetService<IDemoMedicationSeeder>() is { } medSeeder)
    {
        await medSeeder.SeedAsync(); // fictional medications only; registered solely when Medications:SeedDemoData is on
        Log.Warning("DEV DATA: fictional demo medications are loaded. Never enable outside local development.");
    }

    if (app.Services.GetService<IDemoPatientSeeder>() is { } patientSeeder && app.Services.GetService<IDemoCareData>() is not null)
    {
        await patientSeeder.SeedAsync(); // fictional patients, records and care relationships (needs the demo identities)
        Log.Warning("DEV DATA: fictional demo patients are loaded. Never enable outside local development.");
        if (app.Services.GetService<IDemoGuidanceSeeder>() is { } guidanceSeeder)
        {
            await guidanceSeeder.SeedAsync();
        }
    }

    app.MapKnowledge();
    app.MapPatientRecords();
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
