namespace MedSmarter.BuildingBlocks;

/// <summary>
/// Transactional outbox row (Phase 0, §2 of 02-architecture). Written in the same transaction as the
/// state change; a later phase adds the publisher (Kafka). Payload must never contain unnecessary PHI.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Idempotency record for consumers (Inbox pattern).</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
