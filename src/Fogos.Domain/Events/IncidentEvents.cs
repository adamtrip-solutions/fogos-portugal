using Fogos.Domain.Incidents;

namespace Fogos.Domain.Events;

/// <summary>A new incident appeared in the feed for the first time.</summary>
public sealed record IncidentCreated(string IncidentId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>An incident's committed means changed (new POSIT / COS). Carries the before/after snapshot.</summary>
public sealed record IncidentResourcesChanged(string IncidentId, Resources Previous, Resources Current) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>An incident's status transitioned. Both the numeric code and human label of each side travel with it.</summary>
public sealed record IncidentStatusChanged(
    string IncidentId,
    int PreviousCode,
    string PreviousLabel,
    int CurrentCode,
    string CurrentLabel) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// ICNF enrichment produced first-seen data for an incident. The three flags drive the
/// "first cause / first KML / first burn area" social posts (Wave 3).
/// </summary>
public sealed record IcnfEnriched(string IncidentId, bool FirstCause, bool FirstKml, bool FirstBurnArea) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
