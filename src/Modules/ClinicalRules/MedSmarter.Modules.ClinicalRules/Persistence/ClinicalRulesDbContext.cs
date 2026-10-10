using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ClinicalRules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.ClinicalRules.Persistence;

/// <summary>One immutable-content rule version plus its life cycle. The definition is stored as JSON; the columns next to it are what queries and policy need.</summary>
public sealed class RuleRow
{
    public string RuleId { get; set; } = string.Empty;
    public int Version { get; set; }
    public RuleStatus Status { get; set; }
    public bool IsDemo { get; set; }
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset AuthoredAt { get; set; }
    public string? Reviewer { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public int RowVersion { get; set; } = 1;
}

/// <summary>Append-only history of what happened to a rule version (who, when, from which status to which).</summary>
public sealed class RuleEventRow
{
    public Guid Id { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Action { get; set; } = string.Empty;
    public RuleStatus FromStatus { get; set; }
    public RuleStatus ToStatus { get; set; }
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

public sealed class AssessmentRow
{
    public Guid Id { get; set; }

    /// <summary>Internal patient id (no foreign key across module schemas).</summary>
    public Guid PatientId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public bool RequestedByPatient { get; set; }
    public string Trigger { get; set; } = "manual";
    public DateTimeOffset CreatedAt { get; set; }
    public AssessmentStatus Status { get; set; }
    public bool Complete { get; set; }
    public string RuleSetVersion { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public DateTimeOffset DataAsOf { get; set; }
    public int FindingCount { get; set; }
    public int ActionableFindingCount { get; set; }
    public bool ContainsDemonstration { get; set; }
    public GuidanceLinkState GuidanceState { get; set; }
    public string ResultJson { get; set; } = "{}";
    public string GuidanceLinksJson { get; set; } = "[]";
}

/// <summary>PostgreSQL model of rule versions, their history and stored assessments (schema <c>clinical_rules</c>).</summary>
public sealed class ClinicalRulesDbContext(DbContextOptions<ClinicalRulesDbContext> options) : DbContext(options)
{
    public const string Schema = "clinical_rules";

    public DbSet<RuleRow> Rules => Set<RuleRow>();
    public DbSet<RuleEventRow> RuleEvents => Set<RuleEventRow>();
    public DbSet<AssessmentRow> Assessments => Set<AssessmentRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<RuleRow>(e =>
        {
            e.ToTable("rule");
            e.HasKey(x => new { x.RuleId, x.Version });
            e.Property(x => x.RuleId).HasMaxLength(64);
            e.Property(x => x.Author).HasMaxLength(64).IsRequired();
            e.Property(x => x.Reviewer).HasMaxLength(64);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasIndex(x => x.Status);
        });
        modelBuilder.Entity<RuleEventRow>(e =>
        {
            e.ToTable("rule_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.RuleId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.RuleId, x.Version });
        });
        modelBuilder.Entity<AssessmentRow>(e =>
        {
            e.ToTable("assessment");
            e.HasKey(x => x.Id);
            e.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
            e.Property(x => x.RuleSetVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.EngineVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.GuidanceLinksJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
        });
        SnakeCase.Apply(modelBuilder);
    }
}

public sealed class ClinicalRulesDbContextFactory : IDesignTimeDbContextFactory<ClinicalRulesDbContext>
{
    public ClinicalRulesDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new ClinicalRulesDbContext(new DbContextOptionsBuilder<ClinicalRulesDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", ClinicalRulesDbContext.Schema)).Options);
    }
}
