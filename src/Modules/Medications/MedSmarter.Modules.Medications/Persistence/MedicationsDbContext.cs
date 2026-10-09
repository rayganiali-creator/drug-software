using System.Text.RegularExpressions;
using MedSmarter.Modules.Medications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.Medications.Persistence;

/// <summary>
/// PostgreSQL model of the medication knowledge core (schema <c>medications</c>). Table/column/constraint names are snake_case.
/// Enums are stored as text so rows stay readable and new values do not shift numbers. See docs/phase4/02-data-model.md.
/// </summary>
public sealed partial class MedicationsDbContext(DbContextOptions<MedicationsDbContext> options) : DbContext(options)
{
    public const string Schema = "medications";

    public DbSet<ReferenceTerm> ReferenceTerms => Set<ReferenceTerm>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ActiveIngredient> ActiveIngredients => Set<ActiveIngredient>();
    public DbSet<IngredientSynonym> IngredientSynonyms => Set<IngredientSynonym>();
    public DbSet<KnowledgeSource> KnowledgeSources => Set<KnowledgeSource>();
    public DbSet<KnowledgeRevision> KnowledgeRevisions => Set<KnowledgeRevision>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<MedicationIngredient> MedicationIngredients => Set<MedicationIngredient>();
    public DbSet<MedicationRoute> MedicationRoutes => Set<MedicationRoute>();
    public DbSet<MedicationClassification> MedicationClassifications => Set<MedicationClassification>();
    public DbSet<MedicationSynonym> MedicationSynonyms => Set<MedicationSynonym>();
    public DbSet<MedicationIdentifier> MedicationIdentifiers => Set<MedicationIdentifier>();
    public DbSet<MedicationStatement> MedicationStatements => Set<MedicationStatement>();
    public DbSet<DrugInteraction> DrugInteractions => Set<DrugInteraction>();
    public DbSet<MedicationVersion> MedicationVersions => Set<MedicationVersion>();
    public DbSet<MedicationSearchTerm> MedicationSearchTerms => Set<MedicationSearchTerm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("pg_trgm"); // substring search on the normalized names

        modelBuilder.Entity<ReferenceTerm>(e =>
        {
            e.ToTable("reference_term");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameFa).HasMaxLength(200);
            e.HasIndex(x => new { x.Kind, x.Code }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_reference_term_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL"));
        });

