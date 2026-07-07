using Fogos.Domain.Devices;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Alerts;

/// <summary>
/// Daily purge of dead Web Push devices: those already disabled, or unseen for <c>PurgeAfterDays</c>
/// (LastSeenAt older than the cutoff). Each removal cascades through <see cref="DeviceStore"/> — dropping the
/// device's anonymous subscriptions and clearing <c>DeviceId</c> on owned ones. Single-flight.
/// </summary>
public sealed class DevicePurgeJob(
    ISingleFlightLock lockService,
    ILogger<DevicePurgeJob> logger,
    MongoContext mongo,
    DeviceStore deviceStore,
    IClock clock,
    IOptions<WebPushOptions> options) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(options.Value.PurgeAfterDays);
        var f = Builders<Device>.Filter;
        var filter = f.Or(f.Eq(x => x.Disabled, true), f.Lt(x => x.LastSeenAt, cutoff));

        var stale = await mongo.Devices.Find(filter).Project(x => x.Id).ToListAsync(ct);
        foreach (var id in stale)
            await deviceStore.DeleteWithCascadeAsync(id, ct);

        if (stale.Count > 0)
            logger.LogInformation("Purged {Count} stale Web Push devices.", stale.Count);
    }
}
