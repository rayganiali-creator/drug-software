using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Api.Security;

public sealed record DeviceBody(string? DeviceId, string? Platform, string? AppVersion, string? DeviceName);
public sealed record LoginBody(string? Provider, Dictionary<string, string>? Credentials, DeviceBody? Device);
public sealed record RefreshBody(string? RefreshToken);
public sealed record GrantConsentBody(Guid? GranteeUserId, Guid? GranteeOrganizationId, string? Purpose, List<string>? Scope, DateTimeOffset ExpiresAt, string? Version);
public sealed record AssignRoleBody(string? Role, Guid? OrganizationId);

/// <summary>
/// Auth, session, consent, audit and admin endpoints plus the (mock-data) resource endpoints that exercise every
/// authorization layer. Every endpoint is authenticated by default (fallback policy) and needs a permission.
/// </summary>
public static class SecurityEndpoints
{
    private const string DemoNotice = "DEMO DATA - NOT FOR CLINICAL USE";
    private static readonly string[] Sources = ["web", "android", "ios"];

    private static CurrentUser Actor(HttpContext ctx) => (CurrentUser)ctx.Items[BearerDefaults.CurrentUserItem]!;

    private static RequestContext Ctx(HttpContext ctx)
    {
        var claimed = ctx.Request.Headers["X-Client"].FirstOrDefault();
        return new RequestContext(claimed is not null && Sources.Contains(claimed) ? claimed : "api", ctx.TraceIdentifier);
    }

    private static string Perm(string permission) => PermissionPolicyProvider.Prefix + permission;

    public static IEndpointRouteBuilder MapSecurity(this IEndpointRouteBuilder app, bool demoAuth)
    {
        MapAuth(app, demoAuth);
        MapSessions(app);
        MapConsents(app);
        MapAudit(app);
        MapAdmin(app);
        MapResources(app);
        return app;
    }

    private static void MapAuth(IEndpointRouteBuilder app, bool demoAuth)
    {
        var auth = app.MapGroup("/auth");

        auth.MapPost("/login", async (LoginBody body, HttpContext ctx, IAuthenticationService svc) =>
        {
            var device = new DeviceInfo(body.Device?.DeviceId, body.Device?.Platform ?? "other", body.Device?.AppVersion, body.Device?.DeviceName);
            var outcome = await svc.LoginAsync(new LoginRequest(body.Provider ?? "mock", body.Credentials ?? [], device), Ctx(ctx), ctx.RequestAborted);
            if (outcome.Succeeded)
            {
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Ok(outcome.Result);
            }

            return outcome.Failure switch
            {
                AuthFailure.AccountDisabled => CodedProblem(StatusCodes.Status403Forbidden, "Account disabled", "account_disabled"),
                AuthFailure.NoActiveRole => CodedProblem(StatusCodes.Status403Forbidden, "No active role", "no_active_role"),
                AuthFailure.Throttled => CodedProblem(StatusCodes.Status429TooManyRequests, "Too many attempts", "throttled"),
                _ => CodedProblem(StatusCodes.Status401Unauthorized, "Authentication failed", "invalid_credentials"),
            };
        }).AllowAnonymous();

        auth.MapPost("/refresh", async (RefreshBody body, HttpContext ctx, IAuthenticationService svc) =>
        {
            var outcome = await svc.RefreshAsync(body.RefreshToken ?? string.Empty, Ctx(ctx), ctx.RequestAborted);
            if (!outcome.Succeeded)
            {
                return CodedProblem(StatusCodes.Status401Unauthorized, "Authentication failed", "refresh_invalid");
            }

            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(outcome.Result);
        }).AllowAnonymous();

        auth.MapPost("/logout", async (HttpContext ctx, IAuthenticationService svc) =>
        {
            var a = Actor(ctx);
            await svc.LogoutAsync(a.UserId, a.SessionId, Ctx(ctx), ctx.RequestAborted);
            return Results.NoContent();
        });

        auth.MapPost("/logout-all", async (HttpContext ctx, IAuthenticationService svc) =>
        {
            await svc.LogoutAllAsync(Actor(ctx).UserId, Ctx(ctx), ctx.RequestAborted);
            return Results.NoContent();
        });

        auth.MapGet("/me", async (HttpContext ctx, IUserIdentityService users) =>
            await users.GetSummaryAsync(Actor(ctx).UserId, ctx.RequestAborted) is { } me ? Results.Ok(me) : Problem.UnauthenticatedResult());

        if (demoAuth)
        {
            // DEVELOPMENT ONLY: only mapped when Auth:Mode=DevelopmentMock (which the API refuses outside Development/Testing).
            auth.MapGet("/demo-accounts", (IDemoAccountDirectory directory) => Results.Ok(new { notice = "DEMO ENVIRONMENT - fictional accounts, no passwords", accounts = directory.List() })).AllowAnonymous();
        }
    }

