namespace MedSmarter.BuildingBlocks;

/// <summary>Marker for events raised inside a module. Integration events cross module boundaries via the outbox.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
