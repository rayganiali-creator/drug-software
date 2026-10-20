using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Identity.Contracts;

namespace MedSmarter.Modules.Identity;

public static class AuthModes
{
    /// <summary>Accounts are fictional and there are no passwords. Refused outside Development/Testing.</summary>
    public const string DevelopmentMock = "DevelopmentMock";

    /// <summary>No provider registered: login is impossible until a real provider is wired in (later phase).</summary>
    public const string Disabled = "Disabled";
}

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Mode { get; set; } = AuthModes.Disabled;

    /// <summary>
    /// HMAC key for access tokens (at least 32 characters). Comes from configuration/secret store only.
    /// Empty in Development means a random per-process key is generated (tokens die on restart).
    /// </summary>
    public string? SigningKey { get; set; }

    public string Issuer { get; set; } = "medsmarter-api";
    public string Audience { get; set; } = "medsmarter-clients";
    public int AccessTokenMinutes { get; set; } = 10;
    public int SessionDays { get; set; } = 7;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Public-demo allow-list (Auth:DemoAllowedAccounts). When set, ONLY these demo accounts can sign in and only these are listed by
    /// /auth/demo-accounts; every other account is refused server-side. Accounts holding an administrative role are never allowed here
    /// (see <see cref="DemoAccountPolicy"/>). The Demo environment requires it.
    /// </summary>
    public string[]? DemoAllowedAccounts { get; set; }
}

/// <summary>Which demo accounts a public demo may expose. Administrative roles can never be put on the public allow-list.</summary>
public static class DemoAccountPolicy
{
    /// <summary>Roles that carry administrative or content-governance power. An account with any of them is never publicly selectable.</summary>
    public static readonly IReadOnlySet<string> PrivilegedRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        RoleNames.SystemAdmin, RoleNames.AIManager, RoleNames.ContentManager, RoleNames.PharmacyAdmin,
    };

    /// <summary>Throws when the allow-list names an unknown, non-login, or privileged account.</summary>
    public static void Validate(IEnumerable<string> allowed, AccessCatalog catalog)
    {
        foreach (var id in allowed)
        {
            var account = catalog.File.DemoAccounts.FirstOrDefault(a => a.Id == id)
                ?? throw new InvalidOperationException($"Auth:DemoAllowedAccounts names an unknown demo account '{id}'. Refusing to start.");
            if (!account.LoginEnabled)
            {
                throw new InvalidOperationException($"Auth:DemoAllowedAccounts names '{id}', which cannot sign in. Refusing to start.");
            }

            if (account.Roles.Any(r => PrivilegedRoles.Contains(r.Role)))
            {
                throw new InvalidOperationException($"Auth:DemoAllowedAccounts must not contain privileged account '{id}'. Refusing to start.");
            }
        }
    }
}

public static class AuthGuard
{
    /// <summary>Throws when the mock provider is enabled in an environment that could be production.</summary>
    public static void EnsureSafe(string environmentName, AuthOptions options)
    {
        var devLike = EnvironmentRules.IsDevelopmentOrTesting(environmentName);
        var mockAllowed = EnvironmentRules.AllowsDemoFeatures(environmentName); // the dedicated Demo environment may use the mock provider, but still needs a real signing key
        if (options.Mode == AuthModes.DevelopmentMock && !mockAllowed)
        {
            throw new InvalidOperationException(
                $"Auth:Mode=DevelopmentMock is only allowed in Development/Testing/Demo (environment is '{environmentName}'). Refusing to start.");
        }

        if (!devLike && (options.SigningKey is null || options.SigningKey.Length < 32))
        {
            throw new InvalidOperationException("Auth:SigningKey (>= 32 chars) must be supplied outside Development/Testing.");
        }
    }
}
