using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Identity;

/// <summary>
/// DEVELOPMENT ONLY. "Authenticates" by account id from the fictional demo catalog. There are no passwords and no
/// secrets. It only exists when Auth:Mode=DevelopmentMock and AuthGuard has passed, so it cannot be enabled in production.
/// </summary>
public sealed class MockAuthenticationProvider : IAuthenticationProvider, IDemoAccountDirectory
{
    private readonly AccessCatalog catalog;
    private readonly IOptions<AuthOptions> options;
    private readonly HashSet<string>? allowed;

    public MockAuthenticationProvider(AccessCatalog catalog, IOptions<AuthOptions> options)
    {
        this.catalog = catalog;
        this.options = options;
        if (options.Value.DemoAllowedAccounts is { Length: > 0 } list)
        {
            DemoAccountPolicy.Validate(list, catalog); // a privileged account on the public list stops the host from starting
            allowed = [.. list];
        }
    }

    /// <summary>One rule for discovery AND login: enabled in the catalog and, when a public allow-list is configured, on it.</summary>
    private bool CanSignIn(CatalogAccount a) => a.LoginEnabled && (allowed is null || allowed.Contains(a.Id));

    public const string ProviderId = "mock";

    public string Id => ProviderId;

    public Task<ProviderIdentity?> AuthenticateAsync(IReadOnlyDictionary<string, string> credentials, CancellationToken ct = default)
    {
        if (options.Value.Mode != AuthModes.DevelopmentMock
            || !credentials.TryGetValue("accountId", out var accountId)
            || catalog.File.DemoAccounts.All(a => a.Id != accountId || !CanSignIn(a)))
        {
            return Task.FromResult<ProviderIdentity?>(null);
        }

        return Task.FromResult<ProviderIdentity?>(new ProviderIdentity(ProviderId, accountId));
    }

    public IReadOnlyList<DemoAccountInfo> List() =>
        [.. catalog.File.DemoAccounts.Where(CanSignIn).Select(a =>
            new DemoAccountInfo(a.Id, a.DisplayName, a.Description, [.. a.Roles.Select(r => r.Role).Distinct()], a.Primary, a.Status))];
}
