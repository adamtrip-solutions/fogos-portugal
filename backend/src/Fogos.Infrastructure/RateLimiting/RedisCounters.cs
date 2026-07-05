using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Fogos.Infrastructure.RateLimiting;

/// <summary>Outcome of a fixed-window counter hit: the running total and seconds to window end.</summary>
public readonly record struct CounterHit(double Total, int RetryAfterSeconds);

/// <summary>
/// Fixed-window counters in Redis (INCR + EXPIRE-if-none). The single point that talks to
/// Redis for the request-rate, cost-budget, and subscription limiters. On any Redis failure
/// it <b>fails open</b> (returns <c>null</c>) so availability wins over enforcement, counts
/// consecutive failures, and raises a single ops alert once a run of failures is observed.
/// </summary>
public sealed class RedisCounters(
    IConnectionMultiplexer redis,
    IOpsNotifier ops,
    ILogger<RedisCounters> logger)
{
    private const int AlertThreshold = 5;
    private int _consecutiveFailures;
    private int _alerted;

    /// <summary>
    /// Adds <paramref name="amount"/> to the window counter at <paramref name="key"/>, creating
    /// the window (and its expiry) on first hit. Returns <c>null</c> when Redis is unreachable.
    /// </summary>
    public async Task<CounterHit?> HitAsync(string key, double amount, int windowSeconds)
    {
        try
        {
            var db = redis.GetDatabase();
            var total = await db.StringIncrementAsync(key, amount);
            // Set the expiry only when the key has none — a fixed (not sliding) window.
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds), ExpireWhen.HasNoExpiry);
            var ttl = await db.KeyTimeToLiveAsync(key);
            OnSuccess();
            var retryAfter = (int)Math.Ceiling((ttl ?? TimeSpan.FromSeconds(windowSeconds)).TotalSeconds);
            return new CounterHit(total, Math.Max(1, retryAfter));
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            return null;
        }
    }

    /// <summary>Increments an integer counter (subscriptions); returns <c>null</c> on Redis failure.</summary>
    public async Task<long?> IncrementAsync(string key, int safetyTtlSeconds)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringIncrementAsync(key);
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(safetyTtlSeconds), ExpireWhen.HasNoExpiry);
            OnSuccess();
            return value;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            return null;
        }
    }

    /// <summary>Decrements an integer counter, flooring at zero. Best-effort (swallows failures).</summary>
    public async Task DecrementAsync(string key)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringDecrementAsync(key);
            if (value < 0)
                await db.StringSetAsync(key, 0);
            OnSuccess();
        }
        catch (Exception ex)
        {
            OnFailure(ex);
        }
    }

    private void OnSuccess() => Interlocked.Exchange(ref _consecutiveFailures, 0);

    private void OnFailure(Exception ex)
    {
        var n = Interlocked.Increment(ref _consecutiveFailures);
        logger.LogWarning(ex, "Redis limiter counter failed (consecutive #{Count}); failing open", n);
        if (n >= AlertThreshold && Interlocked.Exchange(ref _alerted, 1) == 0)
            _ = ops.ErrorAsync($"Rate limiter Redis unavailable ({n} consecutive failures) — failing open.");
    }
}
