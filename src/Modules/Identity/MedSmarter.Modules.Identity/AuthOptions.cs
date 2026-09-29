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
}

public static class AuthGuard
{
    /// <summary>Throws when the mock provider is enabled in an environment that could be production.</summary>
    public static void EnsureSafe(string environmentName, AuthOptions options)
    {
        var devLike = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        if (options.Mode == AuthModes.DevelopmentMock && !devLike)
        {
            throw new InvalidOperationException(
                $"Auth:Mode=DevelopmentMock is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start.");
        }

        if (!devLike && (options.SigningKey is null || options.SigningKey.Length < 32))
        {
            throw new InvalidOperationException("Auth:SigningKey (>= 32 chars) must be supplied outside Development/Testing.");
        }
    }
}
