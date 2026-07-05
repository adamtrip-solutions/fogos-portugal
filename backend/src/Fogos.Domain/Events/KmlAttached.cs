namespace Fogos.Domain.Events;

/// <summary>
/// An operator attached a KML perimeter to an incident (via the <c>attachKml</c> mutation).
/// <paramref name="Vost"/> records which slot received it (VOST-curated variant vs the ANEPC slot).
/// </summary>
public sealed record KmlAttached(string IncidentId, bool Vost) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
