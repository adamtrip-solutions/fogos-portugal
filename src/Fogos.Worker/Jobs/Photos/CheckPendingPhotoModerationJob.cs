using Fogos.Domain.Photos;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;
using StackExchange.Redis;

namespace Fogos.Worker.Jobs.Photos;

/// <summary>
/// Every 15 min: if any photos await moderation, raise a single ops Info with the count, then hold a
/// 2-hour Redis cooldown so a persistent backlog doesn't spam ops each run. Port of
/// <c>CheckPendingPhotoModeration.php</c> (whose default cadence-cooldown is honoured here as 2h).
/// </summary>
[DisallowConcurrentExecution]
public sealed class CheckPendingPhotoModerationJob(
    MongoContext mongo,
    IConnectionMultiplexer redis,
    IOpsNotifier ops,
    ILogger<CheckPendingPhotoModerationJob> logger) : IJob
{
    public const string CooldownKey = "fogos:photo-moderation:cooldown";
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(2);
    public static readonly TimeSpan Cadence = TimeSpan.FromMinutes(15);

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var count = await mongo.IncidentPhotos.CountDocumentsAsync(
            Builders<IncidentPhoto>.Filter.Eq(x => x.Status, ModerationStatus.Pending),
            cancellationToken: ct);

        if (count == 0)
            return;

        // Claim the cooldown window: SET key NX EX 2h. If another run already holds it, stay quiet.
        bool claimed;
        try
        {
            claimed = await redis.GetDatabase().StringSetAsync(CooldownKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Cooldown, When.NotExists);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pending-moderation cooldown check failed; skipping notice");
            return;
        }

        if (!claimed)
            return;

        await ops.InfoAsync($"📸 Há {count} foto(s) à espera de moderação.", ct);
    }
}
