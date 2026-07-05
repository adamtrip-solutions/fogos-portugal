using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Ports <c>CheckImportantFireIncident</c> (ShouldBeUnique): active fires, statusCode 1–6, not yet
/// flagged, aerial+terrain &gt; 15, older than 3h → mark <c>important</c>. The persisted <c>Important</c>
/// flag is the idempotency guard (the candidate query excludes already-flagged fires). Guarded by a Redis
/// single-flight lock (the ShouldBeUnique twin).
/// </summary>
public sealed class ImportantFireChecker(
    MongoContext mongo,
    ISingleFlightLock locks,
    IClock clock,
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
        var flagged = 0;

        foreach (var incident in candidates)
        {
            if (ct.IsCancellationRequested)
                break;
            if (!IncidentRules.QualifiesAsImportant(incident, now))
                continue;

            // Once-only: the candidate query excludes flagged fires, so the Set is the idempotency guard.
            await mongo.Incidents.UpdateOneAsync(f.Eq(x => x.Id, incident.Id),
                Builders<Incident>.Update.Set(x => x.Important, true), cancellationToken: ct);
            flagged++;
        }

        logger.LogInformation("CheckImportant: {Flagged} fires flagged important.", flagged);
        return flagged;
    }
}
