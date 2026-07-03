using System.Security.Cryptography;
using System.Text;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Ops;
using StackExchange.Redis;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Ports the <c>history.json</c> freshness idea from ProcessOcorrenciasSite: hash the feed content each
/// run; when the hash stays unchanged for longer than the stale window, fire a single "feed stale" ops
/// alert (latched), and a recovery alert the moment it changes again. State lives in Redis.
/// </summary>
public sealed class IncidentFeedFreshness(IConnectionMultiplexer redis, IOpsNotifier ops, IClock clock)
{
    private const string HashKey = "fogos:incidents:feed:hash";
    private const string SinceKey = "fogos:incidents:feed:since";
    private const string AlertLatch = "fogos:incidents:feed:stale-alerted";

    /// <summary>Stable content hash over the ordered (id, status) pairs of a feed snapshot.</summary>
    public static string HashOf(IEnumerable<(string Id, string Status)> entries)
    {
        var joined = string.Join("|", entries.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => $"{e.Id}:{e.Status}"));
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    public async Task TrackAsync(string currentHash, TimeSpan staleAfter, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var now = clock.UtcNow.ToUnixTimeSeconds();
        var previous = await db.StringGetAsync(HashKey);

        if (!previous.HasValue || previous != currentHash)
        {
            await db.StringSetAsync(HashKey, currentHash);
            await db.StringSetAsync(SinceKey, now);

            // If we had alerted about staleness, announce the recovery and clear the latch.
            if (await db.KeyDeleteAsync(AlertLatch))
                await ops.InfoAsync("✅ Feed de incidentes voltou a atualizar.", ct);
            return;
        }

        var since = await db.StringGetAsync(SinceKey);
        if (!since.HasValue)
        {
            await db.StringSetAsync(SinceKey, now);
            return;
        }

        var age = clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds((long)since);
        if (age > staleAfter && await db.StringSetAsync(AlertLatch, "1", when: When.NotExists))
            await ops.InfoAsync($"⏰ Feed de incidentes sem atualizar há {age.TotalMinutes:F0} min.", ct);
    }
}
