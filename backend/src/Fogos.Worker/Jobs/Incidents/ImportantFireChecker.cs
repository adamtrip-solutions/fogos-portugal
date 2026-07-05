using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Ports <c>CheckImportantFireIncident</c> (ShouldBeUnique): active fires, statusCode 1–6, not yet
/// flagged, aerial+terrain &gt; 15, older than 3h → mark <c>important</c> and fire the "incêndio
/// importante" push once. The persisted <c>Important</c> flag is the idempotency guard (the candidate
/// query excludes already-flagged fires), set before the push so redelivery can't double-send. Guarded
/// by a Redis single-flight lock (the ShouldBeUnique twin).
/// </summary>
public sealed class ImportantFireChecker(
    MongoContext mongo,
    ISingleFlightLock locks,
    IClock clock,
    NotificationScheduler scheduler,
    FcmNotifier fcm,
    ILogger<ImportantFireChecker> logger)
{
    private const string LockKey = "check-important";

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var token = await locks.TryAcquireAsync(LockKey, TimeSpan.FromMinutes(5), ct);
        if (token is null)
        {
            logger.LogInformation("CheckImportant skipped: another run holds the lock.");
            return 0;
        }

        try
        {
            return await RunCoreAsync(ct);
        }
        finally
        {
            await locks.ReleaseAsync(LockKey, token, CancellationToken.None);
        }
    }

    private async Task<int> RunCoreAsync(CancellationToken ct)
    {
        var f = Builders<Incident>.Filter;
        var candidates = await mongo.Incidents
            .Find(f.Eq(x => x.Active, true) & f.Eq(x => x.Kind, IncidentKind.Fire) & f.Eq(x => x.Important, false))
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var pushed = 0;

        foreach (var incident in candidates)
        {
            if (ct.IsCancellationRequested)
                break;
            if (!IncidentRules.QualifiesAsImportant(incident, now))
                continue;

            // Mark first (once-only: the candidate query excludes flagged fires), then push.
            await mongo.Incidents.UpdateOneAsync(f.Eq(x => x.Id, incident.Id),
                Builders<Incident>.Update.Set(x => x.Important, true), cancellationToken: ct);

            await scheduler.ScheduleAsync("important", incident.Id, incident.Location,
                IncidentCopy.ImportantPush(incident),
                fcm.Topics.Incident(incident.Id, includeImportant: true).ToArray(), ct: ct);
            pushed++;
        }

        logger.LogInformation("CheckImportant: {Pushed} important fire pushes.", pushed);
        return pushed;
    }
}
