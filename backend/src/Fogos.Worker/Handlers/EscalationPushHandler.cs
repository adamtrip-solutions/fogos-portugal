using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// On <see cref="IncidentEscalating"/>, sends a district-topic FCM push when the fire has mobilised at
/// least <c>EscalationPushMinAssets</c> means — mirroring the big-incident push in
/// <see cref="IncidentHistoryHandler"/> (direct <see cref="FcmNotifier"/>, district topics). Claims the
/// push once per incident so at-least-once redelivery can't double-send. Re-fetches the incident first.
/// </summary>
public sealed class EscalationPushHandler(
    MongoContext mongo,
    FcmNotifier fcm,
    IProcessedMarker processed,
    IOptions<SignalsOptions> options)
    : IEventHandler<IncidentEscalating>
{
    public async Task HandleAsync(IncidentEscalating evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);
        if (incident is null)
            return;

        var assets = incident.Resources.TotalAssets;
        if (assets < options.Value.EscalationPushMinAssets)
            return;

        // At-most-once: one escalation push per incident, surviving redelivery.
        if (!await processed.TryMarkAsync($"escalationpush:{incident.Id}", ct))
            return;

        var data = new Dictionary<string, string>
        {
            ["click_action"] = "FLUTTER_NOTIFICATION_CLICK",
            ["kind"] = "escalation",
            ["fireId"] = incident.Id,
        };

        await fcm.SendNotificationAsync(
            SignalsCopy.EscalationPushTitle(incident.Concelho),
            SignalsCopy.EscalationPushBody(incident.Location, assets),
            fcm.Topics.NewFire(incident.Dico, incident.District),
            data, ct);
    }
}
