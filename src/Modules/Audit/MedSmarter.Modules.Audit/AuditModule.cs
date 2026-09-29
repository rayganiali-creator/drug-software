using MedSmarter.Modules.Audit.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Modules.Audit;

/// <summary>Centralised audit log (see docs/phase3/06-audit-log.md).</summary>
public sealed class AuditModule : MedSmarter.BuildingBlocks.IModule
{
    public string Name => "Audit";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<InMemoryAuditStore>();
        services.AddSingleton<IAuditStore>(sp => sp.GetRequiredService<InMemoryAuditStore>());
        services.AddSingleton<AuditService>();
        services.AddSingleton<IAuditWriter>(sp => sp.GetRequiredService<AuditService>());
        services.AddSingleton<IAuditReader>(sp => sp.GetRequiredService<AuditService>());
    }
}
