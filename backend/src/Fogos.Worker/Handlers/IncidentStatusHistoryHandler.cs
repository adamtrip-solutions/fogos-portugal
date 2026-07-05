using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports the persistence + push side of <c>SaveIncidentStatusHistory</c>: appends an
/// <c>incident_status_history</c> row and schedules the status-change push (3-minute delay) for the
/// incident topic. Re-fetches the incident before acting.
/// </summary>
public sealed class IncidentStatusHistoryHandler(
    MongoContext mongo,
    IClock clock,
    NotificationScheduler scheduler,
    FcmNotifier fcm)
    : IEventHandler<IncidentStatusChanged>
{
    public async Task HandleAsync(IncidentStatusChanged evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        // Always: status-change push (delayed 3 min via the scheduler).
        await scheduler.ScheduleAsync(
            "status-change", incident.Id, incident.Location,
            IncidentCopy.StatusPush(evt.PreviousLabel, evt.CurrentLabel),
            fcm.Topics.Incident(incident.Id, includeImportant: false).ToArray(), ct: ct);

        await mongo.IncidentStatusHistory.InsertOneAsync(new IncidentStatusChange
        {
            IncidentId = incident.Id,
            At = clock.UtcNow,
            Code = incident.Status.Code,
            Label = incident.Status.Label,
        }, cancellationToken: ct);
    }
}
