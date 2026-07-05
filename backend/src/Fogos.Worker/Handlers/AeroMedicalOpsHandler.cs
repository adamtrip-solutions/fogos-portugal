using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Raises a Discord ops info alert for every new aero-medical occurrence (naturezaCode 2409, year ≥ 2022).
/// Claims the fan-out once per incident via <see cref="IProcessedMarker"/> so at-least-once redelivery of
/// <see cref="IncidentCreated"/> can't double-alert. Re-fetches the incident first.
/// </summary>
public sealed class AeroMedicalOpsHandler(
    MongoContext mongo,
    IClock clock,
    IProcessedMarker processed,
    IOpsNotifier ops)
    : IEventHandler<IncidentCreated>
{
    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        if (incident.NaturezaCode != NaturezaCatalog.AeroAlertCode)
            return;

        // Idempotency: claim the fan-out once so a redelivered IncidentCreated can't re-alert.
        if (!await processed.TryMarkAsync($"aeromedical:{incident.Id}", ct))
            return;

        await ops.InfoAsync($"🚨 Novo acidente aereo em {incident.Location} 🚨", ct);
    }
}
