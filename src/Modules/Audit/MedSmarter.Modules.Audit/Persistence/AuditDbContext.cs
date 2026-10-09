using System.Text.Json;
using MedSmarter.Modules.Audit.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.Audit.Persistence;

/// <summary>Storage row of one audit entry. The hash chain (<see cref="PreviousHash"/> -> <see cref="Hash"/>) is computed by <see cref="AuditService"/>.</summary>
public sealed class AuditRow
{
    public long Sequence { get; set; }
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public Guid? SubjectUserId { get; set; }
    public int Result { get; set; }
    public string Source { get; set; } = "api";
    public string? CorrelationId { get; set; }
    public string? ReasonCode { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    public AuditEntry ToEntry() => new(Id, Sequence, Timestamp, ActorUserId, Action, ResourceType, ResourceId, SubjectUserId, (AuditResult)Result, Source, CorrelationId, ReasonCode,
        JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson) ?? [], Hash);
}

/// <summary>
/// PostgreSQL model of the audit log (schema <c>audit</c>). The table is append-only: database triggers reject UPDATE, DELETE and TRUNCATE.
/// Production should additionally connect with a role that only has INSERT and SELECT (docs/phase5/10-persistence-and-migrations.md).
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditRow> Entries => Set<AuditRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditRow>(e =>
        {
            e.ToTable("audit_entry", t =>
            {
                t.HasCheckConstraint("ck_audit_entry_sequence_positive", "sequence >= 1");
                t.HasCheckConstraint("ck_audit_entry_result", "result IN (0, 1, 2)");
            });
            e.HasKey(x => x.Sequence).HasName("pk_audit_entry");
            e.Property(x => x.Sequence).HasColumnName("sequence").ValueGeneratedNever();
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
            e.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(200);
            e.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(200);
            e.Property(x => x.SubjectUserId).HasColumnName("subject_user_id");
            e.Property(x => x.Result).HasColumnName("result");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(200).IsRequired();
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(200);
            e.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(200);
            e.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.PreviousHash).HasColumnName("previous_hash").HasMaxLength(64).IsRequired();
            e.Property(x => x.Hash).HasColumnName("hash").HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Id).IsUnique().HasDatabaseName("ux_audit_entry_id");
            e.HasIndex(x => x.Hash).IsUnique().HasDatabaseName("ux_audit_entry_hash");
            e.HasIndex(x => new { x.SubjectUserId, x.Sequence }).HasDatabaseName("ix_audit_entry_subject_user_id_sequence").IsDescending(false, true);
            e.HasIndex(x => new { x.ActorUserId, x.Sequence }).HasDatabaseName("ix_audit_entry_actor_user_id_sequence").IsDescending(false, true);
            e.HasIndex(x => new { x.Action, x.Sequence }).HasDatabaseName("ix_audit_entry_action_sequence").IsDescending(false, true);
        });
    }
}

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema)).Options);
    }
}
