using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace MedSmarter.Modules.Identity;

/// <summary>
/// DEVELOPMENT ONLY. "Authenticates" by account id from the fictional demo catalog. There are no passwords and no
/// secrets. It only exists when Auth:Mode=DevelopmentMock and AuthGuard has passed, so it cannot be enabled in production.
/// </summary>
public sealed class MockAuthenticationProvider(AccessCatalog catalog, IOptions<AuthOptions> options) : IAuthenticationProvider, IDemoAccountDirectory
{
    public const string ProviderId = "mock";

    public string Id => ProviderId;

    public Task<ProviderIdentity?> AuthenticateAsync(IReadOnlyDictionary<string, string> credentials, CancellationToken ct = default)
    {
        if (options.Value.Mode != AuthModes.DevelopmentMock
            || !credentials.TryGetValue("accountId", out var accountId)
            || catalog.File.DemoAccounts.All(a => a.Id != accountId || !a.LoginEnabled))
        {
            return Task.FromResult<ProviderIdentity?>(null);
        }

        return Task.FromResult<ProviderIdentity?>(new ProviderIdentity(ProviderId, accountId));
    }

    public IReadOnlyList<DemoAccountInfo> List() =>
        [.. catalog.File.DemoAccounts.Where(a => a.LoginEnabled).Select(a =>
            new DemoAccountInfo(a.Id, a.DisplayName, a.Description, [.. a.Roles.Select(r => r.Role).Distinct()], a.Primary, a.Status))];
}
