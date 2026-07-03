namespace Fogos.Domain.Events;

/// <summary>A citizen submitted a photo for an incident (moderation pipeline, Phase 4).</summary>
public sealed record PhotoSubmitted(string PhotoId, string IncidentId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
