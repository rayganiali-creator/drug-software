using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Guidance.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.Guidance.Persistence;

public sealed class GuidanceMessageRow
{
    public Guid Id { get; set; }
    /// <summary>Internal patient id (no foreign key across module schemas).</summary>
    public Guid PatientId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public GuidanceLevel Level { get; set; }
    public GuidanceStatus Status { get; set; }
    public string Locale { get; set; } = "en";
    public GuidanceConfidence Confidence { get; set; }
    public string PatientJson { get; set; } = "{}";
    public string ProfessionalJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SeenAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? ReferredAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool IsDemo { get; set; }
    public int Version { get; set; } = 1;

    // Phase 7 (additive, all nullable): where an engine-raised message came from.
    public string? OriginKind { get; set; }
    public Guid? OriginAssessmentId { get; set; }
    public string? OriginRuleId { get; set; }
    public int? OriginRuleVersion { get; set; }
    public string? OriginFindingKey { get; set; }
    public DateTimeOffset? OriginDataAsOf { get; set; }
}

public sealed class GuidanceEventRow
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public GuidanceStatus FromStatus { get; set; }
    public GuidanceStatus ToStatus { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid ActorUserId { get; set; }
    public bool ActorIsProfessional { get; set; }
}

/// <summary>PostgreSQL model of stored patient messages and their life cycle (schema <c>guidance</c>).</summary>
public sealed class GuidanceDbContext(DbContextOptions<GuidanceDbContext> options) : DbContext(options)
{
    public const string Schema = "guidance";

    public DbSet<GuidanceMessageRow> Messages => Set<GuidanceMessageRow>();
    public DbSet<GuidanceEventRow> Events => Set<GuidanceEventRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<GuidanceMessageRow>(e =>
        {
            e.ToTable("guidance_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.TemplateKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.Locale).HasMaxLength(8).IsRequired();
            e.Property(x => x.PatientJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.ProfessionalJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
            e.Property(x => x.OriginKind).HasMaxLength(32);
            e.Property(x => x.OriginRuleId).HasMaxLength(64);
            e.Property(x => x.OriginFindingKey).HasMaxLength(64);
            e.HasIndex(x => new { x.PatientId, x.OriginFindingKey });
        });
        modelBuilder.Entity<GuidanceEventRow>(e =>
        {
            e.ToTable("guidance_status_event");
            e.HasKey(x => x.Id);
            e.HasOne<GuidanceMessageRow>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MessageId);
        });
        SnakeCase.Apply(modelBuilder);
    }
}

public sealed class GuidanceDbContextFactory : IDesignTimeDbContextFactory<GuidanceDbContext>
{
    public GuidanceDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new GuidanceDbContext(new DbContextOptionsBuilder<GuidanceDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", GuidanceDbContext.Schema)).Options);
    }
}
