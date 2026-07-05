using Fogos.Domain.Alerts;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Alerts;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Matches incident-driven events to alert subscriptions and records one <c>alert_event</c> per matched
/// subscription (deduped by <see cref="AlertEventStore"/>'s unique key), then — only when the event is
/// freshly recorded — sends a direct FCM push to any subscription carrying a device token. Concelho
/// subscriptions match on DICO; point subscriptions match by haversine distance. Idempotent under
/// at-least-once redelivery: the dedupe insert gates the push.
/// </summary>
public sealed class AlertMatchHandler(
    MongoContext mongo,
    AlertReads alerts,
    AlertEventStore events,
    FcmNotifier fcm)
    : IEventHandler<IncidentCreated>, IEventHandler<IncidentEscalating>, IEventHandler<RekindleDetected>
{
    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        var incident = await Fetch(evt.IncidentId, ct);
        if (incident is null)
            return;

        await MatchAsync(
            incident,
            AlertEventKind.NewIncident,
            $"inc:{incident.Id}",
            AlertCopy.NewIncident(Place(incident), incident.Natureza),
            AlertCopy.NewIncidentTitle(incident.Concelho),
            ct);
    }

    public async Task HandleAsync(IncidentEscalating evt, CancellationToken ct)
    {
        var incident = await Fetch(evt.IncidentId, ct);
        if (incident is null)
            return;

        await MatchAsync(
            incident,
            AlertEventKind.Escalation,
            $"esc:{incident.Id}",
            AlertCopy.Escalation(incident.Concelho, evt.Assets),
            AlertCopy.EscalationTitle(incident.Concelho),
            ct);
    }

    public async Task HandleAsync(RekindleDetected evt, CancellationToken ct)
    {
        var incident = await Fetch(evt.IncidentId, ct);
        if (incident is null)
            return;

        await MatchAsync(
            incident,
            AlertEventKind.Rekindle,
            $"rek:{incident.Id}",
            AlertCopy.Rekindle(Place(incident), incident.Natureza),
            AlertCopy.RekindleTitle(incident.Concelho),
            ct);
    }

    private async Task MatchAsync(
        Incident incident, string kind, string dedupeKey, string message, string pushTitle, CancellationToken ct)
    {
        var matched = new List<AlertSubscription>();

        if (!string.IsNullOrEmpty(incident.Dico))
            matched.AddRange(await alerts.ConcelhoSubscriptionsByDicoAsync(incident.Dico, ct));

        if (incident.Coordinates is { } point)
        {
            var pointSubs = await alerts.PointSubscriptionsAsync(ct);
            matched.AddRange(pointSubs.Where(s =>
                s.Point is { } p && s.RadiusKm is { } r && point.DistanceKm(p) <= r));
        }

        foreach (var sub in matched.DistinctBy(s => s.Id))
        {
            var recorded = await events.TryAppendAsync(sub.Id, kind, incident.Id, message, dedupeKey, ct);
            if (recorded && !string.IsNullOrEmpty(sub.FcmToken))
                await fcm.SendToTokenAsync(sub.FcmToken!, pushTitle, message, PushData(kind, incident.Id), ct);
        }
    }

    private static string Place(Incident incident) =>
        string.IsNullOrWhiteSpace(incident.Freguesia) ? incident.Concelho : incident.Freguesia!;

    private static Dictionary<string, string> PushData(string kind, string incidentId) => new()
    {
        ["click_action"] = "FLUTTER_NOTIFICATION_CLICK",
        ["kind"] = "alert",
        ["alertKind"] = kind,
        ["fireId"] = incidentId,
    };

    private Task<Incident?> Fetch(string id, CancellationToken ct) =>
        mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;
}
