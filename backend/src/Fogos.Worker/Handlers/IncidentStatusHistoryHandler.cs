using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports the persistence side of <c>SaveIncidentStatusHistory</c>: appends an
/// <c>incident_status_history</c> row on every witnessed status change. Re-fetches the incident first,
/// then defers to the shared <see cref="IncidentStatusHistoryStore"/> (the same write path the ingest
/// service uses to seed the initial observation).
/// </summary>
public sealed class IncidentStatusHistoryHandler(
    MongoContext mongo,
    IncidentStatusHistoryStore store)
    : IEventHandler<IncidentStatusChanged>
{
    public async Task HandleAsync(IncidentStatusChanged evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null)
            return;

        await store.AppendAsync(incident, ct: ct);
    }
}
