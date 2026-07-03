using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Fogos.Infrastructure.Queue;

/// <inheritdoc />
public sealed class RedisProcessedMarker(IConnectionMultiplexer redis, IOptions<QueueOptions> options) : IProcessedMarker
{
    private IDatabase Db => redis.GetDatabase();

    public async Task<bool> TryMarkAsync(string key, CancellationToken ct = default) =>
        await Db.StringSetAsync(
            QueueKeys.Processed(key),
            "1",
            expiry: options.Value.ProcessedMarkerTtl,
            when: When.NotExists);
}