        modelBuilder.Entity<Manufacturer>(e =>
        {
            e.ToTable("manufacturer");
            e.HasKey(x => x.Id);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameFa).HasMaxLength(200);
            e.Property(x => x.Country).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.ManufacturerCode).HasMaxLength(64);
            e.HasIndex(x => x.ManufacturerCode).IsUnique(); // NULLs are distinct in PostgreSQL: unknown codes never collide
            e.ToTable(t => t.HasCheckConstraint("ck_manufacturer_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL"));
        });

        modelBuilder.Entity<Brand>(e =>
        {
            e.ToTable("brand");
            e.HasKey(x => x.Id);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameFa).HasMaxLength(200);
            e.HasOne<Manufacturer>().WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_brand_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL"));
        });

        modelBuilder.Entity<ActiveIngredient>(e =>
        {
            e.ToTable("active_ingredient");
            e.HasKey(x => x.Id);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameFa).HasMaxLength(200);
            e.Property(x => x.AtcCode).HasMaxLength(7);
            e.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Validation).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(x => x.AtcCode);
            e.HasMany(x => x.Synonyms).WithOne().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_active_ingredient_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL"));
        });

        modelBuilder.Entity<IngredientSynonym>(e =>
        {
            e.ToTable("ingredient_synonym");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.IngredientId);
        });

        modelBuilder.Entity<KnowledgeSource>(e =>
        {
            e.ToTable("knowledge_source");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Publisher).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Version).HasMaxLength(64).IsRequired();
            e.Property(x => x.LicenseName).HasMaxLength(200).IsRequired();
            e.Property(x => x.UsageRestrictions).HasMaxLength(1000);
            e.Property(x => x.Validation).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(x => new { x.Name, x.Version }).IsUnique();
        });

        modelBuilder.Entity<KnowledgeRevision>(e =>
        {
            e.ToTable("knowledge_revision");
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne<KnowledgeSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SourceId, x.Label }).IsUnique();
        });

        modelBuilder.Entity<Medication>(e =>
        {
            e.ToTable("medication");
            e.HasKey(x => x.Id);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameFa).HasMaxLength(200);
            e.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Validation).HasConversion<string>().HasMaxLength(24);
            e.HasOne<Brand>().WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Manufacturer>().WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ReferenceTerm>().WithMany().HasForeignKey(x => x.DosageFormId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Routes).WithOne().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Classifications).WithOne().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Synonyms).WithOne().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Identifiers).WithOne().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Statements).WithOne().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Lifecycle, x.Validation });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_medication_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL");
                t.HasCheckConstraint("ck_medication_version_positive", "version >= 1");
                // fictional records can never be marked verified
                t.HasCheckConstraint("ck_medication_demo_not_validated", "NOT (is_demo AND validation = 'Validated')");
            });
        });

        modelBuilder.Entity<MedicationIngredient>(e =>
        {
            e.ToTable("medication_ingredient");
            e.HasKey(x => new { x.MedicationId, x.IngredientId });
            e.Property(x => x.StrengthValue).HasPrecision(18, 4);
            e.Property(x => x.StrengthUnit).HasMaxLength(16);
            e.Property(x => x.PerUnit).HasMaxLength(32);
            e.HasOne<ActiveIngredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.IngredientId);
            e.ToTable(t => t.HasCheckConstraint("ck_medication_ingredient_strength", "(strength_value IS NULL AND strength_unit IS NULL) OR (strength_value > 0 AND strength_unit IS NOT NULL)"));
        });

        modelBuilder.Entity<MedicationRoute>(e =>
        {
            e.ToTable("medication_route");
            e.HasKey(x => new { x.MedicationId, x.TermId });
            e.HasOne<ReferenceTerm>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MedicationClassification>(e =>
        {
            e.ToTable("medication_classification");
            e.HasKey(x => new { x.MedicationId, x.TermId });
            e.HasOne<ReferenceTerm>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MedicationSynonym>(e =>
        {
            e.ToTable("medication_synonym");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.MedicationId);
        });

        modelBuilder.Entity<MedicationIdentifier>(e =>
        {
            e.ToTable("medication_identifier");
            e.HasKey(x => x.Id);
            e.Property(x => x.Scheme).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Value).HasMaxLength(64).IsRequired();
            e.HasOne<KnowledgeRevision>().WithMany().HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Restrict); // provenance is mandatory
            e.HasIndex(x => new { x.Scheme, x.Value }).IsUnique();
            e.HasIndex(x => x.MedicationId);
        });

        modelBuilder.Entity<MedicationStatement>(e =>
        {
            e.ToTable("medication_statement");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.TextEn).HasMaxLength(4000);
            e.Property(x => x.TextFa).HasMaxLength(4000);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.Frequency).HasMaxLength(16);
            e.Property(x => x.Population).HasMaxLength(200);
            e.HasOne<KnowledgeRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict); // no source, no statement
            e.HasIndex(x => new { x.MedicationId, x.Kind });
            e.ToTable(t => t.HasCheckConstraint("ck_medication_statement_has_text", "text_en IS NOT NULL OR text_fa IS NOT NULL"));
        });

        modelBuilder.Entity<DrugInteraction>(e =>
        {
            e.ToTable("drug_interaction");
            e.HasKey(x => x.Id);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.MechanismEn).HasMaxLength(4000);
            e.Property(x => x.MechanismFa).HasMaxLength(4000);
            e.Property(x => x.ManagementEn).HasMaxLength(4000);
            e.Property(x => x.ManagementFa).HasMaxLength(4000);
            e.HasOne<ActiveIngredient>().WithMany().HasForeignKey(x => x.IngredientAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActiveIngredient>().WithMany().HasForeignKey(x => x.IngredientBId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<KnowledgeRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IngredientAId, x.IngredientBId }).IsUnique();
            e.HasIndex(x => x.IngredientBId);
            e.ToTable(t => t.HasCheckConstraint("ck_drug_interaction_order", "ingredient_a_id < ingredient_b_id")); // (A,B) stored once
        });

        modelBuilder.Entity<MedicationVersion>(e =>
        {
            e.ToTable("medication_version");
            e.HasKey(x => x.Id);
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.ChangeReason).HasMaxLength(300).IsRequired();
            e.HasOne<Medication>().WithMany().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MedicationId, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<MedicationSearchTerm>(e =>
        {
            e.ToTable("medication_search_term");
            e.HasKey(x => x.Id);
            e.Property(x => x.Normalized).HasMaxLength(200).IsRequired();
            e.Property(x => x.Display).HasMaxLength(200).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Medication>().WithMany().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MedicationId);
            e.HasIndex(x => x.Normalized).HasDatabaseName("ix_medication_search_term_trgm").HasMethod("gin").HasOperators("gin_trgm_ops"); // substring: LIKE '%abc%'
        });

        ApplySnakeCase(modelBuilder);
    }

    /// <summary>Column, key, index and constraint names in snake_case (matches the platform schema's convention).</summary>
    private static void ApplySnakeCase(ModelBuilder b)
    {
        foreach (var e in b.Model.GetEntityTypes())
        {
            var table = e.GetTableName()!;
            foreach (var p in e.GetProperties())
            {
                p.SetColumnName(Snake(p.Name));
            }

            foreach (var k in e.GetKeys())
            {
                k.SetName($"pk_{table}");
            }

            foreach (var fk in e.GetForeignKeys())
            {
                fk.SetConstraintName($"fk_{table}_{fk.PrincipalEntityType.GetTableName()}_{string.Join('_', fk.Properties.Select(p => Snake(p.Name)))}");
            }

            foreach (var i in e.GetIndexes().Where(i => i.GetDatabaseName() is null or { Length: 0 } || i.GetDatabaseName()!.StartsWith("IX_", StringComparison.Ordinal)))
            {
                i.SetDatabaseName($"{(i.IsUnique ? "ux" : "ix")}_{table}_{string.Join('_', i.Properties.Select(p => Snake(p.Name)))}");
            }
        }
    }

    private static string Snake(string name) => SnakeRegex().Replace(AcronymRegex().Replace(name, "$1_$2"), "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SnakeRegex();
}

/// <summary>Design-time only (dotnet ef). The dummy default lets migrations be scaffolded offline; no credential in source.</summary>
public sealed class MedicationsDbContextFactory : IDesignTimeDbContextFactory<MedicationsDbContext>
{
    public MedicationsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                 ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        var options = new DbContextOptionsBuilder<MedicationsDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", MedicationsDbContext.Schema))
            .Options;
        return new MedicationsDbContext(options);
    }
}
