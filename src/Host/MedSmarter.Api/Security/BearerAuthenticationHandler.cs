using System.Security.Claims;
using System.Text.Encodings.Web;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MedSmarter.Api.Security;

public static class BearerDefaults
{
    public const string Scheme = "Bearer";
    public const string CurrentUserItem = "medsmarter.current-user";
}

/// <summary>
/// Validates the access token (signature, issuer, audience, lifetime) and then RE-RESOLVES the user on the server:
/// the session must still be valid, the account active and roles current. The token carries no roles or PHI.
/// </summary>
public sealed class BearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITokenService tokens,
    IUserIdentityService users) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return AuthenticateResult.NoResult();
        }

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("scheme");
        }

        var claims = tokens.ValidateAccessToken(header["Bearer ".Length..].Trim());
        if (claims is null)
        {
            return AuthenticateResult.Fail("token");
        }

        var user = await users.GetCurrentUserAsync(claims.UserId, claims.SessionId, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("session");
        }

        Context.Items[BearerDefaults.CurrentUserItem] = user;
        var identity = new ClaimsIdentity([new Claim("sub", user.UserId.ToString()), new Claim("sid", user.SessionId.ToString())], BearerDefaults.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), BearerDefaults.Scheme));
    }
}
