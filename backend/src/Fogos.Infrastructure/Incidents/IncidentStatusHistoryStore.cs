using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Incidents;

/// <summary>
/// The single write path for the <c>incident_status_history</c> timeline. Appends one observation row
/// (incident id + current status + timestamp), gated by the same year-≥ <see cref="IncidentRules.HistoryMinYear"/>
/// guard the legacy <c>SaveIncidentStatusHistory</c> used. Reused by the witnessed-transition handler and by
/// <see cref="Ingest.IncidentIngestService"/>, which seeds the initial observation directly on insert (no event).
/// </summary>
public sealed class IncidentStatusHistoryStore(MongoContext mongo, IClock clock)
{
    /// <summary>
    /// Appends the incident's current status as an observation stamped at <paramref name="at"/>
    /// (defaults to now). No-op for pre-<see cref="IncidentRules.HistoryMinYear"/> incidents.
    /// </summary>
    public async Task AppendAsync(Incident incident, DateTimeOffset? at = null, CancellationToken ct = default)
    {
        if (clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        await mongo.IncidentStatusHistory.InsertOneAsync(new IncidentStatusChange
        {
            IncidentId = incident.Id,
            At = at ?? clock.UtcNow,
            Code = incident.Status.Code,
            Label = incident.Status.Label,
        }, cancellationToken: ct);
    }
}
