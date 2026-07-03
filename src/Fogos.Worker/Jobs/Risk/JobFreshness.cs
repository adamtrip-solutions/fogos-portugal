using Fogos.Domain.Time;
using Fogos.Infrastructure.Ops;
using StackExchange.Redis;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Per-job freshness bookkeeping in Redis (<c>fogos:freshness:{job}</c> = last success unix seconds)
/// plus a one-shot staleness alert when the last success is older than twice the job's cadence. The
/// alert latch is cleared on the next success so a recovered job can alert again if it lapses later.
/// (The weather pipeline carries an identical private helper — folder-local duplication is accepted
/// this wave; a shared extraction can follow.)
/// </summary>
public sealed class JobFreshness(IConnectionMultiplexer redis, IOpsNotifier ops, IClock clock)
{
    private static string Key(string job) => $"fogos:freshness:{job}";
    private static string AlertLatch(string job) => $"fogos:freshness:{job}:stale-alerted";

    /// <summary>Records a successful run and clears the staleness alert latch.</summary>
    public async Task MarkSuccessAsync(string job, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(job), clock.UtcNow.ToUnixTimeSeconds());
        await db.KeyDeleteAsync(AlertLatch(job));
    }

    /// <summary>Fires a single Info alert (until the next success) when the last success is older than 2× cadence.</summary>
    public async Task CheckStaleAsync(string job, TimeSpan cadence, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var last = await db.StringGetAsync(Key(job));
        if (!last.HasValue)
            return; // never succeeded yet — nothing to compare against.

        var age = clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds((long)last);
        if (age <= cadence * 2)
            return;

        // Latch with SET NX so only the first observer of the staleness alerts.
        if (await db.StringSetAsync(AlertLatch(job), "1", when: When.NotExists))
            await ops.InfoAsync(
                $"⏰ Job '{job}' stale: last success {age.TotalMinutes:F0} min ago (cadence {cadence.TotalMinutes:F0} min).",
                ct);
    }
}
