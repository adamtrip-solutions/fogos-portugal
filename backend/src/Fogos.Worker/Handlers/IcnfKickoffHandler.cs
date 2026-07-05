using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports the observer's <c>dispatch(new ProcessICNFFireData($incident))</c>: for a new fire (year ≥ 2022)
/// enqueues ICNF enrichment onto the dedicated <c>icnf</c> stream, keeping the slow, TLS-relaxed ICNF
/// scraping off the hot default queue.
/// </summary>
public sealed class IcnfKickoffHandler(MongoContext mongo, IClock clock, IEventDispatcher dispatcher)
    : IEventHandler<IncidentCreated>
{
    public const string Stream = "icnf";

    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null
            || incident.Kind != IncidentKind.Fire
            || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        await dispatcher.DispatchAsync(new ProcessIcnfFireData(incident.Id, incident.Icnf?.IcnfId ?? incident.Id), Stream, ct);
    }
}
