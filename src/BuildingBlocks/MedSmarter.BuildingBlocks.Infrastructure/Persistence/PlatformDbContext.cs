using Microsoft.EntityFrameworkCore;

namespace MedSmarter.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Owns the cross-cutting <c>platform</c> schema (outbox / inbox). Business modules get their own
/// schema and DbContext in later phases; this context intentionally knows nothing about them.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
    }
}
