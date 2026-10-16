using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ADR.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.ADR.Persistence;

/// <summary>One internal adverse-event record. The original-report columns are written once, at creation, and never updated.</summary>
public sealed class AdverseEventRow
{
    public Guid Id { get; set; }
    /// <summary>Pseudonymous subject id (the patient's user id). No foreign key across module schemas.</summary>
    public Guid SubjectId { get; set; }
    public ReporterRole ReporterRole { get; set; }
    public Guid ReporterUserId { get; set; }
    public ReportOrigin Origin { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public string? ClientRequestId { get; set; }

    // ---- original report (immutable) ----
    public string EventDescription { get; set; } = string.Empty;
    public DateTimeOffset? OnsetAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? OutcomeAsStated { get; set; }
    public string? AdditionalDetails { get; set; }
    public string SuspectedMedicationsJson { get; set; } = "[]";
    /// <summary>Phase 9 (additive): dose/route as reported, and other medicines named. Part of the original report: written once.</summary>
    public string? DoseAndAdministrationAsReported { get; set; }
    public string ConcomitantMedicationsJson { get; set; } = "[]";

    // ---- current review state (the only mutable part) ----
    public EventCategory Category { get; set; } = EventCategory.Unclassified;
    public ReviewStatus Status { get; set; } = ReviewStatus.Submitted;
    public DateTimeOffset LastActivityAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>Append-only history: follow-ups, review notes, assessments, supplements, classification and status changes.</summary>
public sealed class AdverseEventEntryRow
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public EntryKind Kind { get; set; }
    public Guid AuthorUserId { get; set; }
    public ReporterRole AuthorRole { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Text { get; set; }
    public EntryVisibility Visibility { get; set; }
    public ReviewStatus? FromStatus { get; set; }
    public ReviewStatus? ToStatus { get; set; }
    public EventCategory? FromCategory { get; set; }
    public EventCategory? ToCategory { get; set; }
    public DateTimeOffset? SupplementOnsetAt { get; set; }
    public DateTimeOffset? SupplementResolvedAt { get; set; }
    public string? SupplementOutcomeAsStated { get; set; }
}

/// <summary>PostgreSQL model of internal adverse-event records (schema <c>adr</c>).</summary>
public sealed class AdrDbContext(DbContextOptions<AdrDbContext> options) : DbContext(options)
{
    public const string Schema = "adr";

    public DbSet<AdverseEventRow> Events => Set<AdverseEventRow>();
    public DbSet<AdverseEventEntryRow> Entries => Set<AdverseEventEntryRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AdverseEventRow>(e =>
        {
            e.ToTable("adverse_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientRequestId).HasMaxLength(64);
            e.Property(x => x.EventDescription).HasMaxLength(2000).IsRequired();
            e.Property(x => x.OutcomeAsStated).HasMaxLength(500);
            e.Property(x => x.AdditionalDetails).HasMaxLength(2000);
            e.Property(x => x.SuspectedMedicationsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.DoseAndAdministrationAsReported).HasMaxLength(500);
            e.Property(x => x.ConcomitantMedicationsJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SubjectId, x.ReportedAt });
            e.HasIndex(x => new { x.Status, x.LastActivityAt });
            e.HasIndex(x => new { x.SubjectId, x.ReporterUserId, x.ClientRequestId }).IsUnique().HasFilter("client_request_id IS NOT NULL");
        });
        modelBuilder.Entity<AdverseEventEntryRow>(e =>
        {
            e.ToTable("adverse_event_entry");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.Property(x => x.SupplementOutcomeAsStated).HasMaxLength(500);
            e.HasOne<AdverseEventRow>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EventId, x.At });
        });
        SnakeCase.Apply(modelBuilder);
    }
}

public sealed class AdrDbContextFactory : IDesignTimeDbContextFactory<AdrDbContext>
{
    public AdrDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new AdrDbContext(new DbContextOptionsBuilder<AdrDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", AdrDbContext.Schema)).Options);
    }
}
