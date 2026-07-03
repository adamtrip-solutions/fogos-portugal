namespace Fogos.Domain.Events;

/// <summary>
/// Requests ICNF enrichment for an incident (the legacy <c>ProcessICNFFireData</c> job, which ran on
/// its own <c>icnf</c> queue). Dispatched by the new-fire kickoff handler and by the age-bucketed
/// re-scrape job. The handler re-fetches the incident, fetches ICNF XML detail, merges the
/// <c>icnf</c> sub-document + KML perimeter, and raises <see cref="IcnfEnriched"/> on first-seen data.
/// </summary>
public sealed record ProcessIcnfFireData(string IncidentId, string? IcnfId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
