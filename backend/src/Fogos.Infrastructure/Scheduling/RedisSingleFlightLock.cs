using StackExchange.Redis;

namespace Fogos.Infrastructure.Scheduling;

/// <inheritdoc />
public sealed class RedisSingleFlightLock(IConnectionMultiplexer redis) : ISingleFlightLock
{
    // Release only when we still own the lock (compare-and-delete), avoiding freeing someone else's.
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end
        """;

    private static string Key(string key) => $"fogos:lock:{key}";

    public async Task<string?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var acquired = await redis.GetDatabase().StringSetAsync(Key(key), token, ttl, When.NotExists);
        return acquired ? token : null;
    }

    public async Task ReleaseAsync(string key, string token, CancellationToken ct = default)
    {
        await redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [Key(key)], [token]);
    }
}
