using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;

namespace MedSmarter.Modules.Consent.Persistence;

/// <summary>PostgreSQL model of consents and their append-only history (schema <c>consent</c>).</summary>
public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options) : DbContext(options)
{
    public const string Schema = "consent";

    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<ConsentEventRecord> Events => Set<ConsentEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<ConsentRecord>(e =>
        {
            e.ToTable("consent", t =>
            {
                t.HasCheckConstraint("ck_consent_one_grantee_at_most", "NOT (grantee_user_id IS NOT NULL AND grantee_organization_id IS NOT NULL)");
                t.HasCheckConstraint("ck_consent_expires_after_grant", "expires_at > granted_at");
                t.HasCheckConstraint("ck_consent_scope_not_empty", "cardinality(scope) > 0");
            });
            e.HasKey(x => x.Id).HasName("pk_consent");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.SubjectUserId).HasColumnName("subject_user_id");
            e.Property(x => x.GranteeUserId).HasColumnName("grantee_user_id");
            e.Property(x => x.GranteeOrganizationId).HasColumnName("grantee_organization_id");
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(32).IsRequired();
            e.Property(x => x.Scope).HasColumnName("scope").HasColumnType("text[]")
                .HasConversion(v => v.ToArray(), v => v.ToList(),
                    new ValueComparer<IReadOnlyList<string>>((a, b) => a!.SequenceEqual(b!), v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)), v => v.ToList()));
            e.Property(x => x.GrantedAt).HasColumnName("granted_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.Property(x => x.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.SubjectUserId, x.GrantedAt }).HasDatabaseName("ix_consent_subject_user_id_granted_at");
            e.HasIndex(x => x.GranteeUserId).HasDatabaseName("ix_consent_grantee_user_id");
            e.HasIndex(x => x.GranteeOrganizationId).HasDatabaseName("ix_consent_grantee_organization_id");
        });
        modelBuilder.Entity<ConsentEventRecord>(e =>
        {
            e.ToTable("consent_event");
            e.HasKey(x => x.Id).HasName("pk_consent_event");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ConsentId).HasColumnName("consent_id");
            e.Property(x => x.SubjectUserId).HasColumnName("subject_user_id");
            e.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.At).HasColumnName("at");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(32).IsRequired();
            e.HasOne<ConsentRecord>().WithMany().HasForeignKey(x => x.ConsentId).HasConstraintName("fk_consent_event_consent_id").OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SubjectUserId, x.At }).HasDatabaseName("ix_consent_event_subject_user_id_at");
            e.HasIndex(x => new { x.ConsentId, x.Kind }).IsUnique().HasDatabaseName("ux_consent_event_consent_id_kind"); // one grant and at most one revocation per consent
        });
    }
}

public sealed class ConsentDbContextFactory : IDesignTimeDbContextFactory<ConsentDbContext>
{
    public ConsentDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "Host=localhost;Database=design_time_only;Username=design;Password=design";
        return new ConsentDbContext(new DbContextOptionsBuilder<ConsentDbContext>().UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history", ConsentDbContext.Schema)).Options);
    }
}
