using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Notifications;

/// <summary>Phase 1 placeholder: the Notifications module has no behaviour yet (see docs/phase0/03-modules.md).</summary>
public sealed class NotificationsModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Notifications";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }
}
