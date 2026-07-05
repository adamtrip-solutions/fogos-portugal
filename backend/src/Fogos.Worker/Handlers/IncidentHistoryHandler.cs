using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports <c>SaveIncidentHistory</c>: on creation and every resources change (year ≥ 2022), appends an
/// <c>incident_history</c> snapshot, then applies the big-incident rule — fire + man ≥ 100 + not yet
/// pushed → "Ocorrência Importante" push to the important topic, claimed once per incident via
/// <see cref="IProcessedMarker"/> (the same at-most-once guard <see cref="EscalationPushHandler"/> uses).
/// Re-fetches the incident before acting.
/// </summary>
public sealed class IncidentHistoryHandler(
    MongoContext mongo,
    IClock clock,
    FcmNotifier fcm,
    IProcessedMarker processed)
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

        await MaybePushBigAsync(incident, ct);
    }

    private async Task MaybePushBigAsync(Incident incident, CancellationToken ct)
    {
        if (!IncidentRules.QualifiesAsBig(incident))
            return;

        // At-most-once: one big-incident push per incident, surviving redelivery and multiple workers.
        if (!await processed.TryMarkAsync($"bigincident:{incident.Id}", ct))
            return;

        await fcm.SendNotificationAsync("Ocorrência Importante", IncidentCopy.BigPush(incident),
            fcm.Topics.Important(), ct: ct);
    }

    private Task<Incident?> Fetch(string id, CancellationToken ct) =>
        mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;
}
