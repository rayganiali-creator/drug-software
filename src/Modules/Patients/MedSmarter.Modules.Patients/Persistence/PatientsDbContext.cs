using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.Patients.Persistence;

/// <summary>PostgreSQL model of the patient layer (schema <c>patients</c>). Names are snake_case; enums are stored as text.</summary>
public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options) : DbContext(options)
{
    public const string Schema = "patients";

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ProfileRecord> Profiles => Set<ProfileRecord>();
    public DbSet<ConditionRecord> Conditions => Set<ConditionRecord>();
    public DbSet<AllergyRecord> Allergies => Set<AllergyRecord>();
    public DbSet<MedicationRecord> Medications => Set<MedicationRecord>();
    public DbSet<ScheduleEntryRecord> ScheduleEntries => Set<ScheduleEntryRecord>();
    public DbSet<IntakeLogRecord> IntakeLogs => Set<IntakeLogRecord>();
    public DbSet<SymptomRecord> Symptoms => Set<SymptomRecord>();
    public DbSet<DataCategoryRecord> DataCategories => Set<DataCategoryRecord>();
    public DbSet<RecordVersionRow> RecordVersions => Set<RecordVersionRow>();
    public DbSet<ExternalIdentifierRecord> ExternalIdentifiers => Set<ExternalIdentifierRecord>();
    public DbSet<CareRelationshipRecord> CareRelationships => Set<CareRelationshipRecord>();
    public DbSet<CareRelationshipEventRecord> CareRelationshipEvents => Set<CareRelationshipEventRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Patient>(e =>
        {
            e.ToTable("patient");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique(); // one patient record per account; matching is never by name
        });

        modelBuilder.Entity<ProfileRecord>(e =>
        {
            e.ToTable("patient_profile", t =>
            {
                t.HasCheckConstraint("ck_patient_profile_year_of_birth", "year_of_birth IS NULL OR (year_of_birth BETWEEN 1900 AND 2200)");
                t.HasCheckConstraint("ck_patient_profile_weight", "weight_kg IS NULL OR (weight_kg > 0 AND weight_kg <= 500)");
                t.HasCheckConstraint("ck_patient_profile_height", "height_cm IS NULL OR (height_cm > 0 AND height_cm <= 260)");
            });
            e.HasKey(x => x.PatientId);
            e.Property(x => x.WeightKg).HasPrecision(6, 2);
            e.Property(x => x.HeightCm).HasPrecision(5, 1);
            e.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Patient>().WithOne().HasForeignKey<ProfileRecord>(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConditionRecord>(e =>
        {
            e.ToTable("patient_condition", t => t.HasCheckConstraint("ck_patient_condition_name", "length(name) BETWEEN 1 AND 200"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.DeletedAt });
        });

        modelBuilder.Entity<AllergyRecord>(e =>
        {
            e.ToTable("patient_allergy", t => t.HasCheckConstraint("ck_patient_allergy_medication_kind", "medication_id IS NULL OR kind = 'Medication'"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Substance).HasMaxLength(200).IsRequired();
            e.Property(x => x.Reaction).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.DeletedAt });
        });

        modelBuilder.Entity<MedicationRecord>(e =>
        {
            e.ToTable("patient_medication", t =>
            {
                t.HasCheckConstraint("ck_patient_medication_identity", "medication_id IS NOT NULL OR (unregistered_name IS NOT NULL AND length(unregistered_name) > 0)");
                t.HasCheckConstraint("ck_patient_medication_dose", "(dose_amount IS NULL AND dose_unit IS NULL) OR (dose_amount > 0 AND dose_unit IS NOT NULL)");
                t.HasCheckConstraint("ck_patient_medication_dates", "end_date IS NULL OR end_date >= start_date");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.UnregisteredName).HasMaxLength(120);
            e.Property(x => x.DoseAmount).HasPrecision(12, 4);
            e.Property(x => x.DoseUnit).HasMaxLength(16);
            e.Property(x => x.DoseText).HasMaxLength(100);
            e.Property(x => x.Route).HasMaxLength(32);
            e.Property(x => x.PrescriberNote).HasMaxLength(200);
            e.Property(x => x.StopReason).HasMaxLength(200);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status, x.DeletedAt });
            e.HasIndex(x => x.MedicationId);
        });

        modelBuilder.Entity<ScheduleEntryRecord>(e =>
        {
            e.ToTable("medication_schedule_entry", t => t.HasCheckConstraint("ck_medication_schedule_entry_days", "days_mask BETWEEN 1 AND 127"));
            e.HasKey(x => x.Id);
            e.HasOne<MedicationRecord>().WithMany().HasForeignKey(x => x.PatientMedicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.PatientMedicationId });
        });

        modelBuilder.Entity<IntakeLogRecord>(e =>
        {
            e.ToTable("medication_intake_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne<MedicationRecord>().WithMany().HasForeignKey(x => x.PatientMedicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ScheduleEntryRecord>().WithMany().HasForeignKey(x => x.ScheduleEntryId).OnDelete(DeleteBehavior.Restrict);
            // The same planned dose can be logged once; logging it again updates it (idempotent).
            e.HasIndex(x => new { x.ScheduleEntryId, x.ScheduledFor }).IsUnique().HasFilter("schedule_entry_id IS NOT NULL AND scheduled_for IS NOT NULL");
            e.HasIndex(x => new { x.PatientId, x.RecordedAt });
        });

        modelBuilder.Entity<SymptomRecord>(e =>
        {
            e.ToTable("patient_symptom_report");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.OnsetAt });
        });

        modelBuilder.Entity<DataCategoryRecord>(e =>
        {
            e.ToTable("patient_data_snapshot_meta");
            e.HasKey(x => new { x.PatientId, x.Category });
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecordVersionRow>(e =>
        {
            e.ToTable("patient_record_version");
            e.HasKey(x => x.Id);
            e.Property(x => x.RecordType).HasMaxLength(32).IsRequired();
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.RecordType, x.RecordId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => x.PatientId);
        });

        modelBuilder.Entity<ExternalIdentifierRecord>(e =>
        {
            e.ToTable("external_patient_identifier");
            e.HasKey(x => x.Id);
            e.Property(x => x.ValueHash).HasMaxLength(64).IsRequired();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Scheme, x.ValueHash }).IsUnique(); // one identifier maps to one patient
        });

        modelBuilder.Entity<CareRelationshipRecord>(e =>
        {
            e.ToTable("care_relationship", t =>
            {
                t.HasCheckConstraint("ck_care_relationship_not_self", "patient_user_id <> provider_user_id");
                t.HasCheckConstraint("ck_care_relationship_has_provider", "provider_user_id IS NOT NULL OR provider_organization_id IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
            e.Property(x => x.InitiatedBy).HasMaxLength(16).IsRequired();
            e.Property(x => x.EndReason).HasMaxLength(200);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientUserId, x.Status });
            e.HasIndex(x => new { x.ProviderUserId, x.Status });
            // at most one open (pending or active) relationship per patient, provider and kind
            e.HasIndex(x => new { x.PatientUserId, x.ProviderUserId, x.Kind }).IsUnique().HasFilter("provider_user_id IS NOT NULL AND status IN ('PendingProvider', 'PendingPatient', 'Active')");
            e.HasIndex(x => new { x.PatientUserId, x.ProviderOrganizationId, x.Kind }).IsUnique().HasFilter("provider_organization_id IS NOT NULL AND status IN ('PendingProvider', 'PendingPatient', 'Active')");
        });

        modelBuilder.Entity<CareRelationshipEventRecord>(e =>
        {
            e.ToTable("care_relationship_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne<CareRelationshipRecord>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.RelationshipId);
        });

        SnakeCase.Apply(modelBuilder);
    }
}

public sealed class PatientsDbContextFactory : IDesignTimeDbContextFactory<PatientsDbContext>
{
    public PatientsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new PatientsDbContext(new DbContextOptionsBuilder<PatientsDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", PatientsDbContext.Schema)).Options);
    }
}
