namespace Fogos.Infrastructure.RateLimiting;

/// <summary>Decision for a single request against its per-window quota.</summary>
public readonly record struct RateDecision(bool Allowed, int Limit, long Count, int RetryAfterSeconds)
{
    public static RateDecision FailOpen(int limit) => new(true, limit, 0, 0);
}

/// <summary>Per-caller request-rate limiter (fixed windows, Redis-backed, fail-open).</summary>
public sealed class RequestRateLimiter(RedisCounters counters)
{
    /// <summary>Counts one request against <paramref name="partitionKey"/>; caps at <paramref name="limit"/> per window.</summary>
    public async Task<RateDecision> AcquireAsync(string partitionKey, int limit, int windowSeconds)
    {
        var hit = await counters.HitAsync($"rl:req:{partitionKey}", 1, windowSeconds);
        if (hit is null)
            return RateDecision.FailOpen(limit); // Redis down → allow.

        var count = (long)hit.Value.Total;
        return count > limit
            ? new RateDecision(false, limit, count, hit.Value.RetryAfterSeconds)
            : new RateDecision(true, limit, count, hit.Value.RetryAfterSeconds);
    }
}
