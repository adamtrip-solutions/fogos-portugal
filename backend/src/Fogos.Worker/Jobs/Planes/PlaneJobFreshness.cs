using Fogos.Domain.Time;
using Fogos.Infrastructure.Ops;
using StackExchange.Redis;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// Per-job freshness bookkeeping for the plane pollers, backed by Redis
/// (<c>fogos:freshness:{job}</c> holds the last-success epoch). Emits a single ops notice — not one
/// per aircraft, not one per run — when a job has gone stale (no success in ≥ 2× its cadence) or when
/// it repeatedly no-ops (e.g. an empty fleet). Duplication with the weather/risk agents' equivalents
/// is accepted this wave; this copy is private to the plane folder.
/// </summary>
public sealed class PlaneJobFreshness(IConnectionMultiplexer redis, IClock clock, IOpsNotifier ops)
{
    /// <summary>How long a one-shot notice suppresses repeats of the same tag for the same job.</summary>
    public static readonly TimeSpan NoticeWindow = TimeSpan.FromMinutes(30);

    private static string SuccessKey(string job) => $"fogos:freshness:{job}";

    private static string NoticeKey(string job, string tag) => $"fogos:freshness:{job}:{tag}";

    /// <summary>Records a successful run and clears the stale one-shot so a future lapse re-alerts.</summary>
    public async Task MarkSuccessAsync(string job, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(SuccessKey(job), clock.UtcNow.ToUnixTimeSeconds());
        await db.KeyDeleteAsync(NoticeKey(job, "stale"));
    }

    /// <summary>
    /// If the last success is older than <c>2×</c> <paramref name="cadence"/>, emit one stale notice
    /// (suppressed for <see cref="NoticeWindow"/>). A job that has never succeeded is not "stale" yet.
    /// </summary>
    public async Task CheckStaleAsync(string job, TimeSpan cadence, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var last = await db.StringGetAsync(SuccessKey(job));
        if (!last.HasValue)
            return;

        var lastAt = DateTimeOffset.FromUnixTimeSeconds((long)last);
        if (clock.UtcNow - lastAt <= cadence * 2)
            return;

        await NoteOnceAsync(
            job,
            "stale",
            $"Plane job '{job}' has not recorded a success since {lastAt:u} (> {cadence.TotalMinutes * 2:0} min).",
            ct);
    }

    /// <summary>Emit an ops info at most once per <see cref="NoticeWindow"/> for the given tag.</summary>
    public async Task NoteOnceAsync(string job, string tag, string message, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var first = await db.StringSetAsync(NoticeKey(job, tag), "1", NoticeWindow, When.NotExists);
        if (first)
            await ops.InfoAsync(message, ct);
    }
}
