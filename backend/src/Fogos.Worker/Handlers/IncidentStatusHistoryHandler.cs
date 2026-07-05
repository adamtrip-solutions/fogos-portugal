using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports the persistence side of <c>SaveIncidentStatusHistory</c>: appends an
/// <c>incident_status_history</c> row on every status change (year ≥ 2022). Re-fetches the incident first.
/// </summary>
public sealed class IncidentStatusHistoryHandler(
    MongoContext mongo,
    IClock clock)
    : IEventHandler<IncidentStatusChanged>
{
    public async Task HandleAsync(IncidentStatusChanged evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        await mongo.IncidentStatusHistory.InsertOneAsync(new IncidentStatusChange
        {
            IncidentId = incident.Id,
            At = clock.UtcNow,
            Code = incident.Status.Code,
            Label = incident.Status.Label,
        }, cancellationToken: ct);
    }
}
