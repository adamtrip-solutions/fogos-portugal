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
/// subscription (deduped by <see cref="AlertEventStore"/>'s unique key). Concelho subscriptions match on
/// DICO; point subscriptions match by haversine distance. Idempotent under at-least-once redelivery: the
/// dedupe insert collapses a redelivered event. On the single winning insert per subscription it hands the
/// alert to <see cref="AlertDeliveryService"/> for push (exactly-once); delivery never fails the handler.
/// </summary>
public sealed class AlertMatchHandler(
    MongoContext mongo,
    AlertReads alerts,
    AlertEventStore events,
    AlertDeliveryService delivery)
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
            ct);
    }

    private async Task MatchAsync(
        Incident incident, string kind, string dedupeKey, string message, CancellationToken ct)
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

        // Push only for winning inserts (exactly-once), with the same copy the events carry — collected
        // first, then dispatched as one batch (one dry-run capture per event; bounded parallelism live).
        var url = $"/?incident={incident.Id}";
        var won = new List<AlertDelivery>();
        foreach (var sub in matched.DistinctBy(s => s.Id))
        {
            if (await events.TryAppendAsync(sub.Id, kind, incident.Id, message, dedupeKey, ct))
                won.Add(new AlertDelivery(sub, dedupeKey, kind, message, url));
        }

        if (won.Count > 0)
            await delivery.DeliverManyAsync(won, ct);
    }

    private static string Place(Incident incident) =>
        string.IsNullOrWhiteSpace(incident.Freguesia) ? incident.Concelho : incident.Freguesia!;

    private Task<Incident?> Fetch(string id, CancellationToken ct) =>
        mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;
}
