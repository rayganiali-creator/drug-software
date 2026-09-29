using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.BuildingBlocks;

/// <summary>
/// Entry point of a bounded module. The Host discovers modules through this interface only;
/// it never reaches into module internals.
/// </summary>
public interface IModule
{
    /// <summary>Stable module name, also used as the PostgreSQL schema prefix.</summary>
    string Name { get; }

    void Register(IServiceCollection services, IConfiguration configuration);
}