    private static void MapSessions(IEndpointRouteBuilder app)
    {
        app.MapGet("/sessions", async (HttpContext ctx, ISessionService sessions) =>
        {
            var a = Actor(ctx);
            return Results.Ok(await sessions.ListAsync(a.UserId, a.SessionId, ctx.RequestAborted));
        }).RequireAuthorization(Perm(Permissions.SessionRead));

        app.MapDelete("/sessions/{id:guid}", async (Guid id, HttpContext ctx, ISessionService sessions) =>
        {
            var a = Actor(ctx);
            return await sessions.RevokeAsync(a.UserId, id, a.UserId, "user_revoked", Ctx(ctx), ctx.RequestAborted) ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(Perm(Permissions.SessionRevoke));
    }

    private static void MapConsents(IEndpointRouteBuilder app)
    {
        app.MapGet("/consents", async (HttpContext ctx, IConsentService consents, IUserIdentityService users) =>
        {
            var list = await consents.ListGivenAsync(Actor(ctx).UserId, ctx.RequestAborted);
            var names = new Dictionary<Guid, string>();
            foreach (var id in list.Where(c => c.GranteeUserId is not null).Select(c => c.GranteeUserId!.Value).Distinct())
            {
                names[id] = (await users.GetSummaryAsync(id, ctx.RequestAborted))?.DisplayName ?? "Unknown";
            }

            return Results.Ok(list.Select(c => new { consent = c, granteeName = c.GranteeUserId is { } g ? names[g] : null }));
        }).RequireAuthorization(Perm(Permissions.ConsentRead));

        app.MapGet("/directory/providers", async (HttpContext ctx, IUserIdentityService users) =>
        {
            var providers = (await users.ListUsersAsync(ctx.RequestAborted))
                .Where(u => u.Status == nameof(UserStatus.Active) && u.Roles.Any(r => r is RoleNames.Physician or RoleNames.Pharmacist))
                .Select(u => new { id = u.Id, displayName = u.DisplayName, roles = u.Roles.Where(r => r is RoleNames.Physician or RoleNames.Pharmacist) });
            return Results.Ok(providers);
        }).RequireAuthorization(Perm(Permissions.ConsentGrant));

        app.MapPost("/consents", async (GrantConsentBody body, HttpContext ctx, IConsentService consents) =>
        {
            var a = Actor(ctx);
            var outcome = await consents.GrantAsync(
                a.UserId,
                new GrantConsentCommand(a.UserId, body.GranteeUserId, body.GranteeOrganizationId, body.Purpose ?? string.Empty, body.Scope ?? [], body.ExpiresAt, body.Version ?? string.Empty),
                Ctx(ctx).Source, ctx.TraceIdentifier, ctx.RequestAborted);
            return outcome.Succeeded
                ? Results.Created($"/consents/{outcome.Consent!.Id}", outcome.Consent)
                : CodedProblem(StatusCodes.Status400BadRequest, "Invalid consent", outcome.Error.ToString());
        }).RequireAuthorization(Perm(Permissions.ConsentGrant));

        app.MapDelete("/consents/{id:guid}", async (Guid id, HttpContext ctx, IConsentService consents) =>
        {
            var outcome = await consents.RevokeAsync(Actor(ctx).UserId, id, Ctx(ctx).Source, ctx.TraceIdentifier, ctx.RequestAborted);
            return outcome.Succeeded ? Results.Ok(outcome.Consent) : Results.NotFound(); // not-found and not-yours look identical
        }).RequireAuthorization(Perm(Permissions.ConsentRevoke));
    }

    private static void MapAudit(IEndpointRouteBuilder app)
    {
        // "Who accessed MY data" - a subject can always see accesses to their own records.
        app.MapGet("/audit/me", async (HttpContext ctx, IAuditReader reader, IUserIdentityService users) =>
        {
            var entries = await reader.QueryAsync(new AuditQuery(SubjectUserId: Actor(ctx).UserId, Take: 100), ctx.RequestAborted);
            var view = new List<object>();
            foreach (var e in entries.Where(e => e.Action is AuditActions.PatientDataAccessed or AuditActions.PrescriptionAccessed or AuditActions.AdrAccessed or AuditActions.ConsentGranted or AuditActions.ConsentRevoked))
            {
                var name = e.ActorUserId is { } id ? (await users.GetSummaryAsync(id, ctx.RequestAborted))?.DisplayName : null;
                view.Add(new { e.Timestamp, e.Action, e.Result, actorName = name, e.ResourceType });
            }

            return Results.Ok(view);
        }).RequireAuthorization(Perm(Permissions.AuditReadOwn));

        app.MapGet("/audit", async (HttpContext ctx, IAuditReader reader, IAuditWriter writer, Guid? actor, Guid? subject, string? action, int? take) =>
        {
            var a = Actor(ctx);
            var entries = await reader.QueryAsync(new AuditQuery(actor, subject, action, take ?? 100), ctx.RequestAborted);
            await writer.WriteAsync(new AuditEvent(AuditActions.AuditRead, AuditResult.Success, a.UserId, "audit", null, null, Ctx(ctx).Source, ctx.TraceIdentifier), ctx.RequestAborted);
            return Results.Ok(entries);
        }).RequireAuthorization(Perm(Permissions.AuditRead));

        app.MapGet("/audit/verify", async (IAuditReader reader, CancellationToken ct) => Results.Ok(new { intact = await reader.VerifyChainAsync(ct) }))
            .RequireAuthorization(Perm(Permissions.AuditRead));
    }

    private static void MapAdmin(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin");

        admin.MapGet("/users", async (HttpContext ctx, IUserIdentityService users) => Results.Ok(await users.ListUsersAsync(ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.UserRead));

        admin.MapPost("/users/{id:guid}/roles", async (Guid id, AssignRoleBody body, HttpContext ctx, IUserAdministrationService svc) =>
            AdminResult(await svc.AssignRoleAsync(Actor(ctx).UserId, id, body.Role ?? string.Empty, body.OrganizationId, Ctx(ctx), ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.RoleManage));

        admin.MapDelete("/users/{id:guid}/roles/{role}", async (Guid id, string role, HttpContext ctx, IUserAdministrationService svc) =>
            AdminResult(await svc.RevokeRoleAsync(Actor(ctx).UserId, id, role, Ctx(ctx), ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.RoleManage));

        admin.MapDelete("/sessions/{id:guid}", async (Guid id, HttpContext ctx, ISessionService sessions) =>
            await sessions.RevokeAsync(Actor(ctx).UserId, id, null, "admin_revoked", Ctx(ctx), ctx.RequestAborted) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(Perm(Permissions.SessionRevokeAny));
    }

    private static IResult AdminResult(AdminError error) => error switch
    {
        AdminError.None => Results.NoContent(),
        AdminError.UserNotFound => Results.NotFound(),
        _ => CodedProblem(StatusCodes.Status409Conflict, "Role change rejected", error.ToString()),
    };

    // ---- resources (fictional payloads; the point is the authorization path, not the data) ----

    private sealed record Kind(string Route, string Permission, string AuditAction);

    private static readonly Kind[] PatientKinds =
    [
        new("profile", Permissions.PatientProfileRead, AuditActions.PatientDataAccessed),
        new("medications", Permissions.PatientMedicationsRead, AuditActions.PatientDataAccessed),
        new("prescriptions", Permissions.PatientPrescriptionsRead, AuditActions.PrescriptionAccessed),
        new("adherence", Permissions.PatientAdherenceRead, AuditActions.PatientDataAccessed),
        new("adr", Permissions.PatientAdrRead, AuditActions.AdrAccessed),
        new("checkins", Permissions.PatientCheckinsRead, AuditActions.PatientDataAccessed),
        new("symptoms", Permissions.PatientSymptomsRead, AuditActions.PatientDataAccessed),
        new("ai-summary", Permissions.PatientAiSummaryRead, AuditActions.PatientDataAccessed),
    ];

    private static void MapResources(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients", async (HttpContext ctx, IUserIdentityService users) =>
            Results.Ok(await users.ListRelatedPatientsAsync(Actor(ctx), ctx.RequestAborted)))
            .RequireAuthorization(Perm(Permissions.PatientProfileRead));

        foreach (var kind in PatientKinds)
        {
            app.MapGet($"/patients/{{id:guid}}/{kind.Route}", async (Guid id, HttpContext ctx, IAccessAuthorizer authz, IAuditWriter audit, IUserIdentityService users) =>
            {
                var actor = Actor(ctx);
                var decision = await authz.AuthorizeAsync(actor, kind.Permission, AccessResource.Patient(id, kind.Route), Ctx(ctx), ctx.RequestAborted);
                if (!decision.Allowed)
                {
                    return Problem.ForbiddenResult(); // same answer for "no such patient" and "not allowed"
                }

                await audit.WriteAsync(new AuditEvent(kind.AuditAction, AuditResult.Success, actor.UserId, kind.Route, id.ToString(), id, Ctx(ctx).Source, ctx.TraceIdentifier), ctx.RequestAborted);
                var name = (await users.GetSummaryAsync(id, ctx.RequestAborted))?.DisplayName;
                return Results.Ok(DemoPayload(kind.Route, name));
            }).RequireAuthorization(Perm(kind.Permission));
        }

        app.MapPost("/patients/{id:guid}/prescriptions", async (Guid id, HttpContext ctx, IAccessAuthorizer authz) =>
        {
            var decision = await authz.AuthorizeAsync(Actor(ctx), Permissions.PrescriptionCreate, AccessResource.Patient(id, "prescription"), Ctx(ctx), ctx.RequestAborted);
            // Phase 3 proves who may do this; nothing is persisted (no prescription module yet).
            return decision.Allowed ? Results.Ok(new { authorized = true, persisted = false, notice = DemoNotice }) : Problem.ForbiddenResult();
        }).RequireAuthorization(Perm(Permissions.PrescriptionCreate));

        app.MapGet("/organizations/{orgId:guid}/inventory", async (Guid orgId, HttpContext ctx, IAccessAuthorizer authz) =>
        {
            var decision = await authz.AuthorizeAsync(Actor(ctx), Permissions.PharmacyInventoryRead, AccessResource.Organization(orgId), Ctx(ctx), ctx.RequestAborted);
            return decision.Allowed
                ? Results.Ok(new { demo = true, notice = DemoNotice, items = new[] { new { name = "Demopril 5 mg", stock = 120 }, new { name = "Nocturin 10 mg", stock = 8 } } })
                : Problem.ForbiddenResult();
        }).RequireAuthorization(Perm(Permissions.PharmacyInventoryRead));

        app.MapGet("/organizations/{orgId:guid}/patients/{id:guid}/prescriptions", async (Guid orgId, Guid id, HttpContext ctx, IAccessAuthorizer authz, IAuditWriter audit) =>
        {
            var actor = Actor(ctx);
            var decision = await authz.AuthorizeAsync(actor, Permissions.PharmacyPrescriptionsRead, AccessResource.OrgPatient(orgId, id, "prescriptions"), Ctx(ctx), ctx.RequestAborted);
            if (!decision.Allowed)
            {
                return Problem.ForbiddenResult();
            }

            await audit.WriteAsync(new AuditEvent(AuditActions.PrescriptionAccessed, AuditResult.Success, actor.UserId, "prescriptions", id.ToString(), id, Ctx(ctx).Source, ctx.TraceIdentifier), ctx.RequestAborted);
            return Results.Ok(DemoPayload("prescriptions", null));
        }).RequireAuthorization(Perm(Permissions.PharmacyPrescriptionsRead));

        app.MapGet("/analytics/summary", () => Results.Ok(new
        {
            demo = true,
            notice = DemoNotice,
            note = "Aggregate only. No patient-level data. Groups smaller than k are suppressed (k is a demo value).",
            cohorts = new[] { new { label = "Adherence >= 80%", share = 0.71 }, new { label = "Reported mild ADR", share = 0.09 } },
        })).RequireAuthorization(Perm(Permissions.AnalyticsRead));
    }

    private static object DemoPayload(string kind, string? name) => kind switch
    {
        "profile" => new { demo = true, notice = DemoNotice, kind, displayName = name },
        "medications" => new { demo = true, notice = DemoNotice, kind, items = new[] { new { name = "Demopril", dose = "5 mg" }, new { name = "Nocturin", dose = "10 mg" } } },
        _ => new { demo = true, notice = DemoNotice, kind, items = Array.Empty<string>() },
    };

    private static IResult CodedProblem(int status, string title, string code) =>
        Results.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
