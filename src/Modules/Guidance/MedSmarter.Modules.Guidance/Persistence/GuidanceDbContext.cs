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
    public string? OriginSubjectsFingerprint { get; set; }
}

/// <summary>Phase 8: how a notification for a message is going. One row per message and channel; holds ids, states and times only (no message text).</summary>
public sealed class GuidanceDeliveryRow
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid PatientId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public DeliveryMode Mode { get; set; }
    public DeliveryState State { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public int Attempts { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    /// <summary>Phase 14 (additive): optimistic claim token. Every state change increments it, so two instances can never both claim the same due delivery.</summary>
    public int Version { get; set; }
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
    /// <summary>Partial unique index: at most one open (not Resolved) message per (patient, finding).</summary>
    public const string OpenFindingIndex = "ux_guidance_message_open_finding";

    public const string Schema = "guidance";

    public DbSet<GuidanceMessageRow> Messages => Set<GuidanceMessageRow>();
    public DbSet<GuidanceEventRow> Events => Set<GuidanceEventRow>();
    public DbSet<GuidanceDeliveryRow> Deliveries => Set<GuidanceDeliveryRow>();

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
            e.Property(x => x.OriginSubjectsFingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.PatientId, x.OriginFindingKey }).HasDatabaseName("ix_guidance_message_patient_id_origin_finding_key"); // lookup of every message of a finding, resolved or not
            // At most one OPEN message per finding of a patient (a resolved one may be followed by a new one when the data changed). Status is stored as its name (string conversion above).
            e.HasIndex(["PatientId", "OriginFindingKey"], OpenFindingIndex).IsUnique().HasFilter("origin_finding_key IS NOT NULL AND status <> 'Resolved'");
        });
        modelBuilder.Entity<GuidanceEventRow>(e =>
        {
            e.ToTable("guidance_status_event");
            e.HasKey(x => x.Id);
            e.HasOne<GuidanceMessageRow>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MessageId);
        });
        modelBuilder.Entity<GuidanceDeliveryRow>(e =>
        {
            e.ToTable("guidance_delivery");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasMaxLength(32).IsRequired();
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<GuidanceMessageRow>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.MessageId, x.Channel }).IsUnique();
            e.HasIndex(x => new { x.State, x.NextAttemptAt });
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
