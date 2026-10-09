using System.Security.Cryptography;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Security.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>The three security modules wired in memory with a fake clock and fictional seeded identities.</summary>
public sealed class Env : IDisposable
{
    private readonly ServiceProvider _sp;

    public Env(string mode = AuthModes.DevelopmentMock, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = mode,
            ["Persistence:Provider"] = "InMemory",
            ["Auth:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), // random per test run, never a real secret
        };
        foreach (var (k, v) in extra ?? [])
        {
            settings[k] = v;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        new AuditModule().Register(services, config);
        new ConsentModule().Register(services, config);
        new IdentityModule().Register(services, config);
        _sp = services.BuildServiceProvider();
        Catalog = _sp.GetRequiredService<AccessCatalog>();
        if (mode == AuthModes.DevelopmentMock)
        {
            _sp.GetRequiredService<IDemoIdentitySeeder>().SeedAsync().GetAwaiter().GetResult();
        }
    }

    public FakeClock Clock { get; } = new();
    public AccessCatalog Catalog { get; }
    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();
    public IAuthenticationService Auth => Get<IAuthenticationService>();
    public IAccessAuthorizer Authz => Get<IAccessAuthorizer>();
    public IAuditReader AuditReader => Get<IAuditReader>();
    public IConsentService Consents => Get<IConsentService>();
    public IUserIdentityService Users => Get<IUserIdentityService>();
    public static RequestContext Rc { get; } = new("test", "corr-12345678");

    public Guid UserId(string accountId) => Catalog.File.DemoAccounts.Single(a => a.Id == accountId).UserId;
    public Guid OrgId(string key) => Catalog.File.Organizations.Single(o => o.Key == key).Id;

    public Task<AuthOutcome> LoginAsync(string accountId, string? deviceId = null) =>
        Auth.LoginAsync(new LoginRequest("mock", new Dictionary<string, string> { ["accountId"] = accountId }, new DeviceInfo(deviceId, "web", "test", "Test browser")), Rc);

    public async Task<CurrentUser> ActorAsync(string accountId)
    {
        var outcome = await LoginAsync(accountId);
        Assert.True(outcome.Succeeded, $"login {accountId}");
        return (await Users.GetCurrentUserAsync(outcome.Result!.User.Id, outcome.Result.SessionId))!;
    }

    public async Task<AccessDecision> CanAsync(string accountId, string permission, AccessResource resource) =>
        await Authz.AuthorizeAsync(await ActorAsync(accountId), permission, resource, Rc);

    public void Dispose() => _sp.Dispose();
}
