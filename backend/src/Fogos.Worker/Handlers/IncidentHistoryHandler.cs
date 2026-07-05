using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports <c>SaveIncidentHistory</c>: on creation and every resources change (year ≥ 2022), appends an
/// <c>incident_history</c> snapshot. Re-fetches the incident before acting.
/// </summary>
public sealed class IncidentHistoryHandler(
    MongoContext mongo,
    IClock clock)
    : IEventHandler<IncidentCreated>, IEventHandler<IncidentResourcesChanged>
{
    public Task HandleAsync(IncidentCreated evt, CancellationToken ct) => ProcessAsync(evt.IncidentId, ct);

    public Task HandleAsync(IncidentResourcesChanged evt, CancellationToken ct) => ProcessAsync(evt.IncidentId, ct);

    private async Task ProcessAsync(string incidentId, CancellationToken ct)
    {
        var incident = await Fetch(incidentId, ct);
        if (incident is null || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        await mongo.IncidentHistory.InsertOneAsync(new IncidentHistorySnapshot
        {
            IncidentId = incident.Id,
            At = clock.UtcNow,
            Man = incident.Resources.Man,
            Terrain = incident.Resources.Terrain,
            Aerial = incident.Resources.Aerial,
            Location = incident.Location,
        }, cancellationToken: ct);
    }

    private Task<Incident?> Fetch(string id, CancellationToken ct) =>
        mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;
}
