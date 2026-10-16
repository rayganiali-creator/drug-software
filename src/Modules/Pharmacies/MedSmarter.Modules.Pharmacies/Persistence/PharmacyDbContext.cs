using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Pharmacies.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.Pharmacies.Persistence;

/// <summary>A dispensing error or near miss. The original-report columns are written once; only status, activity time and version change afterwards. No patient identifier is stored.</summary>
public sealed class SafetyEventRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ReporterUserId { get; set; }
    public PharmacyActorRole ReporterRole { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public string? ClientRequestId { get; set; }
    public SafetyEventKind Kind { get; set; }
    public ReachedPatient ReachedPatient { get; set; }
    public string ErrorExamplesJson { get; set; } = "[]";
    public string Description { get; set; } = string.Empty;
    public DateOnly? OccurredOn { get; set; }
    public string? MedicineAsReported { get; set; }
    public string? HowDetectedOrIntercepted { get; set; }
    public string? ImmediateActionTaken { get; set; }
    public Guid? LinkedAdverseEventId { get; set; }
    public Guid? LinkedProductConcernId { get; set; }
    public SafetyStatus Status { get; set; } = SafetyStatus.Submitted;
    public DateTimeOffset LastActivityAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>A suspected product concern. Identifiers are either copied from a linked ProductTrace record (a snapshot) or kept exactly as typed; they are never chosen for the reporter.</summary>
public sealed class ProductConcernRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ReporterUserId { get; set; }
    public PharmacyActorRole ReporterRole { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public string? ClientRequestId { get; set; }
    public ProductConcernType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly? ObservedOn { get; set; }
    public ProductLinkState LinkState { get; set; }
    /// <summary>Pseudonymous patient id of the linked product record. Never shown in a response; kept only so the link can be traced by an auditor.</summary>
    public Guid? LinkedSubjectId { get; set; }
    public Guid? LinkedProductRecordId { get; set; }
    public int? LinkedRecordVersion { get; set; }
    public string? LinkedVerificationAtLinking { get; set; }
    public string? LinkedProductName { get; set; }
    public string? LinkedBatchNumber { get; set; }
    public DateOnly? LinkedExpiryDate { get; set; }
    public string? LinkedManufacturerName { get; set; }
    public string? LinkedGtin { get; set; }
    public string? AsReportedProductName { get; set; }
    public string? AsReportedBatchNumber { get; set; }
    public DateOnly? AsReportedExpiryDate { get; set; }
    public string? AsReportedManufacturerName { get; set; }
    public string? AsReportedGtin { get; set; }
    public Guid? LinkedAdverseEventId { get; set; }
    public Guid? LinkedSafetyEventId { get; set; }
    public bool ManufacturerReportProposed { get; set; }
    public DateTimeOffset? ManufacturerReportProposedAt { get; set; }
    public SafetyStatus Status { get; set; } = SafetyStatus.Submitted;
    public DateTimeOffset LastActivityAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class SafetyEntryRow
{
    public Guid Id { get; set; }
    public SafetyRecordKind RecordKind { get; set; }
    public Guid RecordId { get; set; }
    public SafetyEntryKind Kind { get; set; }
    public Guid AuthorUserId { get; set; }
    public PharmacyActorRole AuthorRole { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Text { get; set; }
    public SafetyStatus? FromStatus { get; set; }
    public SafetyStatus? ToStatus { get; set; }
}

/// <summary>PostgreSQL model of internal pharmacy safety records (schema <c>pharmacy</c>).</summary>
public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : DbContext(options)
{
    public const string Schema = "pharmacy";

    public DbSet<SafetyEventRow> Events => Set<SafetyEventRow>();
    public DbSet<ProductConcernRow> Concerns => Set<ProductConcernRow>();
    public DbSet<SafetyEntryRow> Entries => Set<SafetyEntryRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SafetyEventRow>(e =>
        {
            e.ToTable("safety_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientRequestId).HasMaxLength(64);
            e.Property(x => x.ErrorExamplesJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.MedicineAsReported).HasMaxLength(200);
            e.Property(x => x.HowDetectedOrIntercepted).HasMaxLength(1000);
            e.Property(x => x.ImmediateActionTaken).HasMaxLength(1000);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.ReportedAt });
            e.HasIndex(x => new { x.OrganizationId, x.ReporterUserId, x.ClientRequestId }).IsUnique().HasFilter("client_request_id IS NOT NULL");
        });
        modelBuilder.Entity<ProductConcernRow>(e =>
        {
            e.ToTable("product_concern");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientRequestId).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.LinkedVerificationAtLinking).HasMaxLength(40);
            foreach (var n in new[] { nameof(ProductConcernRow.LinkedProductName), nameof(ProductConcernRow.LinkedManufacturerName), nameof(ProductConcernRow.AsReportedProductName), nameof(ProductConcernRow.AsReportedManufacturerName) })
            {
                e.Property(n).HasMaxLength(200);
            }

            foreach (var n in new[] { nameof(ProductConcernRow.LinkedBatchNumber), nameof(ProductConcernRow.AsReportedBatchNumber), nameof(ProductConcernRow.LinkedGtin), nameof(ProductConcernRow.AsReportedGtin) })
            {
                e.Property(n).HasMaxLength(64);
            }

            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.ReportedAt });
            e.HasIndex(x => new { x.OrganizationId, x.ReporterUserId, x.ClientRequestId }).IsUnique().HasFilter("client_request_id IS NOT NULL");
        });
        modelBuilder.Entity<SafetyEntryRow>(e =>
        {
            e.ToTable("safety_entry");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.RecordKind, x.RecordId, x.At });
        });
        SnakeCase.Apply(modelBuilder);
    }
}

public sealed class PharmacyDbContextFactory : IDesignTimeDbContextFactory<PharmacyDbContext>
{
    public PharmacyDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new PharmacyDbContext(new DbContextOptionsBuilder<PharmacyDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", PharmacyDbContext.Schema)).Options);
    }
}
