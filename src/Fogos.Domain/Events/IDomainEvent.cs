namespace Fogos.Domain.Events;

/// <summary>
/// Marker for everything the ingestion pipeline raises. Events carry ids plus the minimal
/// delta needed to route them — handlers re-fetch the incident before acting (the legacy
/// platform learned the hard way that fat events go stale between dispatch and handling).
/// </summary>
/// <remarks>
/// <see cref="EventId"/> is assigned at construction; <see cref="OccurredAt"/> is stamped by
/// the dispatcher at enqueue time (so it reflects when the event actually left the producer).
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Stable identity for idempotency guards and dead-letter correlation.</summary>
    Guid EventId { get; }

    /// <summary>Enqueue timestamp, set by the dispatcher.</summary>
    DateTimeOffset OccurredAt { get; set; }
}
