using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Users;

/// <summary>Phase 1 placeholder: the Users module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class UsersModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Users";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
