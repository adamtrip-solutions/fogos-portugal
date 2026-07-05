namespace Fogos.Infrastructure.RateLimiting;

/// <summary>
/// Caps concurrent subscriptions per caller partition. Anonymous (cap 0) is always rejected.
/// Backed by a Redis counter incremented when a subscription starts and decremented when it
/// completes; a safety TTL bounds counter leaks from unclean disconnects.
/// </summary>
public sealed class SubscriptionLimiter(RedisCounters counters)
{
    private const int SafetyTtlSeconds = 3600;

    /// <summary>True when the tier may open at least one subscription.</summary>
    public static bool Allowed(int cap) => cap > 0;

    /// <summary>
    /// Tries to reserve a subscription slot. Returns false when the cap is exhausted (the reserved
    /// slot is rolled back). Fails open when Redis is unreachable, except for cap 0 which is always false.
    /// </summary>
    public async Task<bool> TryAcquireAsync(string partitionKey, int cap)
    {
        if (cap <= 0)
            return false;

        var value = await counters.IncrementAsync($"rl:sub:{partitionKey}", SafetyTtlSeconds);
        if (value is null)
            return true; // Redis down → fail open (cap 0 already handled above).

        if (value > cap)
        {
            await counters.DecrementAsync($"rl:sub:{partitionKey}");
            return false;
        }

        return true;
    }

    /// <summary>Releases a previously reserved subscription slot.</summary>
    public Task ReleaseAsync(string partitionKey) =>
        counters.DecrementAsync($"rl:sub:{partitionKey}");
}
