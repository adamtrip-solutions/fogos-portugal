using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports the observer's notification fan-out for every new incident (year ≥ 2022):
/// <c>sendNearbyNotification</c> (data-only proximity topic; the app computes distance locally) and
/// <c>sendNewIncidentNotification</c> (district "all incidents" push, 3-min delayed). Aero-medical
/// occurrences (naturezaCode 2409) also raise a Discord ops info alert. Re-fetches the incident first.
/// </summary>
public sealed class NewIncidentNotificationsHandler(
    MongoContext mongo,
    IClock clock,
    FcmNotifier fcm,
    NotificationScheduler scheduler,
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

        var isFire = incident.Kind == IncidentKind.Fire;

        // Nearby proximity (data-only, no user location leaves the device).
        await fcm.SendDataOnlyAsync(fcm.Topics.Nearby(), new Dictionary<string, string>
        {
            ["type"] = "nearby",
            ["fireId"] = incident.Id,
            ["lat"] = (incident.Coordinates?.Latitude ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["lng"] = (incident.Coordinates?.Longitude ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["location"] = incident.Location,
            ["nature"] = incident.Natureza,
            ["isFire"] = isFire ? "1" : "0",
        }, ct);

        // District "all incidents" push (3-min delayed).
        var nature = string.IsNullOrEmpty(incident.Natureza) ? "" : $" — {incident.Natureza}";
        var body = isFire ? $"Novo incêndio em {incident.Location}" : $"Nova ocorrência em {incident.Location}{nature}";
        await scheduler.ScheduleAsync("new-incident", incident.Id, incident.Location, body,
            fcm.Topics.AllIncidents(incident.Dico).ToArray(), ct: ct);

        if (incident.NaturezaCode == NaturezaCatalog.AeroAlertCode)
            await ops.InfoAsync($"🚨 Novo acidente aereo em {incident.Location} 🚨", ct);
    }
}
