using Fogos.Infrastructure.Ops;
using StackExchange.Redis;

namespace Fogos.Worker.Jobs.Weather;

/// <summary>
/// Per-job data-freshness bookkeeping shared by the weather jobs. Each job records its last success
/// in Redis (<c>fogos:freshness:{job}</c>) and, at the start of a run, checks whether the last
/// success is older than twice its cadence — if so it escalates to ops <b>once</b> per stale episode
/// (a latch key cleared on the next success), so a wedged feed alerts without spamming every tick.
/// </summary>
public sealed class WeatherFreshnessTracker(IConnectionMultiplexer redis, IOpsNotifier ops)
{
    private static string StampKey(string job) => $"fogos:freshness:{job}";
    private static string AlertedKey(string job) => $"fogos:freshness:{job}:stale-alerted";

    /// <summary>Record a successful run and clear any stale-alert latch.</summary>
    public async Task MarkSuccessAsync(string job, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(StampKey(job), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await db.KeyDeleteAsync(AlertedKey(job));
    }

    /// <summary>Alert once if the last success is older than 2× <paramref name="cadence"/>.</summary>
    public async Task CheckFreshnessAsync(string job, TimeSpan cadence, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var stamp = await db.StringGetAsync(StampKey(job));
        if (!stamp.HasValue)
            return; // never succeeded yet — nothing to compare against.

        var lastSuccess = DateTimeOffset.FromUnixTimeMilliseconds((long)stamp);
        var age = DateTimeOffset.UtcNow - lastSuccess;
        if (age <= cadence * 2)
            return;

        // Latch so we alert once per stale episode; the latch self-expires as a backstop.
        var latched = await db.StringSetAsync(AlertedKey(job), "1", cadence * 4, When.NotExists);
        if (latched)
            await ops.ErrorAsync(
                $"Weather job '{job}' data is stale: last success {age.TotalHours:F1}h ago (cadence {cadence}, threshold 2×).",
                ct);
    }
}
