using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Design-time only (dotnet ef). The connection string is read from the environment so no credential
/// ever lives in source; the dummy fallback is enough for scaffolding migrations offline.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                 ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.Schema))
            .Options;
        return new PlatformDbContext(options);
    }
}
