using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedSmarter.Modules.Identity;

/// <summary>Identity, authentication, authorization and sessions (see docs/phase3).</summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        var mode = configuration.GetSection(AuthOptions.Section)[nameof(AuthOptions.Mode)] ?? AuthModes.Disabled;

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton(AccessCatalog.LoadEmbedded());
        services.AddSingleton<InMemoryIdentityStore>();
        services.AddSingleton<IIdentityStore>(sp => sp.GetRequiredService<InMemoryIdentityStore>());
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<InMemoryIdentityStore>());
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<UserIdentityService>();
        services.AddSingleton<IUserIdentityService>(sp => sp.GetRequiredService<UserIdentityService>());
        services.AddSingleton<IUserAdministrationService, UserAdministrationService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IAccessAuthorizer, AccessAuthorizer>();

        if (mode == AuthModes.DevelopmentMock)
        {
            services.AddSingleton<MockAuthenticationProvider>();
            services.AddSingleton<IAuthenticationProvider>(sp => sp.GetRequiredService<MockAuthenticationProvider>());
            services.AddSingleton<IDemoAccountDirectory>(sp => sp.GetRequiredService<MockAuthenticationProvider>());
            services.AddSingleton<IDemoIdentitySeeder, DemoIdentitySeeder>();
        }
    }
}
