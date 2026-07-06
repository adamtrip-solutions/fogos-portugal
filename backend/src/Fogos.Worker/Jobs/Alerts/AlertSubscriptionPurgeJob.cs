using Fogos.Domain.Alerts;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Alerts;

/// <summary>
/// Daily purge of stale alert subscriptions: those created more than <c>PurgeAfterDays</c> ago with no
/// activity in the same window (LastSeenAt null or older than the cutoff). Single-flight.
/// </summary>
public sealed class AlertSubscriptionPurgeJob(
    ISingleFlightLock lockService,
    ILogger<AlertSubscriptionPurgeJob> logger,
    MongoContext mongo,
    IClock clock,
    IOptions<AlertOptions> options) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(options.Value.PurgeAfterDays);
        var f = Builders<AlertSubscription>.Filter;
        // Only anonymous (unowned) subscriptions auto-purge. $eq:null also matches documents missing the
        // field, so legacy anonymous docs still purge; account-owned subscriptions are kept indefinitely.
        var filter = f.Lt(x => x.CreatedAt, cutoff)
                     & f.Eq(x => x.OwnerUserId, null)
                     & f.Or(f.Eq(x => x.LastSeenAt, null), f.Lt(x => x.LastSeenAt, cutoff));

        var result = await mongo.AlertSubscriptions.DeleteManyAsync(filter, ct);
        if (result.DeletedCount > 0)
            logger.LogInformation("Purged {Count} stale alert subscriptions.", result.DeletedCount);
    }
}
