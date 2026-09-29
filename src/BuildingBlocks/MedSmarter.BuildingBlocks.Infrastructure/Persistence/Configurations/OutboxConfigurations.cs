using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedSmarter.BuildingBlocks.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Type).HasColumnName("type").HasMaxLength(256).IsRequired();
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        b.Property(x => x.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);
        // Publisher polls unprocessed rows in order.
        b.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_outbox_messages_unprocessed")
            .HasFilter("processed_at IS NULL");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("inbox_messages");
        b.HasKey(x => new { x.MessageId, x.Consumer });
        b.Property(x => x.MessageId).HasColumnName("message_id");
        b.Property(x => x.Consumer).HasColumnName("consumer").HasMaxLength(200);
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at").IsRequired();
    }
}
