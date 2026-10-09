using MedSmarter.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.ProductTrace.Persistence;

/// <summary>PostgreSQL model of batch/lot records and manufacturer safety reports (schema <c>product_trace</c>).</summary>
public sealed class ProductTraceDbContext(DbContextOptions<ProductTraceDbContext> options) : DbContext(options)
{
    public const string Schema = "product_trace";

    public DbSet<ProductRecordRow> Products => Set<ProductRecordRow>();
    public DbSet<ProductVersionRow> ProductVersions => Set<ProductVersionRow>();
    public DbSet<ReportRow> Reports => Set<ReportRow>();
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();
    public DbSet<ReportEventRow> ReportEvents => Set<ReportEventRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProductRecordRow>(e =>
        {
            e.ToTable("dispensed_product_record", t =>
            {
                t.HasCheckConstraint("ck_dispensed_product_record_identity", "medication_id IS NOT NULL OR length(product_name) > 0");
                t.HasCheckConstraint("ck_dispensed_product_record_batch", "length(batch_number) BETWEEN 1 AND 40");
                t.HasCheckConstraint("ck_dispensed_product_record_dates", "manufacture_date IS NULL OR manufacture_date < expiry_date");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.GenericName).HasMaxLength(200);
            e.Property(x => x.ManufacturerName).HasMaxLength(200);
            e.Property(x => x.BatchNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.BatchNormalized).HasMaxLength(40).IsRequired();
            e.Property(x => x.ProductKey).HasMaxLength(220).IsRequired();
            e.Property(x => x.Gtin).HasMaxLength(14);
            e.Property(x => x.PharmacyNote).HasMaxLength(200);
            e.Property(x => x.FindingsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.PatientId, x.DeletedAt });
            e.HasIndex(x => x.BatchNormalized); // "which patients hold batch X": the lookup a recall would need
            e.HasIndex(x => new { x.PatientId, x.BatchNormalized, x.ProductKey, x.ExpiryDate }).IsUnique().HasFilter("deleted_at IS NULL");
        });

        modelBuilder.Entity<ProductVersionRow>(e =>
        {
            e.ToTable("dispensed_product_record_version");
            e.HasKey(x => x.Id);
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.HasOne<ProductRecordRow>().WithMany().HasForeignKey(x => x.ProductRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProductRecordId, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<ReportRow>(e =>
        {
            e.ToTable("manufacturer_safety_report", t => t.HasCheckConstraint("ck_manufacturer_safety_report_duration", "duration_of_use_days IS NULL OR duration_of_use_days BETWEEN 0 AND 36500"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(24).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.ReviewerNote).HasMaxLength(500);
            e.Property(x => x.FailureCode).HasMaxLength(64);
            e.Property(x => x.ExternalReference).HasMaxLength(100);
            e.Property(x => x.PayloadJson).HasColumnType("text"); // text, not jsonb: the integrity hash covers these exact characters, and jsonb would re-format them
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.Property(x => x.ClientRequestId).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<ProductRecordRow>().WithMany().HasForeignKey(x => x.ProductRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.PatientId, x.ClientRequestId }).IsUnique().HasFilter("client_request_id IS NOT NULL"); // a retried create returns the same report
        });

        modelBuilder.Entity<OutboxRow>(e =>
        {
            e.ToTable("manufacturer_report_outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.LastErrorCode).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<ReportRow>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.State, x.NextAttemptAt });
        });

        modelBuilder.Entity<ReportEventRow>(e =>
        {
            e.ToTable("manufacturer_report_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne<ReportRow>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ReportId);
        });

        SnakeCase.Apply(modelBuilder);
    }
}

public sealed class ProductTraceDbContextFactory : IDesignTimeDbContextFactory<ProductTraceDbContext>
{
    public ProductTraceDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new ProductTraceDbContext(new DbContextOptionsBuilder<ProductTraceDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", ProductTraceDbContext.Schema)).Options);
    }
}
