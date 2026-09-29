using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace MedSmarter.Api.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Layer 1 (RBAC): the caller's server-resolved roles must grant the permission. Resource checks happen in the endpoint.</summary>
public sealed class PermissionHandler(IHttpContextAccessor accessor) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (accessor.HttpContext?.Items[BearerDefaults.CurrentUserItem] is CurrentUser user && user.Has(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Builds "perm:xxx" policies on demand; anything else falls back to the default provider.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder(BearerDefaults.Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>
/// One uniform 401 and one uniform 403. Neither says which role, permission or resource was missing;
/// the precise reason goes to the audit log only.
/// </summary>
public sealed class UniformAuthorizationResultHandler(IAuditWriter audit) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await Problem.Unauthenticated(context);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            var user = context.Items[BearerDefaults.CurrentUserItem] as CurrentUser;
            var permission = policy.Requirements.OfType<PermissionRequirement>().FirstOrDefault()?.Permission;
            await audit.WriteAsync(new AuditEvent(AuditActions.AccessDenied, AuditResult.Denied, user?.UserId, "endpoint", context.Request.Path.Value, null, "api", context.TraceIdentifier,
                "Rbac:role_lacks_permission", permission is null ? null : new Dictionary<string, string> { ["permission"] = permission }), context.RequestAborted);
            await Problem.Forbidden(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class Problem
{
    public static Task Unauthenticated(HttpContext ctx)
    {
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return Write(ctx, StatusCodes.Status401Unauthorized, "Authentication required");
    }

    public static Task Forbidden(HttpContext ctx) => Write(ctx, StatusCodes.Status403Forbidden, "Access denied");

    public static IResult ForbiddenResult() => Results.Problem(title: "Access denied", statusCode: StatusCodes.Status403Forbidden);

    public static IResult UnauthenticatedResult() => Results.Problem(title: "Authentication required", statusCode: StatusCodes.Status401Unauthorized);

    private static Task Write(HttpContext ctx, int status, string title)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Problem(title: title, statusCode: status).ExecuteAsync(ctx);
    }
}
